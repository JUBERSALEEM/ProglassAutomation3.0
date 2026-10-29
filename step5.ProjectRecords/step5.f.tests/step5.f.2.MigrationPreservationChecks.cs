// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.f.tests/step5.f.2.MigrationPreservationChecks.cs
// MODULE: step5 — proof that the migration chain preserves Inquiry data.
//
// WHAT THIS IS
//   A verification executable that takes a WORKING COPY of a real inquiries.db,
//   fingerprints everything an operator cares about (step5.f.3), runs the
//   application's own SchemaMigrationRunner against that copy, and compares the
//   fingerprints. It prints a PASS/FAIL line per check and writes a report
//   beside the copy.
//
// WHAT IT REFUSES TO DO
//   • It refuses the production file. Any path inside %APPDATA%\ProGlassAutomation
//     is rejected before a single byte is read.
//   • It never inserts, updates or deletes an Inquiries row. It only reads.
//   • It cannot invent a legacy database: the only file it upgrades is the one
//     you hand it.
//
// THE REWIND FLAG — read this if the check says "NOT RUN"
//   If the copy is already at the chain tip (1017), the runner has nothing to do
//   and a no-op proves nothing. `--rewind-1017` removes what 1017 itself created
//   — the Projects table, its indexes, the two link columns — and sets
//   user_version back one step, on the COPY only, so the real migration then runs
//   against real inquiry data. It refuses if any project or link data exists,
//   because removing those objects would destroy information.
//
// RUN (Windows, .NET 8)
//   dotnet run -- "D:\G1\working-copy\inquiries.db"
//   dotnet run -- "D:\G1\working-copy\inquiries.db" --rewind-1017
//
// Exit code 0 = every check passed; 1 = a check failed; 2 = refused or unusable
// input; 3 = NOT RUN because the copy was already current.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Projects;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ProGlassAutomation.Step5Tests;

internal static class MigrationPreservationChecks
{
    private static int _passed;
    private static readonly List<string> Failures = new();
    private static readonly List<string> Notes = new();
    private static readonly List<string> ReportRows = new();

