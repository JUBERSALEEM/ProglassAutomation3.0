// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.f.tests/step5.f.5.ProjectBehaviourChecks.Link.cs
// MODULE: step5 — behaviour checks, part 2: persistence, the inquiry link and
// the cross-module boundary.
//
// Split from step5.f.4 to stay inside the 400-line rule (docs/12, owner rule K).
// The two files are one partial class and one entry point; step5.f.4 holds the
// rules, the migration check and the shared helpers.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using ProGlassAutomation.Models.Projects;
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
    // ── Round-trip ───────────────────────────────────────────────────────

    private static void CheckRoundTrip()
    {
        WithTempFolder("roundtrip", () =>
        {
            var repository = new ProjectRepository();
            repository.InitializeAsync().GetAwaiter().GetResult();

            Equal("PRJ-0001", repository.SuggestNumberAsync().GetAwaiter().GetResult(),
                "a blank register suggests PRJ-0001");

            ProjectSaveResult first = repository.CreateAsync(new ProjectDraft
            {
                ProjectName = "Al Noor Tower",
                Location = "Ajman",
                Description = "Shopfront glazing"
            }).GetAwaiter().GetResult();
            True(first.Succeeded, "the first project saves: " + first.Message);
            Equal("PRJ-0001", first.Project?.ProjectNumber ?? "", "the blank number was allocated on save");

            ProjectSaveResult second = repository.CreateAsync(new ProjectDraft
            {
                ProjectName = "Marina Offices"
            }).GetAwaiter().GetResult();
            True(second.Succeeded, "the second project saves: " + second.Message);
            Equal("PRJ-0002", second.Project?.ProjectNumber ?? "", "the second number follows the first");

            int id = first.Project!.Id;
            ProjectSaveResult updated = repository.UpdateAsync(id, new ProjectDraft
            {
                ProjectName = "Al Noor Tower — Phase 2",
                ProjectNumber = "PRJ-0001",
                Location = "Sharjah",
                Description = "Shopfront glazing and doors"
            }).GetAwaiter().GetResult();
            True(updated.Succeeded, "the project updates: " + updated.Message);
            Equal("2", QueryLong("SELECT COUNT(*) FROM Projects;").ToString(CultureInfo.InvariantCulture),
                "an update writes into the same row instead of adding one");

            // "After a restart": a NEW repository instance, reading the file.
            var restarted = new ProjectRepository();
            restarted.InitializeAsync().GetAwaiter().GetResult();
            Project? reloaded = restarted.GetAsync(id).GetAwaiter().GetResult();
            True(reloaded != null, "the project reloads through a fresh repository");
            Equal("Al Noor Tower — Phase 2", reloaded?.ProjectName ?? "", "the updated name persisted");
            Equal("Sharjah", reloaded?.Location ?? "", "the updated location persisted");

            ProjectSaveResult closed = restarted.SetStatusAsync(id, ProjectStatus.Closed)
                .GetAwaiter().GetResult();
            True(closed.Succeeded, "the project closes: " + closed.Message);
            Equal(ProjectStatus.Closed, restarted.GetAsync(id).GetAwaiter().GetResult()?.Status ?? "",
                "the closed status persisted");
            Equal("2", QueryLong("SELECT COUNT(*) FROM Projects;").ToString(CultureInfo.InvariantCulture),
                "closing does not delete the row");

            ProjectSaveResult renamed = restarted.UpdateAsync(id, new ProjectDraft
            {
                ProjectName = "Al Noor Tower — Phase 2",
                ProjectNumber = "PRJ-0100",
                Location = "Sharjah"
            }).GetAwaiter().GetResult();
            True(!renamed.Succeeded, "a closed project keeps its number: the rename is refused");
            Equal("PRJ-0001", restarted.GetAsync(id).GetAwaiter().GetResult()?.ProjectNumber ?? "",
                "the closed project's number is unchanged");

            ProjectSaveResult reopened = restarted.SetStatusAsync(id, ProjectStatus.Active)
                .GetAwaiter().GetResult();
            True(reopened.Succeeded, "the project re-opens: " + reopened.Message);
            Equal(ProjectStatus.Active, restarted.GetAsync(id).GetAwaiter().GetResult()?.Status ?? "",
                "re-opening persisted");

            ProjectSaveResult refused = restarted.CreateAsync(new ProjectDraft { ProjectName = "  " })
                .GetAwaiter().GetResult();
            True(!refused.Succeeded, "a blank name is refused by the write path");
            Equal("2", QueryLong("SELECT COUNT(*) FROM Projects;").ToString(CultureInfo.InvariantCulture),
                "a refused save adds no row");

            ProjectSaveResult ghostClient = restarted.CreateAsync(new ProjectDraft
            {
                ProjectName = "Ghost client job",
                ClientId = 999999
            }).GetAwaiter().GetResult();
            True(!ghostClient.Succeeded, "a client id that names no client is refused");
            Equal("2", QueryLong("SELECT COUNT(*) FROM Projects;").ToString(CultureInfo.InvariantCulture),
                "the refused client reference adds no row");
        });
    }

    // ── Duplicate number ─────────────────────────────────────────────────

    private static void CheckDuplicateNumber()
    {
        WithTempFolder("duplicate", () =>
        {
            var repository = new ProjectRepository();
            repository.InitializeAsync().GetAwaiter().GetResult();

            repository.CreateAsync(new ProjectDraft { ProjectName = "First job" }).GetAwaiter().GetResult();
            ProjectSaveResult duplicate = repository.CreateAsync(new ProjectDraft
            {
                ProjectName = "Second job",
                ProjectNumber = "PRJ-0001"
            }).GetAwaiter().GetResult();

            True(!duplicate.Succeeded, "a duplicate number is refused");
            True(duplicate.Message.Contains("number", StringComparison.OrdinalIgnoreCase),
                "the refusal explains that the number is taken, not that the save failed");
            Equal("1", QueryLong("SELECT COUNT(*) FROM Projects;").ToString(CultureInfo.InvariantCulture),
                "the refused duplicate added no row");
        });
    }

    // ── Inquiry link ─────────────────────────────────────────────────────

    private static void CheckInquiryLink()
    {
        WithTempFolder("link", () =>
        {
            var repository = new ProjectRepository();
            repository.InitializeAsync().GetAwaiter().GetResult();

            ProjectSaveResult project = repository.CreateAsync(new ProjectDraft
            {
                ProjectName = "Link target"
            }).GetAwaiter().GetResult();
            True(project.Succeeded, "the target project saves: " + project.Message);
            int projectId = project.Project!.Id;

            int first = InsertInquiry("INQ-T-0001", "Test Customer One");
            int second = InsertInquiry("INQ-T-0002", "Test Customer Two");

            Dictionary<string, string> before = ReadRow(first);

            InquiryLinkResult attached = repository.AttachInquiryAsync(first, projectId)
                .GetAwaiter().GetResult();
            True(attached.Succeeded, "an unlinked inquiry attaches: " + attached.Message);

            Dictionary<string, string> after = ReadRow(first);
            // The heart of the cross-module boundary: one column changes, and
            // nothing else in the inquiry row does — not the text, not a
            // timestamp, not a legacy placeholder.
            var changed = before.Keys.Where(key =>
                !string.Equals(key, ProjectSchema.InquiryProjectColumn, StringComparison.OrdinalIgnoreCase) &&
                before[key] != after[key]).ToList();
            Equal("0", changed.Count.ToString(),
                "attaching changed no column except " + ProjectSchema.InquiryProjectColumn +
                (changed.Count == 0 ? "" : " (changed: " + string.Join(", ", changed) + ")"));
            Equal(projectId.ToString(CultureInfo.InvariantCulture),
                after[ProjectSchema.InquiryProjectColumn],
                "the link column holds the selected project");
            Equal("41", after["CustomerId"], "the legacy CustomerId placeholder is untouched");
            Equal("42", after["ProjectId"], "the legacy ProjectId placeholder is untouched");

            InquiryLinkResult twice = repository.AttachInquiryAsync(first, projectId)
                .GetAwaiter().GetResult();
            True(!twice.Succeeded, "attaching the same inquiry twice is refused");
            Equal("1", QueryLong("SELECT COUNT(*) FROM Inquiries WHERE \"Id\" = " + first +
                                 " AND \"" + ProjectSchema.InquiryProjectColumn + "\" = " + projectId + ";")
                    .ToString(CultureInfo.InvariantCulture),
                "the refused second attach changed nothing");

            True(!repository.AttachInquiryAsync(999999, projectId).GetAwaiter().GetResult().Succeeded,
                "attaching an inquiry that does not exist is refused");
            True(!repository.AttachInquiryAsync(second, 999999).GetAwaiter().GetResult().Succeeded,
                "attaching to a project that does not exist is refused");
            True(ReadRow(second)[ProjectSchema.InquiryProjectColumn] == "<null>",
                "the refused attach left the second inquiry unlinked");

            var linked = repository.GetLinkedInquiriesAsync(projectId).GetAwaiter().GetResult();
            True(linked.Any(row => row.Id == first), "the linked inquiry is listed under the project");
            var candidates = repository.GetUnlinkedInquiriesAsync("").GetAwaiter().GetResult();
            True(candidates.All(row => row.Id != first), "a linked inquiry is no longer offered for linking");
            True(candidates.Any(row => row.Id == second), "the unlinked inquiry is offered");

            InquiryLinkResult detached = repository.DetachInquiryAsync(first).GetAwaiter().GetResult();
            True(detached.Succeeded, "the inquiry detaches: " + detached.Message);
            Equal("<null>", ReadRow(first)[ProjectSchema.InquiryProjectColumn],
                "detaching sets the link back to NULL");
            True(!repository.DetachInquiryAsync(first).GetAwaiter().GetResult().Succeeded,
                "detaching an already-unlinked inquiry is refused");

            Dictionary<string, string> afterDetach = ReadRow(first);
            var changedByDetach = before.Keys.Where(key =>
                !string.Equals(key, ProjectSchema.InquiryProjectColumn, StringComparison.OrdinalIgnoreCase) &&
                before[key] != afterDetach[key]).ToList();
            Equal("0", changedByDetach.Count.ToString(),
                "attaching and detaching left every other column exactly as it was" +
                (changedByDetach.Count == 0 ? "" : " (changed: " + string.Join(", ", changedByDetach) + ")"));
        });
    }
}
