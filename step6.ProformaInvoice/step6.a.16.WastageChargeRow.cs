using System;

namespace ProGlassAutomation.Models.Proforma
{
    public sealed class WastageChargeRow
    {
        public bool IsActive { get; set; }
        public string ColorName { get; set; } = "";
        public decimal Rate { get; set; }
        public bool IsOptionalSuggestion { get; set; }
        public string SuggestionReason { get; set; } = "";

        /// <summary>Billable area of that colour at the last recalculation.</summary>
        public decimal AreaSqm { get; set; }

        /// <summary>Amount at the last recalculation, in AED.</summary>
        public decimal Amount { get; set; }

        /// <summary>An operator added this row; automation must not remove it.</summary>
        public bool IsManuallyAdded { get; set; }

        /// <summary>Derive the rate from the optimization formula instead of <see cref="Rate"/>.</summary>
        public bool UseOptimizationFormula { get; set; }
        public decimal FormulaPricePerSqm { get; set; }
        public decimal OptimizationPercent { get; set; }

        /// <summary>
        /// Whole sheets the shop buys for this colour. Read by Step 9 even when
        /// the wastage CHARGE is switched off: the switch bills the allowance,
        /// it does not decide how much glass the job consumes.
        /// </summary>
        private int _sheetQuantity = 1;
        public int SheetQuantity
        {
            get => _sheetQuantity;
            set => _sheetQuantity = Math.Max(0, value);
        }

        /// <summary>The rate actually used, from the formula when it is enabled.</summary>
        public decimal EffectiveRate
        {
            get
            {
                if (!UseOptimizationFormula) return Rate;
                decimal percent = MoneyRounding.ClampPercent(OptimizationPercent, 0m, 500m);
                return MoneyRounding.Round(FormulaPricePerSqm * percent / 100m);
            }
        }

        public void Recalculate()
        {
            Amount = IsActive ? MoneyRounding.Round(AreaSqm * EffectiveRate) : 0m;
        }
    }

    /// <summary>One operator-defined charge line.</summary>
}
