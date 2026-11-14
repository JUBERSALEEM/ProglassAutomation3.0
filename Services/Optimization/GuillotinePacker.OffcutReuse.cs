// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/GuillotinePacker.OffcutReuse.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — offcut reuse, within one run (PHASE 2, part 2)
//
// WHY THIS FILE
//   Part 1 made the plan say where its offcuts are. This is the half that
//   uses them: if plate 4 leaves a 600 x 400 rectangle and plate 7 is half
//   empty, the panes on plate 7 move into the rectangles plates 1 to 6
//   already left, and plate 7 is never cut. That is the yield lever the
//   analysis named — LiSEC quotes roughly 3 % at a real customer for remnant
//   reuse, far more than any pattern name is worth.
//
// WHY IT IS A POST-PASS AND NOT A PLACEMENT RULE
//   Placing into offcuts during packing means every placement site, on every
//   engine, has to know about a second stock pool, and a bug there loses
//   panes. As a post-pass it runs once, on a finished plan, against
//   rectangles that are provably free, and it can be thrown away entirely
//   if anything about the result is not verifiable. The rectangle a pane
//   moves into was produced by the plan's OWN cuts, so it is reachable by
//   guillotine cuts by construction.
//
// THE SAFETY RULES, IN ORDER
//   1. A pane moves only into a rectangle no pane occupies.
//   2. Every sheet that receives a pane must re-derive a full guillotine cut
//      sequence. If it cannot, the whole attempt is rolled back — a plan
//      nobody can cut is worse than a plan that uses one more sheet.
//   3. A sheet is only dropped when it is completely empty. A half-emptied
//      sheet is not an improvement, so it is rolled back too.
//   4. The plan is re-verified after every move — geometry, kerf, breakout
//      and conservation — because the run records its verdict BEFORE this
//      pass runs. The plan it returns is one that has been checked.
//   5. Off by default, so the default run is untouched.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        /// <summary>
        /// Says what offcut reuse did, in the plan's own words. A silent
        /// improvement is indistinguishable from a bug.
        /// </summary>
        private static void NoteOffcutReuse(OptimizationResult result, int sheetsSaved)
        {
            if (sheetsSaved <= 0)
            {
                result.ScaleNotes.Add(
                    "Offcut reuse: no leftover rectangle could take a whole sheet's panes, "
                    + "so the plan is unchanged.");
                return;
            }

            result.ScaleNotes.Add(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Offcut reuse: {0} sheet(s) emptied into the offcuts the other plates left. "
                + "Every pane was moved into a rectangle the plan's own cuts had already freed.",
                sheetsSaved));
        }

        /// <summary>
        /// Moves panes off the emptiest sheet into the offcuts the other
        /// sheets already hold, and drops any sheet that ends up empty.
        /// </summary>
        /// <returns>The number of sheets eliminated. 0 means the plan was
        /// left exactly as it was.</returns>
        internal static int ConsolidateIntoOffcuts(OptimizationResult result,
            IReadOnlyList<CutPiece> pieces, MarginProfile margins, RotationPolicy rotation)
        {
            if (result == null || margins == null || result.Sheets.Count < 2)
                return 0;

            bool allowRotate = rotation != RotationPolicy.Locked;

            // Every leftover, not just the racking-worthy ones: the only
            // question asked of them here is whether a pane fits.
            DeriveLeftovers(result, 1, margins.KerfMm);

            int removed = 0;
            while (TryEmptyOneSheet(result, pieces, margins, allowRotate))
            {
                removed++;
                if (result.Sheets.Count < 2) break;
            }

            RenumberSheets(result);

            // Unconditional, for the same reason as the local search: an
            // attempt that was rolled back leaves rectangles that no longer
            // exist, and both the report and the DXF export draw them.
            DeriveLeftovers(result, Math.Max(margins.MinBreakoutMm, 1), margins.KerfMm);

            return removed;
        }

        private static bool TryEmptyOneSheet(OptimizationResult result, IReadOnlyList<CutPiece> pieces,
            MarginProfile margins, bool allowRotate)
        {
            SheetLayout? donor = null;
            double smallest = double.MaxValue;
            for (int i = 0; i < result.Sheets.Count; i++)
            {
                double area = 0;
                for (int p = 0; p < result.Sheets[i].Pieces.Count; p++)
                    area += (double)result.Sheets[i].Pieces[p].WidthMm * result.Sheets[i].Pieces[p].HeightMm;
                if (area < smallest)
                {
                    smallest = area;
                    donor = result.Sheets[i];
                }
            }

            if (donor == null || donor.Pieces.Count == 0) return false;

            var snapshot = CapturePlan(result);
            var touched = new List<SheetLayout>();

            // Biggest first: the hard ones get the pick of the rectangles.
            var moving = new List<PlacedPiece>(donor.Pieces);
            moving.Sort((a, b) => ((double)b.WidthMm * b.HeightMm).CompareTo((double)a.WidthMm * a.HeightMm));

            for (int i = 0; i < moving.Count; i++)
            {
                bool placed = false;
                for (int s = 0; s < result.Sheets.Count && !placed; s++)
                {
                    var host = result.Sheets[s];
                    if (ReferenceEquals(host, donor)) continue;
                    if (TryFitIntoLeftover(host, moving[i], allowRotate))
                    {
                        if (!touched.Contains(host)) touched.Add(host);
                        placed = true;
                    }
                }

                if (placed) continue;

                // Cannot empty this sheet, so nothing moves. A sheet with
                // panes left on it still has to be cut.
                RestorePlan(result, snapshot);
                return false;
            }

            // Rule 2: the plan has to stay cuttable.
            for (int i = 0; i < touched.Count; i++)
            {
                if (TryDeriveGuillotineCuts(touched[i])) continue;
                RestorePlan(result, snapshot);
                return false;
            }

            donor.Pieces.Clear();
            donor.Cuts.Clear();
            result.Sheets.Remove(donor);

            // Rule 5: the plan is checked AGAIN, after the move. The run
            // records its verdict before this pass runs, so an unverified
            // improvement would ship with a clean bill of health it no longer
            // deserves. Anything the oracle dislikes undoes the whole attempt.
            if (pieces != null && pieces.Count > 0
                && VerifyComprehensive(result, pieces, margins).Count > 0)
            {
                RestorePlan(result, snapshot);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Puts one pane into the first rectangle that fits it, and replaces
        /// that rectangle with the two strips the pane leaves behind — the
        /// same guillotine split the packer itself performs.
        /// </summary>
        private static bool TryFitIntoLeftover(SheetLayout host, PlacedPiece piece, bool allowRotate)
        {
            for (int i = 0; i < host.Leftovers.Count; i++)
            {
                var region = host.Leftovers[i];
                double w = piece.WidthMm;
                double h = piece.HeightMm;
                bool rotated = false;

                if (w > region.WidthMm + 0.5 || h > region.HeightMm + 0.5)
                {
                    if (!allowRotate) continue;
                    if (h > region.WidthMm + 0.5 || w > region.HeightMm + 0.5) continue;
                    double swap = w; w = h; h = swap;
                    rotated = true;
                }

                // Floor the size and raise the origin, so rounding can never
                // push a pane past the edge of the rectangle it was given.
                host.Pieces.Add(new PlacedPiece
                {
                    Piece = piece.Piece,
                    X = (int)Math.Ceiling(region.X),
                    Y = (int)Math.Ceiling(region.Y),
                    WidthMm = (int)Math.Floor(w),
                    HeightMm = (int)Math.Floor(h),
                    Rotated = rotated
                });

                var remainders = new List<RemnantRegion>(2);
                double above = region.HeightMm - h;
                double right = region.WidthMm - w;
                if (above > 0.5)
                    remainders.Add(new RemnantRegion
                    {
                        SheetNo = host.SheetNo, X = region.X, Y = region.Y + h,
                        WidthMm = region.WidthMm, HeightMm = above
                    });
                if (right > 0.5)
                    remainders.Add(new RemnantRegion
                    {
                        SheetNo = host.SheetNo, X = region.X + w, Y = region.Y,
                        WidthMm = right, HeightMm = h
                    });

                host.Leftovers.RemoveAt(i);
                host.Leftovers.InsertRange(i, remainders);
                return true;
            }

            return false;
        }

        // ── snapshot / rollback ─────────────────────────────────────────

        private static List<(SheetLayout Sheet, List<PlacedPiece> Pieces, List<CutStep> Cuts)> CapturePlan(
            OptimizationResult result)
        {
            var snap = new List<(SheetLayout, List<PlacedPiece>, List<CutStep>)>(result.Sheets.Count);
            for (int i = 0; i < result.Sheets.Count; i++)
                snap.Add((result.Sheets[i], new List<PlacedPiece>(result.Sheets[i].Pieces),
                    new List<CutStep>(result.Sheets[i].Cuts)));
            return snap;
        }

        private static void RestorePlan(OptimizationResult result,
            List<(SheetLayout Sheet, List<PlacedPiece> Pieces, List<CutStep> Cuts)> snap)
        {
            result.Sheets.Clear();
            for (int i = 0; i < snap.Count; i++)
            {
                var entry = snap[i];
                var sheet = entry.Sheet;
                sheet.Pieces.Clear();
                sheet.Pieces.AddRange(entry.Pieces);
                sheet.Cuts.Clear();
                sheet.Cuts.AddRange(entry.Cuts);

                // Leftovers are NOT part of the snapshot: they are derived,
                // and an undone move must not leave its rectangles behind.
                // Clearing them is what makes a rollback honest — the callers
                // re-derive from the restored cuts.
                sheet.Leftovers.Clear();

                result.Sheets.Add(sheet);
            }
        }

        private static void RenumberSheets(OptimizationResult result)
        {
            for (int i = 0; i < result.Sheets.Count; i++)
                result.Sheets[i].SheetNo = i + 1;
        }
    }
}
