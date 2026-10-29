// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.f.tests/step5.f.3.DatabaseSnapshot.cs
// MODULE: step5 — the fingerprint used by the G1 migration check.
//
// Reads a database file and records everything the G1 review has to prove was
// preserved: schema version, inquiry count and ids, every inquiry column, every
// value in every one of those columns (as one stable hash), the four legacy
// columns on their own, the table and index names, and whether any inquiry is
// linked to a client or project.
//
// Read-only. Nothing here writes to a database except Rewind1017, which is only
// ever called on a working copy and refuses if it would destroy project or link
// data. That refusal is the reason it lives in its own method rather than being
// a flag on Take.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Projects;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ProGlassAutomation.Step5Tests;

internal sealed class Snapshot
{
    public int Version;
    public long InquiryCount;
    public long LinkedInquiryCount;
    public List<long> InquiryIds = new();
    public List<string> InquiryColumns = new();
    public HashSet<string> ColumnsWithValues = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Tables = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Indexes = new();
    public string Fingerprint = "";
    public Dictionary<string, string> LegacyValues = new(StringComparer.OrdinalIgnoreCase);
}

internal static class DatabaseSnapshot
{
    public static readonly string[] LegacyColumns =
        { "CustomerId", "ProjectId", "SalesmanId", "PreparedByUserId" };

