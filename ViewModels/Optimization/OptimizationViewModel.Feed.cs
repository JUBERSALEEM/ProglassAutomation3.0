// ════════════════════════════════════════════════════════════════════════
//  Live feed — hands the run currently on screen to Step 6 and Step 9.
//
//  A saved session is the durable contract (see OptimizationResultFeed), but
//  sessions are only written when the operator presses SAVE. Requiring a save
//  before a quote could see the real sheet count would make the bridge look
//  broken most of the time, so every finished run also publishes itself here.
//
//  Direction of dependency: ViewModels → Services, which is the allowed way
//  round. The feed never reaches back into this class.
// ════════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ProGlassAutomation.Services.Optimization;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel
    {
        /// <summary>
        /// The run currently on screen, expanded to one row per Step 5 spec
        /// pane — the shape Step 6 and Step 9 consume.
        ///
        /// Racks with no recorded pane (hand-added stock, or a job imported
        /// without Step 4 sheets) are skipped rather than attributed to a
        /// guessed spec: a number attached to the wrong pane is worse than no
        /// number at all.
        /// </summary>
        public IReadOnlyList<OptimizationPaneResult> BuildLivePaneResults()
        {
            var rows = new List<OptimizationPaneResult>();
            if (Pieces.Select(p => p.ProformaId).Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any()) return rows;

            // PHASE 4 / finding 4c (2026-09-20): a cutting group whose plan
            // FAILED an oracle is excluded from the feed. Before this, every
            // finished run published all of its rows regardless of the verdict,
            // so a group with overlapping placements, a margin breach or a lost
            // piece still drove the quote's wastage and sheet charges. The plan
            // stays on screen for inspection — it is the export and the bridge
            // that must refuse it, because those are the paths that turn a
            // number into glass somebody cuts and somebody invoices.
            var failedCompositions = new HashSet<string>(
                _groupRuns.Where(r => r.ValidationFailed)
                          .Select(r => r.Composition ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            foreach (var c in CostingRows)
            {
                if (c.Panes.Count == 0) continue;

                if (failedCompositions.Contains(c.Composition ?? string.Empty))
                {
                    ExcludedFeedCompositions.Add(c.Composition ?? string.Empty);
                    continue;
                }

                foreach (var pane in c.Panes)
                {
                    if (pane == null || pane.SpecNumber <= 0) continue;

                    rows.Add(new OptimizationPaneResult
                    {
                        SpecNumber = pane.SpecNumber,
                        Side = OptimizationResultFeed.NormalizeSide(pane.Side),
                        Composition = c.Composition,
                        Thickness = c.Thickness,
                        SheetWidthMm = c.WidthMm,
                        SheetHeightMm = c.HeightMm,
                        SheetQuantity = c.SheetCount,
                        PieceCount = c.PieceCount,
                        UtilizationPercent = c.UtilizationPercent,
                        GlassSqm = c.GlassM2,
                        StockSqm = c.StockM2,
                        WasteSqm = c.WasteM2Value,
                        RatePerSqm = c.Rate,
                        HasPrice = c.HasPrice,
                        IsSharedAcrossSpecs = c.IsSharedAcrossSpecs,
                        SharedWithSpecs = c.SharedWithSpecs,
                        SessionName = LastRunStage + " run (not saved)",
                        SessionCreatedUtc = DateTime.UtcNow
                    });
                }
            }

            return rows;
        }

        /// <summary>
        /// Compositions withheld from the last feed because their plan failed an
        /// oracle. Read by <see cref="PublishLiveFeed"/> so the label says what
        /// was withheld rather than leaving a silently short feed.
        /// </summary>
        private readonly HashSet<string> ExcludedFeedCompositions =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Publishes the finished run to the quoting steps. Called from
        /// ApplyResults, so the feed always describes the plan on screen.
        /// </summary>
        private void PublishLiveFeed()
        {
            ExcludedFeedCompositions.Clear();
            var rows = BuildLivePaneResults();
            int withheld = ExcludedFeedCompositions.Count;

            string label;
            if (rows.Count == 0 && withheld > 0)
            {
                // Every group failed an oracle. Saying "no pane identity" here
                // would send the operator to the STOCKS tab to fix something
                // that is not a STOCKS problem.
                label = string.Format(CultureInfo.InvariantCulture,
                    "live run withheld from Step 6 — {0} cutting group(s) failed layout validation: {1}",
                    withheld, string.Join(", ", ExcludedFeedCompositions.ToArray()));
            }
            else if (rows.Count == 0)
            {
                label = "live run has no Step 4 pane identity — import the Step 4 sheets " +
                        "on the STOCKS tab to link it to a spec";
            }
            else
            {
                label = string.Format(CultureInfo.InvariantCulture,
                    "live {0} run · {1:yyyy-MM-dd HH:mm} · {2} pane row(s){3}",
                    LastRunStage, DateTime.Now, rows.Count,
                    withheld > 0
                        ? string.Format(CultureInfo.InvariantCulture,
                            " · {0} group(s) WITHHELD after failing validation ({1})",
                            withheld, string.Join(", ", ExcludedFeedCompositions.ToArray()))
                        : string.Empty);
            }

            if (withheld > 0)
            {
                Services.Logging.AppLogService.Warning(
                    $"[OPT-FEED] {withheld} cutting group(s) withheld from the Step 6 feed after " +
                    $"failing layout validation: {string.Join(", ", ExcludedFeedCompositions.ToArray())}",
                    new Services.Logging.LogContext { Module = "Optimization", Operation = "PublishLiveFeed" });
            }

            OptimizationResultFeed.PublishLive(rows, label,
                // Stamped so Step 6 of a DIFFERENT quote cannot read this run.
                // PHASE 3 note: this key can be a JOB ORDER id when the run was
                // driven from the JOBS tab with exactly one job ticked, while
                // Step 6 compares against a PROFORMA id — see
                // OptimizationDocumentKey, which reconciles the two.
                DocumentKey,
                runId: _lastRunId,
                inputFingerprint: _lastInputFingerprint);
        }
    }
}
