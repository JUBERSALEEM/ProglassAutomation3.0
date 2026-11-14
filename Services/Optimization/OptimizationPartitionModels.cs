// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/OptimizationPartitionModels.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — Partitioning, telemetry, and smart search models (50K+ scale)
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Optimization
{
    /// <summary>
    /// Configuration governing partitioned adaptive smart search for jobs of any scale (up to 50K+ parts).
    /// Safe defaults ensure existing smaller jobs execute unhindered while massive jobs are decomposed and scaled.
    /// </summary>
    public sealed class SmartSearchConfiguration
    {
        /// <summary>Partition threshold: groups larger than this get divided into production batches.</summary>
        public int LargeJobThreshold { get; set; } = 2000;

        /// <summary>Target part count per production compatibility batch.</summary>
        public int BatchTargetSize { get; set; } = 1500;

        /// <summary>Active candidate selection window (K) to eliminate O(N^2) piece scanning.</summary>
        public int CandidateWindowSize { get; set; } = 400;

        /// <summary>Active open-sheets window (W) during sequential packing.</summary>
        public int OpenSheetWindow { get; set; } = 128;

        /// <summary>Maximum concurrent worker tasks for multi-candidate search.</summary>
        public int MaxParallelWorkers { get; set; } = Math.Min(4, Math.Max(1, Environment.ProcessorCount));

        /// <summary>Beam search lookahead width for tail/awkward regions.</summary>
        public int BeamWidth { get; set; } = 4;

        /// <summary>Maximum depth for localized beam search lookahead.</summary>
        public int BeamDepth { get; set; } = 16;

        /// <summary>Percentage of trailing sheets subjected to deep tail-sheet optimization.</summary>
        public double TailOptimizationPercentage { get; set; } = 0.10;

        /// <summary>Utilization percentage threshold under which a sheet is marked as weak for evacuation/exchange.</summary>
        public decimal WeakSheetUtilizationThreshold { get; set; } = 75.0m;

        /// <summary>Number of adjacent batches allowed to participate in cross-batch consolidation.</summary>
        public int ConsolidationRadius { get; set; } = 2;

        /// <summary>Maximum time budget in seconds before plateau early-exit (0 = unlimited).</summary>
        public int SearchTimeBudgetSeconds { get; set; } = 0;

        public static SmartSearchConfiguration Default => new();
    }

    /// <summary>
    /// Telemetry and diagnostics recorded during a partitioned adaptive search.
    /// </summary>
    public sealed class SmartSearchTelemetry
    {
        public int TotalParts { get; set; }
        public int TotalBatches { get; set; }
        public int CandidateWindowSize { get; set; }
        public long CandidateEvaluations { get; set; }
        public int PlacementsAttempted { get; set; }
        public int PlacementsAccepted { get; set; }
        public int CandidatePassesExecuted { get; set; }
        public int CrossBatchExchanges { get; set; }
        public int TailSheetsOptimized { get; set; }
        public int RecoveredUnplaced { get; set; }
        public double ExecutionTimeSeconds { get; set; }
        public string WinningStrategy { get; set; } = string.Empty;
    }
}
