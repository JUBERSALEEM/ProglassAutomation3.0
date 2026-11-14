// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/GuillotinePacker.Smart.cs
// Largest→smallest skyline pack + deterministic re-search of leftover
// sheets. 0/90 chosen by leftover geometry (see TryPlace). Every candidate
// still goes through Verify (margins + kerf).
//
// ⚠ STATUS 2026-09-16 — UNREACHABLE, AND MEASURED NOT TO HELP.
//   Nothing calls ImproveByRepack, and RunSkyline's only two call sites are
//   inside it, so this whole file never executes. It was left in place while
//   the engine header claimed a calibration it did not actually achieve.
//   Before deciding its fate it was measured rather than assumed: across the
//   owner's DGU benchmark and 40 randomized instances (200 skyline runs over
//   five orderings), RunSkyline STRICTLY BEAT THE CANDIDATE POOL 0 TIMES, and
//   on the Clear group it returns 17 sheets against the 16 that
//   GuillotinePacker.BestSplit.cs now reaches. ImproveByRepack likewise moves
//   neither group off the numbers the shipped pool already produces.
//   Wiring it in would therefore add runtime for no gain; it is kept only so
//   the measurement is reproducible and the decision is on the record.
//   Recommendation: delete once BestSplit has soaked in production.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        /// <summary>
        /// Bottom-left skyline: sit each piece on the lowest-left ledge,
        /// trying 0° then 90°, with kerf reserved on the right and above
        /// the contact edge. Completes one sheet (largest remaining first)
        /// before opening the next.
        /// </summary>
        private static OptimizationResult RunSkyline(
            IReadOnlyList<CutPiece> ordered, IReadOnlyList<StockSize> stocks,
            MarginProfile margins, RotationPolicy rotation)
        {
            var remaining = ordered.ToList();
            var layouts = new List<SheetLayout>();
            var per = new int[stocks.Count];
            var unplaced = new List<CutPiece>();
            int kerf = margins.KerfMm;

            while (remaining.Count > 0)
            {
                var opener = remaining[0];
                int stockIndex = PickSmallestStock(stocks, per, opener, margins, rotation);
                if (stockIndex < 0)
                {
                    unplaced.AddRange(remaining);
                    break;
                }

                var stock = stocks[stockIndex];
                int ux = margins.LeftMm, uy = margins.TopMm;
                int uw = Math.Max(0, stock.WidthMm - margins.LeftMm - margins.RightMm);
                int uh = Math.Max(0, stock.HeightMm - margins.TopMm - margins.BottomMm);
                var sky = new List<(int A, int B, int H)> { (0, uw, 0) };
                var layout = new SheetLayout
                {
                    SheetNo = layouts.Count + 1,
                    Stock = stock,
                    UsableX = ux,
                    UsableY = uy,
                    UsableWidth = uw,
                    UsableHeight = uh
                };
                per[stockIndex]++;

                var still = new List<CutPiece>();
                foreach (var piece in remaining)
                {
                    if (!SkylineTry(sky, layout, piece, rotation, kerf, ux, uy, uw, uh))
                        still.Add(piece);
                }

                if (layout.Pieces.Count == 0)
                {
                    unplaced.AddRange(remaining);
                    break;
                }
                layouts.Add(layout);
                remaining = still;
            }

            var result = new OptimizationResult { Rotation = rotation };
            foreach (var p in unplaced) result.Unplaced.Add(p);
            layouts.Sort((a, b) =>
            {
                long aa = a.Pieces.Sum(p => (long)p.WidthMm * p.HeightMm);
                long ba = b.Pieces.Sum(p => (long)p.WidthMm * p.HeightMm);
                return ba.CompareTo(aa);
            });
            for (int i = 0; i < layouts.Count; i++)
            {
                layouts[i].SheetNo = i + 1;
                result.Sheets.Add(layouts[i]);
            }
            return result;
        }

        private static int PickSmallestStock(
            IReadOnlyList<StockSize> stocks, int[] per, CutPiece piece,
            MarginProfile margins, RotationPolicy rotation)
        {
            int best = -1;
            long bestArea = long.MaxValue;
            for (int i = 0; i < stocks.Count; i++)
            {
                if (per[i] >= stocks[i].MaxSheets && stocks[i].MaxSheets != 0) continue;
                int uw = Math.Max(0, stocks[i].WidthMm - margins.LeftMm - margins.RightMm);
                int uh = Math.Max(0, stocks[i].HeightMm - margins.TopMm - margins.BottomMm);
                if (!Orientations(piece, rotation).Any(o => o.W <= uw && o.H <= uh)) continue;
                long area = (long)stocks[i].WidthMm * stocks[i].HeightMm;
                if (area < bestArea || (area == bestArea && i < best))
                {
                    bestArea = area;
                    best = i;
                }
            }
            return best;
        }

        private static bool SkylineTry(
            List<(int A, int B, int H)> sky, SheetLayout layout, CutPiece piece,
            RotationPolicy rotation, int kerf, int ux, int uy, int uw, int uh)
        {
            (int Y, int X, int W, int H, bool Rot)? best = null;

            foreach (var (w, h, rot, _) in Orientations(piece, rotation))
            {
                int x = 0;
                while (x + w <= uw)
                {
                    int yTop = 0;
                    foreach (var (a, b, hh) in sky)
                    {
                        if (b <= x || a >= x + w) continue;
                        if (hh > yTop) yTop = hh;
                    }
                    int y = yTop == 0 ? 0 : yTop + kerf;
                    if (y + h <= uh)
                    {
                        var cand = (y, x, w, h, rot);
                        if (best == null
                            || cand.y < best.Value.Y
                            || (cand.y == best.Value.Y && cand.x < best.Value.X)
                            || (cand.y == best.Value.Y && cand.x == best.Value.X && rot == false && best.Value.Rot))
                            best = cand;
                    }

                    int nxt = uw;
                    foreach (var (a, b, _) in sky)
                    {
                        if (a > x && a < nxt) nxt = a;
                        else if (b > x && b < nxt) nxt = b;
                    }
                    if (nxt <= x) break;
                    x = nxt;
                }
            }

            if (best == null) return false;

            var (py, px, pw, ph, prot) = best.Value;
            layout.Pieces.Add(new PlacedPiece
            {
                Piece = piece,
                X = ux + px,
                Y = uy + py,
                WidthMm = pw,
                HeightMm = ph,
                Rotated = prot
            });

            int x1 = Math.Min(uw, px + pw + kerf);
            var next = new List<(int A, int B, int H)>();
            foreach (var (a, b, hh) in sky)
            {
                if (b <= px || a >= x1) { next.Add((a, b, hh)); continue; }
                if (a < px) next.Add((a, px, hh));
                if (b > x1) next.Add((x1, b, hh));
            }
            next.Add((px, x1, py + ph));
            next.Sort((u, v) => u.A != v.A ? u.A.CompareTo(v.A) : u.B.CompareTo(v.B));
            sky.Clear();
            foreach (var seg in next)
            {
                if (seg.B <= seg.A) continue;
                if (sky.Count > 0 && sky[^1].B == seg.A && sky[^1].H == seg.H)
                    sky[^1] = (sky[^1].A, seg.B, seg.H);
                else
                    sky.Add(seg);
            }
            return true;
        }

        /// <summary>
        /// Re-run leftover-heavy orderings: largest→smallest, area, height,
        /// plus deterministic shuffles. Keeps the fewest-sheet verified plan.
        /// </summary>
        private static OptimizationResult ImproveByRepack(
            OptimizationResult seed,
            IReadOnlyList<CutPiece> pieces,
            IReadOnlyList<StockSize> stocks,
            MarginProfile margins,
            RotationPolicy rotation,
            int effort)
        {
            var best = seed;
            var splits = new[] { SplitMode.HorizontalFirst, SplitMode.VerticalFirst, SplitMode.LongerLeftover };
            var orders = new[] { OrderBy.MaxSide, OrderBy.Area, OrderBy.Height, OrderBy.Width, OrderBy.MinSide };

            foreach (var order in orders)
            {
                var sorted = Sort(pieces, order);
                foreach (var split in splits)
                {
                    best = Better(best, RunSheetFill(sorted, stocks, margins, rotation, split));
                    best = Better(best, RunPass(sorted, stocks, margins, rotation, split));
                }
                best = Better(best, RunSkyline(sorted, stocks, margins, rotation));
            }

            int shuffles = effort >= 5 ? 24 : effort >= 3 ? 12 : 4;
            for (int k = 1; k <= shuffles; k++)
            {
                var shuffled = ShuffleDeterministic(Sort(pieces, OrderBy.MaxSide), 41 + k * 97);
                best = Better(best, RunSheetFill(shuffled, stocks, margins, rotation, SplitMode.HorizontalFirst));
                best = Better(best, RunSkyline(shuffled, stocks, margins, rotation));
                best = Better(best, RunPass(shuffled, stocks, margins, rotation, SplitMode.LongerLeftover));
            }

            if (best != null && Verify(best, margins).Count > 0)
                return seed;
            return best ?? seed;
        }
    }
}
