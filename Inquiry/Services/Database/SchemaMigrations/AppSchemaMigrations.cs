// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.cs
// MODULE: Core — per-database schema migration registries (V3).
//
// V3 scope: the Inquiry database only. The rebuilt app opens the legacy
// data folder, so existing inquiries.db files from interim builds must
// converge to the current model (Inquiries + InquiryLineItems +
// InquiryAttachments + InquiryCommunications, RowVersion included).
// Fresh databases are created by EnsureCreated at the current schema and
// are skipped by the runner (user_version already satisfies the registry
// once 1001..1004 apply on first touch).
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProGlassAutomation.Services.Database.SchemaMigrations
{
    public static partial class AppSchemaMigrations
    {
        // inquiries.db
        //
        // Interim builds shipped an earlier Inquiry domain model, so
        // customer databases exist whose Inquiries table predates the
        // commercial columns (InquiryValue / Priority / Notes / …) or even
        // the child tables. EnsureCreated never alters an existing
        // database — this registry converges those installs.
        //
        // 1001 converges ANY legacy shape to the current model. It is
        // idempotent: CREATE TABLE IF NOT EXISTS, one ADD COLUMN per
        // missing column, unique-index duplicates reported without
        // renumbering, then the remaining indexes. Reserved high version
        // so pre-existing PRAGMA user_version values from old installers
        // cannot cause the step to be skipped.
        public static IReadOnlyList<SchemaMigration> Inquiries { get; } = new List<SchemaMigration>
        {
            new(1001, "Converge legacy inquiry databases to the current model", connection =>
                ConvergeInquirySchema(connection)),
            // 1002 runs after 1001 so databases that already upgraded past
            // 1001 (and therefore still carry the legacy InquiryDate column)
            // get the drop. Separate migration rather than mutating 1001,
            // which is the documented baseline for what EnsureCreated
            // should look like after first run.
            new(1002, "Drop legacy Inquiry columns (InquiryDate, etc.) not in the model", connection =>
                DropLegacyInquiriesColumns(connection), connection =>
                VerifyInquirySchema(connection)),
            // Scope of supply (BOQ) on inquiries.
            // EnsureCreated only creates missing DATABASE files; any
            // install that already ran the app has inquiries.db without
            // this table. Idempotent: CREATE TABLE IF NOT EXISTS +
            // IF NOT EXISTS indexes.
            new(1003, "Inquiry scope of supply (BOQ line items)", connection =>
            {
                SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                    CREATE TABLE IF NOT EXISTS ""InquiryLineItems"" (
                        ""Id""              INTEGER NOT NULL CONSTRAINT ""PK_InquiryLineItems"" PRIMARY KEY AUTOINCREMENT,
                        ""InquiryId""       INTEGER NOT NULL,
                        ""SortOrder""       INTEGER NOT NULL DEFAULT 0,
                        ""GlassType""       TEXT NOT NULL DEFAULT '',
                        ""SpecName""        TEXT NOT NULL DEFAULT '',
                        ""OuterGlassColor"" TEXT NOT NULL DEFAULT '',
                        ""InnerGlassColor"" TEXT NOT NULL DEFAULT '',
                        ""WidthMm""         INTEGER NOT NULL DEFAULT 0,
                        ""HeightMm""        INTEGER NOT NULL DEFAULT 0,
                        ""Qty""             INTEGER NOT NULL DEFAULT 0,
                        ""RatePerSqm""      TEXT NOT NULL DEFAULT '0',
                        ""Notes""           TEXT NOT NULL DEFAULT '',
                        ""CreatedAt""       TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
                    );");
                SchemaMigrationSql.CreateIndexIfNotExists(connection,
                    "CREATE INDEX IF NOT EXISTS \"IX_InquiryLineItems_InquiryId\" ON InquiryLineItems(InquiryId);");
                SchemaMigrationSql.CreateIndexIfNotExists(connection,
                    "CREATE INDEX IF NOT EXISTS \"IX_InquiryLineItems_InquiryId_SortOrder\" ON InquiryLineItems(InquiryId, SortOrder);");
            }),
            new(1004, "Inquiry: RowVersion concurrency counter", connection =>
            {
                SchemaMigrationSql.AddColumnIfMissing(connection, "Inquiries", "RowVersion",
                    "INTEGER NOT NULL DEFAULT 0");
                SchemaMigrationSql.RequireColumn(connection, "Inquiries", "RowVersion");
                SchemaMigrationSql.RequireNoNullValues(connection, "Inquiries", "RowVersion");
            }),
            // Website intake: inbox ledger + traceability columns on the
            // register. Idempotent (ADD COLUMN IF NOT EXISTS via the
            // helper, CREATE TABLE IF NOT EXISTS).
            new(1005, "Inquiry: website inbox state + web submission traceability", connection =>
            {
                SchemaMigrationSql.AddColumnIfMissing(connection, "Inquiries", "WebSubmissionId",
                    "TEXT NOT NULL DEFAULT ''");
                SchemaMigrationSql.AddColumnIfMissing(connection, "Inquiries", "WebSubmissionReference",
                    "TEXT NOT NULL DEFAULT ''");
                SchemaMigrationSql.CreateTableIfNotExists(connection, @"""
                    CREATE TABLE IF NOT EXISTS ""InquiryInboxItems"" (
                        ""SubmissionId""           TEXT NOT NULL CONSTRAINT ""PK_InquiryInboxItems"" PRIMARY KEY,
                        ""Reference""              TEXT NOT NULL DEFAULT '',
                        ""State""                  TEXT NOT NULL DEFAULT 'downloaded',
                        ""ManifestSha256""         TEXT NOT NULL DEFAULT '',
                        ""DocumentCount""          INTEGER NOT NULL DEFAULT 0,
                        ""RoutingKey""             TEXT NOT NULL DEFAULT '',
                        ""PrimaryEstimatorCode""   TEXT NOT NULL DEFAULT '',
                        ""AssignmentSnapshot""     TEXT NOT NULL DEFAULT '[]',
                        ""DownloadedAt""           TEXT NOT NULL DEFAULT '',
                        ""IngestedAt""             TEXT NOT NULL DEFAULT '',
                        ""LocalInquiryId""         INTEGER NULL,
                        ""LocalInquiryNumber""     TEXT NOT NULL DEFAULT '',
                        ""Error""                  TEXT NOT NULL DEFAULT ''
                    );"");
                SchemaMigrationSql.CreateIndexIfNotExists(connection,
                    "CREATE INDEX IF NOT EXISTS \"IX_InquiryInboxItems_State\" ON InquiryInboxItems(State);");
                SchemaMigrationSql.CreateIndexIfNotExists(connection,
                    "CREATE INDEX IF NOT EXISTS \"IX_InquiryInboxItems_Reference\" ON InquiryInboxItems(Reference);");
            })
        };

        private static void ConvergeInquirySchema(SqliteConnection connection)
        {
            // 1) Tables missing entirely on the earliest builds.
            SchemaMigrationSql.CreateTableIfNotExists(connection, """
                CREATE TABLE IF NOT EXISTS "Inquiries" (
                    "Id"                 INTEGER NOT NULL CONSTRAINT "PK_Inquiries" PRIMARY KEY AUTOINCREMENT,
                    "InquiryNumber"      TEXT NOT NULL DEFAULT '',
                    "InquiryReference"   TEXT NOT NULL DEFAULT 'Customer Inquiry',
                    "CustomerId"         INTEGER NULL,
                    "CustomerName"       TEXT NOT NULL DEFAULT '',
                    "CustomerLocation"   TEXT NOT NULL DEFAULT '',
                    "ProjectId"          INTEGER NULL,
                    "ProjectName"        TEXT NOT NULL DEFAULT '',
                    "ProjectDescription" TEXT NOT NULL DEFAULT '',
                    "SalesmanId"         INTEGER NULL,
                    "SalesmanName"       TEXT NOT NULL DEFAULT '',
                    "PreparedByUserId"   INTEGER NULL,
                    "PreparedByName"     TEXT NOT NULL DEFAULT '',
                    "PreparedByRole"     TEXT NOT NULL DEFAULT '',
                    "Status"             TEXT NOT NULL DEFAULT 'Received',
                    "Priority"           TEXT NOT NULL DEFAULT 'Normal',
                    "InquiryValue"       TEXT NULL DEFAULT '0',
                    "Notes"              TEXT NOT NULL DEFAULT '',
                    "ReceivedAt"         TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    "PreparedAt"         TEXT NULL,
                    "SentAt"             TEXT NULL,
                    "ConfirmedAt"        TEXT NULL,
                    "CreatedAt"          TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    "UpdatedAt"          TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    "UpdatedBy"          TEXT NOT NULL DEFAULT '',
                    "ProformaInvoiceId"  INTEGER NULL,
                    "ProformaNumber"     TEXT NOT NULL DEFAULT '',
                    "JobOrderNumber"     TEXT NOT NULL DEFAULT ''
                );
                """);
            SchemaMigrationSql.CreateTableIfNotExists(connection, """
                CREATE TABLE IF NOT EXISTS "InquiryAttachments" (
                    "Id"             INTEGER NOT NULL CONSTRAINT "PK_InquiryAttachments" PRIMARY KEY AUTOINCREMENT,
                    "InquiryId"      INTEGER NOT NULL,
                    "FileName"       TEXT NOT NULL DEFAULT '',
                    "Extension"      TEXT NOT NULL DEFAULT '',
                    "FileSize"       INTEGER NOT NULL DEFAULT 0,
                    "StoragePath"    TEXT NOT NULL DEFAULT '',
                    "AttachmentType" TEXT NOT NULL DEFAULT 'Document',
                    "UploadedBy"     TEXT NOT NULL DEFAULT '',
                    "UploadedAt"     TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    CONSTRAINT "FK_InquiryAttachments_Inquiries_InquiryId"
                        FOREIGN KEY ("InquiryId") REFERENCES "Inquiries" ("Id") ON DELETE CASCADE
                );
                """);
            SchemaMigrationSql.CreateTableIfNotExists(connection, """
                CREATE TABLE IF NOT EXISTS "InquiryCommunications" (
                    "Id"          INTEGER NOT NULL CONSTRAINT "PK_InquiryCommunications" PRIMARY KEY AUTOINCREMENT,
                    "InquiryId"   INTEGER NOT NULL,
                    "EventType"   TEXT NOT NULL DEFAULT '',
                    "Title"       TEXT NOT NULL DEFAULT '',
                    "Description" TEXT NOT NULL DEFAULT '',
                    "OccurredAt"  TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    "ActorUserId" INTEGER NULL,
                    "ActorName"   TEXT NOT NULL DEFAULT '',
                    CONSTRAINT "FK_InquiryCommunications_Inquiries_InquiryId"
                        FOREIGN KEY ("InquiryId") REFERENCES "Inquiries" ("Id") ON DELETE CASCADE
                );
                """);

            // 2) Columns missing on interim builds. ALTER TABLE ADD COLUMN
            //    fills existing rows with the DEFAULT, so EF can read them.
            (string Table, string Column, string Definition)[] columns =
            {
                ("Inquiries", "InquiryNumber",      "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "InquiryReference",   "TEXT NOT NULL DEFAULT 'Customer Inquiry'"),
                ("Inquiries", "CustomerId",         "INTEGER NULL"),
                ("Inquiries", "CustomerName",       "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "CustomerLocation",   "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "ProjectId",          "INTEGER NULL"),
                ("Inquiries", "ProjectName",        "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "ProjectDescription", "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "SalesmanId",         "INTEGER NULL"),
                ("Inquiries", "SalesmanName",       "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "PreparedByUserId",   "INTEGER NULL"),
                ("Inquiries", "PreparedByName",     "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "PreparedByRole",     "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "Status",             "TEXT NOT NULL DEFAULT 'Received'"),
                ("Inquiries", "Priority",           "TEXT NOT NULL DEFAULT 'Normal'"),
                ("Inquiries", "InquiryValue",       "TEXT NULL DEFAULT '0'"),
                ("Inquiries", "Notes",              "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "ReceivedAt",         "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
                ("Inquiries", "PreparedAt",         "TEXT NULL"),
                ("Inquiries", "SentAt",             "TEXT NULL"),
                ("Inquiries", "ConfirmedAt",        "TEXT NULL"),
                ("Inquiries", "CreatedAt",          "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
                ("Inquiries", "UpdatedAt",          "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
                ("Inquiries", "UpdatedBy",          "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "ProformaInvoiceId",  "INTEGER NULL"),
                ("Inquiries", "ProformaNumber",     "TEXT NOT NULL DEFAULT ''"),
                ("Inquiries", "JobOrderNumber",     "TEXT NOT NULL DEFAULT ''"),
                ("InquiryAttachments", "InquiryId",      "INTEGER NOT NULL DEFAULT 0"),
                ("InquiryAttachments", "FileName",       "TEXT NOT NULL DEFAULT ''"),
                ("InquiryAttachments", "Extension",      "TEXT NOT NULL DEFAULT ''"),
                ("InquiryAttachments", "FileSize",       "INTEGER NOT NULL DEFAULT 0"),
                ("InquiryAttachments", "StoragePath",    "TEXT NOT NULL DEFAULT ''"),
                ("InquiryAttachments", "AttachmentType", "TEXT NOT NULL DEFAULT 'Document'"),
                ("InquiryAttachments", "UploadedBy",     "TEXT NOT NULL DEFAULT ''"),
                ("InquiryAttachments", "UploadedAt",     "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
                ("InquiryCommunications", "InquiryId",   "INTEGER NOT NULL DEFAULT 0"),
                ("InquiryCommunications", "EventType",   "TEXT NOT NULL DEFAULT ''"),
                ("InquiryCommunications", "Title",       "TEXT NOT NULL DEFAULT ''"),
                ("InquiryCommunications", "Description", "TEXT NOT NULL DEFAULT ''"),
                ("InquiryCommunications", "OccurredAt",  "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'"),
                ("InquiryCommunications", "ActorUserId", "INTEGER NULL"),
                ("InquiryCommunications", "ActorName",   "TEXT NOT NULL DEFAULT ''"),
            };
            foreach (var (table, column, definition) in columns)
                SchemaMigrationSql.AddColumnIfMissing(connection, table, column, definition);

            // 3) Indexes. Unique-index creation preflights collisions and
            //    fails with representative values. Inquiry numbers are
            //    externally referenced business identifiers, so migrations
            //    never silently renumber them to make an index succeed.
            foreach (string sql in new[]
            {
                "CREATE INDEX IF NOT EXISTS \"IX_Inquiries_ReceivedAt\" ON \"Inquiries\" (\"ReceivedAt\");",
                "CREATE INDEX IF NOT EXISTS \"IX_Inquiries_UpdatedAt\" ON \"Inquiries\" (\"UpdatedAt\");",
                "CREATE INDEX IF NOT EXISTS \"IX_Inquiries_Status\" ON \"Inquiries\" (\"Status\");",
                "CREATE INDEX IF NOT EXISTS \"IX_Inquiries_CustomerId\" ON \"Inquiries\" (\"CustomerId\");",
                "CREATE INDEX IF NOT EXISTS \"IX_Inquiries_SalesmanId\" ON \"Inquiries\" (\"SalesmanId\");",
                "CREATE INDEX IF NOT EXISTS \"IX_Inquiries_PreparedByUserId\" ON \"Inquiries\" (\"PreparedByUserId\");",
                "CREATE INDEX IF NOT EXISTS \"IX_InquiryAttachments_InquiryId\" ON \"InquiryAttachments\" (\"InquiryId\");",
                "CREATE INDEX IF NOT EXISTS \"IX_InquiryAttachments_UploadedAt\" ON \"InquiryAttachments\" (\"UploadedAt\");",
                "CREATE INDEX IF NOT EXISTS \"IX_InquiryCommunications_InquiryId_OccurredAt\" ON \"InquiryCommunications\" (\"InquiryId\", \"OccurredAt\");",
            })
            {
                SchemaMigrationSql.CreateIndexIfNotExists(connection, sql);
            }
            SchemaMigrationSql.CreateIndexIfNotExists(connection,
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Inquiries_InquiryNumber\" ON \"Inquiries\" (\"InquiryNumber\");");
        }

        private static void VerifyInquirySchema(SqliteConnection connection)
        {
            foreach (string column in new[]
            {
                "InquiryNumber", "InquiryReference", "CustomerName", "ProjectName",
                "SalesmanName", "PreparedByName", "ReceivedAt", "InquiryValue"
            })
                SchemaMigrationSql.RequireColumn(connection, "Inquiries", column);

            SchemaMigrationSql.RequireNoEmptyValues(connection, "Inquiries", "InquiryNumber");
            SchemaMigrationSql.RequireNoDuplicateKeyValues(
                connection, "Inquiries", "InquiryNumber");
            SchemaMigrationSql.RequireIndex(connection, "IX_Inquiries_InquiryNumber",
                "Inquiries", unique: true, expectedColumns: new[] { "InquiryNumber" });
            SchemaMigrationSql.RequireIndex(connection, "IX_Inquiries_ReceivedAt",
                "Inquiries", unique: false, expectedColumns: new[] { "ReceivedAt" });
            SchemaMigrationSql.RequireIndex(connection, "IX_Inquiries_UpdatedAt",
                "Inquiries", unique: false, expectedColumns: new[] { "UpdatedAt" });
            SchemaMigrationSql.RequireIndex(connection, "IX_Inquiries_Status",
                "Inquiries", unique: false, expectedColumns: new[] { "Status" });
            SchemaMigrationSql.RequireIndex(connection, "IX_Inquiries_CustomerId",
                "Inquiries", unique: false, expectedColumns: new[] { "CustomerId" });
            SchemaMigrationSql.RequireIndex(connection, "IX_Inquiries_SalesmanId",
                "Inquiries", unique: false, expectedColumns: new[] { "SalesmanId" });
            SchemaMigrationSql.RequireIndex(connection, "IX_Inquiries_PreparedByUserId",
                "Inquiries", unique: false, expectedColumns: new[] { "PreparedByUserId" });
            SchemaMigrationSql.RequireIndex(connection, "IX_InquiryAttachments_InquiryId",
                "InquiryAttachments", unique: false, expectedColumns: new[] { "InquiryId" });
            SchemaMigrationSql.RequireIndex(connection, "IX_InquiryAttachments_UploadedAt",
                "InquiryAttachments", unique: false, expectedColumns: new[] { "UploadedAt" });
            SchemaMigrationSql.RequireIndex(connection, "IX_InquiryCommunications_InquiryId_OccurredAt",
                "InquiryCommunications", unique: false,
                expectedColumns: new[] { "InquiryId", "OccurredAt" });
        }

        /// <summary>
        /// Migration 1002: drop any column that exists in the live
        /// Inquiries table but isn't in the expected model, AND rename
        /// known legacy columns to their current names so user data isn't
        /// lost. Interim builds (before 1001) shipped a different
        /// InquiryRecord shape — most notably a required NOT NULL column
        /// called "InquiryDate" that was later renamed to "ReceivedAt",
        /// and a "Salesman" column later renamed to "SalesmanName".
        ///
        /// Runs after 1001 and is idempotent: a fresh database created in
        /// step 1 has only the expected columns, so the rename / drop
        /// passes are both no-ops.
        /// </summary>
        private static void DropLegacyInquiriesColumns(SqliteConnection connection)
        {
            // 1) Move known legacy values into the current columns before
            //    removing old names. If both columns exist, non-empty
            //    conflicts abort this transaction instead of silently
            //    discarding either value; blank/default target values are
            //    safely backfilled.
            var renames = new (string OldName, string NewName)[]
            {
                ("InquiryDate", "ReceivedAt"),
                ("Salesman",    "SalesmanName"),
                ("Customer",    "CustomerName"),
                ("Project",     "ProjectName"),
                ("PreparedBy",  "PreparedByName"),
            };
            foreach (var (oldName, newName) in renames)
                MergeOrRenameLegacyColumn(connection, "Inquiries", oldName, newName);

            // 2) A remaining unknown legacy column can be dropped only when
            //    it carries no non-empty values. Otherwise the operator
            //    must map/export it first; the migration never destroys
            //    unknown business data to make a schema converge.
            string[] expectedInquiriesColumns =
            {
                "Id", "InquiryNumber", "InquiryReference", "CustomerId", "CustomerName", "CustomerLocation",
                "ProjectId", "ProjectName", "ProjectDescription", "SalesmanId", "SalesmanName",
                "PreparedByUserId", "PreparedByName", "PreparedByRole", "Status", "Priority",
                "InquiryValue", "Notes", "ReceivedAt", "PreparedAt", "SentAt", "ConfirmedAt",
                "CreatedAt", "UpdatedAt", "UpdatedBy", "ProformaInvoiceId", "ProformaNumber",
                "JobOrderNumber"
            };
            var liveColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA table_info(\"Inquiries\");";
                using var reader = pragma.ExecuteReader();
                while (reader.Read()) liveColumns.Add(reader.GetString(1));
            }
            var expectedSet = new HashSet<string>(expectedInquiriesColumns, StringComparer.OrdinalIgnoreCase);
            var orphans = liveColumns.Where(c => !expectedSet.Contains(c)).ToList();
            foreach (var orphan in orphans)
                DropEmptyLegacyColumn(connection, "Inquiries", orphan);

            foreach (string requiredColumn in expectedInquiriesColumns)
                SchemaMigrationSql.RequireColumn(connection, "Inquiries", requiredColumn);
        }

        /// <summary>
        /// Renames an old column when the current name is absent; when both
        /// names exist, copies old non-empty values into blank/default
        /// target cells and refuses to proceed on conflicting values. The
        /// old column is dropped only after a post-copy equality check.
        /// </summary>
        private static void MergeOrRenameLegacyColumn(
            SqliteConnection connection, string table, string oldName, string newName)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info(\"{table}\");";
                using var reader = command.ExecuteReader();
                while (reader.Read()) columns.Add(reader.GetString(1));
            }

            bool hasOld = columns.Contains(oldName);
            bool hasNew = columns.Contains(newName);
            if (!hasOld) return;
            if (!hasNew)
            {
                SchemaMigrationSql.Execute(connection,
                    $"ALTER TABLE \"{table}\" RENAME COLUMN \"{oldName}\" TO \"{newName}\";");
                return;
            }

            string oldValue = $"CAST(\"{oldName}\" AS TEXT)";
            string newValue = $"CAST(\"{newName}\" AS TEXT)";
            string oldHasValue = $"\"{oldName}\" IS NOT NULL AND TRIM({oldValue}) <> ''";
            string newHasValue = $"\"{newName}\" IS NOT NULL AND TRIM({newValue}) <> ''";
            string isDefaultDate = $"\"{newName}\" = '0001-01-01 00:00:00'";

            using (var conflict = connection.CreateCommand())
            {
                conflict.CommandText =
                    $"SELECT COUNT(*) FROM \"{table}\" WHERE {oldHasValue} AND {newHasValue} " +
                    $"AND NOT ({isDefaultDate}) AND {oldValue} <> {newValue};";
                long count = Convert.ToInt64(conflict.ExecuteScalar() ?? 0,
                    CultureInfo.InvariantCulture);
                if (count != 0)
                    throw new InvalidOperationException(
                        $"Legacy column '{table}.{oldName}' conflicts with '{newName}' in {count} row(s). " +
                        "No source values were dropped; resolve the conflicts and retry the migration.");
            }

            using (var copy = connection.CreateCommand())
            {
                copy.CommandText =
                    $"UPDATE \"{table}\" SET \"{newName}\" = \"{oldName}\" " +
                    $"WHERE {oldHasValue} AND (\"{newName}\" IS NULL OR TRIM({newValue}) = '' OR {isDefaultDate});";
                copy.ExecuteNonQuery();
            }

            using (var verify = connection.CreateCommand())
            {
                verify.CommandText =
                    $"SELECT COUNT(*) FROM \"{table}\" WHERE {oldHasValue} " +
                    $"AND (\"{newName}\" IS NULL OR TRIM({newValue}) = '' OR {newValue} <> {oldValue});";
                long unpreserved = Convert.ToInt64(verify.ExecuteScalar() ?? 0,
                    CultureInfo.InvariantCulture);
                if (unpreserved != 0)
                    throw new InvalidOperationException(
                        $"Could not verify preservation of {unpreserved} value(s) from '{table}.{oldName}'.");
            }

            SchemaMigrationSql.DropColumnIfExists(
                connection, table, oldName, dataPreservationVerified: true);
        }

        /// <summary>
        /// Drops an unknown legacy column only when all values are NULL or
        /// whitespace. Non-empty values abort the migration for manual
        /// preservation; failures are never swallowed.
        /// </summary>
        private static void DropEmptyLegacyColumn(
            SqliteConnection connection, string table, string column)
        {
            string quotedTable = $"\"{table}\"";
            string quotedColumn = $"\"{column}\"";
            using (var check = connection.CreateCommand())
            {
                check.CommandText =
                    $"SELECT COUNT(*) FROM {quotedTable} WHERE {quotedColumn} IS NOT NULL " +
                    $"AND TRIM(CAST({quotedColumn} AS TEXT)) <> '';";
                long count = Convert.ToInt64(check.ExecuteScalar() ?? 0,
                    CultureInfo.InvariantCulture);
                if (count != 0)
                    throw new InvalidOperationException(
                        $"Unknown legacy column '{table}.{column}' contains {count} non-empty value(s). " +
                        "The migration preserved the column and aborted; export or map these values before retrying.");
            }

            SchemaMigrationSql.DropColumnIfExists(
                connection, table, column, dataPreservationVerified: true);
        }
    }
}
