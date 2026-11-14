// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.CostBasis.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 3.4 — partial split. This part owns the ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.CostBasis members.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.ProformaInvoice;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguLamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.LamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.SguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Text.RegularExpressions; // Match still used while production rows read spacer/pvb groups
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    public partial class ProfitLossStepViewModel : ViewModelBase
    {
        // ══════════════════════════════════════════════════════════════
        //  COST BASIS — PURCHASE vs SELLING
        //
        //  Step 4 auto-fills its base prices from the Sheet Store SELLING
        //  price (DguParametersViewModel.SheetStore.cs: "sell" wins, and
        //  only falls back to purchase when no sell price exists). So the
        //  48 / 25 figures quoted in a spec are SELL rates.
        //
        //  The P&L, correctly for accounting, costs glass at the PURCHASE
        //  price - profit measured against what the supplier charged. The
        //  consequence is that the P&L will NOT reproduce hand arithmetic
        //  done with the Step 4 numbers, because those are a different
        //  column of the same Sheet Store row.
        //
        //  Both are legitimate questions:
        //     purchase -> "what did we really make?"
        //     sell     -> "does the quote hold together against list price?"
        //
        //  DEFAULT IS NOW SELLING, on the business's instruction.
        //
        //  The reason it matters here: Step 4 builds its rate from the
        //  SELLING column, so a report costed at purchase measures the
        //  quote against a different column of the same Sheet Store row.
        //  On a 40 SQM Clear/Clear DGU quoted at 119.39 the two answers are
        //  20.4% and 4.8% — not a rounding difference, a different
        //  conclusion about whether the job is worth taking.
        //
        //  The toggle remains. Costing at purchase is still the honest
        //  answer to "what did we actually make against supplier invoices",
        //  and switching to it is one click. Whichever is active is named in
        //  the status line, so no figure on this screen is ambiguous.
        // ══════════════════════════════════════════════════════════════

        private bool _costAtSellingPrice = true;
        /// <summary>
        /// True (default) = cost at SELLING price, matching the Step 4 base
        /// rates so the report reconciles with the quote.
        /// False = cost at Sheet Store PURCHASE price, which answers the
        /// separate question of profit against supplier invoices.
        /// </summary>
        public bool CostAtSellingPrice
        {
            get => _costAtSellingPrice;
            set
            {
                if (_costAtSellingPrice == value) return;
                _costAtSellingPrice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CostBasisLabel));

                // Step 9 is live: the switch goes through the coalesced,
                // busy-safe queue like every other source edit. Firing
                // RecalculateAsync directly here would return immediately
                // whenever a run was already in flight, silently dropping the
                // toggle until some unrelated event recalculated again.
                QueueRecalculate();
            }
        }

        /// <summary>Names the basis so no figure on screen is ambiguous.</summary>
        public string CostBasisLabel => _costAtSellingPrice
            ? "selling price"
            : "purchase price";

        private decimal _wastagePercent = 15m;
        /// <summary>
        /// Cutting wastage allowance. Two jobs in one control:
        ///   1. Area basis — applied to the required SQM before the sheet
        ///      count is rounded up (SGU priced by required area; Step 6
        ///      SHEET QTY overrides both when set).
        ///   2. Formula yield — the specification target divides the glass
        ///      rates by yield = 1 − wastage/100 (15% → ÷0.85, 10% → ÷0.90,
        ///      25% → ÷0.75). See ProfitLossStepViewModel.TargetPricing.cs
        ///      EffectiveYieldFactor().
        /// User-editable; 15% is the shop default. Clamped to 0–95% so the
        /// yield can never reach zero or go negative.
        /// </summary>
        public decimal WastagePercent
        {
            get => _wastagePercent;
            set
            {
                if (_wastagePercent == value) return;
                _wastagePercent = value < 0m ? 0m : value > 95m ? 95m : value;
                OnPropertyChanged();

                // Wastage feeds the sheet count, which feeds every glass
                // amount and therefore the profit.
                //
                // The queue (not a direct RecalculateAsync) is deliberate:
                // typing "25" sends 2 then 25, and a sheet-store lookup can
                // still be running from the previous value. The queue
                // coalesces the burst into one run at the FINAL value and
                // re-queues when a run is in flight, so the last number the
                // operator typed is always the number the screen shows.
                // A direct call would hit `if (IsBusy) return;` mid-typing
                // and drop the edit -- the classic "I changed 15 to 20 and
                // nothing happened" report.
                QueueRecalculate();
            }
        }

        private decimal _grossVatInclusive;
        /// <summary>Quoted grand total including VAT (from Step 7).</summary>
        public decimal GrossVatInclusive
        {
            get => _grossVatInclusive;
            private set
            {
                if (_grossVatInclusive == value) return;
                _grossVatInclusive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GrossDisplay));
                RaiseStep9HealthNotifications();
            }
        }

        private decimal _netExVat;
        /// <summary>Revenue excluding VAT.</summary>
        public decimal NetExVat
        {
            get => _netExVat;
            private set
            {
                if (_netExVat == value) return;
                _netExVat = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NetDisplay));
                RaiseTargetProfitabilityCostNotifications();
            }
        }

        private decimal _totalGlassCost;
        /// <summary>Sum of all glass rows.</summary>
        public decimal TotalGlassCost
        {
            get => _totalGlassCost;
            private set
            {
                if (_totalGlassCost == value) return;
                _totalGlassCost = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GlassCostDisplay));
                OnPropertyChanged(nameof(TotalCostDisplay));
                OnPropertyChanged(nameof(MarkupPercent));
                OnPropertyChanged(nameof(MarkupDisplay));
                RaiseTargetProfitabilityCostNotifications();
            }
        }

        private decimal _totalProductionCost;
        /// <summary>Sum of all spacer + production rows.</summary>
        public decimal TotalProductionCost
        {
            get => _totalProductionCost;
            private set
            {
                if (_totalProductionCost == value) return;
                _totalProductionCost = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProductionCostDisplay));
                OnPropertyChanged(nameof(TotalCostDisplay));
                OnPropertyChanged(nameof(MarkupPercent));
                OnPropertyChanged(nameof(MarkupDisplay));
            }
        }

        private decimal _profit;
        /// <summary>Net revenue minus every cost. Negative means a loss.</summary>
        public decimal Profit
        {
            get => _profit;
            private set
            {
                if (_profit == value) return;
                _profit = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProfitDisplay));
                OnPropertyChanged(nameof(ProfitLabel));
                OnPropertyChanged(nameof(MarkupPercent));
                OnPropertyChanged(nameof(MarkupDisplay));
            }
        }

        private decimal _marginPercent;
        /// <summary>Profit as a percentage of net revenue.</summary>
        public decimal MarginPercent
        {
            get => _marginPercent;
            private set
            {
                if (_marginPercent == value) return;
                _marginPercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MarginDisplay));
                // CostBasedMarginPercent IS MarginPercent (renamed only) —
                // the rename must not change the displayed value even by
                // a notification gap.
                OnPropertyChanged(nameof(CostBasedMarginPercent));
                OnPropertyChanged(nameof(CostBasedMarginDisplay));
            }
        }

        private bool _isLoss;
        /// <summary>
        /// True when the invoice loses money. The view flips the profit
        /// tile to DangerBrush through a DataTrigger on this flag.
        /// </summary>
        public bool IsLoss
        {
            get => _isLoss;
            private set
            {
                if (_isLoss == value) return;
                _isLoss = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProfitLabel));
                RaiseStep9HealthNotifications();
            }
        }

        private bool _isBusy;
        /// <summary>True while a recalculation is running.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CalculationStateLabel));
                OnPropertyChanged(nameof(CalculationStatus));
                RaiseStep9HealthNotifications();
            }
        }

        private string _statusMessage = "Waiting for data — results appear and update automatically as you edit Step 4/5/6/7.";
        /// <summary>Human-readable state / warning line under the tiles.</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage == value) return;
                _statusMessage = value ?? "";
                OnPropertyChanged();
            }
        }

        // ── Formatted display strings (keeps StringFormat out of the XAML) ─

        public string GrossDisplay => $"AED {GrossVatInclusive:N2}";
        public string NetDisplay => $"AED {NetExVat:N2}";
        public string GlassCostDisplay => $"AED {TotalGlassCost:N2}";
        public string ProductionCostDisplay => $"AED {TotalProductionCost:N2}";
        public string TotalCostDisplay => $"AED {TotalGlassCost + TotalProductionCost:N2}";

        // ── Target Profitability cost lens ───────────────────────────────
        // The target dashboard is a commercial glass-price check. Active
        // Step 6 charges (U-insert, argon, fanhole, holes, cutouts, polish,
        // custom extras) are billed separately and must not make the glass
        // target look expensive. If an extra toggle is OFF, its value is
        // treated as included inside the Step 5 given AED/m² price/target
        // judgement; it is not imported as a separate production-cost line
        // into this summary card. The full P&L tab can still show production
        // costs for accounting/reference.
        public decimal TargetProfitabilityCost => TotalGlassCost;
        public string TargetProfitabilityCostDisplay => $"AED {TargetProfitabilityCost:N2}";
        public decimal TargetProfitabilityProfit => Math.Round(NetExVat - TargetProfitabilityCost, 2);
        public bool IsTargetProfitabilityLoss => NetExVat > 0m && TargetProfitabilityProfit < 0m;
        public string TargetProfitabilityProfitLabel => IsTargetProfitabilityLoss ? "LOSS" : "PROFIT";
        public string TargetProfitabilityProfitDisplay =>
            (!IsTargetProfitabilityLoss && TargetProfitabilityProfit < 0m)
                ? $"AED -{Math.Abs(TargetProfitabilityProfit):N2}"
                : $"AED {Math.Abs(TargetProfitabilityProfit):N2}";
        public decimal TargetProfitabilityMarginPercent =>
            TargetProfitabilityCost > 0m
                ? Math.Round(TargetProfitabilityProfit / TargetProfitabilityCost * 100m, 2)
                : 0m;
        public string TargetProfitabilityMarginDisplay =>
            $"AED {TargetProfitabilityProfit:N0} | {TargetProfitabilityMarginPercent:0.#}%";

        private void RaiseTargetProfitabilityCostNotifications()
        {
            OnPropertyChanged(nameof(TargetProfitabilityCost));
            OnPropertyChanged(nameof(TargetProfitabilityCostDisplay));
            OnPropertyChanged(nameof(TargetProfitabilityProfit));
            OnPropertyChanged(nameof(IsTargetProfitabilityLoss));
            OnPropertyChanged(nameof(TargetProfitabilityProfitLabel));
            OnPropertyChanged(nameof(TargetProfitabilityProfitDisplay));
            OnPropertyChanged(nameof(TargetProfitabilityMarginPercent));
            OnPropertyChanged(nameof(TargetProfitabilityMarginDisplay));
        }
        /// <summary>
        /// Math.Abs is correct ONLY because the caption flips to "LOSS",
        /// so "AED 500.00" under a LOSS label reads right and avoids the
        /// double-negative "-AED 500.00 LOSS".
        ///
        /// But IsLoss requires gross > 0. With no revenue entered yet the
        /// profit is -cost, the caption still says PROFIT, and Math.Abs
        /// would render that as a POSITIVE profit - the exact opposite of
        /// the truth. That case now keeps its minus sign.
        /// </summary>
        public string ProfitDisplay =>
            (!IsLoss && Profit < 0m)
                ? $"AED -{Math.Abs(Profit):N2}"
                : $"AED {Math.Abs(Profit):N2}";
        public string MarginDisplay => $"{MarginPercent:0.#}% margin";

        /// <summary>
        /// Mark-up actually achieved over total cost, which is the number
        /// Step 4's "margin profit %" box sets.
        ///
        /// The two are different measures and used to be easy to confuse:
        ///   MarginPercent = profit / NET REVENUE   (accounting margin)
        ///   MarkupPercent = profit / TOTAL COST    (Step 4's setting)
        ///
        /// Worked example, 6mm HD Grey FT + 12mm ASP + 6mm Clear FT:
        ///     cost basis = (48 + 25) / 0.85 + 45 = 130.88 per SQM
        ///     Step 4 at 15%                      = 150.51 per SQM
        /// Discount that to 140 and the mark-up actually earned is
        ///     (140 - 130.88) / 130.88 = 6.97%
        /// — less than half the intended 15%, for a 7% cut in price. Showing
        /// the mark-up makes that visible in the same units the price was set
        /// in; the accounting margin alone understates how much was given
        /// away.
        ///
        /// One caveat this figure depends on: Step 4 quotes EX-VAT, while
        /// Profit is measured against NET (gross / 1.05). If a Step 4 rate is
        /// typed into Step 7 as a VAT-INCLUSIVE price, the mark-up shown here
        /// lands near 9.5% rather than 15%, because a twentieth of the
        /// revenue is tax rather than income.
        /// </summary>
        public decimal MarkupPercent
        {
            get
            {
                decimal cost = TotalGlassCost + TotalProductionCost;
                if (cost <= 0m) return 0m;
                return Math.Round(Profit / cost * 100m, 1);
            }
        }

        /// <summary>Reads "7% mark-up over cost", or explains why it cannot.</summary>
        public string MarkupDisplay =>
            (TotalGlassCost + TotalProductionCost) <= 0m
                ? "mark-up not calculable — no costed rows"
                : $"{MarkupPercent:0.#}% mark-up over cost";

        // ── KPI CARDS — COST-BASED vs PRICE-BASED PROFIT MARGIN ───────────
        //
        //  Both report profit margins but answer different questions:
        //
        //    Cost-Based   = the EXISTING Profit ÷ Net Selling × 100 margin
        //                   (the value the current Profit Margin KPI shows)
        //                   — renamed to "Cost-Based Profit Margin",
        //                   formula, data source and calculation behaviour
        //                   unchanged.
        //    Price-Based  = new KPI: the margin after the job's ACTUAL
        //                   U-insert cost is added to the cost side,
        //                   Profit ÷ Net Selling × 100.
        //
        //  The U-insert is an additional cost/loss, not a deduction from
        //  the selling price. Neither number is hardcoded: both follow the
        //  live recalculation.

        // ── THREE MARGIN PERSPECTIVES ─────────────────────────────────────
        //
        //  1. Cost-Based Margin (BASE COST ONLY)
        //     Formula: (Revenue - Base Cost) / Base Cost × 100
        //     Shows: Glass + ASP margin (ignoring U-insert)
        //     Use: Compare to Step 4's margin setting
        //
        //  2. Overall Margin (INCLUDES ALL COSTS) ← MOST IMPORTANT
        //     Formula: (Revenue - Total Cost) / Total Cost × 100
        //     Shows: Real job profitability (glass + ASP + U-insert)
        //     Use: Decision making
        //
        //  3. Net Margin (ACCOUNTING)
        //     Formula: (Revenue - Total Cost) / Revenue × 100
        //     Shows: Profit as % of revenue
        //     Use: Financial reporting
        //
        //  Each KPI card shows: "AED -212 | -6.2%"
        //  (profit amount + margin percentage)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cost-based profit margin: (Revenue - Base Cost) / Base Cost × 100
        /// Shows glass + ASP margin only (ignoring U-insert).
        /// </summary>
        public decimal CostBasedMarginPercent => MarginPercent;

        /// <summary>
        /// Profit amount for cost-based margin (Revenue - Base Cost).
        /// This is different from PriceBasedActualProfit which includes U-insert.
        /// </summary>
        public decimal CostBasedProfitAmount => 
            Math.Round(GrossVatInclusive - PriceBasedBasisCost, 2);

        /// <summary>
        /// KPI card display: "AED -12 | -0.4%"
        /// </summary>
        public string CostBasedMarginDisplay =>
            $"AED {CostBasedProfitAmount:N0} | {CostBasedMarginPercent:0.#}%";

        private decimal _overallMarginPercent;
        /// <summary>
        /// Overall profit margin: (Revenue - Total Cost) / Total Cost × 100
        /// Includes ALL costs (glass + ASP + U-insert).
        /// This is the REAL job profitability - most important for decisions!
        /// </summary>
        public decimal OverallMarginPercent
        {
            get => _overallMarginPercent;
            private set
            {
                if (_overallMarginPercent == value) return;
                _overallMarginPercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OverallMarginDisplay));
            }
        }

        /// <summary>Profit amount for overall margin: NetExVat - GlassCost - ProductionCost (actual P&L profit).</summary>
        public decimal OverallProfitAmount => Profit;

        /// <summary>
        /// KPI card display: "AED -212 | -6.2%"
        /// </summary>
        public string OverallMarginDisplay =>
            $"AED {OverallProfitAmount:N0} | {OverallMarginPercent:0.#}%";

        private decimal _priceBasedMarginPercent;
        /// <summary>
        /// Net profit margin: (Revenue - Total Cost) / Revenue × 100
        /// Accounting perspective - profit as % of revenue.
        /// </summary>
        public decimal PriceBasedMarginPercent
        {
            get => _priceBasedMarginPercent;
            private set
            {
                if (_priceBasedMarginPercent == value) return;
                _priceBasedMarginPercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PriceBasedMarginDisplay));
            }
        }

        /// <summary>Profit amount for net margin (Revenue - Total Cost).</summary>
        public decimal NetProfitAmount => PriceBasedActualProfit;

        /// <summary>
        /// KPI card display: "AED -212 | -6.6%"
        /// </summary>
        public string PriceBasedMarginDisplay =>
            $"AED {NetProfitAmount:N0} | {PriceBasedMarginPercent:0.#}%";

        /// <summary>
        /// The U-insert cost the Price-Based margin uses. Becomes zero when
        /// no spec in the job carries a U-Insert extra.
        /// </summary>
        public decimal PriceBasedUInsertCost { get; private set; }

        /// <summary>
        /// Glass + Production + ASP at the job's NORMAL pre-margin per-SQM
        /// basis (existing QuoteBasisFor × actual area), WITHOUT the U-insert
        /// portion — the number a quoted rate is measured against. When no
        /// Step 4 panel exists this falls back to the actual P&L rows.
        /// </summary>
        public decimal PriceBasedBasisCost { get; private set; }

        /// <summary>Glass + Production/ASP (normal basis) + U-Insert.</summary>
        public decimal PriceBasedTotalActualCost { get; private set; }

        /// <summary>Net Selling Amount − Total Actual Cost.</summary>
        public decimal PriceBasedActualProfit { get; private set; }

        /// <summary>Tile caption — flips wording instead of showing a minus sign.</summary>
        public string ProfitLabel => IsLoss ? "LOSS" : "PROFIT";

        // ── Commands ──────────────────────────────────────────────────────

        /// <summary>Recomputes the whole breakdown.</summary>
        public ICommand RecalculateCommand { get; }

    }
}
