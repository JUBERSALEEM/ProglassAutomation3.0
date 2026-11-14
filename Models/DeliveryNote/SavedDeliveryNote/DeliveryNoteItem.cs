using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote
{
    /// <summary>
    /// One line item on a delivery note — tracks ordered vs delivered vs balance
    /// at the individual glass row level within a spec group.
    ///
    /// Stored as normalized rows (not in JSON blob) so we can query
    /// delivery history across multiple DNs without parsing JSON.
    ///
    /// BUG-04: Added ForeignKey attribute + navigation property for
    ///         EF Core relationship clarity.
    /// BUG-05: BalanceQty is stored for fast list queries but must always
    ///         be recalculated before save. Use ComputedBalanceQty for
    ///         safe in-memory calculations to avoid stale values.
    /// </summary>
    [Table("DeliveryNoteItems")]
    public class DeliveryNoteItem
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Parent DN reference, e.g. "DN-2026-001-01".
        /// FK to SavedDeliveryNote.DeliveryNoteId (string PK via HasPrincipalKey).
        /// </summary>
        [Required, MaxLength(50)]
        [ForeignKey(nameof(DeliveryNote))]
        public string DeliveryNoteId { get; set; } = "";

        /// <summary>
        /// Navigation property back to parent DN.
        /// Not loaded by default — use Include() when needed.
        /// </summary>
        public SavedDeliveryNote? DeliveryNote { get; set; }

        /// <summary>Parent JO reference for cross-DN queries.</summary>
        [Required, MaxLength(50)]
        public string JobOrderId { get; set; } = "";

        // ── Spec identification ──────────────────────────────────────────

        public int SpecGroupNumber { get; set; }

        [MaxLength(200)]
        public string SpecGroupName { get; set; } = "";

        // ── Glass item identification ────────────────────────────────────

        public int SerialNumber { get; set; }

        [MaxLength(200)]
        public string GlassRef { get; set; } = "";

        public int W1 { get; set; }
        public int H1 { get; set; }
        public int W2 { get; set; }
        public int H2 { get; set; }

        // ── Quantity tracking ────────────────────────────────────────────

        /// <summary>Total qty from original JO for this row.</summary>
        public int OrderedQty { get; set; }

        /// <summary>Sum of delivered qty from ALL prior confirmed DNs for this row.</summary>
        public int PreviouslyDeliveredQty { get; set; }

        /// <summary>Qty delivered in THIS DN.</summary>
        public int DeliveredQty { get; set; }

        /// <summary>
        /// Stored balance for fast list queries.
        /// BUG-05: This value CAN go stale if siblings change.
        /// Always recalculate before save:
        ///   BalanceQty = Math.Max(0, OrderedQty - PreviouslyDeliveredQty - DeliveredQty)
        /// Use ComputedBalanceQty for safe in-memory use.
        /// </summary>
        public int BalanceQty { get; set; }

        /// <summary>
        /// BUG-05: Safe computed balance — always fresh, never stale.
        /// Use this for in-memory logic. Use BalanceQty for DB queries only.
        /// </summary>
        [NotMapped]
        public int ComputedBalanceQty =>
            System.Math.Max(0, OrderedQty - PreviouslyDeliveredQty - DeliveredQty);

        /// <summary>Qty marked as rework (returned for fixing).</summary>
        public int ReworkQty { get; set; }

        /// <summary>Qty marked as returned/rejected (not re-deliverable).</summary>
        public int ReturnedQty { get; set; }

        // ── Weight tracking ────────────────────────────────────────────────

        /// <summary>
        /// Weight per piece in kilograms.
        /// Calculated from glass dimensions, thickness, and type using GlassWeightCalculator.
        /// Formula: Area (m²) × Total Glass Thickness (mm) × 2.5 kg/m²/mm
        /// </summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal WeightPerPiece { get; set; }

        /// <summary>
        /// Total weight for delivered quantity in kilograms.
        /// Computed property: WeightPerPiece × DeliveredQty
        /// Used for logistics planning and vehicle load calculations.
        /// </summary>
        [NotMapped]
        public decimal TotalWeight => WeightPerPiece * DeliveredQty;

        // ── Notes ────────────────────────────────────────────────────────

        [MaxLength(500)]
        public string Notes { get; set; } = "";
    }
}