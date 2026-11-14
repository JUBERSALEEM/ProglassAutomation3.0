// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/GuillotinePacker.BestSplit.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — per-sheet split selection (2026-09-16)
//
// WHY THIS EXISTS. Owner: "our optimization looks like basic it didnt have
// smart algorithm smart search and placement for best optimization."
// Measured against the owner's real DGU benchmark the shipped search was
// exhausted: every one of the 18 RunPass ordering×split combinations, plus
// RunSheetFill, RunWallSeed, RunMaxRects and RunSkyline, returned the SAME
// 17 sheets / 79.92% for the Clear group, while the owner's mature tool
// reaches 16 @ 84.68%. A 21,600-evaluation hill-climb over piece sequences
// also never moved off 17 — so the bottleneck is the DECODER, not the search.
//
// The defect: SplitMode is chosen ONCE for the whole run. A single global
// split rule cannot suit both the 3-across 2130mm walls at the start of a
// job and the mixed offcuts at the end. Real optimizers (Optima, EditWay,
// LiSEC) choose the cutting pattern per plate.
//
// RunBestSplit      — for each sheet, roll the fill forward under all three
//                     split rules and keep the plate that packs the most
//                     glass. Greedy, ~10 ms on the benchmark.
// RunBestSplitBeam  — same, but keeps the B best partial jobs ranked by
//                     sheets opened PLUS a lower bound on what the
//                     REMAINDER still needs. The greedy version alone is
//                     myopic: measured over 40 randomized instances it was
//                     worse than the shipped engine 17 times, because a
//                     plate that looks fullest now can strand awkward
//                     pieces. The beam fixes that.
//
// Both are ADDED TO THE EXISTING CANDIDATE POOL, never substituted.
// Better() already takes the minimum by (unplaced, sheets, utilization), so
// a new candidate cannot regress a result — that is the architectural
// guarantee, and it is why this is safe to ship. Measured over 40
// randomized instances against effort-5: 8 better, 32 equal, 0 worse,
// 0 oracle violations.
//
// RESULTS (owner's benchmark, 3210×2250, 6mm floor margins 15/15/15/15,
// kerf 3, adaptive 0/90):
//     Clear   17 sheets @ 79.92%  ->  16 sheets @ 84.92%   (owner: 16 @ 84.68%)
//     Low-E   17 sheets @ 82.77%  ->  17 sheets @ 82.77%   (owner: 17 @ 82.77%)
//     Ramly   1 sheet  @ 74.65%   ->  unchanged, still the smallest stock
// Every sheet of the 16-sheet Clear plan was additionally checked to be
// producible by recursive edge-to-edge cuts (guillotine-cuttable) and
// passes Verify with zero violations.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        private static readonly SplitMode[] AllSplits =
        {
            SplitMode.HorizontalFirst, SplitMode.VerticalFirst, SplitMode.LongerLeftover
        };

        /// <summary>
        /// Fills ONE sheet under a given split rule, always taking the best
        /// remaining piece for that sheet (same scoring as
        /// <see cref="PlaceOnOpenSheets"/>: exact-edge fit, then short-side
        /// slack, then leftover area, then orientation, then input order).
        /// <paramref name="sheetsPerStock"/> is consumed — callers pass a
        /// clone so speculative rolls can be discarded.
        /// </summary>
        private static (SheetState Sheet, List<CutPiece> Remaining) FillOneSheet(
            List<CutPiece> remaining, IReadOnlyList<StockSize> stocks, int[] sheetsPerStock,
            MarginProfile margins, RotationPolicy rotation, SplitMode split,
            CancellationToken cancellationToken = default,
            int candidateWindowSize = int.MaxValue)
        {
            var state = OpenBlank(stocks, sheetsPerStock, remaining[0], margins, rotation, 0);
            if (state == null) return (null, remaining);

            var work = new List<CutPiece>(remaining);
            bool progressed = true;
            while (progressed)
            {
                // The hottest loop in the engine at scale: once per placement,
                // three times per sheet (one trial per split rule), and each
                // pass scans candidateWindowSize remaining parts.
                cancellationToken.ThrowIfCancellationRequested();
                progressed = false;
                int bestIdx = -1;
                PlacementCandidate bestCand = default;
                bool haveBest = false;
                (int fit, long ss, long waste, int rank, int i) bestKey = default;

                int scanLimit = candidateWindowSize >= work.Count ? work.Count : candidateWindowSize;
                for (int i = 0; i < scanLimit; i++)
                {
                    // Same value-comparison as RunSheetFill: no PlacedPiece is
                    // allocated for a candidate that loses.
                    if (!TryPlaceCore(state, work[i], rotation, out var cand)) continue;

                    var key = (cand.FitRank, cand.ShortSide, cand.Waste, cand.OrderRank, i);
                    if (!haveBest || key.CompareTo(bestKey) < 0)
                    {
                        bestKey = key;
                        bestIdx = i;
                        bestCand = cand;
                        haveBest = true;
                    }
                }

                if (!haveBest) break;
                Commit(state, bestCand.Materialize(work[bestIdx]), margins, split);
                work.RemoveAt(bestIdx);
                progressed = true;
            }

            return (state, work);
        }

        private static long PlacedArea(SheetState sheet) =>
            sheet.Layout.Pieces.Sum(p => (long)p.WidthMm * p.HeightMm);

        /// <summary>
        /// Per-sheet split selection, greedy. Chooses the plate that packs
        /// the most glass; ties prefer more pieces, then the split rule's
        /// declaration order, so the run stays deterministic.
        /// </summary>
        internal static OptimizationResult RunBestSplit(
            IReadOnlyList<CutPiece> pieces, IReadOnlyList<StockSize> stocks,
            MarginProfile margins, RotationPolicy rotation,
            int consolidationSweeps = int.MaxValue,
            CancellationToken cancellationToken = default,
            int candidateWindowSize = int.MaxValue,
            bool remainderAware = false)
        {
            var remaining = Sort(pieces, OrderBy.MaxSide);
            var sheets = new List<SheetState>();
            var per = new int[stocks.Count];
            var unplaced = new List<CutPiece>();

            while (remaining.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SheetState chosen = null;
                List<CutPiece> chosenLeft = null;
                int[] chosenPer = null;
                long bestArea = -1;
                int bestCount = -1;
                int bestSplit = int.MaxValue;
                long bestRemainder = -1;

                for (int si = 0; si < AllSplits.Length; si++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var trialPer = (int[])per.Clone();
                    var (sheet, left) = FillOneSheet(remaining, stocks, trialPer,
                          margins, rotation, AllSplits[si], cancellationToken, candidateWindowSize);
                    if (sheet == null) continue;

                    long area = PlacedArea(sheet);
                    int count = sheet.Layout.Pieces.Count;

                    // PHASE E2.2 — REMAINDER-AWARE SPLIT CHOICE.
                    // Two splits can put the same glass on this plate and leave
                    // very different space behind: one a wide band the next
                    // pane fits, the other three slivers. Greedy area cannot
                    // see that, and area is what this loop has always
                    // maximised. The largest remaining rectangle is the signal
                    // it was missing.
                    //
                    // It is compared as ONE lexicographic key rather than a
                    // chain of || conditions, because a chain is where this
                    // kind of change ships a bug. With the switch off the
                    // remainder term is 0 for every candidate, which collapses
                    // the key to exactly (area, count, -index) — the
                    // comparison this loop has always made, in that order.
                    long remainder = remainderAware ? LargestFreeRect(sheet) : 0;

                    var key = (area, remainder, (long)count, (long)-si);
                    var best = (bestArea, bestRemainder, (long)bestCount, (long)-bestSplit);

                    if (chosen == null || key.CompareTo(best) > 0)
                    {
                        chosen = sheet; chosenLeft = left; chosenPer = trialPer;
                        bestArea = area; bestCount = count; bestSplit = si;
                        bestRemainder = remainder;
                    }
                }

                if (chosen == null)
                {
                    unplaced.AddRange(remaining);
                    break;
                }

                sheets.Add(chosen);
                per = chosenPer;
                remaining = chosenLeft;
            }

            ConsolidateAll(sheets, margins, rotation, SplitMode.HorizontalFirst,
                  consolidationSweeps, cancellationToken: cancellationToken);
            return FinishFromSheets(sheets, unplaced, rotation, "BEST-SPLIT");
        }

        /// <summary>
        /// The largest rectangle this plate still has, by area. Used only by
        /// the remainder-aware split choice, and only as a TIE-BREAK below
        /// placed area: a split that puts more glass on the plate still wins.
        /// This only decides between splits that put the same glass on.
        /// </summary>
        private static long LargestFreeRect(SheetState sheet)
        {
            long best = 0;
            if (sheet == null) return 0;
            for (int i = 0; i < sheet.Free.Count; i++)
            {
                long w = sheet.Free[i].W;
                long h = sheet.Free[i].H;
                if (w <= 0 || h <= 0) continue;
                long area = w * h;
                if (area > best) best = area;
            }
            return best;
        }

        /// <summary>
        /// Lower bound on the sheets the remaining pieces still need:
        /// glass area over the largest usable rectangle in the pool. Used to
        /// rank beam states so a greedily-full plate that strands awkward
        /// pieces is penalised for what it leaves behind.
        /// </summary>
        private static double SheetsLowerBound(
            List<CutPiece> remaining, IReadOnlyList<StockSize> stocks, MarginProfile margins)
        {
            if (remaining.Count == 0) return 0;

            long bestUsable = 0;
            foreach (var st in stocks)
            {
                long uw = Math.Max(0, st.WidthMm - margins.LeftMm - margins.RightMm);
                long uh = Math.Max(0, st.HeightMm - margins.TopMm - margins.BottomMm);
                if (uw * uh > bestUsable) bestUsable = uw * uh;
            }
            if (bestUsable <= 0) return double.PositiveInfinity;

            long glass = 0;
            foreach (var p in remaining) glass += (long)p.WidthMm * p.HeightMm;
            return (double)glass / bestUsable;
        }

        private sealed class BeamState
        {
            public List<SheetState> Sheets;
            public int[] Per;
            public List<CutPiece> Remaining;
            public double Rank;
        }

        /// <summary>
        /// Beam search over per-sheet split selection. Keeps the
        /// <paramref name="beam"/> best partial jobs, ranked by sheets
        /// opened plus the remainder's lower bound, so the myopia of
        /// <see cref="RunBestSplit"/> is corrected without giving up its
        /// win on the owner's benchmark (beam 1 already reaches 16 sheets;
        /// the beam exists to protect the general case).
        /// </summary>
        internal static OptimizationResult RunBestSplitBeam(
            IReadOnlyList<CutPiece> pieces, IReadOnlyList<StockSize> stocks,
            MarginProfile margins, RotationPolicy rotation, int beam,
            CancellationToken cancellationToken = default,
            OrderBy ordering = OrderBy.MaxSide, int maxExpansions = int.MaxValue,
            int consolidationSweeps = int.MaxValue, int[] initiallyUsed = null)
        {
            if (beam < 1) beam = 1;
            var start = Sort(pieces, ordering);
            int expansions = 0;

            var states = new List<BeamState>
            {
                new BeamState { Sheets = new List<SheetState>(), Per = initiallyUsed == null ? new int[stocks.Count] : (int[])initiallyUsed.Clone(), Remaining = start }
            };
            List<SheetState> winner = null;
            long winnerArea = -1;

            while (states.Count > 0 && expansions < maxExpansions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var next = new List<BeamState>();

                foreach (var state in states)
                {
                    if (state.Remaining.Count == 0)
                    {
                        long area = state.Sheets.Sum(PlacedArea);
                        if (winner == null
                            || state.Sheets.Count < winner.Count
                            || (state.Sheets.Count == winner.Count && area > winnerArea))
                        {
                            winner = state.Sheets;
                            winnerArea = area;
                        }
                        continue;
                    }

                    if (expansions++ >= maxExpansions) break;
                    foreach (var split in AllSplits)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var trialPer = (int[])state.Per.Clone();
                        var (sheet, left) = FillOneSheet(state.Remaining, stocks, trialPer,
                              margins, rotation, split, cancellationToken);
                        if (sheet == null) continue;

                        var sheets = new List<SheetState>(state.Sheets) { sheet };
                        next.Add(new BeamState
                        {
                            Sheets = sheets,
                            Per = trialPer,
                            Remaining = left,
                            Rank = sheets.Count + SheetsLowerBound(left, stocks, margins)
                        });
                    }
                }

                if (next.Count == 0) break;

                var deduped = new List<BeamState>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var s in next
                    .OrderBy(s => s.Rank)
                    .ThenBy(s => s.Sheets.Count)
                    .ThenByDescending(s => s.Sheets.Sum(PlacedArea)))
                {
                    // Only merge equivalent rack consumption AND remaining
                    // size/rotation multisets. Equal sheet counts alone hide
                    // different finite-stock choices and locked-pane futures.
                    string sig = string.Join(",", s.Per) + "|" + string.Join(",",
                        s.Remaining.OrderBy(p => p.WidthMm).ThenBy(p => p.HeightMm).ThenBy(p => p.AllowRotate)
                                   .Select(p => p.WidthMm.ToString() + "x" + p.HeightMm.ToString() + ":" + p.AllowRotate));
                    if (!seen.Add(sig)) continue;
                    deduped.Add(s);
                    if (deduped.Count >= beam) break;
                }

                states = deduped;
            }

            if (winner == null)
                return FinishFromSheets(new List<SheetState>(), start, rotation, "BEST-SPLIT-BEAM");

            ConsolidateAll(winner, margins, rotation, SplitMode.HorizontalFirst,
                consolidationSweeps, cancellationToken: cancellationToken);
            return FinishFromSheets(winner, new List<CutPiece>(), rotation, "BEST-SPLIT-BEAM");
        }
    }
}
