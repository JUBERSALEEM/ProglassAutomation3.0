// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.b.1.UserSchema.cs
// MODULE: step3 — Users, Departments and Roles DDL, seed and shape checks.
//
// The user directory lives in the existing inquiries.db on the existing single
// version chain (see AppSchemaMigrations). One database file ⇒ one
// PRAGMA user_version ⇒ exactly one chain; this file adds migration 1013 and
// never creates a second chain or a second EnsureCreated owner.
//
//   • 1013 creates three tables: Users, Departments, Roles. Nothing that
//     already exists is dropped, renamed or rewritten, so Clients and Inquiries
//     data and ids are untouched.
//   • The two catalogues are seeded with the owner's initial values using
//     INSERT OR IGNORE: a re-run never overwrites a display name an
//     administrator has since reworded, and never duplicates a code.
//   • Users carries no password column. Not "empty", not "to be filled later" —
//     absent, because the application has no credentialed login (step3.a.1).
//
// Shape assertions (Verify) run after the whole pending set applies:
//   • every expected column exists and is NOT NULL where the model requires it;
//   • NormalizedUsername has no duplicate and no empty values (the unique
//     index would fail on either, and a user without an identity key is
//     unusable);
//   • every stored Department / Role value resolves to a catalogue row, so a
//     hand-edited database is caught at startup rather than at report time;
//   • exactly one role carries IsDefault, and it is the role new users start
//     with.
//
// CreatedByUserId / UpdatedByUserId are deliberately nullable integer columns
// holding the stable id of the administrator who granted or changed access.
// They are pointers, not copies of a name, so a rename elsewhere does not
// rewrite the audit trail. SQLite has no enforceable self-referencing delete
// rule worth the friction on a single-file desktop database, so the
// assertion below checks every non-null value resolves to a real user row
// instead of adding a constraint that could block a legitimate repair.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProGlassAutomation.Services.Users
{
    /// <summary>DDL, seed and shape checks for Users, Departments and Roles.</summary>
    public static class UserSchema
    {
        public const string UsersTable = "Users";
        public const string DepartmentsTable = "Departments";
        public const string RolesTable = "Roles";

        /// <summary>Migration version that introduces the user directory.</summary>
        public const int MigrationVersion = 1013;

        /// <summary>
        /// Migration version that adds the credential columns (hash, salt, work
        /// factor, must-change flag and lockout counters).
        /// </summary>
        public const int CredentialsMigrationVersion = 1015;

        /// <summary>Credential columns, added by 1015 on top of <see cref="RequiredColumns"/>.</summary>
        public static readonly string[] CredentialColumns =
        {
            "PasswordHash", "PasswordSalt", "PasswordIterations", "PasswordUpdatedAt",
            "MustChangePassword", "FailedSignInCount", "LockedUntilUtc"
        };

        /// <summary>Credential columns that must never be NULL once 1015 has run.</summary>
        public static readonly string[] CredentialNonNullColumns =
        {
            "PasswordHash", "PasswordSalt", "PasswordIterations",
            "MustChangePassword", "FailedSignInCount"
        };

        /// <summary>Columns the EF model in step3.b.3 maps today.</summary>
        public static readonly string[] RequiredColumns =
        {
            "Id", "FullName", "Username", "NormalizedUsername", "Department",
            "Role", "IsActive", "CreatedAt", "UpdatedAt",
            "CreatedByUserId", "UpdatedByUserId"
        };

        /// <summary>Columns that must never be empty on a stored user.</summary>
        public static readonly string[] RequiredNonEmptyColumns =
        {
            "FullName", "Username", "NormalizedUsername", "Department", "Role"
        };

        /// <summary>Creates the three tables, their indexes and the catalogue seed. Idempotent.</summary>
        public static void Ensure(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            CreateCatalogTables(connection);
            CreateUsersTable(connection);
            SeedCatalogues(connection);
            CreateIndexes(connection);
        }

        private static void CreateCatalogTables(SqliteConnection connection)
        {
            // Codes are the stable identifiers; display names are relabelable.
            // SortOrder keeps the shipped order in the combo boxes while still
            // allowing an administrator-added row to be placed deliberately.
            SchemaMigrationSql.CreateTableIfNotExists(connection, $@"
                CREATE TABLE IF NOT EXISTS ""{DepartmentsTable}"" (
                    ""Code""        TEXT NOT NULL CONSTRAINT ""PK_Departments"" PRIMARY KEY,
                    ""DisplayName"" TEXT NOT NULL DEFAULT '',
                    ""SortOrder""   INTEGER NOT NULL DEFAULT 0
                );");

            SchemaMigrationSql.CreateTableIfNotExists(connection, $@"
                CREATE TABLE IF NOT EXISTS ""{RolesTable}"" (
                    ""Code""        TEXT NOT NULL CONSTRAINT ""PK_Roles"" PRIMARY KEY,
                    ""DisplayName"" TEXT NOT NULL DEFAULT '',
                    ""SortOrder""   INTEGER NOT NULL DEFAULT 0,
                    ""IsDefault""   INTEGER NOT NULL DEFAULT 0
                );");

            SchemaMigrationSql.AddColumnIfMissing(connection, DepartmentsTable, "DisplayName", "TEXT NOT NULL DEFAULT ''");
            SchemaMigrationSql.AddColumnIfMissing(connection, DepartmentsTable, "SortOrder", "INTEGER NOT NULL DEFAULT 0");
            SchemaMigrationSql.AddColumnIfMissing(connection, RolesTable, "DisplayName", "TEXT NOT NULL DEFAULT ''");
            SchemaMigrationSql.AddColumnIfMissing(connection, RolesTable, "SortOrder", "INTEGER NOT NULL DEFAULT 0");
            SchemaMigrationSql.AddColumnIfMissing(connection, RolesTable, "IsDefault", "INTEGER NOT NULL DEFAULT 0");
        }

        private static void CreateUsersTable(SqliteConnection connection)
        {
            SchemaMigrationSql.CreateTableIfNotExists(connection, $@"
                CREATE TABLE IF NOT EXISTS ""{UsersTable}"" (
                    ""Id""                 INTEGER NOT NULL CONSTRAINT ""PK_Users"" PRIMARY KEY AUTOINCREMENT,
                    ""FullName""           TEXT NOT NULL DEFAULT '',
                    ""Username""           TEXT NOT NULL DEFAULT '',
                    ""NormalizedUsername"" TEXT NOT NULL DEFAULT '',
                    ""Department""         TEXT NOT NULL DEFAULT '{UserCatalog.DefaultDepartment}',
                    ""Role""               TEXT NOT NULL DEFAULT '{UserCatalog.DefaultRole}',
                    ""IsActive""           INTEGER NOT NULL DEFAULT 1,
                    ""CreatedAt""          TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    ""UpdatedAt""          TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    ""CreatedByUserId""    INTEGER NULL,
                    ""UpdatedByUserId""    INTEGER NULL
                );");

            // Defensive convergence for an interrupted install: add any missing
            // column instead of failing the whole application at startup.
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "FullName", "TEXT NOT NULL DEFAULT ''");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "Username", "TEXT NOT NULL DEFAULT ''");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "NormalizedUsername", "TEXT NOT NULL DEFAULT ''");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "Department", $"TEXT NOT NULL DEFAULT '{UserCatalog.DefaultDepartment}'");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "Role", $"TEXT NOT NULL DEFAULT '{UserCatalog.DefaultRole}'");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "IsActive", "INTEGER NOT NULL DEFAULT 1");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "CreatedAt", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "UpdatedAt", "TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "CreatedByUserId", "INTEGER NULL");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "UpdatedByUserId", "INTEGER NULL");
        }

        /// <summary>
        /// Seeds the owner's initial departments and roles. INSERT OR IGNORE keeps
        /// this idempotent and keeps any later administrative wording change.
        /// </summary>
        public static void SeedCatalogues(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            for (int index = 0; index < UserCatalog.Departments.Count; index++)
            {
                CatalogEntry entry = UserCatalog.Departments[index];
                SchemaMigrationSql.Execute(connection,
                    $"INSERT OR IGNORE INTO \"{DepartmentsTable}\" (\"Code\", \"DisplayName\", \"SortOrder\") " +
                    $"VALUES ({Literal(entry.Code)}, {Literal(entry.DisplayName)}, {index});");
            }

            for (int index = 0; index < UserCatalog.Roles.Count; index++)
            {
                CatalogEntry entry = UserCatalog.Roles[index];
                SchemaMigrationSql.Execute(connection,
                    $"INSERT OR IGNORE INTO \"{RolesTable}\" (\"Code\", \"DisplayName\", \"SortOrder\", \"IsDefault\") " +
                    $"VALUES ({Literal(entry.Code)}, {Literal(entry.DisplayName)}, {index}, {(entry.IsDefault ? 1 : 0)});");
            }

            // Exactly one default role: clear any stray flag left by a
            // hand-edited database, then ensure Estimator is the default.
            SchemaMigrationSql.Execute(connection,
                $"UPDATE \"{RolesTable}\" SET \"IsDefault\" = 0 WHERE \"IsDefault\" <> 0 " +
                $"AND \"Code\" <> {Literal(UserCatalog.DefaultRole)};");
            SchemaMigrationSql.Execute(connection,
                $"UPDATE \"{RolesTable}\" SET \"IsDefault\" = 1 WHERE \"Code\" = {Literal(UserCatalog.DefaultRole)};");
        }

        /// <summary>
        /// Username is unique case-insensitively; the remaining indexes serve the
        /// register's filters (status, department, role).
        /// </summary>
        public static void CreateIndexes(SqliteConnection connection)
        {
            SchemaMigrationSql.CreateIndexIfNotExists(connection,
                $"CREATE INDEX IF NOT EXISTS \"IX_Users_FullName\" ON \"{UsersTable}\" (\"FullName\");");
            SchemaMigrationSql.CreateIndexIfNotExists(connection,
                $"CREATE INDEX IF NOT EXISTS \"IX_Users_Department\" ON \"{UsersTable}\" (\"Department\");");
            SchemaMigrationSql.CreateIndexIfNotExists(connection,
                $"CREATE INDEX IF NOT EXISTS \"IX_Users_Role\" ON \"{UsersTable}\" (\"Role\");");
            SchemaMigrationSql.CreateIndexIfNotExists(connection,
                $"CREATE INDEX IF NOT EXISTS \"IX_Users_IsActive_FullName\" ON \"{UsersTable}\" (\"IsActive\", \"FullName\");");

            // The unique index is created last, and only after the duplicate
            // check, so a pre-existing bad row aborts with a readable message
            // rather than a bare SQLite constraint error.
            SchemaMigrationSql.RequireNoDuplicateKeyValues(connection, UsersTable, "NormalizedUsername");
            SchemaMigrationSql.CreateIndexIfNotExists(connection,
                $"CREATE UNIQUE INDEX IF NOT EXISTS \"UX_Users_NormalizedUsername\" " +
                $"ON \"{UsersTable}\" (\"NormalizedUsername\");");
        }

        /// <summary>
        /// Adds the credential columns. Each one is nullable or defaulted, so a
        /// directory created before 1015 keeps every row: an existing user simply
        /// has no credential until an administrator sets one — the sign-in screen
        /// says that, rather than pretending the account works.
        /// </summary>
        public static void EnsureCredentials(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "PasswordHash", "TEXT NOT NULL DEFAULT ''");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "PasswordSalt", "TEXT NOT NULL DEFAULT ''");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "PasswordIterations", "INTEGER NOT NULL DEFAULT 0");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "PasswordUpdatedAt", "TEXT NULL");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "MustChangePassword", "INTEGER NOT NULL DEFAULT 0");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "FailedSignInCount", "INTEGER NOT NULL DEFAULT 0");
            SchemaMigrationSql.AddColumnIfMissing(connection, UsersTable, "LockedUntilUtc", "TEXT NULL");
        }

        /// <summary>Fails the upgrade if the credential columns are missing or malformed.</summary>
        /// <remarks>
        /// The hash itself is not inspected — it is opaque, and a rule about its
        /// content would be a rule about the algorithm. What is asserted is that the
        /// columns exist, hold no NULLs, carry no negative work factor, and that no
        /// row has a hash without the salt and factor needed to check it.
        /// </remarks>
        public static void AssertCredentials(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            foreach (string column in CredentialColumns)
                SchemaMigrationSql.RequireColumn(connection, UsersTable, column);

            foreach (string column in CredentialNonNullColumns)
                SchemaMigrationSql.RequireNoNullValues(connection, UsersTable, column);

            long negative = ScalarLong(connection,
                $"SELECT COUNT(*) FROM \"{UsersTable}\" WHERE \"PasswordIterations\" < 0;");
            if (negative > 0)
                throw new InvalidOperationException(
                    $"Migration {CredentialsMigrationVersion}: {negative} user row(s) carry a negative password work factor.");

            long halfWritten = ScalarLong(connection,
                $"SELECT COUNT(*) FROM \"{UsersTable}\" WHERE " +
                $"\"PasswordHash\" <> '' AND (\"PasswordSalt\" = '' OR \"PasswordIterations\" <= 0);");
            if (halfWritten > 0)
                throw new InvalidOperationException(
                    $"Migration {CredentialsMigrationVersion}: {halfWritten} user row(s) hold a hash without its salt or work factor.");
        }

        /// <summary>Post-migration assertions.</summary>
        public static void AssertShape(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            foreach (string column in RequiredColumns)
                SchemaMigrationSql.RequireColumn(connection, UsersTable, column);

            SchemaMigrationSql.RequireNoNullValues(connection, UsersTable, "IsActive");
            SchemaMigrationSql.RequireNoNullValues(connection, UsersTable, "CreatedAt");
            SchemaMigrationSql.RequireNoNullValues(connection, UsersTable, "UpdatedAt");

            foreach (string column in RequiredNonEmptyColumns)
                SchemaMigrationSql.RequireNoEmptyValues(connection, UsersTable, column);

            SchemaMigrationSql.RequireIndex(connection, "UX_Users_NormalizedUsername",
                UsersTable, unique: true, expectedColumns: new[] { "NormalizedUsername" });
            SchemaMigrationSql.RequireIndex(connection, "IX_Users_IsActive_FullName",
                UsersTable, unique: false, expectedColumns: new[] { "IsActive", "FullName" });

            RequireCatalogueValues(connection, DepartmentsTable, UserCatalog.Departments);
            RequireCatalogueValues(connection, RolesTable, UserCatalog.Roles);
            RequireUsableReferences(connection);
            RequireSingleDefaultRole(connection);
            RequireResolvableAuditIds(connection);
        }

        private static void RequireCatalogueValues(
            SqliteConnection connection, string table, IReadOnlyList<CatalogEntry> expected)
        {
            foreach (CatalogEntry entry in expected)
            {
                if (ScalarLong(connection,
                        $"SELECT COUNT(*) FROM \"{table}\" WHERE \"Code\" = {Literal(entry.Code)};") == 0)
                    throw new InvalidOperationException(
                        $"The {table} catalogue is missing the required code '{entry.Code}' after migration.");
            }
        }

        private static void RequireUsableReferences(SqliteConnection connection)
        {
            long orphanDepartments = ScalarLong(connection,
                $"SELECT COUNT(*) FROM \"{UsersTable}\" u WHERE NOT EXISTS " +
                $"(SELECT 1 FROM \"{DepartmentsTable}\" d WHERE d.\"Code\" = u.\"Department\");");
            if (orphanDepartments != 0)
                throw new InvalidOperationException(
                    $"{orphanDepartments} user row(s) reference a department that is not in the Departments catalogue.");

            long orphanRoles = ScalarLong(connection,
                $"SELECT COUNT(*) FROM \"{UsersTable}\" u WHERE NOT EXISTS " +
                $"(SELECT 1 FROM \"{RolesTable}\" r WHERE r.\"Code\" = u.\"Role\");");
            if (orphanRoles != 0)
                throw new InvalidOperationException(
                    $"{orphanRoles} user row(s) reference a role that is not in the Roles catalogue.");
        }

        /// <summary>
        /// Every non-null CreatedByUserId / UpdatedByUserId must point at a real
        /// user row. A dangling audit pointer is a bug, not a historical fact.
        /// </summary>
        private static void RequireResolvableAuditIds(SqliteConnection connection)
        {
            foreach (string column in new[] { "CreatedByUserId", "UpdatedByUserId" })
            {
                long dangling = ScalarLong(connection,
                    $"SELECT COUNT(*) FROM \"{UsersTable}\" u WHERE u.\"{column}\" IS NOT NULL " +
                    $"AND NOT EXISTS (SELECT 1 FROM \"{UsersTable}\" a WHERE a.\"Id\" = u.\"{column}\");");
                if (dangling != 0)
                    throw new InvalidOperationException(
                        $"{dangling} user row(s) carry a {column} that does not resolve to a user.");
            }
        }

        private static void RequireSingleDefaultRole(SqliteConnection connection)
        {
            long defaults = ScalarLong(connection,
                $"SELECT COUNT(*) FROM \"{RolesTable}\" WHERE \"IsDefault\" <> 0;");
            if (defaults != 1)
                throw new InvalidOperationException(
                    $"The Roles catalogue must mark exactly one default role; found {defaults}.");

            long defaultIsEstimator = ScalarLong(connection,
                $"SELECT COUNT(*) FROM \"{RolesTable}\" WHERE \"IsDefault\" <> 0 " +
                $"AND \"Code\" = {Literal(UserCatalog.DefaultRole)};");
            if (defaultIsEstimator != 1)
                throw new InvalidOperationException(
                    $"The default role must be '{UserCatalog.DefaultRole}'.");
        }

        /// <summary>SQLite string literal with quotes doubled. Codes are constants, not user input.</summary>
        private static string Literal(string value) => "'" + (value ?? "").Replace("'", "''") + "'";

        private static long ScalarLong(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
        }
    }
}
