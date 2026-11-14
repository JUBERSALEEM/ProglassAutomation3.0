using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        // Retain the bulk LP repeats, but also try the existing geometry engines
        // on their small integer remainder. The entire order is never expanded
        // into beam states, and leftover panes are not forced into singleton cuts.
        private static int[] FinishRepeatRemainder(List<RepeatType> types, List<RepeatPattern> patterns,
            RepeatMaster master, int[] demand, int[] available, IReadOnlyList<StockSize> stocks,
            int[] rackIds, MarginProfile margins, RotationPolicy rotation, int[] incumbent,
            CancellationToken token, Func<bool> expired)
        {
            if (expired()) return incumbent;
            var remaining = (int[])demand.Clone(); var racks = (int[])available.Clone();
            var floor = new int[patterns.Count]; var values = master.PatternValues(patterns.Count);
            for (int i = 0; i < patterns.Count; i++)
            {
                int times = (int)Math.Min(int.MaxValue, Math.Floor(values[i] + 1e-7));
                times = Math.Min(times, PatternFits(patterns[i], remaining, racks));
                floor[i] = times; ConsumePattern(patterns[i], times, remaining, racks);
            }
            if (remaining.Sum() == 0 || remaining.Sum() > 2048) return incumbent;
            var input = new List<CutPiece>(); var typeMap = new Dictionary<CutPiece, int>();
            for (int t = 0; t < types.Count; t++)
                foreach (var p in types[t].Pieces.Skip(types[t].Next + demand[t] - remaining[t]).Take(remaining[t]))
                { input.Add(p); typeMap.Add(p, t); }
            var pool = new List<StockSize>(); var stockMap = new Dictionary<StockSize, int>();
            for (int s = 0; s < racks.Length; s++)
            {
                if (racks[s] <= 0) continue;
                var stock = stocks[rackIds[s]].WithMaxSheets(racks[s] == int.MaxValue ? 0 : racks[s]);
                pool.Add(stock); stockMap.Add(stock, s);
            }
            if (pool.Count == 0) return incumbent;
            OptimizationResult best = null;
            void Consider(OptimizationResult candidate)
            {
                if (expired()) return;
                best = Better(best, CheckCandidate(candidate, input, margins, token));
            }
            Consider(RunCapacityStrips(input, pool, margins, rotation, false, token));
            Consider(RunCapacityStrips(input, pool, margins, rotation, true, token));
            if (!expired()) Consider(PackSmart(input, pool, margins, rotation,
                SplitMode.HorizontalFirst, 2, token, candidateWindowSize: 128));
            if (input.Count <= PatternPartWindow)
                foreach (int width in new[] { 4, 8 })
                    if (!expired()) Consider(RunBestSplitBeam(input, pool, margins, rotation, width, token,
                        maxExpansions: PatternPartWindow * width, consolidationSweeps: 3));
            if (best == null || best.HasValidationFailure || best.Unplaced.Count != 0) return incumbent;
            long covered = incumbent.Select((n, i) => (long)n * patterns[i].Parts.Length).Sum();
            if (covered == demand.Sum() && best.SheetsUsed + floor.Sum() >= incumbent.Sum()) return incumbent;
            int originalCount = patterns.Count;
            foreach (var sheet in best.Sheets)
            {
                var parts = sheet.Pieces.Select(p => new RepeatPlacement(typeMap[p.Piece],
                    p.X - margins.LeftMm, p.Y - margins.TopMm, p.WidthMm, p.HeightMm)).ToArray();
                patterns.Add(new RepeatPattern { Stock = stockMap[sheet.Stock], Parts = parts,
                    Counts = parts.GroupBy(p => p.Type).Select(g => (g.Key, g.Count())).ToArray(),
                    Area = parts.Sum(p => (long)p.W * p.H) });
            }
            Array.Resize(ref floor, patterns.Count);
            for (int i = originalCount; i < patterns.Count; i++) floor[i] = 1;
            return floor;
        }
    }
}
