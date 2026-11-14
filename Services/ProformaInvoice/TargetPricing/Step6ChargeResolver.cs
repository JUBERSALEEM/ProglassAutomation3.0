// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/TargetPricing/Step6ChargeResolver.cs
// PROJECT: ProGlassAutomation
// MODULE: U-insert treatment + rate resolution (single dedicated resolver)
//
// APPROVED BUSINESS RULE (revised 2026-09-04):
//   The Step 6 U-insert row toggle decides whether the U-insert extra is
//   billed SEPARATELY or is covered BY the Step 5 price. Either way the
//   FINAL REQUIRED TARGET stays the base-price specification target —
//   the resolved rate never moves the target:
//
//   • Step 6 U-insert row ACTIVE for the spec ("trigger on"):
//       The U-insert is charged as its own Step 6 line on top of the
//       given price. The target check compares the Step 5 given price
//       against the NORMAL specification target — the U-insert is NOT
//       added (its cost is recovered by the Step 6 charge row).
//
//   • Step 6 U-insert row NOT active ("toggle off"):
//       The U-insert is covered inside the Step 5 given/base price, so
//       the customer's given price already includes it. Adding the rate
//       to the required target would double-count it (a 125 all-in given
//       ≈ 20% over the 103.82 basis would be judged against a folded
//       129.39 instead of the base-price 119.39). The target stays the
//       base-price target; the rate is carried for the KPI display only.
//
//   The rate is ALWAYS read dynamically — Step 4 per-spec seed wins,
//   then the Step 6 row rate, then the Step 6 template default.
//   AED 7 / AED 10 are never hardcoded here.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Services.ProformaInvoice.TargetPricing
{
    /// <summary>Inputs the resolver needs. Built by the Step 9 adapter.</summary>
    public sealed class Step6UInsertQuery
    {
        /// <summary>
        /// True when this specification actually carries a U-insert extra
        /// (spec name / automation suggestion evidence).
        /// </summary>
        public bool SpecRequiresUInsert { get; set; }

        /// <summary>True when a Step 6 U-insert charge row exists.</summary>
        public bool RowExists { get; set; }

        /// <summary>True when the Step 6 U-insert row is active FOR THIS SPEC.</summary>
        public bool RowActiveForSpec { get; set; }

        /// <summary>Step 4 per-spec U-insert seed (SpecGroup.UInsertPricePerSqm).</summary>
        public decimal Step4SeedRate { get; set; }

        /// <summary>Rate typed on the Step 6 U-insert row.</summary>
        public decimal RowRate { get; set; }

        /// <summary>Step 6 U-insert template default rate.</summary>
        public decimal TemplateDefaultRate { get; set; }
    }

    /// <summary>
    /// Resolves U-insert accounting treatment and the dynamic rate for one
    /// specification. Pure and deterministic.
    /// </summary>
    public static class Step6ChargeResolver
    {
        public static UInsertResolution ResolveUInsert(Step6UInsertQuery query)
        {
            query ??= new Step6UInsertQuery();
            var result = new UInsertResolution();

            if (!query.SpecRequiresUInsert)
            {
                result.Treatment = UInsertTreatment.NotApplicable;
                result.RatePerSqm = 0m;
                return result;
            }

            // Dynamic rate order — never a hardcoded AED 7 / AED 10.
            decimal rate = query.Step4SeedRate > 0m
                ? query.Step4SeedRate
                : query.RowRate > 0m
                    ? query.RowRate
                    : query.TemplateDefaultRate > 0m
                        ? query.TemplateDefaultRate
                        : 0m;

            if (query.RowExists && query.RowActiveForSpec)
            {
                // "Trigger ON": the U-insert is billed separately in Step 6,
                // so the TARGET stays the normal specification target. The
                // resolved rate is kept for display ("Step 6 extra · rate")
                // but is NOT added into the required target.
                result.Treatment = UInsertTreatment.Additional;
                result.RatePerSqm = Math.Max(0m, rate);

                if (result.RatePerSqm <= 0m)
                {
                    // Row active but nobody configured a rate — we cannot say
                    // what the Step 6 extra is worth. Never guess.
                    result.Treatment = UInsertTreatment.Unresolved;
                    result.Warning =
                        "U-insert is billed as a Step 6 extra but no rate is available " +
                        "(Step 4 seed, Step 6 row rate and template default are all empty).";
                }

                return result;
            }

            // "Toggle OFF": the U-insert is covered inside the Step 5 price.
            // The resolved rate is carried so the KPI can SHOW the U-insert
            // on the card, but the required target itself is never moved
            // (the given price already includes the U-insert, so adding it
            // to the target would double-count it).
            result.Treatment = UInsertTreatment.IncludedInStep5Price;
            result.RatePerSqm = Math.Max(0m, rate);

            if (result.RatePerSqm <= 0m)
            {
                result.Treatment = UInsertTreatment.Unresolved;
                result.Warning =
                    "The U-insert is covered by the given price but no rate is configured " +
                    "(Step 4 seed, Step 6 row rate and template default are all empty), so " +
                    "the U-insert cannot be shown on the KPI.";
            }

            return result;
        }
    }
}
