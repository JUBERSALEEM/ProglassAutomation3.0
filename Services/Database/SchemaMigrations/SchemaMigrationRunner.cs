// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Database/SchemaMigrations/SchemaMigrationRunner.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — versioned schema migration runner
//
// Applies pending <see cref="SchemaMigration"/>s to a single SQLite file
// safely and recoverably:
//
//   1. Read the current schema version via PRAGMA user_version.
//   2. Compute pending migrations (Version > current, in order).
//   3. If none pending, return true (no-op) — fresh and up-to-date files
//      are untouched.
//   4. BACKUP the database file BEFORE migrating (refuses to migrate if the
//      backup cannot be created).
//   5. Apply every pending migration inside ONE transaction.
//   6. Set PRAGMA user_version to the highest applied version.
//   7. On any failure: roll back, log the exact failure + backup location,
//      and leave user_version unchanged so the run is retryable.
//
// The runner is intentionally additive and non-destructive. It never drops
// tables, columns, or rows. All 10 databases share this one implementation,
// so there is no per-database migration approach to drift apart.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using ProGlassAutomation.Services.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ProGlassAutomation.Services.Database.SchemaMigrations
{
    /// <summary>Applies versioned schema migrations to a SQLite file.</summary>
    public static class SchemaMigrationRunner
    {
        private static readonly object FailureGate = new();
        private static readonly List<string> FailureMessages = new();

        /// <summary>
        /// True when any upgrade this process attempted failed. Callers that
        /// ignore the bool still leave a record here so startup can tell the
        /// operator. The schema is unchanged and the backup path, when one
        /// was made, is in <see cref="FailureSummary"/>.
        /// </summary>
        public static bool HasFailure
        {
            get { lock (FailureGate) return FailureMessages.Count > 0; }
        }

        public static string FailureSummary
        {
            get { lock (FailureGate) return string.Join(Environment.NewLine, FailureMessages); }
        }

        private static void RecordFailure(string message)
        {
            lock (FailureGate)
            {
                if (!FailureMessages.Contains(message))
                    FailureMessages.Add(message);
            }
        }

        /// <summary>
        /// Ensures a database file is at the latest registered schema version.
        /// Returns true when up-to-date or successfully upgraded; false when a
        /// migration failed (a backup was made and the schema is unchanged).
        /// </summary>
        public static bool Upgrade(
            string dbPath,
            IReadOnlyList<SchemaMigration> migrations,
            string logModule)
        {
            if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
                return true;   // fresh DB — EnsureCreated owns creation

            if (migrations == null || migrations.Count == 0)
                return true;   // nothing registered for this database yet

            try
            {
                int current = ReadVersion(dbPath);
                var pending = migrations
                    .Where(m => m.Version > current)
                    .OrderBy(m => m.Version)
                    .ToList();

                if (pending.Count == 0)
                    return true;   // already current

                int target = pending[^1].Version;

                // ── SAFETY: backup before migrating ─────────────────────
                string backupPath = BackupBeforeMigrate(dbPath, target);
                if (string.IsNullOrEmpty(backupPath))
                {
                    string aborted = $"Schema upgrade for {Path.GetFileName(dbPath)} was aborted: a backup could not be created. The database was not changed.";
                    RecordFailure(aborted);
                    AppLogService.Error(
                        aborted,
                        null,
                        new LogContext
                        {
                            Module = logModule,
                            Class = nameof(SchemaMigrationRunner),
                            Operation = "Upgrade"
                        });
                    return false;
                }

                using (var connection = Open(dbPath))
                using (var tx = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var migration in pending)
                            migration.Apply(connection);

                        SetVersion(connection, target);
                        tx.Commit();
                    }
                    catch (Exception applyEx)
                    {
                        try { tx.Rollback(); } catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[SchemaMigrationRunner] Upgrade: rollback is best-effort: " + ex.Message); }
                        string failed = $"Schema upgrade for {Path.GetFileName(dbPath)} failed. The database was not changed. Backup: {backupPath}";
                        RecordFailure(failed);
                        AppLogService.Error(
                            failed,
                            applyEx,
                            new LogContext
                            {
                                Module = logModule,
                                Class = nameof(SchemaMigrationRunner),
                                Operation = "Upgrade"
                            });
                        return false;
                    }
                }

                AppLogService.Info(
                    $"Schema upgraded {Path.GetFileName(dbPath)} → v{target} " +
                    $"({pending.Count} migration(s) applied).",
                    new LogContext
                    {
                        Module = logModule,
                        Class = nameof(SchemaMigrationRunner),
                        Operation = "Upgrade"
                    });
                return true;
            }
            catch (Exception ex)
            {
                string runFailed = $"Schema upgrade for {Path.GetFileName(dbPath)} failed ({ex.GetType().Name}). The database was not changed.";
                RecordFailure(runFailed);
                AppLogService.Error(
                    runFailed,
                    ex,
                    new LogContext
                    {
                        Module = logModule,
                        Class = nameof(SchemaMigrationRunner),
                        Operation = "Upgrade"
                    });
                return false;
            }
        }

        // ── Version read/write (PRAGMA user_version) ─────────────────────

        private static int ReadVersion(string dbPath)
        {
            using var connection = Open(dbPath);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var value = command.ExecuteScalar();
            return Convert.ToInt32(value ?? 0, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void SetVersion(SqliteConnection connection, int version)
        {
            // PRAGMA user_version cannot be parameterised; version is an int
            // owned by our own code, so embedding is safe.
            SchemaMigrationSql.Execute(connection, $"PRAGMA user_version = {version};");
        }

        // ── Connections / backup ─────────────────────────────────────────

        private static SqliteConnection Open(string dbPath)
        {
            // Senior audit (Aug 2026): route through SqliteResilience.
            // BuildConnectionString so schema migrations run with the same
            // Mode=ReadWriteCreate + Cache=Shared + pool settings the EF
            // path uses. Then apply the pragma script manually — a raw
            // SqliteConnection does not invoke the EF interceptor, and
            // migrations that run without busy_timeout will fail with
            // SQLITE_BUSY if another process holds the file briefly.
            var connection = new SqliteConnection(
                SqliteResilience.BuildConnectionString(dbPath));
            connection.Open();
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = SqliteResilience.PragmaScript;
                cmd.ExecuteNonQuery();
            }
            catch (System.Exception ex)
            {
                // Non-fatal — migrations still run without WAL, they
                // just risk a BUSY on very heavy contention. See
                // SqliteResilience.PragmaScript for detail.
                ProGlassAutomation.Services.Logging.AppLogService.Debug("[SchemaMigrationRunner] WAL pragma skipped: " + ex.Message);
            }
            return connection;
        }

        /// <summary>
        /// Copies the live database file to a dedicated MigrationBackups
        /// folder before any migration. Returns the backup path, or null on
        /// failure (the caller then refuses to migrate).
        /// </summary>
        private static string? BackupBeforeMigrate(string dbPath, int targetVersion)
        {
            try
            {
                // Release pooled EF connections so the file is not locked.
                try { SqliteConnection.ClearAllPools(); } catch (Exception ex) { AppLogService.Warning($"Suppressed exception: {ex.Message}"); }

                var dir = Path.GetDirectoryName(dbPath);
                if (string.IsNullOrEmpty(dir)) return null;

                var backupDir = Path.Combine(dir, "MigrationBackups");
                Directory.CreateDirectory(backupDir);

                string fileName = Path.GetFileName(dbPath);
                string baseName = Path.GetFileNameWithoutExtension(fileName);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupPath = Path.Combine(
                    backupDir, $"{baseName}_pre_v{targetVersion}_{stamp}.db");

                File.Copy(dbPath, backupPath, overwrite: false);
                return backupPath;
            }
            catch (Exception ex)
            {
                AppLogService.Error(
                    $"Pre-migration backup failed for {Path.GetFileName(dbPath)}.",
                    ex,
                    new LogContext
                    {
                        Module = "Database",
                        Class = nameof(SchemaMigrationRunner),
                        Operation = "BackupBeforeMigrate"
                    });
                return null;
            }
        }
    }
}
