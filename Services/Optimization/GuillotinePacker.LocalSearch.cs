// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/GuillotinePacker.LocalSearch.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — PHASE E2, placement-level local search
//
// WHY THIS IS THE ONE REAL BET IN THE ROADMAP
//   BestSplit.cs:8-13 records that 18 ordering x split combinations, four
//   generators, and a 21,600-evaluation hill-climb over piece SEQUENCES all
//   produced the same 17 sheets. The search is exhausted. What that
//   experiment never tried is moving PLACEMENTS: taking a finished plan and
//   relocating a pane from one sheet to another. Different move set.
//
//   ConsolidateIntoOffcuts (phase 2 part 2) is the first, narrow instance of
//   that move set, and it already works. This file generalises it: best-fit
//   instead of first-fit, every donor sheet rather than only the emptiest,
//   iterated to a fixpoint instead of once — plus the swap move, which
//   exists precisely because some sheets cannot be emptied by relocation
//   alone but become empty after one exchange.
//
// RULES (same discipline as the offcut pass, because the same things can
// go wrong)
//   1. A pane only moves into a rectangle no pane occupies. The rectangles
//      come from replaying the plan's own cuts, so they are free by
//      construction.
//   2. Every sheet that receives a pane must re-derive a full guillotine cut
//      sequence. If it cannot, the move is undone — a plan nobody can cut is
//      worse than a plan with one more sheet.
//   3. A sheet is dropped only when it is completely empty.
//   4. After each pass the whole plan is re-verified, because the run records
//      its verdict before this runs. Anything the oracle dislikes undoes the
//      pass.
//   5. Off by default, and each move type has its own switch so they can be
//      measured separately. Combined, they cannot be attributed.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        /// <summary>
        /// Says what the local search did. Same reason as the reuse note: an
        /// improvement nobody is told about cannot be told apart from a bug.
        /// </summary>
        private static void NoteLocalSearch(OptimizationResult result, int sheetsSaved)
        {
            if (sheetsSaved <= 0)
            {
                result.ScaleNotes.Add(
                    "Local search: no pane could be relocated off a sheet that would then be empty. "
                    + "The plan is unchanged.");
                return;
            }

            result.ScaleNotes.Add(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Local search: {0} sheet(s) emptied by relocating their panes into rectangles the "
                + "other plates had already freed. Every receiving plate was re-derived and "
                + "re-verified before the move was kept.",
                sheetsSaved));
        }

        /// <summary>
        /// Placement-level local search over a finished plan.
        /// </summary>
        /// <param name="enableSwaps">E2.1: exchange two panes between sheets
        /// when neither can relocate. Measured separately from relocation.</param>
        /// <returns>The number of sheets eliminated.</returns>
        internal static int LocalSearch(OptimizationResult result, IReadOnlyList<CutPiece> pieces,
            MarginProfile margins, RotationPolicy rotation, bool enableSwaps = false, int maxPasses = 4)
        {
            if (result == null || margins == null || result.Sheets.Count < 2)
                return 0;

            bool allowRotate = rotation != RotationPolicy.Locked;
            int kerf = margins.KerfMm;
            int removed = 0;

            // Every leftover, not just the racking-worthy ones: the only
            // question asked of them here is whether a pane fits. The kerf is
            // passed every time, because a rectangle measured from the cut
            // line rather than from after the blade is up to a kerf wider than
            // the space that is actually free — and a pane placed by that
            // measurement sits too close to its neighbour.
            DeriveLeftovers(result, 1, kerf);

            for (int pass = 0; pass < maxPasses; pass++)
            {
                var snapshot = CapturePlan(result);
                bool changed = false;

                changed |= RelocatePass(result, allowRotate, kerf, ref removed);

                if (enableSwaps)
                    changed |= SwapPass(result, allowRotate, kerf, ref removed);

                if (!changed) break;

                // Rule 4. The verdict the run recorded was taken before this
                // pass ran, so it has to be earned again.
                if (pieces != null && pieces.Count > 0
                    && VerifyComprehensive(result, pieces, margins).Count > 0)
                {
                    RestorePlan(result, snapshot);
                    DeriveLeftovers(result, 1, kerf);
                    break;
                }
            }

            RenumberSheets(result);

            // Unconditional: a rolled-back attempt leaves rectangles that no
            // longer exist, and the report and the DXF export both draw them.
            DeriveLeftovers(result, Math.Max(margins.MinBreakoutMm, 1), kerf);

            return removed;
        }

        // ── E2.3 relocation, generalised ─────────────────────────────────

        /// <summary>
        /// Empties sheets by moving their panes into other sheets' leftovers.
        /// Donors are tried emptiest-first; each pane takes the TIGHTEST
        /// rectangle that fits (best-fit) so the large rectangles survive for
        /// the panes that need them. A donor that cannot be emptied completely
        /// is rolled back — a half-emptied sheet still has to be cut.
        /// </summary>
        private static bool RelocatePass(OptimizationResult result, bool allowRotate, int kerf,
            ref int removed)
        {
            bool anyChange = false;
            DeriveLeftovers(result, 1, kerf);

            for (int guard = 0; guard < result.Sheets.Count + 1; guard++)
            {
                var donors = DonorsEmptiestFirst(result);
                bool emptiedOne = false;

                foreach (var donor in donors)
                {
                    if (donor.Pieces.Count == 0) continue;

                    var snapshot = CapturePlan(result);
                    var moving = PiecesLargestFirst(donor);
                    var touched = new List<SheetLayout>();
                    bool ok = true;

                    foreach (var piece in moving)
                    {
                        SheetLayout host = null;
                        Fit best = default;
                        bool found = false;

                        for (int s = 0; s < result.Sheets.Count; s++)
                        {
                            var candidate = result.Sheets[s];
                            if (ReferenceEquals(candidate, donor)) continue;
                            if (TryBestFit(candidate, piece, allowRotate, out Fit fit)
                                && (!found || fit.Waste < best.Waste))
                            {
                                host = candidate;
                                best = fit;
                                found = true;
                            }
                        }

                        if (!found) { ok = false; break; }

                        ApplyFit(host, piece, best);
                        if (!touched.Contains(host)) touched.Add(host);
                    }

                    if (ok)
                    {
                        // Rule 2: the receiving sheets must still be cuttable.
                        for (int i = 0; i < touched.Count; i++)
                            if (!TryDeriveGuillotineCuts(touched[i])) { ok = false; break; }
                    }

                    if (!ok)
                    {
                        RestorePlan(result, snapshot);
                        continue;
                    }

                    donor.Pieces.Clear();
                    donor.Cuts.Clear();
                    result.Sheets.Remove(donor);
                    removed++;
                    emptiedOne = true;
                    anyChange = true;

                    // Leftovers moved; recompute before the next attempt.
                    DeriveLeftovers(result, 1, kerf);
                    break;
                }

                if (!emptiedOne) break;
                if (result.Sheets.Count < 2) break;
            }

            return anyChange;
        }

        // ── E2.1 swap ────────────────────────────────────────────────────

        /// <summary>
        /// Exchanges two panes between two sheets. Relocation alone cannot
        /// empty a sheet whose last pane is too big for every rectangle on
        /// offer but SMALLER than something already sitting on another sheet —
        /// trading the two puts the small pane where the big one was and frees
        /// a rectangle the big one can use.
        ///
        /// A swap is only accepted when it strictly removes glass from the
        /// emptier sheet. A swap that leaves both sheets as full as they were
        /// is churn, and churn costs time without moving the sheet count.
        /// </summary>
        private static bool SwapPass(OptimizationResult result, bool allowRotate, int kerf,
            ref int removed)
        {
            bool anyChange = false;

            for (int guard = 0; guard < result.Sheets.Count + 1; guard++)
            {
                var donors = DonorsEmptiestFirst(result);
                bool progressed = false;

                foreach (var donor in donors)
                {
                    if (donor.Pieces.Count == 0) continue;
                    double donorArea = PieceArea(donor);

                    var moving = PiecesLargestFirst(donor);
                    bool swapped = false;

                    foreach (var small in moving)
                    {
                        for (int s = 0; s < result.Sheets.Count && !swapped; s++)
                        {
                            var other = result.Sheets[s];
                            if (ReferenceEquals(other, donor)) continue;

                            for (int q = 0; q < other.Pieces.Count; q++)
                            {
                                var big = other.Pieces[q];
                                if ((double)big.WidthMm * big.HeightMm <= (double)small.WidthMm * small.HeightMm)
                                    continue;   // only trade UP: donor gives away the larger pane

                                var snapshot = CapturePlan(result);

                                // Take both out, then put each back elsewhere.
                                donor.Pieces.Remove(small);
                                other.Pieces.Remove(big);

                                DeriveLeftovers(donor, 1, kerf);
                                DeriveLeftovers(other, 1, kerf);

                                bool placedBig = TryBestFit(donor, big, allowRotate, out Fit bigFit);
                                if (placedBig) ApplyFit(donor, big, bigFit);

                                bool placedSmall = TryBestFit(other, small, allowRotate, out Fit smallFit);
                                if (placedSmall) ApplyFit(other, small, smallFit);

                                bool ok = placedBig && placedSmall
                                          && PieceArea(donor) < donorArea
                                          && TryDeriveGuillotineCuts(donor)
                                          && TryDeriveGuillotineCuts(other);

                                if (!ok)
                                {
                                    RestorePlan(result, snapshot);
                                    continue;
                                }

                                swapped = true;
                                anyChange = true;
                                break;
                            }
                        }
                        if (swapped) break;
                    }

                    if (!swapped) continue;

                    // A swap is only worth anything if it lets a sheet go.
                    int before = removed;
                    RelocatePass(result, allowRotate, kerf, ref removed);
                    progressed = removed > before;
                    break;
                }

                if (!progressed) break;
                if (result.Sheets.Count < 2) break;
            }

            return anyChange;
        }

        // ── fitting ──────────────────────────────────────────────────────

        private struct Fit
        {
            public int Index;
            public double W;
            public double H;
            public bool Rotated;
            /// <summary>Rectangle area left over after the pane goes in.
            /// Lower is a tighter fit, which is what best-fit minimises.</summary>
            public double Waste;
        }

        /// <summary>Finds the tightest leftover rectangle that fits this pane.</summary>
        private static bool TryBestFit(SheetLayout host, PlacedPiece piece, bool allowRotate, out Fit fit)
        {
            fit = default;
            bool found = false;
            double w0 = piece.WidthMm;
            double h0 = piece.HeightMm;

            for (int i = 0; i < host.Leftovers.Count; i++)
            {
                var region = host.Leftovers[i];
                double w = w0, h = h0;
                bool rotated = false;

                if (w > region.WidthMm + 0.5 || h > region.HeightMm + 0.5)
                {
                    if (!allowRotate) continue;
                    if (h > region.WidthMm + 0.5 || w > region.HeightMm + 0.5) continue;
                    double swap = w; w = h; h = swap;
                    rotated = true;
                }

                double waste = region.WidthMm * region.HeightMm - w * h;
                if (found && waste >= fit.Waste) continue;

                fit = new Fit { Index = i, W = w, H = h, Rotated = rotated, Waste = waste };
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Puts a pane into the chosen rectangle and replaces that rectangle
        /// with the two strips the pane leaves behind — the same guillotine
        /// split the packer itself performs.
        /// </summary>
        private static void ApplyFit(SheetLayout host, PlacedPiece piece, Fit fit)
        {
            var region = host.Leftovers[fit.Index];

            // Floor the size and raise the origin, so rounding can never push
            // a pane past the edge of the rectangle it was given.
            host.Pieces.Add(new PlacedPiece
            {
                Piece = piece.Piece,
                X = (int)Math.Ceiling(region.X),
                Y = (int)Math.Ceiling(region.Y),
                WidthMm = (int)Math.Floor(fit.W),
                HeightMm = (int)Math.Floor(fit.H),
                Rotated = fit.Rotated
            });

            var remainders = new List<RemnantRegion>(2);
            double above = region.HeightMm - fit.H;
            double right = region.WidthMm - fit.W;
            if (above > 0.5)
                remainders.Add(new RemnantRegion
                {
                    SheetNo = host.SheetNo, X = region.X, Y = region.Y + fit.H,
                    WidthMm = region.WidthMm, HeightMm = above
                });
            if (right > 0.5)
                remainders.Add(new RemnantRegion
                {
                    SheetNo = host.SheetNo, X = region.X + fit.W, Y = region.Y,
                    WidthMm = right, HeightMm = fit.H
                });

            host.Leftovers.RemoveAt(fit.Index);
            host.Leftovers.InsertRange(fit.Index, remainders);
        }

        // ── ordering helpers ─────────────────────────────────────────────

        private static double PieceArea(SheetLayout sheet)
        {
            double area = 0;
            for (int i = 0; i < sheet.Pieces.Count; i++)
                area += (double)sheet.Pieces[i].WidthMm * sheet.Pieces[i].HeightMm;
            return area;
        }

        private static List<SheetLayout> DonorsEmptiestFirst(OptimizationResult result)
        {
            var list = new List<SheetLayout>(result.Sheets);
            list.Sort((a, b) => PieceArea(a).CompareTo(PieceArea(b)));
            return list;
        }

        private static List<PlacedPiece> PiecesLargestFirst(SheetLayout sheet)
        {
            var list = new List<PlacedPiece>(sheet.Pieces);
            list.Sort((a, b) => ((double)b.WidthMm * b.HeightMm).CompareTo((double)a.WidthMm * a.HeightMm));
            return list;
        }
    }
}
