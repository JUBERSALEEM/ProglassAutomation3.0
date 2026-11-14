// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Database/SchemaMigrations/SchemaMigrationSql.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — idempotent SQL helpers for schema migrations
//
// Shared, idempotent building blocks for migration Apply actions. They are
// safe to run more than once: a migration that was already applied (e.g. a
// fresh database already carries the column/table) is a no-op.
//
// These generalise the private helpers that the original audit.db migrator
// used, so every database now shares one implementation instead of each
// keeping its own copy.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Database.SchemaMigrations
{
    /// <summary>Idempotent SQL primitives used by migration Apply actions.</summary>
    public static class SchemaMigrationSql
    {
        /// <summary>
        /// Adds a column to a table only if it does not already exist.
        /// Existing rows are never touched (SQLite ALTER ADD COLUMN keeps
        /// them; the DEFAULT populates them).
        /// </summary>
        public static void AddColumnIfMissing(
            SqliteConnection connection,
            string table,
            string column,
            string definition)
        {
            var existing = GetColumnNames(connection, table);
            if (existing.Contains(column)) return;

            Execute(connection,
                $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
        }

        /// <summary>Creates a table if it does not already exist.</summary>
        public static void CreateTableIfNotExists(
            SqliteConnection connection, string sql)
        {
            Execute(connection, sql);
        }

        /// <summary>
        /// Drops a column from a table only if it exists. Idempotent and
        /// safe to call on any SQLite ≥ 3.35.0 (DROP COLUMN was added then).
        /// The bundled SQLitePCL raw library in this project ships a
        /// modern SQLite, so the operation is always available.
        /// </summary>
        public static void DropColumnIfExists(
            SqliteConnection connection,
            string table,
            string column)
        {
            var existing = GetColumnNames(connection, table);
            if (!existing.Contains(column)) return;
            Execute(connection, $"ALTER TABLE {table} DROP COLUMN {column};");
        }

        /// <summary>Creates an index if it does not already exist.</summary>
        public static void CreateIndexIfNotExists(
            SqliteConnection connection, string sql)
        {
            Execute(connection, sql);
        }

        /// <summary>Runs a single non-query SQL statement.</summary>
        public static void Execute(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        /// <summary>Returns the existing column names for a table.</summary>
        private static HashSet<string> GetColumnNames(
            SqliteConnection connection, string table)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var pragma = connection.CreateCommand();
            pragma.CommandText = $"PRAGMA table_info({table});";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
                set.Add(reader.GetString(1));
            return set;
        }
    }
}
