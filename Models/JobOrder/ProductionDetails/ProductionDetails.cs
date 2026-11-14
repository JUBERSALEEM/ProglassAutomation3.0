using System;

namespace ProGlassAutomation.Models.JobOrder.ProductionDetails
{
    /// <summary>
    /// Production-specific fields for a Job Order.
    ///
    /// These fields are what makes a Job Order different from its source
    /// Proforma Invoice. Mutable — updated as the job progresses through
    /// the factory (cutting, processing, tempering, dispatch).
    ///
    /// Serialized as part of JobOrderSnapshot.Production.
    /// </summary>
    public class ProductionDetails
    {
        // ── status + priority (stored as strings for JSON safety) ─────────

        /// <summary>
        /// Current status: Draft / Released / InProduction / Completed / Delivered.
        /// String form of JobOrderStatus enum for JSON round-trip safety.
        /// </summary>
        public string Status { get; set; } = "Draft";

        /// <summary>
        /// Priority: High / Medium / Low.
        /// String form of JobPriority enum for JSON round-trip safety.
        /// </summary>
        public string Priority { get; set; } = "Medium";

        // ── delivery ──────────────────────────────────────────────────────

        /// <summary>Target delivery date. Nullable — not set until Released.</summary>
        public DateTime? DeliveryDate { get; set; }

        /// <summary>Site delivery contact person name.</summary>
        public string SiteDeliveryContact { get; set; } = string.Empty;

        /// <summary>Site delivery contact phone.</summary>
        public string SiteDeliveryPhone { get; set; } = string.Empty;

        /// <summary>
        /// Site delivery address. May differ from customer billing address
        /// (billing address stored in EmbeddedInvoice.Client.BillingAddress).
        /// </summary>
        public string SiteDeliveryAddress { get; set; } = string.Empty;

        // ── production assignment ─────────────────────────────────────────

        /// <summary>Production manager or team assigned. Free text for now.</summary>
        public string AssignedTo { get; set; } = string.Empty;

        // ── factory floor notes ───────────────────────────────────────────

        /// <summary>
        /// Cutting / processing instructions for factory floor.
        /// Multi-line. Shown to production staff, not client.
        /// </summary>
        public string CuttingInstructions { get; set; } = string.Empty;

        /// <summary>
        /// Drawing / DWG file reference (filename or drawing number).
        /// Free text — file storage integration is a future phase.
        /// </summary>
        public string DrawingReference { get; set; } = string.Empty;

        /// <summary>
        /// Internal notes not shown to client. Anything production/logistics
        /// staff need to remember.
        /// </summary>
        public string InternalNotes { get; set; } = string.Empty;

        // ── audit ─────────────────────────────────────────────────────────

        /// <summary>Timestamp of last production detail edit.</summary>
        public DateTime LastEditedAt { get; set; } = DateTime.Now;

        /// <summary>Windows username of the last person who edited.</summary>
        public string LastEditedBy { get; set; } = Environment.UserName;
    }
}