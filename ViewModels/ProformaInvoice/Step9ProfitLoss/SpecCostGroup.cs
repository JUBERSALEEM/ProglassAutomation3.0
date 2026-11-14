// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/SpecCostGroup.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — Step 9 Profit & Loss
//
// SpecCostGroup extracted from the large ProfitLossStepViewModel file (Phase 11/12
// God-VM split) — behaviour unchanged, relocated for organisation only.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.ProformaInvoice;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguLamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.LamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.SguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    public class SpecCostGroup : ViewModelBase
    {
        /// <summary>Sequence number from the Step 5 DataGrid.</summary>
        public int SpecNumber { get; set; }

        private string _specName = "";
        /// <summary>Full specification text, e.g. "6mm HD Grey FT Glass + 12mm ASP + …".</summary>
        public string SpecName
        {
            get => _specName;
            set
            {
                var next = value ?? "";
                if (string.Equals(_specName, next, StringComparison.Ordinal)) return;
                _specName = next;
                OnPropertyChanged();
            }
        }

        /// <summary>Cost lines for this specification, in physical build order.</summary>
        public ObservableCollection<ProfitLossRow> Rows { get; } = new();

        /// <summary>Cut area this specification covers, in SQM.</summary>
        public decimal CutSqm { get; set; }

        /// <summary>Glass + production cost for this specification only.</summary>
        public decimal TotalCost { get; set; }

        /// <summary>
        /// The Step 4 pre-margin cost basis for this specification: the exact
        /// number the quoted rate was built from, before the profit margin.
        /// Set during recalculation; 0 when Step 4 has no matching selection.
        /// </summary>
        public decimal QuoteBasisPerSqm { get; set; }

        /// <summary>
        /// Materials cost per SQM of cut area, from the Profit &amp; Loss
        /// rows. Shown for reference only.
        ///
        /// This is NOT what a quoted rate should be measured against, and
        /// using it here was the bug. It differs from the quote basis in
        /// three ways at once:
        ///   · priced at PURCHASE, while the quote is built from SELLING
        ///   · counts whole SHEETS BOUGHT, not the area actually cut
        ///   · excludes the spacer, cutting, tempering and yield division
        /// On a Clear/Clear DGU the two came out around 60 and 103.82 — the
        /// rate box was reporting a margin against a number that had nothing
        /// to do with the price.
        /// </summary>
        public decimal MaterialsCostPerSqm =>
            CutSqm > 0m ? Math.Round(TotalCost / CutSqm, 2) : 0m;

        /// <summary>
        /// The figure every percentage below is measured against: the Step 4
        /// basis when it is known, falling back to materials cost so a spec
        /// Step 4 cannot identify still reports something rather than zero.
        /// </summary>
        public decimal CostPerSqm =>
            QuoteBasisPerSqm > 0m ? QuoteBasisPerSqm : MaterialsCostPerSqm;

        /// <summary>True when the rate check is using the real quote basis.</summary>
        public bool HasQuoteBasis => QuoteBasisPerSqm > 0m;

        // ══════════════════════════════════════════════════════════════
        //  RATE CHECK
        //
        //  Type any rate and see the mark-up it actually earns. The wizard
        //  computes a rate at the target margin, but the price finally given
        //  to a customer is often negotiated, and until now nothing showed
        //  what that negotiation cost.
        //
        //  Worked example, 6mm HD Grey FT + 12mm ASP + 6mm Clear FT:
        //      cost basis 130.88 per SQM
        //      150.51 -> 15%      143.97 -> 10%      140.00 -> 6.97%
        //  A 4% cut from 150.51 to 143.97 gives away a third of the profit.
        // ══════════════════════════════════════════════════════════════

        private decimal _quotedRatePerSqm;
        /// <summary>Rate to test against this specification's cost. 0 = not set.</summary>
        public decimal QuotedRatePerSqm
        {
            get => _quotedRatePerSqm;
            set
            {
                if (_quotedRatePerSqm == value) return;
                _quotedRatePerSqm = value < 0m ? 0m : value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QuotedMarkupPercent));
                OnPropertyChanged(nameof(QuotedProfitPerSqm));
                OnPropertyChanged(nameof(RateCheckDisplay));
                OnPropertyChanged(nameof(IsQuotedRateBelowCost));
                OnPropertyChanged(nameof(HasQuotedRate));
            }
        }

        public bool HasQuotedRate => QuotedRatePerSqm > 0m;

        /// <summary>Mark-up the quoted rate earns over cost, as a percentage.</summary>
        public decimal QuotedMarkupPercent
        {
            get
            {
                decimal cost = CostPerSqm;
                if (cost <= 0m || QuotedRatePerSqm <= 0m) return 0m;
                return Math.Round((QuotedRatePerSqm - cost) / cost * 100m, 2);
            }
        }

        /// <summary>Profit per SQM at the quoted rate. Negative below cost.</summary>
        public decimal QuotedProfitPerSqm =>
            QuotedRatePerSqm <= 0m ? 0m
                                   : Math.Round(QuotedRatePerSqm - CostPerSqm, 2);

        /// <summary>True when the rate entered would sell at a loss.</summary>
        public bool IsQuotedRateBelowCost =>
            QuotedRatePerSqm > 0m && QuotedRatePerSqm < CostPerSqm;

        /// <summary>
        /// Plain reading of the rate check, including the total effect across
        /// this specification's whole area — a small per-SQM gap is easy to
        /// wave away until it is multiplied by the job.
        /// </summary>
        public string RateCheckDisplay
        {
            get
            {
                if (CostPerSqm <= 0m)
                    return "Cost not known for this specification yet.";

                // Say plainly which number the percentage is measured against.
                // A margin quoted against the wrong basis is worse than none.
                string basisNote = HasQuoteBasis
                    ? $"basis {CostPerSqm:N2}/SQM"
                    : $"materials {CostPerSqm:N2}/SQM (Step 4 basis unavailable)";

                if (QuotedRatePerSqm <= 0m)
                    return $"{char.ToUpperInvariant(basisNote[0])}{basisNote[1..]}. " +
                           "Enter a rate to check its margin.";

                decimal total = Math.Round(QuotedProfitPerSqm * CutSqm, 2);

                return IsQuotedRateBelowCost
                    ? $"BELOW COST by {Math.Abs(QuotedProfitPerSqm):N2}/SQM " +
                      $"({QuotedMarkupPercent:0.##}%) — {Math.Abs(total):N2} lost over " +
                      $"{CutSqm:0.##} SQM · {basisNote}."
                    : $"{QuotedMarkupPercent:0.##}% margin · {QuotedProfitPerSqm:N2}/SQM " +
                      $"· {total:N2} over {CutSqm:0.##} SQM · {basisNote}.";
            }
        }

        /// <summary>Header caption: "SPEC 1 · 12.40 SQM · cost 130.88/SQM".</summary>
        public string HeaderDisplay
        {
            get
            {
                if (SpecNumber <= 0)
                    return "INVOICE-WIDE OTHER CHARGES";

                return HasQuoteBasis
                    ? $"SPEC {SpecNumber} · {CutSqm:0.##} SQM · quote basis {CostPerSqm:N2}/SQM"
                    : $"SPEC {SpecNumber} · {CutSqm:0.##} SQM · materials {CostPerSqm:N2}/SQM";
            }
        }

        public string TotalCostDisplay => $"AED {TotalCost:N2}";

        public bool CanRateCheck => SpecNumber > 0;

        /// <summary>
        /// Update cost rows and totals without replacing this object.
        /// The Check Rate / SQM TextBox is bound to this instance; rebuilding
        /// SpecGroupsView would destroy that box and swallow whatever the
        /// operator is typing.
        /// </summary>
        public void ApplyCalculatedState(
            string specName,
            decimal totalCost,
            decimal cutSqm,
            decimal quoteBasisPerSqm,
            IEnumerable<ProfitLossRow> rows)
        {
            SpecName = specName ?? "";
            TotalCost = totalCost;
            CutSqm = cutSqm;
            QuoteBasisPerSqm = quoteBasisPerSqm;

            Rows.Clear();
            if (rows != null)
            {
                foreach (var row in rows)
                    Rows.Add(row);
            }

            NotifyTotalsChanged();
        }

        /// <summary>Raised after the rows are filled so computed values refresh.</summary>
        public void NotifyTotalsChanged()
        {
            OnPropertyChanged(nameof(CostPerSqm));
            OnPropertyChanged(nameof(QuoteBasisPerSqm));
            OnPropertyChanged(nameof(MaterialsCostPerSqm));
            OnPropertyChanged(nameof(HasQuoteBasis));
            OnPropertyChanged(nameof(TotalCost));
            OnPropertyChanged(nameof(TotalCostDisplay));
            OnPropertyChanged(nameof(HeaderDisplay));
            OnPropertyChanged(nameof(QuotedMarkupPercent));
            OnPropertyChanged(nameof(QuotedProfitPerSqm));
            OnPropertyChanged(nameof(RateCheckDisplay));
            OnPropertyChanged(nameof(IsQuotedRateBelowCost));
        }
    }
}
