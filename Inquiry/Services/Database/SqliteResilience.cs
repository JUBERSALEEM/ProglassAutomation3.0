// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Database/SqliteResilience.cs
// MODULE: Core — SQLite hardening (ported from the legacy build).
//
// WHY THIS FILE EXISTS
// ────────────────────
// Bare `UseSqlite("Data Source=…")` connections produced three classes of
// production bug: transient SQLITE_BUSY / SQLITE_LOCKED errors under
// overlapping writes, silent cascade-delete failures (SQLite foreign-key
// enforcement is connection-local and OFF by default), and retry loops
// that could not actually retry because they reused a failed DbContext.
//
// WHAT THIS FILE DOES
// ───────────────────
// One-stop connection policy + retry helper used by every DbContext and
// raw migration connection:
//
//     options.UseSqlite(SqliteResilience.BuildConnectionString(path))
//            .AddInterceptors(SqliteResilience.PragmaInterceptor);
//
// • Pragmas are re-applied on every new physical connection, not once
//   per app start (EF Core opens and closes connections on demand).
// • journal_mode is persisted in the database header; the local policy
//   requests WAL.
// • busy_timeout / foreign_keys are per-connection settings.
// • The PRAGMA foreign_keys=ON statement must NOT run inside a
//   transaction — the interceptor runs at ConnectionOpened, which is
//   before any transaction can start.
//
// V3 scope: local database root only. The legacy build's shared-root
// (SMB/NAS) journal policy is not ported with the single-station root.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Database
{
    /// <summary>
    /// Central SQLite hardening: connection pragmas (WAL, busy_timeout,
    /// foreign_keys) and a transient-error retry helper.
    /// </summary>
    public static class SqliteResilience
    {
        internal const string PragmaScript =
            "PRAGMA journal_mode = WAL;" +
            "PRAGMA synchronous  = NORMAL;" +
            "PRAGMA busy_timeout = 5000;" +
            "PRAGMA foreign_keys = ON;" +
            "PRAGMA temp_store   = MEMORY;" +
            "PRAGMA mmap_size    = 67108864;" +   // 64 MB
            "PRAGMA cache_size   = -16000;";      // 16 MB (negative = kB)

        /// <summary>
        /// Applies the central SQLite pragma policy to a connection that is
        /// already open. Safe to call from both the EF interceptor and raw
        /// migration connections so runtime and migration policy cannot drift.
        /// </summary>
        internal static void ApplyConnectionPolicy(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            using var command = connection.CreateCommand();
            command.CommandText = PragmaScript;
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Preferred connection-string builder: shared page cache, 30 s
        /// default command timeout. The returned string never chooses a
        /// journal mode itself; ApplyConnectionPolicy owns that choice.
        /// </summary>
        public static string BuildConnectionString(string databaseFilePath)
        {
            if (string.IsNullOrWhiteSpace(databaseFilePath))
                throw new ArgumentException("Database file path is required.", nameof(databaseFilePath));
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databaseFilePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                DefaultTimeout = 30,
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
        /// Convenience: apply UseSqlite + PragmaInterceptor in one call —
        /// the one line every DbContext.OnConfiguring should call.
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
        /// (UNIQUE / NOT NULL / FOREIGN KEY / CHECK) surface immediately.
        /// </summary>
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
                    // Exponential back-off capped at 800 ms. The writer
                    // usually clears within one tick; longer waits cover
                    // WAL checkpoint stalls.
                    int delayMs = Math.Min(800, 40 * (1 << (attempt - 1)));
                    try { await Task.Delay(delayMs, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                }
            }
        }

        /// <summary>
        /// Runs a bulk write under the same transient-error retry as
        /// <see cref="SaveWithRetryAsync"/>. Returns the affected row count.
        /// </summary>
        public static Task<int> ExecuteWithRetryAsync(
            Func<CancellationToken, Task<int>> execute,
            CancellationToken ct = default,
            int maxAttempts = 5)
            => SaveWithRetryAsync(execute, ct, maxAttempts);

        private sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
        {
            public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
            {
                if (connection is SqliteConnection sqlite)
                    SqliteResilience.ApplyConnectionPolicy(sqlite);
                base.ConnectionOpened(connection, eventData);
            }

            public override Task ConnectionOpenedAsync(
                DbConnection connection,
                ConnectionEndEventData eventData,
                CancellationToken cancellationToken = default)
            {
                if (connection is SqliteConnection sqlite)
                    SqliteResilience.ApplyConnectionPolicy(sqlite);
                return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
            }
        }
    }

    /// <summary>
    /// The small set of SQLite error categories the app cares about.
    /// </summary>
    public enum SqliteErrorKind
    {
        Other = 0,
        Busy = 5,             // SQLITE_BUSY — retry-able.
        Locked = 6,           // SQLITE_LOCKED — retry-able.
        ReadOnly = 8,         // write on read-only media/mount.
        Corrupt = 11,         // damaged file — terminal.
        Constraint = 19,      // generic NOT NULL / CHECK / FK / UNIQUE.
        CheckViolation = 275,
        ForeignKeyViolation = 787,
        NotNullViolation = 1299,
        UniqueViolation = 2067
    }

    /// <summary>
    /// Pure functions that inspect an exception (typically the
    /// InnerException of a DbUpdateException) and report which SQLite
    /// category it maps to.
    /// </summary>
    public static class SqliteErrorClassifier
    {
        /// <summary>
        /// Walks the exception chain looking for a SqliteException.
        /// Returns <see cref="SqliteErrorKind.Other"/> when none is found.
        /// </summary>
        public static SqliteErrorKind Classify(Exception? exception)
        {
            var sqlite = Find(exception);
            if (sqlite == null) return SqliteErrorKind.Other;

            // Prefer the extended code — it distinguishes
            // SQLITE_CONSTRAINT_UNIQUE from the generic constraint code.
            switch (sqlite.SqliteExtendedErrorCode)
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
                    // Extended code was not populated — sniff the message
                    // as a last resort (covers only the codes retry loops
                    // and delete guards care about).
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

        /// <summary>True iff the error is Busy or Locked (retry-able).</summary>
        public static bool IsTransient(Exception? exception)
        {
            var kind = Classify(exception);
            return kind == SqliteErrorKind.Busy || kind == SqliteErrorKind.Locked;
        }

        /// <summary>True iff the error is a unique-key violation.</summary>
        public static bool IsUnique(Exception? exception) =>
            Classify(exception) == SqliteErrorKind.UniqueViolation;

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
