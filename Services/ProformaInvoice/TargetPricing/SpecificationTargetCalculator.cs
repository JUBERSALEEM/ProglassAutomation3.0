// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/TargetPricing/SpecificationTargetCalculator.cs
// PROJECT: ProGlassAutomation
// MODULE: Shared target-price calculation layer
//
// APPROVED KPI MARGIN (2026-09-04 — the BASE price is the trigger):
//     Base Price (DGU) = (Outer Glass Rate + Inner Glass Rate) ÷ Yield + ASP
//                        e.g. (25 + 25) ÷ 0.85 + 45 = 103.82
//     Given judged for the margin =
//         Given Price                       (U-insert billed separately)
//         Given Price − U-insert rate       (U-insert inside the given price)
//     Extra profit vs base (AED/m²) = Given judged − Base Price
//     Margin against target %        = (Given judged − Base Price)
//                                      ÷ Base Price × 100
//   The margin headline is ALWAYS judged on the GIVEN price against the
//   BASE price — never against the final required target.
//
//   Specification target (DGU example) — the required-target benchmark:
//     ((Outer Glass Rate + Inner Glass Rate) ÷ Yield Factor + ASP Rate) × 1.15
//   The target-based margin (Given vs Final Required Target) is still
//   computed and still drives the ABOVE/BELOW/AT-TARGET status pills.
//
// Yield Factor is supplied by the caller: Step 9 derives it from the
// WASTAGE % box (yield = 1 − wastage/100), so that box moves the
// required target exactly as approved. Step 6 wastage CHARGES never
// enter this model — they are billed production costs in Sheet Detail,
// not part of the target KPI.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.Services.ProformaInvoice.TargetPricing
{
    /// <summary>
    /// Pure, deterministic per-specification target-price calculator.
    /// No WPF, no Sheet Store, no dispatcher — the same code drives Step 9,
    /// the saved snapshot and the unit tests.
    /// </summary>
    public static class SpecificationTargetCalculator
    {
        /// <summary>
        /// The approved target uplift applied to the specification formula
        /// result. Never read from a competing margin setting.
        /// </summary>
        public const decimal DefaultTargetUpliftPercent = 15m;

        /// <summary>
        /// Tolerance used to decide AT TARGET vs ABOVE/BELOW TARGET. Alias for
        /// <see cref="TargetMarginTolerance.AtTargetPercent"/> — kept as a
        /// local name so the classification code below reads the same, but the
        /// VALUE now has exactly one declaration.
        /// </summary>
        private const decimal AtTargetTolerancePercent = TargetMarginTolerance.AtTargetPercent;

        public static SpecTargetPriceResult Calculate(
            int specNumber,
            string specName,
            string glassTypeCode,
            GlassBuildType buildType,
            SpecificationRateCard card,
            decimal givenPricePerSqm,
            decimal areaSqm)
        {
            var result = new SpecTargetPriceResult
            {
                SpecNumber = specNumber,
                SpecName = specName ?? "",
                GlassTypeCode = glassTypeCode ?? "",
                BuildType = buildType,
                GivenPricePerSqm = givenPricePerSqm,
                AreaSqm = Math.Max(0m, areaSqm),
                OuterGlassRatePerSqm = card?.OuterGlassRatePerSqm ?? 0m,
                InnerGlassRatePerSqm = card?.InnerGlassRatePerSqm ?? 0m,
                ThirdGlassRatePerSqm = card?.ThirdGlassRatePerSqm ?? 0m,
                AspRatePerSqm = card?.AspRatePerSqm ?? 0m,
                ProcessingRatePerSqm = card?.ProcessingRatePerSqm ?? 0m,
                YieldFactor = card?.YieldFactor ?? 0m,
                TargetUpliftPercent = (card?.TargetUpliftPercent ?? 0m) > 0m
                    ? card!.TargetUpliftPercent
                    : DefaultTargetUpliftPercent
            };

            if (card == null)
            {
                result.Status = TargetStatus.CalculationIncomplete;
                result.Warnings.Add("No rate card could be built for this specification.");
                FinalizeValues(result);
                return result;
            }

            // ── Input validation (never silently treat missing as zero) ──
            if (buildType == GlassBuildType.Unknown)
            {
                result.Status = TargetStatus.CalculationIncomplete;
                result.Warnings.Add(
                    "Could not identify the build (DGU / SGU / LAM / DGU+LAM) for this specification.");
                FinalizeValues(result);
                return result;
            }

            if (result.YieldFactor <= 0m)
            {
                result.Status = TargetStatus.MissingYield;
                result.Warnings.Add("Missing yield factor — the specification target cannot be calculated.");
                FinalizeValues(result);
                return result;
            }

            if (!HasRequiredGlassRates(result, buildType))
            {
                result.Status = TargetStatus.MissingGlassRate;
                result.Warnings.Add("Missing glass rate — the specification target cannot be calculated.");
                FinalizeValues(result);
                return result;
            }

            if (RequiresAsp(buildType) && result.AspRatePerSqm <= 0m)
            {
                result.Status = TargetStatus.MissingAspRate;
                result.Warnings.Add("Missing ASP/spacer rate — the specification target cannot be calculated.");
                FinalizeValues(result);
                return result;
            }

            // All required formula inputs are present.
            result.Status = TargetStatus.UpToDate;

            // ── Specification formula ────────────────────────────────────
            decimal glassSum = GlassRateSum(result, buildType);
            decimal addons = result.AspRatePerSqm + result.ProcessingRatePerSqm;
            decimal upliftFactor = 1m + result.TargetUpliftPercent / 100m;

            decimal preUplift = (glassSum / result.YieldFactor) + addons;

            // Base price = the pre-uplift formula result (the KPI trigger),
            // rounded at 2dp exactly as the shop writes it down:
            //   (25 + 25) ÷ 0.85 + 45 = 103.82 → × 1.15 = 119.39.
            // The specification target lifts the ROUNDED base price
            // (103.82 × 1.15 = 119.39) so the written-down numbers on the
            // card always reconcile with the shop's own arithmetic.
            decimal basePerSqm = MoneyRounding.Round(preUplift);
            result.BasePricePerSqm = basePerSqm;
            result.SpecificationTargetPerSqm = MoneyRounding.Round(basePerSqm * upliftFactor);

            // Final required target starts as the base-price specification
            // target (the U-insert rate is never folded in — ApplyUInsert
            // keeps it at this value for both treatments).
            result.FinalRequiredTargetPerSqm = result.SpecificationTargetPerSqm;
            result.FormulaText = BuildFormulaText(result, buildType, upliftFactor);

            FinalizeValues(result);
            return result;
        }

        /// <summary>
        /// Applies the resolved U-insert treatment to a calculated spec
        /// result and derives the final target, difference and margin.
        /// Call after <see cref="Calculate"/> on every result.
        ///
        /// Revised rule (2026-09-04): the U-insert rate is NEVER added into
        /// the FINAL REQUIRED TARGET. The required target a quotation is
        /// judged against is always the base-price specification target
        /// (DGU formula result × uplift, e.g. ((25+25)÷0.85+45)×1.15 =
        /// AED 119.39/m²) — for BOTH treatments:
        ///   • Treatment == Additional (Step 6 U-insert row active): the
        ///     extra is billed as its own Step 6 line, so it never belonged
        ///     in the target; the rate is carried for display only.
        ///   • Treatment == IncludedInStep5Price (Step 6 row not active —
        ///     U-insert given inside the base price): the customer's given
        ///     price already includes the U-insert, so adding the rate to
        ///     the required target would DOUBLE-COUNT it and push a
        ///     genuinely profitable quotation (e.g. given 125 ≈ 20% over a
        ///     103.82 basis) below target. The base-price target is used
        ///     unchanged; the U-insert rate stays visible on the KPI as
        ///     informational only.
        /// </summary>
        public static void ApplyUInsert(
            SpecTargetPriceResult result,
            UInsertResolution resolution)
        {
            if (result == null) return;
            resolution ??= new UInsertResolution();

            result.UInsertTreatment = resolution.Treatment;
            result.UInsertRatePerSqm = Math.Max(0m, resolution.RatePerSqm);
            result.UInsertSourceLabel = "Step 6 Other Charges — U-insert";

            // Informational only — the required target never moves.
            result.AppliedUInsertPerSqm = 0m;
            result.FinalRequiredTargetPerSqm = result.SpecificationTargetPerSqm;

            if (resolution.HasWarning
                && !result.Warnings.Contains(resolution.Warning, StringComparer.Ordinal))
            {
                result.Warnings.Add(resolution.Warning);
            }

            // Only promote to the U-insert state when the rest of the
            // formula is complete — a missing yield must stay MissingYield.
            if (resolution.Treatment == UInsertTreatment.Unresolved
                && result.Status is TargetStatus.UpToDate
                                or TargetStatus.BelowTarget
                                or TargetStatus.AtTarget
                                or TargetStatus.AboveTarget)
            {
                result.Status = TargetStatus.MissingUInsertTreatment;
            }

            FinalizeValues(result);
        }

        /// <summary>
        /// Marks a specification whose saved snapshot predates the target
        /// model (or whose inputs are unavailable) as stale/incomplete so the
        /// UI never paints a fake 0% margin.
        /// </summary>
        public static SpecTargetPriceResult StaleResult(
            int specNumber, string specName, string staleReason)
        {
            return new SpecTargetPriceResult
            {
                SpecNumber = specNumber,
                SpecName = specName ?? "",
                Status = TargetStatus.SnapshotStale,
                Warnings =
                {
                    string.IsNullOrWhiteSpace(staleReason)
                        ? "Saved profitability data predates the target-price model — reopen Step 9 and save to refresh."
                        : staleReason
                }
            };
        }

        // ────────────────────────────────────────────────────────────────

        private static bool HasRequiredGlassRates(
            SpecTargetPriceResult r, GlassBuildType buildType)
        {
            return buildType switch
            {
                GlassBuildType.Sgu => r.OuterGlassRatePerSqm > 0m,
                GlassBuildType.Dgu => r.OuterGlassRatePerSqm > 0m && r.InnerGlassRatePerSqm > 0m,
                GlassBuildType.Lam => r.OuterGlassRatePerSqm > 0m && r.InnerGlassRatePerSqm > 0m,
                GlassBuildType.DguLam => r.OuterGlassRatePerSqm > 0m
                                         && r.InnerGlassRatePerSqm > 0m
                                         && r.ThirdGlassRatePerSqm > 0m,
                _ => false
            };
        }

        private static bool RequiresAsp(GlassBuildType buildType) =>
            buildType is GlassBuildType.Dgu or GlassBuildType.DguLam;

        private static decimal GlassRateSum(
            SpecTargetPriceResult r, GlassBuildType buildType)
        {
            return buildType switch
            {
                GlassBuildType.Sgu => r.OuterGlassRatePerSqm,
                GlassBuildType.Dgu => r.OuterGlassRatePerSqm + r.InnerGlassRatePerSqm,
                GlassBuildType.Lam => r.OuterGlassRatePerSqm + r.InnerGlassRatePerSqm,
                GlassBuildType.DguLam => r.OuterGlassRatePerSqm
                                         + r.InnerGlassRatePerSqm
                                         + r.ThirdGlassRatePerSqm,
                _ => 0m
            };
        }

        private static string BuildFormulaText(
            SpecTargetPriceResult r, GlassBuildType buildType, decimal upliftFactor)
        {
            string glass;
            string yieldDisp = Fmt(r.YieldFactor);

            switch (buildType)
            {
                case GlassBuildType.Sgu:
                    glass = Fmt(r.OuterGlassRatePerSqm);
                    break;
                case GlassBuildType.Dgu:
                case GlassBuildType.Lam:
                    glass = $"({Fmt(r.OuterGlassRatePerSqm)} + {Fmt(r.InnerGlassRatePerSqm)})";
                    break;
                default:
                    glass = $"({Fmt(r.OuterGlassRatePerSqm)} + {Fmt(r.InnerGlassRatePerSqm)}"
                            + $" + {Fmt(r.ThirdGlassRatePerSqm)})";
                    break;
            }

            var inner = new List<string> { $"{glass} ÷ {yieldDisp}" };

            if (r.AspRatePerSqm > 0m)
                inner.Add(Fmt(r.AspRatePerSqm));
            if (r.ProcessingRatePerSqm > 0m)
                inner.Add(Fmt(r.ProcessingRatePerSqm));

            return $"({string.Join(" + ", inner)}) × {Fmt(upliftFactor)}";
        }

        private static void FinalizeValues(SpecTargetPriceResult r)
        {
            // The FINAL required target (after any additional U-insert) is the
            // authoritative per-SQM basis once it exists; before the U-insert
            // merge the specification target stands in for it.
            decimal targetPerSqm = r.FinalRequiredTargetPerSqm > 0m
                ? r.FinalRequiredTargetPerSqm
                : r.SpecificationTargetPerSqm;

            if (targetPerSqm > 0m)
            {
                r.RequiredTargetValue = MoneyRounding.Round(targetPerSqm * r.AreaSqm);
            }

            if (r.GivenPricePerSqm > 0m)
            {
                r.GivenValue = MoneyRounding.Round(r.GivenPricePerSqm * r.AreaSqm);
                r.DifferencePerSqm = MoneyRounding.Round(r.GivenPricePerSqm - targetPerSqm);
                r.DifferenceValue = MoneyRounding.Round(r.GivenValue - r.RequiredTargetValue);

                // Given JUDGED for the margin: when the U-insert is covered
                // INSIDE the given price (IncludedInStep5Price) the customer
                // pays for it out of the same given price, so only
                // (given − rate) is really the glass price the margin is
                // judged on. A separate Step 6 charge (Additional) leaves
                // the given price untouched.
                decimal judged = r.GivenPricePerSqm;
                if (r.UInsertTreatment == UInsertTreatment.IncludedInStep5Price)
                    judged = Math.Max(0m, judged - Math.Max(0m, r.UInsertRatePerSqm));
                r.GivenPriceJudgedPerSqm = MoneyRounding.Round(judged);

                bool marginEligible = r.Status is not (TargetStatus.MissingBasePrice
                        or TargetStatus.MissingGlassRate
                        or TargetStatus.MissingAspRate
                        or TargetStatus.MissingYield
                        or TargetStatus.MissingUInsertTreatment
                        or TargetStatus.CalculationIncomplete
                        or TargetStatus.SnapshotStale);

                if (targetPerSqm > 0m && marginEligible)
                {
                    // Status margin: judged given vs the required target. The
                    // judged given nets any U-insert that is covered INSIDE the
                    // given price, so a toggle-off quotation is judged on the
                    // price that is really the glass (115, not 125).
                    decimal margin = (judged - targetPerSqm)
                                     / targetPerSqm * 100m;
                    r.MarginAgainstTargetPercent = Math.Round(margin, 2);

                    // Re-derive the classification whenever the result is
                    // already in a CLASSIFIED state — not only from UpToDate.
                    //
                    // Calculate() leaves the status at AboveTarget/BelowTarget/
                    // AtTarget, and ApplyUInsert() then calls FinalizeValues
                    // again after netting an included U-insert out of the
                    // judged price. With the guard limited to UpToDate, that
                    // second pass recomputed the margin but left the status
                    // frozen at whatever Calculate() decided: a toggle-off
                    // quotation showed -2.20% beside a stale ABOVE TARGET.
                    // The two numbers on the same card disagreed, and the
                    // disagreement was silent.
                    //
                    // Safe because marginEligible above already excludes every
                    // incomplete state (Missing*, CalculationIncomplete,
                    // SnapshotStale), so this cannot promote a result whose
                    // formula inputs are absent.
                    if (r.Status is TargetStatus.UpToDate
                                    or TargetStatus.BelowTarget
                                    or TargetStatus.AtTarget
                                    or TargetStatus.AboveTarget)
                    {
                        decimal m = r.MarginAgainstTargetPercent.Value;
                        r.Status = TargetMarginTolerance.IsBelow(m)
                            ? TargetStatus.BelowTarget
                            : TargetMarginTolerance.IsAbove(m)
                                ? TargetStatus.AboveTarget
                                : TargetStatus.AtTarget;
                    }
                }

                // Base-price judgement (the KPI trigger): extra profit and
                // margin are measured from the BASE price, never from the
                // final required target.
                if (r.BasePricePerSqm > 0m && marginEligible)
                {
                    decimal extra = judged - r.BasePricePerSqm;
                    r.ExtraProfitPerSqm = MoneyRounding.Round(extra);
                    r.ExtraProfitMarginPercent = Math.Round(
                        extra / r.BasePricePerSqm * 100m, 2);
                }
                else
                {
                    r.ExtraProfitPerSqm = null;
                    r.ExtraProfitMarginPercent = null;
                }
            }
            else
            {
                r.DifferencePerSqm = 0m;
                r.DifferenceValue = 0m;
                r.MarginAgainstTargetPercent = null;
                r.GivenPriceJudgedPerSqm = 0m;
                r.ExtraProfitPerSqm = null;
                r.ExtraProfitMarginPercent = null;

                if (r.Status is TargetStatus.UpToDate
                    or TargetStatus.BelowTarget
                    or TargetStatus.AtTarget
                    or TargetStatus.AboveTarget)
                {
                    r.Status = TargetStatus.MissingBasePrice;
                }

                if (!r.Warnings.Contains("Missing Step 5 Base Price — the given price is not set.",
                        StringComparer.Ordinal))
                {
                    r.Warnings.Add("Missing Step 5 Base Price — the given price is not set.");
                }
            }
        }

        /// <summary>Formats a decimal without trailing zeros ("47", "0.85", "1.15").</summary>
        public static string Fmt(decimal value)
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }
}
