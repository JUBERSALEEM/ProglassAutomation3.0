// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/TargetPricing/TargetMarginTolerance.cs
// PROJECT: ProGlassAutomation
// MODULE: Single source of truth for the "at target" margin band
//
// WHY THIS EXISTS (2026-09-08):
//   The band that decides ABOVE / AT / BELOW TARGET was declared in four
//   places, with two different values:
//
//     SpecificationTargetCalculator.cs:51   0.005m   (>= / <=)
//     TargetPriceModels.cs:604              0.005m   (>= / <=, hardcoded)
//     TargetSnapshotRecompute.cs:81         0.05m    (>  / < )
//     JobProfitabilityRow.cs:255            0.05m    (>  / < , hardcoded)
//
//   So the proforma side and the Job Profitability side disagreed by a
//   factor of ten. An invoice whose margin against target sat between
//   0.005 and 0.05 read ABOVE TARGET on the quotation and AT TARGET in Job
//   Profitability — two screens, two answers, no error anywhere. Exactly
//   the class of defect that produced the Job Order and Delivery Note
//   dashboard splits fixed alongside this one.
//
//   All four sites now call this type. There is one number and one
//   comparison convention, and a golden test pins both.
//
// DECISION RECORDED: 0.005m. Chosen because the two Proforma sites are the
//   ones that FIRST classify a margin and persist it; the other two are
//   read-side views of that same judgement, and TargetSnapshotRecompute
//   exists specifically to rebuild a stored snapshot ("Old invoices
//   therefore correct themselves on view"), so it must reproduce the
//   original band rather than widen it. This DOES change Job Profitability
//   output for margins between 0.005 and 0.05 — they now read ABOVE/BELOW
//   instead of AT, agreeing with the quotation. If the business rule is
//   actually ±0.05, change AtTargetPercent here and nowhere else, and
//   update TargetMarginToleranceTests.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.ProformaInvoice.TargetPricing
{
    /// <summary>
    /// The band around zero within which a margin against target counts as
    /// AT TARGET rather than ABOVE or BELOW.
    /// </summary>
    public static class TargetMarginTolerance
    {
        /// <summary>
        /// Half-width of the AT TARGET band, in the same units as
        /// <c>MarginAgainstTargetPercent</c>. One value, used everywhere.
        /// </summary>
        public const decimal AtTargetPercent = 0.005m;

        /// <summary>
        /// Margin is meaningfully above target. Inclusive at the boundary:
        /// exactly <c>+0.005</c> counts as ABOVE, matching
        /// <see cref="SpecificationTargetCalculator"/>, which is the site
        /// that first classifies and persists the status.
        /// </summary>
        public static bool IsAbove(decimal marginAgainstTargetPercent) =>
            marginAgainstTargetPercent >= AtTargetPercent;

        /// <summary>Mirror of <see cref="IsAbove"/>.</summary>
        public static bool IsBelow(decimal marginAgainstTargetPercent) =>
            marginAgainstTargetPercent <= -AtTargetPercent;

        /// <summary>Inside the band — neither meaningfully above nor below.</summary>
        public static bool IsAt(decimal marginAgainstTargetPercent) =>
            !IsAbove(marginAgainstTargetPercent) && !IsBelow(marginAgainstTargetPercent);
    }
}
