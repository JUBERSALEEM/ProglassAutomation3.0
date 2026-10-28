// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.a.1.Project.cs
// MODULE: step5 — Project Records & Inquiry Integration, domain model.
//
// A project is the job or site a client's inquiries belong to. It is a record
// of its own, not a field on an inquiry: the same site generates several
// inquiries, is quoted once, and outlives any single one of them.
//
// The specification (docs/12 §step5) left three questions open. They are
// answered here, conservatively, and recorded in step5.e.1:
//
//   1. Is a project record required at this stage, or is it only the client
//      link?  — Required. The module is "Project Records"; a screen that only
//      linked clients would be step2 with extra steps.
//
//   2. Does a project carry a number, a status, or both?  — Both. A number is
//      how the office refers to the job (PRJ-0001); a status is how it stops
//      being current without being deleted.
//
//   3. Can one inquiry belong to several projects?  — No. One inquiry holds at
//      most one project link, because every column the product already has is
//      singular and a join table would be a bigger change than the stage needs.
//      Widening it later adds a table; it never rewrites these columns.
//
// Nothing here is derived by name matching: ClientId is an id into Clients, and
// ClientName is a SNAPSHOT taken when the project is saved, so a project for a
// one-off customer still reads correctly with no client row at all.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.Projects
{
    /// <summary>The two states a project can be in. There is no third.</summary>
    public static class ProjectStatus
    {
        public const string Active = "Active";
        public const string Closed = "Closed";

        /// <summary>Every legal value, in display order.</summary>
        public static readonly string[] All = { Active, Closed };

        /// <summary>
        /// The status a reader sees when the stored value is not one of the two.
        /// A database can carry anything; the screen must still open.
        /// </summary>
        public static string Normalize(string? value) =>
            string.Equals(value, Closed, StringComparison.OrdinalIgnoreCase) ? Closed : Active;

        public static bool IsDefined(string? value) =>
            string.Equals(value, Active, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, Closed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One project record. Persisted in the ERP file by migration 1017.</summary>
    [Table("Projects")]
    public sealed class Project
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// The office's reference for the job (PRJ-0001). Unique, allocated from
        /// the highest existing number, and enforced by a UNIQUE index so a
        /// collision is refused by SQLite rather than trusted to the caller.
        /// </summary>
        [Required, MaxLength(ProjectRules.NumberMaxLength)]
        public string ProjectNumber { get; set; } = "";

        [Required, MaxLength(ProjectRules.NameMaxLength)]
        public string ProjectName { get; set; } = "";

        /// <summary>
        /// Stable id into Clients, or null for a one-off customer. Never matched
        /// by name: a project saved before its client existed keeps its snapshot
        /// and stays readable.
        /// </summary>
        public int? ClientId { get; set; }

        /// <summary>
        /// The client name as it was when this project was saved. A snapshot, not
        /// a copy kept in sync: renaming the client later must not rewrite
        /// history, exactly as an inquiry keeps its own customer name.
        /// </summary>
        [MaxLength(ProjectRules.NameMaxLength)]
        public string ClientName { get; set; } = "";

        /// <summary>Site or area, as entered. Optional.</summary>
        [MaxLength(ProjectRules.LocationMaxLength)]
        public string Location { get; set; } = "";

        /// <summary>Free description of the work. Optional.</summary>
        public string Description { get; set; } = "";

        [Required, MaxLength(ProjectRules.StatusMaxLength)]
        public string Status { get; set; } = ProjectStatus.Active;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>Who created / last changed it, as a stamped user name.</summary>
        [MaxLength(ProjectRules.ActorMaxLength)]
        public string CreatedBy { get; set; } = "";

        [MaxLength(ProjectRules.ActorMaxLength)]
        public string UpdatedBy { get; set; } = "";

        public bool IsClosed => string.Equals(Status, ProjectStatus.Closed, StringComparison.Ordinal);
    }

    /// <summary>What the editor collects, before validation and normalization.</summary>
    public sealed class ProjectDraft
    {
        public string ProjectNumber { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public int? ClientId { get; set; }
        public string ClientName { get; set; } = "";
        public string Location { get; set; } = "";
        public string Description { get; set; } = "";
        public string Status { get; set; } = ProjectStatus.Active;

        /// <summary>Edit mode: an existing project loaded into the editor.</summary>
        public static ProjectDraft FromProject(Project project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            return new ProjectDraft
            {
                ProjectNumber = project.ProjectNumber,
                ProjectName = project.ProjectName,
                ClientId = project.ClientId,
                ClientName = project.ClientName,
                Location = project.Location,
                Description = project.Description,
                Status = project.Status
            };
        }
    }
}
