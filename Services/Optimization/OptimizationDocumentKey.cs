// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/OptimizationDocumentKey.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — document identity
//
// PHASE 3 / finding 2 (2026-09-20).
//
// THE BUG
//   An optimization run is stamped with a DocumentKey so a quote cannot read
//   another quote's figures. Two callers stamp it, in two different identity
//   namespaces:
//
//     • Step 5's headless host stamps the PROFORMA id
//       (ProformaInvoiceViewModel.RunOptimizationFromStep5 →
//        host.DocumentKey = CurrentDocumentKey = ActiveInvoiceContext.ProformaId)
//     • the module stamps the JOB ORDER id when exactly one job is ticked
//       (OptimizationViewModel.Run → DocumentKey = selectedJoIds[0])
//
//   JobOrderMapper.DeriveJobOrderId is a pure prefix swap — "PI-2026-004" →
//   "JOB-2026-004" — so the two are deterministically related and NEVER equal.
//   Everything downstream compared them with a plain string equality:
//
//     • OptimizationResultFeed.IsForDocument → Step 6's guard rejected the run,
//       so the single most common production action (tick one released job,
//       optimize it) produced figures the source quote silently refused.
//     • SavedOptimizationSession.LatestRunIdFor → the run was stored under
//       "latest-run:JOB-…" while Step 5's VIEW RESULT looked up
//       "latest-run:PI-…", so each quote accumulated two rows and neither
//       could find the other's run.
//
//   Feed.cs even documented the opposite intent — "Empty for a run driven from
//   the OPTIMIZATION tab, which has no proforma identity" — which the single-job
//   stamping contradicted.
//
// THE FIX
//   One canonical form, applied at every COMPARISON and every STORAGE key. The
//   run label keeps whatever id the operator needs to see (a job order id on the
//   JOBS tab is correct and useful); only matching and persistence are
//   canonicalised. Nothing has to stop stamping the job id.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Services.Optimization
{
    /// <summary>
    /// Reconciles the two spellings of one document identity so a run stamped
    /// with a job order id is recognised as belonging to the proforma it was
    /// converted from.
    /// </summary>
    public static class OptimizationDocumentKey
    {
        /// <summary>Proforma invoice prefix — the canonical namespace.</summary>
        public const string ProformaPrefix = "PI-";

        /// <summary>Job order prefix, as produced by
        /// <c>JobOrderMapper.DeriveJobOrderId</c>.</summary>
        public const string JobOrderPrefix = "JOB-";

        /// <summary>
        /// Reduces any document key to its canonical (proforma) spelling.
        ///
        /// The proforma is canonical because it is the identity the quoting
        /// wizard publishes and the one Step 6 compares against; a job order is
        /// derived FROM a proforma, never the other way round. An empty key
        /// stays empty — it addresses the legacy global run and must keep
        /// meaning that.
        /// </summary>
        public static string Canonicalize(string? key)
        {
            string k = (key ?? string.Empty).Trim();
            if (k.Length == 0) return string.Empty;

            if (k.StartsWith(JobOrderPrefix, StringComparison.OrdinalIgnoreCase))
                return ProformaPrefix + k.Substring(JobOrderPrefix.Length);

            return k;
        }

        /// <summary>
        /// True when two keys address the same document, in either spelling.
        /// Both empty is a match (the legacy global run); one empty and one not
        /// is NOT a match here — callers that want to accept an unstamped
        /// snapshot keep doing that explicitly, as
        /// <see cref="OptimizationResultFeed.IsForDocument"/> does.
        /// </summary>
        public static bool Same(string? left, string? right) =>
            string.Equals(Canonicalize(left), Canonicalize(right), StringComparison.OrdinalIgnoreCase);

        /// <summary>True when the key is a job order id rather than a proforma id.</summary>
        public static bool IsJobOrderKey(string? key) =>
            (key ?? string.Empty).Trim().StartsWith(JobOrderPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
