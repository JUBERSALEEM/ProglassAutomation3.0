using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        // Managed restricted master: equality demand rows and finite-rack slack
        // rows. No native solver/runtime/package is required on the shop PC.
        // Artificial demand columns represent UNPLACED, at a lexicographic
        // penalty greater than placing every input pane on its own sheet.
        private sealed class RepeatMaster
        {
            private sealed class Column
            {
                public (int Row, double Value)[] Cells;
                public double Cost;
                public int Pattern = -1;
            }
            private readonly int _rows;
            private readonly int _tieSeed;
            private readonly double[] _rhs;
            private readonly List<Column> _columns = new();
            private readonly int[] _basis;
            private readonly double[,] _inverse;
            private readonly Dictionary<int, int> _stockRows = new();
            private readonly Dictionary<int, int> _boundRows = new();
            private readonly CancellationToken _token;
            private readonly Func<bool> _expired;
            public double[] Prices { get; private set; }
            public bool Limited { get; private set; }
            public double Objective => BasicValues().Select((v, i) => Math.Max(0, v) * _columns[_basis[i]].Cost).Sum();

            public RepeatMaster(int[] demand, int[] available, double shortagePenalty,
                CancellationToken token, Func<bool> expired, Dictionary<int, int> upper = null, int tieSeed = 0)
            {
                _token = token; _expired = expired; _tieSeed = tieSeed;
                for (int i = 0; i < available.Length; i++)
                    if (available[i] != int.MaxValue) _stockRows.Add(i, demand.Length + _stockRows.Count);
                if (upper != null)
                    foreach (var p in upper.OrderBy(p => p.Key)) _boundRows.Add(p.Key, demand.Length + _stockRows.Count + _boundRows.Count);
                _rows = demand.Length + _stockRows.Count + _boundRows.Count;
                _rhs = new double[_rows]; _basis = new int[_rows]; _inverse = new double[_rows, _rows];
                for (int i = 0; i < demand.Length; i++) _rhs[i] = demand[i];
                foreach (var p in _stockRows) _rhs[p.Value] = available[p.Key];
                foreach (var p in _boundRows) _rhs[p.Value] = upper[p.Key];
                for (int i = 0; i < _rows; i++)
                {
                    _basis[i] = i; _inverse[i, i] = 1;
                    _columns.Add(new Column { Cells = new[] { (i, 1d) }, Cost = i < demand.Length ? shortagePenalty : 0 });
                }
                Prices = new double[_rows];
            }
            public void Add(RepeatPattern pattern, int index)
            {
                var cells = pattern.Counts.Select(p => (p.Type, (double)p.Count)).ToList();
                if (_stockRows.TryGetValue(pattern.Stock, out int r)) cells.Add((r, 1));
                if (_boundRows.TryGetValue(index, out int bound)) cells.Add((bound, 1));
                _columns.Add(new Column { Cells = cells.ToArray(), Cost = 1, Pattern = index });
            }
            private double[] BasicValues()
            {
                var result = new double[_rows];
                for (int i = 0; i < _rows; i++)
                    for (int j = 0; j < _rows; j++) result[i] += _inverse[i, j] * _rhs[j];
                return result;
            }
            private void Duals()
            {
                Array.Clear(Prices, 0, Prices.Length);
                for (int i = 0; i < _rows; i++)
                {
                    double cost = _columns[_basis[i]].Cost;
                    for (int j = 0; j < _rows; j++) Prices[j] += cost * _inverse[i, j];
                }
            }
            public void Solve()
            {
                for (int step = 0; step < 6000; step++)
                {
                    _token.ThrowIfCancellationRequested();
                    if (_expired()) { Limited = true; break; }
                    Duals();
                    var basic = new HashSet<int>(_basis);
                    int enter = -1; double reduced = -1e-7;
                    for (int j = 0; j < _columns.Count; j++)
                    {
                        if (basic.Contains(j)) continue;
                        var col = _columns[j]; double rc = col.Cost;
                        foreach (var cell in col.Cells) rc -= Prices[cell.Row] * cell.Value;
                        uint Priority(int index) => unchecked((uint)(index + 1) * 2654435761u ^ (uint)_tieSeed * 2246822519u);
                        if (rc < reduced - 1e-9 || (_tieSeed != 0 && enter >= 0 && Math.Abs(rc - reduced) <= 1e-9
                            && Priority(j) < Priority(enter))) { reduced = rc; enter = j; }
                    }
                    if (enter < 0) return;
                    var values = BasicValues(); var direction = new double[_rows];
                    int leave = -1; double ratio = double.PositiveInfinity;
                    for (int i = 0; i < _rows; i++)
                    {
                        foreach (var c in _columns[enter].Cells) direction[i] += _inverse[i, c.Row] * c.Value;
                        if (direction[i] <= 1e-9) continue;
                        double r = Math.Max(0, values[i]) / direction[i];
                        if (r < ratio - 1e-8 || (Math.Abs(r - ratio) <= 1e-8 && (leave < 0 || _basis[i] < _basis[leave])))
                        { ratio = r; leave = i; }
                    }
                    // Numerical trouble is a candidate failure, not permission
                    // to invent quantities. The integer assembler rechecks every
                    // count and remaining rack capacity before using any column.
                    if (leave < 0) { Limited = true; break; }
                    double pivot = direction[leave];
                    for (int j = 0; j < _rows; j++) _inverse[leave, j] /= pivot;
                    for (int i = 0; i < _rows; i++)
                    {
                        if (i == leave) continue;
                        double factor = direction[i];
                        for (int j = 0; j < _rows; j++) _inverse[i, j] -= factor * _inverse[leave, j];
                    }
                    _basis[leave] = enter;
                    if (step == 5999) Limited = true;
                }
                Duals();
            }
            public double[] PatternValues(int count)
            {
                var output = new double[count]; var values = BasicValues();
                for (int i = 0; i < _rows; i++)
                {
                    int p = _columns[_basis[i]].Pattern;
                    if (p >= 0 && double.IsFinite(values[i])) output[p] = Math.Max(0, values[i]);
                }
                return output;
            }
        }

        private static int PatternFits(RepeatPattern p, int[] remaining, int[] stock)
        {
            int times = stock[p.Stock];
            foreach (var c in p.Counts) times = Math.Min(times, remaining[c.Type] / c.Count);
            return times;
        }
        private static void ConsumePattern(RepeatPattern p, int times, int[] remaining, int[] stock)
        {
            foreach (var c in p.Counts) remaining[c.Type] -= c.Count * times;
            if (stock[p.Stock] != int.MaxValue) stock[p.Stock] -= times;
        }

        private static int[] IntegerRepeatPlan(List<RepeatPattern> patterns, RepeatMaster master,
            int[] demand, int[] available, CancellationToken token, Func<bool> expired, int effort, bool diversify = true)
        {
            var counts = new int[patterns.Count]; var remaining = (int[])demand.Clone(); var stock = (int[])available.Clone();
            var fractional = master.PatternValues(patterns.Count);
            for (int i = 0; i < patterns.Count; i++)
            {
                int times = (int)Math.Min(int.MaxValue, Math.Floor(fractional[i] + 1e-7));
                times = Math.Min(times, PatternFits(patterns[i], remaining, stock));
                if (times <= 0) continue;
                counts[i] += times; ConsumePattern(patterns[i], times, remaining, stock);
            }
            var weights = master.Prices.Take(demand.Length).Select(v => Math.Max(0, v)).ToArray();
            // Greedy integer completion is always a feasible incumbent. Exact
            // quantity/rack tests, not fractional solver tolerances, authorize it.
            var patternValues = patterns.Select(p => p.Counts.Sum(c => weights[c.Type] * c.Count)).ToArray();
            int[] Complete(int[] needs, int[] racks, int trial = 0)
            {
                var eligible = Enumerable.Range(0, patterns.Count).Where(i => PatternFits(patterns[i], needs, racks) > 0).ToArray();
                var priorities = (double[])patternValues.Clone();
                if (trial > 0)
                    foreach (int i in eligible)
                    {
                        uint hash = unchecked((uint)(i + 1) * 2654435761u ^ (uint)trial * 2246822519u);
                        hash ^= hash >> 16; hash *= 3266489917u; hash ^= hash >> 13;
                        double noise = (hash & 65535) / 65535d - 0.5;
                        priorities[i] *= 1 + noise * (0.1 + (trial % 4) * 0.15);
                    }
                var result = new int[patterns.Count];
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int best = -1; double score = -1, fraction = -1; long area = -1;
                    foreach (int i in eligible)
                    {
                        var p = patterns[i]; if (PatternFits(p, needs, racks) <= 0) continue;
                        double value = priorities[i];
                        double f = fractional[i] - Math.Floor(fractional[i]);
                        if (value > score + 1e-8 || (Math.Abs(value - score) <= 1e-8 &&
                            (f > fraction + 1e-8 || (Math.Abs(f - fraction) <= 1e-8 && p.Area > area))))
                        { best = i; score = value; fraction = f; area = p.Area; }
                    }
                    if (best < 0) break;
                    int times = PatternFits(patterns[best], needs, racks);
                    result[best] += times; ConsumePattern(patterns[best], times, needs, racks);
                }
                return result;
            }
            var residual = (int[])remaining.Clone(); var residualStock = (int[])stock.Clone();
            var completion = Complete(remaining, stock);
            for (int trial = 1; trial <= 96 + effort * 32 && !expired(); trial++)
            {
                var needs = (int[])residual.Clone(); var racks = (int[])residualStock.Clone();
                var other = Complete(needs, racks, trial);
                if (needs.Sum() < remaining.Sum() || (needs.Sum() == remaining.Sum() && other.Sum() < completion.Sum()))
                { completion = other; remaining = needs; stock = racks; }
            }
            if (remaining.All(x => x == 0))
                completion = RepairIntegerPatterns(patterns, residual, residualStock, completion, weights,
                    token, expired, 12000 + effort * 4000);
            if (effort >= 3 && diversify)
                completion = BranchRepeatMaster(patterns, residual, residualStock, completion, token, expired, effort);
            for (int i = 0; i < counts.Length; i++) counts[i] += completion[i];

            // Reopen a few repeated patterns, not individual million-order panes.
            // Different cuts of the integer solution can resolve rounding waste.
            for (int pass = 0; pass < 2 + Math.Min(effort, 5); pass++)
            {
                token.ThrowIfCancellationRequested(); if (expired()) break;
                var repairNeeds = new int[demand.Length]; var repairStock = (int[])available.Clone();
                for (int i = 0; i < counts.Length; i++)
                    if (repairStock[patterns[i].Stock] != int.MaxValue) repairStock[patterns[i].Stock] -= counts[i];
                var removed = new int[counts.Length]; int total = 0;
                var order = Enumerable.Range(0, counts.Length).Where(i => counts[i] > 0)
                    .OrderBy(i => pass == 0 ? patterns[i].Area : (long)((i * 1103515245L + pass * 12345L) & 0x7fffffff));
                foreach (int i in order)
                {
                    if (total >= 16 || repairNeeds.Sum() + patterns[i].Parts.Length > 160) break;
                    removed[i]++; total++;
                    foreach (var c in patterns[i].Counts) repairNeeds[c.Type] += c.Count;
                    if (repairStock[patterns[i].Stock] != int.MaxValue) repairStock[patterns[i].Stock]++;
                }
                var repaired = RepairIntegerPatterns(patterns, repairNeeds, repairStock, removed, weights,
                    token, expired, 6000 + effort * 2000);
                if (repaired.Sum() >= total) continue;
                for (int i = 0; i < counts.Length; i++) counts[i] += repaired[i] - removed[i];
            }
            if (diversify)
            {
                double Score(int[] plan)
                {
                    var left = (int[])demand.Clone(); var racks = (int[])available.Clone();
                    for (int i = 0; i < plan.Length; i++)
                    {
                        if (plan[i] > PatternFits(patterns[i], left, racks)) return double.PositiveInfinity;
                        ConsumePattern(patterns[i], plan[i], left, racks);
                    }
                    return plan.Sum() + ((double)demand.Sum() + 1) * left.Sum();
                }
                double score = Score(counts);
                for (int seed = 1; seed <= 8 + effort * 4 && !expired(); seed++)
                {
                    // Degenerate LP optima may round very differently. Change
                    // only pivot tie order, never dimensions, demand or cost.
                    var alternative = new RepeatMaster(demand, available, (double)demand.Sum() + 1, token, expired, tieSeed: seed);
                    for (int i = 0; i < patterns.Count; i++) alternative.Add(patterns[i], i);
                    alternative.Solve();
                    var other = IntegerRepeatPlan(patterns, alternative, demand, available, token, expired, effort, false);
                    double candidateScore = Score(other);
                    if (candidateScore < score) { counts = other; score = candidateScore; }
                    if (!alternative.Limited && Math.Ceiling(alternative.Objective - 1e-6) >= score) break;
                }
                if (effort >= 3) counts = BranchRepeatMaster(patterns, demand, available, counts, token, expired, effort);
            }
            return counts;
        }

        private static int[] RepairIntegerPatterns(List<RepeatPattern> patterns, int[] demand, int[] available,
            int[] incumbent, double[] weights, CancellationToken token, Func<bool> expired, int nodeLimit)
        {
            if (demand.Sum() > 256 || expired()) return incumbent;
            var allowed = Enumerable.Range(0, patterns.Count).Where(i => PatternFits(patterns[i], demand, available) > 0).ToArray();
            if (allowed.Length == 0) return incumbent;
            var byType = new List<int>[demand.Length];
            for (int t = 0; t < demand.Length; t++) byType[t] = new List<int>();
            foreach (int i in allowed) foreach (var c in patterns[i].Counts) byType[c.Type].Add(i);
            double maxValue = allowed.Max(i => patterns[i].Counts.Sum(c => weights[c.Type] * c.Count));
            var needs = (int[])demand.Clone(); var racks = (int[])available.Clone();
            var path = new int[patterns.Count]; var best = (int[])incumbent.Clone(); int bestCount = best.Sum(), nodes = 0;
            var seen = new Dictionary<string, int>();
            void Search(int depth)
            {
                token.ThrowIfCancellationRequested();
                if (++nodes > nodeLimit || expired() || depth >= bestCount) return;
                double value = 0; int total = 0;
                for (int t = 0; t < needs.Length; t++) { value += weights[t] * needs[t]; total += needs[t]; }
                if (total == 0) { bestCount = depth; best = (int[])path.Clone(); return; }
                // Normalize the dual weights against EVERY allowed pattern: a
                // valid lower bound even if pricing or simplex hit a work limit.
                if (maxValue > 0 && depth + Math.Ceiling(value / maxValue - 1e-7) >= bestCount) return;
                string key = string.Join(",", needs) + "|" + string.Join(",", racks);
                if (seen.TryGetValue(key, out int oldDepth) && oldDepth <= depth) return;
                seen[key] = depth;
                List<int> choices = null;
                for (int t = 0; t < needs.Length; t++)
                {
                    if (needs[t] == 0) continue;
                    var fit = byType[t].Where(i => PatternFits(patterns[i], needs, racks) > 0).ToList();
                    if (fit.Count == 0) return;
                    if (choices == null || fit.Count < choices.Count) choices = fit;
                }
                foreach (int i in choices.OrderByDescending(i => patterns[i].Counts.Sum(c => weights[c.Type] * c.Count))
                    .ThenByDescending(i => patterns[i].Area).ThenBy(i => i))
                {
                    if (nodes >= nodeLimit || expired()) break;
                    ConsumePattern(patterns[i], 1, needs, racks); path[i]++;
                    Search(depth + 1);
                    path[i]--; ConsumePattern(patterns[i], -1, needs, racks);
                }
            }
            Search(0);
            return best;
        }
    }
}
