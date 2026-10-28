// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.b.1.ProjectSchema.cs
// MODULE: step5 — the Projects table and the inquiry link, on the existing
// ERP file.
//
// One file ⇒ one PRAGMA user_version ⇒ one migration chain. This is migration
// **1017** on the chain that already carries 1011 (Clients), 1013 (Users),
// 1014 (representative), 1015 (credentials) and 1016 (CompanyProfile). No
// second database, no second runner, no inline DDL from the repository.
//
// Two rules from the specification govern every line below:
//
//   1. NOTHING EXISTING IS REPURPOSED. `Inquiries.CustomerId` and
//      `Inquiries.ProjectId` are pre-existing placeholders; they are left exactly
//      as they are, with their values and their meaning. The link this module
//      needs is carried by two new columns (`ClientId`, `ProjectRecordId`), so a
//      reader can always tell a step5 link from a legacy placeholder.
//
//   2. NO HISTORY IS REWRITTEN. Both new columns are nullable and are added with
//      no default value, so every existing inquiry keeps NULL and its text
//      columns byte-for-byte. Nothing here matches an old inquiry to a client or
//      a project by name.
//
// Additive and safe: CREATE TABLE IF NOT EXISTS for Projects, ADD COLUMN for the
// two links, and indexes. Re-running is a no-op.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using ProGlassAutomation.Models.Projects;
using ProGlassAutomation.Services.Database.SchemaMigrations;

namespace ProGlassAutomation.Services.Projects
{
    /// <summary>DDL and shape assertions for Projects and the inquiry link.</summary>
    public static class ProjectSchema
    {
        public const string TableName = "Projects";

        /// <summary>Migration version that introduces project records.</summary>
        public const int MigrationVersion = 1017;

        // ── The inquiry link ────────────────────────────────────────────
        // New columns on Inquiries. The legacy CustomerId / ProjectId
        // placeholders are NOT touched: the specification forbids repurposing
        // them, and a column that changes meaning silently is worse than a
        // column that is duplicated openly.
        public const string InquiriesTable = "Inquiries";
        public const string InquiryClientColumn = "ClientId";
        public const string InquiryProjectColumn = "ProjectRecordId";
        public const string InquiryClientIndex = "IX_Inquiries_ClientId_Step5";
        public const string InquiryProjectIndex = "IX_Inquiries_ProjectRecordId";

        public static readonly string[] RequiredColumns =
        {
            "Id", "ProjectNumber", "ProjectName", "ClientId", "ClientName",
            "Location", "Description", "Status",
            "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy"
        };

        public static readonly string[] NonNullColumns =
        {
            "Id", "ProjectNumber", "ProjectName", "ClientName", "Location",
            "Description", "Status", "CreatedAt", "UpdatedAt", "CreatedBy", "UpdatedBy"
        };

