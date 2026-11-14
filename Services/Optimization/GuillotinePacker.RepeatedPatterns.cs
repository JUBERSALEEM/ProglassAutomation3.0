using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        private static OptimizationResult PackIndustrialPatterns(IReadOnlyList<CutPiece> pieces,
            IReadOnlyList<StockSize> stocks, MarginProfile margins, RotationPolicy rotation, int effort,
            CancellationToken token, IProgress<OptimizeProgress> progress)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(OptimizeProgress.Of("repeat", 0, 100, "Aggregating whole-order repeated sizes"));
            var best = CheckCandidate(RunRepeatedPatterns(pieces, stocks, margins, rotation, 1, token), pieces, margins, token);
            foreach (bool transpose in new[] { false, true })
            {
                progress?.Report(OptimizeProgress.Of("capacity", transpose ? 65 : 45, 100, "Comparing scalable strip floor"));
                best = Better(best, CheckCandidate(RunCapacityStrips(pieces, stocks, margins, rotation, transpose, token), pieces, margins, token));
            }
            best = ImprovePatterns(best, pieces, stocks, margins, rotation, 1, Math.Clamp(effort, 1, 5), token);
            if (effort > 1)
            {
                progress?.Report(OptimizeProgress.Of("repeat", 80, 100, "Deepening whole-order pattern counts"));
                best = Better(best, CheckCandidate(RunRepeatedPatterns(pieces, stocks, margins, rotation, effort, token), pieces, margins, token));
            }
            best.ScaleNotes.Add("Industrial quantity route: count-based pattern master, row/column strip floors and bounded weak-sheet repair. Whole-order quadratic shelf/beam passes are not run at this scale; no panes are truncated.");
            progress?.Report(OptimizeProgress.Of("done", 100, 100, $"{best.SheetsUsed:N0} sheets; {best.Unplaced.Count:N0} unplaced"));
            return best;
        }

        private static bool RepeatPatternSeparationsValid(RepeatPlacement[] parts, MarginProfile margins)
        {
            for (int i = 0; i < parts.Length; i++)
                for (int j = i + 1; j < parts.Length; j++)
                {
                    var a = parts[i]; var b = parts[j];
                    bool x = a.X < b.X + b.W && b.X < a.X + a.W;
                    bool y = a.Y < b.Y + b.H && b.Y < a.Y + a.H;
                    if (x && y) return false;
                    if (!x && !y) continue;
                    int gap = x ? Math.Max(b.Y - a.Y - a.H, a.Y - b.Y - b.H)
                        : Math.Max(b.X - a.X - a.W, a.X - b.X - b.W);
                    if (gap < margins.KerfMm || (gap > margins.KerfMm && gap - margins.KerfMm < margins.MinBreakoutMm)) return false;
                }
            return true;
        }

        private const int RepeatTypeWindow = 128;
        private const int RepeatColumnLimit = 8192;
        private const int IndustrialQuantityThreshold = 100000;

        /// <summary>
        /// Shown when a staged cut pattern is requested for a job big enough to
        /// take the industrial path, which builds its own free rectangles and so
        /// cannot be stage-bounded. Silently handing back a free-guillotine plan
        /// is the one thing a production constraint must never do.
        /// </summary>
        private const string IndustrialStagedUnsupportedNote =
            "STAGED PATTERN NOT APPLIED: jobs of 100,000 pieces or more use the " +
            "industrial path, which builds its own rectangles and cannot be " +
            "stage-bounded. This plan is free-guillotine. Split the job into " +
            "smaller cutting groups to get a staged pattern.";
        private sealed class RepeatType
        {
            public int W, H, Thickness, Next;
            public bool Rotate;
            public string Color, Material;
            public List<CutPiece> Pieces = new();
            public int Remaining => Pieces.Count - Next;
        }

        /// <summary>
        /// Quantity-aware cutting stock: price guillotine patterns, balance their
        /// repeat counts against whole-order demand, then expand original pane
        /// identities exactly once. Search complexity is governed by distinct
        /// sizes, not expanded order quantities. No fixture/pattern catalogue.
        /// </summary>
        private static OptimizationResult RunRepeatedPatterns(IReadOnlyList<CutPiece> pieces,
            IReadOnlyList<StockSize> stocks, MarginProfile margins, RotationPolicy rotation,
            int effort, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = new OptimizationResult { Rotation = rotation, Strategy = "REPEATED PATTERN MASTER" };
            if (pieces.Count == 0) return result;
            var watch = Stopwatch.StartNew();
            var budget = TimeSpan.FromSeconds(20 + Math.Clamp(effort, 1, 5) * 10);
            bool Expired() => watch.Elapsed >= budget;
            string Normalize(string s) => (s ?? "").Trim().ToUpperInvariant();
            var groups = new Dictionary<(int, int, bool, int, string, string), RepeatType>();
            foreach (var p in pieces)
            {
                token.ThrowIfCancellationRequested();
                var key = (p.WidthMm, p.HeightMm, rotation == RotationPolicy.Free90 && p.AllowRotate,
                    p.ThicknessMm, Normalize(p.Color), Normalize(p.MaterialKey));
                if (!groups.TryGetValue(key, out var t))
                {
                    t = new RepeatType { W = key.Item1, H = key.Item2, Rotate = key.Item3,
                        Thickness = key.Item4, Color = key.Item5, Material = key.Item6 };
                    groups.Add(key, t);
                }
                t.Pieces.Add(p);
            }
            bool priceWindows = pieces.Count > groups.Count &&
                (groups.Count <= RepeatTypeWindow || (long)pieces.Count >= 2L * groups.Count);
            var used = new int[stocks.Count]; int windows = 0, columnCount = 0;
            bool limited = false;
            foreach (var material in groups.Values.GroupBy(t => (t.Thickness, t.Color, t.Material)))
            {
                if (!priceWindows) break;
                var sorted = material.OrderByDescending(t => t.Pieces.Count).ThenBy(t => t.W).ThenBy(t => t.H)
                    .ThenBy(t => t.Rotate).ToList();
                for (int offset = 0; offset < sorted.Count && !Expired(); offset += RepeatTypeWindow)
                {
                    token.ThrowIfCancellationRequested();
                    var types = sorted.Skip(offset).Take(RepeatTypeWindow).OrderBy(t => t.W).ThenBy(t => t.H)
                        .ThenBy(t => t.Rotate).ToList();
                    var rackIds = Enumerable.Range(0, stocks.Count).Where(i =>
                        stocks[i].MaxSheets >= 0 && (stocks[i].MaxSheets == 0 || used[i] < stocks[i].MaxSheets)
                        && (stocks[i].ThicknessMm == 0 || types[0].Thickness == 0 || stocks[i].ThicknessMm == types[0].Thickness)
                        && (string.IsNullOrWhiteSpace(stocks[i].Color) || types[0].Color.Length == 0 || Normalize(stocks[i].Color) == types[0].Color)
                        && (string.IsNullOrWhiteSpace(stocks[i].MaterialKey) || types[0].Material.Length == 0 || Normalize(stocks[i].MaterialKey) == types[0].Material))
                        .OrderBy(i => (long)stocks[i].WidthMm * stocks[i].HeightMm).ThenBy(i => i).Take(32).ToArray();
                    if (rackIds.Length == 0) continue;
                    var demand = types.Select(t => t.Remaining).ToArray();
                    var available = rackIds.Select(i => stocks[i].MaxSheets == 0 ? int.MaxValue : stocks[i].MaxSheets - used[i]).ToArray();
                    var master = new RepeatMaster(demand, available, (double)pieces.Count + 1, token, Expired);
                    var patterns = new List<RepeatPattern>(); var seen = new HashSet<string>(StringComparer.Ordinal);
                    void Add(RepeatPattern p)
                    {
                        if (patterns.Count >= RepeatColumnLimit) { limited = true; return; }
                        // Small quantities can use a subset of a priced pattern.
                        // Removing panes enlarges waste, never shrinks separation.
                        var taken = new int[types.Count];
                        p.Parts = p.Parts.Where(part => taken[part.Type]++ < demand[part.Type]).ToArray();
                        if (p.Parts.Length == 0 || !RepeatPatternSeparationsValid(p.Parts, margins)) return;
                        p.Counts = p.Parts.GroupBy(part => part.Type).Select(g => (g.Key, g.Count())).OrderBy(c => c.Key).ToArray();
                        string key = p.Stock + "|" + string.Join(",", p.Counts.Select(c => c.Type + ":" + c.Count));
                        if (!seen.Add(key)) return;
                        p.Area = p.Parts.Sum(part => (long)part.W * part.H);
                        master.Add(p, patterns.Count); patterns.Add(p);
                    }
                    // Single-pane columns provide a feasible completion whenever
                    // rack supply permits; orientation/stock/material are explicit.
                    for (int s = 0; s < rackIds.Length; s++)
                    {
                        var st = stocks[rackIds[s]];
                        long uw = (long)st.WidthMm - margins.LeftMm - margins.RightMm;
                        long uh = (long)st.HeightMm - margins.TopMm - margins.BottomMm;
                        for (int t = 0; t < types.Count; t++)
                        {
                            var type = types[t]; if (type.W <= 0 || type.H <= 0) continue;
                            if (type.W <= uw && type.H <= uh)
                                Add(new RepeatPattern { Stock = s, Parts = new[] { new RepeatPlacement(t, 0, 0, type.W, type.H) } });
                            else if (type.Rotate && type.H <= uw && type.W <= uh)
                                Add(new RepeatPattern { Stock = s, Parts = new[] { new RepeatPlacement(t, 0, 0, type.H, type.W) } });
                        }
                    }
                    void Price(double[] values)
                    {
                        // A common cut uses one kerf; a separated waste strip
                        // uses two kerfs plus the configured minimum breakout.
                        long separated = (long)margins.MinBreakoutMm + 2L * margins.KerfMm;
                        if (margins.KerfMm < 0 || margins.MinBreakoutMm < 0 || separated > RepeatAxisLimit) return;
                        foreach (int gap in new[] { (int)separated, margins.KerfMm }.Distinct())
                            for (int s = 0; s < rackIds.Length && !Expired(); s++)
                                foreach (bool transpose in new[] { false, true })
                                    PriceRepeatPatterns(types, stocks[rackIds[s]], s, margins, values, gap, transpose, Add, token, Expired);
                    }
                    Price(types.Select(t => (double)t.W * t.H / 1000000d).ToArray());
                    Price(Enumerable.Repeat(1d, types.Count).ToArray());
                    int rounds = 0;
                    for (; rounds < 48 + effort * 8 && !Expired() && patterns.Count < RepeatColumnLimit; rounds++)
                    {
                        master.Solve(); int old = patterns.Count;
                        Price(master.Prices);
                        if (old == patterns.Count) break;
                    }
                    master.Solve();
                    // Integer remainders need partially used versions of priced
                    // patterns: a pattern requiring twelve identical panes must
                    // not strand the last five onto five singleton sheets.
                    var residual = (int[])demand.Clone(); var residualRacks = (int[])available.Clone();
                    var values = master.PatternValues(patterns.Count);
                    for (int i = 0; i < patterns.Count; i++)
                    {
                        int count = (int)Math.Min(int.MaxValue, Math.Floor(values[i] + 1e-7));
                        count = Math.Min(count, PatternFits(patterns[i], residual, residualRacks));
                        ConsumePattern(patterns[i], count, residual, residualRacks);
                    }
                    foreach (var p in patterns.ToArray())
                    {
                        token.ThrowIfCancellationRequested(); if (Expired()) break;
                        var remaining = (int[])residual.Clone();
                        var subset = p.Parts.Where(part => remaining[part.Type]-- > 0).ToArray();
                        if (subset.Length > 0 && subset.Length < p.Parts.Length)
                            Add(new RepeatPattern { Stock = p.Stock, Parts = subset });
                    }
                    master.Solve();
                    var repeats = IntegerRepeatPlan(patterns, master, demand, available, token, Expired, effort);
                    repeats = FinishRepeatRemainder(types, patterns, master, demand, available, stocks, rackIds,
                        margins, rotation, repeats, token, Expired);
                    result.ScaleNotes.Add($"Pattern window {windows + 1}: restricted LP objective {master.Objective:F3} (includes shortage penalties); integer pattern repeats {repeats.Sum():N0} (not a global optimum certificate).");
                    for (int i = 0; i < repeats.Length; i++)
                    {
                        var pattern = patterns[i]; int rack = rackIds[pattern.Stock];
                        for (int repeat = 0; repeat < repeats[i]; repeat++)
                        {
                            token.ThrowIfCancellationRequested();
                            var sheet = new SheetLayout { SheetNo = result.Sheets.Count + 1, Stock = stocks[rack],
                                UsableX = margins.LeftMm, UsableY = margins.TopMm,
                                UsableWidth = stocks[rack].WidthMm - margins.LeftMm - margins.RightMm,
                                UsableHeight = stocks[rack].HeightMm - margins.TopMm - margins.BottomMm };
                            foreach (var p in pattern.Parts)
                            {
                                var type = types[p.Type]; var piece = type.Pieces[type.Next++];
                                sheet.Pieces.Add(new PlacedPiece { Piece = piece, X = margins.LeftMm + p.X, Y = margins.TopMm + p.Y,
                                    WidthMm = p.W, HeightMm = p.H, Rotated = p.W != type.W || p.H != type.H });
                            }
                            result.Sheets.Add(sheet); used[rack]++;
                        }
                    }
                    windows++; columnCount += patterns.Count;
                    limited |= master.Limited || rounds >= 48 + effort * 8;
                }
            }
            // Every untouched pane survives search limits. Repair with the
            // scalable strip constructor using ONLY remaining inventory.
            foreach (var material in groups.Values.GroupBy(t => (t.Thickness, t.Color, t.Material)))
            {
                token.ThrowIfCancellationRequested();
                var left = material.SelectMany(t => t.Pieces.Skip(t.Next)).ToList();
                if (left.Count == 0) continue;
                var allowed = new List<StockSize>(); var original = new Dictionary<StockSize, int>();
                foreach (int i in Enumerable.Range(0, stocks.Count))
                {
                    var st = stocks[i]; var type = material.First();
                    if (st.MaxSheets < 0 || (st.MaxSheets > 0 && used[i] >= st.MaxSheets)
                        || (st.ThicknessMm > 0 && type.Thickness > 0 && st.ThicknessMm != type.Thickness)
                        || (!string.IsNullOrWhiteSpace(st.Color) && type.Color.Length > 0 && Normalize(st.Color) != type.Color)
                        || (!string.IsNullOrWhiteSpace(st.MaterialKey) && type.Material.Length > 0 && Normalize(st.MaterialKey) != type.Material)) continue;
                    var copy = st.WithMaxSheets(st.MaxSheets == 0 ? 0 : st.MaxSheets - used[i]);
                    allowed.Add(copy); original.Add(copy, i);
                }
                if (allowed.Count == 0) { result.Unplaced.AddRange(left); continue; }
                var tail = RunCapacityStrips(left, allowed, margins, rotation, false, token);
                foreach (var sheet in tail.Sheets)
                {
                    int i = original[sheet.Stock]; used[i]++;
                    var copy = new SheetLayout { SheetNo = result.Sheets.Count + 1, Stock = stocks[i],
                        UsableX = sheet.UsableX, UsableY = sheet.UsableY, UsableWidth = sheet.UsableWidth, UsableHeight = sheet.UsableHeight };
                    copy.Pieces.AddRange(sheet.Pieces); result.Sheets.Add(copy);
                }
                result.Unplaced.AddRange(tail.Unplaced);
            }
            if (!priceWindows) result.ScaleNotes.Add("High-diversity input: repeated-pattern pricing was bypassed because most sizes occur once; scalable strip and existing candidate searches remain available.");
            result.ScaleNotes.Add($"Quantity-aware search: {groups.Count:N0} distinct size/rotation/material types; {windows} type windows; {columnCount:N0} generated patterns. Original identities preserved.");
            if (limited || Expired()) result.ScaleNotes.Add("Pattern search work/time limit reached; completed integer patterns retained and all remaining panes accounted for. Not a proof of optimality.");
            return result;
        }
    }
}
