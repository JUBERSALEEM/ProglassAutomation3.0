// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.b.3.IProjectRepository.cs
// MODULE: step5 — project persistence and the inquiry link (the port).
//
// Two halves, and the boundary between them is the point of this file:
//
//   • Project records — this module owns them. Create, update, close, re-open.
//     There is no delete: a closed project stays readable, exactly as an
//     inactive client does.
//
//   • The inquiry link — this module owns the LINK, not the inquiry. Attaching
//     an inquiry writes one column (`ProjectRecordId`); it never rewrites an
//     inquiry's number, customer, text or timestamps. Detaching sets it back to
//     NULL. Neither operation can create or delete an inquiry.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ProGlassAutomation.Models.Projects;

namespace ProGlassAutomation.Services.Projects
{
    /// <summary>What the register asks for.</summary>
    public sealed class ProjectQuery
    {
        /// <summary>Free text over number, name, client and location.</summary>
        public string Search { get; init; } = "";

        /// <summary>False shows every status; true shows closed projects too.</summary>
        public bool IncludeClosed { get; init; } = true;

        public int MaxRows { get; init; } = 5000;

        /// <summary>
        /// Scoping rule (owner decision, step2): a non-administrator sees only
        /// what is theirs. Null means "everything", which only the shell opens.
        /// </summary>
        public int? ScopeToClientId { get; init; }
    }

    /// <summary>One row of the project register, formatted for the grid.</summary>
    public sealed class ProjectListRow
    {
        public int Id { get; init; }
        public string ProjectNumber { get; init; } = "";
        public string ProjectName { get; init; } = "";
        public string ClientName { get; init; } = "";
        public string Location { get; init; } = "";
        public string Status { get; init; } = ProjectStatus.Active;
        public int LinkedInquiries { get; init; }
        public DateTime UpdatedAt { get; init; }

        public bool IsClosed => string.Equals(Status, ProjectStatus.Closed, StringComparison.Ordinal);

        public string StatusText => IsClosed ? "Closed" : "Active";

        public string ClientText => string.IsNullOrWhiteSpace(ClientName) ? "No client recorded" : ClientName;

        public string LocationText => string.IsNullOrWhiteSpace(Location) ? "—" : Location;

        public string LinkedInquiriesText => LinkedInquiries == 1 ? "1 inquiry" : LinkedInquiries + " inquiries";

        public string UpdatedText => UpdatedAt == default
            ? "—"
            : UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

        /// <summary>Identity in the tooltip, so a trimmed cell still answers.</summary>
        public string Tooltip => $"{ProjectNumber} · {ProjectName}\n{ClientText}"
                                 + (string.IsNullOrWhiteSpace(Location) ? "" : "\n" + Location)
                                 + "\n" + StatusText + " · " + LinkedInquiriesText;
    }

    /// <summary>Outcome of a create/update/status call.</summary>
    public sealed class ProjectSaveResult
    {
        public bool Succeeded { get; init; }
        public Project? Project { get; init; }

        /// <summary>Operator-facing message; empty on success.</summary>
        public string Message { get; init; } = "";

        public static ProjectSaveResult Ok(Project project) => new() { Succeeded = true, Project = project };

        public static ProjectSaveResult Fail(string message) => new() { Succeeded = false, Message = message };
    }

    /// <summary>Outcome of an attach/detach call.</summary>
    public sealed class InquiryLinkResult
    {
        public bool Succeeded { get; init; }
        public string Message { get; init; } = "";
        public int Affected { get; init; }

        public static InquiryLinkResult Ok(int affected) => new() { Succeeded = true, Affected = affected };

        public static InquiryLinkResult Fail(string message) => new() { Succeeded = false, Message = message };
    }

    /// <summary>A client the editor may link a project to.</summary>
    public sealed class ProjectClientChoice
    {
        public int Id { get; init; }
        public string DisplayName { get; init; } = "";

        public override string ToString() => DisplayName;
    }

    /// <summary>An inquiry, as the link screen needs it — read-only facts.</summary>
    public sealed class InquiryCandidateRow
    {
        public int Id { get; init; }
        public string InquiryNumber { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public string Status { get; init; } = "";
        public DateTime ReceivedAt { get; init; }
        public string ProjectName { get; init; } = "";

        public bool IsLinked => !string.IsNullOrWhiteSpace(ProjectName);

        public string ReceivedText => ReceivedAt == default
            ? "—"
            : ReceivedAt.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.CurrentCulture);

        public string LinkText => IsLinked ? ProjectName : "Not linked";
    }

    /// <summary>Project records and the inquiry link.</summary>
    public interface IProjectRepository
    {
        /// <summary>Brings the ERP file to the current schema (migration 1017). Idempotent.</summary>
        Task InitializeAsync(CancellationToken ct = default);

        /// <summary>Register rows, active first then by number.</summary>
        Task<IReadOnlyList<ProjectListRow>> QueryAsync(ProjectQuery query, CancellationToken ct = default);

        Task<Project?> GetAsync(int id, CancellationToken ct = default);

        /// <summary>
        /// Inserts a project. A blank draft number is allocated here, one above the
        /// highest existing sequence, so a closed project's number is never reused.
        /// </summary>
        Task<ProjectSaveResult> CreateAsync(ProjectDraft draft, CancellationToken ct = default);

        Task<ProjectSaveResult> UpdateAsync(int id, ProjectDraft draft, CancellationToken ct = default);

        /// <summary>Active ⇄ Closed. Never deletes a row.</summary>
        Task<ProjectSaveResult> SetStatusAsync(int id, string status, CancellationToken ct = default);

        /// <summary>The number a new project would receive, for the editor to show.</summary>
        Task<string> SuggestNumberAsync(CancellationToken ct = default);

        /// <summary>Active clients, for the project's client picker.</summary>
        Task<IReadOnlyList<ProjectClientChoice>> GetClientChoicesAsync(CancellationToken ct = default);

        /// <summary>Inquiries already attached to a project.</summary>
        Task<IReadOnlyList<InquiryCandidateRow>> GetLinkedInquiriesAsync(int projectId, CancellationToken ct = default);

        /// <summary>Inquiries not attached to any project, optionally filtered by text.</summary>
        Task<IReadOnlyList<InquiryCandidateRow>> GetUnlinkedInquiriesAsync(
            string search, int maxRows = 200, CancellationToken ct = default);

        /// <summary>Attaches one inquiry to a project. Writes one column; never touches the rest.</summary>
        Task<InquiryLinkResult> AttachInquiryAsync(int inquiryId, int projectId, CancellationToken ct = default);

        /// <summary>Detaches one inquiry. Sets the link back to NULL.</summary>
        Task<InquiryLinkResult> DetachInquiryAsync(int inquiryId, CancellationToken ct = default);

        /// <summary>True when the Projects table and both link columns are present.</summary>
        Task<bool> IsSchemaReadyAsync(CancellationToken ct = default);
    }
}
