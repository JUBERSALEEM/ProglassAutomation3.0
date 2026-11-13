// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.b.1.ProformaInvoiceSchema.cs
// MODULE: step6 — migration 1018: the two tables a proforma invoice owns.
//
// One database file ⇒ one PRAGMA user_version ⇒ exactly one chain. This
// migration is appended after 1017 on the chain that already carries the
// inquiry, client, user, company and project tables. There is no second
// database, no second runner and no inline DDL from a repository.
//
// Purely additive, and it cannot rewrite anything that exists:
//   • two CREATE TABLE IF NOT EXISTS statements (invoice + attachments);
//   • indexes on the invoice's searchable columns;
//   • no ALTER on Inquiries, Clients, Users, CompanyProfile or Projects;
//   • no seed row, so "never saved" stays distinguishable from "saved empty".
//
// The invoice NUMBER is unique at the database level. The repository also
// checks before it writes, but a race between two writers must not be able to
// produce two PI-2026-001s: hiding a Save button would enforce nothing.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>DDL and shape assertions for the proforma invoice tables.</summary>
    public static class ProformaInvoiceSchema
    {
        public const string TableName = "ProformaInvoices";
        public const string AttachmentTableName = "ProformaInvoiceAttachments";

        /// <summary>Migration that introduces the invoice tables.</summary>
        public const int MigrationVersion = 1018;

        /// <summary>Every column the repository reads or writes on the invoice.</summary>
        public static readonly string[] RequiredColumns =
        {
            "Id", "ProformaNumber", "Status", "IssueDate", "ValidUntil",
            "ClientId", "ClientName", "ProjectId", "ProjectName",
            "InquiryId", "InquiryNumber", "PreparedByUserId", "PreparedByName",
            "SalesRepresentativeUserId", "SalesRepresentativeName",
            "NetSubtotal", "OtherChargesSubtotal", "DiscountAmount", "VatAmount",
            "GrandTotal", "TotalCost", "Profit", "SnapshotJson", "Notes",
            "RowVersion", "CreatedAtUtc", "UpdatedAtUtc",
            "CreatedByUserId", "CreatedByName", "UpdatedByUserId", "UpdatedByName"
        };

        /// <summary>Columns that may never be NULL once the tables exist.</summary>
        public static readonly string[] NonNullColumns =
        {
            "Id", "ProformaNumber", "Status", "IssueDate", "ValidUntil",
            "ClientName", "ProjectName", "InquiryNumber", "PreparedByName",
            "SalesRepresentativeName", "NetSubtotal", "OtherChargesSubtotal",
            "DiscountAmount", "VatAmount", "GrandTotal", "TotalCost", "Profit",
            "SnapshotJson", "Notes", "RowVersion", "CreatedAtUtc", "UpdatedAtUtc",
            "CreatedByName", "UpdatedByName"
        };

        /// <summary>Attachment columns the store reads or writes.</summary>
        public static readonly string[] AttachmentColumns =
        {
            "Id", "ProformaInvoiceId", "FileName", "StoredFileName", "Extension",
            "FileSize", "AttachmentType", "Note", "UploadedAtUtc",
            "UploadedByUserId", "UploadedByName"
        };

        private static string StatusValuesSql() =>
            string.Join(", ", ProformaInvoiceStatus.All.Select(
                status => "'" + status.Replace("'", "''") + "'"));

        /// <summary>Creates both tables and their indexes. Idempotent.</summary>
        public static void Ensure(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            SchemaMigrationSql.CreateTableIfNotExists(connection, $@"
                CREATE TABLE IF NOT EXISTS ""{TableName}"" (
                    ""Id""                INTEGER NOT NULL CONSTRAINT ""PK_{TableName}"" PRIMARY KEY AUTOINCREMENT,
                    ""ProformaNumber""    TEXT    NOT NULL DEFAULT '',
                    ""Status""            TEXT    NOT NULL DEFAULT '{ProformaInvoiceStatus.Draft}',
                    ""IssueDate""         TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00',
                    ""ValidUntil""        TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00',
                    ""ClientId""          INTEGER NULL,
                    ""ClientName""        TEXT    NOT NULL DEFAULT '',
                    ""ProjectId""         INTEGER NULL,
                    ""ProjectName""       TEXT    NOT NULL DEFAULT '',
                    ""InquiryId""         INTEGER NULL,
                    ""InquiryNumber""     TEXT    NOT NULL DEFAULT '',
                    ""PreparedByUserId""  INTEGER NULL,
                    ""PreparedByName""    TEXT    NOT NULL DEFAULT '',
                    ""SalesRepresentativeUserId"" INTEGER NULL,
                    ""SalesRepresentativeName""   TEXT    NOT NULL DEFAULT '',
                    ""NetSubtotal""       TEXT    NOT NULL DEFAULT '0',
                    ""OtherChargesSubtotal"" TEXT NOT NULL DEFAULT '0',
                    ""DiscountAmount""    TEXT    NOT NULL DEFAULT '0',
                    ""VatAmount""         TEXT    NOT NULL DEFAULT '0',
                    ""GrandTotal""        TEXT    NOT NULL DEFAULT '0',
                    ""TotalCost""         TEXT    NOT NULL DEFAULT '0',
                    ""Profit""            TEXT    NOT NULL DEFAULT '0',
                    ""SnapshotJson""      TEXT    NOT NULL DEFAULT '{{}}',
                    ""Notes""             TEXT    NOT NULL DEFAULT '',
                    ""RowVersion""        INTEGER NOT NULL DEFAULT 0,
                    ""CreatedAtUtc""      TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00',
                    ""UpdatedAtUtc""      TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00',
                    ""CreatedByUserId""   INTEGER NULL,
                    ""CreatedByName""     TEXT    NOT NULL DEFAULT '',
                    ""UpdatedByUserId""   INTEGER NULL,
                    ""UpdatedByName""     TEXT    NOT NULL DEFAULT '',
                    CONSTRAINT ""CK_{TableName}_Status"" CHECK (""Status"" IN ({StatusValuesSql()}))
                );");

            SchemaMigrationSql.CreateTableIfNotExists(connection, $@"
                CREATE TABLE IF NOT EXISTS ""{AttachmentTableName}"" (
                    ""Id""                INTEGER NOT NULL CONSTRAINT ""PK_{AttachmentTableName}"" PRIMARY KEY AUTOINCREMENT,
                    ""ProformaInvoiceId"" INTEGER NOT NULL,
                    ""FileName""          TEXT    NOT NULL DEFAULT '',
                    ""StoredFileName""    TEXT    NOT NULL DEFAULT '',
                    ""Extension""         TEXT    NOT NULL DEFAULT '',
                    ""FileSize""          INTEGER NOT NULL DEFAULT 0,
                    ""AttachmentType""    TEXT    NOT NULL DEFAULT 'Document',
                    ""Note""              TEXT    NOT NULL DEFAULT '',
                    ""UploadedAtUtc""     TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00',
                    ""UploadedByUserId""  INTEGER NULL,
                    ""UploadedByName""    TEXT    NOT NULL DEFAULT '',
                    CONSTRAINT ""FK_{AttachmentTableName}_{TableName}""
                        FOREIGN KEY (""ProformaInvoiceId"") REFERENCES ""{TableName}"" (""Id"")
                        ON DELETE CASCADE
                );");

            foreach (string sql in new[]
            {
                $"CREATE UNIQUE INDEX IF NOT EXISTS \"IX_{TableName}_Number\" ON \"{TableName}\" (\"ProformaNumber\");",
                $"CREATE INDEX IF NOT EXISTS \"IX_{TableName}_Status\" ON \"{TableName}\" (\"Status\");",
                $"CREATE INDEX IF NOT EXISTS \"IX_{TableName}_ClientId\" ON \"{TableName}\" (\"ClientId\");",
                $"CREATE INDEX IF NOT EXISTS \"IX_{TableName}_ProjectId\" ON \"{TableName}\" (\"ProjectId\");",
                $"CREATE INDEX IF NOT EXISTS \"IX_{TableName}_InquiryId\" ON \"{TableName}\" (\"InquiryId\");",
                $"CREATE INDEX IF NOT EXISTS \"IX_{TableName}_IssueDate\" ON \"{TableName}\" (\"IssueDate\");",
                $"CREATE INDEX IF NOT EXISTS \"IX_{TableName}_UpdatedAtUtc\" ON \"{TableName}\" (\"UpdatedAtUtc\");",
                $"CREATE INDEX IF NOT EXISTS \"IX_{AttachmentTableName}_Invoice\" ON \"{AttachmentTableName}\" (\"ProformaInvoiceId\");"
            })
            {
                SchemaMigrationSql.CreateIndexIfNotExists(connection, sql);
            }
        }

        /// <summary>
        /// Post-migration assertions. The runner re-runs this after the whole
        /// pending set has applied, so it describes the FINAL shape.
        /// </summary>
        public static void AssertShape(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            foreach (string column in RequiredColumns)
                SchemaMigrationSql.RequireColumn(connection, TableName, column);
            foreach (string column in AttachmentColumns)
                SchemaMigrationSql.RequireColumn(connection, AttachmentTableName, column);
            foreach (string column in NonNullColumns)
                SchemaMigrationSql.RequireNoNullValues(connection, TableName, column);

            SchemaMigrationSql.RequireNoDuplicateKeyValues(connection, TableName, "ProformaNumber");
            SchemaMigrationSql.RequireIndex(connection, $"IX_{TableName}_Number", TableName,
                unique: true, expectedColumns: new[] { "ProformaNumber" });
            SchemaMigrationSql.RequireIndex(connection, $"IX_{TableName}_Status", TableName,
                unique: false, expectedColumns: new[] { "Status" });
            SchemaMigrationSql.RequireIndex(connection, $"IX_{TableName}_InquiryId", TableName,
                unique: false, expectedColumns: new[] { "InquiryId" });
            SchemaMigrationSql.RequireIndex(connection, $"IX_{AttachmentTableName}_Invoice",
                AttachmentTableName, unique: false,
                expectedColumns: new[] { "ProformaInvoiceId" });

            RequireKnownStatuses(connection);
        }

        /// <summary>
        /// Fails the migration when a stored status is outside the vocabulary.
        /// A value nobody can interpret is not something to keep quietly.
        /// </summary>
        private static void RequireKnownStatuses(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT DISTINCT \"Status\" FROM \"{TableName}\";";
            var unknown = new List<string>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string value = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    if (!ProformaInvoiceStatus.IsDefined(value)) unknown.Add(value);
                }
            }

            if (unknown.Count > 0)
                throw new InvalidOperationException(
                    $"Migration {MigrationVersion}: {unknown.Count} invoice row(s) carry an unknown status: " +
                    string.Join(", ", unknown) + ". Repair them before upgrading.");
        }

        /// <summary>
        /// The next free document number for a year, derived from the HIGHEST
        /// stored sequence for that year — never from a row count, which reuses
        /// a cancelled document's number.
        /// </summary>
        public static string NextNumber(SqliteConnection connection, int year)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            string prefix = "PI-" + year.ToString("0000", CultureInfo.InvariantCulture) + "-";

            using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT \"ProformaNumber\" FROM \"{TableName}\" WHERE \"ProformaNumber\" LIKE @prefix;";
            command.Parameters.AddWithValue("@prefix", prefix + "%");

            int highest = 0;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    string number = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    if (number.Length == 0) continue;
                    used.Add(number);
                    string tail = number.Substring(prefix.Length);
                    if (int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out int value) &&
                        value > highest)
                        highest = value;
                }
            }

            // Skip any number that exists even if it is above the highest parsed
            // one (a hand-edited database must not produce a duplicate).
            for (int candidate = highest + 1; candidate < highest + 100000; candidate++)
            {
                string text = prefix + candidate.ToString("000", CultureInfo.InvariantCulture);
                if (!used.Contains(text)) return text;
            }

            throw new InvalidOperationException(
                $"No free proforma number remains for {year} in the current sequence block.");
        }
    }
}
