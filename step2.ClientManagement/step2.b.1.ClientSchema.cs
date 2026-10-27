// ═══════════════════════════════════════════════════════════════════════
// FILE: step2.ClientManagement/step2.b.1.ClientSchema.cs
// MODULE: step2 — Clients table DDL, convergence and shape assertions.
//
// The client directory lives in the existing inquiries.db on the existing
// single version chain. One database file ⇒ one PRAGMA user_version ⇒
// exactly one migration chain; a second chain is never created.
//
//   • 1011 creates the directory and its indexes.
//   • 1012 replaces the generic contact-person column with AttentionName —
//     the person at the client to address, since ClientName is the company.
//
// 1011 is NOT rewritten. It has already run on the owner's database, so it
// stays the immutable baseline; 1012 converges both an early 1011 database
// (contact-person column present) and a fresh database (EF already created
// AttentionName from the model) to the same final shape, without losing a
// single stored value.
//
// Two details exist because verification is strict:
//   • SchemaMigrationRunner re-runs every Verify callback after the WHOLE
//     pending set has applied. 1011's assertions therefore cover only columns
//     that survive 1012; the person column is asserted by 1012 alone.
//   • 1012 asserts that the legacy column is GONE, so a failed merge aborts the
//     transaction (backup kept, user_version unchanged) instead of leaving two
//     competing person columns behind.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProGlassAutomation.Services.Clients
{
    /// <summary>DDL and shape checks for the Clients table.</summary>
    public static class ClientSchema
    {
        public const string TableName = "Clients";

        /// <summary>Migration version that introduces the client directory.</summary>
        public const int MigrationVersion = 1011;

        /// <summary>Migration version that renames the person column to AttentionName.</summary>
        public const int AttentionNameMigrationVersion = 1012;

        /// <summary>The superseded person column, kept only for convergence.</summary>
        private const string LegacyPersonColumn = "ContactPerson";

        /// <summary>The person column the model uses.</summary>
        public const string PersonColumn = "AttentionName";

        /// <summary>
        /// 1011 — creates the table and indexes when missing, and adds any missing
        /// column. Idempotent; no existing value is rewritten.
        /// </summary>
        public static void Ensure(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            SchemaMigrationSql.CreateTableIfNotExists(connection, $@"
                CREATE TABLE IF NOT EXISTS ""{TableName}"" (
                    ""Id""            INTEGER NOT NULL CONSTRAINT ""PK_Clients"" PRIMARY KEY AUTOINCREMENT,
                    ""ClientName""    TEXT NOT NULL DEFAULT '',
                    ""{LegacyPersonColumn}"" TEXT NOT NULL DEFAULT '',
                    ""Phone""         TEXT NOT NULL DEFAULT '',
                    ""Email""         TEXT NOT NULL DEFAULT '',
                    ""Trn""           TEXT NOT NULL DEFAULT '',
                    ""Address""       TEXT NOT NULL DEFAULT '',
                    ""Notes""         TEXT NOT NULL DEFAULT '',
                    ""IsActive""      INTEGER NOT NULL DEFAULT 1,
                    ""CreatedAt""     TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    ""UpdatedAt""     TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
                );");

            // Defensive convergence: a partially created table (interrupted
            // install) gains the missing columns instead of failing the app.
            SchemaMigrationSql.AddColumnIfMissing(connection, TableName, LegacyPersonColumn, "TEXT NOT NULL DEFAULT ''");
            SchemaMigrationSql.AddColumnIfMissing(connection, TableName, "Phone", "TEXT NOT NULL DEFAULT ''");
            SchemaMigrationSql.AddColumnIfMissing(connection, TableName, "Email", "TEXT NOT NULL DEFAULT ''");
            SchemaMigrationSql.AddColumnIfMissing(connection, TableName, "Trn", "TEXT NOT NULL DEFAULT ''");
            SchemaMigrationSql.AddColumnIfMissing(connection, TableName, "Address", "TEXT NOT NULL DEFAULT ''");
            SchemaMigrationSql.AddColumnIfMissing(connection, TableName, "Notes", "TEXT NOT NULL DEFAULT ''");
            SchemaMigrationSql.AddColumnIfMissing(connection, TableName, "IsActive", "INTEGER NOT NULL DEFAULT 1");
            SchemaMigrationSql.AddColumnIfMissing(connection, TableName, "CreatedAt", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'");
            SchemaMigrationSql.AddColumnIfMissing(connection, TableName, "UpdatedAt", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'");

            CreateIndexes(connection);
        }

        /// <summary>
        /// 1012 — makes the person column <see cref="PersonColumn"/>, preserving
        /// every stored value.
        ///
        /// Cases, all safe to re-run:
        ///   • new column only          → nothing to do (fresh database);
        ///   • legacy only              → rename (values move with the column);
        ///   • both, legacy empty       → drop the empty legacy column;
        ///   • both, values in both     → copy legacy into blank cells, refuse on
        ///                                conflicting values, then drop legacy.
        /// </summary>
        public static void EnsureAttentionName(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            HashSet<string> columns = ReadColumns(connection, TableName);
            bool hasLegacy = columns.Contains(LegacyPersonColumn);
            bool hasCurrent = columns.Contains(PersonColumn);

            if (!hasLegacy && hasCurrent) return;                 // already converged
            if (!hasLegacy && !hasCurrent)                        // add the column
            {
                SchemaMigrationSql.AddColumnIfMissing(connection, TableName, PersonColumn, "TEXT NOT NULL DEFAULT ''");
                return;
            }
            if (hasLegacy && !hasCurrent)                         // rename, values intact
            {
                SchemaMigrationSql.Execute(connection,
                    $@"ALTER TABLE ""{TableName}"" RENAME COLUMN ""{LegacyPersonColumn}"" TO ""{PersonColumn}"";");
                return;
            }

            // Both columns exist (a database that created AttentionName from the
            // EF model and then had the legacy column added by 1011).
            string legacy = $@"CAST(""{LegacyPersonColumn}"" AS TEXT)";
            string current = $@"CAST(""{PersonColumn}"" AS TEXT)";
            string legacyHasValue =
                $@"""{LegacyPersonColumn}"" IS NOT NULL AND TRIM({legacy}) <> ''";
            string currentHasValue =
                $@"""{PersonColumn}"" IS NOT NULL AND TRIM({current}) <> ''";

            long conflicts = ScalarLong(connection,
                $@"SELECT COUNT(*) FROM ""{TableName}"" WHERE {legacyHasValue} AND {currentHasValue} " +
                $"AND {legacy} <> {current};");
            if (conflicts != 0)
                throw new InvalidOperationException(
                    $"The legacy column '{TableName}.{LegacyPersonColumn}' conflicts with " +
                    $"'{PersonColumn}' in {conflicts} row(s). No value was dropped; resolve the " +
                    "conflict and retry the upgrade.");

            SchemaMigrationSql.Execute(connection,
                $@"UPDATE ""{TableName}"" SET ""{PersonColumn}"" = ""{LegacyPersonColumn}"" " +
                $"WHERE {legacyHasValue} AND ({current} IS NULL OR TRIM({current}) = '');");

            long unpreserved = ScalarLong(connection,
                $@"SELECT COUNT(*) FROM ""{TableName}"" WHERE {legacyHasValue} " +
                $"AND ({current} IS NULL OR TRIM({current}) = '' OR {current} <> {legacy});");
            if (unpreserved != 0)
                throw new InvalidOperationException(
                    $"Could not verify preservation of {unpreserved} value(s) from " +
                    $"'{TableName}.{LegacyPersonColumn}'.");

            SchemaMigrationSql.DropColumnIfExists(
                connection, TableName, LegacyPersonColumn, dataPreservationVerified: true);
        }

        /// <summary>
        /// Search indexes. Deliberately NOT unique: duplicate clients are
        /// warned about, never blocked by a database constraint (owner
        /// decision D9), because two genuine clients can share a name.
        /// </summary>
        public static void CreateIndexes(SqliteConnection connection)
        {
            SchemaMigrationSql.CreateIndexIfNotExists(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_Clients_ClientName\" ON \"Clients\" (\"ClientName\");");
            SchemaMigrationSql.CreateIndexIfNotExists(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_Clients_Phone\" ON \"Clients\" (\"Phone\");");
            SchemaMigrationSql.CreateIndexIfNotExists(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_Clients_Email\" ON \"Clients\" (\"Email\");");
            SchemaMigrationSql.CreateIndexIfNotExists(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_Clients_Trn\" ON \"Clients\" (\"Trn\");");
            SchemaMigrationSql.CreateIndexIfNotExists(connection,
                "CREATE INDEX IF NOT EXISTS \"IX_Clients_IsActive_ClientName\" ON \"Clients\" (\"IsActive\", \"ClientName\");");
        }

        /// <summary>
        /// 1011's post-migration assertions. Covers only what also holds after
        /// 1012, because the runner re-runs every Verify callback once the whole
        /// pending set has applied.
        /// </summary>
        public static void AssertShape(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            foreach (string column in BaselineColumns)
                SchemaMigrationSql.RequireColumn(connection, TableName, column);

            SchemaMigrationSql.RequireNoNullValues(connection, TableName, "ClientName");
            SchemaMigrationSql.RequireNoNullValues(connection, TableName, "IsActive");
            SchemaMigrationSql.RequireNoNullValues(connection, TableName, "CreatedAt");
            SchemaMigrationSql.RequireNoNullValues(connection, TableName, "UpdatedAt");

            SchemaMigrationSql.RequireIndex(connection, "IX_Clients_ClientName",
                TableName, unique: false, expectedColumns: new[] { "ClientName" });
            SchemaMigrationSql.RequireIndex(connection, "IX_Clients_IsActive_ClientName",
                TableName, unique: false, expectedColumns: new[] { "IsActive", "ClientName" });
        }

        /// <summary>1012's post-migration assertions.</summary>
        public static void AssertAttentionName(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            foreach (string column in RequiredColumns)
                SchemaMigrationSql.RequireColumn(connection, TableName, column);

            SchemaMigrationSql.RequireNoNullValues(connection, TableName, PersonColumn);

            if (ReadColumns(connection, TableName).Contains(LegacyPersonColumn))
                throw new InvalidOperationException(
                    $"The legacy column '{TableName}.{LegacyPersonColumn}' is still present after the " +
                    "attention-name convergence; two competing person columns must not survive.");
        }

        /// <summary>Columns every version of the directory has (1011 assertions).</summary>
        public static readonly string[] BaselineColumns =
        {
            "Id", "ClientName", "Phone", "Email", "Trn", "Address", "Notes",
            "IsActive", "CreatedAt", "UpdatedAt"
        };

        /// <summary>Every column the EF model in step2.b.3 maps today.</summary>
        public static readonly string[] RequiredColumns =
        {
            "Id", "ClientName", PersonColumn, "Phone", "Email", "Trn",
            "Address", "Notes", "IsActive", "CreatedAt", "UpdatedAt"
        };

        // ── Helpers ──────────────────────────────────────────────────────

        private static HashSet<string> ReadColumns(SqliteConnection connection, string table)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{table}\");";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(1)) columns.Add(reader.GetString(1));
            }
            return columns;
        }

        private static long ScalarLong(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }
    }
}
