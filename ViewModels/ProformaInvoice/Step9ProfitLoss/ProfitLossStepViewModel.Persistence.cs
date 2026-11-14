// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.Persistence.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 3.4 — partial split. This part owns the ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.Persistence members.
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
using ProGlassAutomation.Services.ProformaInvoice.TargetPricing;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    public partial class ProfitLossStepViewModel : ViewModelBase
    {
        // ══════════════════════════════════════════════════════════════
        //  PERSISTENCE
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds the JSON-serializable form consumed by
        /// ProformaInvoiceMapper.BuildSnapshot(). Always returns an object
        /// (never null) so the mapper needs no extra guard.
        /// </summary>
        public ProfitLossSnapshot BuildSnapshot()
        {
            var snap = new ProfitLossSnapshot
            {
                GrossVatInclusive = GrossVatInclusive,
                NetExVat = NetExVat,
                TotalGlassCost = TotalGlassCost,
                TotalProductionCost = TotalProductionCost,
                Profit = Profit,
                MarginPercent = MarginPercent,
                OverallMarginPercent = OverallMarginPercent,
                NetMarginPercent = PriceBasedMarginPercent,
                WastagePercent = WastagePercent,
                CostAtSellingPrice = CostAtSellingPrice
            };

            foreach (var row in Rows)
                snap.Rows.Add(row.ToSnapshot());

            // ── Target-price model (additive, backward-compatible) ────────
            if (JobTargetResult != null)
            {
                snap.TargetTotalGivenValue = JobTargetResult.TotalGivenValue;
                snap.TargetTotalRequiredValue = JobTargetResult.TotalRequiredTargetValue;
                snap.TargetTotalDifferenceValue = JobTargetResult.TotalDifferenceValue;
                snap.TargetAverageGivenPricePerSqm = JobTargetResult.AverageGivenPricePerSqm;
                snap.TargetAverageSpecificationTargetPerSqm = JobTargetResult.AverageSpecificationTargetPerSqm;
                snap.TargetAverageFinalRequiredTargetPerSqm = JobTargetResult.AverageFinalRequiredTargetPerSqm;
                snap.TargetAverageAppliedUInsertPerSqm = JobTargetResult.AverageAppliedUInsertPerSqm;
                snap.TargetAverageBasePricePerSqm = JobTargetResult.AverageBasePricePerSqm;
                snap.TargetAverageGivenJudgedPerSqm = JobTargetResult.AverageGivenJudgedPerSqm;
                snap.TargetAverageExtraProfitPerSqm = JobTargetResult.AverageExtraProfitPerSqm;
                snap.TargetExtraProfitMarginPercent = JobTargetResult.ExtraProfitMarginPercent;
                snap.TargetMarginAgainstTargetPercent = JobTargetResult.MarginAgainstTargetPercent;
                snap.TargetStatus = JobTargetResult.Status.ToString();
                snap.TargetValidSpecCount = JobTargetResult.ValidSpecCount;
                snap.TargetIncompleteSpecCount = JobTargetResult.IncompleteSpecCount;
                snap.TargetCalculationVersion = TargetCalculationVersion;
                snap.TargetCalculatedAtUtc = LastCalculatedAt.HasValue
                    ? LastCalculatedAt.Value.ToUniversalTime()
                    : (DateTime?)null;
                snap.TargetUInsertTreatmentSummary = UInsertSummaryDisplay;

                foreach (var w in JobTargetResult.Warnings)
                    snap.TargetWarnings.Add(w);

                foreach (var row in TargetSpecsView)
                {
                    if (row.Result == null) continue;
                    snap.SpecTargets.Add(ToSnapshot(row.Result));
                }
            }

            return snap;
        }

        private static SpecTargetSnapshot ToSnapshot(SpecTargetPriceResult s)
        {
            return new SpecTargetSnapshot
            {
                SpecNumber = s.SpecNumber,
                SpecName = s.SpecName ?? "",
                GlassTypeCode = s.GlassTypeCode ?? "",
                BuildType = s.BuildType.ToString(),
                GivenPricePerSqm = s.GivenPricePerSqm,
                AreaSqm = s.AreaSqm,
                GivenValue = s.GivenValue,
                OuterGlassRatePerSqm = s.OuterGlassRatePerSqm,
                InnerGlassRatePerSqm = s.InnerGlassRatePerSqm,
                ThirdGlassRatePerSqm = s.ThirdGlassRatePerSqm,
                AspRatePerSqm = s.AspRatePerSqm,
                ProcessingRatePerSqm = s.ProcessingRatePerSqm,
                YieldFactor = s.YieldFactor,
                TargetUpliftPercent = s.TargetUpliftPercent,
                FormulaText = s.FormulaText ?? "",
                SpecificationTargetPerSqm = s.SpecificationTargetPerSqm,
                UInsertTreatment = s.UInsertTreatment.ToString(),
                UInsertRatePerSqm = s.UInsertRatePerSqm,
                UInsertSourceLabel = s.UInsertSourceLabel ?? "",
                AppliedUInsertPerSqm = s.AppliedUInsertPerSqm,
                FinalRequiredTargetPerSqm = s.FinalRequiredTargetPerSqm,
                BasePricePerSqm = s.BasePricePerSqm,
                GivenPriceJudgedPerSqm = s.GivenPriceJudgedPerSqm,
                ExtraProfitPerSqm = s.ExtraProfitPerSqm,
                ExtraProfitMarginPercent = s.ExtraProfitMarginPercent,
                DifferencePerSqm = s.DifferencePerSqm,
                RequiredTargetValue = s.RequiredTargetValue,
                DifferenceValue = s.DifferenceValue,
                MarginAgainstTargetPercent = s.MarginAgainstTargetPercent,
                Status = s.Status.ToString(),
                Warnings = new List<string>(s.Warnings ?? new List<string>())
            };
        }
    }
}
