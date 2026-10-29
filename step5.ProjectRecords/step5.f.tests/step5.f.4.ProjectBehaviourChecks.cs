// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.f.tests/step5.f.4.ProjectBehaviourChecks.cs
// MODULE: step5 — behaviour checks (G1-08).
//
// The foundation review found that step5 was the only module with no behaviour
// tests, and it is the module that performs the one cross-module write into
// Inquiry data. This file closes that gap for exactly the cases docs/12 §step5
// says the module must prove — nothing invented, nothing beyond the approved
// scope:
//
//   • numbering from the highest existing sequence, never the count, so a
//     closed project's number cannot be handed to the next one created;
//   • the number shape, the required name and the two legal statuses;
//   • migration 1017 on an ISOLATED database, and a second run being a no-op;
//   • round-trip persistence: create, update-in-the-same-row, status changes,
//     and re-reading the stored values through a fresh repository instance;
//   • a duplicate number being refused by the database, not by the UI;
//   • linking an inquiry: one column changes and NOTHING else in the row does;
//     attaching twice is refused; attaching to a project that does not exist is
//     refused; detach sets the column back to NULL and detaching twice is
//     refused; a linked inquiry stops being offered as a candidate;
//   • the legacy placeholders CustomerId/ProjectId keep their values through
//     every link operation;
//   • deactivating a client is not deletion: the project keeps its ClientId;
//   • no delete path anywhere in the repository interface.
//
// Every database here is a throwaway folder under %TEMP% reached through
// AppDataRoot.TestFolderOverride. The production %APPDATA% folder is never
// touched: the same discipline step2.f/step3.f/step4.f already use.
//
// Exit code 0 = every check passed. Windows/.NET 8 only.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using ProGlassAutomation.Models.Projects;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Database.SchemaMigrations;
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
    private static int _passed;
    private static readonly List<string> Failures = new();

    public static int Run()
    {
        Console.WriteLine("step5 — project behaviour checks (G1-08)");
        Console.WriteLine("=======================================");

        Run("numbering and rules", CheckRules);
        Run("migration 1017 on an isolated database", CheckMigration);
        Run("round-trip persistence", CheckRoundTrip);
        Run("duplicate number refused by the database", CheckDuplicateNumber);
        Run("inquiry linking changes one column and nothing else", CheckInquiryLink);
        Run("deactivating a client does not remove the project's reference", CheckClientDeactivation);
        Run("no delete path in the module", CheckNoDeletePath);

        Console.WriteLine();
        Console.WriteLine($"step5 project checks: {_passed} passed, {Failures.Count} failed.");
        foreach (string failure in Failures) Console.WriteLine("  FAIL: " + failure);
        if (Failures.Count == 0)
            Console.WriteLine("PASS: project numbering, persistence, status, the inquiry link and its limits.");
        return Failures.Count == 0 ? 0 : 1;
    }

    // ── Rules (no database) ──────────────────────────────────────────────

    private static void CheckRules()
    {
        // One above the highest sequence — not one above the count. This is the
        // rule docs/12 states: closing a project must never recycle its number.
        Equal("PRJ-0001", ProjectRules.NextNumber(Array.Empty<string>()), "the first number is PRJ-0001");
        Equal("PRJ-0003", ProjectRules.NextNumber(new[] { "PRJ-0001", "PRJ-0002" }),
            "the next number follows the highest existing one");
        Equal("PRJ-0004", ProjectRules.NextNumber(new[] { "PRJ-0001", "PRJ-0003" }),
            "a gap is not filled: a closed project's number is never reused");
        Equal("PRJ-10000", ProjectRules.NextNumber(new[] { "PRJ-9999" }),
            "the sequence continues past the padding instead of wrapping");
        Equal("PRJ-0001", ProjectRules.NextNumber(new[] { "", "ABC", "PRJ-", "PRJ-00X1", "0007" }),
            "malformed stored numbers are ignored, not counted");

        True(ProjectRules.IsValidNumber("PRJ-0001"), "PRJ-0001 is a valid number");
        True(ProjectRules.IsValidNumber("prj-0001"), "case does not invalidate a number");
        True(!ProjectRules.IsValidNumber("PRJ-001"), "three digits is not the documented shape");
        True(!ProjectRules.IsValidNumber("PRJ-0001A"), "a trailing letter is refused");
        True(!ProjectRules.IsValidNumber("0001"), "a bare sequence is refused");

        True(ProjectRules.Validate(new ProjectDraft { ProjectName = "" }).ContainsKey("ProjectName"),
            "a blank project name is refused");
        True(ProjectRules.Validate(new ProjectDraft
            {
                ProjectName = "Al Noor Tower",
                ProjectNumber = "JOB-12"
            }).ContainsKey("ProjectNumber"), "a hand-typed number outside the shape is refused");
        True(ProjectRules.Validate(new ProjectDraft
            {
                ProjectName = "Al Noor Tower",
                Status = "Archived"
            }).ContainsKey("Status"), "a status other than Active/Closed is refused");
        Equal("0", ProjectRules.Validate(new ProjectDraft { ProjectName = "Al Noor Tower" }).Count.ToString(),
            "a name alone is a valid draft: the number is allocated on save");
        Equal("0", ProjectRules.Validate(new ProjectDraft
            {
                ProjectName = "One-off customer job",
                ClientId = null
            }).Count.ToString(), "a project without a client is legal");

        ProjectDraft normalized = ProjectRules.Normalize(new ProjectDraft
        {
            ProjectName = "  Al   Noor  Tower ",
            ProjectNumber = " prj-0007 ",
            Status = "closed"
        });
        Equal("Al Noor Tower", normalized.ProjectName, "text is trimmed and inner runs collapse");
        Equal("PRJ-0007", normalized.ProjectNumber, "the number is upper-cased and trimmed");
        Equal(ProjectStatus.Closed, normalized.Status, "the status is normalised to its stored form");

        Equal(ProjectRules.ComparisonKey("Al Noor"), ProjectRules.ComparisonKey("  AL   NOOR "),
            "the duplicate key ignores case and spacing (for warnings only)");
    }

    // ── Migration 1017 ───────────────────────────────────────────────────

    private static void CheckMigration()
    {
        WithTempFolder("migration", () =>
        {
            var repository = new ProjectRepository();
            repository.InitializeAsync().GetAwaiter().GetResult();

            // The tip is derived, never written as a literal: a check that pins
            // "1017" fails the day migration 1018 is added, and the failure would
            // look like a broken project module rather than a stale test.
            string chainTip = AppSchemaMigrations.Inquiries
                .Max(migration => migration.Version)
                .ToString(CultureInfo.InvariantCulture);
            True(repository.IsSchemaReadyAsync().GetAwaiter().GetResult(),
                "the schema probe reports the project schema ready");
            Equal(chainTip, QueryLong("PRAGMA user_version;").ToString(CultureInfo.InvariantCulture),
                "the chain reached its tip (" + chainTip + ")");
            True(TableExists("Projects"), "migration 1017 created the Projects table");
            True(ColumnExists("Inquiries", ProjectSchema.InquiryClientColumn),
                "Inquiries.ClientId exists");
            True(ColumnExists("Inquiries", ProjectSchema.InquiryProjectColumn),
                "Inquiries.ProjectRecordId exists");
            True(QueryLong("SELECT COUNT(*) FROM sqlite_master WHERE type='index' " +
                           "AND name='IX_Projects_Number';") == 1,
                "the unique project-number index exists");
            True(QueryLong("SELECT COUNT(*) FROM pragma_table_info('Inquiries') " +
                           "WHERE name IN ('CustomerId','ProjectId');") == 2,
                "the legacy placeholders are still present after 1017");

            // A second initialisation must be a no-op, not a second migration.
            repository.InitializeAsync().GetAwaiter().GetResult();
            Equal(chainTip, QueryLong("PRAGMA user_version;").ToString(CultureInfo.InvariantCulture),
                "re-initialising does not move the version");
            Equal("0", QueryLong("SELECT COUNT(*) FROM Projects;").ToString(CultureInfo.InvariantCulture),
                "initialising creates no project row of its own");
        });
    }
}
