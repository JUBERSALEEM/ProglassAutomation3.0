// ═══════════════════════════════════════════════════════════════════════
// FILE: step2.ClientManagement/step2.b.1.ClientSchema.cs
// MODULE: step2 — Clients table DDL and shape assertions.
//
// The client directory lives in the existing inquiries.db on the existing
// single version chain, as AppSchemaMigrations migration 1011. One database
// file ⇒ one PRAGMA user_version ⇒ exactly one migration chain; a second
// chain against the same file is never created.
//
// Every statement is idempotent (CREATE ... IF NOT EXISTS / ADD COLUMN when
// missing) so a re-run is always safe, and the assertions run inside the
// migration's transaction: a wrong shape aborts the whole upgrade and leaves
// the schema and user_version untouched.
//
// This module does NOT alter the Inquiries table, add a foreign key, or add
// any Inquiry→Client relationship.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using ProGlassAutomation.Models.Clients;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using System;

namespace ProGlassAutomation.Services.Clients
{
    /// <summary>DDL and shape checks for the Clients table.</summary>
    public static class ClientSchema
    {
        public const string TableName = "Clients";

        /// <summary>Migration version that introduces the client directory.</summary>
        public const int MigrationVersion = 1011;

        /// <summary>Creates the table and indexes when missing, and adds any missing column.</summary>
        public static void Ensure(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            SchemaMigrationSql.CreateTableIfNotExists(connection, $@"
                CREATE TABLE IF NOT EXISTS ""{TableName}"" (
                    ""Id""            INTEGER NOT NULL CONSTRAINT ""PK_Clients"" PRIMARY KEY AUTOINCREMENT,
                    ""ClientName""    TEXT NOT NULL DEFAULT '',
                    ""ContactPerson"" TEXT NOT NULL DEFAULT '',
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
            SchemaMigrationSql.AddColumnIfMissing(connection, TableName, "ContactPerson", "TEXT NOT NULL DEFAULT ''");
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
        /// Post-migration assertions. Runs inside the migration transaction, so
        /// a failure rolls the whole upgrade back and never leaves a half-built
        /// client directory.
        /// </summary>
        public static void AssertShape(SqliteConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            foreach (string column in RequiredColumns)
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

        /// <summary>Every column the EF model in step2.b.3 maps.</summary>
        public static readonly string[] RequiredColumns =
        {
            "Id", "ClientName", "ContactPerson", "Phone", "Email", "Trn",
            "Address", "Notes", "IsActive", "CreatedAt", "UpdatedAt"
        };

        /// <summary>Column names used by the projection queries.</summary>
        public static string ColumnsForSelect =>
            "\"Id\", \"ClientName\", \"ContactPerson\", \"Phone\", \"Email\", \"Trn\", " +
            "\"Address\", \"Notes\", \"IsActive\", \"CreatedAt\", \"UpdatedAt\"";
    }
}
