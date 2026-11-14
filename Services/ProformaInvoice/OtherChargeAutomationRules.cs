using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    /// <summary>
    /// One predefined-charge activation inferred from the Step 4 / Step 5
    /// specification. Pure business mapping only — no WPF, no repositories.
    /// </summary>
    public sealed class AutoChargeSuggestion
    {
        public string TemplateKey { get; init; } = "";
        public string MetricKey { get; init; } = "";
        public PolishSubType? PolishSubType { get; init; }
        public string Reason { get; init; } = "";
    }

    /// <summary>
    /// One auto-wastage target: a Step 5 spec-group side whose SQM should be
    /// preselected into Step 6's wastage grid in Advanced mode.
    /// </summary>
    public sealed class AutoWastageSuggestion
    {
        public int SpecGroupNumber { get; init; }
        public string SourceSideLabel { get; init; } = "";
        public string DisplayName { get; init; } = "";

        /// <summary>
        /// The resolved glass colour carried as STRUCTURED data. The wastage
        /// section used to scrape it back out of the human DisplayName
        /// ("HD Grey — outer glass wastage"), which produced garbage row
        /// names like "Outer glass wastage" whenever a colour could not be
        /// resolved. Empty when no colour identity is known.
        /// </summary>
        public string ColorName { get; init; } = "";

        public string Reason { get; init; } = "";
    }

    /// <summary>
    /// Abstraction for spec parsing operations. Allows OtherChargeAutomationService
    /// to be tested with mock parsers instead of depending on the static PnLSpecParser.
    /// </summary>
    public interface ISpecParser
    {
        IReadOnlyList<Match> GlassMatches(string spec);
        Match Spacer(string spec);
        Match Interlayer(string spec);
    }

    /// <summary>
    /// Default implementation that delegates to the static PnLSpecParser.
    /// Used in production code.
    /// </summary>
    public sealed class PnLSpecParserAdapter : ISpecParser
    {
        public IReadOnlyList<Match> GlassMatches(string spec) => PnLSpecParser.GlassMatches(spec);
        public Match Spacer(string spec) => PnLSpecParser.Spacer(spec);
        public Match Interlayer(string spec) => PnLSpecParser.Interlayer(spec);
    }

    /// <summary>
    /// Testable service for Advanced Step 6 automation. Reads a spec name
    /// and glass type and decides which predefined Step 6 rows should turn on.
    /// 
    /// This is the instance-based version. For backward compatibility, the
    /// static OtherChargeAutomationRules class delegates to a singleton instance.
    /// </summary>
    public sealed class OtherChargeAutomationService
    {
        private readonly ISpecParser _specParser;

        public OtherChargeAutomationService(ISpecParser specParser)
        {
            _specParser = specParser ?? throw new ArgumentNullException(nameof(specParser));
        }

        public IReadOnlyList<AutoChargeSuggestion> GetChargeSuggestions(
            string? glassType,
            string? specName,
            bool matchedSameGlass)
        {
            string spec = specName ?? "";
            string type = ResolveEffectiveGlassType(glassType, spec);
            bool doubleGlass = IsDoubleGlass(type);
            bool dguLike = type is "DGU" or "DGU+LAM";

            var suggestions = new Dictionary<string, AutoChargeSuggestion>(
                StringComparer.OrdinalIgnoreCase);

            void Add(string templateKey, string metricKey, string reason,
                PolishSubType? polish = null)
            {
                suggestions[templateKey] = new AutoChargeSuggestion
                {
                    TemplateKey = templateKey,
                    MetricKey = metricKey,
                    Reason = reason,
                    PolishSubType = polish
                };
            }

            if (Contains(spec, "U-Insert"))
                Add("U_INSERT", type is "DGU" or "DGU+LAM" ? "DGU_CAVITY" : "SQM1",
                    "Spec requires U-Insert.");

            if (Contains(spec, "Overlap"))
            {
                bool specialOverlapSqm = Contains(spec, "Overlap (SQM)");

                if (specialOverlapSqm)
                {
                    // Overlap (SQM) mode: rare case for specific customers.
                    // Only charge overlap (sqm), do NOT charge silicon bed.
                    // The SQM pricing already includes all overlap-related costs.
                    Add("OVERLAP_SQM", "SQM1", "Spec requires overlap charged by area (SQM).");
                }
                else
                {
                    // Overlap (LM) mode: standard case.
                    // For DGU builds, overlap ALWAYS requires silicon bed as
                    // a separate charge. The spec just says "With Overlap" but
                    // physically overlap + silicon bed are two distinct processes
                    // that are always charged separately.
                    Add("OVERLAP", "LM1", "Spec requires overlap charged by perimeter (LM).");

                    if (dguLike)
                    {
                        Add("SILICON_BED", "LM1",
                            "DGU overlap requires silicon bed as a separate charge (LM).");
                    }
                }
            }

            if (Contains(spec, "Argon Gas"))
                Add("ARGON_GAS", "DGU_CAVITY", "Spec requires argon gas.");

            if (Contains(spec, "Shutter Bonding"))
                Add("SHUTTER_BONDING", "LM_COMBINED", "Spec requires shutter bonding.");

            if (Contains(spec, "Fanhole"))
                Add("FAN_HOLES", "QTY", "Spec requires fan holes.");

            if (ContainsAny(spec,
                    "Cutout And Holes",
                    "Cutouts And Holes",
                    "Holes And Cutout",
                    "Holes And Cutouts"))
            {
                Add("CUTOUT", "QTY", "Spec requires cutouts.");
                Add("HOLES", "QTY", "Spec requires holes.");
            }
            else
            {
                if (ContainsAny(spec, "Cutout", "Cutouts"))
                    Add("CUTOUT", "QTY", "Spec requires cutouts.");
                if (Contains(spec, "Holes"))
                    Add("HOLES", "QTY", "Spec requires holes.");
            }

            if (Contains(spec, "Mitring") || Contains(spec, "Beveled"))
                Add("MITRING", "LM1", "Spec requires mitring/beveling.");

            if (Contains(spec, "Sandblasted") || Contains(spec, "Frosted"))
                Add("SANDBLASTING", "SQM1", "Spec requires sandblasting/frosting.");

            var polishType = DetectPolish(spec);
            if (polishType.HasValue)
            {
                string template = matchedSameGlass && doubleGlass
                    ? "POLISH_MATCHED_LAM"
                    : "POLISH";
                Add(template,
                    doubleGlass ? "LM_COMBINED" : "LM1",
                    matchedSameGlass && doubleGlass
                        ? "Matched multi-pane build uses the 2× polish row."
                        : "Spec requires polish.",
                    polishType.Value);
            }

            return suggestions.Values.ToList();
        }

        public IReadOnlyList<AutoWastageSuggestion> GetWastageSuggestions(
            int specGroupNumber,
            string? glassType,
            string? specName,
            string? outerColorFallback,
            string? innerColorFallback)
        {
            string spec = specName ?? "";
            string type = ResolveEffectiveGlassType(glassType, spec);
            bool doubleGlass = IsDoubleGlass(type);

            var glassMatches = _specParser.GlassMatches(spec);
            string outerColor = glassMatches.Count > 0
                ? (glassMatches[0].Groups["color"].Value ?? "").Trim()
                : (outerColorFallback ?? "").Trim();
            string innerColor = glassMatches.Count > 1
                ? (glassMatches[^1].Groups["color"].Value ?? "").Trim()
                : (innerColorFallback ?? "").Trim();

            var rows = new List<AutoWastageSuggestion>();

            if (type == "DGU+LAM")
            {
                string lamOuterColor = glassMatches.Count > 1
                    ? (glassMatches[1].Groups["color"].Value ?? "").Trim()
                    : (outerColorFallback ?? "").Trim();
                string lamInnerColor = glassMatches.Count > 2
                    ? (glassMatches[2].Groups["color"].Value ?? "").Trim()
                    : (innerColorFallback ?? "").Trim();

                if (string.IsNullOrWhiteSpace(outerColor)
                    && string.IsNullOrWhiteSpace(lamOuterColor)
                    && string.IsNullOrWhiteSpace(lamInnerColor)
                    && glassMatches.Count == 0)
                    return rows;

                // All panes are suggested for wastage, but clear glass is
                // marked as optional (offcuts often reusable across orders).
                // User can uncheck clear glass wastage if offcuts are reusable.
                if (!string.IsNullOrWhiteSpace(outerColor) || glassMatches.Count > 0)
                {
                    rows.Add(new AutoWastageSuggestion
                    {
                        SpecGroupNumber = specGroupNumber,
                        SourceSideLabel = "Outer (W1/H1)",
                        ColorName = outerColor,
                        DisplayName = string.IsNullOrWhiteSpace(outerColor)
                            ? "Outer glass wastage"
                            : $"{outerColor} — outer glass wastage",
                        Reason = IsClearGlass(outerColor)
                            ? "Clear glass — optional (offcuts often reusable)."
                            : "Colored/coated glass — offcuts not reusable."
                    });
                }

                if (!string.IsNullOrWhiteSpace(lamOuterColor) || glassMatches.Count > 1)
                {
                    rows.Add(new AutoWastageSuggestion
                    {
                        SpecGroupNumber = specGroupNumber,
                        SourceSideLabel = "Lam Outer (W2/H2)",
                        ColorName = lamOuterColor,
                        DisplayName = string.IsNullOrWhiteSpace(lamOuterColor)
                            ? "Lam outer glass wastage"
                            : $"{lamOuterColor} — lam outer glass wastage",
                        Reason = IsClearGlass(lamOuterColor)
                            ? "Clear glass — optional (offcuts often reusable)."
                            : "Colored/coated glass — offcuts not reusable."
                    });
                }

                if (!string.IsNullOrWhiteSpace(lamInnerColor) || glassMatches.Count > 2)
                {
                    rows.Add(new AutoWastageSuggestion
                    {
                        SpecGroupNumber = specGroupNumber,
                        SourceSideLabel = "Lam Inner (W2/H2)",
                        ColorName = lamInnerColor,
                        DisplayName = string.IsNullOrWhiteSpace(lamInnerColor)
                            ? "Lam inner glass wastage"
                            : $"{lamInnerColor} — lam inner glass wastage",
                        Reason = IsClearGlass(lamInnerColor)
                            ? "Clear glass — optional (offcuts often reusable)."
                            : "Colored/coated glass — offcuts not reusable."
                    });
                }

                return rows;
            }

            if (string.IsNullOrWhiteSpace(specName)
                && string.IsNullOrWhiteSpace(outerColor)
                && string.IsNullOrWhiteSpace(innerColor)
                && glassMatches.Count == 0)
                return rows;

            // All panes are suggested for wastage, but clear glass is
            // marked as optional (offcuts often reusable across orders).
            rows.Add(new AutoWastageSuggestion
            {
                SpecGroupNumber = specGroupNumber,
                SourceSideLabel = "Outer (W1/H1)",
                ColorName = outerColor,
                DisplayName = string.IsNullOrWhiteSpace(outerColor)
                    ? "Outer glass wastage"
                    : $"{outerColor} — outer glass wastage",
                Reason = IsClearGlass(outerColor)
                    ? "Clear glass — optional (offcuts often reusable)."
                    : "Colored/coated glass — offcuts not reusable."
            });

            // FIX-2026-09-05 (3+ pane builds): a triple-glazed DGU has
            // TWO spacers and a middle pane the old rule never read —
            // its wastage charge silently didn't exist (the operator's
            // deadline meter counted a sheet that wasn't chargeded).
            // Every pane between first and last is a wastage side now.
            if (doubleGlass && glassMatches.Count > 2)
            {
                for (int m = 1; m < glassMatches.Count - 1; m++)
                {
                    string middleColor =
                        (glassMatches[m].Groups["color"].Value ?? "").Trim();
                    string middleSide = glassMatches.Count == 3
                        ? "Middle (W2/H2)"
                        : $"Middle {m} (W2/H2)";

                    rows.Add(new AutoWastageSuggestion
                    {
                        SpecGroupNumber = specGroupNumber,
                        SourceSideLabel = middleSide,
                        ColorName = middleColor,
                        DisplayName = string.IsNullOrWhiteSpace(middleColor)
                            ? $"{middleSide.Replace(" (W2/H2)", "")} glass wastage"
                            : $"{middleColor} — {middleSide.Replace(" (W2/H2)", "").ToLowerInvariant()} glass wastage",
                        Reason = IsClearGlass(middleColor)
                            ? "Clear glass — optional (offcuts often reusable)."
                            : "Colored/coated glass — offcuts not reusable."
                    });
                }
            }

            if (doubleGlass)
            {
                rows.Add(new AutoWastageSuggestion
                {
                    SpecGroupNumber = specGroupNumber,
                    SourceSideLabel = "Inner (W2/H2)",
                    ColorName = innerColor,
                    DisplayName = string.IsNullOrWhiteSpace(innerColor)
                        ? "Inner glass wastage"
                        : $"{innerColor} — inner glass wastage",
                    Reason = IsClearGlass(innerColor)
                        ? "Clear glass — optional (offcuts often reusable)."
                        : "Colored/coated glass — offcuts not reusable."
                });
            }

            return rows;
        }

        /// <summary>
        /// Checks if a glass color is "Clear" (the most common glass type).
        /// Clear glass offcuts are often reusable across orders, so wastage
        /// charges for clear glass are optional (user can uncheck if offcuts
        /// are reusable in their situation).
        /// </summary>
        private static bool IsClearGlass(string? colorName)
        {
            if (string.IsNullOrWhiteSpace(colorName))
                return false;

            return colorName.Trim().Equals("Clear", StringComparison.OrdinalIgnoreCase);
        }

        public PolishSubType? DetectPolish(string? specName)
        {
            string spec = specName ?? "";

            if (Contains(spec, "All Side Polish As Per DWG")) return PolishSubType.AllSidePolishAsPerDwg;
            if (Contains(spec, "All Side Polish")) return PolishSubType.AllSidePolish;
            if (Contains(spec, "Polished Edges")) return PolishSubType.AllSidePolish;

            if (Contains(spec, "Both Height Side Polish As Per DWG")) return PolishSubType.BothHeightSideAsPerDwg;
            if (Contains(spec, "Both Height Side Polish")) return PolishSubType.BothHeightSide;

            if (Contains(spec, "Both Width Side Polish As Per DWG")) return PolishSubType.BothWidthSideAsPerDwg;
            if (Contains(spec, "Both Width Side Polish")) return PolishSubType.BothWidthSide;

            if (Contains(spec, "Both Height + One Width Side Polish As Per DWG")) return PolishSubType.BothHeightOneWidthAsPerDwg;
            if (Contains(spec, "Both Height + One Width Side Polish")) return PolishSubType.BothHeightOneWidth;

            if (Contains(spec, "One Height + One Width Side Polish As Per DWG")) return PolishSubType.OneHeightOneWidthAsPerDwg;
            if (Contains(spec, "One Height + One Width Side Polish")) return PolishSubType.OneHeightOneWidth;

            if (Contains(spec, "One Height Side Polish")) return PolishSubType.OneHeightSide;
            if (Contains(spec, "One Width Side Polish")) return PolishSubType.OneWidthSide;

            return null;
        }

        private static bool IsDoubleGlass(string glassType) =>
            glassType is "DGU" or "LAM" or "DGU+LAM";

        /// <summary>
        /// The effective build of a specification. Spec-name markers win
        /// (spacer → DGU, interlayer → LAM, both → DGU+LAM); when the spec
        /// text is ambiguous the Step 4 glass-type field is authoritative;
        /// "SGU" is the final fallback.
        /// </summary>
        public string ResolveEffectiveGlassType(string? glassType, string? specName)
        {
            string spec = specName ?? string.Empty;
            bool hasSpacer = _specParser.Spacer(spec).Success;
            bool hasInterlayer = _specParser.Interlayer(spec).Success;
            bool hasGlass = _specParser.GlassMatches(spec).Count > 0;

            if (hasSpacer && hasInterlayer) return "DGU+LAM";
            if (hasSpacer) return "DGU";
            if (hasInterlayer) return "LAM";

            // ══════════════════════════════════════════════════════════════
            //  TYPE FIELD IS AUTHORITATIVE WHEN THE SPEC NAME IS AMBIGUOUS
            //
            //  The old rule returned "SGU" whenever the spec name contained
            //  a glass pane but no spacer/interlayer token. A DGU whose
            //  generated name variant does not carry the "+ 12mm ASP" token
            //  was then classified SGU: Step 6 auto-wastage suggested the
            //  OUTER side only (the inner checkbox stayed unchecked), argon /
            //  U-Insert / polish suggestions were lost, and the whole
            //  Advanced automation looked like it "was not triggering".
            //
            //  The Step 4 panel is the authoritative source of the build, so
            //  when the spec text cannot decide, the selected glass type
            //  decides. "SGU" remains the fallback when the type is blank
            //  AND no build marker exists in the spec.
            // ══════════════════════════════════════════════════════════════
            string type = NormalizeGlassType(glassType);
            if (type.Length > 0) return type;
            if (hasGlass) return "SGU";
            return type;
        }

        private static string NormalizeGlassType(string? glassType) =>
            (glassType ?? "").Trim().ToUpperInvariant();

        private static bool Contains(string specName, string token) =>
            specName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool ContainsAny(string specName, params string[] tokens) =>
            tokens.Any(t => Contains(specName, t));
    }

    /// <summary>
    /// Static wrapper for backward compatibility. Delegates to a singleton
    /// instance of OtherChargeAutomationService with the default PnLSpecParserAdapter.
    /// 
    /// New code should prefer injecting OtherChargeAutomationService directly
    /// for better testability.
    /// </summary>
    public static class OtherChargeAutomationRules
    {
        private static readonly OtherChargeAutomationService _instance =
            new OtherChargeAutomationService(new PnLSpecParserAdapter());

        public static IReadOnlyList<AutoChargeSuggestion> GetChargeSuggestions(
            string? glassType,
            string? specName,
            bool matchedSameGlass) =>
            _instance.GetChargeSuggestions(glassType, specName, matchedSameGlass);

        public static IReadOnlyList<AutoWastageSuggestion> GetWastageSuggestions(
            int specGroupNumber,
            string? glassType,
            string? specName,
            string? outerColorFallback,
            string? innerColorFallback) =>
            _instance.GetWastageSuggestions(specGroupNumber, glassType, specName, outerColorFallback, innerColorFallback);

        public static PolishSubType? DetectPolish(string? specName) =>
            _instance.DetectPolish(specName);

        public static string ResolveEffectiveGlassType(string? glassType, string? specName) =>
            _instance.ResolveEffectiveGlassType(glassType, specName);
    }
}
