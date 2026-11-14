// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/ProformaInvoice/Step6OtherCharges/SpecCheckOption.cs
// ═══════════════════════════════════════════════════════════════════════
// CHAT 17 — NEW FILE
//
//   One checkbox in the "SELECT SPECS" section of the spec-target popup.
//   Holds a snapshot reference to per-spec metrics — updated live when
//   the underlying spec group changes.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.ViewModels;

namespace ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges
{
    /// <summary>
    /// One spec group option in the "SELECT SPECS" checkbox list.
    /// </summary>
    public class SpecCheckOption : ViewModelBase
    {
        /// <summary>Spec group number (1-based).</summary>
        public int SpecGroupNumber { get; set; }

        /// <summary>Display label: "Spec 1 — 6mm Clear Tempered".</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>True for DGU, LAM, DGU+LAM.</summary>
        public bool IsDoubleGlass { get; set; }

        /// <summary>1 = SGU, 2 = DGU/LAM, 3 = DGU+LAM hybrid.</summary>
        public int PaneFactor { get; set; } = 1;

        // ── Live metric snapshot (updated by parent SpecTargetSelection) ──

        public decimal Lm1 { get; set; }
        public decimal Lm2 { get; set; }
        public decimal LmCombined => Lm1 + Lm2;

        public decimal Sqm1 { get; set; }
        public decimal Sqm2 { get; set; }
        public decimal SqmCombined => Sqm1 + Sqm2;

        public int Qty { get; set; }
        public int PhysicalPanes => Qty * (PaneFactor <= 0 ? 1 : PaneFactor);

        // ── Check state ─────────────────────────────────────────────────

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Returns the value for a given metric key from this spec.</summary>
        public decimal GetMetricValue(string metricKey)
        {
            return metricKey switch
            {
                "LM1" => Lm1,
                "LM2" => Lm2,
                "LM_COMBINED" => LmCombined,
                "SQM1" => Sqm1,
                "SQM2" => Sqm2,
                "SQM_COMBINED" => SqmCombined,
                "DGU_CAVITY" => IsDoubleGlass ? Sqm1 : 0m,
                "QTY" => Qty,
                "DOUBLE_GLASS" => PhysicalPanes,
                _ => 0m
            };
        }
    }
}