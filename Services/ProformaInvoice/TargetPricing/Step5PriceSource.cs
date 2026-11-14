// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/TargetPricing/Step5PriceSource.cs
// PROJECT: ProGlassAutomation
// MODULE: Shared target-price calculation layer
//
// THE GIVEN SELLING PRICE SOURCE
//   The given price is the Step 5 → Specification → Base Price of each spec
//   group (SpecGroup.BaseRate, AED per SQM). It is NEVER read from Step 4's
//   calculated price, Step 9's own figures, the Sheet Store, actual cost or
//   the gross invoice total.
//
// This type centralises that rule and the missing-price validation so no
// consumer can silently fall back to another price source.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Services.ProformaInvoice.TargetPricing
{
    public static class Step5PriceSource
    {
        /// <summary>UI label for the given-price source.</summary>
        public const string SourceLabel = "Step 5 Specification Base Price";

        public const string MissingBasePriceWarning =
            "Missing Step 5 Base Price — enter the given price on the Step 5 specification row.";

        /// <summary>
        /// The given selling price for a specification group.
        /// <paramref name="baseRatePerSqm"/> is SpecGroup.BaseRate (AED/m²).
        /// </summary>
        public static decimal GivenPricePerSqm(decimal baseRatePerSqm)
        {
            return Math.Max(0m, baseRatePerSqm);
        }

        public static bool HasGivenPrice(decimal baseRatePerSqm) => baseRatePerSqm > 0m;

        public static string Describe(decimal baseRatePerSqm)
        {
            return HasGivenPrice(baseRatePerSqm)
                ? $"{SourceLabel} — AED {Math.Round(baseRatePerSqm, 2):0.##}/m²"
                : $"{SourceLabel} — not set";
        }
    }
}
