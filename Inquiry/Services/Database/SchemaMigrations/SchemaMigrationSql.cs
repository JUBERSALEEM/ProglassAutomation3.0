// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Database/SchemaMigrations/SchemaMigrationSql.cs
// MODULE: Core — idempotent SQL helpers for schema migrations.
//
// Shared, idempotent building blocks for migration Apply actions. They are
// safe to run more than once: a migration that was already applied (e.g. a
// fresh database already carries the column/table) is a no-op.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
        public static void CreateTableIfNotExists(SqliteConnection connection, string sql)
        {
            Execute(connection, sql);
        }

        /// <summary>
        /// Drops a column from a table only if it exists. Idempotent and
        /// safe to call on SQLite >= 3.35.0 (DROP COLUMN was added then).
        /// Refuses to drop without an explicit data-preservation check.
        /// </summary>
        public static void DropColumnIfExists(
            SqliteConnection connection,
            string table,
            string column,
            bool dataPreservationVerified = false)
        {
            ValidateIdentifier(table, nameof(table));
            ValidateIdentifier(column, nameof(column));
            var existing = GetColumnNames(connection, table);
            if (!existing.Contains(column)) return;
            if (!dataPreservationVerified)
                throw new InvalidOperationException(
                    $"Refusing to drop '{table}.{column}' without an explicit data-preservation check.");
            Execute(connection,
                $"ALTER TABLE {QuoteIdentifier(table)} DROP COLUMN {QuoteIdentifier(column)};");
        }

        /// <summary>
        /// Creates an index if it does not already exist, then verifies that
        /// the named index exists on the expected table, has the expected
        /// uniqueness, and indexes the declared columns in order. A same-name
        /// but incompatible index is a migration failure, not a silent no-op.
        /// </summary>
        public static void CreateIndexIfNotExists(SqliteConnection connection, string sql)
        {
            var definition = ParseIndexDefinition(sql);
            if (definition.IsUnique)
            {
                // Preflight instead of catching and suppressing SQLite's
                // UNIQUE failure. This preserves every source row and gives
                // operators concrete duplicate keys to resolve.
                RequireNoDuplicateKeyValues(
                    connection, definition.Table, definition.Columns);
            }
            Execute(connection, sql);
            RequireIndex(connection, definition.Name, definition.Table,
                definition.IsUnique, definition.Columns);
        }

        /// <summary>Verifies a required named index on a table.</summary>
        public static void RequireIndex(
            SqliteConnection connection,
            string indexName,
            string table,
            bool unique,
            IReadOnlyList<string>? expectedColumns = null)
        {
            ValidateIdentifier(indexName, nameof(indexName));
            ValidateIdentifier(table, nameof(table));
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA index_list({QuoteIdentifier(table)});";
            using var reader = command.ExecuteReader();
            bool found = false;
            bool actualUnique = false;
            while (reader.Read())
            {
                if (!string.Equals(reader.GetString(1), indexName, StringComparison.OrdinalIgnoreCase))
                    continue;
                found = true;
                actualUnique = reader.GetInt32(2) != 0;
                break;
            }

            if (!found)
                throw new InvalidOperationException(
                    $"Required SQLite index '{indexName}' on '{table}' is missing after migration.");
            if (actualUnique != unique)
                throw new InvalidOperationException(
                    $"SQLite index '{indexName}' on '{table}' has unique={actualUnique}; expected unique={unique}.");

            if (expectedColumns == null || expectedColumns.Count == 0) return;

            var actualColumns = new List<string>();
            using (var info = connection.CreateCommand())
            {
                info.CommandText = $"PRAGMA index_info({QuoteIdentifier(indexName)});";
                using var columns = info.ExecuteReader();
                while (columns.Read())
                {
                    if (columns.IsDBNull(2))
                        throw new InvalidOperationException(
                            $"Required SQLite index '{indexName}' contains an expression instead of named columns.");
                    actualColumns.Add(columns.GetString(2));
                }
            }

            if (!actualColumns.SequenceEqual(expectedColumns, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"SQLite index '{indexName}' has columns ({string.Join(", ", actualColumns)}); " +
                    $"expected ({string.Join(", ", expectedColumns)}).");
        }

        /// <summary>Verifies that a required column exists on a table.</summary>
        public static void RequireColumn(SqliteConnection connection, string table, string column)
        {
            ValidateIdentifier(table, nameof(table));
            ValidateIdentifier(column, nameof(column));
            if (!GetColumnNames(connection, table).Contains(column))
                throw new InvalidOperationException(
                    $"Required SQLite column '{table}.{column}' is missing after migration.");
        }

        /// <summary>
        /// Fails with representative duplicate key values before a unique
        /// index is created. No existing rows are rewritten or re-numbered.
        /// </summary>
        public static void RequireNoDuplicateKeyValues(
            SqliteConnection connection,
            string table,
            params string[] columns)
        {
            ValidateIdentifier(table, nameof(table));
            if (columns == null || columns.Length == 0)
                throw new ArgumentException("At least one key column is required.", nameof(columns));
            foreach (string column in columns) ValidateIdentifier(column, nameof(columns));

            string groupedColumns = string.Join(", ", columns.Select(QuoteIdentifier));
            string nonNullKey = string.Join(" AND ", columns
                .Select(column => $"{QuoteIdentifier(column)} IS NOT NULL"));
            using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT {groupedColumns}, COUNT(*) FROM {QuoteIdentifier(table)} " +
                $"WHERE {nonNullKey} GROUP BY {groupedColumns} HAVING COUNT(*) > 1 LIMIT 10;";
            using var reader = command.ExecuteReader();
            var duplicates = new List<string>();
            while (reader.Read())
            {
                var values = new string[columns.Length];
                for (int i = 0; i < columns.Length; i++)
                    values[i] = reader.IsDBNull(i) ? "<NULL>" : Convert.ToString(reader.GetValue(i),
                        System.Globalization.CultureInfo.InvariantCulture) ?? "<NULL>";
                long count = reader.GetInt64(columns.Length);
                duplicates.Add($"({string.Join(", ", values)}) x{count}");
            }

            if (duplicates.Count > 0)
                throw new InvalidOperationException(
                    $"Unique key ({string.Join(", ", columns)}) on '{table}' has pre-existing duplicates: " +
                    string.Join("; ", duplicates) +
                    ". No records were renumbered; resolve the duplicates and retry the migration.");
        }

        /// <summary>Verifies a migrated NOT NULL field contains no NULL rows.</summary>
        public static void RequireNoNullValues(SqliteConnection connection, string table, string column)
        {
            ValidateIdentifier(table, nameof(table));
            ValidateIdentifier(column, nameof(column));
            using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT COUNT(*) FROM {QuoteIdentifier(table)} WHERE {QuoteIdentifier(column)} IS NULL;";
            long count = Convert.ToInt64(command.ExecuteScalar() ?? 0, System.Globalization.CultureInfo.InvariantCulture);
            if (count != 0)
                throw new InvalidOperationException(
                    $"Critical migrated field '{table}.{column}' contains {count} NULL row(s).");
        }

        /// <summary>Verifies a critical migrated text field has no NULL or blank values.</summary>
        public static void RequireNoEmptyValues(SqliteConnection connection, string table, string column)
        {
            ValidateIdentifier(table, nameof(table));
            ValidateIdentifier(column, nameof(column));
            using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT COUNT(*) FROM {QuoteIdentifier(table)} " +
                $"WHERE {QuoteIdentifier(column)} IS NULL OR TRIM(CAST({QuoteIdentifier(column)} AS TEXT)) = '';";
            long count = Convert.ToInt64(command.ExecuteScalar() ?? 0,
                System.Globalization.CultureInfo.InvariantCulture);
            if (count != 0)
                throw new InvalidOperationException(
                    $"Critical migrated field '{table}.{column}' contains {count} NULL or blank row(s).");
        }

        /// <summary>Runs a single non-query SQL statement.</summary>
        public static void Execute(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static (string Name, string Table, bool IsUnique, string[] Columns)
            ParseIndexDefinition(string sql)
        {
            var match = Regex.Match(sql ?? "",
                @"^\s*CREATE\s+(?<unique>UNIQUE\s+)?INDEX\s+IF\s+NOT\s+EXISTS\s+(?<name>[^\s]+)\s+ON\s+(?<table>[^\s(]+)\s*\((?<columns>.*)\)\s*;?\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
            if (!match.Success)
                throw new ArgumentException(
                    "CreateIndexIfNotExists requires a CREATE [UNIQUE] INDEX IF NOT EXISTS statement " +
                    "with a simple named-column list.", nameof(sql));

            string name = UnquoteIdentifier(match.Groups["name"].Value);
            string table = UnquoteIdentifier(match.Groups["table"].Value);
            string[] columns = match.Groups["columns"].Value
                .Split(',')
                .Select(UnquoteIdentifier)
                .ToArray();
            if (columns.Length == 0 || columns.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("Index column list is empty or unsupported.", nameof(sql));
            ValidateIdentifier(name, "index name");
            ValidateIdentifier(table, "table name");
            foreach (string column in columns) ValidateIdentifier(column, "index column");
            return (name, table, match.Groups["unique"].Success, columns);
        }

        private static string UnquoteIdentifier(string value)
        {
            value = value.Trim();
            if (value.Length >= 2
                && ((value[0] == '"' && value[^1] == '"')
                    || (value[0] == '`' && value[^1] == '`')
                    || (value[0] == '[' && value[^1] == ']')))
                value = value.Substring(1, value.Length - 2);
            return value.Replace("\"\"", "\"", StringComparison.Ordinal).Trim();
        }

        private static void ValidateIdentifier(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_')))
                throw new ArgumentException($"Invalid SQLite identifier '{value}'.", parameterName);
        }

        private static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

        /// <summary>Returns the existing column names for a table.</summary>
        private static HashSet<string> GetColumnNames(SqliteConnection connection, string table)
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
