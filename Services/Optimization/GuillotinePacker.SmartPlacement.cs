// ════════════════════════════════════════
//  FILE: Services/Optimization/GuillotinePacker.SmartPlacement.cs
//  PROJECT: ProGlassAutomation
//  MODULE: Optimization — cutting engine, smart placement policy (2026-09-17)
//
//  WHY THIS EXISTS
//  ---------------------------------------------------------------------
//  The engine already searches hard: six orderings × three split rules,
//  sheet-fill, wall-seed, MaxRects, per-sheet best split and a beam over it.
//  What it did NOT have is a better answer to the question each of those
//  strategies asks at every single placement:
//
//      "Given this piece and these free rectangles, which one do I use?"
//
//  The legacy score is LOCAL — exact-edge fit, then best-short-side-fit, then
//  leftover area, all measured inside the one rectangle being consumed. It
//  never asks what the placement leaves the SHEET capable of, which is the
//  defining question in guillotine nesting: a small piece dropped into the
//  largest free rectangle splits it into two medium ones, while the same piece
//  dropped into a rectangle it nearly fills leaves the large one intact. Both
//  can score identically, and the first sheet wins the tie.
//
//  Two further things were forced rather than decided:
//
//    • the guillotine split direction is fixed for an entire run, even though
//      whether the remainder should go right or down is a per-placement choice;
//    • MarginProfile.MinBreakoutMm — the narrowest offcut the floor can
//      actually cut — was editable in MARGINS, persisted with the session and
//      even drawn in the 2D preview, but no code path honoured it. Commit kept
//      every remainder above zero as usable stock, so a plan could call for
//      cutting a 4 mm sliver out of a sheet. Glass does not do that; it breaks.
//
//  WHAT THIS DOES
//  ---------------------------------------------------------------------
//  PackSmart scores a placement by what SURVIVES it:
//
//    1. least area forced into uncuttable trim (breakout honoured first);
//    2. then the largest surviving contiguous free rectangle, by area;
//    3. then that rectangle's shortest side — a long thin survivor is much
//       less useful than a squarer one of the same area;
//    4. then the legacy geometry (exact-edge fit, best short side) as a
//       tie-break, so where capacity is equal this behaves like the engine
//       the owner calibrated;
//    5. then orientation, rectangle index and position — a total order, so
//       the run is deterministic.
//
//  Both split directions are evaluated at every placement, and the caller's
//  SplitMode becomes a PREFERENCE used only as a tie-break rather than a
//  constraint applied to the whole job.
//
//  SAFETY — WHY THIS CANNOT REGRESS A RESULT
//  ---------------------------------------------------------------------
//  It is an additional candidate, never a replacement. PackOptimize feeds it
//  through Better(), which keeps the minimum on unplaced, then sheets, then
//  utilization: a candidate can only win by being better. Every benchmark pin
//  in OptimizationBenchmarkTests is on PackQuick, which is untouched — the
//  split maths it uses is the same function with minBreakout = 0, which
//  reproduces the previous behaviour exactly.
//
//  Placements are committed through the SAME Commit and the SAME SplitRemainder
//  as every other strategy, so the oracle's guarantees (no overlap, kerf
//  respected, inside the margin zone) hold by construction rather than by
//  parallel implementation.
//
//  COST
//  ---------------------------------------------------------------------
//  Roughly four split evaluations per (piece, rectangle, orientation) instead
//  of one, on a sheet-at-a-time pass. That is why it is gated: it runs at
//  effort 2 and above, once in the Large band, and not at all in the Huge band
//  where termination matters more than the last sheet.
// ════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        /// <summary>Written to <see cref="OptimizationResult.Strategy"/> so a
        /// multi-start run can say which of its candidates actually won.</summary>
        public const string SmartStrategyLabel = "SMART PLACEMENT";

        /// <summary>
        /// SMART PLACEMENT — capacity-preserving, breakout-aware, sheet-at-a-time.
        /// Fills one sheet completely before opening the next (industrial practice:
        /// "finish this plate before you start another"), choosing each placement
        /// by what it leaves the sheet capable of rather than by how tightly the
        /// piece fits the rectangle it consumes.
        /// </summary>
        /// <param name="split">A PREFERENCE, used only to break ties. Both split
        /// directions are evaluated at every placement regardless.</param>
        public static OptimizationResult PackSmart(
            IReadOnlyList<CutPiece> pieces,
            IReadOnlyList<StockSize> stocks,
            MarginProfile margins,
            RotationPolicy rotation,
            SplitMode split = SplitMode.HorizontalFirst,
            int consolidationSweeps = int.MaxValue,
            CancellationToken cancellationToken = default,
            IProgress<OptimizeProgress> progress = null,
            int candidateWindowSize = int.MaxValue,
            CutPolicy policy = null)
        {
            if (pieces == null) throw new ArgumentNullException(nameof(pieces));
            if (stocks == null || stocks.Count == 0)
                throw new ArgumentException("At least one stock size is required.", nameof(stocks));
            if (margins == null) throw new ArgumentNullException(nameof(margins));

            if (pieces.Count == 0)
                return new OptimizationResult { Rotation = rotation, Strategy = SmartStrategyLabel };

            int breakout = margins.MinBreakoutMm;
            var remaining = Sort(pieces, OrderBy.Area);
            var sheets = new List<SheetState>();
            var per = new int[stocks.Count];
            var unplaced = new List<CutPiece>();

            // Reused for the whole run: scoring evaluates four splits per
            // (piece, rectangle, orientation), and allocating two lists each time
            // would be the same O(parts x sheets) garbage the legacy scorer was
            // refactored away from.
            var kept = new List<FreeRect>(4);
            var unbounded = new List<FreeRect>(4);

            int totalPieces = remaining.Count;

            // Priority candidate window: at large scales, scanning the top K candidate parts
            // preserves placement quality while reducing complexity from O(N^2) to O(K*N).
            int scanLimit = candidateWindowSize > 0 && candidateWindowSize < int.MaxValue
                ? candidateWindowSize
                : (totalPieces > 2000 ? 400 : int.MaxValue);

            while (remaining.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var state = OpenBlank(stocks, per, remaining[0], margins, rotation, sheets.Count, policy);
                if (state == null && remaining.Count > 1)
                {
                    // The largest remaining piece can be bigger than any stock
                    // still within its cap while smaller ones fit fine. Giving up
                    // here would report the whole rest of the job as unplaced.
                    // Probing uses a COPY of the per-stock counters, because
                    // OpenBlank consumes quota when it succeeds and a probe must
                    // not spend stock it is not going to use.
                    int probeCount = Math.Min(remaining.Count, scanLimit);
                    for (int i = 1; i < probeCount && state == null; i++)
                    {
                        var probePer = (int[])per.Clone();
                        if (OpenBlank(stocks, probePer, remaining[i], margins, rotation, sheets.Count, policy) == null)
                            continue;
                        state = OpenBlank(stocks, per, remaining[i], margins, rotation, sheets.Count, policy);
                    }
                }

                if (state == null)
                {
                    unplaced.AddRange(remaining);
                    break;
                }

                sheets.Add(state);
                int placedOnThisSheet = 0;

                bool progressed = true;
                while (progressed)
                {
                    progressed = false;

                    int bestIdx = -1;
                    SmartOption bestOption = default;

                    int searchLimit = Math.Min(remaining.Count, scanLimit);
                    for (int i = 0; i < searchLimit; i++)
                    {
                        // Per part, so Cancel lands inside one placement decision
                        // rather than after a whole sheet.
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!TrySmartPlace(state, remaining[i], margins, rotation, split,
                                kept, unbounded, out SmartOption option))
                            continue;

                        if (bestIdx < 0 || Compare(option, bestOption, i, bestIdx) < 0)
                        {
                            bestIdx = i;
                            bestOption = option;
                        }
                    }

                    if (bestIdx < 0) break;

                    Commit(state, bestOption.Materialize(remaining[bestIdx]), margins,
                        bestOption.Split, breakout);
                    remaining.RemoveAt(bestIdx);
                    placedOnThisSheet++;
                    progressed = true;
                }

                // Insurance against a pathological loop: a sheet that opened but
                // accepted nothing would otherwise be re-opened forever. It cannot
                // happen — OpenBlank admits a piece exactly when the sheet's single
                // free rectangle can take it — but an infinite loop in a quoting
                // tool is worse than a defensive branch.
                if (placedOnThisSheet == 0)
                {
                    sheets.RemoveAt(sheets.Count - 1);
                    per[state.StockIndex] = Math.Max(0, per[state.StockIndex] - 1);
                    unplaced.AddRange(remaining);
                    break;
                }

                progress?.Report(OptimizeProgress.Of("smart",
                    totalPieces - remaining.Count, totalPieces,
                    string.Format(CultureInfo.InvariantCulture,
                        "Smart placement: {0} sheet(s) filled, {1} part(s) left",
                        sheets.Count, remaining.Count)));
            }

            // Consolidation still squeezes out the emptiest sheet — that is what
            // moves the sheet count — but it commits through the same breakout
            // rule, so it cannot reintroduce a sliver the placement phase refused.
            int sweeps = totalPieces > 20000 ? Math.Min(consolidationSweeps, 1) : consolidationSweeps;
            ConsolidateAll(sheets, margins, rotation, split, sweeps, breakout,
                cancellationToken);

            var result = FinishFromSheets(sheets, unplaced, rotation);
            result.Strategy = SmartStrategyLabel;
            return result;
        }

        /// <summary>
        /// One placement considered by <see cref="PackSmart"/>, scored by value so
        /// the winner is the only thing that allocates.
        /// </summary>
        internal struct SmartOption
        {
            public int X, Y, W, H;
            public bool Rotated;
            public SplitMode Split;
            /// <summary>Stock area this placement turns into uncuttable trim.</summary>
            public long LostToTrim;
            /// <summary>Area of the largest free rectangle that survives it.</summary>
            public long Capacity;
            /// <summary>Shortest side of that surviving rectangle.</summary>
            public long CapacitySide;
            /// <summary>Legacy geometry rank: 0 exact both edges, 1 one edge, 2 none.</summary>
            public int FitRank;
            /// <summary>Legacy best-short-side-fit value.</summary>
            public long ShortSide;
            /// <summary>0 when this split direction is the caller's preference.</summary>
            public int SplitPreference;

            public PlacedPiece Materialize(CutPiece piece) => new PlacedPiece
            {
                Piece = piece,
                X = X,
                Y = Y,
                WidthMm = W,
                HeightMm = H,
                Rotated = Rotated
            };
        }

        /// <summary>
        /// The total order over two placement options. Deliberately ends in
        /// piece index so that a full tie is resolved by input order: the same
        /// job always produces the same plan.
        /// </summary>
        private static int Compare(SmartOption a, SmartOption b, int indexA, int indexB)
        {
            int cmp = a.LostToTrim.CompareTo(b.LostToTrim);
            if (cmp == 0) cmp = b.Capacity.CompareTo(a.Capacity);           // larger is better
            if (cmp == 0) cmp = b.CapacitySide.CompareTo(a.CapacitySide);   // larger is better
            if (cmp == 0) cmp = a.FitRank.CompareTo(b.FitRank);
            if (cmp == 0) cmp = a.ShortSide.CompareTo(b.ShortSide);
            if (cmp == 0) cmp = a.SplitPreference.CompareTo(b.SplitPreference);
            if (cmp == 0) cmp = indexA.CompareTo(indexB);
            return cmp;
        }

        /// <summary>
        /// Best placement for one piece on one sheet under the smart policy:
        /// every free rectangle × every allowed orientation × BOTH split
        /// directions, scored by what survives. Returns false when the sheet
        /// cannot take the piece at all.
        /// </summary>
        private static bool TrySmartPlace(
            SheetState sheet, CutPiece piece, MarginProfile margins, RotationPolicy rotation,
            SplitMode preferredSplit, List<FreeRect> kept, List<FreeRect> unbounded,
            out SmartOption best)
        {
            best = default;

            int ow = piece.WidthMm, oh = piece.HeightMm;
            bool canRotate = rotation == RotationPolicy.Free90 && piece.AllowRotate && ow != oh;
            int orientationCount = canRotate ? 2 : 1;
            int kerf = margins.KerfMm;
            int breakout = margins.MinBreakoutMm;

            // Same sound whole-sheet prune the legacy scorer uses: MaxFreeW and
            // MaxFreeH are exact maxima over the free rectangles, so an
            // orientation exceeding them exceeds every rectangle.
            bool anyFits = false;
            for (int oi = 0; oi < orientationCount; oi++)
            {
                int wProbe = oi == 0 ? ow : oh;
                int hProbe = oi == 0 ? oh : ow;
                if (wProbe <= sheet.MaxFreeW && hProbe <= sheet.MaxFreeH) { anyFits = true; break; }
            }
            if (!anyFits) return false;

            bool found = false;
            int bestIndex = 0;   // rectangle index, for the deterministic tie-break

            for (int oi = 0; oi < orientationCount; oi++)
            {
                int w = oi == 0 ? ow : oh;
                int h = oi == 0 ? oh : ow;
                if (w > sheet.MaxFreeW || h > sheet.MaxFreeH) continue;

                for (int ri = 0; ri < sheet.Free.Count; ri++)
                {
                    FreeRect rect = sheet.Free[ri];
                    if (w > rect.W || h > rect.H) continue;

                    bool exactW = w == rect.W;
                    bool exactH = h == rect.H;
                    int fit = (exactW && exactH) ? 0 : (exactW || exactH) ? 1 : 2;
                    long shortSide = Math.Min(rect.W - w, rect.H - h);

                    for (int sd = 0; sd < 2; sd++)
                    {
                        SplitMode mode = sd == 0
                            ? SplitMode.HorizontalFirst
                            : SplitMode.VerticalFirst;

                        // PHASE 1: the same stage-budget guard TryPlaceCore
                        // applies. This scorer has its own rect loop, so it
                        // needs its own copy — otherwise it would happily pick
                        // a placement whose second cut has no stage left.
                        if (!StagedPlacementAllowed(rect, w, h, mode, sheet.Policy)) continue;

                        // What survives when the floor's minimum breakout is
                        // honoured, and what would have survived if it were not.
                        // The difference is the area this placement condemns to
                        // trim waste — the first thing the score minimises.
                        // The policy is passed to both so the score sees the
                        // same remainders the commit will actually produce.
                        kept.Clear();
                        SplitRemainder(rect, w, h, kerf, breakout, mode, kept, sheet.Policy);
                        unbounded.Clear();
                        SplitRemainder(rect, w, h, kerf, 0, mode, unbounded, sheet.Policy);

                        long keptArea = 0, capacity = 0, capacitySide = 0;
                        for (int k = 0; k < kept.Count; k++)
                        {
                            long area = (long)kept[k].W * kept[k].H;
                            keptArea += area;
                            if (area > capacity)
                            {
                                capacity = area;
                                capacitySide = Math.Min(kept[k].W, kept[k].H);
                            }
                        }

                        long unboundedArea = 0;
                        for (int u = 0; u < unbounded.Count; u++)
                            unboundedArea += (long)unbounded[u].W * unbounded[u].H;

                        var option = new SmartOption
                        {
                            X = rect.X,
                            Y = rect.Y,
                            W = w,
                            H = h,
                            Rotated = oi == 1,
                            Split = mode,
                            LostToTrim = unboundedArea - keptArea,
                            Capacity = capacity,
                            CapacitySide = capacitySide,
                            FitRank = fit,
                            ShortSide = shortSide,
                            SplitPreference = mode == preferredSplit ? 0 : 1
                        };

                        // Rectangle index stands in for the legacy scorer's
                        // positional tie-break; orientation is already ordered by
                        // the loop, and Compare() ends on the piece index.
                        if (!found || Compare(option, best, ri, bestIndex) < 0)
                        {
                            found = true;
                            best = option;
                            bestIndex = ri;
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Reports offcuts thinner than <see cref="MarginProfile.MinBreakoutMm"/>
        /// that a finished plan would ask the floor to cut.
        ///
        /// Kept OUT of <see cref="Verify"/> on purpose: Verify is the oracle that
        /// decides whether a layout is geometrically legal, and it runs on every
        /// group of every run. A thin offcut is not an illegal layout — the glass
        /// is simply wasted — and adding it there would turn every legacy plan
        /// into "N issue(s)" overnight, which reads as a regression rather than
        /// as the new information it is. This is the diagnostic that says the
        /// truth about a plan, and what PackSmart is built to eliminate.
        ///
        /// The blade consumes one kerf between two pieces, so the offcut is the
        /// gap MINUS the kerf: a gap of exactly kerf means the pieces are
        /// adjacent and nothing is cut out between them.
        /// </summary>
        public static List<string> AuditBreakout(OptimizationResult result, MarginProfile margins)
        {
            var problems = new List<string>();
            if (result == null || margins == null) return problems;

            int min = margins.MinBreakoutMm;
            if (min <= 0) return problems;
            int kerf = margins.KerfMm;

            foreach (var sheet in result.Sheets)
            {
                for (int i = 0; i < sheet.Pieces.Count; i++)
                {
                    for (int j = i + 1; j < sheet.Pieces.Count; j++)
                    {
                        var a = sheet.Pieces[i];
                        var b = sheet.Pieces[j];

                        bool xOverlap = a.X < b.X + b.WidthMm && b.X < a.X + a.WidthMm;
                        bool yOverlap = a.Y < b.Y + b.HeightMm && b.Y < a.Y + a.HeightMm;
                        if (xOverlap && yOverlap) continue;   // Verify's job, not ours

                        int gap;
                        if (xOverlap)
                            gap = Math.Max(b.Y - (a.Y + a.HeightMm), a.Y - (b.Y + b.HeightMm));
                        else if (yOverlap)
                            gap = Math.Max(b.X - (a.X + a.WidthMm), a.X - (b.X + b.WidthMm));
                        else
                            continue;   // diagonal neighbours leave no strip between them

                        int offcut = gap - kerf;
                        if (offcut <= 0 || offcut >= min) continue;

                        problems.Add(string.Format(CultureInfo.InvariantCulture,
                            "Sheet {0}: {1} leaves a {2} mm offcut against {3} — below the {4} mm minimum breakout.",
                            sheet.SheetNo, Label(a), offcut, Label(b), min));
                    }
                }
            }

            return problems;
        }
    }
}
