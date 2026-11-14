using System;

namespace ProGlassAutomation.Models.ProformaInvoice.Specifications
{
    /// <summary>
    /// Represents a saved glass specification stored in the specifications.db database.
    /// Generated from Step 4 (Pricing Estimator) when user clicks APPLY.
    /// Reused in Step 5 (Data Grid) via the specification dropdown.
    /// </summary>
    public class SavedSpecification
    {
        public int Id { get; set; }

        /// <summary>
        /// Glass type: "SGU", "DGU", "LAM", or "DGU+LAM"
        /// Used to filter the Step 5 specification dropdown.
        /// </summary>
        public string GlassType { get; set; }

        /// <summary>
        /// Auto-generated human-readable spec name.
        /// Examples:
        ///   SGU: "6mm Clear FT Glass"
        ///   DGU: "6mm HD Grey FT Glass + 12mm ASP + 6mm Clear FT Glass"
        ///   LAM: "6mm Clear FT Glass + 1.52mm Clear PVB + 6mm Clear FT Glass"
        /// </summary>
        public string SpecName { get; set; }

        /// <summary>
        /// Live-calculated rate per SQM from Step 4.
        /// User can override in Step 5 grid if needed.
        /// </summary>
        public decimal PricePerSqm { get; set; }

        /// <summary>
        /// When this spec was first created.
        /// </summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Last time this spec was quoted on a SAVED proforma.
        /// LEDGER-DERIVED (SpecificationLedgerService) — Step 4 never writes
        /// it. Default (0001-01-01) means "not on any saved invoice yet".
        /// Used for smart sorting (most recently used first).
        /// </summary>
        public DateTime LastUsedAt { get; set; }

        /// <summary>
        /// How many saved Step-5 groups quote this spec, across all saved
        /// proformas. LEDGER-DERIVED (SpecificationLedgerService) — Step 4
        /// never writes it. 0 means a draft/library entry: built or imported
        /// but not on any saved invoice yet. Higher usage = show first in
        /// dropdown suggestions.
        /// </summary>
        public int UsageCount { get; set; }

        // ═══════════════════════════════════════════════════════════════
        //  SPECIFICATIONS CENTER (2026-09-02)
        //  ---------------------------------------------------------------
        //  Catalogue columns. Step 4 remains the only place a spec is
        //  BUILT and priced; these fields just record what it was built
        //  from so the Specifications Center can list, group and filter
        //  entries the way Sheet Store does.
        //
        //  The Sheet Store relationship is READ-ONLY and for MATCHING the
        //  glass sheet size only -- opening a row shows which sheet this
        //  spec was quoted against. Nothing here drives pricing; that
        //  stays with PricePerSqm from Step 4.
        //
        //  All columns are NULLABLE / defaulted. Existing rows created
        //  before this change stay valid and simply read as blank, which
        //  is why the migration is purely additive.
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Sheet Store category, e.g. "Clear", "Tinted", "Low-E".</summary>
        public string? Category { get; set; }

        /// <summary>Outer pane colour, as parsed from the spec name.</summary>
        public string? Color { get; set; }

        /// <summary>Outer pane thickness in mm. 0 when unknown.</summary>
        public int ThicknessMm { get; set; }

        /// <summary>
        /// Matched Sheet Store sheet size, in mm. Recorded at save time so
        /// the catalogue can show "which sheet was used" without a live
        /// lookup, and so a later Sheet Store edit cannot silently rewrite
        /// what an existing quote was costed against.
        /// </summary>
        public int SheetWidthMm { get; set; }
        public int SheetHeightMm { get; set; }

        /// <summary>
        /// Id of the GlassSheet row this spec matched, or 0 if none did.
        /// Kept only so the Center can open the originating sheet.
        /// </summary>
        public int SheetId { get; set; }

        /// <summary>
        /// Most recent SAVED proforma quoting this spec, for traceability in
        /// the catalogue list. LEDGER-DERIVED (SpecificationLedgerService);
        /// null means "not on any saved invoice yet". The full per-invoice
        /// history lives in SpecificationUsages, not here.
        /// </summary>
        public string? ProformaId { get; set; }

        /// <summary>
        /// Per-pane sheet matches, serialized as JSON (see SpecPane).
        ///
        /// SheetWidthMm/SheetHeightMm above describe only the OUTER pane and
        /// are kept for the list column and backward compatibility. A DGU has
        /// two panes that can be cut from DIFFERENT stock sizes, so the
        /// detail view needs this list rather than a single pair.
        ///
        /// Stored as JSON in one column so a three- or four-leaf build needs
        /// no further migration.
        /// </summary>
        public string? PanesJson { get; set; }

        /// <summary>True when a Sheet Store size was actually matched.</summary>
        public bool HasSheetMatch => SheetWidthMm > 0 && SheetHeightMm > 0;

        /// <summary>Sheet area in SQM, or 0 when no sheet was matched.</summary>
        public decimal SheetSqm =>
            Utilities.SqmCalculator.CalculateSheetArea(SheetWidthMm, SheetHeightMm);

        /// <summary>"3210 × 2250" or "—" for the catalogue list.</summary>
        public string SheetSizeDisplay =>
            HasSheetMatch ? $"{SheetWidthMm} × {SheetHeightMm}" : "—";

        public SavedSpecification()
        {
            CreatedAt = DateTime.Now;
            LastUsedAt = DateTime.Now;
            UsageCount = 0;
        }
    }
}