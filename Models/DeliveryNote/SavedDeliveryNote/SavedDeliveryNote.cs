using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote
{
    /// <summary>
    /// EF Core entity for one delivery note row in deliverynotes.db.
    ///
    /// Uses JSON-blob strategy (same as PI + JO):
    /// FullSnapshotJson contains entire DN state for load/export.
    /// Indexed columns surfaced for list queries without JSON parsing.
    ///
    /// BUG-10: DeliveryDate / DeliveryTime defaults removed from property
    ///         level — set explicitly by mapper on creation only.
    /// BUG-11: CreatedAt / UpdatedAt defaults removed from property
    ///         level — set explicitly by repository on insert/update only.
    ///
    /// Phase 10.7: Added TotalPreviouslyDeliveredQty (Improvement 7).
    ///             Added TaxInvoiceId + HasTaxInvoice (Improvement 6).
    ///             ⚠️ DB schema change — delete deliverynotes.db before first run.
    ///
    /// CHAT 19: Added IsMasterDn + LastUpdatedFromDnId.
    ///   IsMasterDn = true means this DN is the auto-generated Master
    ///   reference tracker for a Job Order. Created once when JO is saved.
    ///   Never manually created or edited. Hidden from normal DN list.
    ///   Updated automatically after every real DN dispatch.
    ///   ⚠️ DB schema change — delete deliverynotes.db before first run.
    /// </summary>
    [Table("DeliveryNotes")]
    public class SavedDeliveryNote
    {
        [Key]
        public int Id { get; set; }

        /// <summary>User-facing DN reference, e.g. "DN-2026-001-01". UNIQUE.</summary>
        [Required, MaxLength(50)]
        public string DeliveryNoteId { get; set; } = "";

        /// <summary>Parent Job Order reference, e.g. "JOB-2026-001".</summary>
        [Required, MaxLength(50)]
        public string JobOrderId { get; set; } = "";

        /// <summary>Proforma Invoice reference, e.g. "PI-2026-001".</summary>
        [MaxLength(50)]
        public string ProformaId { get; set; } = "";

        /// <summary>Delivery sequence within this JO (1, 2, 3...).
        /// Master DN uses sequence = 0.</summary>
        public int SequenceNumber { get; set; } = 1;

        // ── Indexed searchable fields ────────────────────────────────────

        [MaxLength(200)]
        public string CustomerName { get; set; } = "";

        [MaxLength(200)]
        public string ProjectName { get; set; } = "";

        /// <summary>Draft | Dispatched | Delivered | Signed | Received</summary>
        [MaxLength(50)]
        public string Status { get; set; } = "Draft";

        // ── Void / soft-close (ERP lifecycle hardening — Phase 1) ─────────
        // Additive, nullable. A live delivery note is IsVoid=false with null
        // audit fields — byte-identical to legacy rows.

        /// <summary>True when the delivery note was soft-closed (voided)
        /// instead of deleted.</summary>
        public bool IsVoid { get; set; }

        /// <summary>UTC timestamp the void was applied. Null when live.</summary>
        public DateTime? VoidedAtUtc { get; set; }

        /// <summary>Username that voided the delivery note. Null when live.</summary>
        [MaxLength(100)]
        public string? VoidedBy { get; set; }

        /// <summary>Mandatory reason captured when voiding. Null when live.</summary>
        [MaxLength(500)]
        public string? VoidReason { get; set; }

        /// <summary>Original | Dummy | Rework | Return</summary>
        [MaxLength(50)]
        public string DeliveryType { get; set; } = "Original";

        /// <summary>Free of charge flag — entire DN is FOC when true.</summary>
        public bool IsFoc { get; set; }

        // ── CHAT 19 — Master DN fields ───────────────────────────────────

        /// <summary>
        /// CHAT 19: True when this DN is the Master (reference) DN
        /// for a Job Order.
        ///
        /// Master DNs:
        ///   - Have DeliveryType = "Dummy"
        ///   - Have SequenceNumber = 0
        ///   - Are auto-created when a JO is saved
        ///   - Are NEVER manually created or edited by user
        ///   - Are hidden from all normal DN list tabs (0-5)
        ///   - Appear ONLY in Tab 6 (Master DNs)
        ///   - Are updated every time a real DN is dispatched
        ///   - Are READ-ONLY in the detail view
        ///
        /// DB schema addition — delete deliverynotes.db if upgrading.
        /// </summary>
        public bool IsMasterDn { get; set; }

        /// <summary>
        /// CHAT 19: Tracks which real DN last triggered a Master DN update.
        /// e.g. "DN-2026-001-02" = the second delivery updated this master.
        /// Used for audit trail. No business logic depends on it.
        /// Empty string = never updated (just created).
        /// </summary>
        [MaxLength(50)]
        public string LastUpdatedFromDnId { get; set; } = "";

        // ── Delivery logistics ───────────────────────────────────────────

        [MaxLength(200)]
        public string DriverName { get; set; } = "";

        [MaxLength(50)]
        public string VehicleNumber { get; set; } = "";

        [MaxLength(200)]
        public string ReceiverName { get; set; } = "";

        /// <summary>
        /// Set explicitly by mapper at creation time.
        /// BUG-10: Removed DateTime.Now default at property level.
        /// </summary>
        public DateTime DeliveryDate { get; set; }

        /// <summary>
        /// Stored as "HH:mm". Set explicitly by mapper at creation time.
        /// BUG-10: Removed default at property level.
        /// </summary>
        [MaxLength(10)]
        public string DeliveryTime { get; set; } = "";

        [MaxLength(500)]
        public string Notes { get; set; } = "";

        // ── Quantity summary (for quick list display) ────────────────────

        public int TotalOrderedQty { get; set; }
        public int TotalDeliveredQty { get; set; }
        public int TotalBalanceQty { get; set; }

        /// <summary>
        /// Phase 10.7 (Improvement 7) — sum of PreviouslyDeliveredQty
        /// across all items on this DN at the time it was saved.
        /// On Master DN: equals total delivered across all real DNs.
        /// </summary>
        public int TotalPreviouslyDeliveredQty { get; set; }

        // ── Tax Invoice link ─────────────────────────────────────────────

        /// <summary>Empty string = no Tax Invoice generated yet.</summary>
        [MaxLength(50)]
        public string TaxInvoiceId { get; set; } = "";

        /// <summary>True when a Tax Invoice has been generated from this DN.</summary>
        public bool HasTaxInvoice { get; set; }

        // ── Full snapshot ────────────────────────────────────────────────

        [Required]
        public string FullSnapshotJson { get; set; } = "{}";

        // ── Timestamps ───────────────────────────────────────────────────

        /// <summary>BUG-11: Set explicitly by repository in InsertAsync only.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>BUG-11: Set explicitly by repository in InsertAsync + UpdateAsync.</summary>
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// ERP hardening (Phase 5): optimistic-concurrency counter, incremented
        /// by the repository on every successful UpdateAsync. See
        /// SavedTaxInvoice.RowVersion for the full rationale.
        /// </summary>
        public int RowVersion { get; set; }

        // ── Navigation (EF relationships) ────────────────────────────────

        public List<DeliveryNoteItem> Items { get; set; } = new();
        public List<DeliveryNoteAttachment> Attachments { get; set; } = new();
    }
}