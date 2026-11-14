// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/OptimizationInstrumentation.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — PHASE E1, measurement
//
// WHY THIS FILE EXISTS
//   Two questions the engine could never answer about its own output:
//
//   1. Is this plan actually cuttable? RunMaxRects is documented as
//      NON-guillotine and runs at the default effort level, but no oracle
//      checks cut reachability. When cut derivation fails the sheet silently
//      keeps an empty cut list and the plan ships. This file makes that
//      visible (finding F1).
//
//   2. How far from optimal is this plan? "16 sheets" is an unqualified
//      number. A lower bound turns it into "16 sheets when at least 15 are
//      provably required", which is the difference between a finished engine
//      and one with headroom nobody can see (finding F2).
//
// WHAT IT DELIBERATELY DOES NOT DO
//   It observes. It does not reject candidates, does not change placements,
//   does not reorder anything, and is not consulted by Better(). It runs once,
//   at the end of a finished plan, and writes numbers and a note. That is why
//   this phase cannot move a benchmark result: nothing downstream reads these
//   properties to make a decision.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProGlassAutomation.Services.Optimization
{
    public static class OptimizationInstrumentation
    {
        /// <summary>
        /// Measures a finished plan. Safe to call anywhere a plan exists.
        /// </summary>
        public static void Instrument(OptimizationResult result,
            IReadOnlyList<StockSize> stocks, MarginProfile margins, RotationPolicy rotation)
        {
            if (result == null) return;

            AuditCutReachability(result);

            // Repair BEFORE the audit, so the audit reports the plan as it is
            // actually exported. Running it the other way round would count a
            // sheet as inconsistent and then quietly fix it, leaving a warning
            // describing a problem the operator can no longer see.
            //
            // A cut list recorded during placement can be stale by the time
            // the plan settles, because later passes move panes. Re-deriving
            // it from the final positions costs nothing and moves no pane.
            GuillotinePacker.RepairStaleCutLists(result, margins?.KerfMm ?? 0);

            GuillotinePacker.AuditCutConsistency(result, margins?.KerfMm ?? 0);
            MeasureOptimality(result, stocks, margins, rotation);
        }

        // ── E1.1 cut reachability ───────────────────────────────────────

        /// <summary>
        /// Counts sheets that hold panes but carry no cut sequence. At the
        /// point this runs, every such sheet has already had
        /// <see cref="GuillotinePacker.TryDeriveGuillotineCuts"/> attempted on
        /// it and it failed — so an empty cut list here means "no sequence of
        /// straight cuts produces this layout", not "nobody has looked yet".
        /// </summary>
        internal static int AuditCutReachability(OptimizationResult result)
        {
            int uncuttable = 0;

            for (int i = 0; i < result.Sheets.Count; i++)
            {
                var sheet = result.Sheets[i];
                if (sheet.Pieces.Count == 0) continue;   // nothing to cut
                if (sheet.Cuts.Count > 0) continue;      // sequence known
                uncuttable++;
            }

            result.UncuttableSheetCount = uncuttable;

            if (uncuttable > 0)
                result.ScaleNotes.Add(string.Format(CultureInfo.InvariantCulture,
                    "CUT REACHABILITY: {0} sheet(s) hold panes but no guillotine cut sequence "
                    + "could be derived for them. This layout cannot be produced on a guillotine "
                    + "table. Most likely source: a non-guillotine generator (MaxRects) won the "
                    + "candidate comparison.", uncuttable));

            return uncuttable;
        }

        // ── E1.2 optimality bound ────────────────────────────────────────

        /// <summary>
        /// A lower bound on the sheets this job needs: the area of every pane
        /// that can physically be cut, divided by the usable area of the
        /// largest stock plate available.
        ///
        /// It is deliberately WEAK, because a bound that is too high is worse
        /// than none — it would claim headroom that does not exist. It ignores
        /// kerf, guillotine structure and the fact that one pane cannot span
        /// two plates, all of which can only push the true requirement UP. So
        /// the real optimum is at least this number. Panes too large for any
        /// stock are excluded for the same reason: they need no plate.
        /// </summary>
        internal static int MeasureOptimality(OptimizationResult result,
            IReadOnlyList<StockSize> stocks, MarginProfile margins, RotationPolicy rotation)
        {
            result.SheetsLowerBound = 0;
            if (margins == null || stocks == null) return 0;

            // Largest usable plate, and its dimensions (needed to test whether
            // an unplaced pane could ever fit on one).
            double bestArea = 0, bestW = 0, bestH = 0;
            for (int i = 0; i < stocks.Count; i++)
            {
                var stock = stocks[i];
                if (stock == null) continue;
                double uw = stock.WidthMm - margins.LeftMm - margins.RightMm;
                double uh = stock.HeightMm - margins.TopMm - margins.BottomMm;
                if (uw <= 0 || uh <= 0) continue;
                // plateArea, not area: the method scope below declares 'area'
                // for the total pane area, and C# forbids a nested scope from
                // re-using a name the enclosing scope declares - even when the
                // enclosing declaration comes later in the file (CS0136).
                double plateArea = uw * uh;
                if (plateArea <= bestArea) continue;
                bestArea = plateArea;
                bestW = uw;
                bestH = uh;
            }

            if (bestArea <= 0) return 0;

            // Passed in rather than read from result.Rotation: Locked is the
            // enum's zero value, so an unset policy would silently read as
            // no-rotation and tighten the fits test — which is the one
            // direction that can make a bound claim headroom it does not have.
            bool rotate = rotation != RotationPolicy.Locked;
            double area = 0;

            for (int i = 0; i < result.Sheets.Count; i++)
            {
                var pieces = result.Sheets[i].Pieces;
                for (int p = 0; p < pieces.Count; p++)
                    area += (double)pieces[p].WidthMm * pieces[p].HeightMm;
            }

            // Unplaced panes count toward what MUST be cut, but only if they
            // could be cut at all. Counting one that fits nowhere would
            // inflate the bound and invent headroom.
            for (int i = 0; i < result.Unplaced.Count; i++)
            {
                var piece = result.Unplaced[i];
                bool fits = (piece.WidthMm <= bestW && piece.HeightMm <= bestH)
                         || (rotate && piece.HeightMm <= bestW && piece.WidthMm <= bestH);
                if (fits) area += (double)piece.WidthMm * piece.HeightMm;
            }

            int bound = (int)Math.Ceiling(area / bestArea);
            result.SheetsLowerBound = bound;

            if (bound > 0 && result.SheetsUsed > 0)
            {
                int gap = result.OptimalityGapSheets;
                result.ScaleNotes.Add(gap <= 0
                    ? string.Format(CultureInfo.InvariantCulture,
                        "OPTIMALITY: {0} sheet(s) used against an area bound of {1} - this plan "
                        + "meets the bound, so no plan can use fewer plates for this glass.",
                        result.SheetsUsed, bound)
                    : string.Format(CultureInfo.InvariantCulture,
                        "OPTIMALITY: {0} sheet(s) used against an area bound of {1} - at most {2} "
                        + "sheet(s) of headroom. The bound ignores kerf and guillotine structure, "
                        + "so the true minimum is at least {1}.",
                        result.SheetsUsed, bound, gap));
            }

            return bound;
        }
    }
}
