using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        // Limits search work, NEVER input quantity. Large orders keep all panes
        // and apply the same search to a small set of their weakest sheets.
        private const int PatternPartWindow = 256;
        private const int PatternTailSheets = 6;

        private static IEnumerable<(int Width, OrderBy Order)> PatternCandidates(int firstLevel, int lastLevel)
        {
            if (firstLevel <= 1 && lastLevel >= 1)
            {
                yield return (4, OrderBy.MaxSide);
                yield return (8, OrderBy.MaxSide);
            }
            if (firstLevel <= 2 && lastLevel >= 2) yield return (12, OrderBy.Area);
            if (firstLevel <= 3 && lastLevel >= 3) yield return (16, OrderBy.MinSide);
            if (firstLevel <= 4 && lastLevel >= 4) yield return (24, OrderBy.Height);
            if (firstLevel <= 5 && lastLevel >= 5) yield return (32, OrderBy.Width);
        }

        private static OptimizationResult ImprovePatterns(OptimizationResult baseline,
            IReadOnlyList<CutPiece> pieces, IReadOnlyList<StockSize> stocks,
            MarginProfile margins, RotationPolicy rotation, int firstLevel, int lastLevel,
            CancellationToken token, Func<bool> budgetExhausted = null)
        {
            if (pieces.Count == 0) return baseline;
            token.ThrowIfCancellationRequested();
            if (budgetExhausted?.Invoke() == true) return baseline;
            bool local = pieces.Count > PatternPartWindow;
            var removed = new HashSet<SheetLayout>();
            var selectedUnplaced = new HashSet<CutPiece>();
            var work = new List<CutPiece>();
            var used = new int[stocks.Count];
            if (!local) work.AddRange(pieces);
            else
            {
                // Reserve some recovery space, without allowing a large shortage
                // list to crowd every existing sheet out of the repair window.
                foreach (var p in baseline.Unplaced.Take(PatternPartWindow / 4))
                { work.Add(p); selectedUnplaced.Add(p); }
                foreach (var sheet in baseline.Sheets.OrderBy(s => s.UtilizationPct).ThenBy(s => s.SheetNo))
                {
                    token.ThrowIfCancellationRequested();
                    if (removed.Count >= PatternTailSheets) break;
                    if (sheet.Pieces.Count + work.Count > PatternPartWindow) continue;
                    removed.Add(sheet);
                    work.AddRange(sheet.Pieces.Select(p => p.Piece));
                }
                if (work.Count == 0) return baseline;
                // Repacking may consume only inventory not committed outside
                // the window. Keep original StockSize references in the result.
                foreach (var sheet in baseline.Sheets.Where(s => !removed.Contains(s)))
                {
                    token.ThrowIfCancellationRequested();
                    for (int i = 0; i < stocks.Count; i++)
                        if (ReferenceEquals(stocks[i], sheet.Stock)) { used[i]++; break; }
                }
            }

            var best = baseline;
            foreach (var (width, order) in PatternCandidates(firstLevel, lastLevel))
            {
                token.ThrowIfCancellationRequested();
                if (budgetExhausted?.Invoke() == true) break;
                // At most window * beam state expansions, each considering the
                // three existing split directions. No whole-million-part beam.
                var candidate = RunBestSplitBeam(work, stocks, margins, rotation, width, token,
                    ordering: order, maxExpansions: PatternPartWindow * width,
                    consolidationSweeps: 3, initiallyUsed: used);
                candidate.Strategy = $"PATTERN BEAM ({order}, width {width}" + (local ? ", tail)" : ")");
                if (local)
                {
                    var combined = new OptimizationResult { Rotation = rotation, Strategy = candidate.Strategy };
                    // Clone sheet containers so renumbering cannot mutate the
                    // retained floor. PlacedPiece objects are never edited here.
                    foreach (var sheet in baseline.Sheets.Where(s => !removed.Contains(s)).Concat(candidate.Sheets))
                    {
                        token.ThrowIfCancellationRequested();
                        var copy = new SheetLayout { SheetNo = combined.Sheets.Count + 1, Stock = sheet.Stock,
                            UsableX = sheet.UsableX, UsableY = sheet.UsableY,
                            UsableWidth = sheet.UsableWidth, UsableHeight = sheet.UsableHeight };
                        copy.Pieces.AddRange(sheet.Pieces);
                        combined.Sheets.Add(copy);
                    }
                    combined.Unplaced.AddRange(baseline.Unplaced.Where(p => !selectedUnplaced.Contains(p)));
                    combined.Unplaced.AddRange(candidate.Unplaced);
                    candidate = combined;
                }
                best = Better(best, CheckCandidate(candidate, pieces, margins, token));
            }
            return best;
        }
    }
}