    public static int Main(string[] args)
    {
        Console.WriteLine("G1 — migration preservation check");
        Console.WriteLine("=================================");
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: dotnet run -- \"<path to a WORKING COPY of inquiries.db>\" [--rewind-1017]");
            return 2;
        }

        string dbPath = Path.GetFullPath(args[0]);
        bool rewind = args.Any(a => string.Equals(a, "--rewind-1017", StringComparison.OrdinalIgnoreCase));

        // Safety: the production file is never opened. Checked first, before
        // anything reads the path.
        string productionFolder = Path.GetFullPath(AppDataRoot.ProductionFolder);
        string productionFile = Path.GetFullPath(Path.Combine(productionFolder, "inquiries.db"));
        if (string.Equals(dbPath, productionFile, StringComparison.OrdinalIgnoreCase) ||
            dbPath.StartsWith(productionFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("FAIL: that path is the production database, or inside the production folder:");
            Console.WriteLine("      " + productionFolder);
            Console.WriteLine("      Copy it elsewhere and pass the copy. Nothing was read or changed.");
            return 2;
        }
        if (!File.Exists(dbPath))
        {
            Console.WriteLine("FAIL: no database at " + dbPath + " — nothing was read or changed.");
            return 2;
        }

        Console.WriteLine("Working copy : " + dbPath);
        Console.WriteLine("Size         : " + new FileInfo(dbPath).Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
        Console.WriteLine("Production   : " + productionFile + "  (not opened)");
        Console.WriteLine();

        Snapshot before = DatabaseSnapshot.Take(dbPath);
        Console.WriteLine($"Before: user_version={before.Version}, Inquiries={before.InquiryCount}, " +
                          $"columns={before.InquiryColumns.Count}, fingerprint={before.Fingerprint}");

        int chainTip = AppSchemaMigrations.Inquiries.Max(m => m.Version);
        var pending = AppSchemaMigrations.Inquiries
            .Where(m => m.Version > before.Version).OrderBy(m => m.Version).ToList();
        Notes.Add("chain tip " + chainTip + "; pending before the run: " +
                  (pending.Count == 0 ? "none" : string.Join("; ", pending.Select(m => m.Version + " " + m.Name))));

        if (pending.Count == 0 && !rewind)
        {
            Console.WriteLine();
            Console.WriteLine("NOT RUN: this copy is already at the chain tip, so the runner has nothing to do.");
            Console.WriteLine("         A no-op proves nothing about the upgrade. Re-run with --rewind-1017 to");
            Console.WriteLine("         remove what 1017 created (on the copy only) and let it run for real.");
            WriteReport(dbPath, before, before, false, rewind, chainTip, "NOT RUN — the copy was already current");
            return 3;
        }

        if (rewind)
        {
            // Rewinding means "set the version back to the step before 1017", which
            // is only truthful when the copy is AT 1017. On an older copy it would
            // skip every migration in between, which is the opposite of a test.
            if (before.Version != chainTip)
            {
                Console.WriteLine($"FAIL: --rewind-1017 needs a copy at {chainTip}, this one is at " +
                                  before.Version + ". Run without the switch: the pending migrations " +
                                  "will apply normally.");
                return 2;
            }
            if (!DatabaseSnapshot.Rewind1017(dbPath, before, out string problem))
            {
                Console.WriteLine("FAIL: rewind refused — " + problem);
                WriteReport(dbPath, before, before, false, rewind, chainTip, "NOT RUN — rewind refused");
                return 2;
            }
            Console.WriteLine("Rewound: 1017's objects removed from the copy; user_version is now " +
                              DatabaseSnapshot.Version(dbPath));
            before = DatabaseSnapshot.Take(dbPath, reference: before);
        }

        // The runner makes its own backup under <copy folder>\MigrationBackups;
        // record what is there before the run so the new file can be named.
        string backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "MigrationBackups");
        var backupsBefore = Directory.Exists(backupDir)
            ? Directory.GetFiles(backupDir, "*.db").ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The copy is opened with Cache=Shared and pooled handles, so drop every
        // pooled connection before the runner opens its transaction: a stale
        // shared-cache reader would otherwise be able to block the migration.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        Console.WriteLine();
        Console.WriteLine("Running SchemaMigrationRunner.Upgrade — the application's own runner...");
        bool upgraded = SchemaMigrationRunner.Upgrade(
            dbPath, AppSchemaMigrations.Inquiries, "G1 migration preservation check");
        string failureSummary = SchemaMigrationRunner.FailureSummary;
        Check("runner reported success", upgraded, upgraded ? "" : failureSummary);

        Snapshot after = DatabaseSnapshot.Take(dbPath, reference: before);
        Console.WriteLine($"After : user_version={after.Version}, Inquiries={after.InquiryCount}, " +
                          $"columns={after.InquiryColumns.Count}, fingerprint={after.Fingerprint}");
        Console.WriteLine();

        Check("schema version advanced to the chain tip (" + chainTip + ")",
            after.Version == chainTip, "actual " + after.Version);
        Check("inquiry record count unchanged",
            after.InquiryCount == before.InquiryCount,
            $"before {before.InquiryCount}, after {after.InquiryCount}");
        Check("inquiry ids unchanged",
            before.InquiryIds.SequenceEqual(after.InquiryIds),
            $"{before.InquiryIds.Count} id(s) before, {after.InquiryIds.Count} after");
        Check("every value in every pre-existing inquiry column unchanged",
            after.Fingerprint == before.Fingerprint,
            $"fingerprint {before.Fingerprint} → {after.Fingerprint}");

        // A documented removal is not a failure — unless the column still held
        // data, which would make it a loss regardless of what the migration says.
        var removed = before.InquiryColumns.Where(c => !after.InquiryColumns.Contains(c)).ToList();
        var added = after.InquiryColumns.Where(c => !before.InquiryColumns.Contains(c)).ToList();
        if (removed.Count == 0) Check("no inquiry column was removed", true, "");
        else
        {
            var withData = removed.Where(before.ColumnsWithValues.Contains).ToList();
            Check("documented column removal (" + string.Join(", ", removed) + ") held no values",
                withData.Count == 0,
                "these still contained data: " + string.Join(", ", withData));
            Notes.Add("columns removed by the chain, documented in migration 1002: " + string.Join(", ", removed));
        }
        if (added.Count > 0) Notes.Add("columns added by the chain: " + string.Join(", ", added));

        Check("the two link columns exist on Inquiries",
            after.InquiryColumns.Contains(ProjectSchema.InquiryClientColumn) &&
            after.InquiryColumns.Contains(ProjectSchema.InquiryProjectColumn),
            "missing " + string.Join(", ", new[]
            {
                ProjectSchema.InquiryClientColumn, ProjectSchema.InquiryProjectColumn
            }.Where(c => !after.InquiryColumns.Contains(c))));
        Check("the Projects table exists",
            after.Tables.Contains(ProjectSchema.TableName), "");

        foreach (string legacy in DatabaseSnapshot.LegacyColumns)
        {
            if (!before.InquiryColumns.Contains(legacy))
            {
                Notes.Add("legacy column " + legacy + " is not present in this database — nothing to compare");
                continue;
            }
            Check("legacy column " + legacy + " present with unchanged values",
                after.LegacyValues.TryGetValue(legacy, out string? a) &&
                before.LegacyValues.TryGetValue(legacy, out string? b) && a == b,
                "value hash differs, or the column is gone");
        }

        Check("no existing inquiry was given a client or project link",
            after.LinkedInquiryCount == 0,
            after.LinkedInquiryCount + " inquiry row(s) carry a link after the migration");

        string integrity = DatabaseSnapshot.QuickCheck(dbPath);
        Check("SQLite integrity check (PRAGMA quick_check) is clean", integrity == "ok", integrity);

        var newBackups = (Directory.Exists(backupDir)
                ? Directory.GetFiles(backupDir, "*.db").ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            .Except(backupsBefore).ToList();
        Check("the runner created its own pre-migration backup", newBackups.Count > 0, "nothing appeared in " + backupDir);
        foreach (string backup in newBackups) Notes.Add("runner backup: " + backup);
        if (failureSummary.Length > 0) Notes.Add("runner messages: " + failureSummary);

        Console.WriteLine();
        string report = WriteReport(dbPath, before, after, upgraded, rewind, chainTip, null);
        Console.WriteLine($"step5 migration checks: {_passed} passed, {Failures.Count} failed.");
        foreach (string failure in Failures) Console.WriteLine("  FAIL: " + failure);
        foreach (string note in Notes) Console.WriteLine("  note: " + note);
        Console.WriteLine("Report: " + report);
        if (Failures.Count == 0)
            Console.WriteLine("PASS: the migration preserved every inquiry record, every value and every legacy column.");
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (ok) { _passed++; Console.WriteLine("  PASS  " + name); }
        else
        {
            Failures.Add(name + (detail.Length > 0 ? " (" + detail + ")" : ""));
            Console.WriteLine("  FAIL  " + name + (detail.Length > 0 ? " — " + detail : ""));
        }
        ReportRows.Add($"| {name} | {(ok ? "**PASS**" : "**FAIL**")} | {detail.Replace("|", "\\|")} |");
    }

    private static string WriteReport(string dbPath, Snapshot before, Snapshot after,
        bool upgraded, bool rewind, int chainTip, string? verdict)
    {
        string path = dbPath + ".g1-migration-report.md";
        var text = new StringBuilder();
        text.AppendLine("# G1 — migration preservation report");
        text.AppendLine();
        text.AppendLine("Generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                        " by `step5.f.2`.");
        text.AppendLine();
        text.AppendLine("| Item | Value |");
        text.AppendLine("| --- | --- |");
        text.AppendLine("| Working copy | `" + dbPath + "` |");
        text.AppendLine("| Production file | `" + Path.Combine(AppDataRoot.ProductionFolder, "inquiries.db") +
                        "` — **not opened** |");
        text.AppendLine("| Chain tip at this build | " + chainTip + " |");
        text.AppendLine("| user_version before → after | " + before.Version + " → " + after.Version + " |");
        text.AppendLine("| Inquiries before → after | " + before.InquiryCount + " → " + after.InquiryCount + " |");
        text.AppendLine("| Data fingerprint before → after | `" + before.Fingerprint + "` → `" +
                        after.Fingerprint + "` |");
        text.AppendLine("| Runner reported success | " + (upgraded ? "yes" : "no") + " |");
        text.AppendLine("| Rewind used | " + (rewind ? "yes — 1017's objects removed from the copy first" : "no") + " |");
        text.AppendLine();
        text.AppendLine("## Checks");
        text.AppendLine();
        text.AppendLine("| Check | Result | Detail |");
        text.AppendLine("| --- | --- | --- |");
        foreach (string row in ReportRows) text.AppendLine(row);
        if (verdict != null)
        {
            text.AppendLine();
            text.AppendLine("**Verdict: " + verdict + "**");
        }
        if (Notes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("## Notes");
            text.AppendLine();
            foreach (string note in Notes) text.AppendLine("- " + note);
        }
        File.WriteAllText(path, text.ToString());
        return path;
    }
}
