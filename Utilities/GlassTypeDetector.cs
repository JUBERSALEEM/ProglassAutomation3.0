// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Utilities/GlassTypeDetector.cs                                          ║
// ║                                                                          ║
// ║  CHAT 13 HOTFIX-4 — NEW FILE                                             ║
// ║                                                                          ║
// ║  Detects glass type (SGU / DGU / LAM / DGU+LAM / Unknown) from a         ║
// ║  specification name by pattern matching against known industry markers:  ║
// ║                                                                          ║
// ║    ASP  → spacer present  → DGU family                                   ║
// ║    PVB  → interlayer      → LAM family                                   ║
// ║    Both → DGU+LAM hybrid                                                 ║
// ║    Single pane, no markers → SGU (monolithic)                            ║
// ║    Multiple panes, no markers → Unknown (build cannot be read)           ║
// ║                                                                          ║
// ║  Used by DataGridViewModel to auto-switch Glass Type dropdown when the   ║
// ║  user picks or types a specification whose glass type differs from the   ║
// ║  current selection.                                                      ║
// ╚══════════════════════════════════════════════════════════════════════════╝

using System;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;

namespace ProGlassAutomation.Utilities
{
    /// <summary>
    /// Pattern-based detector that infers a specification's glass type
    /// from its human-readable name. Case-insensitive.
    /// </summary>
    public static class GlassTypeDetector
    {
        /// <summary>
        /// Returns "SGU" | "DGU" | "LAM" | "DGU+LAM" | "Unknown"
        /// based on the same grammar Step 9 already trusts.
        ///
        /// Detection rules:
        ///   spacer + interlayer → DGU+LAM
        ///   spacer only         → DGU
        ///   interlayer only     → LAM
        ///   one pane, no markers → SGU
        ///   2+ panes, no markers → Unknown (build ambiguous)
        ///   empty / unparsable  → Unknown
        ///
        /// Using the shared parser matters: the older implementation only
        /// looked for "PVB", so SGP/EVA hybrids were mis-detected as DGU and
        /// the Step 5 glass-type dropdown drifted away from the actual spec.
        /// </summary>
        public static string Detect(string specName)
        {
            if (string.IsNullOrWhiteSpace(specName))
                return "Unknown";

            bool hasSpacer = PnLSpecParser.Spacer(specName).Success;
            bool hasInterlayer = PnLSpecParser.Interlayer(specName).Success;
            int paneCount = PnLSpecParser.GlassMatches(specName).Count;

            if (hasSpacer && hasInterlayer) return "DGU+LAM";
            if (hasSpacer) return "DGU";
            if (hasInterlayer) return "LAM";

            // ══════════════════════════════════════════════════════════════
            //  MULTI-PANE SPECS WITHOUT BUILD MARKERS ARE NOT SINGLE GLAZED
            //
            //  A name that lists two or more panes but no spacer (ASP) and
            //  no interlayer (PVB/SGP/EVA) is a shorthand for DGU, LAM or a
            //  hybrid — the text alone cannot pick. Returning SGU here made
            //  Step 5 auto-switch the operator's authoritative glass type to
            //  "SGU"; Step 6 advanced automation then suggested ONLY the
            //  outer pane, argon / U-Insert / polish rows vanished, and the
            //  whole automation looked dead. The Step 4 selection must be
            //  preserved, so this shape reports Unknown instead of a guess.
            // ══════════════════════════════════════════════════════════════
            if (paneCount > 1) return "Unknown";
            if (paneCount == 1) return "SGU";

            return "Unknown";
        }

        /// <summary>
        /// True when a spec name lists two or more glass panes but carries no
        /// spacer/interlayer marker: the build (DGU vs LAM vs DGU+LAM) cannot
        /// be read from the text, so the authoritative glass-type selection
        /// must be kept instead of being guessed at.
        /// </summary>
        public static bool IsMultiPaneWithoutBuildMarkers(string specName)
        {
            if (string.IsNullOrWhiteSpace(specName)) return false;

            if (PnLSpecParser.Spacer(specName).Success) return false;
            if (PnLSpecParser.Interlayer(specName).Success) return false;

            return PnLSpecParser.GlassMatches(specName).Count > 1;
        }

        /// <summary>
        /// True if the detected glass type differs from the currently
        /// selected glass type. Returns false when either value is empty
        /// or "Unknown" (nothing to compare).
        /// </summary>
        public static bool IsMismatch(string specName, string currentGlassType)
        {
            if (string.IsNullOrWhiteSpace(currentGlassType)) return false;

            var detected = Detect(specName);
            if (detected == "Unknown") return false;

            return !string.Equals(detected, currentGlassType, StringComparison.OrdinalIgnoreCase);
        }
    }
}