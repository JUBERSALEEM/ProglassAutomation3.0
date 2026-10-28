// ═══════════════════════════════════════════════════════════════════════
// FILE: step4.CompanyProfile/step4.b.1.CompanyProfileSchema.cs
// MODULE: step4 — the CompanyProfile table on the existing ERP file.
//
// One file ⇒ one PRAGMA user_version ⇒ one migration chain. The profile is
// migration **1016** on the chain that already carries 1011 (Clients), 1013
// (Users), 1014 (client representative) and 1015 (credentials). No second
// database, no second runner, no inline DDL from the repository.
//
// The single-profile invariant is a CONSTRAINT, not a convention
//   "Id" is a fixed primary key with a table CHECK that it equals 1. A caller
//   that tries to insert a second company does not get a validation message it
//   could ignore: SQLite refuses the insert. Hiding a New button would not
//   survive a direct repository call or a future module; this does.
//
// Additive and safe
//   The migration creates a table that did not exist, so there is nothing to
//   rewrite: inquiry, client and user rows are untouched, and re-running it is
//   a no-op (CREATE TABLE IF NOT EXISTS, no seed row). A fresh profile row is
//   created by the first save, not by the migration, so "never saved" stays
//   distinguishable from "saved empty".
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using ProGlassAutomation.Models.Company;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProGlassAutomation.Services.Company
{
    /// <summary>DDL and shape assertions for the CompanyProfile table.</summary>
    public static class CompanyProfileSchema
    {
        public const string TableName = "CompanyProfile";

        /// <summary>Migration version that introduces the company profile.</summary>
        public const int MigrationVersion = 1016;

        // SQL literals, written with the invariant culture rather than letting
        // string interpolation pick up the operator's number format: a migration
        // that emitted "١" or "24,0" would produce a table SQLite cannot read.
        private static readonly string SingleIdSql =
            CompanyProfile.SingleId.ToString(CultureInfo.InvariantCulture);
        private static readonly string LayoutNoneSql =
            ((int)CompanyLogoLayout.None).ToString(CultureInfo.InvariantCulture);
        private static readonly string LayoutPortraitSql =
            ((int)CompanyLogoLayout.LeftPortrait).ToString(CultureInfo.InvariantCulture);
        private static readonly string LayoutTopPanelSql =
            ((int)CompanyLogoLayout.TopPanel).ToString(CultureInfo.InvariantCulture);
        private static readonly string DefaultLogoSizeSql =
            CompanyProfileRules.DefaultLogoSizeMm.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Every column the repository reads or writes.</summary>
        public static readonly string[] RequiredColumns =
        {
            "Id", "CompanyName", "Trn", "Address", "Telephone", "Website",
            "RepresentativeName", "RepresentativeMobile", "Email",
            "BankName", "AccountNumber",
            "LogoFileName", "LogoLayout", "LogoSizeMm",
            "CreatedAt", "UpdatedAt", "CreatedByUserId", "UpdatedByUserId"
        };

        /// <summary>Columns that may never be NULL once the table exists.</summary>
        public static readonly string[] NonNullColumns =
        {
            "Id", "CompanyName", "Trn", "Address", "Telephone", "Website",
            "RepresentativeName", "RepresentativeMobile", "Email",
            "BankName", "AccountNumber", "LogoFileName", "LogoLayout", "LogoSizeMm",
            "CreatedAt", "UpdatedAt"
        };

        /// <summary>Creates the table when it is missing. Idempotent.</summary>
        public static void Ensure(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            SchemaMigrationSql.CreateTableIfNotExists(connection, $@"
                CREATE TABLE IF NOT EXISTS ""{TableName}"" (
                    ""Id""                   INTEGER NOT NULL CONSTRAINT ""PK_CompanyProfile"" PRIMARY KEY,
                    ""CompanyName""          TEXT    NOT NULL DEFAULT '',
                    ""Trn""                  TEXT    NOT NULL DEFAULT '',
                    ""Address""              TEXT    NOT NULL DEFAULT '',
                    ""Telephone""            TEXT    NOT NULL DEFAULT '',
                    ""Website""              TEXT    NOT NULL DEFAULT '',
                    ""RepresentativeName""   TEXT    NOT NULL DEFAULT '',
                    ""RepresentativeMobile"" TEXT    NOT NULL DEFAULT '',
                    ""Email""                TEXT    NOT NULL DEFAULT '',
                    ""BankName""             TEXT    NOT NULL DEFAULT '',
                    ""AccountNumber""        TEXT    NOT NULL DEFAULT '',
                    ""LogoFileName""         TEXT    NOT NULL DEFAULT '',
                    ""LogoLayout""           INTEGER NOT NULL DEFAULT 0,
                    ""LogoSizeMm""           REAL    NOT NULL DEFAULT {DefaultLogoSizeSql},
                    ""CreatedAt""            TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00',
                    ""UpdatedAt""            TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00',
                    ""CreatedByUserId""      INTEGER NULL,
                    ""UpdatedByUserId""      INTEGER NULL,
                    CONSTRAINT ""CK_CompanyProfile_Single"" CHECK (""Id"" = {SingleIdSql}),
                    CONSTRAINT ""CK_CompanyProfile_Layout"" CHECK (""LogoLayout"" IN ({LayoutNoneSql}, {LayoutPortraitSql}, {LayoutTopPanelSql}))
                );");

            // A lookup from the profile to the person who last saved it is always
            // "the one row", so there is nothing to index beyond the primary key.
            // The table deliberately carries no AUTOINCREMENT: the id is fixed.
        }

        /// <summary>
        /// Post-migration assertions. Run by the migration runner after the whole
        /// pending set has applied, so it must describe the FINAL shape.
        /// </summary>
        public static void AssertShape(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            foreach (string column in RequiredColumns)
                SchemaMigrationSql.RequireColumn(connection, TableName, column);

            foreach (string column in NonNullColumns)
                SchemaMigrationSql.RequireNoNullValues(connection, TableName, column);

            // The invariant, written twice on purpose: the CHECK constraint proves
            // it on every future insert, and this proves the table that exists is
            // the one that carries the constraint.
            long rows = ScalarLong(connection, $@"SELECT COUNT(*) FROM ""{TableName}"";");
            if (rows > 1)
                throw new InvalidOperationException(
                    $"CompanyProfile must hold at most one row, but holds {rows}. This database was "
                    + "written by something that bypassed the single-profile constraint; export the "
                    + "extra rows before retrying.");

            long offCentre = ScalarLong(connection,
                $@"SELECT COUNT(*) FROM ""{TableName}"" WHERE ""Id"" <> {CompanyProfile.SingleId};");
            if (offCentre != 0)
                throw new InvalidOperationException(
                    $"CompanyProfile must use Id = {CompanyProfile.SingleId}, but {offCentre} row(s) do not.");

            long badLayout = ScalarLong(connection,
                $@"SELECT COUNT(*) FROM ""{TableName}"" WHERE ""LogoLayout"" NOT IN (0, 1, 2);");
            if (badLayout != 0)
                throw new InvalidOperationException(
                    $"{badLayout} company profile row(s) carry a logo layout the module does not define.");

            long badSize = ScalarLong(connection,
                $@"SELECT COUNT(*) FROM ""{TableName}"" WHERE ""LogoSizeMm"" <= 0;");
            if (badSize != 0)
                throw new InvalidOperationException(
                    $"{badSize} company profile row(s) carry a non-positive logo size.");
        }

        private static long ScalarLong(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Column order, used by diagnostics and by the verification executable to
        /// prove the table matches the model rather than a stale copy of it.
        /// </summary>
        public static IReadOnlyList<string> Columns(SqliteConnection connection)
        {
            var columns = new List<string>();
            using var command = connection.CreateCommand();
            command.CommandText = $@"PRAGMA table_info(""{TableName}"");";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (!reader.IsDBNull(1)) columns.Add(reader.GetString(1));
            return columns;
        }
    }
}
