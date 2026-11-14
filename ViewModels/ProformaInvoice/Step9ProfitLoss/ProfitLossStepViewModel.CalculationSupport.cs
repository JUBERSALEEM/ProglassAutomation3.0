// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.Calculation.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 3.4 — partial split. This part owns the ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.Calculation members.
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
        private string BuildStatus(
            int groupCount, int rowCount, int skipped, decimal gross)
        {
            if (groupCount == 0)
                return "No spec groups in Step 5 yet — add glass items first.";

            // REVENUE MISSING IS NOT A LOSS.
            //
            // When the gross provider returns zero - typically because the
            // invoice totals in Step 7 have not been produced yet - net is
            // zero and Profit becomes -(total cost). The figures used to be
            // presented as a genuine loss, so the user could not tell an
            // unprofitable job from an incomplete one. Say so plainly.
            if (gross <= 0m && rowCount > 0)
                return "Costs are calculated, but this invoice has no revenue " +
                       "total yet — the profit figure below is NOT a real loss. " +
                       "Complete Step 7 (invoice totals), then recalculate.";

            if (rowCount == 0)
                return "Nothing could be costed. The glass in this invoice is not in " +
                       "Sheet Store — add it there, or treat this figure as unavailable.";

            // Naming the cost basis matters: the same job shows a different
            // profit at purchase vs selling price, and a figure whose basis
            // is unstated is a figure nobody can check.
            string basis =
                $"{rowCount} line(s) from {groupCount} spec group(s) · " +
                $"glass costed at {CostBasisLabel} · " +
                $"SGU glass = required SQM + {WastagePercent:0.#}% wastage (Step 6 SHEET QTY wins when set), cutting and tempering separate · " +
                $"active Step 6 charges plus advanced inferred charges are included as cost lines · " +
                $"Step 6 wastage rows are excluded until optimization/utilization is implemented · " +
                $"multi-pane builds still round to whole sheets · VAT stripped at 5%.";

            return skipped > 0
                ? basis + $" {skipped} pane(s) shown unpriced — not in Sheet Store and no Step 4 rate; not included in profit."
                : basis;
        }

        private void AppendStep6Costs(
            List<ProfitLossRow> rows,
            ref decimal productionCost,
            ref decimal uInsertInProduction)
        {
            if (_otherChargesVm == null) return;

            var billedCoverage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var charge in _otherChargesVm.ChargeRows.Where(r => r.IsActive))
                AppendPredefinedChargeRows(
                    rows, ref productionCost, ref uInsertInProduction,
                    charge, billedCoverage);

            // 2026-09-07 business rule: Step 6 wastage is a quotation/support
            // setup only and must NOT create Profit & Loss cost rows. Clear
            // glass wastage is also excluded unconditionally (advanced on/off
            // does not matter). P&L keeps material usage/sheet optimisation in
            // its own sheet-cost model and avoids double-counting Step 6.

            if (_otherChargesVm.UseAdvancedAutomation)
                AppendAdvancedSuggestedCharges(
                    rows, ref productionCost, ref uInsertInProduction,
                    billedCoverage);

            foreach (var custom in _otherChargesVm.CustomChargesSection.AddedCharges.Where(r => r.IsActive))
                AppendInvoiceWideCharge(rows, ref productionCost,
                    custom.ChargeName ?? "Custom charge",
                    custom.Qty,
                    custom.Rate,
                    custom.Unit.ToString().ToUpperInvariant());
        }

        private void AppendPredefinedChargeRows(
            List<ProfitLossRow> rows,
            ref decimal productionCost,
            ref decimal uInsertInProduction,
            Models.ProformaInvoice.Step6OtherCharges.OtherChargeRow charge,
            HashSet<string> billedCoverage)
        {
            if (IsExcludedSupplementalPnLCharge(charge.TemplateKey))
                return;

            if (charge.IsDeliveryRow)
            {
                AppendInvoiceWideCharge(rows, ref productionCost,
                    charge.ChargeName,
                    1m,
                    charge.Rate,
                    "TRIP");
                return;
            }

            if (charge.TargetSelection == null) return;

            bool isUInsert = string.Equals(
                charge.TemplateKey, "U_INSERT",
                StringComparison.OrdinalIgnoreCase);

            foreach (var spec in charge.TargetSelection.AvailableSpecs.Where(s => s.IsChecked))
            {
                decimal qty = charge.IsPolishRow
                    ? ComputePolishQtyForSpec(spec.SpecGroupNumber, charge)
                    : spec.GetMetricValue(charge.TargetSelection.SelectedMetric?.Key ?? "");

                if (qty <= 0m) continue;

                decimal amount = Math.Round(qty * charge.Rate, 2);
                string kind = BuildChargeKind(charge.ChargeName);
                billedCoverage.Add(BuildCoverageKey(charge.TemplateKey, spec.SpecGroupNumber));
                rows.Add(new ProfitLossRow
                {
                    Kind = kind,
                    SpecNumber = spec.SpecGroupNumber,
                    SpecName = FindSpecName(spec.SpecGroupNumber),
                    Description = PnLMeasurement.BuildRowDescription(
                        FindSpecGroup(spec.SpecGroupNumber),
                        kind,
                        $"{qty:0.###} {charge.Unit.ToString().ToUpperInvariant()}"),
                    Quantity = Math.Round(qty, 3),
                    PricePerSqm = charge.Rate,
                    UnitLabel = charge.Unit.ToString().ToUpperInvariant(),
                    Amount = amount
                });
                productionCost += amount;

                // The Price-Based KPI re-adds U-insert at the resolved
                // per-SQM rate, so the amount already inside production cost
                // must be remembered here or the job's U-insert cost would
                // be counted twice.
                if (isUInsert) uInsertInProduction += amount;
            }
        }

        private void AppendAdvancedSuggestedCharges(
            List<ProfitLossRow> rows,
            ref decimal productionCost,
            ref decimal uInsertInProduction,
            HashSet<string> billedCoverage)
        {
            if (_otherChargesVm == null || _dataGrid == null) return;

            var metricsByGroup = _otherChargesVm.MetricHub.PerSpecMetrics
                .ToDictionary(x => x.SpecGroupNumber);

            foreach (var group in _dataGrid.SpecGroups)
            {
                metricsByGroup.TryGetValue(group.GroupNumber, out var metric);
                bool matchedSameGlass = metric?.IsMatchedSameGlass
                    ?? Models.ProformaInvoice.Step6OtherCharges.LaminateGlassMatcher
                        .IsMatchedSameGlass(
                            group.SelectedSpecName,
                            group.SelectedGlassType,
                            group.OuterGlassColor,
                            group.InnerGlassColor);

                foreach (var suggestion in OtherChargeAutomationRules.GetChargeSuggestions(
                    group.SelectedGlassType,
                    group.SelectedSpecName,
                    matchedSameGlass))
                {
                    string coverageKey = BuildCoverageKey(
                        suggestion.TemplateKey,
                        group.GroupNumber);
                    if (billedCoverage.Contains(coverageKey))
                        continue;

                    if (IsExcludedSupplementalPnLCharge(suggestion.TemplateKey))
                        continue;

                    var charge = _otherChargesVm.ChargeRows.FirstOrDefault(r =>
                        string.Equals(r.TemplateKey, suggestion.TemplateKey,
                            StringComparison.OrdinalIgnoreCase));
                    if (charge == null) continue;

                    decimal qty = 0m;
                    string unitLabel = charge.Unit.ToString().ToUpperInvariant();

                    if (charge.IsPolishRow)
                    {
                        var polishType = suggestion.PolishSubType
                            ?? charge.SelectedPolishSubType?.Type
                            ?? Models.ProformaInvoice.Step6OtherCharges.PolishSubType.AllSidePolish;
                        qty = ComputePolishQtyForSpec(group.GroupNumber, charge, polishType);
                    }
                    else if (metric != null)
                    {
                        qty = MetricValueForSpec(metric, suggestion.MetricKey);
                    }

                    decimal rate = ResolveSuggestedChargeRate(
                        charge, suggestion.TemplateKey, group);
                    if (qty <= 0m || rate <= 0m) continue;

                    string kind = BuildChargeKind(charge.ChargeName);
                    decimal amount = Math.Round(qty * rate, 2);
                    rows.Add(new ProfitLossRow
                    {
                        Kind = kind,
                        SpecNumber = group.GroupNumber,
                        SpecName = FindSpecName(group.GroupNumber),
                        Description = PnLMeasurement.BuildRowDescription(
                            FindSpecGroup(group.GroupNumber),
                            kind,
                            $"{qty:0.###} {unitLabel}"),
                        Quantity = Math.Round(qty, 3),
                        PricePerSqm = rate,
                        UnitLabel = unitLabel,
                        Amount = amount
                    });
                    productionCost += amount;
                    billedCoverage.Add(coverageKey);

                    // Same anti-double-count bookkeeping as the predefined
                    // path: the Price-Based KPI needs to know what U-insert
                    // amount is already part of TotalProductionCost.
                    if (string.Equals(suggestion.TemplateKey, "U_INSERT",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        uInsertInProduction += amount;
                    }
                }
            }
        }

        private void AppendInvoiceWideCharge(
            List<ProfitLossRow> rows,
            ref decimal productionCost,
            string label,
            decimal qty,
            decimal rate,
            string unit)
        {
            if (qty <= 0m || rate <= 0m) return;

            decimal amount = Math.Round(qty * rate, 2);
            string kind = BuildChargeKind(label);
            rows.Add(new ProfitLossRow
            {
                Kind = kind,
                SpecNumber = 0,
                SpecName = "Invoice-wide other charges",
                Description = $"{kind} · {qty:0.###} {unit}",
                Quantity = Math.Round(qty, 3),
                PricePerSqm = rate,
                UnitLabel = unit,
                Amount = amount
            });
            productionCost += amount;
        }

        private decimal ComputePolishQtyForSpec(
            int specNumber,
            Models.ProformaInvoice.Step6OtherCharges.OtherChargeRow charge,
            Models.ProformaInvoice.Step6OtherCharges.PolishSubType? overrideType = null)
        {
            var selectedType = overrideType ?? charge.SelectedPolishSubType?.Type;
            if (_dataGrid == null
                || selectedType == null
                || selectedType == Models.ProformaInvoice.Step6OtherCharges.PolishSubType.Custom)
                return 0m;

            var group = _dataGrid.SpecGroups.FirstOrDefault(g => g.GroupNumber == specNumber);
            if (group == null) return 0m;

            var snapshot = new SpecGroupSnapshot
            {
                GroupNumber = group.GroupNumber,
                GroupName = group.GroupName ?? "",
                SelectedGlassType = group.SelectedGlassType ?? "",
                SelectedSpecName = group.SelectedSpecName ?? "",
                OuterGlassColor = group.OuterGlassColor ?? "",
                InnerGlassColor = group.InnerGlassColor ?? ""
            };

            foreach (var row in group.ItemRows)
            {
                snapshot.ItemRows.Add(new GlassItemRowSnapshot
                {
                    SerialNumber = row.SerialNumber,
                    GlassRef = row.GlassRef ?? "",
                    W1 = row.W1,
                    H1 = row.H1,
                    W2 = row.W2,
                    H2 = row.H2,
                    Qty = row.Qty
                });
            }

            return Services.ProformaInvoice.Export.ProformaChargeMetricsCalculator.ComputePolishLm(
                new List<SpecGroupSnapshot> { snapshot },
                selectedType.Value.ToString(),
                matchedSameGlassOnly: charge.IsMatchedLamPolishRow);
        }

        private decimal ResolveSuggestedChargeRate(
            Models.ProformaInvoice.Step6OtherCharges.OtherChargeRow charge,
            string templateKey,
            SpecGroup group)
        {
            if (string.Equals(templateKey, "U_INSERT", StringComparison.OrdinalIgnoreCase)
                && group.UInsertPricePerSqm > 0m)
                return group.UInsertPricePerSqm;

            return charge.Rate;
        }

        /// <summary>
        /// The U-insert cost the Price-Based Profit Margin KPI must use:
        /// applicable U-insert area/quantity × the U-insert per-SQM rate.
        ///
        /// It reuses the EXACT U-insert machinery the rest of Step 9 uses —
        /// the same automation detection, the same per-spec metric (cavity
        /// SQM for DGU/DGU+LAM, SQM1 otherwise) and the same rate
        /// resolution (Step 4's UInsertPricePerSqm wins, then the Step 6
        /// row rate, then the U-insert template default of AED 10/m²).
        /// No separate pricing engine and no hardcoded margin/result.
        ///
        /// Deliberately recomputed from the spec groups rather than read
        /// from the production-cost rows: a job whose quote carries a
        /// U-Insert extra still carries that cost even when its Step 6
        /// charge row was left inactive, so the KPI never overstates the
        /// margin by pretending the extra was free.
        /// </summary>
        private decimal ComputePriceBasedUInsertCost()
        {
            if (_otherChargesVm == null || _dataGrid == null) return 0m;

            var metricsByGroup = _otherChargesVm.MetricHub.PerSpecMetrics
                .ToDictionary(x => x.SpecGroupNumber);

            var chargeRow = _otherChargesVm.ChargeRows.FirstOrDefault(r =>
                string.Equals(r.TemplateKey, "U_INSERT",
                    StringComparison.OrdinalIgnoreCase));

            // Template default (AED 10/m²) is the last stop when neither
            // the Step 4 seed nor a Step 6 row exists. Read once, not per
            // spec group.
            decimal templateDefault = Models.ProformaInvoice.Step6OtherCharges
                .PredefinedChargeTemplates.GetAll()
                .FirstOrDefault(t => string.Equals(
                    t.Key, "U_INSERT",
                    StringComparison.OrdinalIgnoreCase))
                ?.DefaultRate ?? 0m;

            decimal total = 0m;

            foreach (var group in _dataGrid.SpecGroups)
            {
                metricsByGroup.TryGetValue(group.GroupNumber, out var metric);
                if (metric == null) continue;

                // Same matcher usage as AppendAdvancedSuggestedCharges —
                // the per-spec metric (when present) is authoritative.
                bool matchedSameGlass = metric.IsMatchedSameGlass;

                foreach (var suggestion in OtherChargeAutomationRules.GetChargeSuggestions(
                    group.SelectedGlassType,
                    group.SelectedSpecName,
                    matchedSameGlass))
                {
                    if (!string.Equals(suggestion.TemplateKey, "U_INSERT",
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    decimal qty = MetricValueForSpec(metric, suggestion.MetricKey);
                    if (qty <= 0m) continue;

                    // Rate order mirrors ResolveSuggestedChargeRate, with the
                    // template default as the last stop when no Step 6 row
                    // exists at all: Step 4 seed → Step 6 row rate → AED 10/m².
                    decimal rate = group.UInsertPricePerSqm > 0m
                        ? group.UInsertPricePerSqm
                        : chargeRow != null && chargeRow.Rate > 0m
                            ? chargeRow.Rate
                            : templateDefault;

                    if (rate <= 0m) continue;

                    total += Math.Round(qty * rate, 2);
                }
            }

            return total;
        }

        private static decimal MetricValueForSpec(
            Models.ProformaInvoice.Step6OtherCharges.SpecGroupMetric spec,
            string metricKey)
        {
            return metricKey switch
            {
                "LM1" => spec.Lm1,
                "LM2" => spec.Lm2,
                "LM_COMBINED" => spec.LmCombined,
                "SQM1" => spec.Sqm1,
                "SQM2" => spec.Sqm2,
                "SQM_COMBINED" => spec.SqmCombined,
                "DGU_CAVITY" => spec.Sqm1,
                "QTY" => spec.Qty,
                "DOUBLE_GLASS" => spec.PhysicalPanes,
                _ => 0m
            };
        }

        private static string BuildCoverageKey(string templateKey, int specNumber) =>
            $"{NormalizeCoverageTemplate(templateKey)}|{specNumber}";

        private static string NormalizeCoverageTemplate(string? templateKey)
        {
            return (templateKey ?? string.Empty).Trim().ToUpperInvariant() switch
            {
                "OVERLAP_SQM" => "OVERLAP",
                "SILICON_BED_SQM" => "SILICON_BED",
                _ => (templateKey ?? string.Empty).Trim().ToUpperInvariant()
            };
        }


        private static bool IsExcludedSupplementalPnLCharge(string? templateKey)
        {
            // Argon / fan holes / cutouts / holes are commercial add-ons that
            // belong in Step 6 / the quoted DGU specification text. Do not pull
            // them into Step 9 as standalone profit-loss cost lines unless a
            // future dedicated production-cost model is introduced.
            string key = (templateKey ?? string.Empty).Trim().ToUpperInvariant();
            return key is "ARGON_GAS" or "FAN_HOLES" or "CUTOUT" or "HOLES" or "COUNTERSUNK_HOLES";
        }

        private static string BuildChargeKind(string? label)
        {
            string text = (label ?? "Charge").Trim();
            int unitAt = text.IndexOf(" (", StringComparison.Ordinal);
            if (unitAt > 0)
                text = text[..unitAt].Trim();
            return string.IsNullOrWhiteSpace(text) ? "Charge" : text;
        }

        private SpecGroup? FindSpecGroup(int specNumber) =>
            _dataGrid?.SpecGroups.FirstOrDefault(g => g.GroupNumber == specNumber);

        private string FindSpecName(int specNumber) =>
            FindSpecGroup(specNumber)?.SelectedSpecName ??
            (specNumber > 0 ? $"SPEC {specNumber}" : "Invoice-wide other charges");
    }
}
