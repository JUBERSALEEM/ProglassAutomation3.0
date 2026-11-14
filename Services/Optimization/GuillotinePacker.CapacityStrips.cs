using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        // A fixed search window, not an input/quantity limit. Largest anchors and
        // smallest fillers are both visible even on a million-piece order.
        private const int StripCandidateWindow = 128;

        /// <summary>
        /// Capacity-aware strip candidate: columns (or transposed rows),
        /// with stacked panes inside each strip. Seed orientation maximizes
        /// repeat capacity, rather than the short-side remainder of ONE piece.
        /// Narrow pieces fill residual strips. No dimension, sheet count, job
        /// name or composition-specific pattern is encoded here.
        /// </summary>
        internal static OptimizationResult RunCapacityStrips(
            IReadOnlyList<CutPiece> pieces, IReadOnlyList<StockSize> stocks,
            MarginProfile margins, RotationPolicy rotation, bool transpose,
            CancellationToken cancellationToken)
        {
            var result = new OptimizationResult
            {
                Rotation = rotation,
                Strategy = transpose ? "CAPACITY STRIPS (rows)" : "CAPACITY STRIPS (columns)"
            };
            cancellationToken.ThrowIfCancellationRequested();
            var ordered = Sort(pieces, OrderBy.Area);
            var pool = new StripPool(ordered.Count);
            var frontier = new List<int>(StripCandidateWindow);
            var usedSheets = new int[stocks.Count];
            var stockOrder = Enumerable.Range(0, stocks.Count)
                .OrderBy(i => (long)stocks[i].WidthMm * stocks[i].HeightMm).ThenBy(i => i).ToArray();
            // A separated offcut needs two cuts, not just an empty gap. When
            // breakout is disabled, ordinary kerf-only separation is enough.
            long gap = margins.MinBreakoutMm > 0
                ? (long)margins.MinBreakoutMm + 2L * margins.KerfMm : margins.KerfMm;

            while (pool.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int anchor = pool.Head;
                int stockIndex = -1;
                StripChoice opening = default;
                long usableU = 0, usableV = 0;
                foreach (int si in stockOrder)
                {
                    var stock = stocks[si];
                    if (stock.MaxSheets < 0 || (stock.MaxSheets > 0 && usedSheets[si] >= stock.MaxSheets)) continue;
                    long w = (long)stock.WidthMm - margins.LeftMm - margins.RightMm;
                    long h = (long)stock.HeightMm - margins.TopMm - margins.BottomMm;
                    long u = transpose ? h : w, v = transpose ? w : h;
                    if (!TryStripOrientation(ordered[anchor], u, v, rotation, transpose, gap,
                        margins, capacitySeed: true, out opening)) continue;
                    stockIndex = si; usableU = u; usableV = v; break;
                }
                if (stockIndex < 0)
                {
                    result.Unplaced.Add(ordered[anchor]);
                    pool.Remove(anchor);
                    continue;
                }

                usedSheets[stockIndex]++;
                var sheet = new SheetLayout
                {
                    SheetNo = result.Sheets.Count + 1, Stock = stocks[stockIndex],
                    UsableX = margins.LeftMm, UsableY = margins.TopMm,
                    UsableWidth = (int)(transpose ? usableV : usableU),
                    UsableHeight = (int)(transpose ? usableU : usableV)
                };
                long uOffset = 0;
                int seed = anchor;
                var seedChoice = opening;
                while (seed >= 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int stripWidth = WidenStrip(pool, frontier, ordered, seedChoice,
                        usableU - uOffset, usableV, rotation, transpose, gap, margins);
                    long vOffset = 0;
                    int selected = seed;
                    var choice = seedChoice;
                    while (selected >= 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        sheet.Pieces.Add(new PlacedPiece
                        {
                            Piece = ordered[selected], Rotated = choice.Rotated,
                            X = checked(margins.LeftMm + (int)(transpose ? vOffset : uOffset)),
                            Y = checked(margins.TopMm + (int)(transpose ? uOffset : vOffset)),
                            WidthMm = transpose ? choice.V : choice.U,
                            HeightMm = transpose ? choice.U : choice.V
                        });
                        // PHASE 0: this path writes PlacedPiece directly, so it
                        // records its own cuts — see RecordStripPieceCuts.
                        RecordStripPieceCuts(sheet, transpose, uOffset, stripWidth, vOffset, choice);
                        pool.Remove(selected);
                        vOffset += choice.V + gap;
                        selected = FindStripPiece(pool, frontier, ordered, stripWidth, usableV - vOffset,
                            rotation, transpose, gap, margins, capacitySeed: false, out choice, flushWidth: uOffset + stripWidth == usableU);
                    }
                    // PHASE 0: the single cut that separates this strip from the
                    // rest of the sheet. Emitted after the strip's own cuts —
                    // a legal execution order, since each cut still crosses the
                    // whole sub-rectangle it is made in.
                    if (uOffset + stripWidth < usableU)
                        RecordStripSeparator(sheet, transpose, uOffset + stripWidth);
                    uOffset += stripWidth + gap;
                    seed = FindStripPiece(pool, frontier, ordered, usableU - uOffset, usableV,
                        rotation, transpose, gap, margins, capacitySeed: true, out seedChoice);
                }
                result.Sheets.Add(sheet);
            }
            // Do not consolidate this candidate with the old free-rectangle
            // mover: it can reintroduce the narrow offcuts this construction avoids.
            result.Sheets.Sort((a, b) => b.UsedSqm.CompareTo(a.UsedSqm));
            for (int i = 0; i < result.Sheets.Count; i++) result.Sheets[i].SheetNo = i + 1;
            return result;
        }

        /// <summary>
        /// PHASE 0: the strip builders write <see cref="PlacedPiece"/> directly
        /// instead of going through Commit, so they have to record their own
        /// cuts. Otherwise a QUICK plan that wins on capacity strips would
        /// produce sheets with NO cut sequence at all — the CNC export would
        /// have nothing to write for a third of the portfolio, and it would
        /// fail silently.
        ///
        /// The order is the physical one: first the cut that separates this
        /// piece from whatever follows it inside the strip, then — when the
        /// piece is narrower than its strip — the cut that frees the sliver
        /// beside it. Interior pieces are allowed to be narrower than the strip
        /// (TryStripOrientation only applies the width-edge test to strip
        /// seeds), so that sliver genuinely exists in the layout.
        /// </summary>
        private static void RecordStripPieceCuts(
            SheetLayout sheet, bool transpose, long uOffset, int stripWidth, long vOffset,
            StripChoice choice)
        {
            int px = sheet.UsableX + (int)(transpose ? vOffset : uOffset);
            int py = sheet.UsableY + (int)(transpose ? uOffset : vOffset);
            int pw = transpose ? choice.V : choice.U;
            int ph = transpose ? choice.U : choice.V;

            int sx = sheet.UsableX + (int)(transpose ? 0 : uOffset);
            int sy = sheet.UsableY + (int)(transpose ? uOffset : 0);
            int sw = transpose ? sheet.UsableWidth : stripWidth;
            int sh = transpose ? stripWidth : sheet.UsableHeight;

            if (!transpose)
            {
                // Columns: pieces stack downwards, the sliver sits to the right.
                if (py + ph < sy + sh)
                    AddStripCut(sheet, CutAxis.Horizontal, py + ph, sx, py, sw, sy + sh - py, 2);
                if (px + pw < sx + sw)
                    AddStripCut(sheet, CutAxis.Vertical, px + pw, px, py, sx + sw - px, ph, 3);
            }
            else
            {
                // Rows: pieces run rightwards, the sliver sits underneath.
                if (px + pw < sx + sw)
                    AddStripCut(sheet, CutAxis.Vertical, px + pw, px, py, sx + sw - px, sh, 2);
                if (py + ph < sy + sh)
                    AddStripCut(sheet, CutAxis.Horizontal, py + ph, px, py, pw, sy + sh - py, 3);
            }
        }

        /// <summary>
        /// The single full-length cut that separates one strip from the sheet
        /// (stage 1). Drawn across the whole usable rectangle, which is what a
        /// guillotine blade does.
        /// </summary>
        private static void RecordStripSeparator(SheetLayout sheet, bool transpose, long boundary)
        {
            int at = (transpose ? sheet.UsableY : sheet.UsableX) + (int)boundary;
            AddStripCut(sheet, transpose ? CutAxis.Horizontal : CutAxis.Vertical, at,
                sheet.UsableX, sheet.UsableY, sheet.UsableWidth, sheet.UsableHeight, 1);
        }

        private static void AddStripCut(SheetLayout sheet, CutAxis axis, int atMm,
            int x, int y, int w, int h, int stage)
        {
            sheet.Cuts.Add(new CutStep
            {
                Seq = sheet.Cuts.Count + 1,
                Axis = axis,
                AtMm = atMm,
                FromX = x,
                FromY = y,
                FromW = w,
                FromH = h,
                Stage = stage,
                IsTrim = false
            });
        }

        private static int WidenStrip(StripPool pool, List<int> frontier, List<CutPiece> pieces,
            StripChoice seed, long availableU, long availableV, RotationPolicy rotation,
            bool transpose, long gap, MarginProfile margins)
        {
            // Near-size pieces belong to the same repetition-capacity family,
            // not necessarily the same exact dimension. Widening within that
            // family preserves both the across/down counts and avoids a narrow
            // first filler preventing its slightly wider peers from rotating.
            long across = (availableU + gap) / (seed.U + gap);
            long down = (availableV + gap) / (seed.V + gap);
            int width = seed.U;
            pool.Frontier(frontier);
            foreach (int index in frontier)
            {
                var p = pieces[index];
                int turns = rotation == RotationPolicy.Free90 && p.AllowRotate ? 2 : 1;
                for (int turn = 0; turn < turns; turn++)
                {
                    int w = turn == 0 ? p.WidthMm : p.HeightMm;
                    int h = turn == 0 ? p.HeightMm : p.WidthMm;
                    int u = transpose ? h : w, v = transpose ? w : h;
                    if (u <= width || v <= 0 || u > availableU || v > availableV) continue;
                    if ((availableU + gap) / (u + gap) != across
                        || (availableV + gap) / (v + gap) != down) continue;
                    if (StripEdgeFits(availableU - u, margins)) width = u;
                }
            }
            return width;
        }

        private struct StripChoice
        {
            public int U, V;
            public bool Rotated;
        }

        private static bool TryStripOrientation(CutPiece piece, long availableU, long availableV,
            RotationPolicy rotation, bool transpose, long gap, MarginProfile margins,
            bool capacitySeed, out StripChoice choice, bool flushWidth = false)
        {
            choice = default;
            if (piece.WidthMm <= 0 || piece.HeightMm <= 0) return false;
            long bestCapacity = -1, bestDown = -1;
            int bestWidth = -1;
            int orientations = rotation == RotationPolicy.Free90 && piece.AllowRotate
                && piece.WidthMm != piece.HeightMm ? 2 : 1;
            for (int turn = 0; turn < orientations; turn++)
            {
                int w = turn == 0 ? piece.WidthMm : piece.HeightMm;
                int h = turn == 0 ? piece.HeightMm : piece.WidthMm;
                int u = transpose ? h : w, v = transpose ? w : h;
                if (u > availableU || v > availableV) continue;
                // Interior strip widths can vary: the reserved separator gives
                // even their largest member a cuttable offcut. Only the outer
                // boundary of the sheet needs the width-edge test here.
                if ((capacitySeed || flushWidth) && !StripEdgeFits(availableU - u, margins)) continue;
                if (!StripEdgeFits(availableV - v, margins)) continue;
                long down = (availableV + gap) / (v + gap);
                long capacity = ((availableU + gap) / (u + gap)) * down;
                bool better = bestCapacity < 0 || (capacitySeed
                    ? capacity > bestCapacity || (capacity == bestCapacity && down > bestDown)
                    : u > bestWidth || (u == bestWidth && down > bestDown));
                if (!better) continue;
                bestCapacity = capacity; bestDown = down; bestWidth = u;
                choice = new StripChoice { U = u, V = v, Rotated = turn != 0 };
            }
            return bestCapacity >= 0;
        }

        private static bool StripEdgeFits(long remainder, MarginProfile margins) => remainder == 0
            || remainder >= (long)margins.KerfMm + margins.MinBreakoutMm;

        private static int FindStripPiece(StripPool pool, List<int> frontier, List<CutPiece> pieces,
            long availableU, long availableV, RotationPolicy rotation, bool transpose, long gap,
            MarginProfile margins, bool capacitySeed, out StripChoice choice, bool flushWidth = false)
        {
            choice = default;
            if (pool.Count == 0 || availableU <= 0 || availableV <= 0) return -1;
            pool.Frontier(frontier);
            int best = -1;
            long bestArea = -1;
            foreach (int index in frontier)
            {
                var piece = pieces[index];
                long area = (long)piece.WidthMm * piece.HeightMm;
                if (area <= bestArea || !TryStripOrientation(piece, availableU, availableV, rotation,
                    transpose, gap, margins, capacitySeed, out var candidate, flushWidth)) continue;
                best = index; bestArea = area; choice = candidate;
            }
            return best;
        }

        /// <summary>
        /// Intrusive ordered frontier: O(1) removal, O(K) queries, O(N) memory.
        /// Unlike repeatedly scanning a marked-used array, it never walks over
        /// all earlier sheets to find the next live piece. No pieces are dropped
        /// when the candidate window is full; every remaining head is eventually
        /// placed or explicitly returned as unplaced.
        /// </summary>
        private sealed class StripPool
        {
            private readonly int[] _next, _previous;
            public int Head { get; private set; }
            private int _tail;
            public int Count { get; private set; }
            public StripPool(int count)
            {
                Count = count; Head = count == 0 ? -1 : 0; _tail = count - 1;
                _next = new int[count]; _previous = new int[count];
                for (int i = 0; i < count; i++)
                { _next[i] = i + 1 == count ? -1 : i + 1; _previous[i] = i - 1; }
            }
            public void Remove(int index)
            {
                int before = _previous[index], after = _next[index];
                if (before < 0) Head = after; else _next[before] = after;
                if (after < 0) _tail = before; else _previous[after] = before;
                Count--;
            }
            public void Frontier(List<int> output)
            {
                output.Clear();
                int front = Head, back = _tail;
                int take = Math.Min(StripCandidateWindow, Count);
                for (int n = 0; n < take; n++)
                {
                    if ((n & 1) == 0) { output.Add(front); front = _next[front]; }
                    else { output.Add(back); back = _previous[back]; }
                }
            }
        }

        // Synchronous adapter: the legacy pass's "done" is only 60% of QUICK.
        // Do not post an early completion before strips and validation finish.
        private sealed class QuickShelfProgress : IProgress<OptimizeProgress>
        {
            private readonly IProgress<OptimizeProgress> _target;
            public QuickShelfProgress(IProgress<OptimizeProgress> target) => _target = target;
            public void Report(OptimizeProgress value) => _target.Report(OptimizeProgress.Of(
                "quick", (int)(60 * value.Fraction), 100, value.Message));
        }

        private static OptimizationResult CheckCandidate(OptimizationResult candidate,
            IReadOnlyList<CutPiece> pieces, MarginProfile margins, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidate.RecordValidation(VerifyComprehensive(candidate, pieces, margins),
                conservationChecked: true, geometryChecked: true);
            return candidate;
        }
    }
}
