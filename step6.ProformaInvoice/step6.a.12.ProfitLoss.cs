// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.12.ProfitLoss.cs
// MODULE: step6 — stage 7 (ADV step 9): the single pricing engine's costing
// side.
//
// What is costed, and against what:
//   • glass is costed by WHOLE STOCK SHEETS from the pane's recorded sheet
//     size and cost rate, after a wastage allowance. Half a sheet still has to
//     be bought.
//   • production rows are the spacer, interlayer, cutting and tempering rates
//     the group was QUOTED on — carried on the group's own cost card, never
//     read from whichever pricing section is on screen.
//   • a disabled wastage CHARGE still owns its sheet quantity: the switch
//     decides whether an allowance is billed, not how much glass the job eats.
//
// Revenue is the VAT-exclusive grand total, because profit is a net figure.
// The margin KPI is measured against the cost basis of the quoted rate, which
// is a different question from "profit ÷ revenue" and is labelled as such.
//
// HONESTY NOTE — no external price basis exists in this application yet, so a
// saved P&L records the invoice's own pricing inputs (fingerprint) instead of
// pretending a catalogue stamp exists. Nothing is reported as "current" that
// could not be checked.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>Which cost a P&L line belongs to.</summary>
    public enum ProfitLossRowKind
    {
        Glass = 0,
        Spacer = 1,
        Interlayer = 2,
        Cutting = 3,
        Tempering = 4
    }

    /// <summary>Stage-7 controls: the one setting the trade asks for.</summary>
    public sealed class ProfitLossSettings
    {
        /// <summary>Cutting loss allowed when sheets are counted. 0–95.</summary>
        public decimal WastagePercent { get; set; } = 10m;
    }

    /// <summary>One costed line of the profit &amp; loss.</summary>
    public sealed class ProfitLossRow
    {
        public ProfitLossRowKind Kind { get; set; }
        public int SpecNumber { get; set; }
        public string SpecName { get; set; } = "";
        public string Description { get; set; } = "";
        public int SheetWidthMm { get; set; }
        public int SheetHeightMm { get; set; }
        public decimal SheetSqm { get; set; }
        public decimal Quantity { get; set; }
        public string UnitLabel { get; set; } = "sheet";
        public decimal PricePerSqm { get; set; }
        public decimal Amount { get; set; }

        /// <summary>Where the price came from ("purchase rate", "quoted pane rate").</summary>
        public string CostBasis { get; set; } = "";
    }

    /// <summary>Verdict of one specification against its target price.</summary>
    public enum TargetStatus
    {
        NotCalculable = 0,
        AboveTarget = 1,
        AtTarget = 2,
        BelowTarget = 3
    }

    /// <summary>Per-specification target-price result (the trade's KPI).</summary>
    public sealed class SpecTargetRow
    {
        public int SpecNumber { get; set; }
        public string SpecName { get; set; } = "";
        public decimal AreaSqm { get; set; }
        public decimal GivenPricePerSqm { get; set; }
        public decimal CostBasisPerSqm { get; set; }
        public decimal TargetUpliftPercent { get; set; }
        public decimal SpecificationTargetPerSqm { get; set; }
        public decimal DifferencePerSqm { get; set; }
        public decimal ExtraProfitPerSqm { get; set; }
        public decimal? MarginAgainstTargetPercent { get; set; }
        public decimal? ExtraProfitMarginPercent { get; set; }
        public TargetStatus Status { get; set; }
        public List<string> Warnings { get; set; } = new();

        /// <summary>Display wording for the KPI chip (the view prints this).</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string StatusText => Status switch
        {
            TargetStatus.AboveTarget => "Above target",
            TargetStatus.AtTarget => "At target",
            TargetStatus.BelowTarget => "Below target",
            _ => "Not calculable"
        };

        /// <summary>Status chip tag for the theme's badge.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string StatusTag => Status switch
        {
            TargetStatus.AboveTarget => "Warning",
            TargetStatus.AtTarget => "Success",
            TargetStatus.BelowTarget => "Information",
            _ => ""
        };
    }

    /// <summary>The stage-7 result: rows, totals, targets and the basis note.</summary>
    public sealed class ProfitLossResult
    {
        public List<ProfitLossRow> Rows { get; set; } = new();
        public decimal GrossVatInclusive { get; set; }
        public decimal NetExVat { get; set; }
        public decimal TotalGlassCost { get; set; }
        public decimal TotalProductionCost { get; set; }
        public decimal TotalCost { get; set; }
        public decimal Profit { get; set; }
        public decimal? MarginPercent { get; set; }
        public decimal? MarkupOnCostPercent { get; set; }
        public decimal WastagePercent { get; set; }
        public string BasisNote { get; set; } = "";
        public string PricingInputsFingerprint { get; set; } = "";
        public DateTime? ComputedAtUtc { get; set; }

        public List<SpecTargetRow> SpecTargets { get; set; } = new();
        public decimal TargetTotalGivenValue { get; set; }
        public decimal TargetTotalRequiredValue { get; set; }
        public decimal TargetTotalDifferenceValue { get; set; }
        public decimal TargetAverageGivenPerSqm { get; set; }
        public decimal TargetAverageBasePerSqm { get; set; }
        public decimal TargetAverageRequiredPerSqm { get; set; }
        public TargetStatus TargetStatus { get; set; }
        public int TargetIncompleteCount { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>Calculates the profit &amp; loss for a priced invoice.</summary>
    public static class ProfitLossCalculator
    {
        /// <summary>Above/below within this much is reported as "at target".</summary>
        public const decimal AtTargetTolerancePercent = 1m;

        public static ProfitLossResult Calculate(
            IReadOnlyList<SpecGroup>? groups,
            OtherChargesSheet? charges,
            InvoiceSummary summary,
            ProfitLossSettings? settings)
        {
            var result = new ProfitLossResult();
            var list = (groups ?? Array.Empty<SpecGroup>()).Where(group => group.IsPriced).ToList();
            decimal wastage = MoneyRounding.ClampPercent(settings?.WastagePercent ?? 10m, 0m, 95m);
            result.WastagePercent = wastage;

            foreach (SpecGroup group in list)
                AddSpecRows(group, charges, wastage, result);

            result.TotalGlassCost = MoneyRounding.Round(
                result.Rows.Where(row => row.Kind == ProfitLossRowKind.Glass).Sum(row => row.Amount));
            result.TotalProductionCost = MoneyRounding.Round(
                result.Rows.Where(row => row.Kind != ProfitLossRowKind.Glass).Sum(row => row.Amount));
            result.TotalCost = MoneyRounding.Round(result.TotalGlassCost + result.TotalProductionCost);

            result.GrossVatInclusive = MoneyRounding.Round(summary?.GrandTotal ?? 0m);
            decimal vatPercent = summary?.IsVatApplicable == true
                ? MoneyRounding.ClampPercent(summary.VatPercent, 0m, 100m)
                : 0m;
            result.NetExVat = MoneyRounding.Round(result.GrossVatInclusive / (1m + vatPercent / 100m));
            result.Profit = MoneyRounding.Round(result.NetExVat - result.TotalCost);

            if (result.NetExVat > 0m)
                result.MarginPercent = MoneyRounding.RoundPercent(result.Profit / result.NetExVat * 100m);
            if (result.TotalCost > 0m)
                result.MarkupOnCostPercent = MoneyRounding.RoundPercent(result.Profit / result.TotalCost * 100m);

            if (result.Rows.Count == 0)
                result.Warnings.Add("Nothing is costed yet: no priced specification carries a cost card.");
            if (result.TotalCost > 0m && result.Profit < 0m)
                result.Warnings.Add("The quotation is below its cost — profit is negative.");

            AddTargets(list, result);
            result.BasisNote = BuildBasisNote(result, vatPercent);
            result.PricingInputsFingerprint = Fingerprint(list, wastage);
            result.ComputedAtUtc = DateTime.UtcNow;
            return result;
        }

        private static void AddSpecRows(
            SpecGroup group, OtherChargesSheet? charges, decimal wastage, ProfitLossResult result)
        {
            decimal cutSqm = group.TotalSqm;
            if (cutSqm <= 0m) return;
            decimal needed = GlassMeasure.WithWastage(cutSqm, wastage);
            SpecCostCard card = group.CostCard ?? new SpecCostCard();

            foreach (SpecCostPane pane in card.Panes)
            {
                decimal sheetSqm = GlassMeasure.SheetSqm(pane.SheetWidthMm, pane.SheetHeightMm);
                int sheets = pane.HasSheet ? GlassMeasure.SheetsRequired(needed, sheetSqm) : 0;

                // A sheet quantity the operator set on this colour's wastage row
                // wins over the estimate. A disabled charge still owns it.
                int setup = SheetQuantityFor(charges, pane.Colour);
                if (setup > 0) sheets = setup;

                decimal amount = sheets > 0 && pane.CostPricePerSqm > 0m
                    ? MoneyRounding.Round(sheetSqm * sheets * pane.CostPricePerSqm)
                    : 0m;

                result.Rows.Add(new ProfitLossRow
                {
                    Kind = ProfitLossRowKind.Glass,
                    SpecNumber = group.GroupNumber,
                    SpecName = group.DisplayText,
                    Description = pane.PaneLabel + " pane · " + pane.Colour +
                        (pane.HasSheet ? "" : " · no stock sheet recorded"),
                    SheetWidthMm = pane.SheetWidthMm,
                    SheetHeightMm = pane.SheetHeightMm,
                    SheetSqm = sheetSqm,
                    Quantity = sheets,
                    UnitLabel = "sheet",
                    PricePerSqm = pane.CostPricePerSqm,
                    Amount = amount,
                    CostBasis = pane.CostBasis
                });
            }

            AddProductionRow(result, group, cutSqm, ProfitLossRowKind.Spacer,
                "Air spacer", card.SpacerCostPerSqm);
            AddProductionRow(result, group, cutSqm, ProfitLossRowKind.Interlayer,
                "Interlayer", card.InterlayerCostPerSqm);
            AddProductionRow(result, group, cutSqm, ProfitLossRowKind.Cutting,
                "Cutting", card.CuttingCostPerSqm);
            AddProductionRow(result, group, cutSqm, ProfitLossRowKind.Tempering,
                "Tempering", card.TemperingCostPerSqm);
        }

        private static void AddProductionRow(
            ProfitLossResult result, SpecGroup group, decimal cutSqm,
            ProfitLossRowKind kind, string label, decimal ratePerSqm)
        {
            if (ratePerSqm <= 0m) return;
            result.Rows.Add(new ProfitLossRow
            {
                Kind = kind,
                SpecNumber = group.GroupNumber,
                SpecName = group.DisplayText,
                Description = label + " · " + PricingText.Number(cutSqm) + " m²",
                Quantity = cutSqm,
                UnitLabel = "m²",
                PricePerSqm = ratePerSqm,
                Amount = MoneyRounding.Round(cutSqm * ratePerSqm),
                CostBasis = "quoted production rate"
            });
        }

        private static int SheetQuantityFor(OtherChargesSheet? charges, string? colour)
        {
            string wanted = (colour ?? "").Trim();
            if (charges == null || wanted.Length == 0) return 0;
            WastageChargeRow? match = charges.Wastages.FirstOrDefault(row =>
                !row.IsManuallyAdded &&
                row.SheetQuantity > 0 &&
                string.Equals(row.ColorName.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
            return match?.SheetQuantity ?? 0;
        }

        private static void AddTargets(IReadOnlyList<SpecGroup> groups, ProfitLossResult result)
        {
            decimal givenValue = 0m, requiredValue = 0m, area = 0m, baseArea = 0m;
            decimal givenArea = 0m, requiredArea = 0m;

            foreach (SpecGroup group in groups)
            {
                decimal groupArea = group.TotalSqm;
                decimal given = group.EffectiveRatePerSqm;
                decimal basis = group.CostCard?.CostBasisPerSqm ?? 0m;
                decimal uplift = group.CostCard?.TargetUpliftPercent ?? PricingEstimator.DefaultTargetUpliftPercent;

                var target = new SpecTargetRow
                {
                    SpecNumber = group.GroupNumber,
                    SpecName = group.DisplayText,
                    AreaSqm = groupArea,
                    GivenPricePerSqm = given,
                    CostBasisPerSqm = basis,
                    TargetUpliftPercent = uplift
                };

                if (basis <= 0m)
                {
                    target.Status = TargetStatus.NotCalculable;
                    target.Warnings.Add("No cost basis is recorded for this specification.");
                    result.TargetIncompleteCount++;
                }
                else
                {
                    decimal specificationTarget = MoneyRounding.Round(basis * (1m + uplift / 100m));
                    target.SpecificationTargetPerSqm = specificationTarget;
                    target.DifferencePerSqm = MoneyRounding.Round(given - specificationTarget);
                    target.ExtraProfitPerSqm = MoneyRounding.Round(given - basis);
                    target.MarginAgainstTargetPercent = specificationTarget <= 0m
                        ? null
                        : MoneyRounding.RoundPercent(target.DifferencePerSqm / specificationTarget * 100m);
                    target.ExtraProfitMarginPercent = basis <= 0m
                        ? null
                        : MoneyRounding.RoundPercent(target.ExtraProfitPerSqm / basis * 100m);

                    decimal tolerance = specificationTarget * AtTargetTolerancePercent / 100m;
                    target.Status = target.DifferencePerSqm > tolerance
                        ? TargetStatus.AboveTarget
                        : target.DifferencePerSqm < -tolerance
                            ? TargetStatus.BelowTarget
                            : TargetStatus.AtTarget;
                }

                result.SpecTargets.Add(target);
                givenValue += given * groupArea;
                requiredValue += target.SpecificationTargetPerSqm * groupArea;
                area += groupArea;
                givenArea += given * groupArea;
                requiredArea += target.SpecificationTargetPerSqm * groupArea;
                baseArea += basis * groupArea;
            }

            result.TargetTotalGivenValue = MoneyRounding.Round(givenValue);
            result.TargetTotalRequiredValue = MoneyRounding.Round(requiredValue);
            result.TargetTotalDifferenceValue = MoneyRounding.Round(givenValue - requiredValue);
            result.TargetAverageGivenPerSqm = area <= 0m ? 0m : MoneyRounding.Round(givenArea / area);
            result.TargetAverageRequiredPerSqm = area <= 0m ? 0m : MoneyRounding.Round(requiredArea / area);
            result.TargetAverageBasePerSqm = area <= 0m ? 0m : MoneyRounding.Round(baseArea / area);

            if (result.TargetIncompleteCount > 0 && result.SpecTargets.Count == result.TargetIncompleteCount)
                result.TargetStatus = TargetStatus.NotCalculable;
            else if (result.TargetTotalDifferenceValue > 0m)
                result.TargetStatus = TargetStatus.AboveTarget;
            else if (result.TargetTotalDifferenceValue < 0m)
                result.TargetStatus = TargetStatus.BelowTarget;
            else
                result.TargetStatus = TargetStatus.AtTarget;
        }

        private static string BuildBasisNote(ProfitLossResult result, decimal vatPercent)
        {
            string vat = vatPercent.ToString("0.##", CultureInfo.InvariantCulture);
            return "Glass is costed by whole stock sheets after a " +
                PricingText.Number(result.WastagePercent) + "% wastage allowance, at the rate recorded " +
                "on each specification (purchase rate where stated, quoted pane rate otherwise). " +
                "Production rows use the quoted spacer, interlayer, cutting and tempering rates. " +
                "Revenue is the VAT-exclusive total (" + vat + "% VAT removed).";
        }

        private static string Fingerprint(IReadOnlyList<SpecGroup> groups, decimal wastage)
        {
            var parts = new List<string> { "pl1", PricingText.Number(wastage) };
            foreach (SpecGroup group in groups)
            {
                parts.Add(group.GroupNumber.ToString(CultureInfo.InvariantCulture));
                parts.Add(group.GlassType);
                parts.Add(PricingText.Number(group.EffectiveRatePerSqm));
                parts.Add(PricingText.Number(group.TotalSqm));
                parts.Add(PricingText.Number(group.CostCard?.CostBasisPerSqm ?? 0m));
                foreach (SpecCostPane pane in group.CostCard?.Panes ?? new List<SpecCostPane>())
                {
                    parts.Add(pane.PaneLabel);
                    parts.Add(PricingText.Number(pane.CostPricePerSqm));
                    parts.Add(pane.SheetWidthMm.ToString(CultureInfo.InvariantCulture));
                    parts.Add(pane.SheetHeightMm.ToString(CultureInfo.InvariantCulture));
                }
            }

            // A stable, short signature — not a cryptographic hash, and never
            // presented as one.
            string joined = string.Join("|", parts);
            return joined.Length <= 64 ? joined : joined.Substring(0, 64);
        }
    }
}
