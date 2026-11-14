// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/Optimization/OptimizationEntities.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — persisted entities (design 2026-09-12)
//
// Sessions are stored as one row + JSON payloads (margins / stocks /
// pieces / result). The engine itself stays pure C# — persistence is a
// thin shell so saved runs can be reopened, re-verified and re-exported.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.Optimization
{
    /// <summary>A saved optimization run (reopenable, re-verifiable).</summary>
    public class SavedOptimizationSession
    {
        /// <summary>
        /// Reserved id for the single auto-saved "latest run" row.
        ///
        /// A finished run persists itself here so a quote reopened in a LATER app
        /// session can still be checked against it — the live publish only covers
        /// the current process. It is one row, replaced on every run, and it is
        /// filtered out of the Saved Sessions list: auto-saving named sessions
        /// would bury the ones the operator chose to keep.
        /// </summary>
        public const string LatestRunId = "__latest_run__";

        /// <summary>
        /// Prefix of a PER-DOCUMENT latest-run row: <c>__latest_run__:{proforma id}</c>.
        /// </summary>
        /// <remarks>
        /// One reserved row per document rather than one global row, so a quote
        /// reopened later is checked against the plan IT was cut against. The
        /// global <see cref="LatestRunId"/> is kept for rows written before
        /// stamping existed and for runs that have no document identity (a run
        /// driven from the OPTIMIZATION tab for a Job Order).
        ///
        /// Encoding the identity in the existing string primary key is deliberate:
        /// the store is created with Database.EnsureCreated and has no migrations,
        /// so adding a column would leave every existing installation querying a
        /// column that does not exist.
        /// </remarks>
        public const string LatestRunIdPrefix = "__latest_run__:";

        /// <summary>Reserved-row id for a document. Empty key = the legacy global row.</summary>
        public static string LatestRunIdFor(string? documentKey) =>
            string.IsNullOrWhiteSpace(documentKey)
                ? LatestRunId
                : LatestRunIdPrefix + documentKey.Trim();

        /// <summary>True for ANY reserved latest-run row, global or per-document.
        /// These are feed plumbing and must never appear in Saved Sessions.</summary>
        public static bool IsReservedLatestRunId(string? id) =>
            !string.IsNullOrWhiteSpace(id)
            && (string.Equals(id, LatestRunId, StringComparison.Ordinal)
                || id!.StartsWith(LatestRunIdPrefix, StringComparison.Ordinal));

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;

        /// <summary>Confirmed Job Order references feeding the run (comma-separated; source lock).</summary>
        public string JobOrderRefs { get; set; } = string.Empty;

        /// <summary>QUICK or OPTIMIZE.</summary>
        public string EngineStage { get; set; } = "QUICK";
        public int EffortLevel { get; set; }
        public bool RotationEnabled { get; set; } = true;

        public int PieceCount { get; set; }
        public int SheetsUsed { get; set; }
        public int UnplacedCount { get; set; }
        public double UtilizationPct { get; set; }

        public string MarginsJson { get; set; } = string.Empty;
        public string StocksJson { get; set; } = string.Empty;
        public string PiecesJson { get; set; } = string.Empty;
        public string ResultJson { get; set; } = string.Empty;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
