using System;

namespace ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote
{
    /// <summary>
    /// Row model for the Delivery Note list UI. Only surfaces indexed
    /// columns (fast to load, no JSON deserialization for browsing).
    /// Full snapshot loaded only when user opens detail view.
    ///
    /// BUG-06: Removed ViewModelBase inheritance — pure POCO DTO.
    ///         Always wrapped in DeliveryNoteRowViewModel for UI binding.
    ///
    /// Phase 10.7: Added TotalPreviouslyDeliveredQty (Improvement 7).
    ///             Added TaxInvoiceId + HasTaxInvoice (Improvement 6).
    ///
    /// CHAT 19: Added IsMasterDn flag.
    ///          Master DNs are hidden from normal tabs.
    ///          Shown only in Tab 6 (Master DNs).
    ///          IsMasterDn = true means DeliveryType = "Dummy" AND
    ///          this record is the auto-generated reference tracker.
    /// </summary>
    public class DeliveryNoteListItem
    {
        public int Id { get; set; }
        public string DeliveryNoteId { get; set; } = "";
        public string JobOrderId { get; set; } = "";
        public string ProformaId { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public string ProjectName { get; set; } = "";

        /// <summary>Draft | Dispatched | Delivered | Signed | Received</summary>
        public string Status { get; set; } = "Draft";

        /// <summary>Original | Dummy | Rework | Return</summary>
        public string DeliveryType { get; set; } = "Original";

        public bool IsFoc { get; set; }
        public string DriverName { get; set; } = "";
        public string VehicleNumber { get; set; } = "";

        // ── CHAT 19 — Master DN flag ─────────────────────────────────────

        /// <summary>
        /// True when this item represents the Master DN for a Job Order.
        /// Master DNs are auto-created when a JO is saved.
        /// They are read-only reference trackers — never manually created.
        /// They are excluded from all normal list tabs (0–5).
        /// They appear ONLY in Tab 6 (Master DNs).
        /// Populated by ToListItem() in DeliveryNoteRepository.
        /// </summary>
        public bool IsMasterDn { get; set; }

        // ── Quantity summary ─────────────────────────────────────────────

        public int TotalOrderedQty { get; set; }
        public int TotalDeliveredQty { get; set; }
        public int TotalBalanceQty { get; set; }

        /// <summary>
        /// Phase 10.7 (Improvement 7) — mirrors
        /// SavedDeliveryNote.TotalPreviouslyDeliveredQty.
        /// On Master DN: equals TotalDeliveredQty (sum of all real DNs).
        /// Populated by ToListItem() in the repository.
        /// </summary>
        public int TotalPreviouslyDeliveredQty { get; set; }

        public int SequenceNumber { get; set; }
        public DateTime DeliveryDate { get; set; }
        public string DeliveryTime { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // ── Tax Invoice link ─────────────────────────────────────────────

        /// <summary>
        /// Phase 10.7 placeholder — wired in Phase 19.
        /// Empty string = no Tax Invoice generated yet.
        /// </summary>
        public string TaxInvoiceId { get; set; } = "";

        /// <summary>
        /// True when a Tax Invoice has been generated from this DN.
        /// </summary>
        public bool HasTaxInvoice { get; set; }

        /// <summary>ERP lifecycle hardening (Phase 1): true when this delivery
        /// note has been voided (soft-closed).</summary>
        public bool IsVoid { get; set; }
    }
}