// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/TargetPricing/TargetSnapshotRecompute.cs
// PROJECT: ProGlassAutomation
// MODULE: Corrected read-side rebuild of a saved target-price snapshot
//
// Why this exists (2026-09-04):
//   The required target a quotation is judged against is the BASE-PRICE
//   specification target (DGU formula result × uplift). Earlier builds
//   could persist a FINAL REQUIRED TARGET that had the U-insert rate folded
//   in (specification target + rate, e.g. 119.39 + 10 = 129.39). Because a
//   given price that already includes the U-insert must not be judged
//   against a target that also adds it, Job Profitability and its detail
//   sidebar rebuild each saved invoice from the per-specification rows
//   using ONLY the specification target. Old invoices therefore correct
//   themselves on view — no data migration.
//
//   Every rebuild result still honours the "never fake a margin" rule:
//   a job is only complete when EVERY specification row is complete; any
//   incomplete spec makes the whole job incomplete (null margin).
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.Services.ProformaInvoice.TargetPricing
{
    /// <summary>
    /// Corrected aggregate rebuilt from one invoice's per-specification
    /// snapshot rows. Null-safe output; callers fall back to the stored
    /// aggregate totals when no per-spec rows exist.
    /// </summary>
    public sealed class RebuiltTargetTotals
    {
        public decimal TotalAreaSqm { get; set; }
        public decimal TotalGivenValue { get; set; }
        public decimal TotalRequiredValue { get; set; }
        public decimal TotalDifferenceValue { get; set; }
        public decimal AverageGivenPricePerSqm { get; set; }
        public decimal AverageRequiredPerSqm { get; set; }

        // ── Base-price judgement (the KPI trigger) ─────────────────────
        /// <summary>Area-weighted average BASE price (pre-uplift formula, AED/m²).</summary>
        public decimal AverageBasePricePerSqm { get; set; }

        /// <summary>Σ (Base price × area) across the usable rows.</summary>
        public decimal TotalBaseValue { get; set; }

        /// <summary>Σ ((Given judged − Base price) × area) across the usable rows.</summary>
        public decimal TotalExtraValue { get; set; }

        /// <summary>Area-weighted average GIVEN JUDGED price (AED/m²).</summary>
        public decimal AverageGivenJudgedPerSqm { get; set; }

        /// <summary>
        /// Area-weighted average extra profit over the base price (AED/m²).
        /// Null when any row cannot be judged (never fake 0).
        /// </summary>
        public decimal? AverageExtraProfitPerSqm { get; set; }

        /// <summary>
        /// Job margin over the base price (value-weighted), mirroring Step 9.
        /// Null when any row cannot be judged.
        /// </summary>
        public decimal? ExtraProfitMarginPercent { get; set; }

        /// <summary>Null when any specification row is incomplete.</summary>
        public decimal? MarginAgainstTargetPercent { get; set; }

        /// <summary>TargetStatus enum name ("AboveTarget" / "BelowTarget" / …).</summary>
        public string Status { get; set; } = "CalculationIncomplete";

        public int ValidSpecCount { get; set; }
        public int IncompleteSpecCount { get; set; }
        public bool AllSpecsComplete { get; set; }
    }

    public static class TargetSnapshotRecompute
    {
        /// <summary>
        /// Was 0.05m here while the Proforma calculator used 0.005m, so a
        /// rebuild could re-classify a snapshot it was supposed to reproduce.
        /// Now aliased to the single declaration.
        /// </summary>
        private const decimal AtTargetTolerancePercent = TargetMarginTolerance.AtTargetPercent;

        /// <summary>TargetStatus names that mean "fully calculable".</summary>
        private static readonly HashSet<string> CompleteStatusNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "UpToDate", "AboveTarget", "AtTarget", "BelowTarget"
            };

        /// <summary>
        /// Rebuilds the job-level given/required/difference + margin from the
        /// per-specification snapshot rows. The per-spec required basis is
        /// ALWAYS the base-price <see cref="SpecTargetSnapshot.SpecificationTargetPerSqm"/>
        /// — never a stored final target that may have had the U-insert rate
        /// folded in by an older build.
        /// </summary>
        /// <returns>Null when there are no usable per-spec rows at all.</returns>
        public static RebuiltTargetTotals? Recompute(
            IReadOnlyList<SpecTargetSnapshot>? specs)
        {
            var rows = (specs ?? Array.Empty<SpecTargetSnapshot>())
                .Where(s => s != null)
                .ToList();

            if (rows.Count == 0) return null;

            bool usable(SpecTargetSnapshot s) =>
                s.AreaSqm > 0m
                && s.GivenPricePerSqm > 0m
                && s.SpecificationTargetPerSqm > 0m;

            var usableRows = rows.Where(usable).ToList();
            bool allComplete = usableRows.Count == rows.Count
                               && rows.All(s =>
                                   CompleteStatusNames.Contains(s.Status ?? "")
                                   || s.MarginAgainstTargetPercent.HasValue);

            var result = new RebuiltTargetTotals
            {
                AllSpecsComplete = allComplete,
                ValidSpecCount = rows.Count(s =>
                    CompleteStatusNames.Contains(s.Status ?? "")
                    || s.MarginAgainstTargetPercent.HasValue),
                IncompleteSpecCount = rows.Count(s =>
                    !CompleteStatusNames.Contains(s.Status ?? "")
                    && !s.MarginAgainstTargetPercent.HasValue)
            };

            decimal totalArea = 0m, given = 0m, required = 0m;
            decimal baseW = 0m, judgedW = 0m;
            foreach (var s in usableRows)
            {
                decimal area = Math.Max(0m, s.AreaSqm);
                totalArea += area;
                given += Math.Max(0m, s.GivenPricePerSqm) * area;
                required += Math.Max(0m, s.SpecificationTargetPerSqm) * area;

                // Base-price judgement (the KPI trigger): recompute the base
                // price from the stored component rates exactly as Step 9
                // does — never by dividing the uplifted target back down.
                decimal basePerSqm = SnapshotBasePricePerSqm(s);
                decimal judged = Math.Max(0m, s.GivenPricePerSqm);
                if (string.Equals(s.UInsertTreatment, "IncludedInStep5Price",
                        StringComparison.OrdinalIgnoreCase))
                {
                    judged = Math.Max(0m, judged - Math.Max(0m, s.UInsertRatePerSqm));
                }

                baseW += basePerSqm * area;
                judgedW += judged * area;
            }

            result.TotalAreaSqm = Math.Round(totalArea, 2);
            result.TotalGivenValue = MoneyRounding.Round(given);
            result.TotalRequiredValue = MoneyRounding.Round(required);
            result.TotalDifferenceValue = MoneyRounding.Round(given - required);
            result.AverageGivenPricePerSqm = totalArea > 0m
                ? MoneyRounding.Round(given / totalArea)
                : 0m;
            result.AverageRequiredPerSqm = totalArea > 0m
                ? MoneyRounding.Round(required / totalArea)
                : 0m;

            result.AverageBasePricePerSqm = totalArea > 0m
                ? MoneyRounding.Round(baseW / totalArea)
                : 0m;
            result.AverageGivenJudgedPerSqm = totalArea > 0m
                ? MoneyRounding.Round(judgedW / totalArea)
                : 0m;
            result.TotalBaseValue = MoneyRounding.Round(baseW);
            result.TotalExtraValue = MoneyRounding.Round(judgedW - baseW);

            // Base-price judgement aggregates. Only when EVERY row is
            // complete AND carries the component rates needed for the base
            // price — never a fake 0.
            if (allComplete
                && usableRows.Count > 0
                && baseW > 0m)
            {
                result.AverageExtraProfitPerSqm = MoneyRounding.Round(
                    (judgedW - baseW) / totalArea);
                result.ExtraProfitMarginPercent = Math.Round(
                    (judgedW - baseW) / baseW * 100m, 2);
            }
            else
            {
                result.AverageExtraProfitPerSqm = null;
                result.ExtraProfitMarginPercent = null;
            }

            if (allComplete
                && usableRows.Count > 0
                && result.TotalRequiredValue > 0m
                && judgedW > 0m)
            {
                // Status margin — judged given (net of any U-insert that is
                // inside the given price) vs the required target. Raw given
                // totals stay separate for the GIVEN VALUE displays.
                decimal margin = (judgedW - result.TotalRequiredValue)
                                 / result.TotalRequiredValue * 100m;
                result.MarginAgainstTargetPercent = Math.Round(margin, 2);

                decimal m = result.MarginAgainstTargetPercent.Value;
                // Boundary is now inclusive (>= / <=), matching the
                // calculator that first persisted this status. Previously this
                // used > / <, so a margin exactly on the band edge classified
                // differently here than it did at write time.
                result.Status = TargetMarginTolerance.IsAbove(m) ? "AboveTarget"
                    : TargetMarginTolerance.IsBelow(m) ? "BelowTarget"
                    : "AtTarget";
            }
            else if (allComplete)
            {
                // Complete rows but no money yet — never a fake 0% margin.
                result.Status = "MissingBasePrice";
            }
            else
            {
                // Surface the most important missing input, mirroring
                // TargetProfitabilityAggregator's ordering.
                var first = rows.FirstOrDefault(s =>
                    !CompleteStatusNames.Contains(s.Status ?? "")
                    && !s.MarginAgainstTargetPercent.HasValue);
                result.Status = !string.IsNullOrWhiteSpace(first?.Status)
                    ? first!.Status!
                    : "CalculationIncomplete";
            }

            return result;
        }

        /// <summary>
        /// Recomputes the BASE price of a saved spec row from its stored
        /// component rates — the exact pre-uplift formula Step 9 uses:
        ///   DGU/LAM: (Outer + Inner) ÷ Yield + ASP + processing
        ///   SGU:      Outer ÷ Yield + processing
        ///   DGU+LAM:  (Outer + Inner + Third) ÷ Yield + ASP + processing
        /// Returns 0 when the stored row does not carry the components
        /// (very old snapshot) so the caller can stay honest.
        /// </summary>
        public static decimal SnapshotBasePricePerSqm(SpecTargetSnapshot s)
        {
            if (s == null || s.YieldFactor <= 0m) return 0m;

            decimal glass = s.BuildType switch
            {
                "Sgu" => s.OuterGlassRatePerSqm,
                "Dgu" => s.OuterGlassRatePerSqm + s.InnerGlassRatePerSqm,
                "Lam" => s.OuterGlassRatePerSqm + s.InnerGlassRatePerSqm,
                "DguLam" => s.OuterGlassRatePerSqm + s.InnerGlassRatePerSqm
                            + s.ThirdGlassRatePerSqm,
                _ => 0m
            };

            if (glass <= 0m) return 0m;

            decimal preUplift = (glass / s.YieldFactor)
                                + Math.Max(0m, s.AspRatePerSqm)
                                + Math.Max(0m, s.ProcessingRatePerSqm);
            return MoneyRounding.Round(preUplift);
        }
    }
}
