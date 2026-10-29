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
//   dotnet run -- --describe "D:\G1\frozen\inquiries.db"      (read-only report)
//
// `--describe` never opens the file you name: it copies it to the temp folder,
// reads the copy, and deletes the copy. It exists so an existing backup can be
// identified (which version is it?) without touching it.
//
// Exit code 0 = every check passed; 1 = a check failed; 2 = refused or unusable
// input; 3 = NOT RUN because the copy was already current.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
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
            Console.WriteLine("       dotnet run -- --describe \"<path to ANY database copy>\"");
            return 2;
        }

        bool describe = string.Equals(args[0], "--describe", StringComparison.OrdinalIgnoreCase);
        if (describe && args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run -- --describe \"<path to a database copy>\"");
            return 2;
        }
        string dbPath = Path.GetFullPath(describe ? args[1] : args[0]);
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

        if (describe)
            return Describe(dbPath);

        // Belt and braces: if any code path inside the application falls back to
        // its data folder instead of the path we pass, make that folder this one.
        // It can never be %APPDATA%\ProGlassAutomation.
        AppDataRoot.TestFolderOverride = Path.GetDirectoryName(dbPath)!;

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
            int before1017 = AppSchemaMigrations.Inquiries
                .Where(m => m.Version < ProjectSchema.MigrationVersion)
                .Max(m => m.Version);
            Snapshot atTip = before;
            if (!DatabaseSnapshot.Rewind1017(dbPath, before, out string problem))
            {
                Console.WriteLine("FAIL: rewind refused — " + problem);
                WriteReport(dbPath, before, before, false, rewind, chainTip, "NOT RUN — rewind refused");
                return 2;
            }
            Console.WriteLine("Rewound: 1017's objects removed from the copy; user_version is now " +
                              DatabaseSnapshot.Version(dbPath));
            before = DatabaseSnapshot.Take(dbPath, reference: before);

            // A rewind that removes too little would carry 1017's own objects into
            // the "before" side and make the whole comparison meaningless, so the
            // shape is compared object by object against what 1017 added.
            CheckRewindShape(atTip, before, before1017);

            pending = AppSchemaMigrations.Inquiries
                .Where(m => m.Version > before.Version).OrderBy(m => m.Version).ToList();
            Check("exactly migration 1017 is now pending (nothing was skipped)",
                pending.Count == 1 && pending[0].Version == ProjectSchema.MigrationVersion,
                "pending: " + (pending.Count == 0 ? "none" : string.Join(", ", pending.Select(m => m.Version))));
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
        Check("the migration created no project record of its own",
            after.ProjectCount == 0,
            after.ProjectCount + " project row(s) exist; the migration must create none");

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
        string fullIntegrity = DatabaseSnapshot.IntegrityCheck(dbPath);
        Check("SQLite integrity check (PRAGMA integrity_check) is clean", fullIntegrity == "ok", fullIntegrity);
        string foreignKeys = DatabaseSnapshot.ForeignKeyCheck(dbPath);
        Check("no foreign-key violations (PRAGMA foreign_key_check)", foreignKeys == "ok", foreignKeys);
        Check("the number of foreign keys on Inquiries did not change",
            after.ForeignKeyCount == before.ForeignKeyCount,
            $"before {before.ForeignKeyCount}, after {after.ForeignKeyCount}");

        // Every object 1017 promises, named, after the run — not "a table exists
        // somewhere" but the exact objects the migration is supposed to create.
        string[] required =
        {
            "IX_Projects_Number", "IX_Projects_ClientId", "IX_Projects_Status_Name",
            ProjectSchema.InquiryClientIndex, ProjectSchema.InquiryProjectIndex
        };
        var missingIndexes = required.Where(i => !after.Indexes.Contains(i, StringComparer.OrdinalIgnoreCase)).ToList();
        Check("every index migration 1017 creates exists", missingIndexes.Count == 0,
            "missing " + string.Join(", ", missingIndexes));
        Check("the schema version and the actual schema agree",
            after.Version == chainTip &&
            after.Tables.Contains(ProjectSchema.TableName) &&
            after.InquiryColumns.Contains(ProjectSchema.InquiryClientColumn) &&
            after.InquiryColumns.Contains(ProjectSchema.InquiryProjectColumn),
            $"version {after.Version}, Projects {(after.Tables.Contains(ProjectSchema.TableName) ? "present" : "absent")}");

        var newBackups = (Directory.Exists(backupDir)
                ? Directory.GetFiles(backupDir, "*.db").ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            .Except(backupsBefore).ToList();
        Check("the runner created its own pre-migration backup", newBackups.Count > 0, "nothing appeared in " + backupDir);
        foreach (string backup in newBackups) Notes.Add("runner backup: " + backup);
        if (failureSummary.Length > 0) Notes.Add("runner messages: " + failureSummary);

        // ── The guarantees, exercised rather than quoted ─────────────────
        // A deliberately failing migration must: make a backup first, change
        // nothing, leave user_version where it was, and report failure. This is
        // the runner's own transaction path, on the same disposable copy.
        var withProbe = AppSchemaMigrations.Inquiries.ToList();
        withProbe.Add(new SchemaMigration(
            chainTip + 1,
            "G1 transaction-guard probe (deliberately fails)",
            (SqliteConnection _) => throw new InvalidOperationException("deliberate failure: G1 transaction guard")));
        Snapshot beforeProbe = DatabaseSnapshot.Take(dbPath, reference: after);
        var backupsBeforeProbe = Directory.Exists(backupDir)
            ? Directory.GetFiles(backupDir, "*.db").ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SqliteConnection.ClearAllPools();
        bool probeRan = SchemaMigrationRunner.Upgrade(dbPath, withProbe, "G1 transaction guard");
        string probeSummary = SchemaMigrationRunner.FailureSummary;
        Snapshot afterProbe = DatabaseSnapshot.Take(dbPath, reference: after);
        var probeBackups = (Directory.Exists(backupDir)
                ? Directory.GetFiles(backupDir, "*.db").ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase))
            .Except(backupsBeforeProbe).ToList();

        Check("a deliberately failing migration is reported as failed", !probeRan, "the runner reported success");
        Check("a failed migration leaves user_version unchanged", afterProbe.Version == after.Version,
            $"before {after.Version}, after {afterProbe.Version}");
        Check("a failed migration leaves every inquiry value unchanged",
            afterProbe.Fingerprint == beforeProbe.Fingerprint && afterProbe.InquiryCount == beforeProbe.InquiryCount,
            $"{beforeProbe.InquiryCount} row(s) before, {afterProbe.InquiryCount} after");
        Check("a failed migration still made a backup before it started", probeBackups.Count > 0,
            "nothing appeared in " + backupDir);
        foreach (string backup in probeBackups) Notes.Add("transaction-guard backup: " + backup);
        if (probeSummary.Length > 0) Notes.Add("transaction-guard message: " + probeSummary);

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

    /// <summary>
    /// Read-only description of ANY database copy. The file named is copied to the
    /// temp folder and the copy is read, so the original is never opened — not even
    /// to create a -wal sidecar. It answers one question: which version is this?
    /// </summary>
    private static int Describe(string source)
    {
        string temp = Path.Combine(Path.GetTempPath(),
            "g1-describe-" + Guid.NewGuid().ToString("N")[..8] + ".db");
        try
        {
            File.Copy(source, temp, overwrite: false);
            Snapshot snapshot = DatabaseSnapshot.Take(temp);
            Console.WriteLine("Source (copied, never opened) : " + source);
            Console.WriteLine("user_version                  : " + snapshot.Version);
            Console.WriteLine("Inquiries                     : " + snapshot.InquiryCount + " row(s)");
            Console.WriteLine("inquiry columns               : " + string.Join(", ", snapshot.InquiryColumns));
            Console.WriteLine("Projects table                : " +
                (snapshot.Tables.Contains(ProjectSchema.TableName) ? "present" : "absent") + "   (created by 1017)");
            Console.WriteLine("ClientId / ProjectRecordId    : " +
                (snapshot.InquiryColumns.Contains(ProjectSchema.InquiryClientColumn) ? "present" : "absent") +
                "   (created by 1017)");
            Console.WriteLine("linked inquiries              : " + snapshot.LinkedInquiryCount);
            Console.WriteLine("indexes                       : " + string.Join(", ", snapshot.Indexes));
            Console.WriteLine("integrity                     : " + DatabaseSnapshot.IntegrityCheck(temp));
            Console.WriteLine();
            Console.WriteLine(snapshot.Version >= ProjectSchema.MigrationVersion
                ? "Verdict: this copy is already at 1017. It cannot test the upgrade without a rewind."
                : "Verdict: this copy is BEFORE 1017 — a genuine legacy input for the test.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: could not describe " + source +
                              " (" + ex.GetType().Name + ": " + ex.Message + ")");
            return 2;
        }
        finally
        {
            foreach (string file in new[] { temp, temp + "-wal", temp + "-shm" })
                try { if (File.Exists(file)) File.Delete(file); } catch { /* temp only */ }
        }
    }

    /// <summary>
    /// The rewind is only safe if it removed exactly what migration 1017 created
    /// and nothing else. This compares the at-1017 shape with the rewound shape
    /// object by object, so a leftover table, index or column fails here instead
    /// of silently becoming part of the "before" side of the test.
    /// </summary>
    private static void CheckRewindShape(Snapshot atTip, Snapshot rewound, int expectedVersion)
    {
        var expectedTables = atTip.Tables
            .Where(t => !string.Equals(t, ProjectSchema.TableName, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedIndexes = atTip.Indexes
            .Where(i => !i.StartsWith("IX_Projects_", StringComparison.OrdinalIgnoreCase))
            .Where(i => !string.Equals(i, ProjectSchema.InquiryClientIndex, StringComparison.OrdinalIgnoreCase))
            .Where(i => !string.Equals(i, ProjectSchema.InquiryProjectIndex, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i, StringComparer.Ordinal)
            .ToList();
        var expectedColumns = atTip.InquiryColumns.Where(c => !IsLinkColumn(c)).ToList();

        Check("rewind set user_version to the step before 1017 (" + expectedVersion + ")",
            rewound.Version == expectedVersion, "actual " + rewound.Version);
        Check("rewind removed the Projects table and no other table",
            !rewound.Tables.Contains(ProjectSchema.TableName) &&
            expectedTables.SetEquals(rewound.Tables),
            Difference(expectedTables, rewound.Tables));
        Check("rewind removed 1017's indexes and no other index",
            expectedIndexes.SequenceEqual(rewound.Indexes, StringComparer.Ordinal),
            Difference(expectedIndexes, rewound.Indexes));
        Check("rewind removed exactly the two link columns",
            expectedColumns.SequenceEqual(rewound.InquiryColumns, StringComparer.OrdinalIgnoreCase),
            Difference(expectedColumns, rewound.InquiryColumns));
    }

    private static bool IsLinkColumn(string column) =>
        string.Equals(column, ProjectSchema.InquiryClientColumn, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(column, ProjectSchema.InquiryProjectColumn, StringComparison.OrdinalIgnoreCase);

    private static string Difference(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var wanted = expected.ToList();
        var found = actual.ToList();
        var missing = wanted.Except(found, StringComparer.OrdinalIgnoreCase).ToList();
        var extra = found.Except(wanted, StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count == 0 && extra.Count == 0) return "";
        return (missing.Count > 0 ? "missing " + string.Join(", ", missing) : "") +
               (extra.Count > 0 ? (missing.Count > 0 ? "; " : "") + "unexpected " + string.Join(", ", extra) : "");
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
        text.AppendLine("| Migration 1017 executed | " +
                        (rewind && upgraded && after.Version == chainTip
                            ? "yes — rewound to " + before.Version + ", then 1017 applied"
                            : "no — this run did not apply 1017") + " |");
        text.AppendLine("| Runner reported success | " + (upgraded ? "yes" : "no") + " |");
        text.AppendLine("| Project rows in the database after the run | " + after.ProjectCount + " |");
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