    /// <summary>
    /// Reads the file. When <paramref name="reference"/> is supplied the row
    /// hash is taken over the columns that existed in the reference and still
    /// exist now — so an ADD COLUMN cannot change the hash, and only a changed
    /// value can.
    /// </summary>
    public static Snapshot Take(string dbPath, Snapshot? reference = null)
    {
        var snapshot = new Snapshot();
        using var connection = Open(dbPath);

        snapshot.Version = Version(dbPath);
        snapshot.Tables = Names(connection, "table");
        snapshot.Indexes = Names(connection, "index").OrderBy(x => x, StringComparer.Ordinal).ToList();
        snapshot.InquiryColumns = Columns(connection, "Inquiries");
        snapshot.InquiryCount = Scalar(connection, "SELECT COUNT(*) FROM \"Inquiries\";");

        bool hasLink = snapshot.InquiryColumns.Contains(ProjectSchema.InquiryClientColumn) &&
                       snapshot.InquiryColumns.Contains(ProjectSchema.InquiryProjectColumn);
        snapshot.LinkedInquiryCount = hasLink
            ? Scalar(connection,
                $"SELECT COUNT(*) FROM \"Inquiries\" WHERE \"{ProjectSchema.InquiryClientColumn}\" IS NOT NULL " +
                $"OR \"{ProjectSchema.InquiryProjectColumn}\" IS NOT NULL;")
            : 0;

        List<string> columns = reference is null
            ? snapshot.InquiryColumns
            : reference.InquiryColumns
                .Where(c => snapshot.InquiryColumns.Any(n => string.Equals(n, c, StringComparison.OrdinalIgnoreCase)))
                .ToList();

        var rows = new StringBuilder();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT " + string.Join(", ", columns.Select(c => "\"" + c + "\"")) +
                                  " FROM \"Inquiries\" ORDER BY \"Id\";";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    string value = reader.IsDBNull(i)
                        ? "\u0000"
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "";
                    rows.Append(columns[i]).Append('=').Append(value).Append('\u001f');
                    if (value.Length > 0) snapshot.ColumnsWithValues.Add(columns[i]);
                }
                rows.Append('\n');
            }
        }
        snapshot.Fingerprint = Hash(rows.ToString());

        foreach (string legacy in LegacyColumns)
            if (columns.Contains(legacy))
                snapshot.LegacyValues[legacy] = Hash(Column(connection, legacy));

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT \"Id\" FROM \"Inquiries\" ORDER BY \"Id\";";
            using var reader = command.ExecuteReader();
            while (reader.Read()) snapshot.InquiryIds.Add(reader.GetInt64(0));
        }
        return snapshot;
    }

    /// <summary>
    /// Removes exactly what migration 1017 created and sets the version back to
    /// the step before it, so the real migration has work to do on real data.
    /// Refuses if any project row or inquiry link exists: those are data, and
    /// this method only removes structure.
    /// </summary>
    public static bool Rewind1017(string dbPath, Snapshot before, out string problem)
    {
        problem = "";
        try
        {
            using var connection = Open(dbPath);

            long projects = TableExists(connection, ProjectSchema.TableName)
                ? Scalar(connection, $"SELECT COUNT(*) FROM \"{ProjectSchema.TableName}\";")
                : 0;
            long links = Scalar(connection,
                $"SELECT COUNT(*) FROM \"Inquiries\" WHERE \"{ProjectSchema.InquiryClientColumn}\" IS NOT NULL " +
                $"OR \"{ProjectSchema.InquiryProjectColumn}\" IS NOT NULL;");
            if (projects > 0 || links > 0)
            {
                problem = $"this copy holds {projects} project row(s) and {links} linked inquiry row(s); " +
                          "removing 1017's objects would destroy them";
                return false;
            }

            // The baseline for the self-check is taken over the columns that will
            // SURVIVE the rewind — comparing a hash over seven columns with one
            // over five is not a comparison. Taken before any DDL runs.
            var surviving = new Snapshot
            {
                InquiryColumns = before.InquiryColumns
                    .Where(c => c != ProjectSchema.InquiryClientColumn &&
                                c != ProjectSchema.InquiryProjectColumn)
                    .ToList()
            };
            Snapshot pre = Take(dbPath, reference: surviving);

            foreach (string index in new[]
                     {
                         ProjectSchema.InquiryProjectIndex, ProjectSchema.InquiryClientIndex,
                         // The two Projects indexes 1017 creates; its table indexes go with the table.
                         "IX_Projects_ClientId", "IX_Projects_Status_Name"
                     })
                Execute(connection, $"DROP INDEX IF EXISTS \"{index}\";");
            Execute(connection, $"DROP TABLE IF EXISTS \"{ProjectSchema.TableName}\";");
            // DROP COLUMN needs SQLite >= 3.35; the bundled provider is newer.
            // Guarded because a copy that never reached 1017 has no such column.
            List<string> existing = Columns(connection, "Inquiries");
            foreach (string column in new[] { ProjectSchema.InquiryClientColumn, ProjectSchema.InquiryProjectColumn })
                if (existing.Contains(column))
                    Execute(connection, $"ALTER TABLE \"Inquiries\" DROP COLUMN \"{column}\";");

            int previous = AppSchemaMigrations.Inquiries
                .Where(m => m.Version < ProjectSchema.MigrationVersion)
                .Max(m => m.Version);
            Execute(connection, $"PRAGMA user_version = {previous};");
            SqliteConnection.ClearAllPools();

            Snapshot post = Take(dbPath, reference: surviving);
            if (post.InquiryCount != pre.InquiryCount || post.Fingerprint != pre.Fingerprint)
            {
                problem = "the rewind changed the inquiry rows themselves; refusing to continue";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            problem = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    // ── Readers ──────────────────────────────────────────────────────────

    public static SqliteConnection Open(string dbPath)
    {
        var connection = new SqliteConnection(SqliteResilience.BuildConnectionString(dbPath));
        connection.Open();
        SqliteResilience.ApplyConnectionPolicy(connection);
        return connection;
    }

    public static int Version(string dbPath)
    {
        using var connection = Open(dbPath);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    public static string QuickCheck(string dbPath)
    {
        using var connection = Open(dbPath);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "(no result)";
    }

    public static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    public static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static List<string> Columns(SqliteConnection connection, string table)
    {
        var names = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var reader = command.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(1));
        return names;
    }

    private static HashSet<string> Names(SqliteConnection connection, string type)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM sqlite_master WHERE type = '{type}' AND name NOT LIKE 'sqlite_%';";
        using var reader = command.ExecuteReader();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    private static string Column(SqliteConnection connection, string column)
    {
        var builder = new StringBuilder();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT \"{column}\" FROM \"Inquiries\" ORDER BY \"Id\";";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            builder.Append(reader.IsDBNull(0)
                    ? "\u0000"
                    : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture))
                   .Append('\u001f');
        return builder.ToString();
    }

    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..16];

    private static bool TableExists(SqliteConnection connection, string table) =>
        Names(connection, "table").Contains(table);
}
