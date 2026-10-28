// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Database/SchemaMigrations/AppSchemaMigrations.LegacyColumns.cs
// MODULE: Core — legacy inquiry-column convergence.
//
// Split out of AppSchemaMigrations so that file stays a register of migrations
// rather than a register plus the column surgery. This is the only code in the
// chain that DROPS or RENAMES anything, which is why it is worth keeping apart
// from the additive steps: every destructive operation is now in one place.
//
// Behaviour is unchanged — same methods, same order, same guards.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ProGlassAutomation.Services.Database.SchemaMigrations
{
    public static partial class AppSchemaMigrations
    {
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
