// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/Audit/ActivityLogEntry.cs
// PROJECT: ProGlassAutomation
// MODULE: Audit / Activity Logging
//
// One immutable audit-trail row. Every meaningful user action in every
// section writes one of these through Services/Audit/AuditService.cs.
//
// Rows are INSERT-ONLY: the app never updates or deletes them, so the
// trail is trustworthy. (System Center can prune entries older than a
// retention window — that operation is itself audited.)
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.Audit
{
    /// <summary>
    /// One audit-trail entry describing a user action.
    /// Example: user "juber" | section "Delivery Note" | action "Saved"
    ///          | details "DN-2026-003-01 saved (12 pcs)".
    /// </summary>
    [Table("ActivityLog")]
    public class ActivityLogEntry
    {
        [Key]
        public int Id { get; set; }

        /// <summary>UTC timestamp of the action.</summary>
        public DateTime TimestampUtc { get; set; }

        /// <summary>Username of the active user at the time.</summary>
        [Required, MaxLength(60)]
        public string Username { get; set; } = "";

        /// <summary>Display name captured at the time of action.</summary>
        [MaxLength(120)]
        public string UserDisplayName { get; set; } = "";

        /// <summary>User role captured at the time of action.</summary>
        [MaxLength(30)]
        public string UserRole { get; set; } = "";

        /// <summary>Machine/workstation that produced the entry.</summary>
        [MaxLength(80)]
        public string MachineName { get; set; } = "";

        /// <summary>Application version captured with the entry.</summary>
        [MaxLength(40)]
        public string AppVersion { get; set; } = "";

        /// <summary>Local timezone of the workstation at the time of write.</summary>
        [MaxLength(80)]
        public string LocalTimeZone { get; set; } = "";

        /// <summary>Unique operation id for this audit write.</summary>
        [MaxLength(40)]
        public string OperationId { get; set; } = "";

        /// <summary>
        /// Module where the action happened.
        /// Use constants from Services/Audit/AuditSections.cs
        /// ("Sheet Store", "Proforma Invoice", "Job Order", ...).
        /// </summary>
        [Required, MaxLength(60)]
        public string Section { get; set; } = "";

        /// <summary>Short verb: "Created", "Saved", "Deleted", ...</summary>
        [Required, MaxLength(60)]
        public string Action { get; set; } = "";

        /// <summary>Free-text details (record ids, quantities, ...).</summary>
        [MaxLength(1000)]
        public string Details { get; set; } = "";

        /// <summary>Optional entity type hint ("PI", "JO", "DN", "TI").</summary>
        [MaxLength(20)]
        public string EntityType { get; set; } = "";

        /// <summary>Optional entity id ("PI-2026-001", ...).</summary>
        [MaxLength(60)]
        public string EntityId { get; set; } = "";

        /// <summary>Session that produced this entry (v2).</summary>
        [MaxLength(40)]
        public string SessionId { get; set; } = "";

        /// <summary>
        /// Structured before/after change set (JSON of List&lt;AuditChange&gt;).
        /// Empty when the entry carries no field-level diff (v2).
        /// </summary>
        public string ChangesJson { get; set; } = "";

        /// <summary>
        /// PHASE D — the human WHY behind the action ("price corrected
        /// after supplier re-quote"). Optional and free-text: the audit
        /// trail already records WHAT changed, this records the reason a
        /// reviewer would otherwise have to guess.
        /// Empty for entries written without one.
        /// </summary>
        [MaxLength(500)]
        public string Reason { get; set; } = "";
    }
}
