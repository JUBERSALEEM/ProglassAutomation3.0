// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.10.OtherChargeAutomation.cs
// MODULE: step6 — stage 4's suggestions, derived from the specification.
//
// A charge is suggested only when the SPECIFICATION SAYS SO ("U-Insert",
// "Overlap", "Sandblast", "Mitre") or when the BUILD makes it unavoidable (a
// DGU cavity has gas and a perimeter seal; polishing a unit whose panes are
// the same glass polishes both). Holes, cutouts and delivery depend on a
// drawing and a truck, not on a spec name, so they are never guessed.
//
// Pure and deterministic: same input, same suggestions. `Apply` never
// overrules a switch the operator has touched.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>One suggested charge activation, with the reason it was made.</summary>
    public sealed class ChargeSuggestion
    {
        public string TemplateKey { get; init; } = "";
        public string Reason { get; init; } = "";
    }

    /// <summary>Turns a priced specification into charge suggestions.</summary>
    public static class OtherChargeAutomation
    {
        /// <summary>
        /// Suggestions for one group. The effective glass type is the group's own
        /// code, falling back to a build named inside the specification text, so
        /// an imported inquiry row without a type is still handled.
        /// </summary>
        public static IReadOnlyList<ChargeSuggestion> Suggest(SpecGroup group)
        {
            var suggestions = new List<ChargeSuggestion>();
            if (group == null) return suggestions;

            string spec = (group.SpecName ?? "").ToUpperInvariant();
            string type = EffectiveGlassType(group.GlassType, group.SpecName);
            bool dguLike = type is GlassBuilds.Dgu or GlassBuilds.DguLam;
            bool lamLike = type is GlassBuilds.Lam or GlassBuilds.DguLam;

            // ── Unavoidable for the build ────────────────────────────────
            if (dguLike || spec.Contains("ARGON GAS"))
            {
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = "ARGON_GAS",
                    Reason = "The build is double glazed, so its cavity is filled with argon."
                });
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = "SILICON_BED",
                    Reason = "The double-glazed unit needs a secondary perimeter seal."
                });
            }

            // ── Named by the specification ───────────────────────────────
            if ((spec.Contains("SILICON BED") || spec.Contains("SILICONE BED")) && !dguLike)
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = "SILICON_BED",
                    Reason = "The specification explicitly names a silicon bed."
                });

            if (spec.Contains("U-INSERT") || spec.Contains("U INSERT") || spec.Contains("UINSERT"))
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = "U_INSERT",
                    Reason = "The specification names a U-insert."
                });

            if (spec.Contains("OVERLAP"))
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = spec.Contains("(SQM)") || spec.Contains("(M²)") || spec.Contains("(M2)")
                        ? "OVERLAP_SQM"
                        : "OVERLAP",
                    Reason = "The specification names an overlap."
                });

            if (spec.Contains("SANDBLAST"))
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = "SANDBLASTING",
                    Reason = "The specification names sandblasting."
                });

            if (spec.Contains("SHUTTER BONDING"))
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = "SHUTTER_BONDING",
                    Reason = "The specification names shutter bonding."
                });

            if (spec.Contains("FANHOLE") || spec.Contains("FAN HOLE"))
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = "FAN_HOLES",
                    Reason = "The specification names fan holes."
                });

            if (spec.Contains("CUTOUT") || spec.Contains("CUT OUT"))
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = "CUTOUT",
                    Reason = "The specification names cutouts."
                });

            if (spec.Contains("HOLES"))
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = "HOLES",
                    Reason = "The specification names drilled holes."
                });

            if (spec.Contains("MITRE") || spec.Contains("MITRING")
                || spec.Contains("BEVEL"))
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = "MITRING",
                    Reason = "The specification names mitring or beveling."
                });

            if (spec.Contains("SANDBLAST") || spec.Contains("SAND BLAST") || spec.Contains("FROSTED"))
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = "SANDBLASTING",
                    Reason = "The specification names sandblasting or frosting."
                });

            // ── Polish follows the glass count, not a word ───────────────
            bool sameGlass = SamePaneColours(group);
            bool polishNamed = spec.Contains("POLISH");
            if (doubleGlass && sameGlass)
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = "POLISH_MATCHED_LAM",
                    Reason = "Both panes are the same glass, so both edges are polished."
                });
            else if (lamLike || polishNamed || !string.IsNullOrWhiteSpace(group.OuterGlassColor))
                suggestions.Add(new ChargeSuggestion
                {
                    TemplateKey = "POLISH",
                    Reason = "A single pane with a named glass colour is polished."
                });

            // One suggestion per template, first reason wins.
            return suggestions
                .GroupBy(item => item.TemplateKey, StringComparer.OrdinalIgnoreCase)
                .Select(grouping => grouping.First())
                .ToList();
        }

        /// <summary>
        /// Applies suggestions to a worksheet that is in advanced mode. A row the
        /// operator switched by hand is left alone; a row automation already
        /// switched on keeps its rate.
        /// </summary>
        public static void Apply(OtherChargesSheet sheet, IEnumerable<SpecGroup>? groups)
        {
            if (sheet == null || !sheet.UseAdvancedAutomation) return;
            sheet.EnsureCatalogueRows();

            var suggested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SpecGroup group in groups ?? Array.Empty<SpecGroup>())
                foreach (ChargeSuggestion suggestion in Suggest(group))
                    suggested.Add(suggestion.TemplateKey);

            foreach (PredefinedChargeRow row in sheet.Predefined)
                if (!row.HasManualActiveOverride)
                    row.IsActive = suggested.Contains(row.TemplateKey);
        }

        /// <summary>
        /// The build the specification text names when the group's code is blank.
        /// DGU+LAM is tested first so the compound build is never read as DGU.
        /// </summary>
        public static string EffectiveGlassType(string? glassType, string? specName)
        {
            string direct = GlassBuilds.Normalize(glassType);
            if (direct.Length > 0) return direct;

            string text = (specName ?? "").ToUpperInvariant();
            if (text.Contains("DGU+LAM")) return GlassBuilds.DguLam;
            if (text.Contains("DGU")) return GlassBuilds.Dgu;
            if (text.Contains("LAM")) return GlassBuilds.Lam;
            if (text.Contains("SGU")) return GlassBuilds.Sgu;
            return "";
        }

        /// <summary>True when every named pane colour in the group is the same.</summary>
        public static bool SamePaneColours(SpecGroup group)
        {
            if (group == null) return false;
            var colours = new List<string>(3);
            foreach (string colour in new[]
                     { group.OuterGlassColor, group.InnerGlassColor, group.ThirdGlassColor })
            {
                string text = (colour ?? "").Trim();
                if (text.Length > 0) colours.Add(text);
            }

            if (colours.Count < 2) return false;
            return colours.All(colour =>
                string.Equals(colour, colours[0], StringComparison.OrdinalIgnoreCase));
        }
    }
}
