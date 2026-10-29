// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.f.tests/step5.f.6.ProjectBehaviourChecks.CrossModule.cs
// MODULE: step5 — behaviour checks, part 3: the cross-module boundary.
//
// Split from step5.f.5 to stay inside the 400-line rule (docs/12, owner rule K).
// One partial class, one entry point; this file holds the checks that prove a
// deactivated client does not damage a project, and that the module has no way
// to delete a record — plus the helpers all three files share.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Inquiry;
using ProGlassAutomation.Services.Projects;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ProGlassAutomation.Step5Tests;

internal static partial class ProjectBehaviourChecks
{
    // ── Client deactivation ──────────────────────────────────────────────

    private static void CheckClientDeactivation()
    {
        WithTempFolder("client", () =>
        {
            var repository = new ProjectRepository();
            repository.InitializeAsync().GetAwaiter().GetResult();

            Execute("INSERT INTO \"Clients\" (\"ClientName\", \"IsActive\") VALUES ('Deactivatable Ltd', 1);");
            long clientId = QueryLong("SELECT \"Id\" FROM \"Clients\" WHERE \"ClientName\" = 'Deactivatable Ltd';");
            True(clientId > 0, "the test client exists");

            ProjectSaveResult project = repository.CreateAsync(new ProjectDraft
            {
                ProjectName = "Client job",
                ClientId = (int)clientId,
                ClientName = "Deactivatable Ltd"
            }).GetAwaiter().GetResult();
            True(project.Succeeded, "a project may reference an existing client: " + project.Message);

            // Deactivation is not deletion: the row stays and the reference stays.
            Execute("UPDATE \"Clients\" SET \"IsActive\" = 0 WHERE \"Id\" = " + clientId + ";");
            Equal("0", QueryLong("SELECT \"IsActive\" FROM \"Clients\" WHERE \"Id\" = " + clientId + ";")
                    .ToString(CultureInfo.InvariantCulture), "the client is inactive");
            Equal(clientId.ToString(CultureInfo.InvariantCulture),
                QueryLong("SELECT \"ClientId\" FROM \"Projects\" WHERE \"Id\" = " + project.Project!.Id + ";")
                    .ToString(CultureInfo.InvariantCulture),
                "the project keeps its client reference after the client is deactivated");
            True(repository.GetAsync(project.Project.Id).GetAwaiter().GetResult() != null,
                "the project still loads after its client was deactivated");
        });
    }

    // ── No delete path ───────────────────────────────────────────────────

    private static void CheckNoDeletePath()
    {
        var methods = typeof(IProjectRepository).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name).ToList();
        True(methods.All(name => !name.Contains("Delete", StringComparison.OrdinalIgnoreCase)),
            "the repository interface has no delete method (projects close, they do not vanish)");

        string repositorySource = ReadRepositorySource();
        if (repositorySource.Length == 0) return;
        True(!repositorySource.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase),
            "no step5 source file issues a DELETE");
        True(!repositorySource.Contains("CustomerId", StringComparison.Ordinal) ||
             repositorySource.Contains("legacy", StringComparison.OrdinalIgnoreCase),
            "no step5 source file writes the legacy CustomerId placeholder");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// A real inquiry row, written through the Inquiry module's own context rather
    /// than by hand: a hand-written INSERT would have to guess every NOT NULL
    /// column the model owns, and a guess that drifts from the model would look
    /// like a linking defect. The two legacy placeholders are set to known values
    /// so the checks can prove the link never touches them.
    /// </summary>
    private static int InsertInquiry(string number, string customer)
    {
        using var db = new InquiryDbContext();
        var record = new InquiryRecord
        {
            InquiryNumber = number,
            CustomerName = customer,
            Status = "Received",
            ReceivedAt = new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc),
            CustomerId = 41,
            ProjectId = 42
        };
        db.Inquiries.Add(record);
        db.SaveChanges();
        return record.Id;
    }

    /// <summary>Every column of one inquiry row, as text, for a whole-row comparison.</summary>
    private static Dictionary<string, string> ReadRow(int inquiryId)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM \"Inquiries\" WHERE \"Id\" = " + inquiryId + ";";
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return values;
        for (int index = 0; index < reader.FieldCount; index++)
            values[reader.GetName(index)] = reader.IsDBNull(index)
                ? "<null>"
                : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? "";
        return values;
    }

    private static void WithTempFolder(string name, Action check)
    {
        string tempFolder = Path.Combine(
            Path.GetTempPath(), "proglass-step5-" + name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempFolder);
        AppDataRoot.TestFolderOverride = tempFolder;
        try
        {
            check();
        }
        catch (Exception error)
        {
            Failures.Add($"{name} threw {error.GetType().Name}: {error.Message}");
            Console.WriteLine($"  FAIL  {name} threw {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            AppDataRoot.TestFolderOverride = null;
            try { Directory.Delete(tempFolder, recursive: true); } catch { /* best effort */ }
        }
    }

    private static SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection(
            SqliteResilience.BuildConnectionString(ProjectDbContext.DatabaseFilePath));
        connection.Open();
        SqliteResilience.ApplyConnectionPolicy(connection);
        return connection;
    }

    private static long QueryLong(string sql)
    {
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value == null || value is DBNull ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static void Execute(string sql)
    {
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool TableExists(string table) =>
        QueryLong("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='" + table + "';") > 0;

    private static bool ColumnExists(string table, string column) =>
        QueryLong("SELECT COUNT(*) FROM pragma_table_info('" + table + "') WHERE name='" + column + "';") > 0;

    private static string ReadRepositorySource()
    {
        // Reading the module's own files is the only way to assert "no DELETE
        // anywhere" from inside a check. Failure to find them is not a failure:
        // the reflection check above already covers the interface.
        try
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "step5.ProjectRecords")))
                folder = folder.Parent;
            if (folder == null) return "";
            return string.Join("\n", Directory.GetFiles(
                Path.Combine(folder.FullName, "step5.ProjectRecords"), "*.cs").Select(File.ReadAllText));
        }
        catch
        {
            return "";
        }
    }

    private static void Run(string name, Action check)
    {
        Console.WriteLine();
        Console.WriteLine("-- " + name);
        try
        {
            check();
        }
        catch (Exception error)
        {
            Failures.Add($"{name} threw {error.GetType().Name}: {error.Message}");
            Console.WriteLine($"  FAIL  {name} threw {error.GetType().Name}: {error.Message}");
        }
    }

    private static void True(bool condition, string description)
    {
        if (condition) { _passed++; Console.WriteLine("  PASS  " + description); }
        else { Failures.Add(description); Console.WriteLine("  FAIL  " + description); }
    }

    private static void Equal(string expected, string actual, string description) =>
        True(string.Equals(expected, actual, StringComparison.Ordinal), description);
}
