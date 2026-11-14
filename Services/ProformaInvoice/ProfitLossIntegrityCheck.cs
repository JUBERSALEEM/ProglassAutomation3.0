// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/ProfitLossIntegrityCheck.cs
// PROJECT: ProGlassAutomation
//
// DETECTS PROFIT FIGURES PRODUCED BY THE PRE-FIX DGU COSTING.
//
// ─────────────────────────────────────────────────────────────────────
//  THE PROBLEM THIS EXISTS TO SOLVE
// ─────────────────────────────────────────────────────────────────────
//  Step 9's glass-pane regex used a lazy wildcard for the colour, which
//  crossed the '+' separating layers. On
//
//      "6mm HD Grey FT Glass + 12mm ASP + 6mm Clear FT Glass"
//
//  the second match began inside the SPACER and swallowed the inner pane,
//  so every double-glazed unit was costed on its outer pane alone. The
//  regex is fixed, but the P&L snapshot is written at SAVE time — so
//  every invoice saved before the fix still carries an understated cost
//  and an overstated profit.
//
//  Silently rewriting stored financial history would be worse than the
//  bug. Instead this identifies the affected records so they can be
//  recalculated deliberately, by a person.
//
// ─────────────────────────────────────────────────────────────────────
//  THE DETECTION SIGNATURE
// ─────────────────────────────────────────────────────────────────────
//  A snapshot is suspect when a spec group is clearly a DGU (its spec
//  name contains an air spacer) yet contributed only ONE glass row.
//  Correctly costed DGUs always produce two — outer and inner.
//
//  This is a HEURISTIC over saved text, not a proof. It is deliberately
//  biased toward false negatives: it only flags what it can justify from
//  the stored rows, because telling someone their books are wrong when
//  they are not is its own kind of damage. See ScanSnapshot for the cases
//  it will not flag.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    /// <summary>Outcome of checking one saved invoice's P&amp;L snapshot.</summary>
    public sealed class ProfitLossIntegrityResult
    {
        /// <summary>True when this invoice looks like it was costed pre-fix.</summary>
        public bool NeedsRecalculation { get; init; }

        /// <summary>Plain-language reason, empty when nothing is wrong.</summary>
        public string Reason { get; init; } = "";

        /// <summary>Spec names that appear to be missing their inner pane.</summary>
        public IReadOnlyList<string> AffectedSpecs { get; init; } =
            Array.Empty<string>();

        public static ProfitLossIntegrityResult Clean() => new();

        public static ProfitLossIntegrityResult Suspect(
            string reason, IReadOnlyList<string> specs) =>
            new()
            {
                NeedsRecalculation = true,
                Reason = reason,
                AffectedSpecs = specs
            };
    }

    /// <summary>Read-only inspector. Changes nothing, ever.</summary>
    public static class ProfitLossIntegrityCheck
    {
        // A middle layer marks a pane boundary. These two patterns replaced
        // a single combined ASP|PVB one once the DGU+LAM hybrid arrived:
        // that build shows BOTH markers and needs them counted separately
        // to arrive at three panes rather than two.
        //
        // Both keep the bounded lazy colour class [^+\d]*? — an unbounded
        // wildcard walked straight across the '+' separators and matched
        // layers it had no business matching.

        /// <summary>
        /// An air spacer on its own — the DGU half of a build.
        /// </summary>
        private static readonly Regex SpacerMarker = new(
            @"\+\s*[\d\.]+\s*mm\s+(?:[^+\d]*?\s+)?ASP\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// A laminating interlayer — the LAM half of a build. SGP and EVA
        /// are listed alongside PVB because all three are sold as
        /// interlayers and a spec naming any of them has a bonded pane
        /// behind it.
        /// </summary>
        private static readonly Regex InterlayerMarker = new(
            @"\+\s*[\d\.]+\s*mm\s+(?:[^+\d]*?\s+)?(?:PVB|SGP|EVA)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// How many panes of glass a spec name says the unit contains.
        ///
        /// A spec carrying BOTH a spacer and an interlayer is the DGU+LAM
        /// hybrid: outer + ASP + lam-outer + interlayer + lam-inner, which
        /// is THREE panes. Counting it as two would let a hybrid that lost
        /// its third pane pass as correctly costed — the very failure this
        /// check exists to catch, one pane deeper.
        ///
        /// Returns 1 for anything unrecognised, so an unfamiliar spec is
        /// never accused.
        /// </summary>
        private static int ExpectedPaneCount(string specName)
        {
            bool hasSpacer = SpacerMarker.IsMatch(specName);
            bool hasInterlayer = InterlayerMarker.IsMatch(specName);

            if (hasSpacer && hasInterlayer) return 3;
            if (hasSpacer || hasInterlayer) return 2;

            return 1;
        }

        /// <summary>
        /// Inspects one saved snapshot. Never throws — a check that can
        /// crash the screen it is warning on is worse than no check.
        /// </summary>
        public static ProfitLossIntegrityResult ScanSnapshot(
            ProfitLossSnapshot? snapshot)
        {
            try
            {
                if (snapshot == null) return ProfitLossIntegrityResult.Clean();

                var rows = snapshot.Rows;
                if (rows == null || rows.Count == 0)
                {
                    // No rows at all is "never costed", which the UI already
                    // reports as "not costed". Not this check's business.
                    return ProfitLossIntegrityResult.Clean();
                }

                // Group glass rows by the spec they were costed against.
                var glassBySpec = new Dictionary<string, int>(
                    StringComparer.OrdinalIgnoreCase);

                foreach (var row in rows)
                {
                    if (row == null) continue;

                    if (!string.Equals(row.Kind, "Glass",
                            StringComparison.OrdinalIgnoreCase)) continue;

                    string spec = (row.SpecName ?? "").Trim();

                    // A row with no spec name predates SpecName being stored.
                    // Cannot be judged, so it is not accused.
                    if (spec.Length == 0) continue;

                    glassBySpec.TryGetValue(spec, out int n);
                    glassBySpec[spec] = n + 1;
                }

                // Flag a spec only when it holds FEWER glass rows than its
                // own name says it has panes. Extra rows are never accused:
                // an unexpected surplus is not the understating bug this
                // check hunts, and guessing at it would produce noise.
                var affected = glassBySpec
                    .Where(kv => kv.Value < ExpectedPaneCount(kv.Key))
                    .Select(kv => kv.Key)
                    .ToList();

                if (affected.Count == 0)
                    return ProfitLossIntegrityResult.Clean();

                return ProfitLossIntegrityResult.Suspect(
                    $"{affected.Count} multi-pane spec(s) — double-glazed, " +
                    "laminated, or both — were costed with fewer glass panes " +
                    "than the specification lists. Missing panes mean the " +
                    "cost is understated and the profit overstated. Open this " +
                    "invoice, recalculate Step 9, and save.",
                    affected);
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[ProfitLossIntegrityCheck] {ex.Message}");

                // Fail quiet: an inspector that cannot decide must not
                // accuse. A false "your books are wrong" has its own cost.
                return ProfitLossIntegrityResult.Clean();
            }
        }
    }
}