        /// <summary>Creates the table and the two link columns when missing. Idempotent.</summary>
        public static void Ensure(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            SchemaMigrationSql.CreateTableIfNotExists(connection, $@"
                CREATE TABLE IF NOT EXISTS ""{TableName}"" (
                    ""Id""            INTEGER NOT NULL CONSTRAINT ""PK_Projects"" PRIMARY KEY AUTOINCREMENT,
                    ""ProjectNumber"" TEXT    NOT NULL DEFAULT '',
                    ""ProjectName""   TEXT    NOT NULL DEFAULT '',
                    ""ClientId""      INTEGER NULL,
                    ""ClientName""    TEXT    NOT NULL DEFAULT '',
                    ""Location""      TEXT    NOT NULL DEFAULT '',
                    ""Description""   TEXT    NOT NULL DEFAULT '',
                    ""Status""        TEXT    NOT NULL DEFAULT '{ProjectStatus.Active}',
                    ""CreatedAt""     TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00',
                    ""UpdatedAt""     TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00',
                    ""CreatedBy""     TEXT    NOT NULL DEFAULT '',
                    ""UpdatedBy""     TEXT    NOT NULL DEFAULT '',
                    CONSTRAINT ""CK_Projects_Status"" CHECK (""Status"" IN ('{ProjectStatus.Active}', '{ProjectStatus.Closed}'))
                );");

            // The number is how the office identifies the job, so two projects may
            // never share one. Enforced here rather than in the allocator: a
            // second writer, or a retry, must hit a refusal, not a duplicate.
            SchemaMigrationSql.CreateIndexIfNotExists(connection, $@"
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Projects_Number""
                    ON ""{TableName}"" (""ProjectNumber"");");

            SchemaMigrationSql.CreateIndexIfNotExists(connection, $@"
                CREATE INDEX IF NOT EXISTS ""IX_Projects_ClientId""
                    ON ""{TableName}"" (""ClientId"");");

            SchemaMigrationSql.CreateIndexIfNotExists(connection, $@"
                CREATE INDEX IF NOT EXISTS ""IX_Projects_Status_Name""
                    ON ""{TableName}"" (""Status"", ""ProjectName"");");

            // The inquiry link. Nullable, no default: existing rows stay NULL,
            // which is what "not linked yet" must mean.
            SchemaMigrationSql.AddColumnIfMissing(connection, InquiriesTable, InquiryClientColumn, "INTEGER NULL");
            SchemaMigrationSql.AddColumnIfMissing(connection, InquiriesTable, InquiryProjectColumn, "INTEGER NULL");

            SchemaMigrationSql.CreateIndexIfNotExists(connection, $@"
                CREATE INDEX IF NOT EXISTS ""{InquiryClientIndex}""
                    ON ""{InquiriesTable}"" (""{InquiryClientColumn}"");");

            SchemaMigrationSql.CreateIndexIfNotExists(connection, $@"
                CREATE INDEX IF NOT EXISTS ""{InquiryProjectIndex}""
                    ON ""{InquiriesTable}"" (""{InquiryProjectColumn}"");");
        }

        /// <summary>
        /// Post-migration assertions, run after the whole pending set has applied,
        /// so they must describe the FINAL shape.
        /// </summary>
        public static void AssertShape(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            foreach (string column in RequiredColumns)
                SchemaMigrationSql.RequireColumn(connection, TableName, column);

            foreach (string column in NonNullColumns)
                SchemaMigrationSql.RequireNoNullValues(connection, TableName, column);

            SchemaMigrationSql.RequireIndex(connection, "IX_Projects_Number", TableName,
                unique: true, expectedColumns: new[] { "ProjectNumber" });
            SchemaMigrationSql.RequireIndex(connection, "IX_Projects_ClientId", TableName,
                unique: false, expectedColumns: new[] { "ClientId" });

            // The link columns exist on Inquiries, and the legacy placeholders they
            // sit beside are still there, untouched and unreferenced by this module.
            SchemaMigrationSql.RequireColumn(connection, InquiriesTable, InquiryClientColumn);
            SchemaMigrationSql.RequireColumn(connection, InquiriesTable, InquiryProjectColumn);
            SchemaMigrationSql.RequireColumn(connection, InquiriesTable, "CustomerId");
            SchemaMigrationSql.RequireColumn(connection, InquiriesTable, "ProjectId");

            SchemaMigrationSql.RequireIndex(connection, InquiryProjectIndex, InquiriesTable,
                unique: false, expectedColumns: new[] { InquiryProjectColumn });

            long badStatus = ScalarLong(connection,
                $@"SELECT COUNT(*) FROM ""{TableName}"" WHERE ""Status"" NOT IN ('{ProjectStatus.Active}', '{ProjectStatus.Closed}');");
            if (badStatus != 0)
                throw new InvalidOperationException(
                    $"{TableName} holds {badStatus} row(s) with a status that is neither "
                    + $"{ProjectStatus.Active} nor {ProjectStatus.Closed}; the CHECK constraint is missing "
                    + "from this table, so this database was written by another build.");

            long blankNumbers = ScalarLong(connection,
                $@"SELECT COUNT(*) FROM ""{TableName}"" WHERE TRIM(""ProjectNumber"") = '';");
            if (blankNumbers != 0)
                throw new InvalidOperationException(
                    $"{TableName} holds {blankNumbers} row(s) with no project number. This migration "
                    + "never writes rows; a blank number means something else created this table.");
        }

        private static long ScalarLong(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            object? value = command.ExecuteScalar();
            return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value);
        }
    }
}
