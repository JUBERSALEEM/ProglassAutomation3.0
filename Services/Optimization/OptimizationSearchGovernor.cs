// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/OptimizationSearchGovernor.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — Adaptive Industrial Resource & Search Governor
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProGlassAutomation.Services.Optimization
{
    /// <summary>Search breadth tier determined adaptively by workload and available resources.</summary>
    public enum SearchTier
    {
        /// <summary>Standard scale: deep exhaustive search.</summary>
        Full,
        /// <summary>Medium-large scale: deep bounded search with candidate indexing.</summary>
        Narrowed,
        /// <summary>Massive industrial scale (50K - 1M+): stratified partitioned search with active reservoirs.</summary>
        Minimum
    }

    /// <summary>
    /// Governs optimization strategies, resource allocations, and search bounds for a cutting run.
    /// Completely eliminates fixed part-count ceilings in favor of dynamic hardware-aware scaling.
    /// </summary>
    public sealed class SearchPlan
    {
        public SearchTier Tier { get; init; } = SearchTier.Full;

        /// <summary>Max deterministic-shuffle index per (order, split).</summary>
        public int ShuffleCap { get; init; } = int.MaxValue;

        /// <summary>Whether to evaluate the full multi-strategy grid across all orderings and splits.</summary>
        public bool ForceFullGrid { get; init; } = true;

        /// <summary>Max simultaneously-open sheets per packing pass.</summary>
        public int OpenSheetWindow { get; init; } = int.MaxValue;

        /// <summary>Splits attempted by sheet-fill / wall-seed.</summary>
        public SplitMode[] FillWallSplits { get; init; } = Array.Empty<SplitMode>();

        /// <summary>MaxRects orderings attempted (effort ≥ 3).</summary>
        public int MaxRectOrders { get; init; } = int.MaxValue;

        /// <summary>Consolidation outer sweeps per pass.</summary>
        public int MaxConsolidationSweeps { get; init; } = int.MaxValue;

        /// <summary>Donor attempts per consolidation.</summary>
        public int MaxDonorAttempts { get; init; } = int.MaxValue;

        /// <summary>Batch target size for partitioned adaptive execution.</summary>
        public int BatchTargetSize { get; init; } = 1500;

        /// <summary>Candidate selection window size for spatial retrieval.</summary>
        public int CandidateWindowSize { get; init; } = 500;

        /// <summary>User-facing explanation.</summary>
        public string Note { get; init; } = string.Empty;

        /// <summary>Short grid-cell tag.</summary>
        public string ShortTag { get; init; } = string.Empty;

        // ── PHASE 8 / finding 8b (2026-09-20): a wall-clock budget ──────
        //
        // Until now the governor bounded BREADTH only. Effort level widened the
        // candidate grid, and the tier caps narrowed it by part count, but
        // nothing anywhere bounded ELAPSED TIME: Tier 1 left
        // MaxConsolidationSweeps, OpenSheetWindow and MaxDonorAttempts at
        // int.MaxValue, and PackQuick's own comment concedes that
        // "ConsolidateAll still restarts its donor sweep after every
        // improvement, which is unbounded in the number of sheets". The only
        // escape from a pathological layout was the operator noticing and
        // pressing CANCEL.
        //
        // The budgets below are deliberately generous — multiples of what a
        // normal run takes — so this bounds the pathological case without ever
        // truncating a healthy search. When a budget does engage, the search
        // stops adding candidates and KEEPS THE BEST PLAN FOUND SO FAR, and says
        // so in ScaleNotes. A usable plan now beats an unfinished search.

        /// <summary>Wall-clock budget for one cutting group's search.</summary>
        public TimeSpan TimeBudget { get; init; } = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Memory the machine had available when this plan was built, in bytes.
        /// Zero means the platform did not report it, in which case no
        /// memory-driven narrowing was applied.
        /// </summary>
        public long ObservedAvailableMemoryBytes { get; init; }

        /// <summary>
        /// Copies this plan with selected fields replaced. SearchPlan is
        /// immutable, so every transformation goes through here — one place that
        /// has to be kept in step with the property list, instead of several
        /// object initialisers that can each quietly omit a field and reset it to
        /// its default.
        /// </summary>
        private SearchPlan CopyWith(
            SearchTier? tier = null,
            int? shuffleCap = null,
            bool? forceFullGrid = null,
            int? openSheetWindow = null,
            SplitMode[]? fillWallSplits = null,
            int? maxRectOrders = null,
            int? maxConsolidationSweeps = null,
            int? maxDonorAttempts = null,
            int? batchTargetSize = null,
            int? candidateWindowSize = null,
            TimeSpan? timeBudget = null,
            long? observedMemory = null,
            string? shortTag = null,
            string? note = null) => new SearchPlan
            {
                Tier = tier ?? Tier,
                ShuffleCap = shuffleCap ?? ShuffleCap,
                ForceFullGrid = forceFullGrid ?? ForceFullGrid,
                OpenSheetWindow = openSheetWindow ?? OpenSheetWindow,
                FillWallSplits = fillWallSplits ?? FillWallSplits,
                MaxRectOrders = maxRectOrders ?? MaxRectOrders,
                MaxConsolidationSweeps = maxConsolidationSweeps ?? MaxConsolidationSweeps,
                MaxDonorAttempts = maxDonorAttempts ?? MaxDonorAttempts,
                BatchTargetSize = batchTargetSize ?? BatchTargetSize,
                CandidateWindowSize = candidateWindowSize ?? CandidateWindowSize,
                TimeBudget = timeBudget ?? TimeBudget,
                ObservedAvailableMemoryBytes = observedMemory ?? ObservedAvailableMemoryBytes,
                ShortTag = shortTag ?? ShortTag,
                Note = note ?? Note
            };

        /// <summary>Records the wall-clock budget for this group's search.</summary>
        public SearchPlan WithTimeBudget(TimeSpan budget) => CopyWith(timeBudget: budget);

        /// <summary>Records what the memory probe saw, without changing breadth.</summary>
        public SearchPlan WithObservedMemory(long bytes) => CopyWith(observedMemory: bytes);

        /// <summary>
        /// Returns a narrower copy of this plan. Used when the observed memory
        /// will not comfortably hold the workload, so the search gives up
        /// breadth before the allocator gives up entirely.
        /// </summary>
        public SearchPlan WithReducedBreadth(string reason) => CopyWith(
            tier: Tier == SearchTier.Full ? SearchTier.Narrowed
                : Tier == SearchTier.Narrowed ? SearchTier.Minimum
                : SearchTier.Minimum,
            shuffleCap: System.Math.Min(ShuffleCap, 1),
            forceFullGrid: false,
            openSheetWindow: System.Math.Min(OpenSheetWindow, 400),
            fillWallSplits: FillWallSplits.Length > 1 ? new[] { FillWallSplits[0] } : FillWallSplits,
            maxRectOrders: System.Math.Min(MaxRectOrders, 1),
            maxConsolidationSweeps: System.Math.Min(MaxConsolidationSweeps, 2),
            maxDonorAttempts: System.Math.Min(MaxDonorAttempts, 1000),
            candidateWindowSize: System.Math.Min(CandidateWindowSize, 400),
            shortTag: ShortTag + " · memory-narrowed",
            note: string.IsNullOrEmpty(Note) ? reason : Note + " " + reason);
    }

    public static class OptimizationSearchGovernor
    {
        private static readonly SplitMode[] AllSplits =
        {
            SplitMode.HorizontalFirst, SplitMode.VerticalFirst, SplitMode.LongerLeftover
        };

        private static readonly SplitMode[] FirstSplitOnly = { SplitMode.HorizontalFirst };

        private static string Count(int n) =>
            n.ToString("N0", CultureInfo.InvariantCulture);

        // ── PHASE 8 / finding 8d: the governor never looked at memory ──────
        //
        // The class is titled "Adaptive Industrial Resource & Search Governor"
        // and documents "dynamic hardware-aware scaling", but PlanForOptimize
        // read exactly one hardware input: Environment.ProcessorCount. Available
        // memory was never consulted, so the only memory backstop in the whole
        // engine was the catch (OutOfMemoryException) in RunCoreAsync — which
        // fires AFTER the allocation has already failed.

        /// <summary>
        /// Conservative working-set estimate per expanded part. Each part is a
        /// CutPiece plus its "3/24" style label, and the orderings and shuffles
        /// each materialise a fresh copy of the piece list, so the peak is a
        /// small multiple of the input rather than the input itself. 1 KB per
        /// part is deliberately pessimistic: at this figure 2,000 parts need
        /// 2 MB (never a constraint) and 1,000,000 parts need ~1 GB (where a
        /// tier drop is exactly the right response).
        /// </summary>
        internal const long EstimatedBytesPerPart = 1024;

        /// <summary>
        /// Memory the machine can still give this process, in bytes, or 0 when
        /// the platform does not report it. Never throws — a failed probe must
        /// degrade to "no memory-driven narrowing", not to a crashed run.
        /// </summary>
        public static long GetAvailableMemoryBytes()
        {
            try
            {
                var info = GC.GetGCMemoryInfo();
                long total = info.TotalAvailableMemoryBytes;
                long load = info.MemoryLoadBytes;

                if (total > 0 && load > 0 && total > load) return total - load;
                if (total > 0) return total;
            }
            catch { }

            return 0;
        }

        /// <summary>
        /// Narrows the plan when the estimated working set will not comfortably
        /// fit in what the machine has. The threshold is half of available
        /// memory: the estimate is rough, the process is not the only consumer,
        /// and a tier drop costs sheet quality while an OutOfMemoryException
        /// costs the whole run.
        /// </summary>
        private static SearchPlan ApplyMemoryPressure(SearchPlan plan, int pieceCount)
        {
            long available = GetAvailableMemoryBytes();
            if (available <= 0) return plan.WithObservedMemory(available);

            long estimatedNeed = (long)pieceCount * EstimatedBytesPerPart;
            if (estimatedNeed <= available / 2) return plan.WithObservedMemory(available);

            string reason = string.Format(CultureInfo.InvariantCulture,
                "MEMORY GUARD: {0} parts need roughly {1:F1} GB of working set and only {2:F1} GB " +
                "was available, so the search was narrowed a tier to finish instead of exhausting " +
                "memory. Close other applications or split the job for the full search.",
                Count(pieceCount),
                estimatedNeed / 1_073_741_824.0,
                available / 1_073_741_824.0);

            return plan.WithReducedBreadth(reason).WithObservedMemory(available);
        }

        /// <summary>
        /// Adaptively derives search parameters based on piece count, hardware concurrency, and requested effort.
        /// Scales continuously from 100 to 1,000,000+ parts without reverting to un-optimized shelf packing.
        /// </summary>
        public static SearchPlan PlanForOptimize(int pieceCount, int effortLevel)
        {
            var core = PlanForOptimizeCore(pieceCount, effortLevel);
            return ApplyMemoryPressure(core.WithTimeBudget(BudgetFor(core.Tier, effortLevel)), pieceCount);
        }

        /// <summary>
        /// Wall-clock budget for one cutting group, by tier and effort. Generous
        /// by design — see <see cref="SearchPlan.TimeBudget"/>.
        /// </summary>
        private static TimeSpan BudgetFor(SearchTier tier, int effortLevel) => tier switch
        {
            SearchTier.Full => effortLevel >= 5 ? TimeSpan.FromMinutes(25)
                             : effortLevel >= 4 ? TimeSpan.FromMinutes(15)
                             : effortLevel >= 3 ? TimeSpan.FromMinutes(8)
                             : TimeSpan.FromMinutes(5),
            SearchTier.Narrowed => TimeSpan.FromMinutes(20),
            _ => TimeSpan.FromMinutes(40)
        };

        private static SearchPlan PlanForOptimizeCore(int pieceCount, int effortLevel)
        {
            int cpuCores = Environment.ProcessorCount;

            // Tier 1: Small/Medium Workloads (<= 2,000 parts)
            if (pieceCount <= 2000)
            {
                return new SearchPlan
                {
                    Tier = SearchTier.Full,
                    FillWallSplits = AllSplits,
                    ShuffleCap = effortLevel >= 5 ? 6 : (effortLevel >= 4 ? 3 : 0),
                    ForceFullGrid = true,
                    MaxRectOrders = effortLevel >= 5 ? 6 : 3,
                    MaxConsolidationSweeps = int.MaxValue,
                    OpenSheetWindow = int.MaxValue,
                    BatchTargetSize = 2000,
                    CandidateWindowSize = int.MaxValue,
                    ShortTag = "Full Search",
                    Note = $"{Count(pieceCount)} parts: full multi-strategy search at effort {effortLevel}."
                };
            }

            // Tier 2: Large Workloads (2,001 - 15,000 parts)
            if (pieceCount <= 15000)
            {
                return new SearchPlan
                {
                    Tier = SearchTier.Narrowed,
                    FillWallSplits = effortLevel >= 3 ? AllSplits : FirstSplitOnly,
                    ShuffleCap = effortLevel >= 4 ? 2 : 0,
                    ForceFullGrid = true,
                    MaxRectOrders = effortLevel >= 4 ? 3 : 1,
                    MaxConsolidationSweeps = 4,
                    MaxDonorAttempts = 3000,
                    OpenSheetWindow = int.MaxValue,
                    BatchTargetSize = Math.Min(2500, Math.Max(1200, pieceCount / cpuCores)),
                    CandidateWindowSize = 600,
                    ShortTag = "Adaptive Bounded Search",
                    Note = $"Packing {Count(pieceCount)} parts — adaptive bounded multi-candidate grid with indexed candidate retrieval."
                };
            }

            // Tier 3: Industrial High-Volume Workloads (15,001 - 1,000,000+ parts)
            // Never disables optimization: all plates remain open to all size strata for maximum nesting yield
            int adaptiveBatchSize = Math.Clamp(pieceCount / (cpuCores * 2), 1200, 3000);

            return new SearchPlan
            {
                Tier = SearchTier.Minimum,
                FillWallSplits = AllSplits,
                ShuffleCap = effortLevel >= 5 ? 3 : (effortLevel >= 4 ? 2 : 0),
                ForceFullGrid = true,
                MaxRectOrders = effortLevel >= 4 ? 3 : 1,
                MaxConsolidationSweeps = 3,
                MaxDonorAttempts = 2000,
                OpenSheetWindow = int.MaxValue,
                BatchTargetSize = adaptiveBatchSize,
                CandidateWindowSize = 500,
                ShortTag = "Stratified Industrial Search",
                Note = $"Packing {Count(pieceCount)} parts across full open-plate strata with deep tail consolidation."
            };
        }

        /// <summary>
        /// An existing stage, chosen from the cutting list. This does not
        /// invent a search and it does not choose the plate pattern.
        /// </summary>
        public readonly struct EffortRecommendation
        {
            public int Stage { get; init; }
            public string Reason { get; init; }

            public EffortRecommendation()
            {
                Stage = 0;
                Reason = string.Empty;
            }
        }

        /// <summary>Same full-search ceiling as <see cref="PlanForOptimizeCore"/> and the partitioned handoff.</summary>
        public const int FullSearchPieceLimit = 2000;

        /// <summary>Same narrowed-search ceiling as <see cref="PlanForOptimizeCore"/>.</summary>
        public const int NarrowedSearchPieceLimit = 15000;

        /// <summary>Same industrial handoff as the packer. The small-job grid is not run at this count.</summary>
        public const int IndustrialPieceLimit = 100000;

        /// <summary>
        /// Picks stage 3, 4, or 5. Stage 1 and stage 2 stay manual. A higher
        /// stage can improve the plan; it cannot replace a better one.
        /// Yield is not changed here.
        /// </summary>
        public static EffortRecommendation RecommendEffort(int pieceCount, int stockSizeCount, bool finiteStock)
        {
            if (pieceCount < 0) pieceCount = 0;
            if (stockSizeCount < 1) stockSizeCount = 1;

            if (pieceCount >= IndustrialPieceLimit)
            {
                return new EffortRecommendation
                {
                    Stage = 3,
                    Reason = "industrial quantity uses the pattern master, not the small-job grid. Stage 3 keeps the default depth."
                };
            }

            if (pieceCount > NarrowedSearchPieceLimit && !finiteStock)
            {
                return new EffortRecommendation
                {
                    Stage = 3,
                    Reason = "above 15,000 pieces the batched search is already narrowed. Stage 3 stays inside that budget."
                };
            }

            if (pieceCount > FullSearchPieceLimit)
            {
                return new EffortRecommendation
                {
                    Stage = 4,
                    Reason = finiteStock
                        ? "a finite rack stays on the global search. Stage 4 runs every ordering without the stage 5 shuffle grid."
                        : "above 2,000 pieces the batched search adds best-split, sheet-fill, and wall-seed at stage 4."
                };
            }

            if (pieceCount <= 200)
            {
                return new EffortRecommendation
                {
                    Stage = 5,
                    Reason = "a short list finishes the deepest existing stage quickly, and that stage cannot replace a better plan."
                };
            }

            if (stockSizeCount > 1 || finiteStock)
            {
                return new EffortRecommendation
                {
                    Stage = 4,
                    Reason = "mixed stock sizes or a finite rack use stage 4, which tries every ordering and split."
                };
            }

            return new EffortRecommendation
            {
                Stage = 3,
                Reason = "one stock size in the normal shop range stays on stage 3, the calibrated default."
            };
        }

        /// <summary>Search plan for PackQuick.</summary>
        public static SearchPlan PlanForQuick(int pieceCount)
        {
            // PHASE 8 / finding 8b: QUICK is bounded too. Its own source comment
            // records that ConsolidateAll "restarts its donor sweep after every
            // improvement, which is unbounded in the number of sheets", and below
            // 2,000 parts MaxConsolidationSweeps was left at its cap. QUICK is
            // the interactive path — the operator is watching — so its budget is
            // the tightest of the three, and it is still far above what a normal
            // quick pass takes.
            return ApplyMemoryPressure(new SearchPlan
            {
                Tier = pieceCount <= 2000 ? SearchTier.Full : SearchTier.Narrowed,
                OpenSheetWindow = int.MaxValue,
                MaxConsolidationSweeps = pieceCount <= 2000 ? 3 : 2,
                MaxDonorAttempts = 1500,
                ShortTag = pieceCount <= 2000 ? "Quick Single-Pass" : "Adaptive Quick Pass",
                Note = pieceCount <= 2000 ? "" : $"Packing {Count(pieceCount)} parts with full open-plate quick pass.",
                TimeBudget = TimeSpan.FromMinutes(5)
            }, pieceCount);
        }
    }
}
