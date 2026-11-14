using ProGlassAutomation.ViewModels;

namespace ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges
{
    /// <summary>
    /// Represents one option in the "Charge Basis" dropdown for an Other Charge row.
    ///
    /// CHAT 17 — Per-Spec Targeting:
    ///   Options are now grouped by spec group (SPEC 1 / SPEC 2 / etc.) or
    ///   fall back to global aggregates ("ALL SPECS COMBINED").
    ///
    /// Example labels:
    ///   "LM1 (Glass 1 Perimeter): 45.20 lm"       — per-spec (Spec 1)
    ///   "SQM1 + SQM2 (Combined Area): 30.00 sqm"  — per-spec (Spec 3 DGU)
    ///   "TOTAL PIECES: 20 pcs"                    — global aggregate
    /// </summary>
    public class GridSourceOption : ViewModelBase
    {
        /// <summary>
        /// Internal key used for lookup and selection restoration.
        ///
        /// Global keys:  "LM1", "LM2", "LM_COMBINED", "SQM1", "SQM2",
        ///               "SQM_COMBINED", "DGU_CAVITY", "TOTAL_PIECES",
        ///               "DOUBLE_GLASS"
        ///
        /// Per-spec keys (Chat 17):  "S1_LM1", "S1_SQM1", "S2_QTY",
        ///                            "S3_LM_COMBINED", "S3_DGU_CAVITY", etc.
        ///                            Format: S{SpecGroupNumber}_{MetricKey}
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>Human-readable label shown in the dropdown item.</summary>
        private string _displayLabel = "";
        public string DisplayLabel
        {
            get => _displayLabel;
            set { if (_displayLabel == value) return; _displayLabel = value; OnPropertyChanged(); }
        }

        /// <summary>Live numeric value pulled from DataGrid / MetricHub.</summary>
        private decimal _value;
        public decimal Value
        {
            get => _value;
            set { if (_value == value) return; _value = value; OnPropertyChanged(); }
        }

        /// <summary>Unit of measurement — filters which charges can use this source.</summary>
        public OtherChargeUnit Unit { get; set; }

        /// <summary>Short description shown after the formula, e.g. "(W1 H1 PERIMETER (LM1))".</summary>
        public string FormulaHint { get; set; } = "";

        // ══════════════════════════════════════════════════════════════════
        //  CHAT 17 — PER-SPEC TARGETING (NEW)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Null = global aggregate across all spec groups.
        /// 1..N = scoped to a specific spec group number.
        /// Used by ComboBox grouping and per-spec value lookup.
        /// </summary>
        public int? SpecGroupNumber { get; set; }

        /// <summary>
        /// ComboBox group header text. All items with the same header
        /// appear together under one heading in the grouped dropdown.
        /// Examples:
        ///   "SPEC 1 — 6mm Clear Tempered"
        ///   "SPEC 2 — 12mm Blue Reflective"
        ///   "SPEC 3 — DGU 6+12+6"
        ///   "ALL SPECS COMBINED"
        /// </summary>
        public string GroupHeader { get; set; } = "";

        public override string ToString() => DisplayLabel;
    }
}