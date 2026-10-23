// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Models/Inquiry/InquiryInboxState.cs
// MODULE: Inquiry — website inbox state (local ledger).
//
// One row per website submission the inbox has delivered locally.
// This is the DESKTOP's ledger of the cloud-to-Windows handoff; the
// remote ledger lives in Supabase (inquiry_customer_deliveries) and the
// two agree via the acknowledgement RPC. State values:
//
//   downloaded — READY folder present and verified on disk;
//   ingested   — a local InquiryRecord was created from it (LocalInquiryId);
//   failed     — last attempt failed (Error carries the reason).
// ═══════════════════════════════════════════════════════════════════════

using System.ComponentModel.DataAnnotations;

namespace ProGlassAutomation.Models.Inquiry
{
    public static class InquiryInboxStates
    {
        public const string Downloaded = "downloaded";
        public const string Ingested = "ingested";
        public const string Failed = "failed";
    }

    public sealed class InquiryInboxItem
    {
        [Key, MaxLength(40)] public string SubmissionId { get; set; } = "";
        /// <summary>Human reference from the website (WEB-YYYY-NNNNNN).</summary>
        [MaxLength(30)] public string Reference { get; set; } = "";
        [MaxLength(20)] public string State { get; set; } = InquiryInboxStates.Downloaded;
        [MaxLength(64)] public string ManifestSha256 { get; set; } = "";
        public int DocumentCount { get; set; }
        /// <summary>"unassigned" or "assigned/{ESTIMATOR_CODE}".</summary>
        [MaxLength(40)] public string RoutingKey { get; set; } = "";
        [MaxLength(30)] public string PrimaryEstimatorCode { get; set; } = "";
        /// <summary>Raw assignment snapshot JSON (role/code/displayName/assignedAt).</summary>
        public string AssignmentSnapshot { get; set; } = "[]";
        /// <summary>ISO-8601 UTC timestamps (stored as TEXT by SQLite).</summary>
        public string DownloadedAt { get; set; } = "";
        public string IngestedAt { get; set; } = "";
        public int? LocalInquiryId { get; set; }
        [MaxLength(200)] public string LocalInquiryNumber { get; set; } = "";
        [MaxLength(500)] public string Error { get; set; } = "";
    }
}
