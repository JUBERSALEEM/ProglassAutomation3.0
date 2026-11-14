using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        // Pricing works in integer millimetres, not a raster approximation.
        // These are search/memory limits, never order-quantity limits.
        private const int RepeatAxisLimit = 16384;
        private const int RepeatPatternPanes = 256;
        private readonly record struct RepeatPlacement(int Type, int X, int Y, int W, int H);
        private readonly record struct RepeatOrientation(int Type, int U, int V);
        private sealed class RepeatPattern
        {
            public int Stock;
            public RepeatPlacement[] Parts = Array.Empty<RepeatPlacement>();
            public (int Type, int Count)[] Counts = Array.Empty<(int, int)>();
            public long Area;
        }
        private sealed class RepeatStrip
        {
            public int Width;
            public double Value;
            public List<RepeatPlacement> Parts = new();
        }

        private static void PriceRepeatPatterns(List<RepeatType> types, StockSize stock, int stockIndex,
            MarginProfile margins, double[] prices, int gap, bool transpose,
            Action<RepeatPattern> emit, CancellationToken token, Func<bool> expired)
        {
            long uw = (long)stock.WidthMm - margins.LeftMm - margins.RightMm;
            long uh = (long)stock.HeightMm - margins.TopMm - margins.BottomMm;
            long lu = transpose ? uh : uw, lv = transpose ? uw : uh;
            if (lu <= 0 || lv <= 0 || lu + gap > RepeatAxisLimit || lv + gap > RepeatAxisLimit) return;
            int U = (int)lu, V = (int)lv, edge = checked(margins.KerfMm + margins.MinBreakoutMm);
            bool Edge(int remainder) => remainder == 0 || remainder >= edge;
            var options = new List<RepeatOrientation>();
            for (int i = 0; i < types.Count; i++)
            {
                var t = types[i];
                if (prices[i] <= 1e-10 || t.W <= 0 || t.H <= 0) continue;
                for (int turn = 0; turn < (t.Rotate && t.W != t.H ? 2 : 1); turn++)
                {
                    int w = turn == 0 ? t.W : t.H, h = turn == 0 ? t.H : t.W;
                    int u = transpose ? h : w, v = transpose ? w : h;
                    if (u <= U && v <= V) options.Add(new RepeatOrientation(i, u, v));
                }
            }
            var strips = new List<RepeatStrip>();
            foreach (int width in options.Select(o => o.U).Distinct().OrderBy(w => w))
            {
                token.ThrowIfCancellationRequested();
                if (expired()) return;
                var eligible = options.Where(o => o.U <= width && Edge(width - o.U)).ToArray();
                int cap = V + gap;
                var dp = new double[cap + 1];
                var pick = Enumerable.Repeat(-1, cap + 1).ToArray();
                var count = new int[cap + 1];
                for (int c = 1; c <= cap; c++)
                {
                    if ((c & 255) == 0) token.ThrowIfCancellationRequested();
                    dp[c] = dp[c - 1]; count[c] = count[c - 1];
                    for (int j = 0; j < eligible.Length; j++)
                    {
                        var o = eligible[j]; int cost = o.V + gap;
                        if (cost > c || count[c - cost] >= RepeatPatternPanes) continue;
                        double value = dp[c - cost] + prices[o.Type];
                        if (value <= dp[c] + 1e-10) continue;
                        dp[c] = value; pick[c] = j; count[c] = count[c - cost] + 1;
                    }
                }
                RepeatStrip Build(int capacity)
                {
                    var s = new RepeatStrip { Width = width }; int offset = 0;
                    for (int c = capacity; c > 0;)
                    {
                        int j = pick[c]; if (j < 0) { c--; continue; }
                        var o = eligible[j];
                        s.Parts.Add(new RepeatPlacement(o.Type, 0, offset, o.U, o.V));
                        offset += o.V + gap; c -= o.V + gap; s.Value += prices[o.Type];
                    }
                    return s;
                }
                var strip = Build(cap);
                if (strip.Parts.Count > 0)
                {
                    var last = strip.Parts[strip.Parts.Count - 1];
                    if (!Edge(V - last.Y - last.H)) strip = Build(Math.Max(0, cap - edge));
                    if (strip.Parts.Count > 0) strips.Add(strip);
                }
            }
            int capacityU = U + gap;
            var across = new double[capacityU + 1];
            var choice = Enumerable.Repeat(-1, capacityU + 1).ToArray();
            var sizes = new int[capacityU + 1];
            for (int c = 1; c <= capacityU; c++)
            {
                if ((c & 255) == 0) { token.ThrowIfCancellationRequested(); if (expired()) return; }
                across[c] = across[c - 1]; sizes[c] = sizes[c - 1];
                for (int j = 0; j < strips.Count; j++)
                {
                    var s = strips[j]; int cost = s.Width + gap;
                    if (cost > c || sizes[c - cost] + s.Parts.Count > RepeatPatternPanes) continue;
                    double value = across[c - cost] + s.Value;
                    if (value <= across[c] + 1e-10) continue;
                    across[c] = value; choice[c] = j; sizes[c] = sizes[c - cost] + s.Parts.Count;
                }
            }
            List<int> Reconstruct(int cap)
            {
                var ids = new List<int>();
                for (int c = cap; c > 0;)
                { int j = choice[c]; if (j < 0) { c--; continue; } ids.Add(j); c -= strips[j].Width + gap; }
                return ids;
            }
            void Emit(List<int> ids)
            {
                int used = ids.Sum(i => strips[i].Width + gap) - gap;
                if (ids.Count == 0 || used > U || !Edge(U - used)
                    || ids.Sum(i => strips[i].Parts.Count) > RepeatPatternPanes) return;
                var parts = new List<RepeatPlacement>(); int offset = 0;
                foreach (int id in ids)
                {
                    var s = strips[id];
                    foreach (var p in s.Parts)
                        parts.Add(transpose
                            ? new RepeatPlacement(p.Type, p.Y, p.X + offset, p.H, p.W)
                            : p with { X = p.X + offset });
                    offset += s.Width + gap;
                }
                emit(new RepeatPattern { Stock = stockIndex, Parts = parts.ToArray() });
            }
            Emit(Reconstruct(capacityU)); Emit(Reconstruct(Math.Max(0, capacityU - edge)));
            for (int j = 0; j < strips.Count; j++)
            {
                token.ThrowIfCancellationRequested(); if (expired()) return;
                int cost = strips[j].Width + gap;
                foreach (int reserve in new[] { 0, edge })
                {
                    if (cost > capacityU - reserve) continue;
                    var ids = Reconstruct(capacityU - reserve - cost); ids.Insert(0, j); Emit(ids);
                }
                int times = Math.Min(capacityU / cost, RepeatPatternPanes / strips[j].Parts.Count);
                while (times > 0 && !Edge(U - (times * cost - gap))) times--;
                Emit(Enumerable.Repeat(j, times).ToList());
            }
        }
    }
}
