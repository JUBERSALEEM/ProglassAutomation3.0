// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/GuillotinePacker.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — cutting engine (design 2026-09-12)
//
// CALIBRATED against the owner's real DGU benchmark (Low-E / Clear /
// Ramly job): with 15mm margins, kerf 3, adaptive 0/90 rotation the
// engine reproduces the reference result — Low-E 17 sheets @ 82.77%,
// Clear 16 sheets @ 84.92%, Ramly 1 sheet on 2500×1875.
//
// Stages
//   PackQuick    — deterministic portfolio: area-first shelf, capacity strips and bounded pattern beam,
//                  horizontal-first guillotine splits, adaptive
//                  rotation, consolidation.
//   PackOptimize — multi-start effort levels (orderings × split modes,
//                  plus per-sheet split selection and a beam over it —
//                  see GuillotinePacker.BestSplit.cs — and the smart
//                  placement policy, see GuillotinePacker.SmartPlacement.cs),
//                  each consolidated, best run wins by sheets then
//                  utilization. Level = iteration budget.
//   PlatePattern — optional, default Yield. FillFirstPlate is applied
//                  after that winner (GuillotinePacker.FillFirstPlate.cs)
//                  and cannot replace it with more sheets or unplaced pieces.
//   PackSmart    — one capacity-preserving pass: every placement is scored
//                  by what SURVIVES it (least area condemned to sub-breakout
//                  trim, then the largest remaining free rectangle), both
//                  split directions are evaluated per placement instead of
//                  being fixed for the run, and MarginProfile.MinBreakoutMm
//                  is honoured. Runs as an OPTIMIZE candidate at effort 2+,
//                  so it can only improve the chosen plan. Public, so the
//                  oracle tests can hold it to the same guarantees.
//
// A note on "utilization": total glass area is fixed by the order, so
// utilization is purely a function of SHEET COUNT. Reducing sheets by one
// is the only thing that moves the number — which is why every strategy
// here is scored on sheets first.
//
// Guarantees (pinned by oracle tests via Verify):
//   • pieces never overlap — kerf gap enforced between neighbours
//   • pieces stay inside the usable rectangle (stock − LM/RM/TM/BM)
//   • rotation only when the policy and the individual pane allow it
//   • deterministic — same input, same layout
//   • smart placement never offers stock narrower than the minimum
//     breakout, so it cannot ask the floor to cut an uncuttable sliver
//     (AuditBreakout reports any that a legacy plan contains)
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        // Internal, not private: GuillotinePacker.SmartPlacement.cs shares the
        // split maths below, and the test assembly has InternalsVisibleTo.
        internal struct FreeRect
        {
            public int X, Y, W, H;

            /// <summary>
            /// Which cut stage PRODUCED this rectangle: 0 for the raw usable
            /// sheet, 1 for a strip separated by the sheet's first cut, and so
            /// on. A rect at depth d can host a placement whose first cut is
            /// stage d+1 — that is the rule staged patterns are enforced with.
            ///
            /// The four-argument constructor is kept and defaults to 0, so
            /// every pre-existing construction site is unchanged.
            /// </summary>
            public int Depth;

            public FreeRect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; Depth = 0; }
            public FreeRect(int x, int y, int w, int h, int depth) { X = x; Y = y; W = w; H = h; Depth = depth; }
        }

        private sealed class SheetState
        {
            public SheetLayout Layout { get; }
            public List<FreeRect> Free { get; } = new();
            public int StockIndex { get; }

            // ── SCALE METRICS ────────────────────────────────────────────
            // Maintained at every mutation of Free / Layout.Pieces, of which
            // there are exactly four: this constructor, Commit, the
            // consolidation rollback, and the MaxRects commit in
            // GuillotinePacker.Advanced.cs. Keeping them EXACT is what makes
            // the TryPlace prune result-identical rather than approximate.

            /// <summary>Sealed by the open-sheet window: never re-scanned
            /// for placement. Only set when a window binds (large tiers).</summary>
            public bool IsClosed { get; set; }
            /// <summary>Removed by consolidation (tombstone so the window
            /// queue can skip dead entries in amortized O(1)).</summary>
            public bool IsRemoved { get; set; }

            /// <summary>Largest width among this sheet's free rects.</summary>
            public int MaxFreeW;
            /// <summary>Largest height among this sheet's free rects.</summary>
            public int MaxFreeH;
            /// <summary>Total placed glass area in mm^2, cached so sorting and
            /// consolidation stop recomputing a LINQ Sum per comparison.</summary>
            public long PlacedArea;

            /// <summary>
            /// Cut-complexity policy this sheet is packed under. Read by Commit
            /// (which splits the remainder) and by TryPlaceCore (which decides
            /// whether a placement is legal), so the policy never has to be
            /// threaded through every helper — it rides on the sheet.
            /// </summary>
            public CutPolicy Policy { get; }

            public SheetState(SheetLayout layout, int stockIndex, CutPolicy policy = null)
            {
                Layout = layout;
                StockIndex = stockIndex;
                Policy = policy ?? CutPolicy.Free;
                Free.Add(new FreeRect(layout.UsableX, layout.UsableY,
                    layout.UsableWidth, layout.UsableHeight));
                RefreshAll();
            }

            /// <summary>Recompute the free-rect bounds. O(|Free|), and a
            /// guillotine split keeps only a handful of rects.</summary>
            public void RefreshFreeBounds()
            {
                int mw = 0, mh = 0;
                for (int i = 0; i < Free.Count; i++)
                {
                    if (Free[i].W > mw) mw = Free[i].W;
                    if (Free[i].H > mh) mh = Free[i].H;
                }
                MaxFreeW = mw;
                MaxFreeH = mh;
            }

            /// <summary>Recompute bounds AND area. Use after a bulk rebuild of
            /// either list (construction, rollback, MaxRects commit).</summary>
            public void RefreshAll()
            {
                RefreshFreeBounds();
                long a = 0;
                var ps = Layout.Pieces;
                for (int i = 0; i < ps.Count; i++) a += (long)ps[i].WidthMm * ps[i].HeightMm;
                PlacedArea = a;
            }
        }

        /// <summary>
        /// Bounded open-sheet window — the industrial "active plates"
        /// pattern. A pass keeps at most <see cref="OpenWindow"/> sheets
        /// open for placement; opening beyond that seals the OLDEST open
        /// sheet (it already received every placement attempt since it
        /// opened, so it is near-final — same idea as completing one
        /// plate before starting another). Per-piece scan cost drops from
        /// O(all sheets) to O(window): a 30k-part job scans ~128 sheets
        /// per piece instead of ~7,000. FIFO order is deterministic, so
        /// runs stay reproducible. int.MaxValue = unbounded (FULL tier).
        /// </summary>
        private sealed class PackingContext
        {
            public int OpenWindow = int.MaxValue;
            public int OpenCount;
            public readonly Queue<SheetState> OpenQueue = new();
        }

        /// <summary>Registers a freshly opened sheet; seals the oldest
        /// open sheet when the window is full (amortized O(1)).</summary>
        private static void RegisterOpenedSheet(PackingContext ctx, SheetState state)
        {
            if (ctx.OpenCount >= ctx.OpenWindow)
                CloseOldestOpenSheet(ctx);
            else
                ctx.OpenCount++;
            ctx.OpenQueue.Enqueue(state);
        }

        private static void CloseOldestOpenSheet(PackingContext ctx)
        {
            while (ctx.OpenQueue.Count > 0)
            {
                var s = ctx.OpenQueue.Dequeue();
                if (s.IsRemoved || s.IsClosed) continue;
                s.IsClosed = true;
                return;
            }
        }

        /// <summary>QUICK stage — a fixed shelf/strip/bounded-pattern portfolio.</summary>
        /// <param name="progress">Optional per-part progress. QUICK reported
        /// nothing at all until 2026-09-17, so the bar sat pinned at 0% for the
        /// whole run and then jumped to 100% — and because a Step 5 quote is
        /// usually ONE cutting group, the operator watched a frozen bar for the
        /// entire cut. Reporting is purely observational: the pass below is
        /// identical with or without it, which is what keeps the owner's
        /// benchmark figures (Low-E 17 @ 82.77%) exact.</param>
        public static OptimizationResult PackQuick(
            IReadOnlyList<CutPiece> pieces,
            IReadOnlyList<StockSize> stocks,
            MarginProfile margins,
            RotationPolicy rotation,
            SplitMode split = SplitMode.HorizontalFirst,
            CancellationToken cancellationToken = default,
            IProgress<OptimizeProgress> progress = null,
            PlatePattern platePattern = PlatePattern.Yield,
            int fillEffort = 1,
            CutPolicy policy = null,
            bool reuseOffcuts = false,
            bool localSearch = false,
            bool enableSwaps = false)
        {
            if (pieces == null) throw new ArgumentNullException(nameof(pieces));
            if (stocks == null || stocks.Count == 0)
                throw new ArgumentException("At least one stock size is required.", nameof(stocks));
            if (margins == null) throw new ArgumentNullException(nameof(margins));
            cancellationToken.ThrowIfCancellationRequested();

            if (pieces.Count >= IndustrialQuantityThreshold)
            {
                var industrialPlan = NoteFillSkipped(
                    PackIndustrialPatterns(pieces, stocks, margins, rotation, 1, cancellationToken, progress),
                    platePattern);
                if (policy != null && policy.IsDepthConstrained)
                    industrialPlan.ScaleNotes.Add(IndustrialStagedUnsupportedNote);
                return industrialPlan;
            }

            // 2026-09-16: was OrderBy.MaxSide, which cost a whole sheet on the
            // owner's own benchmark. MaxSide packs the Low-E group into 18
            // sheets @ 78.18%; Area reaches 17 @ 82.77% — the reference figure
            // the file header claims and Benchmark_LowE_MatchesReference pins.
            // Area is also identical on Clear (17) and Ramly (1 sheet, smallest
            // stock), so it is a strict improvement for the QUICK stage.
            // Keep the old shelf candidate and its scale limits. Add two
            // bounded capacity-strip and pattern-beam candidates below; QUICK
            // remains a fixed portfolio rather than a full deep search.
            var plan = OptimizationSearchGovernor.PlanForQuick(pieces.Count);
            var quickWatch = System.Diagnostics.Stopwatch.StartNew();
            int window = plan.OpenSheetWindow;
            var result = RunPass(Sort(pieces, OrderBy.Area), stocks, margins, rotation, split,
                cancellationToken, plan.MaxConsolidationSweeps,
                progress == null ? null : new QuickShelfProgress(progress), window,
                policy: policy);
            // Name the pass it actually was. The generic "GRID (...)" default
            // from RunPass would obscure which QUICK candidate actually won.
            result.Strategy = $"QUICK SHELF (Area / {split})";

            result = CheckCandidate(result, pieces, margins, cancellationToken);

            // PHASE 1: capacity strips and the best-split beam both build their
            // own free rectangles and know nothing about the stage budget, so a
            // staged pattern is served by the policy-aware shelf pass alone.
            // Skipping candidates can only cost yield, never correctness.
            bool staged = policy != null && policy.IsDepthConstrained;

            if (!staged)
            {
                foreach (bool transpose in new[] { false, true })
                {
                    progress?.Report(OptimizeProgress.Of("capacity", transpose ? 80 : 60, 100,
                        transpose ? "Capacity-aware row strips" : "Capacity-aware column strips"));
                    var strips = RunCapacityStrips(pieces, stocks, margins, rotation, transpose, cancellationToken);
                    result = Better(result, CheckCandidate(strips, pieces, margins, cancellationToken), platePattern);
                }
                progress?.Report(OptimizeProgress.Of("pattern", 90, 100,
                    "Bounded Best-Split pattern search (beam 4 + 8)"));
                result = ImprovePatterns(result, pieces, stocks, margins, rotation, 1, 1,
                    cancellationToken, () => quickWatch.Elapsed >= plan.TimeBudget);
            }
            progress?.Report(OptimizeProgress.Of("repeat", 95, 100,
                "Balancing reusable patterns against whole-order quantities"));
            if (!staged)
                result = Better(result, CheckCandidate(RunRepeatedPatterns(pieces, stocks, margins, rotation, 1,
                    cancellationToken), pieces, margins, cancellationToken), platePattern);
            if (quickWatch.Elapsed >= plan.TimeBudget)
                result.ScaleNotes.Add("QUICK pattern budget reached; retained the best validated candidate found.");
            result = ApplyPlatePattern(result, pieces, stocks, margins, rotation,
                fillEffort, platePattern, cancellationToken, progress, policy);

            // PHASE 0: give every sheet a cut sequence. Paths that stamp
            // pieces straight onto a layout leave it without one, and a plan
            // nobody can cut is not a plan.
            for (int i = 0; i < result.Sheets.Count; i++)
            {
                var sheet = result.Sheets[i];
                if (sheet.Cuts.Count != 0) continue;

                // The return value used to be discarded (finding F1). That
                // was worse than a bad plan: a failed derivation leaves the
                // sheet with an EMPTY cut list, and the consistency audit
                // deliberately skips empty sheets so it does not double
                // report them. So the one plan that cannot be cut at all was
                // the only one that said nothing. Rated a warning rather
                // than an error: it is reported here, not refused, so the
                // operator decides with the sheet in front of them.
                if (!TryDeriveGuillotineCuts(sheet) && sheet.Pieces.Count > 0)
                    result.ScaleNotes.Add(
                        "WARNING: sheet " + sheet.SheetNo + " holds "
                        + sheet.Pieces.Count + " pane(s) but no sequence of "
                        + "straight cuts produces that layout. It cannot be cut "
                        + "as drawn - open it in the 2D VIEW before exporting.");
            }

            // PHASE 2 part 2: use the offcuts before reporting them. Off by
            // default, so the default run is untouched.
            if (reuseOffcuts)
                NoteOffcutReuse(result, ConsolidateIntoOffcuts(result, pieces, margins, rotation));

            // PHASE E2: placement-level local search. Off by default, and the
            // swap move has its own switch so relocation and swapping can be
            // measured separately - combined, neither can be attributed.
            if (localSearch)
                NoteLocalSearch(result, LocalSearch(result, pieces, margins, rotation, enableSwaps));

            // PHASE 2: register what the plan leaves behind. Derived from the
            // cut sequence, so it reads the finished plan and cannot move a
            // single piece. Anything at least as big as the smallest pane the
            // run allows is flagged worth racking.
            DeriveLeftovers(result, Math.Max(margins.MinBreakoutMm, 1), margins.KerfMm);

            // PHASE E1: measure the finished plan. Last, so the cut sequences
            // exist and the reachability audit reads a settled result.
            OptimizationInstrumentation.Instrument(result, stocks, margins, rotation);

            // A staged plan that does not honour its own pattern is as useless
            // as one that loses pieces, so it is reported the same way.
            if (staged)
            {
                var cutProblems = ValidateCutPattern(result, policy);
                if (cutProblems.Count > 0)
                {
                    result.ValidationProblems.AddRange(cutProblems);
                    result.ScaleNotes.Add(
                        "CUT PATTERN: the requested staged pattern was not fully held — " +
                        "inspection only, not exportable.");
                }
            }

            if (result.HasValidationFailure)
                result.ScaleNotes.Add("WARNING: no valid QUICK candidate — inspection only, not exportable.");
            progress?.Report(OptimizeProgress.Of("done", 100, 100,
                $"{result.Sheets.Count} sheet(s) cut · {result.Unplaced.Count} unplaced"));
            return result;
        }

        /// <summary>
        /// OPTIMIZE stage — effort levels 1..5. Each level widens the
        /// multi-start search (orderings × split modes); every candidate
        /// run is consolidated and the best wins (fewest sheets, then
        /// highest utilization). Deterministic.
        /// </summary>
        public static OptimizationResult PackOptimize(
            IReadOnlyList<CutPiece> pieces,
            IReadOnlyList<StockSize> stocks,
            MarginProfile margins,
            RotationPolicy rotation,
            int effortLevel = 3,
            CancellationToken cancellationToken = default,
            IProgress<OptimizeProgress> progress = null,
            PlatePattern platePattern = PlatePattern.Yield,
            CutPolicy policy = null,
            int maxStage = 0,
            bool reuseOffcuts = false,
            bool localSearch = false,
            bool enableSwaps = false,
            bool remainderAware = false,
            bool wideBeam = false,
            bool parallelEval = false,
            bool requireGuillotine = false)
        {
            if (pieces == null) throw new ArgumentNullException(nameof(pieces));
            if (stocks == null || stocks.Count == 0)
                throw new ArgumentException("At least one stock size is required.", nameof(stocks));
            if (margins == null) throw new ArgumentNullException(nameof(margins));

            if (pieces.Count >= IndustrialQuantityThreshold)
            {
                var industrialPlan = NoteFillSkipped(
                    PackIndustrialPatterns(pieces, stocks, margins, rotation, effortLevel, cancellationToken, progress),
                    platePattern);
                if (policy != null && policy.IsDepthConstrained)
                    industrialPlan.ScaleNotes.Add(IndustrialStagedUnsupportedNote);
                return industrialPlan;
            }

            // PHASE 5a — two axes, kept apart.
            //   stage  = WHICH engines and orderings run (the portfolio).
            //   effort = HOW HARD they search (time budget, shuffles, beam,
            //            consolidation sweeps).
            // They were the same number, so "search harder" also silently meant
            // "run a different portfolio". maxStage = 0 means "let effort decide
            // both", which is exactly what this method has always done — every
            // caller that does not opt in stays bit-for-bit identical.
            int stage = maxStage > 0 ? maxStage : effortLevel;

            if (stage <= 1)
                return PackQuick(pieces, stocks, margins, rotation,
                    cancellationToken: cancellationToken, progress: progress,
                    platePattern: platePattern, fillEffort: 1);

            var plan = OptimizationSearchGovernor.PlanForOptimize(pieces.Count, effortLevel);
            var scale = ScaleProfile.For(pieces.Count, effortLevel);

            // PHASE 8 / finding 8b (2026-09-20): the search now has a deadline.
            // Every candidate boundary checks it, and when it engages the search
            // STOPS ADDING CANDIDATES AND KEEPS THE BEST PLAN FOUND SO FAR —
            // Better() only ever replaces best with something strictly better, so
            // truncating the sequence can never make the returned plan worse than
            // one already in hand. Cancellation still throws; the budget does not,
            // because a budget expiry is a normal outcome with a usable result,
            // not a failure.
            var budgetWatch = System.Diagnostics.Stopwatch.StartNew();
            bool budgetStopped = false;
            bool BudgetExhausted() => budgetWatch.Elapsed >= plan.TimeBudget;

            if (pieces.Count == 0)
                return new OptimizationResult { Rotation = rotation };

            // LiSEC / Editway-style multi-start: several decreasing
            // orderings × guillotine split rules. Higher effort adds
            // deterministic shuffles (reproducible, not random).
            var orderings = new[]
            {
                OrderBy.MaxSide, OrderBy.Area, OrderBy.MinSide,
                OrderBy.Height, OrderBy.Width, OrderBy.Perimeter
            };
            var splits = new[]
            {
                SplitMode.HorizontalFirst, SplitMode.VerticalFirst, SplitMode.LongerLeftover
            };

            IEnumerable<(OrderBy o, SplitMode s, int shuffle)> combos;
            if (stage <= 1)
                // Must mirror PackQuick exactly: Benchmark_OptimizeStage_
                // NeverRegressesQuick asserts OPTIMIZE <= QUICK.
                combos = new[] { (OrderBy.Area, SplitMode.HorizontalFirst, 0) };
            else if (stage == 2)
                combos = orderings.Take(3).Select(o => (o, SplitMode.HorizontalFirst, 0));
            else if (stage == 3)
                combos = orderings.Take(4).SelectMany(o => splits.Select(s => (o, s, 0)));
            else
            {
                int shuffles = stage >= 5 ? 6 : 3;
                shuffles = System.Math.Min(shuffles, plan.ShuffleCap);
                combos = orderings.SelectMany(o => splits.SelectMany(s =>
                    Enumerable.Range(0, shuffles + 1).Select(k => (o, s, k))));
            }

            // Materialized so progress can report a real total.
            var comboList = combos.ToList();

            // SMART PLACEMENT adds one or two more complete plans to the search
            // (see GuillotinePacker.SmartPlacement.cs). Counted up front so the
            // bar keeps moving forward instead of reaching 100% and then dropping
            // back while the last candidates run. Zero at effort 1, so the total
            // is exactly what it was before and QUICK-equivalent runs are
            // unaffected.
            int smartPasses = stage >= 2 && scale.Tier != ScaleTier.Huge
                ? (effortLevel >= 4 && scale.Tier == ScaleTier.Standard ? 2 : 1)
                : 0;
            int smartTotal = comboList.Count + smartPasses;

            // ── SCALE FIX 4: cache the base sorts ──
            // Sort(pieces, order) is a pure function of the input and the
            // ordering key, yet effort 5 re-ran it for all 126 combos. There are
            // only six distinct keys, so at 100k parts this was ~120 redundant
            // full sorts (each an O(n log n) LINQ chain allocating a new list).
            // ShuffleDeterministic copies its input before permuting it, so
            // handing out the cached list is safe.
            var sortCache = new Dictionary<OrderBy, List<CutPiece>>();

            // Every level includes the same validated QUICK portfolio. Deeper
            // search can improve it but cannot replace it with an invalid plan.
            // PHASE 1: same rule as QUICK — the beam and the repeated-pattern
            // pass build their own rectangles and cannot honour a stage budget.
            bool staged = policy != null && policy.IsDepthConstrained;

            int guillotineRejects = 0;

            OptimizationResult best = PackQuick(pieces, stocks, margins, rotation,
                cancellationToken: cancellationToken, policy: policy);
            if (!staged)
            {
                var improved = ImprovePatterns(best, pieces, stocks, margins, rotation, 2, effortLevel,
                    cancellationToken, BudgetExhausted);
                if (Accepts(improved)) best = improved;
                if (!BudgetExhausted())
                {
                    var repeated = CheckCandidate(RunRepeatedPatterns(pieces, stocks, margins,
                        rotation, effortLevel, cancellationToken), pieces, margins, cancellationToken);
                    if (Accepts(repeated)) best = Better(best, repeated, platePattern);
                }
            }

            // PHASE E4.1 — A PLAN THAT CANNOT BE CUT IS NOT A PLAN.
            //
            // Finding F1: RunMaxRects is documented as non-guillotine, it runs
            // by default at effort 3 or deeper, and nothing checked the
            // consequence. TryDeriveGuillotineCuts leaves a sheet with an EMPTY
            // cut list when no sequence of straight cuts produces its layout,
            // and the call site at the end of this method discarded that
            // result. So a non-guillotine candidate could win the comparison
            // and ship as a plan with pieces on it and no way to cut them out.
            //
            // The fix is a REJECTION AT THE CANDIDATE BOUNDARY, which is the
            // only place a policy like this belongs: a plan is either
            // admissible or it is not considered. It is deliberately not a
            // repair — there is no way to nudge a pinwheel into a guillotine
            // layout without moving panes, and moving panes mid-search is what
            // turns a packer into something nobody can reason about.
            //
            // Two things make this safe by construction rather than by hope:
            //   - best is seeded from PackQuick, which is guillotine by
            //     construction, so rejecting every candidate still leaves the
            //     default QUICK plan standing. This switch can never produce
            //     an invalid layout and can never lose a pane.
            //   - the check derives cuts as a side effect, so an accepted
            //     candidate arrives at the export path already cuttable.
            //
            // It is OFF BY DEFAULT because it changes results: an uncuttable
            // plan that used one sheet fewer will now be refused. That is the
            // whole point, and it is why it is a decision for the operator
            // rather than something to slip into the default path.
            bool Accepts(OptimizationResult candidate)
            {
                if (!requireGuillotine) return true;
                if (AllSheetsCuttable(candidate)) return true;
                guillotineRejects++;
                return false;
            }

            void Consider(OptimizationResult candidate)
            {
                var next = Better(best, CheckCandidate(candidate, pieces, margins, cancellationToken),
                    platePattern);

                // Nothing changed, or there was nothing to compare against.
                if (next == null || ReferenceEquals(next, best)) { best = next; return; }

                // It won the comparison, so it is the plan that would ship.
                // Refuse it here rather than repair it: best is left standing,
                // which is the PackQuick seed (guillotine by construction) or
                // an earlier admissible winner. Checking only the winner keeps
                // the derivation cost proportional to the number of times the
                // plan actually improves, not to the size of the pool.
                if (!Accepts(next)) return;
                best = next;
            }
            // PHASE E3.1 — EVALUATE THE POOL IN PARALLEL.
            //
            // Multi-start is embarrassingly parallel, and the reduction is a
            // deterministic minimum over a total order, so the winner is the
            // same plan the sequential loop would have produced — provided the
            // search budget does not bind. When it binds it CHANGES the plan:
            // the sequential loop stops adding candidates part-way through,
            // this one finishes all of them. That is a better plan more
            // slowly, which is a trade worth making deliberately, hence the
            // switch.
            //
            // Two shared things had to be made safe first:
            //   sortCache is a Dictionary written on first use. A concurrent
            //   write can corrupt it, so every ordering is prepared up front,
            //   on one thread, and the parallel section only reads it.
            //   progress is not marshalled across threads, so the parallel
            //   section reports nothing and the bar moves once at the end.
            //
            // THREAD-SAFETY OF RunPass ITSELF IS REASONED, NOT MEASURED. It
            // takes its own piece list, treats stocks and margins as
            // read-only, and keeps no state between calls — but that was
            // established by reading it, and this environment cannot compile
            // or run the suite. The switch exists so it can be validated on a
            // machine that can.
            void RunCombosParallel()
            {
                var orderedBy = new List<CutPiece>[comboList.Count];
                for (int ci = 0; ci < comboList.Count; ci++)
                {
                    var (order, split, shuffle) = comboList[ci];
                    if (!sortCache.TryGetValue(order, out var ordered))
                    {
                        ordered = Sort(pieces, order);
                        sortCache[order] = ordered;
                    }
                    orderedBy[ci] = shuffle > 0
                        ? ShuffleDeterministic(ordered, 17 + shuffle * 131 + (int)order * 7)
                        : ordered;
                }

                var results = new OptimizationResult[comboList.Count];
                System.Threading.Tasks.Parallel.For(0, comboList.Count,
                    new System.Threading.Tasks.ParallelOptions { CancellationToken = cancellationToken },
                    ci =>
                    {
                        var (order, split, shuffle) = comboList[ci];
                        results[ci] = RunPass(orderedBy[ci], stocks, margins, rotation, split,
                            cancellationToken, plan.MaxConsolidationSweeps, null,
                            plan.OpenSheetWindow, policy: policy);
                    });

                // Fold in candidate order. Better() is a deterministic total
                // preorder, so folding in a fixed order reaches the same
                // winner the sequential loop would have reached.
                for (int ci = 0; ci < results.Length; ci++)
                {
                    var candidate = results[ci];
                    if (candidate == null) continue;

                    var (order, split, shuffle) = comboList[ci];
                    candidate.Strategy = $"GRID ({order} / {split}"
                        + (shuffle > 0 ? $" / shuffle {shuffle}" : "") + ")";
                    Consider(candidate);
                }

                progress?.Report(OptimizeProgress.Of("pass", comboList.Count, smartTotal,
                    $"Evaluated {comboList.Count} candidates in parallel"));
            }

            if (parallelEval && comboList.Count > 1)
                RunCombosParallel();
            else
            {
                for (int ci = 0; ci < comboList.Count; ci++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Checked at the candidate boundary, never mid-placement: a plan
                    // half-built is worth nothing, a plan completed and then stopped
                    // is worth exactly what it always was.
                    if (BudgetExhausted())
                    {
                        budgetStopped = true;
                        break;
                    }

                    var (order, split, shuffle) = comboList[ci];

                    if (!sortCache.TryGetValue(order, out var ordered))
                    {
                        ordered = Sort(pieces, order);
                        sortCache[order] = ordered;
                    }
                    if (shuffle > 0)
                        ordered = ShuffleDeterministic(ordered, 17 + shuffle * 131 + (int)order * 7);

                    progress?.Report(OptimizeProgress.Of("pass", ci, smartTotal,
                        $"Pass {ci + 1} of {comboList.Count}: {order} / {split}" +
                        (shuffle > 0 ? $" / shuffle {shuffle}" : "")));

                    // A single-candidate search (effort 1) has no candidate
                    // sequence to drive the bar, so hand the sink to the pass
                    // itself: real per-part progress instead of one 0% → 100%
                    // jump. Multi-candidate runs keep the candidate-level reports
                    // above, which are already smooth, and pay no per-part cost.
                    int window = plan.OpenSheetWindow;
                    var candidate = RunPass(ordered, stocks, margins, rotation, split,
                        cancellationToken, plan.MaxConsolidationSweeps,
                        comboList.Count == 1 ? progress : null, window, policy: policy);
                    // Tag with the EXACT candidate this plan came from — the
                    // ordering and (for effort 4/5) the deterministic shuffle
                    // index. RunPass's default "GRID ({split})" hides the
                    // ordering, so a winning shuffled pass was indistinguishable
                    // from the plain one in the "Strategy breakdown" line.
                    //
                    // Effort 1 runs exactly the QUICK pass (that is the contract
                    // Benchmark_OptimizeStage_NeverRegressesQuick is built on),
                    // so it carries the same label QUICK does — a one-candidate
                    // run is not a grid.
                    candidate.Strategy = comboList.Count == 1
                        ? $"QUICK SHELF ({order} / {split})"
                        : $"GRID ({order} / {split}" +
                          (shuffle > 0 ? $" / shuffle {shuffle}" : "") + ")";
                    Consider(candidate);
                }
            }

            // With windowed candidate scanning, advanced strategies (BestSplit, SheetFill,
            // WallSeed, SmartPlacement) run in bounded O(K * N) time across all tiers.
            if (stage >= 2 && !budgetStopped)
            {
                int windowSize = scale.Tier == ScaleTier.Huge ? 300 : scale.Tier == ScaleTier.Large ? 400 : int.MaxValue;

                foreach (var split in splits)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Consider(RunSheetFill(pieces, stocks, margins, rotation, split,
                        scale.ConsolidationSweeps, cancellationToken, windowSize));
                    Consider(RunWallSeed(pieces, stocks, margins, rotation, split,
                        scale.ConsolidationSweeps, cancellationToken, windowSize));
                }
                cancellationToken.ThrowIfCancellationRequested();

                // Per-sheet split selection — see GuillotinePacker.BestSplit.cs.
                // This is the candidate that takes the Clear group from 17
                // sheets to the owner's reference 16.
                Consider(RunBestSplit(pieces, stocks, margins, rotation,
                    scale.ConsolidationSweeps, cancellationToken, windowSize,
                    remainderAware: remainderAware));

                // ── SMART PLACEMENT ──────────────────────────
                // Capacity-preserving placement, per-placement split choice and
                // the configured minimum breakout enforced. Added as a candidate,
                // never as a replacement: Better() keeps the minimum on unplaced,
                // then sheets, then utilization, so this can only improve the
                // chosen plan or lose. Enabled across all tiers using windowed candidate scanning.
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(OptimizeProgress.Of("smart", comboList.Count, smartTotal,
                    "Smart placement: capacity-preserving, breakout-aware"));
                Consider(PackSmart(pieces, stocks, margins, rotation,
                    SplitMode.HorizontalFirst, scale.ConsolidationSweeps, cancellationToken,
                    progress: null, candidateWindowSize: windowSize, policy: policy));

                if (stage >= 4)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(OptimizeProgress.Of("smart", comboList.Count + 1, smartTotal,
                        "Smart placement: vertical-split preference"));
                    Consider(PackSmart(pieces, stocks, margins, rotation,
                        SplitMode.VerticalFirst, scale.ConsolidationSweeps, cancellationToken,
                        progress: null, candidateWindowSize: windowSize, policy: policy));
                }
            }

            if (stage >= 3 && !budgetStopped && !staged)
            {
                // Math.Min with an unlimited cap leaves the standard band
                // exactly as it was (6 orderings at effort 5, 3 below).
                foreach (var order in orderings.Take(System.Math.Min(
                             stage >= 5 ? 6 : 3, scale.MaxRectsOrderings)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Consider(RunMaxRects(Sort(pieces, order), stocks, margins, rotation,
                            cancellationToken));
                }

                // Beam-corrected version: greedy per-sheet selection is myopic
                // (measured worse than the shipped engine on 17 of 40
                // randomized instances when used ALONE), so it is widened here.
                // Adding candidates can never regress — Better() takes the min.
                // The beam clones the whole open-sheet list for every candidate
                // (new List<SheetState>(state.Sheets) per split, per step), so
                // its cost is O(sheets^2 x beam width). At 5,000 sheets that is
                // hundreds of millions of reference copies on top of holding
                // eight full layouts alive at once — the other main memory
                // multiplier. It only ever ADDS candidates, so skipping it can
                // raise the sheet count but never produces an invalid layout.
                if (scale.AllowBeam)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Consider(RunBestSplitBeam(pieces, stocks, margins, rotation, 4, cancellationToken));
                    if (stage >= 4)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Consider(RunBestSplitBeam(pieces, stocks, margins, rotation, 8, cancellationToken));
                    }

                    // PHASE E3.3. Beam 16 at the top effort only, and behind a
                    // switch, so nothing below effort 5 and nothing that does
                    // not ask for it pays for it. It only ever ADDS a
                    // candidate and Better() takes the minimum, so it cannot
                    // make a plan worse - only slower.
                    if (stage >= 5 && wideBeam)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Consider(RunBestSplitBeam(pieces, stocks, margins, rotation, 16, cancellationToken));
                    }
                }
            }

            var final = best ?? PackQuick(pieces, stocks, margins, rotation, policy: policy);

            // Never let a size-driven reduction be invisible: the operator sees
            // exactly what was skipped and why, next to the sheet count.
            if (scale.IsReduced) final.ScaleNotes.Add(scale.Summary);

            // A refusal the operator cannot see is worse than no policy at all:
            // the sheet count would look worse for no stated reason.
            if (requireGuillotine && guillotineRejects > 0)
                final.ScaleNotes.Add("GUILLOTINE POLICY: " + guillotineRejects
                    + " candidate plan(s) were refused because no sequence of straight cuts "
                    + "produces their layout. The plan shown is the best one a guillotine "
                    + "table can actually cut, which is not always the fewest sheets.");

            // Same rule for the time budget. A truncated search that reported
            // nothing would look like a complete one, and the operator would have
            // no way to know that raising the effort level or splitting the job
            // could still buy sheets.
            if (budgetStopped)
            {
                final.ScaleNotes.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "TIME BUDGET: the search reached its {0:mm\\:ss} limit after {1:F1}s and stopped " +
                    "adding candidates — this is the best plan found by then, not a fully searched one. " +
                    "Splitting the job into smaller cutting groups would search deeper.",
                    plan.TimeBudget, budgetWatch.Elapsed.TotalSeconds));
            }

            // Industrial safety net: a plan that fails validation must never
            // reach a cut file. Fall back to the quick pass, which is an
            // independent single-pass engine; if even that fails (should be
            // impossible), keep the original and say so explicitly.
            var problems = VerifyComprehensive(final, pieces, margins);
            if (problems.Count > 0)
            {
                var quick = PackQuick(pieces, stocks, margins, rotation,
                    cancellationToken: cancellationToken, policy: policy);
                var quickProblems = VerifyComprehensive(quick, pieces, margins);
                if (quickProblems.Count == 0)
                {
                    // PHASE 4 / finding 24 (2026-09-20): this note used to be
                    // added to `final.ScaleNotes` and THEN `final = quick` threw
                    // that object away — so the warning travelled with the
                    // discarded plan and the operator never saw that their
                    // optimized result had failed validation and been replaced.
                    // A safety net nobody can see is not a safety net. The note
                    // now goes on the plan that is actually returned.
                    quick.ScaleNotes.Add($"WARNING: selected plan failed layout validation " +
                        $"({problems[0]}); replaced with validated quick pass.");
                    final = quick;
                    problems = quickProblems;
                }
                else
                {
                    final.ScaleNotes.Add($"WARNING: layout validation failed " +
                        $"({problems[0]}; {problems.Count} issue(s)) — result not re-run.");
                }
            }

            // PHASE 4: the verdict travels with the plan, so the run header can
            // say what was actually checked and the export / Step 6 feed paths
            // can refuse a plan that failed conservation.
            final.RecordValidation(problems, conservationChecked: true, geometryChecked: true);
            var beforePlatePattern = final;
            final = ApplyPlatePattern(final, pieces, stocks, margins, rotation,
                effortLevel, platePattern, cancellationToken, progress, policy);

            // E4.1: the fill-first-plate branch chooses between plans OUTSIDE
            // the candidate gate above, so it is held to the same rule here
            // rather than being allowed to reintroduce what the gate removed.
            // Reverting to beforePlatePattern is always safe: that plan was
            // admitted by the gate.
            if (requireGuillotine && !AllSheetsCuttable(final))
            {
                final = beforePlatePattern;
                guillotineRejects++;
            }

            for (int i = 0; i < final.Sheets.Count; i++)
            {
                var sheet = final.Sheets[i];
                if (sheet.Cuts.Count != 0) continue;

                // The return value used to be discarded (finding F1). That
                // was worse than a bad plan: a failed derivation leaves the
                // sheet with an EMPTY cut list, and the consistency audit
                // deliberately skips empty sheets so it does not double
                // report them. So the one plan that cannot be cut at all was
                // the only one that said nothing. Rated a warning rather
                // than an error: it is reported here, not refused, so the
                // operator decides with the sheet in front of them.
                if (!TryDeriveGuillotineCuts(sheet) && sheet.Pieces.Count > 0)
                    final.ScaleNotes.Add(
                        "WARNING: sheet " + sheet.SheetNo + " holds "
                        + sheet.Pieces.Count + " pane(s) but no sequence of "
                        + "straight cuts produces that layout. It cannot be cut "
                        + "as drawn - open it in the 2D VIEW before exporting.");
            }

            // PHASE 2 part 2: see PackQuick.
            if (reuseOffcuts)
                NoteOffcutReuse(final, ConsolidateIntoOffcuts(final, pieces, margins, rotation));

            // PHASE E2: see PackQuick.
            if (localSearch)
                NoteLocalSearch(final, LocalSearch(final, pieces, margins, rotation, enableSwaps));

            // PHASE 2: see PackQuick. Derived from cuts, never from placements.
            DeriveLeftovers(final, Math.Max(margins.MinBreakoutMm, 1), margins.KerfMm);

            // PHASE E1: see PackQuick.
            OptimizationInstrumentation.Instrument(final, stocks, margins, rotation);

            if (staged)
            {
                var cutProblems = ValidateCutPattern(final, policy);
                if (cutProblems.Count > 0)
                {
                    final.ValidationProblems.AddRange(cutProblems);
                    final.ScaleNotes.Add(
                        "CUT PATTERN: the requested staged pattern was not fully held — " +
                        "inspection only, not exportable.");
                }
            }

            progress?.Report(OptimizeProgress.Of("done", comboList.Count, comboList.Count,
                $"{final.Sheets.Count} sheets"));
            return final;
        }

        private static OptimizationResult Better(OptimizationResult a, OptimizationResult b) =>
            Better(a, b, PlatePattern.Yield);

        /// <summary>
        /// Keeps the better of two plans. Unplaced pieces and sheet count
        /// always dominate — no pattern may buy its preference by leaving a
        /// piece out or adding a sheet. The plate pattern decides between plans
        /// that are already equal on both, which is exactly the choice the
        /// operator is making when they pick one.
        /// </summary>
        private static OptimizationResult Better(OptimizationResult a, OptimizationResult b,
            PlatePattern pattern)
        {
            if (b == null) return a;
            if (a == null) return b;
            if (a.HasValidationFailure != b.HasValidationFailure)
                return a.HasValidationFailure ? b : a;
            if (b.Unplaced.Count != a.Unplaced.Count)
                return b.Unplaced.Count < a.Unplaced.Count ? b : a;
            if (b.SheetsUsed != a.SheetsUsed)
                return b.SheetsUsed < a.SheetsUsed ? b : a;

            switch (pattern)
            {
                case PlatePattern.FewestCuts:
                    int cutA = TotalCuts(a), cutB = TotalCuts(b);
                    if (cutB != cutA) return cutB < cutA ? b : a;
                    break;
                case PlatePattern.MinimiseStockChanges:
                    int sizeA = DistinctStockSizes(a), sizeB = DistinctStockSizes(b);
                    if (sizeB != sizeA) return sizeB < sizeA ? b : a;
                    int switchA = StockSwitches(a), switchB = StockSwitches(b);
                    if (switchB != switchA) return switchB < switchA ? b : a;
                    break;
                case PlatePattern.RemnantFirst:
                    long leftA = LargestLeftover(a), leftB = LargestLeftover(b);
                    if (leftB != leftA) return leftB > leftA ? b : a;
                    break;
                case PlatePattern.BalancedSpread:
                    decimal spreadA = UtilizationSpread(a), spreadB = UtilizationSpread(b);
                    if (spreadB != spreadA) return spreadB < spreadA ? b : a;
                    break;
            }

            return Utilization(b) > Utilization(a) ? b : a;
        }

        private static decimal Utilization(OptimizationResult r)
        {
            decimal stock = r.Sheets.Sum(sheet => (decimal)sheet.Stock.WidthMm * sheet.Stock.HeightMm);
            decimal glass = r.Sheets.Sum(sheet => sheet.Pieces.Sum(p => (decimal)p.WidthMm * p.HeightMm));
            return stock == 0 ? 0 : glass / stock;
        }

        /// <summary>
        /// Total guillotine cuts across a plan. Sheets that were stamped rather
        /// than split get their sequence reconstructed first, so a plan can
        /// never win "fewest cuts" by having failed to record them.
        /// </summary>
        internal static int TotalCuts(OptimizationResult result)
        {
            int total = 0;
            for (int i = 0; i < result.Sheets.Count; i++)
            {
                var sheet = result.Sheets[i];
                if (sheet.Cuts.Count == 0 && sheet.Pieces.Count > 0)
                    TryDeriveGuillotineCuts(sheet);
                total += sheet.Cuts.Count;
            }
            return total;
        }

        /// <summary>How many different stock sizes a plan reaches for.</summary>
        internal static int DistinctStockSizes(OptimizationResult result)
        {
            var seen = new HashSet<long>();
            for (int i = 0; i < result.Sheets.Count; i++)
            {
                var stock = result.Sheets[i].Stock;
                if (stock == null) continue;
                seen.Add(StockKey(stock));
            }
            return seen.Count;
        }

        /// <summary>One number per stock size, so "same size" is a value test
        /// rather than a string comparison per sheet.</summary>
        private static long StockKey(StockSize stock) =>
            ((long)stock.WidthMm << 32) | (uint)stock.HeightMm;

        /// <summary>
        /// How often consecutive sheets change stock size — the rack moves and
        /// setups a floor actually pays for. Sheet order is the report order
        /// (fullest first), which is the order the table sees them.
        /// </summary>
        internal static int StockSwitches(OptimizationResult result)
        {
            int switches = 0;
            bool hasPrevious = false;
            long previous = 0;
            for (int i = 0; i < result.Sheets.Count; i++)
            {
                var stock = result.Sheets[i].Stock;
                if (stock == null) continue;
                long key = StockKey(stock);
                if (hasPrevious && key != previous) switches++;
                previous = key;
                hasPrevious = true;
            }
            return switches;
        }

        /// <summary>
        /// Largest single-sheet leftover area in mm^2. Maximised by the
        /// remnant-first pattern: with the sheet count and the piece count
        /// already equal, concentrating the waste on ONE sheet is what makes it
        /// big enough to come off as a usable remnant rather than as several
        /// slivers spread over every plate.
        /// </summary>
        internal static long LargestLeftover(OptimizationResult result)
        {
            long best = 0;
            for (int i = 0; i < result.Sheets.Count; i++)
            {
                var sheet = result.Sheets[i];
                long usable = (long)sheet.UsableWidth * sheet.UsableHeight;
                long placed = 0;
                for (int p = 0; p < sheet.Pieces.Count; p++)
                    placed += (long)sheet.Pieces[p].WidthMm * sheet.Pieces[p].HeightMm;
                long leftover = usable - placed;
                if (leftover > best) best = leftover;
            }
            return best;
        }

        /// <summary>
        /// Gap between the fullest and the emptiest sheet, 0..1. Minimised by
        /// the balanced-spread pattern.
        /// </summary>
        internal static decimal UtilizationSpread(OptimizationResult result)
        {
            if (result.Sheets.Count <= 1) return 0m;
            decimal min = 1m, max = 0m;
            for (int i = 0; i < result.Sheets.Count; i++)
            {
                var sheet = result.Sheets[i];
                decimal stock = (decimal)sheet.Stock.WidthMm * sheet.Stock.HeightMm;
                if (stock <= 0) continue;
                decimal glass = 0m;
                for (int p = 0; p < sheet.Pieces.Count; p++)
                    glass += (decimal)sheet.Pieces[p].WidthMm * sheet.Pieces[p].HeightMm;
                decimal u = glass / stock;
                if (u < min) min = u;
                if (u > max) max = u;
            }
            return max - min;
        }

        // ── one packing pass ─────────────────────────────────────────────

        /// <summary>Piece-ordering key. internal, not private: RunBestSplitBeam
        /// is internal so the generator census can call it, and CS0051 forbids
        /// an internal signature from naming a private type. Widening this is
        /// safe — loosening accessibility can never introduce an error.</summary>
        internal enum OrderBy { MaxSide, Area, MinSide, Height, Width, Perimeter }

        internal static OptimizationResult RunPass(
            IReadOnlyList<CutPiece> orderedPieces,
            IReadOnlyList<StockSize> stocks,
            MarginProfile margins,
            RotationPolicy rotation,
            SplitMode split,
            CancellationToken cancellationToken = default,
            int consolidationSweeps = int.MaxValue,
            IProgress<OptimizeProgress> progress = null,
            int openSheetWindow = int.MaxValue,
            CutPolicy policy = null)
        {
            if (orderedPieces == null) throw new ArgumentNullException(nameof(orderedPieces));
            if (stocks == null || stocks.Count == 0)
                throw new ArgumentException("At least one stock size is required.", nameof(stocks));
            if (margins == null) throw new ArgumentNullException(nameof(margins));

            var result = new OptimizationResult { Rotation = rotation };
            if (orderedPieces.Count == 0) return result;

            var sheets = new List<SheetState>();
            var sheetsPerStock = new int[stocks.Count];
            var ctx = new PackingContext { OpenWindow = openSheetWindow };

            // ── real progress (2026-09-17) ────────────────────────────────────
            // Placement is the dominant work in a single pass, so it owns 0-90%
            // and consolidation owns the last 10%: a bar that reaches 100% and
            // then keeps working for another minute is exactly the lie this
            // replaces. Reports are throttled to whole-percent changes because
            // Progress<T> posts to the captured UI context, and posting once per
            // part on a 30,000-part group would cost more than the packing
            // itself. progress is null on every OPTIMIZE candidate pass, so the
            // multi-start search keeps its own candidate-level reporting and
            // pays nothing here. Purely observational: the packing above is
            // byte-identical with or without a sink, which is what keeps the
            // owner's benchmark figures exact.
            int totalParts = orderedPieces.Count;
            int placedParts = 0;
            int lastPercent = -1;

            foreach (var piece in orderedPieces)
            {
                // Checked per part so a Cancel takes effect within one part
                // rather than one whole pass. On a default token this is a
                // single boolean test and costs nothing measurable.
                cancellationToken.ThrowIfCancellationRequested();

                if (!PlaceOnOpenSheets(sheets, piece, margins, rotation, split))
                {
                    if (!OpenNewSheet(sheets, sheetsPerStock, stocks, piece, margins, rotation, split, result, ctx, policy))
                        result.Unplaced.Add(piece);
                }

                if (progress == null) continue;
                placedParts++;
                int percent = (int)((long)placedParts * 90 / totalParts);
                if (percent == lastPercent) continue;
                lastPercent = percent;
                progress.Report(OptimizeProgress.Of("quick", percent, 100,
                    $"Placing part {placedParts} of {totalParts} · {sheets.Count} sheet(s) open"));
            }

            progress?.Report(OptimizeProgress.Of("consolidate", 92, 100,
                $"Consolidating {sheets.Count} sheet(s)"));
            ConsolidateAll(sheets, margins, rotation, split, consolidationSweeps,
                cancellationToken: cancellationToken, ctx: ctx);
            SortSheetsFullestFirst(sheets);

            result.Strategy = $"GRID ({split})";
            for (int i = 0; i < sheets.Count; i++)
                result.Sheets.Add(sheets[i].Layout);
            for (int i = 0; i < result.Sheets.Count; i++)
                result.Sheets[i].SheetNo = i + 1;

            // The pass is genuinely finished here, so the bar is allowed to say
            // 100%. Without this a QUICK run topped out at 92% and the last 8%
            // arrived only as the progress panel disappearing, which reads as a
            // stall rather than a finish.
            progress?.Report(OptimizeProgress.Of("done", 100, 100,
                $"{result.Sheets.Count} sheet(s) cut \u00b7 {result.Unplaced.Count} unplaced"));
            return result;
        }

        /// <summary>Industrial report order: fullest sheet first, emptiest last.</summary>
        private static void SortSheetsFullestFirst(List<SheetState> sheets)
        {
            // PlacedArea is cached on the sheet. Recomputing two LINQ Sums
            // inside a comparator made this O(sheets log sheets x parts), and
            // it runs on every pass.
            sheets.Sort((a, b) =>
            {
                int cmp = b.PlacedArea.CompareTo(a.PlacedArea);
                if (cmp != 0) return cmp;
                return a.Layout.Pieces.Count.CompareTo(b.Layout.Pieces.Count) * -1;
            });
        }

        private static List<CutPiece> Sort(IReadOnlyList<CutPiece> pieces, OrderBy order)
        {
            switch (order)
            {
                case OrderBy.Area:
                    return pieces
                        .OrderByDescending(p => (long)p.WidthMm * p.HeightMm)
                        .ThenByDescending(p => System.Math.Max(p.WidthMm, p.HeightMm))
                        .ThenBy(p => p.GlassRef, StringComparer.Ordinal)
                        .ThenBy(p => p.PieceLabel, StringComparer.Ordinal)
                        .ToList();
                case OrderBy.MinSide:
                    return pieces
                        .OrderByDescending(p => System.Math.Min(p.WidthMm, p.HeightMm))
                        .ThenByDescending(p => (long)p.WidthMm * p.HeightMm)
                        .ThenBy(p => p.GlassRef, StringComparer.Ordinal)
                        .ThenBy(p => p.PieceLabel, StringComparer.Ordinal)
                        .ToList();
                case OrderBy.Height:
                    return pieces
                        .OrderByDescending(p => p.HeightMm)
                        .ThenByDescending(p => p.WidthMm)
                        .ThenBy(p => p.GlassRef, StringComparer.Ordinal)
                        .ThenBy(p => p.PieceLabel, StringComparer.Ordinal)
                        .ToList();
                case OrderBy.Width:
                    return pieces
                        .OrderByDescending(p => p.WidthMm)
                        .ThenByDescending(p => p.HeightMm)
                        .ThenBy(p => p.GlassRef, StringComparer.Ordinal)
                        .ThenBy(p => p.PieceLabel, StringComparer.Ordinal)
                        .ToList();
                case OrderBy.Perimeter:
                    return pieces
                        .OrderByDescending(p => (long)p.WidthMm + p.HeightMm)
                        .ThenByDescending(p => (long)p.WidthMm * p.HeightMm)
                        .ThenBy(p => p.GlassRef, StringComparer.Ordinal)
                        .ThenBy(p => p.PieceLabel, StringComparer.Ordinal)
                        .ToList();
                default: // calibrated default: tallest first
                    return pieces
                        .OrderByDescending(p => System.Math.Max(p.WidthMm, p.HeightMm))
                        .ThenByDescending(p => (long)p.WidthMm * p.HeightMm)
                        .ThenBy(p => p.GlassRef, StringComparer.Ordinal)
                        .ThenBy(p => p.PieceLabel, StringComparer.Ordinal)
                        .ToList();
            }
        }

        private static List<CutPiece> ShuffleDeterministic(List<CutPiece> src, int seed)
        {
            var list = src.ToList();
            int state = seed;
            for (int i = list.Count - 1; i > 0; i--)
            {
                state = unchecked(state * 1103515245 + 12345);
                int j = (int)((uint)state % (i + 1));
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        // ── placement ────────────────────────────────────────────────────

        private static bool PlaceOnOpenSheets(
            List<SheetState> sheets, CutPiece piece, MarginProfile margins,
            RotationPolicy rotation, SplitMode split)
        {
            // Compared as values: this used to build a PlacedPiece for the best
            // rect on EVERY open sheet and throw all but the winner away. The
            // comparator is the same lexicographic minimum over
            // (fitRank, shortSide, waste, orderRank, sheetIndex); because si
            // ascends, the final clause can never fire, so the FIRST sheet
            // still wins an exact tie.
            bool found = false;
            int bestSheet = -1;
            PlacementCandidate bestCand = default;

            for (int si = 0; si < sheets.Count; si++)
            {
                var sheet = sheets[si];
                if (sheet.IsClosed) continue;
                if (!TryPlaceCore(sheet, piece, rotation, out var candidate, split)) continue;

                if (!found
                    || candidate.FitRank < bestCand.FitRank
                    || (candidate.FitRank == bestCand.FitRank && candidate.ShortSide < bestCand.ShortSide)
                    || (candidate.FitRank == bestCand.FitRank && candidate.ShortSide == bestCand.ShortSide
                        && candidate.Waste < bestCand.Waste)
                    || (candidate.FitRank == bestCand.FitRank && candidate.ShortSide == bestCand.ShortSide
                        && candidate.Waste == bestCand.Waste && candidate.OrderRank < bestCand.OrderRank)
                    || (candidate.FitRank == bestCand.FitRank && candidate.ShortSide == bestCand.ShortSide
                        && candidate.Waste == bestCand.Waste && candidate.OrderRank == bestCand.OrderRank
                        && si < bestSheet))
                {
                    found = true;
                    bestSheet = si;
                    bestCand = candidate;

                    // Perfect fit shortcut: when a piece fits a free rectangle with 0 waste
                    // and unrotated orientation, no other placement can improve upon it.
                    if (candidate.FitRank == 0 && candidate.Waste == 0 && candidate.OrderRank == 0)
                        break;
                }
            }

            if (!found) return false;

            Commit(sheets[bestSheet], bestCand.Materialize(piece), margins, split);
            return true;
        }


        /// <summary>
        /// Local best fit for one piece: exact edges / short-side remainder
        /// first, orientation only a tie-break. This does not measure future
        /// row/column capacity; RunCapacityStrips supplies that alternative.
        /// </summary>
        private static PlacedPiece TryPlace(
            SheetState sheet, CutPiece piece, RotationPolicy rotation,
            out int rank, out long waste)
        {
            return TryPlace(sheet, piece, rotation, out rank, out _, out _, out waste);
        }

        private static PlacedPiece TryPlace(
            SheetState sheet, CutPiece piece, RotationPolicy rotation,
            out int rank, out int fitRank, out long shortSide, out long waste)
        {
            if (!TryPlaceCore(sheet, piece, rotation, out var c))
            {
                rank = int.MaxValue;
                fitRank = int.MaxValue;
                shortSide = long.MaxValue;
                waste = long.MaxValue;
                return null;
            }

            rank = c.OrderRank;
            fitRank = c.FitRank;
            shortSide = c.ShortSide;
            waste = c.Waste;
            return c.Materialize(piece);
        }

        /// <summary>
        /// A placement described by value. TryPlace used to allocate a
        /// PlacedPiece for every improvement it found, and its callers allocate
        /// one per candidate they compare, then discard all but the winner.
        /// RunSheetFill and FillSheetGreedy compare EVERY remaining piece in
        /// order to place one, which made that O(parts^2) allocations for a
        /// single candidate stream; PlaceOnOpenSheets made it O(parts x sheets).
        /// Scoring in a struct keeps the comparison exactly as it was while
        /// allocating nothing until the winner is known.
        /// </summary>
        private struct PlacementCandidate
        {
            public int X, Y, W, H;
            public bool Rotated;
            public int OrderRank, FitRank;
            public long ShortSide, Waste;

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
        /// Best fit for one piece on one sheet, allocation-free. ADAPTIVE
        /// rotation: the original orientation is preferred (rank 0); rotated
        /// placements are only chosen when nothing accepts the piece unrotated.
        /// </summary>
        private static bool TryPlaceCore(
            SheetState sheet, CutPiece piece, RotationPolicy rotation,
            out PlacementCandidate best, SplitMode split = SplitMode.HorizontalFirst)
        {
            best = default;

            int ow = piece.WidthMm, oh = piece.HeightMm;
            bool canRotate = rotation == RotationPolicy.Free90 && piece.AllowRotate && ow != oh;
            int orientationCount = canRotate ? 2 : 1;

            // ── SCALE FIX: sound whole-sheet prune ──
            // MaxFreeW / MaxFreeH are the EXACT maxima over this sheet's free
            // rects, so an orientation exceeding them exceeds every rect, and
            // the scan below could not have placed it. Returning false here is
            // indistinguishable from scanning and finding nothing — identical
            // results — but a full sheet is rejected in O(1) instead of
            // O(rects).
            bool anyFits = false;
            for (int oi = 0; oi < orientationCount; oi++)
            {
                int wProbe = oi == 0 ? ow : oh;
                int hProbe = oi == 0 ? oh : ow;
                if (wProbe <= sheet.MaxFreeW && hProbe <= sheet.MaxFreeH) { anyFits = true; break; }
            }
            if (!anyFits) return false;

            bool found = false;
            int bRank = int.MaxValue, bFit = int.MaxValue;
            int bX = 0, bY = 0, bW = 0, bH = 0;
            long bSs = long.MaxValue, bWaste = long.MaxValue;
            bool bRot = false;

            for (int oi = 0; oi < orientationCount; oi++)
            {
                int w = oi == 0 ? ow : oh;
                int h = oi == 0 ? oh : ow;
                bool rot = oi == 1;
                int orientationRank = oi;
                if (w > sheet.MaxFreeW || h > sheet.MaxFreeH) continue;

                for (int i = 0; i < sheet.Free.Count; i++)
                {
                    var rect = sheet.Free[i];
                    if (w > rect.W || h > rect.H) continue;

                    // PHASE 1: a staged policy can make this rectangle an
                    // illegal host even though the piece fits — see
                    // StagedPlacementAllowed. CutPolicy.Free short-circuits it.
                    if (!StagedPlacementAllowed(rect, w, h, split, sheet.Policy)) continue;

                    bool exactW = w == rect.W;
                    bool exactH = h == rect.H;
                    int fit = (exactW && exactH) ? 0 : (exactW || exactH) ? 1 : 2;
                    long ss = Math.Min(rect.W - w, rect.H - h);
                    long leftover = (long)rect.W * rect.H - (long)w * h;

                    // Geometry first (exact edge / BSSF), 0 vs 90 only as a
                    // tie-break — LiSEC/Editway mix rotation when it leaves a
                    // tighter strip, not only when 0° fails.
                    bool better = !found
                        || fit < bFit
                        || (fit == bFit && ss < bSs)
                        || (fit == bFit && ss == bSs && leftover < bWaste)
                        || (fit == bFit && ss == bSs && leftover == bWaste && orientationRank < bRank)
                        || (fit == bFit && ss == bSs && leftover == bWaste && orientationRank == bRank && rect.X < bX)
                        || (fit == bFit && ss == bSs && leftover == bWaste && orientationRank == bRank && rect.X == bX && rect.Y < bY);
                    if (!better) continue;

                    found = true;
                    bRank = orientationRank;
                    bFit = fit;
                    bSs = ss;
                    bWaste = leftover;
                    bX = rect.X;
                    bY = rect.Y;
                    bW = w;
                    bH = h;
                    bRot = rot;
                }
            }

            if (!found) return false;

            best.X = bX;
            best.Y = bY;
            best.W = bW;
            best.H = bH;
            best.Rotated = bRot;
            best.OrderRank = bRank;
            best.FitRank = bFit;
            best.ShortSide = bSs;
            best.Waste = bWaste;
            return true;
        }


        private static List<(int W, int H, bool Rotated, int Rank)> Orientations(
            CutPiece piece, RotationPolicy rotation)
        {
            var list = new List<(int, int, bool, int)> { (piece.WidthMm, piece.HeightMm, false, 0) };
            if (rotation == RotationPolicy.Free90 && piece.AllowRotate
                && piece.WidthMm != piece.HeightMm)
            {
                list.Add((piece.HeightMm, piece.WidthMm, true, 1));
            }
            return list;
        }

        /// <summary>
        /// The guillotine split, as a pure function: given the host rectangle and a
        /// piece placed at its top-left corner, append the remainder(s) to
        /// <paramref name="output"/> in the order the free-rect ledger expects.
        ///
        /// ONE SOURCE OF TRUTH. Commit used to inline this, so any smarter split
        /// policy would have had to duplicate it — and a duplicated split is how a
        /// plan ends up valid in one code path and not in another.
        ///
        /// <paramref name="minBreakout"/> is the narrowest offcut the floor can
        /// actually cut. 0 — the default, and what every existing caller passes —
        /// keeps every positive remainder, exactly as before. A positive value drops
        /// remainders narrower than it in either dimension: that area becomes trim
        /// waste instead of fictional stock, which is what
        /// MarginProfile.MinBreakoutMm has always claimed to mean.
        /// </summary>
        internal static void SplitRemainder(
            in FreeRect host, int pieceW, int pieceH, int kerf, int minBreakout,
            SplitMode split, List<FreeRect> output,
            CutPolicy policy = null, List<CutStep> cuts = null)
        {
            int rightW = host.W - pieceW - kerf;
            int bottomH = host.H - pieceH - kerf;

            bool verticalFirst = split == SplitMode.VerticalFirst ||
                (split == SplitMode.LongerLeftover && rightW >= bottomH);

            // PHASE 1 (2026-09-26): pin the first cut direction when the
            // operator chose one. Applied HERE, where the split is actually
            // decided, so the free-rect ledger and the recorded cut sequence
            // can never disagree about which way the sheet was cut first.
            bool forcedFirst = false;
            if (host.Depth == 0 && policy != null)
            {
                if (policy.ForceFirstCutVertical) { verticalFirst = true; forcedFirst = true; }
                else if (policy.ForceFirstCutHorizontal) { verticalFirst = false; forcedFirst = true; }
            }

            // A placement makes at most two cuts. The FIRST spans the whole
            // host rectangle; the SECOND runs inside the strip the first one
            // created, so it is one stage deeper. When the piece already spans
            // the host along that axis the first cut is not needed at all, and
            // the single remaining cut is a stage-(depth+1) cut, not depth+2.
            int stageFirst = host.Depth + 1;

            if (verticalFirst)
            {
                bool needsFirst = rightW > 0;
                if (needsFirst)
                {
                    // Vertical cut, full height: splits off the right-hand strip.
                    bool kept = Keepable(rightW, host.H, minBreakout) && RectCanHost(stageFirst, policy);
                    if (kept)
                        output.Add(new FreeRect(host.X + pieceW + kerf, host.Y, rightW, host.H, stageFirst));
                    RecordCut(cuts, CutAxis.Vertical, host.X + pieceW,
                        host.X, host.Y, host.W, host.H, stageFirst, !kept);
                }
                if (bottomH > 0)
                {
                    // Horizontal cut inside the left column: frees the piece.
                    // When the first direction is forced, that column is a
                    // STRIP even if it happens to be the whole sheet (the
                    // piece spans the sheet height) — no stage-1 cut is needed
                    // to make it, but the cut that frees the piece is still a
                    // stage-2 cut, so a forced first direction is never
                    // quietly contradicted by a stray stage-1 vertical cut.
                    int stage = (needsFirst || forcedFirst) ? stageFirst + 1 : stageFirst;
                    bool kept = Keepable(pieceW, bottomH, minBreakout) && RectCanHost(stage, policy);
                    if (kept)
                        output.Add(new FreeRect(host.X, host.Y + pieceH + kerf, pieceW, bottomH, stage));
                    RecordCut(cuts, CutAxis.Horizontal, host.Y + pieceH,
                        host.X, host.Y, needsFirst ? pieceW : host.W, host.H, stage, !kept);
                }
            }
            else
            {
                bool needsFirst = bottomH > 0;
                if (needsFirst)
                {
                    // Horizontal cut, full width: splits off the bottom strip.
                    bool kept = Keepable(host.W, bottomH, minBreakout) && RectCanHost(stageFirst, policy);
                    if (kept)
                        output.Add(new FreeRect(host.X, host.Y + pieceH + kerf, host.W, bottomH, stageFirst));
                    RecordCut(cuts, CutAxis.Horizontal, host.Y + pieceH,
                        host.X, host.Y, host.W, host.H, stageFirst, !kept);
                }
                if (rightW > 0)
                {
                    // Vertical cut inside the top strip: frees the piece. Same
                    // single-strip reasoning as the branch above.
                    int stage = (needsFirst || forcedFirst) ? stageFirst + 1 : stageFirst;
                    bool kept = Keepable(rightW, pieceH, minBreakout) && RectCanHost(stage, policy);
                    if (kept)
                        output.Add(new FreeRect(host.X + pieceW + kerf, host.Y, rightW, pieceH, stage));
                    RecordCut(cuts, CutAxis.Vertical, host.X + pieceW,
                        host.X, host.Y, host.W, needsFirst ? pieceH : host.H, stage, !kept);
                }
            }
        }

        /// <summary>
        /// True when a rectangle produced by a stage-<paramref name="stage"/>
        /// cut may host another placement. A rect made by a stage-S cut can
        /// only be cut again at stage S+1, so under an N-stage pattern a
        /// stage-N rectangle is the end of the line: it stays out of the free
        /// ledger and its area becomes waste. That is the documented yield
        /// cost of choosing a staged pattern, and it is why
        /// <see cref="CutPolicy.Free"/> (unlimited) remains the default.
        /// </summary>
        internal static bool RectCanHost(int stage, CutPolicy policy) =>
            policy == null || !policy.IsDepthConstrained || stage < policy.MaxStages;

        /// <summary>
        /// Appends one cut to a sheet's cut sequence. No-op when the caller is
        /// not recording (every pre-existing call site), so cut capture costs
        /// nothing on paths that do not ask for it.
        /// </summary>
        private static void RecordCut(List<CutStep> cuts, CutAxis axis, int atMm,
            int x, int y, int w, int h, int stage, bool isTrim)
        {
            if (cuts == null) return;
            cuts.Add(new CutStep
            {
                Seq = cuts.Count + 1,
                Axis = axis,
                AtMm = atMm,
                FromX = x,
                FromY = y,
                FromW = w,
                FromH = h,
                Stage = stage,
                IsTrim = isTrim
            });
        }

        /// <summary>
        /// True when a staged policy allows placing a <paramref name="w"/> x
        /// <paramref name="h"/> piece into this rectangle.
        ///
        /// Only binds on depth-constrained levels. Cut #2 of a placement is one
        /// stage deeper than cut #1, so on the LAST permitted stage a placement
        /// may not leave material on both axes — there would be no stage left
        /// to make that second cut with, and the piece could not be freed.
        ///
        /// Conservative on kerf: a gap of one kerf or less leaves nothing worth
        /// cutting, but deciding that needs the margin profile, which this
        /// helper does not carry. Rejecting it costs a sliver at worst.
        /// </summary>
        private static bool StagedPlacementAllowed(in FreeRect rect, int w, int h,
            SplitMode split, CutPolicy policy)
        {
            if (policy == null || !policy.IsDepthConstrained) return true;
            if (rect.Depth + 2 <= policy.MaxStages) return true;

            bool verticalFirst = split == SplitMode.VerticalFirst ||
                (split == SplitMode.LongerLeftover && (rect.W - w) >= (rect.H - h));
            return verticalFirst ? rect.H - h <= 0 : rect.W - w <= 0;
        }

        /// <summary>
        /// True when any piece in this cutting group is laminated, from the
        /// piece's own glass type ("LAM", "DGU+LAM") or — when that field was
        /// never filled in — from an interlayer named in its glass reference
        /// (PVB / EVA / SGP / LAM). Matched as whole tokens so a reference like
        /// "LAMINATED CLEAR" reads as laminate while an unrelated word that
        /// merely contains those letters does not.
        ///
        /// Used for one decision only: refusing trim cuts on laminated stock.
        /// Erring towards laminate is the safe direction.
        /// </summary>
        public static bool GroupIsLaminated(IReadOnlyList<CutPiece> pieces)
        {
            if (pieces == null) return false;
            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                string type = piece.GlassType ?? string.Empty;
                if (type.Equals("LAM", StringComparison.OrdinalIgnoreCase)
                    || type.Equals("DGU+LAM", StringComparison.OrdinalIgnoreCase)
                    || type.Equals("LAMINATED", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (ReferenceNamesLaminate(piece.GlassRef)) return true;
            }
            return false;
        }

        private static bool ReferenceNamesLaminate(string? glassRef)
        {
            if (string.IsNullOrWhiteSpace(glassRef)) return false;
            string token = string.Empty;
            string text = glassRef + " ";
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c))
                {
                    token += c;
                    continue;
                }
                if (token.Length > 0)
                {
                    if (token.Equals("LAM", StringComparison.OrdinalIgnoreCase)
                        || token.Equals("PVB", StringComparison.OrdinalIgnoreCase)
                        || token.Equals("EVA", StringComparison.OrdinalIgnoreCase)
                        || token.Equals("SGP", StringComparison.OrdinalIgnoreCase)
                        || token.Equals("LAMINATE", StringComparison.OrdinalIgnoreCase)
                        || token.Equals("LAMINATED", StringComparison.OrdinalIgnoreCase))
                        return true;
                    token = string.Empty;
                }
            }
            return false;
        }

        /// <summary>
        /// E4.1. True when every sheet that holds panes has a cut sequence, or
        /// one can be derived for it. Sheets with nothing on them are trivially
        /// fine, and sheets that already carry cuts are trusted — the only two
        /// producers of cuts are the guillotine split path and the derivation
        /// below, so an existing sequence is already known to be guillotine.
        /// </summary>
        internal static bool AllSheetsCuttable(OptimizationResult result)
        {
            if (result == null) return false;

            for (int i = 0; i < result.Sheets.Count; i++)
            {
                var sheet = result.Sheets[i];
                if (sheet.Pieces.Count == 0) continue;   // nothing to cut
                if (sheet.Cuts.Count > 0) continue;      // sequence already known
                if (!TryDeriveGuillotineCuts(sheet)) return false;
            }
            return true;
        }

        /// <summary>
        /// PHASE 0 fallback: reconstructs a sheet's guillotine cut sequence
        /// from the pieces already sitting on it.
        ///
        /// Needed because three packing paths stamp pieces straight onto a
        /// layout instead of going through Commit — the repeat-pattern
        /// stamper, the MaxRects candidates and the skyline placer — so their
        /// sheets would otherwise carry no cut sequence at all, and a CNC
        /// export would silently have nothing to write for them.
        ///
        /// It finds a straight line that splits the region's pieces into two
        /// non-empty groups without crossing any piece — the definition of a
        /// guillotine cut — and recurses. It never INVENTS cuts: when no such
        /// line exists the layout is not guillotine-cuttable, and the sheet is
        /// deliberately left with an empty cut list. That is a finding in its
        /// own right, and on a glass table it is one that matters.
        /// </summary>
        internal static bool TryDeriveGuillotineCuts(SheetLayout sheet)
        {
            if (sheet == null || sheet.Pieces.Count == 0) return false;

            var cuts = new List<CutStep>();
            int budget = 20000;
            bool ok = DeriveCuts(sheet.UsableX, sheet.UsableY, sheet.UsableWidth, sheet.UsableHeight,
                new List<PlacedPiece>(sheet.Pieces), 1, cuts, ref budget);
            if (!ok) return false;

            sheet.Cuts.Clear();
            for (int i = 0; i < cuts.Count; i++)
                sheet.Cuts.Add(cuts[i]);
            return true;
        }

        private static bool DeriveCuts(int x, int y, int w, int h, List<PlacedPiece> pieces,
            int stage, List<CutStep> cuts, ref int budget)
        {
            if (pieces.Count <= 1) return true;
            if (budget <= 0) return false;

            for (int i = 0; i < pieces.Count; i++)
            {
                budget--;
                int cutAt = pieces[i].X + pieces[i].WidthMm;
                if (cutAt <= x || cutAt >= x + w) continue;
                if (!SplitAt(pieces, vertical: true, cutAt, out var left, out var right)) continue;

                int mark = cuts.Count;
                cuts.Add(DerivedCut(cuts.Count + 1, CutAxis.Vertical, cutAt, x, y, w, h, stage));
                if (DeriveCuts(x, y, cutAt - x, h, left, stage + 1, cuts, ref budget)
                    && DeriveCuts(cutAt, y, x + w - cutAt, h, right, stage + 1, cuts, ref budget))
                    return true;
                cuts.RemoveRange(mark, cuts.Count - mark);
            }

            for (int i = 0; i < pieces.Count; i++)
            {
                budget--;
                int cutAt = pieces[i].Y + pieces[i].HeightMm;
                if (cutAt <= y || cutAt >= y + h) continue;
                if (!SplitAt(pieces, vertical: false, cutAt, out var below, out var above)) continue;

                int mark = cuts.Count;
                cuts.Add(DerivedCut(cuts.Count + 1, CutAxis.Horizontal, cutAt, x, y, w, h, stage));
                if (DeriveCuts(x, y, w, cutAt - y, below, stage + 1, cuts, ref budget)
                    && DeriveCuts(x, cutAt, w, y + h - cutAt, above, stage + 1, cuts, ref budget))
                    return true;
                cuts.RemoveRange(mark, cuts.Count - mark);
            }

            return false;
        }

        /// <summary>Splits a piece list at a candidate cut line. False when the
        /// line crosses a piece (not a guillotine cut) or leaves one side empty
        /// (a cut that accomplishes nothing).</summary>
        private static bool SplitAt(List<PlacedPiece> pieces, bool vertical, int at,
            out List<PlacedPiece> low, out List<PlacedPiece> high)
        {
            low = new List<PlacedPiece>();
            high = new List<PlacedPiece>();
            for (int i = 0; i < pieces.Count; i++)
            {
                var p = pieces[i];
                int start = vertical ? p.X : p.Y;
                int end = start + (vertical ? p.WidthMm : p.HeightMm);
                if (end <= at) low.Add(p);
                else if (start >= at) high.Add(p);
                else return false;
            }
            return low.Count > 0 && high.Count > 0;
        }

        private static CutStep DerivedCut(int seq, CutAxis axis, int atMm,
            int x, int y, int w, int h, int stage) => new CutStep
            {
                Seq = seq,
                Axis = axis,
                AtMm = atMm,
                FromX = x,
                FromY = y,
                FromW = w,
                FromH = h,
                Stage = stage,
                IsTrim = false
            };

        /// <summary>
        /// PHASE 1 oracle: does this plan obey the cut policy it was built
        /// under? Deliberately separate from <see cref="Verify"/> (geometry,
        /// kerf, overlaps) and ValidateResult (conservation): it checks a
        /// different property — not whether the layout is legal, but whether
        /// the CUTS that produce it are the ones the operator asked for.
        ///
        /// Returns the list of violations; empty means the pattern holds.
        /// </summary>
        public static List<string> ValidateCutPattern(OptimizationResult result, CutPolicy policy)
        {
            var problems = new List<string>();
            if (result == null) return problems;
            policy = policy ?? CutPolicy.Free;

            for (int si = 0; si < result.Sheets.Count; si++)
            {
                var sheet = result.Sheets[si];
                var cuts = sheet.Cuts;
                if (cuts.Count == 0) continue;

                if (sheet.Pieces.Count == 0)
                {
                    problems.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Sheet {0} carries {1} cut(s) but no pieces — its cut list is stale.",
                        sheet.SheetNo, cuts.Count));
                    continue;
                }

                int ux = sheet.UsableX, uy = sheet.UsableY;
                int expectedSeq = 1;

                for (int ci = 0; ci < cuts.Count; ci++)
                {
                    var cut = cuts[ci];
                    string where = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "sheet {0} cut {1}", sheet.SheetNo, ci + 1);

                    if (cut.Seq != expectedSeq)
                    {
                        problems.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "Cut sequence is not contiguous at {0}: Seq {1}, expected {2}.",
                            where, cut.Seq, expectedSeq));
                        expectedSeq = cut.Seq;
                    }
                    expectedSeq++;

                    if (policy.IsDepthConstrained && cut.Stage > policy.MaxStages)
                        problems.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0} is a stage-{1} cut but the chosen pattern allows {2} stage(s).",
                            where, cut.Stage, policy.MaxStages));

                    if (cut.Stage == 1 && policy.ForceFirstCutHorizontal && cut.Axis != CutAxis.Horizontal)
                        problems.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0} is vertical but the pattern forces a first X (horizontal) cut.", where));
                    if (cut.Stage == 1 && policy.ForceFirstCutVertical && cut.Axis != CutAxis.Vertical)
                        problems.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0} is horizontal but the pattern forces a first Y (vertical) cut.", where));

                    if (cut.FromX < ux || cut.FromY < uy
                        || cut.FromX + cut.FromW > ux + sheet.UsableWidth
                        || cut.FromY + cut.FromH > uy + sheet.UsableHeight)
                    {
                        problems.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0} lies outside the usable sheet area.", where));
                        continue;
                    }

                    if (cut.Axis == CutAxis.Vertical)
                    {
                        if (cut.AtMm < cut.FromX || cut.AtMm > cut.FromX + cut.FromW)
                            problems.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                "{0} sits at X {1}, outside the rectangle it cuts through ({2}..{3}).",
                                where, cut.AtMm, cut.FromX, cut.FromX + cut.FromW));
                    }
                    else if (cut.AtMm < cut.FromY || cut.AtMm > cut.FromY + cut.FromH)
                    {
                        problems.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "{0} sits at Y {1}, outside the rectangle it cuts through ({2}..{3}).",
                            where, cut.AtMm, cut.FromY, cut.FromY + cut.FromH));
                    }
                }
            }

            return problems;
        }

        /// <summary>True when a remainder is wide enough in BOTH dimensions to
        /// survive as stock. A minBreakout of 0 or less disables the check, which
        /// is what keeps every legacy path bit-identical.</summary>
        internal static bool Keepable(int w, int h, int minBreakout) =>
            minBreakout <= 0 || (w >= minBreakout && h >= minBreakout);

        /// <summary>
        /// Industrial safety net: verify a finished plan before it can reach a
        /// cut file. Checks, per sheet: pieces within the usable rectangle,
        /// piece dimensions matching their source CutPiece (rotation allowed),
        /// no two pieces overlapping, and no piece placed twice (reference
        /// identity). Also checks placed+unplaced account for every input
        /// piece. Returns a list of problems; empty list = valid plan.
        /// This is the guard that would have caught the double-claimed
        /// free-rect defect (overlapping placements in consolidated sheets).
        /// </summary>
        internal static List<string> ValidateResult(OptimizationResult result, IReadOnlyList<CutPiece> pieces)
        {
            var problems = new List<string>();
            if (result == null) { problems.Add("null result"); return problems; }

            var seen = new HashSet<CutPiece>();
            long placedArea = 0;
            foreach (var sheet in result.Sheets)
            {
                for (int a = 0; a < sheet.Pieces.Count; a++)
                {
                    var pa = sheet.Pieces[a];
                    var src = pa.Piece;
                    bool dimOk = src != null &&
                        ((pa.WidthMm == src.WidthMm && pa.HeightMm == src.HeightMm) ||
                         (src.AllowRotate && pa.WidthMm == src.HeightMm && pa.HeightMm == src.WidthMm));
                    if (!dimOk)
                        problems.Add($"Sheet {sheet.SheetNo} piece {a + 1}: placed {pa.WidthMm}x{pa.HeightMm} does not match source dims");
                    if (src != null && !seen.Add(src))
                        problems.Add($"Sheet {sheet.SheetNo} piece {a + 1}: piece placed more than once");
                    if (pa.X < sheet.UsableX || pa.Y < sheet.UsableY ||
                        pa.X + pa.WidthMm > sheet.UsableX + sheet.UsableWidth ||
                        pa.Y + pa.HeightMm > sheet.UsableY + sheet.UsableHeight)
                    {
                        problems.Add($"Sheet {sheet.SheetNo} piece {a + 1}: escapes usable rect at {pa.X},{pa.Y} {pa.WidthMm}x{pa.HeightMm}");
                        continue; // out-of-bounds pieces break the overlap test's meaning
                    }
                    placedArea += (long)pa.WidthMm * pa.HeightMm;
                    for (int b = a + 1; b < sheet.Pieces.Count; b++)
                    {
                        var pb = sheet.Pieces[b];
                        if (pa.X < pb.X + pb.WidthMm && pb.X < pa.X + pa.WidthMm &&
                            pa.Y < pb.Y + pb.HeightMm && pb.Y < pa.Y + pa.HeightMm)
                        {
                            problems.Add($"Sheet {sheet.SheetNo}: pieces {a + 1} and {b + 1} overlap " +
                                $"({pa.X},{pa.Y} {pa.WidthMm}x{pa.HeightMm}) vs ({pb.X},{pb.Y} {pb.WidthMm}x{pb.HeightMm})");
                        }
                    }
                }
            }

            long expectedArea = 0;
            var unplacedSet = new HashSet<CutPiece>();
            foreach (var up in result.Unplaced)
            {
                if (up != null)
                {
                    unplacedSet.Add(up);
                    expectedArea += (long)up.WidthMm * up.HeightMm;
                }
            }
            foreach (var p in pieces)
            {
                if (!unplacedSet.Contains(p) && !seen.Contains(p))
                    problems.Add($"Piece missing from plan: {p.PieceLabel}");
            }
            if (pieces.Count != result.Unplaced.Count + result.Sheets.Sum(s => s.Pieces.Count))
                problems.Add($"Piece accounting mismatch: input {pieces.Count}, placed " +
                    $"{result.Sheets.Sum(s => s.Pieces.Count)} + unplaced {result.Unplaced.Count}");

            return problems;
        }

        /// <summary>
        /// Commits a placement: consumes the host free rectangle and splits
        /// the remainder with one guillotine cut (kerf consumed).
        /// </summary>
        private static void Commit(SheetState sheet, PlacedPiece placed, MarginProfile margins,
            SplitMode split, int minBreakout = 0)
        {
            int kerf = margins.KerfMm;

            int idx = sheet.Free.FindIndex(r =>
                placed.X >= r.X && placed.Y >= r.Y &&
                placed.X + placed.WidthMm <= r.X + r.W &&
                placed.Y + placed.HeightMm <= r.Y + r.H);
            if (idx < 0)
                throw new InvalidOperationException("Placement escaped the free-rectangle ledger.");

            var rect = sheet.Free[idx];
            sheet.Free.RemoveAt(idx);

            // minBreakout defaults to 0, which reproduces the previous behaviour
            // exactly: every positive remainder stayed available as stock.
            //
            // PHASE 0/1: the sheet now supplies the cut policy and the cut
            // list, so every placement is recorded as the cuts that produce it.
            // With CutPolicy.Free this call is bit-identical to before.
            SplitRemainder(rect, placed.WidthMm, placed.HeightMm, kerf, minBreakout,
                split, sheet.Free, sheet.Policy, sheet.Layout.Cuts);

            sheet.Layout.Pieces.Add(placed);

            // Keep the scale metrics exact (see SheetState). Area is O(1)
            // incremental; the bounds need a rescan because the rect we just
            // consumed may have been the widest or tallest one.
            sheet.PlacedArea += (long)placed.WidthMm * placed.HeightMm;
            sheet.RefreshFreeBounds();
        }

        // ── stock selection ──────────────────────────────────────────────

        private static bool OpenNewSheet(
            List<SheetState> sheets, int[] sheetsPerStock, IReadOnlyList<StockSize> stocks,
            CutPiece piece, MarginProfile margins, RotationPolicy rotation,
            SplitMode split, OptimizationResult result, PackingContext? ctx = null,
            CutPolicy policy = null)
        {
            // "Best sheet size" rule: try the SMALLEST stock that fits the
            // piece first, so large racks are saved for the glass that
            // needs them (owner requirement for stock selection).
            var byPreference = Enumerable.Range(0, stocks.Count)
                .Where(i => sheetsPerStock[i] < stocks[i].MaxSheets || stocks[i].MaxSheets == 0)
                .OrderBy(i => (long)stocks[i].WidthMm * stocks[i].HeightMm)
                .ThenBy(i => i)
                .ToList();

            foreach (int i in byPreference)
            {
                var stock = stocks[i];

                int usableW = System.Math.Max(0, stock.WidthMm - margins.LeftMm - margins.RightMm);
                int usableH = System.Math.Max(0, stock.HeightMm - margins.TopMm - margins.BottomMm);

                bool fits = Orientations(piece, rotation)
                    .Any(o => o.W <= usableW && o.H <= usableH);
                if (!fits) continue;

                var layout = new SheetLayout
                {
                    SheetNo = result.Sheets.Count + sheets.Count + 1,
                    Stock = stock,
                    UsableX = margins.LeftMm,
                    UsableY = margins.TopMm,
                    UsableWidth = usableW,
                    UsableHeight = usableH
                };
                var state = new SheetState(layout, i, policy);
                sheets.Add(state);
                sheetsPerStock[i]++;
                if (ctx != null) RegisterOpenedSheet(ctx, state);

                var placed = TryPlace(state, piece, rotation, out _, out _);
                if (placed == null)
                    throw new InvalidOperationException("New sheet rejected the piece that opened it.");
                Commit(state, placed, margins, split);
                return true;
            }

            return false;
        }

        // ── consolidation — squeeze out the emptiest sheet ──────────────

        /// <summary>
        /// Records a copy-on-write snapshot of <paramref name="sheet"/> the
        /// first time an operation is about to modify it. Repeat calls for the
        /// same sheet are no-ops, so the snapshot always holds the pre-attempt
        /// state. Must be called BEFORE the write.
        /// </summary>
        private static void SnapshotBeforeWrite(
            SheetState sheet,
            HashSet<SheetState> seen,
            List<SheetState> touched,
            List<List<PlacedPiece>> touchedPieces,
            List<List<FreeRect>> touchedFree,
            List<List<CutStep>> touchedCuts)
        {
            if (!seen.Add(sheet)) return;
            touched.Add(sheet);
            touchedPieces.Add(sheet.Layout.Pieces.ToList());
            touchedFree.Add(sheet.Free.ToList());
            // PHASE 0: a rolled-back placement must take its cuts with it, or
            // the sheet would keep a cut list describing cuts that no longer
            // exist. Cuts are recorded by SplitRemainder, so they live and die
            // with the free-rect ledger they were derived from.
            touchedCuts.Add(sheet.Layout.Cuts.ToList());
        }

        /// <summary>
        /// Try to empty every sheet from sparsest to densest. One successful
        /// evacuation restarts the scan — LiSEC-style leftover squeeze.
        /// </summary>
        private static void ConsolidateAll(
            List<SheetState> sheets, MarginProfile margins, RotationPolicy rotation, SplitMode split,
            int maxSweeps = int.MaxValue, int minBreakout = 0,
            CancellationToken cancellationToken = default,
            PackingContext? ctx = null)
        {
            if (sheets.Count <= 1) return;

            // Each successful evacuation breaks out and restarts the whole
            // donor scan, and a failed donor still costs O(parts_on_donor x
            // sheets). Unbounded, that is O(parts x sheets^2) — at 100k parts
            // over 5,000 sheets, trillions of operations. maxSweeps is
            // unlimited for standard-size groups, so their behaviour (and the
            // calibration benchmark) is unchanged.
            bool improved = true;
            int sweeps = 0;
            while (improved && sheets.Count > 1 && sweeps < maxSweeps)
            {
                sweeps++;
                improved = false;
                // PlacedArea is the same value the Sum produced, maintained
                // incrementally by Commit. OrderBy is a stable sort, so with
                // identical keys this yields an identical order.
                var order = Enumerable.Range(0, sheets.Count)
                    .OrderBy(i => sheets[i].PlacedArea)
                    .ToList();

                foreach (int donorIndex in order)
                {
                    // One check per donor: evacuating a sheet costs
                    // victims x sheets placement attempts, so this is the
                    // granularity at which Cancel can actually be honoured.
                    cancellationToken.ThrowIfCancellationRequested();
                    if (sheets.Count <= 1) break;
                    if (donorIndex >= sheets.Count) continue;

                    var donor = sheets[donorIndex];
                    if (donor.Layout.Pieces.Count == 0)
                    {
                        bool donorWasOpen = !donor.IsClosed;
                        donor.IsRemoved = true;
                        sheets.RemoveAt(donorIndex);
                        if (donorWasOpen && ctx != null) ctx.OpenCount--;
                        improved = true;
                        break;
                    }

                    var victims = donor.Layout.Pieces
                        .OrderByDescending(p => (long)p.WidthMm * p.HeightMm)
                        .ToList();

                    // ── SCALE FIX 3: snapshot only what this attempt writes ──
                    // The previous code deep-copied every sheet's pieces AND
                    // every sheet's free rects for every donor, then restored
                    // all of them on failure. That is O(sheets x parts) per
                    // attempt, in a loop that restarts after each improvement,
                    // at the end of every pass: roughly 500 million reference
                    // copies per pass at 100k parts / 5,000 sheets. It was the
                    // largest single allocation source in the engine. Recording
                    // only the sheets actually touched makes the rollback exact
                    // (same pieces, same rects, same order) at O(touched).
                    var touched = new List<SheetState>();
                    var touchedPieces = new List<List<PlacedPiece>>();
                    var touchedFree = new List<List<FreeRect>>();
                    var touchedCuts = new List<List<CutStep>>();
                    var seen = new HashSet<SheetState>();

                    SnapshotBeforeWrite(donor, seen, touched, touchedPieces, touchedFree, touchedCuts);
                    donor.Layout.Pieces.Clear();
                    donor.PlacedArea = 0;
                    bool allEvacuated = true;

                    foreach (var victim in victims)
                    {
                        bool placed = false;
                        for (int si = 0; si < sheets.Count; si++)
                        {
                            if (si == donorIndex) continue;
                            var host = sheets[si];
                            if (host.IsClosed) continue;
                            if (!TryPlaceCore(host, victim.Piece, rotation, out var moved, split)) continue;
                            SnapshotBeforeWrite(host, seen, touched, touchedPieces, touchedFree, touchedCuts);
                            Commit(host, moved.Materialize(victim.Piece), margins,
                                split, minBreakout);
                            placed = true;
                            break;
                        }
                        if (!placed) { allEvacuated = false; break; }
                    }

                    if (allEvacuated)
                    {
                        bool donorWasOpen = !sheets[donorIndex].IsClosed;
                        sheets[donorIndex].IsRemoved = true;
                        sheets.RemoveAt(donorIndex);
                        if (donorWasOpen && ctx != null) ctx.OpenCount--;
                        improved = true;
                        break;
                    }

                    // Roll back exactly the sheets we wrote, newest first, and
                    // restore their cached metrics so the invariants hold.
                    for (int t = touched.Count - 1; t >= 0; t--)
                    {
                        touched[t].Layout.Pieces.Clear();
                        touched[t].Layout.Pieces.AddRange(touchedPieces[t]);
                        touched[t].Free.Clear();
                        touched[t].Free.AddRange(touchedFree[t]);
                        touched[t].Layout.Cuts.Clear();
                        touched[t].Layout.Cuts.AddRange(touchedCuts[t]);
                        touched[t].RefreshAll();
                    }
                }
            }
        }

        // ── ORACLE — verification used by tests and the UI ──────────────

        /// <summary>
        /// Asserts every guarantee of a run. Returns the list of
        /// violations (empty = layout valid). Used by oracle tests and
        /// available to the UI as a sanity badge.
        /// </summary>
        public static List<string> Verify(OptimizationResult result, MarginProfile margins)
        {
            var problems = new List<string>();
            int kerf = margins.KerfMm;

            foreach (var sheet in result.Sheets)
            {
                int usableRight = sheet.UsableX + sheet.UsableWidth;
                int usableBottom = sheet.UsableY + sheet.UsableHeight;

                foreach (var p in sheet.Pieces)
                {
                    if (p.X < sheet.UsableX || p.Y < sheet.UsableY ||
                        p.X + p.WidthMm > usableRight || p.Y + p.HeightMm > usableBottom)
                        problems.Add($"Sheet {sheet.SheetNo}: {Label(p)} breaches the margin zone.");

                    // Verify rotation permission adherence
                    if (result.Rotation == RotationPolicy.Locked && p.Rotated)
                        problems.Add($"Sheet {sheet.SheetNo}: {Label(p)} is rotated under Locked policy.");

                    // Verify dimension integrity.
                    // PHASE 4 / finding 10: guarded, matching ValidateResult's
                    // `src != null`. A missing back-reference is itself a
                    // problem worth reporting; it must not be a crash.
                    if (p.Piece == null)
                    {
                        problems.Add($"Sheet {sheet.SheetNo}: {Label(p)} has no source piece to verify dimensions against.");
                    }
                    else
                    {
                        bool origMatch = (p.WidthMm == p.Piece.WidthMm && p.HeightMm == p.Piece.HeightMm) ||
                                         (p.WidthMm == p.Piece.HeightMm && p.HeightMm == p.Piece.WidthMm && p.Rotated);
                        if (!origMatch)
                            problems.Add($"Sheet {sheet.SheetNo}: {Label(p)} dimension mutated from {p.Piece.WidthMm}x{p.Piece.HeightMm} to {p.WidthMm}x{p.HeightMm}.");
                    }
                }

                for (int i = 0; i < sheet.Pieces.Count; i++)
                {
                    for (int j = i + 1; j < sheet.Pieces.Count; j++)
                    {
                        var a = sheet.Pieces[i];
                        var b = sheet.Pieces[j];

                        bool xOverlap = a.X < b.X + b.WidthMm && b.X < a.X + a.WidthMm;
                        bool yOverlap = a.Y < b.Y + b.HeightMm && b.Y < a.Y + a.HeightMm;
                        if (xOverlap && yOverlap)
                        {
                            problems.Add($"Sheet {sheet.SheetNo}: {Label(a)} overlaps {Label(b)}.");
                            continue;
                        }

                        int xGap = System.Math.Max(
                            b.X - (a.X + a.WidthMm), a.X - (b.X + b.WidthMm));
                        int yGap = System.Math.Max(
                            b.Y - (a.Y + a.HeightMm), a.Y - (b.Y + b.HeightMm));

                        if (xOverlap && !yOverlap && yGap < kerf)
                            problems.Add($"Sheet {sheet.SheetNo}: {Label(a)} breaks kerf against {Label(b)} ({yGap}mm < {kerf}mm).");
                        if (yOverlap && !xOverlap && xGap < kerf)
                            problems.Add($"Sheet {sheet.SheetNo}: {Label(a)} breaks kerf against {Label(b)} ({xGap}mm < {kerf}mm).");
                    }
                }
            }

            return problems;
        }

        /// <summary>
        /// Comprehensive independent verification oracle validating geometry, kerf,
        /// margins, minimum breakout, quantity conservation, and material conservation.
        /// </summary>
        public static List<string> VerifyComprehensive(
            OptimizationResult result,
            IReadOnlyList<CutPiece> inputPieces,
            MarginProfile margins)
        {
            var problems = Verify(result, margins);
            problems.AddRange(AuditBreakout(result, margins));
            if (inputPieces != null) problems.AddRange(ValidateResult(result, inputPieces));

            // Quantity conservation: InputCount == PlacedCount + UnplacedCount
            int totalPlaced = result.Sheets.Sum(s => s.Pieces.Count);
            int totalUnplaced = result.Unplaced.Count;
            if (inputPieces != null && totalPlaced + totalUnplaced != inputPieces.Count)
            {
                problems.Add($"Quantity conservation violation: input {inputPieces.Count} != placed {totalPlaced} + unplaced {totalUnplaced}.");
            }

            foreach (var rack in result.Sheets.GroupBy(sheet => sheet.Stock))
            {
                if (rack.Key.MaxSheets > 0 && rack.Count() > rack.Key.MaxSheets)
                    problems.Add($"Stock cap violated: {rack.Count()} sheets used; only {rack.Key.MaxSheets} allowed.");
            }

            // Material conservation: All pieces on each sheet must match the sheet's stock material / thickness
            foreach (var sheet in result.Sheets)
            {
                foreach (var p in sheet.Pieces)
                {
                    if (sheet.Stock != null && p.Piece.ThicknessMm > 0 && sheet.Stock.ThicknessMm > 0 &&
                        p.Piece.ThicknessMm != sheet.Stock.ThicknessMm)
                    {
                        problems.Add($"Sheet {sheet.SheetNo}: piece {Label(p)} thickness {p.Piece.ThicknessMm}mm does not match stock {sheet.Stock.ThicknessMm}mm.");
                    }
                }
            }

            return problems;
        }

        /// <summary>
        /// Names a placed piece for an oracle message.
        ///
        /// PHASE 4 / finding 10 (2026-09-20): this dereferenced
        /// <c>p.Piece</c> unconditionally, and it is called from inside the
        /// failure branches of Verify's bounds, overlap and kerf checks. A
        /// PlacedPiece with no back-reference — which ValidateResult explicitly
        /// anticipates with a <c>src != null</c> guard — therefore threw while
        /// the oracle was trying to REPORT a problem, and the exception
        /// propagated out of RunCoreAsync as "Optimization failed: Object
        /// reference not set to an instance of an object.", discarding every
        /// group's results. Fixing it here rather than at one call site covers
        /// all of them: a label must never be the thing that breaks a verifier.
        /// </summary>
        private static string Label(PlacedPiece p)
        {
            if (p == null) return "(null placement)";
            if (p.Piece == null) return "(placement with no source piece)";
            return string.IsNullOrWhiteSpace(p.Piece.PieceLabel)
                ? p.Piece.GlassRef
                : p.Piece.PieceLabel;
        }
    }
}
