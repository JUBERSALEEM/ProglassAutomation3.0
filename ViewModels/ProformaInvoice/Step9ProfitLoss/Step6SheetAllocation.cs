using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges.Wastages;
using ProGlassAutomation.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    /// <summary>
    /// Splits one Step 6 wastage setup row across its checked pane sources.
    ///
    /// Industrial meaning: one wastage setup row may intentionally combine
    /// matching glass from multiple specifications (for example the Low-E
    /// pane of Spec 1 and Spec 2). Step 6 owns the total bought sheet count;
    /// Step 9 must allocate that count back to each source without duplicating
    /// the whole quantity on every spec.
    /// </summary>
    internal static class Step6SheetAllocation
    {
        public sealed class Allocation
        {
            public SpecGroupColorSide Source { get; init; } = null!;
            public int SheetQuantity { get; init; }
            public decimal SheetSqm { get; init; }
            public decimal UtilizationPercent { get; init; }
            public decimal Weight { get; init; }
        }

        public static IReadOnlyList<Allocation> Allocate(
            WastageRow setup,
            IReadOnlyList<SpecGroupColorSide> sources)
        {
            if (setup == null || setup.SheetQuantity <= 0 || sources == null || sources.Count == 0)
                return Array.Empty<Allocation>();

            decimal utilizationPercent = setup.OptimizationPercent > 0m
                ? setup.OptimizationPercent
                : ResolveActualUtilizationPercent(setup, sources);
            decimal utilizationFactor = utilizationPercent / 100m;
            if (utilizationFactor <= 0m) utilizationFactor = 1m;

            var weighted = sources
                .Where(s => s != null && s.IsChecked)
                .Select(s =>
                {
                    decimal sheetSqm = SqmCalculator.CalculateSheetArea(
                        s.SheetWidthMm, s.SheetHeightMm);
                    if (sheetSqm <= 0m) return null;

                    // If utilization is 72%, one 7.2225 m² sheet contributes
                    // only 5.2002 useful m². Weight by the useful sheet area
                    // so a shared 17-sheet setup can split back to roughly
                    // 11 sheets for one spec and 6 for another when their
                    // Step 5 source SQM demands it.
                    decimal usefulSheetSqm = sheetSqm * utilizationFactor;
                    decimal weight = usefulSheetSqm > 0m && s.SqmValue > 0m
                        ? s.SqmValue / usefulSheetSqm
                        : 1m;

                    return new WeightedSource
                    {
                        Source = s,
                        SheetSqm = sheetSqm,
                        UtilizationPercent = utilizationPercent,
                        Weight = weight
                    };
                })
                .Where(x => x != null)
                .Cast<WeightedSource>()
                .ToList();

            if (weighted.Count == 0)
                return Array.Empty<Allocation>();

            decimal totalWeight = weighted.Sum(w => w.Weight);
            if (totalWeight <= 0m)
            {
                foreach (var w in weighted)
                    w.Weight = 1m;
                totalWeight = weighted.Count;
            }

            foreach (var w in weighted)
            {
                decimal exact = setup.SheetQuantity * (w.Weight / totalWeight);
                w.ExactSheets = exact;
                w.AllocatedSheets = (int)Math.Floor(exact);
                w.Remainder = exact - w.AllocatedSheets;
            }

            int allocated = weighted.Sum(w => w.AllocatedSheets);
            int remaining = setup.SheetQuantity - allocated;

            foreach (var w in weighted
                         .OrderByDescending(w => w.Remainder)
                         .ThenByDescending(w => w.Weight))
            {
                if (remaining <= 0) break;
                w.AllocatedSheets++;
                remaining--;
            }

            return weighted
                .Select(w => new Allocation
                {
                    Source = w.Source,
                    SheetQuantity = w.AllocatedSheets,
                    SheetSqm = w.SheetSqm,
                    UtilizationPercent = w.UtilizationPercent,
                    Weight = w.Weight
                })
                .Where(a => a.SheetQuantity > 0)
                .ToList();
        }

        private static decimal ResolveActualUtilizationPercent(
            WastageRow setup,
            IReadOnlyList<SpecGroupColorSide> sources)
        {
            if (setup == null || setup.SheetQuantity <= 0 || sources == null || sources.Count == 0)
                return 100m;

            var valid = sources
                .Where(s => s != null && s.IsChecked && s.SheetWidthMm > 0 && s.SheetHeightMm > 0)
                .ToList();
            if (valid.Count == 0) return 100m;

            decimal totalSourceSqm = valid.Sum(s => Math.Max(0m, s.SqmValue));
            if (totalSourceSqm <= 0m) return 100m;

            var sheetAreas = valid
                .Select(s => SqmCalculator.CalculateSheetArea(s.SheetWidthMm, s.SheetHeightMm))
                .Where(a => a > 0m)
                .Distinct()
                .ToList();

            // The usual industrial case is one glass type / one stock sheet
            // shared across specs. If Step 6 has no explicit optimization %,
            // infer utilization from the setup's selected SQM and total
            // bought sheet area rather than defaulting the Sheet Details KPI
            // to a misleading 100%.
            if (sheetAreas.Count == 1)
            {
                decimal boughtArea = setup.SheetQuantity * sheetAreas[0];
                if (boughtArea > 0m)
                    // 2026-09-11: inferred utilization MUST cap at 100%.
                    // >100% means Step 6 bought FEWER sheets than the checked
                    // sources need (e.g. default SHEET QTY 1 against a large
                    // pane) — that is a SHORTFALL, not super-efficiency (the
                    // "249%" report). The shortfall itself is surfaced by the
                    // caller via the needed-sheets comparison.
                    return Math.Round(Math.Min(100m, totalSourceSqm / boughtArea * 100m), 2);
            }

            return 100m;
        }

        private sealed class WeightedSource
        {
            public SpecGroupColorSide Source { get; init; } = null!;
            public decimal SheetSqm { get; init; }
            public decimal UtilizationPercent { get; init; }
            public decimal Weight { get; set; }
            public decimal ExactSheets { get; set; }
            public int AllocatedSheets { get; set; }
            public decimal Remainder { get; set; }
        }
    }
}
