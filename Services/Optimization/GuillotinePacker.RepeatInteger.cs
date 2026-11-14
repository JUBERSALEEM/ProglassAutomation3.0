using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        private sealed class RepeatBranch
        {
            public Dictionary<int, int> Lower = new();
            public Dictionary<int, int> Upper = new();
            public int Depth;
            public double Bound;
        }

        private static int[] BranchRepeatMaster(List<RepeatPattern> patterns, int[] demand, int[] available,
            int[] incumbent, CancellationToken token, Func<bool> expired, int effort)
        {
            if (expired()) return incumbent;
            var active = Enumerable.Range(0, demand.Length).Where(i => demand[i] > 0).ToArray();
            var ids = Enumerable.Range(0, patterns.Count).Where(i => PatternFits(patterns[i], demand, available) > 0).ToArray();
            if (active.Length == 0 || ids.Length == 0) return incumbent;
            var map = active.Select((t, i) => (t, i)).ToDictionary(p => p.t, p => p.i);
            var pool = ids.Select(i => new RepeatPattern { Stock = patterns[i].Stock, Area = patterns[i].Area,
                Counts = patterns[i].Counts.Select(c => (map[c.Type], c.Count)).ToArray() }).ToList();
            var reduced = BranchRepeatMasterCore(pool, active.Select(i => demand[i]).ToArray(), available,
                ids.Select(i => incumbent[i]).ToArray(), token, expired, effort);
            var result = new int[patterns.Count];
            for (int i = 0; i < ids.Length; i++) result[ids[i]] = reduced[i];
            return result;
        }

        // Bounded integer master over pattern MULTIPLICITIES. A branch fixes a
        // count of a reusable pattern; it never branches once per expanded pane.
        // Each node starts with a feasible artificial/slack basis. Search limits
        // keep the incumbent, not a fractional/overproduced solution.
        private static int[] BranchRepeatMasterCore(List<RepeatPattern> patterns, int[] demand, int[] available,
            int[] incumbent, CancellationToken token, Func<bool> expired, int effort)
        {
            if (expired() || patterns.Count == 0) return incumbent;
            double penalty = (double)demand.Sum() + 1;
            double Score(int[] candidate)
            {
                var needs = (int[])demand.Clone(); var racks = (int[])available.Clone();
                for (int i = 0; i < candidate.Length; i++)
                {
                    if (candidate[i] < 0 || candidate[i] > PatternFits(patterns[i], needs, racks)) return double.PositiveInfinity;
                    ConsumePattern(patterns[i], candidate[i], needs, racks);
                }
                return candidate.Sum() + penalty * needs.Sum();
            }
            var best = incumbent; double bestScore = Score(best);
            var pending = new PriorityQueue<RepeatBranch, (int, double, int)>(); int serial = 0;
            pending.Enqueue(new RepeatBranch(), (0, 0, serial++));
            int nodes = 0, nodeLimit = 64 + Math.Clamp(effort, 1, 5) * 64;
            while (pending.Count > 0 && nodes++ < nodeLimit && !expired())
            {
                token.ThrowIfCancellationRequested();
                pending.TryDequeue(out var node, out var priority);
                if (Math.Ceiling(node.Bound - 1e-6) >= bestScore) continue;
                var needs = (int[])demand.Clone(); var racks = (int[])available.Clone();
                var forced = new int[patterns.Count]; bool feasible = true;
                foreach (var p in node.Lower)
                {
                    if (p.Value > PatternFits(patterns[p.Key], needs, racks)) { feasible = false; break; }
                    forced[p.Key] = p.Value; ConsumePattern(patterns[p.Key], p.Value, needs, racks);
                }
                if (!feasible) continue;
                var upper = new Dictionary<int, int>();
                foreach (var p in node.Upper)
                {
                    int bound = p.Value - forced[p.Key];
                    if (bound < 0) { feasible = false; break; }
                    upper.Add(p.Key, bound);
                }
                if (!feasible) continue;
                var lp = new RepeatMaster(needs, racks, penalty, token, expired, upper);
                for (int i = 0; i < patterns.Count; i++) lp.Add(patterns[i], i);
                lp.Solve();
                if (lp.Limited) continue; // unfinished primal is NOT a lower bound
                double lowerBound = forced.Sum() + lp.Objective;
                if (Math.Ceiling(lowerBound - 1e-6) >= bestScore) continue;
                var values = lp.PatternValues(patterns.Count);
                var candidate = (int[])forced.Clone();
                for (int i = 0; i < values.Length; i++)
                {
                    int times = (int)Math.Min(int.MaxValue, Math.Floor(values[i] + 1e-7));
                    times = Math.Min(times, PatternFits(patterns[i], needs, racks));
                    candidate[i] += times; ConsumePattern(patterns[i], times, needs, racks);
                }
                // Recover a feasible integer incumbent at every visited node.
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    int chosen = -1; double score = -1, fraction = -1; long area = -1;
                    for (int i = 0; i < patterns.Count; i++)
                    {
                        if (node.Upper.TryGetValue(i, out int cap) && candidate[i] >= cap) continue;
                        if (PatternFits(patterns[i], needs, racks) <= 0) continue;
                        double value = patterns[i].Counts.Sum(c => Math.Max(0, lp.Prices[c.Type]) * c.Count);
                        double f = values[i] - Math.Floor(values[i]);
                        if (value > score + 1e-8 || (Math.Abs(value - score) <= 1e-8 &&
                            (f > fraction + 1e-8 || (Math.Abs(f - fraction) <= 1e-8 && patterns[i].Area > area))))
                        { chosen = i; score = value; fraction = f; area = patterns[i].Area; }
                    }
                    if (chosen < 0) break;
                    int times = PatternFits(patterns[chosen], needs, racks);
                    if (node.Upper.TryGetValue(chosen, out int cap2)) times = Math.Min(times, cap2 - candidate[chosen]);
                    candidate[chosen] += times; ConsumePattern(patterns[chosen], times, needs, racks);
                }
                double candidateScore = Score(candidate);
                if (candidateScore < bestScore) { bestScore = candidateScore; best = candidate; }
                if (Math.Ceiling(lowerBound - 1e-6) >= bestScore || node.Depth >= 96) continue;
                int branch = -1; double fractionality = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    double f = values[i] - Math.Floor(values[i]); double distance = f > 1e-6 && f < 1 - 1e-6 ? f : 0;
                    if (distance > fractionality + 1e-7) { fractionality = distance; branch = i; }
                }
                if (branch < 0) continue;
                int down = (int)Math.Floor(values[branch] + forced[branch]);
                var left = new RepeatBranch { Lower = new Dictionary<int, int>(node.Lower), Upper = new Dictionary<int, int>(node.Upper), Depth = node.Depth + 1, Bound = lowerBound };
                left.Upper[branch] = down;
                var right = new RepeatBranch { Lower = new Dictionary<int, int>(node.Lower), Upper = new Dictionary<int, int>(node.Upper), Depth = node.Depth + 1, Bound = lowerBound };
                right.Lower[branch] = down + 1;
                pending.Enqueue(right, (-right.Depth, Math.Ceiling(lowerBound - 1e-6), serial++));
                pending.Enqueue(left, (-left.Depth, Math.Ceiling(lowerBound - 1e-6), serial++));
            }
            return best;
        }
    }
}
