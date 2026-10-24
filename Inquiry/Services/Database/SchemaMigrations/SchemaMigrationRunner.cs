// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Database/SchemaMigrations/SchemaMigrationRunner.cs
// MODULE: Core — versioned schema migration runner.
//
// Applies pending SchemaMigrations to a single SQLite file safely and
// recoverably:
//
//   1. Read the current schema version via PRAGMA user_version.
//   2. Compute pending migrations (Version > current, in order).
//   3. If none pending, return true (no-op) — fresh and up-to-date
//      files are untouched.
//   4. BACKUP the database file BEFORE migrating (refuses to migrate
//      if the backup cannot be created).
//   5. Apply every pending migration inside ONE transaction.
//   6. Set PRAGMA user_version to the highest applied version.
//   7. On any failure: roll back, record the failure + backup location,
//      and leave user_version unchanged so the run is retryable.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
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
        /// True when any upgrade this process attempted failed. Callers
        /// that ignore the bool still leave a record here so startup can
        /// tell the operator. The schema is unchanged and the backup path,
        /// when one was made, is in <see cref="FailureSummary"/>.
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
        /// Ensures a database file is at the latest registered schema
        /// version. Returns true when up-to-date or successfully upgraded;
        /// false when a migration failed (a backup was made and the schema
        /// is unchanged).
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
                    RecordFailure(
                        $"Schema upgrade for {Path.GetFileName(dbPath)} was aborted: a backup could not be created. The database was not changed.");
                    return false;
                }

                using (var connection = Open(dbPath))
                using (var tx = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var migration in pending)
                        {
                            migration.Apply(connection);
                            migration.Verify?.Invoke(connection);
                        }

                        // Re-run explicit verification callbacks after the
                        // entire pending set has applied. This catches a
                        // later migration accidentally invalidating an
                        // earlier required index/column/data invariant
                        // before the version marker commits.
                        foreach (var migration in pending)
                            migration.Verify?.Invoke(connection);

                        VerifyDatabaseIntegrity(connection);
                        SetVersion(connection, target);
                        tx.Commit();
                    }
                    catch (Exception applyEx)
                    {
                        try { tx.Rollback(); } catch (System.Exception) { /* rollback is best-effort */ }
                        RecordFailure(
                            $"Schema upgrade for {Path.GetFileName(dbPath)} failed ({applyEx.GetType().Name}). " +
                            "The database was not changed. Backup: " + backupPath);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                RecordFailure(
                    $"Schema upgrade for {Path.GetFileName(dbPath)} failed ({ex.GetType().Name}). The database was not changed.");
                return false;
            }
        }

        /// <summary>
        /// Runs a focused, data-preserving schema repair when an applied
        /// migration version does not match the actual SQLite shape. Unlike
        /// Upgrade, this check is not gated by PRAGMA user_version; this is
        /// for repairs to a required invariant such as the inbox ledger.
        /// </summary>
        public static bool RepairIfNeeded(
            string dbPath,
            int backupVersion,
            string description,
            Func<SqliteConnection, bool> isReady,
            Action<SqliteConnection> repair)
        {
            if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath)) return true;
            ArgumentNullException.ThrowIfNull(isReady);
            ArgumentNullException.ThrowIfNull(repair);
            try
            {
                using (var probe = Open(dbPath))
                    if (isReady(probe)) return true;

                string? backupPath = BackupBeforeMigrate(dbPath, backupVersion);
                if (string.IsNullOrEmpty(backupPath))
                {
                    RecordFailure($"SQLite repair for {description} was aborted: a backup could not be created. The database was not changed.");
                    return false;
                }

                using var connection = Open(dbPath);
                using var tx = connection.BeginTransaction();
                try
                {
                    repair(connection);
                    if (!isReady(connection))
                        throw new InvalidOperationException("The repair completed but the required schema is still incomplete.");
                    VerifyDatabaseIntegrity(connection);
                    tx.Commit();
                    return true;
                }
                catch (Exception repairEx)
                {
                    try { tx.Rollback(); } catch (Exception) { /* rollback is best-effort */ }
                    RecordFailure($"SQLite repair for {description} failed ({repairEx.GetType().Name}). The database was not changed. Backup: {backupPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                RecordFailure($"SQLite repair for {description} failed ({ex.GetType().Name}). The database was not changed.");
                return false;
            }
        }

        // ── Version read/write (PRAGMA user_version) ─────────────────────

        private static void VerifyDatabaseIntegrity(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            using var reader = command.ExecuteReader();
            var problems = new List<string>();
            bool receivedResult = false;
            while (reader.Read())
            {
                receivedResult = true;
                string result = reader.IsDBNull(0) ? "" : reader.GetString(0);
                if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                    problems.Add(result);
            }

            if (!receivedResult)
                throw new InvalidOperationException(
                    "Post-migration SQLite quick_check returned no result.");
            if (problems.Count > 0)
                throw new InvalidOperationException(
                    "Post-migration SQLite quick_check failed: " + string.Join("; ", problems));
        }

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
            // PRAGMA user_version cannot be parameterised; version is an
            // int owned by our own code, so embedding is safe.
            SchemaMigrationSql.Execute(connection, $"PRAGMA user_version = {version};");
        }

        // ── Connections / backup ─────────────────────────────────────────

        private static SqliteConnection Open(string dbPath)
        {
            // Raw migration connections do not invoke EF interceptors, so
            // apply the same policy explicitly.
            var connection = new SqliteConnection(SqliteResilience.BuildConnectionString(dbPath));
            try
            {
                connection.Open();
                SqliteResilience.ApplyConnectionPolicy(connection);
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Copies the live database file to a dedicated MigrationBackups
        /// folder before any migration. Returns the backup path, or null
        /// on failure (the caller then refuses to migrate).
        /// </summary>
        private static string? BackupBeforeMigrate(string dbPath, int targetVersion)
        {
            string? backupPath = null;
            try
            {
                // Drop idle pooled handles, then use SQLite's online backup
                // API rather than File.Copy. In local WAL mode, committed
                // pages may still live in *.db-wal; a main-file copy alone
                // could therefore produce a pre-migration backup that omits
                // committed data. The backup API snapshots the database
                // including its WAL state consistently.
                SqliteConnection.ClearAllPools();

                var dir = Path.GetDirectoryName(dbPath);
                if (string.IsNullOrEmpty(dir)) return null;

                var backupDir = Path.Combine(dir, "MigrationBackups");
                Directory.CreateDirectory(backupDir);

                string fileName = Path.GetFileName(dbPath);
                string baseName = Path.GetFileNameWithoutExtension(fileName);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                    "_" + Guid.NewGuid().ToString("N")[..6];
                backupPath = Path.Combine(
                    backupDir, $"{baseName}_pre_v{targetVersion}_{stamp}.db");

                using var source = Open(dbPath);
                var destinationBuilder = new SqliteConnectionStringBuilder(
                    SqliteResilience.BuildConnectionString(backupPath))
                {
                    Pooling = false
                };
                using var destination = new SqliteConnection(destinationBuilder.ToString());
                destination.Open();
                SqliteResilience.ApplyConnectionPolicy(destination);
                source.BackupDatabase(destination);

                using var check = destination.CreateCommand();
                check.CommandText = "PRAGMA quick_check;";
                var integrity = Convert.ToString(
                    check.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
                if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "The pre-migration backup failed SQLite quick_check: " +
                        (integrity ?? "no result"));

                return backupPath;
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(backupPath))
                {
                    foreach (string candidate in new[] { backupPath, backupPath + "-wal", backupPath + "-shm" })
                    {
                        try { if (File.Exists(candidate)) File.Delete(candidate); }
                        catch { /* incomplete backup cleanup is best-effort */ }
                    }
                }
                return null;
            }
        }
    }
}
