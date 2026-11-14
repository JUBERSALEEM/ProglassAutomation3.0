// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Database/SqliteResilience.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — SQLite hardening (senior audit — Aug 2026)
//
// WHY THIS FILE EXISTS
// ────────────────────
// Every module's DbContext in this repo used to override OnConfiguring
// with a bare-minimum `options.UseSqlite("Data Source=…")` and nothing
// else. That produced three classes of production bug:
//
//   1. "database is locked" (SQLITE_BUSY, error code 5) whenever two
//      code paths tried to write at the same instant — for example the
//      Inquiry editor's auto-save colliding with the lifecycle-service
//      background hydration. SQLite's default rollback journal is a
//      single-writer, single-reader mode: any concurrent open on the
//      same file fails immediately with error 5.
//
//   2. Silent cascade-delete failures because SQLite's foreign-key
//      enforcement is OFF by default (a legacy compatibility choice).
//      InquiryDbContext declared OnDelete(DeleteBehavior.Cascade) but
//      SQLite never enforced it — deleting an Inquiry would leave
//      orphan InquiryAttachment / InquiryCommunication rows behind
//      unless the repository remembered to RemoveRange() them first.
//
//   3. UNIQUE-violation retry loops that could not actually retry
//      because they reused the same DbContext instance across attempts
//      — the change tracker still held the failed entity and the
//      SqliteConnection was in an ambiguous state.
//
// WHAT THIS FILE DOES
// ───────────────────
// One-stop configuration + retry helper that every DbContext calls from
// OnConfiguring:
//
//     options.UseSqlite(SqliteResilience.BuildConnectionString(path))
//            .AddInterceptors(SqliteResilience.PragmaInterceptor);
//
// Pragmas applied on every new physical connection:
//
//     PRAGMA journal_mode  = WAL      → true multi-reader + single-writer,
//                                        no "database is locked" on read
//                                        during a write.
//     PRAGMA synchronous   = NORMAL   → WAL-safe fsync policy. FULL is
//                                        the SQLite default and is 2-3x
//                                        slower for zero durability win
//                                        in WAL mode.
//     PRAGMA busy_timeout  = 5000     → any statement that hits a lock
//                                        blocks up to 5 s instead of
//                                        throwing immediately. Covers
//                                        checkpoint stalls and short
//                                        writer bursts.
//     PRAGMA foreign_keys  = ON       → cascade / restrict / set-null
//                                        are enforced. Off by default
//                                        for legacy compatibility.
//     PRAGMA temp_store    = MEMORY   → temp b-trees for ORDER BY /
//                                        GROUP BY stay in RAM.
//     PRAGMA mmap_size     = 64 MB    → read-only pages are memory-
//                                        mapped, cutting per-query
//                                        overhead on large tables.
//     PRAGMA cache_size    = -16000   → 16 MB per-connection page cache
//                                        (negative = kB, positive = pages).
//
// Retry helper: SaveChanges wrappers that catch transient SQLITE_BUSY /
// SQLITE_LOCKED (codes 5 / 6) and retry with exponential back-off.
// UNIQUE / NOT-NULL / CHECK / FOREIGN-KEY violations are NOT retried
// — those are programmer errors that should surface immediately.
//
// SAFETY / IDEMPOTENCY
// ────────────────────
// • Pragmas are re-applied on every new physical connection, not once
//   per app start. That is intentional: EF Core opens and closes
//   connections on demand, and each new connection to a SQLite file
//   starts with PRAGMA defaults regardless of what the previous
//   connection set.
//
// • WAL is a file-format setting persisted to the header. Applying it
//   on an existing rollback-journal database is a one-time upgrade and
//   is a no-op afterwards. Applying it on a read-only medium (network
//   share, CD-ROM) is silently ignored — the pragma returns the old
//   mode. We don't fail startup for that.
//
// • busy_timeout is a per-connection setting. It affects only future
//   statements on the same connection.
//
// • foreign_keys is enforced only for statements that follow the
//   PRAGMA. That means cascade behaviour changes the first time the
//   pragma is applied. This matches the DbContext's model expectations
//   (all HasMany / HasForeignKey declarations already assumed enforced
//   FKs). No repository code needs to change.
//
// • DO NOT wrap PRAGMA foreign_keys=ON inside a transaction — SQLite
//   silently ignores it in that case. The pragma interceptor runs at
//   ConnectionOpened, which is BEFORE any transaction can start.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProGlassAutomation.Services.Logging;
using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Database
{
    /// <summary>
    /// Central SQLite hardening: pragmas, busy_timeout, WAL, foreign_keys,
    /// and a transient-error retry helper. See the file-header comment
    /// for the rationale.
    /// </summary>
    public static class SqliteResilience
    {
        // ── Pragma script applied to every new connection ────────────────
        //
        // A single SQL batch is faster than N round-trips: SqliteCommand.
        // ExecuteNonQuery over a semicolon-separated batch runs all
        // pragmas in one call. Kept on separate lines here for
        // readability — the runtime concatenates them.
        internal const string PragmaScript =
            "PRAGMA journal_mode = WAL;" +
            "PRAGMA synchronous  = NORMAL;" +
            "PRAGMA busy_timeout = 5000;" +
            "PRAGMA foreign_keys = ON;" +
            "PRAGMA temp_store   = MEMORY;" +
            "PRAGMA mmap_size    = 67108864;" +   // 64 MB
            "PRAGMA cache_size   = -16000;";      // 16 MB (negative = kB)

        internal const string NetworkSharePragmaScript =
            "PRAGMA journal_mode = DELETE;" +     // WAL shared memory is unsafe on SMB/NAS
            "PRAGMA synchronous  = FULL;" +
            "PRAGMA busy_timeout = 15000;" +
            "PRAGMA foreign_keys = ON;" +
            "PRAGMA temp_store   = MEMORY;" +
            "PRAGMA cache_size   = -12000;";

        internal static string PragmaScriptFor(string? databaseFilePath) =>
            IsSharedRootDatabase(databaseFilePath) ? NetworkSharePragmaScript : PragmaScript;

        private static bool IsSharedRootDatabase(string? databaseFilePath)
        {
            try
            {
                if (!AppDataRoot.IsSharedDataRootEnabled || string.IsNullOrWhiteSpace(databaseFilePath))
                    return false;
                string db = System.IO.Path.GetFullPath(databaseFilePath);
                string root = System.IO.Path.GetFullPath(AppDataRoot.SharedDataRoot)
                    .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                return db.Equals(root, StringComparison.OrdinalIgnoreCase)
                    || db.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || db.StartsWith(root + System.IO.Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // ── Public helpers ──────────────────────────────────────────────

        /// <summary>
        /// Preferred connection string builder. Uses <see cref="SqliteOpenMode.ReadWriteCreate"/>
        /// (the EF default) plus <see cref="SqliteCacheMode.Shared"/> which lets
        /// multiple in-process connections share the same page cache — WAL
        /// still gives the concurrency guarantees, but the shared cache
        /// avoids duplicated buffers when several DbContext instances are
        /// active. Pooling is enabled by default in Microsoft.Data.Sqlite
        /// 8.x, so we don't need to opt in.
        /// </summary>
        public static string BuildConnectionString(string databaseFilePath)
        {
            if (string.IsNullOrWhiteSpace(databaseFilePath))
                throw new ArgumentException("Database file path is required.", nameof(databaseFilePath));

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databaseFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = IsSharedRootDatabase(databaseFilePath)
                    ? SqliteCacheMode.Default
                    : SqliteCacheMode.Shared,
                // 30 s default statement timeout is fine for a desktop
                // app; the busy_timeout PRAGMA above already covers
                // transient lock waits. The connection-string
                // DefaultTimeout only affects "how long does a single
                // ExecuteXxx allow before ADO.NET gives up", it does
                // not affect SQLite's internal retry.
                DefaultTimeout = 30,
                // Foreign keys are enforced by PRAGMA (see PragmaScript).
                // The connection-string ForeignKeys option only sets
                // the pragma at open time, but we do that explicitly
                // via the interceptor to keep every setting in one
                // place and to survive connection-pool checkout.
                ForeignKeys = null
            };
            return builder.ToString();
        }

        /// <summary>
        /// The single EF Core interceptor every DbContext hooks in
        /// OnConfiguring. Applies the pragma script to each new physical
        /// connection immediately after open.
        /// </summary>
        public static readonly IInterceptor PragmaInterceptor = new SqlitePragmaInterceptor();

        /// <summary>
        /// Convenience: apply UseSqlite + PragmaInterceptor in one call.
        /// The one line every DbContext.OnConfiguring should call.
        /// </summary>
        public static DbContextOptionsBuilder ConfigureFor(
            DbContextOptionsBuilder options, string databaseFilePath)
        {
            options.UseSqlite(BuildConnectionString(databaseFilePath));
            options.AddInterceptors(PragmaInterceptor);
            return options;
        }

        /// <summary>
        /// Retry SaveChangesAsync on transient SQLite lock errors
        /// (SQLITE_BUSY / SQLITE_LOCKED). Non-transient failures
        /// (UNIQUE / NOT NULL / FOREIGN KEY / CHECK) surface
        /// immediately — the caller must handle them explicitly
        /// (typically by regenerating a key or reporting to the user).
        /// </summary>
        /// <param name="save">Delegate that performs the SaveChanges call.
        ///   Typically <c>ct => db.SaveChangesAsync(ct)</c>.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="maxAttempts">Maximum total attempts (default 5).</param>
        /// <returns>The number of rows written on the successful attempt.</returns>
        public static async Task<int> SaveWithRetryAsync(
            Func<CancellationToken, Task<int>> save,
            CancellationToken ct = default,
            int maxAttempts = 5)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (maxAttempts < 1) maxAttempts = 1;

            int attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    return await save(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < maxAttempts && SqliteErrorClassifier.IsTransient(ex))
                {
                    // Exponential back-off with cap at 800 ms. The
                    // SQLite writer usually clears within a single tick;
                    // longer waits handle checkpoint stalls on WAL.
                    int delayMs = Math.Min(800, 40 * (1 << (attempt - 1)));
                    AppLogService.Debug(
                        $"[SqliteResilience] transient SQLite error on attempt {attempt}/{maxAttempts} " +
                        $"(code={SqliteErrorClassifier.Classify(ex)}): {ex.Message} — retrying in {delayMs} ms.",
                        new LogContext
                        {
                            Module = "Database",
                            Class = nameof(SqliteResilience),
                            Operation = nameof(SaveWithRetryAsync)
                        });
                    try { await Task.Delay(delayMs, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                }
            }
        }

        /// <summary>
        /// Runs a bulk write (<c>ExecuteUpdateAsync</c> /
        /// <c>ExecuteDeleteAsync</c>) under the same transient-error retry as
        /// <see cref="SaveWithRetryAsync"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Bulk writes are what make a counter increment safe on SQLite. A
        /// read-modify-write in C# — load the row, check the balance, add to it,
        /// save — loses an update whenever two operators touch the same row,
        /// because both read the same starting value and the second write
        /// silently overwrites the first. Expressing the increment and its guard
        /// as a single conditional UPDATE lets SQLite evaluate and apply them
        /// under one write lock, which removes the race entirely.
        /// </para>
        /// <para>
        /// The retry policy is shared rather than duplicated: a bulk write hits
        /// the same SQLITE_BUSY and WAL checkpoint stalls as a SaveChanges, and
        /// needs the same back-off. This is a thin alias so the loop lives in one
        /// place and both kinds of write age together.
        /// </para>
        /// <para>
        /// Note what a bulk write does NOT do: it bypasses change tracking and
        /// any SaveChanges interceptor, so computed properties, audit stamps and
        /// domain validation attached to those paths do not run. Every column
        /// the operation should touch must be named in the setter list. Verified
        /// when this was introduced — this codebase has no
        /// <c>SaveChanges</c> override and no SaveChanges interceptor, only the
        /// PRAGMA connection interceptor in <see cref="ConfigureFor"/>, so
        /// nothing was being skipped. Re-check that if either is ever added.
        /// </para>
        /// </remarks>
        /// <param name="execute">Delegate performing the bulk write. Typically
        ///   <c>ct =&gt; db.Entity.Where(...).ExecuteUpdateAsync(..., ct)</c>.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="maxAttempts">Maximum total attempts (default 5).</param>
        /// <returns>The number of rows affected on the successful attempt. Zero
        ///   means the WHERE clause matched nothing — which for a guarded
        ///   increment is the guard refusing, not an error.</returns>
        public static Task<int> ExecuteWithRetryAsync(
            Func<CancellationToken, Task<int>> execute,
            CancellationToken ct = default,
            int maxAttempts = 5)
            => SaveWithRetryAsync(execute, ct, maxAttempts);

        // ── Interceptor implementation ──────────────────────────────────

        private sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
        {
            public override void ConnectionOpened(
                DbConnection connection, ConnectionEndEventData eventData)
            {
                ApplyPragmas(connection);
                base.ConnectionOpened(connection, eventData);
            }

            public override Task ConnectionOpenedAsync(
                DbConnection connection,
                ConnectionEndEventData eventData,
                CancellationToken cancellationToken = default)
            {
                ApplyPragmas(connection);
                return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
            }

            private static void ApplyPragmas(DbConnection connection)
            {
                // Defensive: only run for SQLite. This interceptor is
                // referenced by every DbContext.OnConfiguring, and if a
                // future refactor points one at a different provider
                // (or a test harness swaps in InMemory) the PRAGMA
                // batch would be invalid SQL. Silently skip.
                if (connection is not SqliteConnection sqlite) return;
                try
                {
                    using var cmd = sqlite.CreateCommand();
                    cmd.CommandText = PragmaScriptFor(sqlite.DataSource);
                    cmd.CommandType = CommandType.Text;
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    // A pragma failure must NOT prevent the connection
                    // from being used. Read-only media (network share,
                    // CD-ROM) can reject journal_mode=WAL; the connection
                    // still works with the previous journal mode.
                    // (The one exception — WAL still active on a
                    // shared-root database — is verified AFTER this
                    // catch, below.)
                    //
                    // Error, not Warning (Phase 14). This is the one silent
                    // degradation that quietly changes data integrity rather
                    // than performance: if foreign_keys did not take,
                    // referential integrity is unenforced for every write on
                    // this connection and nothing else in the application will
                    // ever say so. Warning is what this codebase reserves for
                    // things that recovered; this one did not — it continued
                    // unhardened. The exception object is passed too, so the
                    // ErrorId carries a stack rather than only ex.Message.
                    AppLogService.Error(
                        $"[SqliteResilience] Could not apply hardening pragmas to '{sqlite.DataSource}'. " +
                        "The connection will still be used, but WAL / foreign_keys may not be active.",
                        ex,
                        new LogContext
                        {
                            Module = "Database",
                            Class = nameof(SqliteResilience),
                            Operation = "ApplyPragmas"
                        });
                }

                // Senior audit (2026-09-24): on a shared-root database, WAL
                // is not a performance preference — it is UNSAFE across
                // SMB/NAS (the -shm coordination file is not reliable over
                // the network redirector, and two workstations can diverge
                // silently). journal_mode=DELETE is a file-format change
                // that needs exclusive access, so it can fail to take
                // effect while another workstation holds the file. Verify
                // the mode that is actually live and fail loud instead of
                // risking silent corruption of the company database.
                if (IsSharedRootDatabase(sqlite.DataSource))
                {
                    try
                    {
                        using var check = sqlite.CreateCommand();
                        check.CommandText = "PRAGMA journal_mode;";
                        using var reader = check.ExecuteReader();
                        if (reader.Read())
                        {
                            string mode = reader.GetString(0);
                            if (string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
                            {
                                // Documented public ctor: (string? message, int errorCode).
                                // Code 1 = SQLITE_ERROR: the classifier treats it as
                                // non-transient, so this surfaces immediately instead
                                // of retrying a condition that will not clear itself.
                                throw new SqliteException(
                                    "The shared company database is in WAL journal mode, which is " +
                                    "unsafe over a WiFi/LAN share (SQLITE journal-mode verification " +
                                    "failed at connection open). Run Settings → Data → " +
                                    "“Prepare Folder” once (it converts the databases to " +
                                    "rollback-journal mode), then restart every workstation.",
                                    1);
                            }
                        }
                    }
                    catch (SqliteException security)
                    {
                        throw security;
                    }
                    catch (Exception ex)
                    {
                        // Verification itself failing (connection trouble)
                        // is logged; the connection is still used.
                        AppLogService.Error(
                            $"[SqliteResilience] journal_mode verification failed on '{sqlite.DataSource}': {ex.Message}",
                            ex,
                            new LogContext
                            {
                                Module = "Database",
                                Class = nameof(SqliteResilience),
                                Operation = "VerifySharedJournalMode"
                            });
                    }
                }
            }
        }
    }

    /// <summary>
    /// Classifies a SQLite error into one of a small set of categories
    /// the app cares about. Used by <see cref="SqliteResilience.SaveWithRetryAsync"/>
    /// to distinguish retry-able from terminal failures, and by
    /// repositories to raise clean user-visible messages.
    /// </summary>
    public enum SqliteErrorKind
    {
        /// <summary>Not a SQLite error, or an unrecognised category.</summary>
        Other = 0,
        /// <summary>SQLITE_BUSY (5) — another process/thread holds a lock. Retry-able.</summary>
        Busy = 5,
        /// <summary>SQLITE_LOCKED (6) — a lock inside the same connection. Retry-able.</summary>
        Locked = 6,
        /// <summary>SQLITE_CORRUPT (11) — the database file is damaged. Terminal.</summary>
        Corrupt = 11,
        /// <summary>SQLITE_READONLY (8) — write attempted on read-only media/mount.</summary>
        ReadOnly = 8,
        /// <summary>SQLITE_CONSTRAINT (19) generic — NOT NULL / CHECK / FOREIGN KEY / UNIQUE.</summary>
        Constraint = 19,
        /// <summary>SQLITE_CONSTRAINT_UNIQUE (2067) — UNIQUE index violation.</summary>
        UniqueViolation = 2067,
        /// <summary>SQLITE_CONSTRAINT_FOREIGNKEY (787) — cascade / restrict FK violation.</summary>
        ForeignKeyViolation = 787,
        /// <summary>SQLITE_CONSTRAINT_NOTNULL (1299) — NOT NULL violated.</summary>
        NotNullViolation = 1299,
        /// <summary>SQLITE_CONSTRAINT_CHECK (275) — CHECK violated.</summary>
        CheckViolation = 275,
    }

    /// <summary>
    /// Pure functions that inspect an exception (typically the InnerException
    /// of a <see cref="DbUpdateException"/>) and report which SQLite category
    /// it maps to.
    /// </summary>
    public static class SqliteErrorClassifier
    {
        /// <summary>Walks the exception chain looking for a <see cref="SqliteException"/>.
        /// Returns <see cref="SqliteErrorKind.Other"/> if none is found.</summary>
        public static SqliteErrorKind Classify(Exception? exception)
        {
            var sqlite = Find(exception);
            if (sqlite == null) return SqliteErrorKind.Other;

            // Prefer the extended code (SqliteExtendedErrorCode) — it
            // distinguishes SQLITE_CONSTRAINT_UNIQUE from the generic
            // SQLITE_CONSTRAINT. Fall back to the base code when the
            // extended one is not populated.
            int extended = sqlite.SqliteExtendedErrorCode;
            switch (extended)
            {
                case 2067: return SqliteErrorKind.UniqueViolation;
                case 1555: return SqliteErrorKind.UniqueViolation;   // SQLITE_CONSTRAINT_PRIMARYKEY
                case 787:  return SqliteErrorKind.ForeignKeyViolation;
                case 1299: return SqliteErrorKind.NotNullViolation;
                case 275:  return SqliteErrorKind.CheckViolation;
            }

            switch (sqlite.SqliteErrorCode)
            {
                case 5:  return SqliteErrorKind.Busy;
                case 6:  return SqliteErrorKind.Locked;
                case 8:  return SqliteErrorKind.ReadOnly;
                case 11: return SqliteErrorKind.Corrupt;
                case 19:
                    // Extended code was not populated (older driver) —
                    // sniff the message as a last resort. Message-based
                    // detection is inherently fragile, so we cover only
                    // the two we care about in retry loops.
                    string msg = sqlite.Message ?? "";
                    if (msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
                        return SqliteErrorKind.UniqueViolation;
                    if (msg.Contains("FOREIGN KEY constraint failed", StringComparison.OrdinalIgnoreCase))
                        return SqliteErrorKind.ForeignKeyViolation;
                    if (msg.Contains("NOT NULL constraint failed", StringComparison.OrdinalIgnoreCase))
                        return SqliteErrorKind.NotNullViolation;
                    if (msg.Contains("CHECK constraint failed", StringComparison.OrdinalIgnoreCase))
                        return SqliteErrorKind.CheckViolation;
                    return SqliteErrorKind.Constraint;
            }
            return SqliteErrorKind.Other;
        }

        /// <summary>Convenience: true iff <see cref="Classify"/> returns Busy or Locked.</summary>
        public static bool IsTransient(Exception? exception)
        {
            var kind = Classify(exception);
            return kind == SqliteErrorKind.Busy || kind == SqliteErrorKind.Locked;
        }

        /// <summary>Convenience: true iff <see cref="Classify"/> returns UniqueViolation.</summary>
        public static bool IsUnique(Exception? exception) =>
            Classify(exception) == SqliteErrorKind.UniqueViolation;

        /// <summary>Convenience: true iff <see cref="Classify"/> returns Corrupt or ReadOnly.</summary>
        public static bool IsTerminal(Exception? exception)
        {
            var kind = Classify(exception);
            return kind == SqliteErrorKind.Corrupt || kind == SqliteErrorKind.ReadOnly;
        }

        /// <summary>Walks the InnerException chain, up to a small depth, looking for a SqliteException.</summary>
        private static SqliteException? Find(Exception? exception)
        {
            for (int depth = 0; depth < 8 && exception != null; depth++)
            {
                if (exception is SqliteException s) return s;
                exception = exception.InnerException;
            }
            return null;
        }
    }
}
