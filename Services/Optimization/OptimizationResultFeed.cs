// ════════════════════════════════════════════════════════════════════════
//  OptimizationResultFeed — read-only bridge from a finished optimization
//  run to the quoting steps.
//
//  WHY THIS EXISTS
//  Step 6 WASTAGE CHARGE SETUP asks the operator to type a SHEET QTY and an
//  optimization %. Step 9 then drives its Sheet Details tab from exactly those
//  two numbers (see the "Senior ERP rule" comment in
//  SheetProfitabilityViewModel). But the optimization module already computes
//  both — per rack, from the real cut. Hand-copying them is where the quote
//  silently drifts from the plan.
//
//  HOW ROWS ARE JOINED
//  By (SpecNumber, Side), which both modules take from the SAME Step 5
//  SpecGroup: Step 6's SpecGroupColorSide.SpecNumber/SemanticSideLabel and the
//  optimization rack's imported spec/side. This is structural. Matching on
//  colour instead would be fragile — Step 6 deliberately allows a pane colour
//  to stay blank ("Unresolvable colours stay blank instead of becoming row
//  names"), so a colour join would drop rows without saying so.
//
//  THE SHARED-RACK CAVEAT (important, and the reason for IsSharedAcrossSpecs)
//  The engine groups pieces by COMPOSITION, not by spec. When two specs use the
//  same glass at the same sheet size they are cut from one pool, and after the
//  run there is no record of which sheet belonged to which spec. Utilization is
//  still exact — it is a property of the rack. Sheet quantity is not: it is the
//  shared total. This feed therefore reports the quantity once, flags it, and
//  leaves the split to Step6SheetAllocation's existing weighted allocation,
//  which was written for precisely that job. Inventing a per-spec split here
//  would double the buy.
//
//  LAYERING
//  Services must not reach into ViewModels, so this owns its own read model and
//  parses the persisted ResultJson. Three writers exist and their shapes
//  differ (this module writes List<ResultRowDto>; CuttingWorkflowService writes
//  an anonymous summary; ProductionOptimizationBridge writes "{}"), so parsing
//  is defensive and a foreign shape yields an empty feed rather than a crash.
//
//  STATE — PROCESS-WIDE BY DESIGN
//  Current is a cache over a persisted store, refreshed explicitly, and it
//  always carries a provenance label and a timestamp. That is deliberately NOT
//  the SpecSheetRegistry anti-pattern, where the static map was the only copy
//  of the data and a restart silently zeroed it.
//
//  It is static because "the latest optimization run" is process-wide truth:
//  the optimization module is a single global tool, not a per-invoice one, and
//  this mirrors SpecSheetRegistry and the other static caches in Services.
//  Two proforma windows would share one snapshot, which is the correct reading
//  of that fact rather than a leak.
//
//  Starting a NEW INVOICE deliberately does NOT clear this. The run still
//  happened; discarding it would make the feed useless for the common case of
//  cutting a job and then quoting it. The real hazard — applying one job's
//  figures to another — is handled by making the source unmistakable instead:
//  both switches are opt-in, per row, and every figure carries a provenance
//  label naming the run and when it happened.
//
//  Readers never see a torn snapshot: Set swaps an immutable-by-convention list
//  reference under the gate, so a reader holding the previous reference keeps
//  enumerating a complete list.
// ════════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Optimization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Optimization
{
    /// <summary>
    /// One Step 5 spec pane's slice of a finished optimization run.
    /// </summary>
    public sealed class OptimizationPaneResult
    {
        /// <summary>Step 5 spec group number (SpecGroupColorSide.SpecNumber).</summary>
        public int SpecNumber { get; init; }

        /// <summary>"Outer" or "Inner" — the rack's pane, matching
        /// SpecGroupColorSide.SemanticSideLabel after normalization.</summary>
        public string Side { get; init; } = string.Empty;

        public string Composition { get; init; } = string.Empty;
        public string Thickness { get; init; } = string.Empty;
        public int SheetWidthMm { get; init; }
        public int SheetHeightMm { get; init; }

        /// <summary>Sheets the engine consumed from this rack. When
        /// IsSharedAcrossSpecs is true this is the shared total, not this pane's
        /// own share.</summary>
        public int SheetQuantity { get; init; }

        public int PieceCount { get; init; }

        /// <summary>Real utilization of this rack, 0-100. Exact regardless of
        /// sharing, because utilization belongs to the rack.</summary>
        public decimal UtilizationPercent { get; init; }

        public decimal GlassSqm { get; init; }
        public decimal StockSqm { get; init; }
        public decimal WasteSqm { get; init; }
        public decimal RatePerSqm { get; init; }
        public bool HasPrice { get; init; }

        public bool IsSharedAcrossSpecs { get; init; }
        public int SharedWithSpecs { get; init; }

        public string SessionName { get; init; } = string.Empty;
        public DateTime SessionCreatedUtc { get; init; }

        /// <summary>Where this number came from, in the same spirit as Step 9's
        /// existing "chosen in Step 4" / "from Sheet Store" / "default sheet"
        /// tier labels. Never left implicit.</summary>
        public string Provenance => IsSharedAcrossSpecs
            ? string.Format(CultureInfo.InvariantCulture,
                "optimization run · shared by {0} specs", SharedWithSpecs)
            : "optimization run";
    }

    /// <summary>
    /// Read side of the optimization → quoting bridge.
    /// </summary>
    public static class OptimizationResultFeed
    {
        /// <summary>Mirror of the persisted ResultJson row. Kept private and
        /// separate from the ViewModel's DTO so Services never depends on
        /// ViewModels; JSON is the contract between them.</summary>
        private sealed class RowDto
        {
            public int W { get; set; }
            public int H { get; set; }
            public string Composition { get; set; } = string.Empty;
            public string Thickness { get; set; } = string.Empty;
            public int Sheets { get; set; }
            public int Pieces { get; set; }
            public decimal UtilizationPct { get; set; }
            public decimal GlassSqm { get; set; }
            public decimal StockSqm { get; set; }
            public decimal WasteSqm { get; set; }
            public decimal RatePerSqm { get; set; }
            public bool HasPrice { get; set; }
            public bool IsSharedAcrossSpecs { get; set; }
            public int SharedWithSpecs { get; set; }
            public List<PaneDto> Panes { get; set; } = new();
        }

        private sealed class PaneDto
        {
            public int SpecNumber { get; set; }
            public string Side { get; set; } = string.Empty;
        }

        private static readonly object Gate = new object();

        private static IReadOnlyList<OptimizationPaneResult> _current =
            Array.Empty<OptimizationPaneResult>();

        private static string _sourceLabel = "no optimization run loaded";

        // A run produced in this app session outranks anything on disk: it is
        // strictly newer, and the operator is looking at it. Without this guard
        // a later RefreshAsync would silently replace live figures with an older
        // saved session and the quote would be checked against the wrong plan.
        private static bool _isLive;

        // Identity of the document the snapshot was produced for. Empty means
        // "unstamped" — a run driven from the OPTIMIZATION tab for a Job Order
        // has no proforma identity, and those stay usable everywhere they were
        // usable before, rather than being rejected for lack of a stamp.
        private static string _documentKey = string.Empty;

        private static string _runId = string.Empty;
        private static string _inputFingerprint = string.Empty;
        private static bool _isCurrent;

        /// <summary>The most recently refreshed run, per pane. Empty until
        /// RefreshAsync succeeds — consumers must treat empty as "not available"
        /// and fall back to their own manual numbers, never as zero.</summary>
        public static IReadOnlyList<OptimizationPaneResult> Current
        {
            get { lock (Gate) return _current; }
        }

        /// <summary>Human-readable provenance of <see cref="Current"/>, including
        /// which session it came from and when, so a stale read is visible.</summary>
        /// <remarks>
        /// <para><b>Deliberately English, and deliberately not routed through
        /// <c>LocalizationService</c>.</b> This is a diagnostic string: it names
        /// the session and timestamp a set of figures came from, and it gets read
        /// when a number looks wrong. Everything else in that channel is English
        /// too — the Step 6 wastage mode captions in <c>WastageRow</c>, the Step 9
        /// per-row and header provenance in <c>SheetProfitabilityViewModel</c>, and
        /// the export titles and section names in <c>OptimizationViewModel.Report</c>.
        /// Translating this one string alone would put two languages on the same
        /// diagnostic surface, which is harder to read at the moment it matters
        /// than one consistent language is.</para>
        /// <para>The line being drawn is <i>chrome versus diagnostics</i>. The
        /// operator-facing switches that turn this feed on and off ARE localized —
        /// <c>ProformaInvoice.OptimizationFormula</c> and
        /// <c>ProformaInvoice.ReadFromOptimizationRun</c> — because those belong to
        /// the UI vocabulary the rest of the application translates.</para>
        /// <para>If the diagnostics decision is ever revisited, localize the whole
        /// channel in one pass rather than string by string, and add the keys to
        /// <c>scripts/i18n/{code}.py</c>. Do not add them to
        /// <c>Themes/Languages/*.xaml</c>: those files are generated from the
        /// <c>.py</c> sources by <c>scripts/i18n/build.py</c>, which enforces key
        /// parity across all 15 languages and silently drops any key that was
        /// written into the XAML by hand.</para>
        /// </remarks>
        public static string SourceLabel
        {
            get { lock (Gate) return _sourceLabel; }
        }

        public static bool HasData
        {
            get { lock (Gate) return _current.Count > 0; }
        }

        /// <summary>
        /// Raised after a run publishes itself, so an already-open proforma can
        /// re-resolve instead of quietly showing figures from the previous run.
        ///
        /// Published from OptimizationViewModel.ApplyResults, which resumes on
        /// the UI thread (the run awaits with ConfigureAwait(true)), so handlers
        /// may touch bound collections directly without marshalling.
        ///
        /// Subscribers must unsubscribe: this is a static event and the wizard
        /// view models are shorter-lived than the process. ProformaInvoiceViewModel
        /// already does exactly this for DocumentLifecycle.Changed.
        /// </summary>
        public static event Action? RunPublished;

        /// <summary>True when the current snapshot came from a run in this app
        /// session rather than from disk.</summary>
        public static bool IsLive
        {
            get { lock (Gate) return _isLive; }
        }

        /// <summary>Identity of the document the current snapshot was produced
        /// for. Empty when the run had no document identity.</summary>
        public static string DocumentKey
        {
            get { lock (Gate) return _documentKey; }
        }

        /// <summary>Identity of the run that produced <see cref="Current"/>.</summary>
        public static string RunId
        {
            get { lock (Gate) return _runId; }
        }

        /// <summary>Fingerprint of the cutting list the current run consumed.</summary>
        public static string InputFingerprint
        {
            get { lock (Gate) return _inputFingerprint; }
        }

        /// <summary>
        /// True when Current is a successful run that has not been invalidated
        /// by a later fail / cancel / input change.
        /// </summary>
        public static bool IsCurrent
        {
            get { lock (Gate) return _isCurrent && _current.Count > 0; }
        }

        /// <summary>
        /// Whether the current snapshot may be applied to
        /// <paramref name="documentKey"/>.
        /// </summary>
        /// <remarks>
        /// Refuses a run that was stamped for a DIFFERENT document. Without this
        /// a quote can silently read another job's figures: the join is
        /// structural (spec number + side + sheet size), so two quotes that both
        /// have a "Spec 1 outer" on the same rack size match each other.
        ///
        /// An unstamped snapshot — or a caller with no identity yet, e.g. a draft
        /// that has not been through Step 1 — is accepted, because there is no
        /// evidence of a mismatch and rejecting it would only revive the "read
        /// from optimization does nothing" report this guard exists to prevent.
        /// </remarks>
        public static bool IsForDocument(string? documentKey)
        {
            lock (Gate)
            {
                if (!_isCurrent || _current.Count == 0) return false;
                if (string.IsNullOrWhiteSpace(_documentKey)) return true;
                if (string.IsNullOrWhiteSpace(documentKey)) return true;

                // PHASE 3 / finding 2 (2026-09-20): this was a plain
                // string.Equals, and the two stamping callers use different
                // identity namespaces — Step 5 stamps the PROFORMA id, the
                // module stamps the JOB ORDER id when one job is ticked, and
                // DeriveJobOrderId is only a "PI-" → "JOB-" prefix swap. So a
                // run produced from the JOBS tab could never satisfy this guard,
                // and Step 6 of the source quote silently kept its manual
                // values. Comparison now goes through the canonical form; the
                // two unstamped cases above keep their documented leniency.
                return OptimizationDocumentKey.Same(_documentKey, documentKey);
            }
        }

        /// <summary>
        /// Drops the snapshot and the live flag. Mirrors SpecSheetRegistry.Clear,
        /// whose documented purpose is likewise "new invoice / test isolation":
        /// without it, one test that publishes a live run silently changes what
        /// every later test reads.
        /// </summary>
        public static void Reset() =>
            Set(Array.Empty<OptimizationPaneResult>(),
                "no optimization run loaded", isLive: false, documentKey: string.Empty,
                runId: string.Empty, inputFingerprint: string.Empty, isCurrent: false);

        /// <summary>
        /// Drops CURRENT so a failed, cancelled, or input-changed run cannot
        /// keep driving Step 6 / Step 9.
        /// </summary>
        public static void Invalidate(string reason)
        {
            Set(Array.Empty<OptimizationPaneResult>(),
                string.IsNullOrWhiteSpace(reason) ? "optimization result invalidated" : reason,
                isLive: false, documentKey: string.Empty,
                runId: string.Empty, inputFingerprint: string.Empty, isCurrent: false);
        }

        /// <summary>
        /// Maps a Step 6 pane onto the two racks the optimization module imports
        /// per spec ("Outer" = W1/H1, "Inner" = W2/H2).
        ///
        /// THE WORD "OUTER" IS NOT ENOUGH. WastagesSectionViewModel builds four
        /// kinds of pane, and three of them are cut from the INNER sheet:
        ///
        ///     "Outer (W1/H1)"     -> group.OuterSheetWidthMm  -> Outer
        ///     "Inner (W2/H2)"     -> group.InnerSheetWidthMm  -> Inner
        ///     "Lam Outer (W2/H2)" -> group.InnerSheetWidthMm  -> Inner
        ///     "Lam Inner (W2/H2)" -> group.InnerSheetWidthMm  -> Inner
        ///     "Middle (W2/H2)"    -> group.InnerSheetWidthMm  -> Inner
        ///
        /// So a naive substring test sends "Lam Outer" to the outer rack, and
        /// SpecGroupColorSide.SemanticSideLabel sends "Middle" to "Outer" by
        /// fall-through. Both would price a laminated pane against the wrong
        /// sheet. The coordinate key the SideLabel already carries — W1/H1 versus
        /// W2/H2 — is the ground truth, so it is tested first and the pane words
        /// are only a fallback for a label that has lost it.
        /// </summary>
        public static string NormalizeSide(string? sideLabel)
        {
            if (string.IsNullOrWhiteSpace(sideLabel)) return string.Empty;

            string s = sideLabel.Trim();

            // Ground truth: which pane coordinate set the sheet came from.
            if (s.IndexOf("W1/H1", StringComparison.OrdinalIgnoreCase) >= 0) return "Outer";
            if (s.IndexOf("W2/H2", StringComparison.OrdinalIgnoreCase) >= 0) return "Inner";

            // Fallback for a bare semantic label. Every laminated and middle
            // pane is built on the inner sheet, so "Lam"/"Middle" win over the
            // word "Outer" that follows them.
            if (s.IndexOf("Lam", StringComparison.OrdinalIgnoreCase) >= 0) return "Inner";
            if (s.IndexOf("Middle", StringComparison.OrdinalIgnoreCase) >= 0) return "Inner";
            if (s.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0) return "Inner";
            if (s.IndexOf("Outer", StringComparison.OrdinalIgnoreCase) >= 0) return "Outer";

            return string.Empty;
        }

        /// <summary>
        /// Loads the newest saved optimization session and expands its rack rows
        /// into one result per (spec, side) pane. Safe to call repeatedly; a
        /// failure leaves the previous snapshot and label in place rather than
        /// blanking a screen the operator is reading.
        /// </summary>
        public static async Task RefreshAsync(
            string? documentKey = null, CancellationToken ct = default)
        {
            // Never trade a live run for an older saved one.
            lock (Gate)
            {
                if (_isLive && _current.Count > 0) return;
            }

            try
            {
                Services.Logging.AppLogService.Info(
                    $"[OPT-FEED] RefreshAsync doc={documentKey}",
                    new Services.Logging.LogContext { Module = "Optimization", Operation = "RefreshAsync" });
            }
            catch { }

            try
            {
                var repo = new OptimizationSessionRepository();
                await repo.EnsureCreatedAsync(ct).ConfigureAwait(false);

                // This document's own reserved row outranks everything else on
                // disk: it is the plan THIS quote was cut against.
                if (!string.IsNullOrWhiteSpace(documentKey))
                {
                    var own = await repo.GetLatestRunAsync(documentKey, ct)
                        .ConfigureAwait(false);
                    if (own != null)
                    {
                        var ownRows = Parse(own.ResultJson, own.Name, own.CreatedUtc);
                        var identity = OptimizationSessionIdentity.Parse(own.PiecesJson);
                        bool currentValid = OptimizationSessionIdentity.IsCurrentValid(identity);
                        Set(ownRows, DescribeSavedRun(own, ownRows.Count),
                            isLive: false, documentKey: documentKey,
                            runId: identity.RunId,
                            inputFingerprint: identity.InputFingerprint,
                            isCurrent: currentValid && ownRows.Count > 0);
                        return;
                    }
                }

                var sessions = await repo.ListAsync(ct).ConfigureAwait(false);
                if (sessions == null || sessions.Count == 0)
                {
                    Set(Array.Empty<OptimizationPaneResult>(),
                        "no saved optimization run", isLive: false, isCurrent: false);
                    return;
                }

                // Newest first: the plan the operator most recently produced is
                // the one a quote should be checked against.
                var newest = sessions
                    .Where(s => s != null)
                    .OrderByDescending(s => s.CreatedUtc)
                    .First();

                var rows = Parse(newest.ResultJson, newest.Name, newest.CreatedUtc);

                // Unstamped on purpose: a globally-newest session cannot prove
                // which document it belongs to, so it stays usable exactly as it
                // was before stamping existed. The session name rides in the
                // label so the operator can see which run they are looking at.
                Set(rows, DescribeSavedRun(newest, rows.Count),
                    isLive: false, documentKey: string.Empty);
            }
            catch (Exception ex)
            {
                // Never let a reporting bridge break quoting.
                Set(Current, "optimization feed unavailable: " + ex.Message);
            }
        }

        /// <summary>
        /// Publishes a live snapshot taken straight from the optimization
        /// ViewModel's last run, so an operator who has not saved a session still
        /// gets real numbers. Called by the composer, which is the only place
        /// that can see both modules.
        /// </summary>
        public static void PublishLive(
            IReadOnlyList<OptimizationPaneResult> rows, string label,
            string? documentKey = null,
            string? runId = null,
            string? inputFingerprint = null)
        {
            try
            {
                Services.Logging.AppLogService.Info(
                    $"[OPT-FEED] PublishLive doc={documentKey} rows={rows?.Count ?? 0} label={label}",
                    new Services.Logging.LogContext { Module = "Optimization", Operation = "PublishLive" });
            }
            catch { }

            string liveRunId = runId ?? string.Empty;
            Set(rows ?? Array.Empty<OptimizationPaneResult>(),
                string.IsNullOrWhiteSpace(label) ? "live optimization run" : label,
                isLive: true,
                documentKey: documentKey ?? string.Empty,
                runId: liveRunId,
                inputFingerprint: inputFingerprint ?? string.Empty,
                isCurrent: rows != null && rows.Count > 0 && !string.IsNullOrWhiteSpace(liveRunId));

            // Raised outside the lock: a handler that refreshes a wizard step
            // must never run while the snapshot gate is held.
            RunPublished?.Invoke();
        }

        /// <summary>
        /// Finds the run figures for one Step 5 pane. <paramref name="widthMm"/>
        /// is an optional tie-breaker: when two specs share a rack the sheet size
        /// confirms the row belongs to this pane rather than to a look-alike.
        /// Returns null when the run has nothing for this pane — callers must
        /// then keep their manual value and say so.
        /// </summary>
        public static OptimizationPaneResult? Find(
            IReadOnlyList<OptimizationPaneResult>? rows,
            int specNumber,
            string? semanticSide,
            int widthMm = 0,
            int heightMm = 0)
        {
            if (rows == null || rows.Count == 0 || specNumber <= 0) return null;

            string side = NormalizeSide(semanticSide);
            if (side.Length == 0) return null;

            OptimizationPaneResult? fallback = null;

            foreach (var r in rows)
            {
                if (r == null || r.SpecNumber != specNumber) continue;
                if (!string.Equals(r.Side, side, StringComparison.OrdinalIgnoreCase)) continue;

                bool sizeMatches =
                    (widthMm <= 0 || r.SheetWidthMm == widthMm) &&
                    (heightMm <= 0 || r.SheetHeightMm == heightMm);

                if (sizeMatches) return r;
                fallback ??= r;
            }

            // Same spec and side but a different sheet size: still the right
            // pane, the operator just changed the sheet in Step 4 since the run.
            return fallback;
        }

        /// <summary>Provenance line for a run read back from disk.</summary>
        private static string DescribeSavedRun(SavedOptimizationSession session, int rowCount) =>
            rowCount == 0
                ? "saved run has no per-pane breakdown — re-run and save on the OPTIMIZATION tab"
                : string.Format(CultureInfo.InvariantCulture,
                    "optimization run \"{0}\" · {1:yyyy-MM-dd HH:mm} · {2} pane row(s)",
                    session.Name, session.CreatedUtc.ToLocalTime(), rowCount);

        /// <summary>Publishes one run as the current snapshot.</summary>
        /// <param name="isLive">
        /// Tri-state on purpose: null leaves the live flag alone, which is what a
        /// failed refresh needs — it must not demote a live snapshot to "from
        /// disk" just because a read threw, or the next refresh would happily
        /// overwrite newer live figures with an older saved session.
        /// </param>
        private static void Set(
            IReadOnlyList<OptimizationPaneResult> rows, string label, bool? isLive = null,
            string? documentKey = null,
            string? runId = null,
            string? inputFingerprint = null,
            bool? isCurrent = null)
        {
            lock (Gate)
            {
                _current = rows ?? Array.Empty<OptimizationPaneResult>();
                _sourceLabel = string.IsNullOrWhiteSpace(label)
                    ? "no optimization run loaded"
                    : label;

                if (isLive.HasValue) _isLive = isLive.Value;

                // null = "leave the stamp alone" (an error path must not unstamp
                // a snapshot it did not replace); empty = "explicitly unstamped".
                if (documentKey != null) _documentKey = documentKey.Trim();
                if (runId != null) _runId = runId.Trim();
                if (inputFingerprint != null) _inputFingerprint = inputFingerprint.Trim();
                if (isCurrent.HasValue) _isCurrent = isCurrent.Value && _current.Count > 0;
            }
        }

        /// <summary>
        /// Expands persisted rack rows into per-pane results. Tolerates the two
        /// foreign ResultJson shapes (an anonymous production summary and "{}")
        /// by returning empty rather than throwing.
        /// </summary>
        /// <remarks>
        /// Internal, not private: the test assembly has InternalsVisibleTo and
        /// this parser is the one piece of the bridge that has to shrug off two
        /// foreign ResultJson shapes without throwing, which is worth locking
        /// down with a regression test rather than a comment.
        /// </remarks>
        internal static IReadOnlyList<OptimizationPaneResult> Parse(
            string? resultJson, string sessionName, DateTime createdUtc)
        {
            if (string.IsNullOrWhiteSpace(resultJson))
                return Array.Empty<OptimizationPaneResult>();

            string trimmed = resultJson.Trim();

            // Only this module's shape is an array of rack rows.
            if (trimmed[0] != '[') return Array.Empty<OptimizationPaneResult>();

            List<RowDto>? rows;
            try
            {
                rows = JsonSerializer.Deserialize<List<RowDto>>(trimmed);
            }
            catch (JsonException)
            {
                return Array.Empty<OptimizationPaneResult>();
            }

            if (rows == null || rows.Count == 0)
                return Array.Empty<OptimizationPaneResult>();

            var result = new List<OptimizationPaneResult>();

            foreach (var row in rows)
            {
                if (row == null) continue;

                var panes = row.Panes ?? new List<PaneDto>();

                // A rack with no recorded spec pane (hand-added stock, or a
                // session saved before this bridge existed) cannot be joined to
                // a Step 6 row. Skipped rather than guessed at.
                if (panes.Count == 0) continue;

                foreach (var pane in panes)
                {
                    if (pane == null || pane.SpecNumber <= 0) continue;

                    result.Add(new OptimizationPaneResult
                    {
                        SpecNumber = pane.SpecNumber,
                        Side = NormalizeSide(pane.Side),
                        Composition = row.Composition ?? string.Empty,
                        Thickness = row.Thickness ?? string.Empty,
                        SheetWidthMm = row.W,
                        SheetHeightMm = row.H,
                        SheetQuantity = row.Sheets,
                        PieceCount = row.Pieces,
                        UtilizationPercent = row.UtilizationPct,
                        GlassSqm = row.GlassSqm,
                        StockSqm = row.StockSqm,
                        WasteSqm = row.WasteSqm,
                        RatePerSqm = row.RatePerSqm,
                        HasPrice = row.HasPrice,
                        IsSharedAcrossSpecs = row.IsSharedAcrossSpecs || panes.Count > 1,
                        SharedWithSpecs = row.SharedWithSpecs > 0
                            ? row.SharedWithSpecs
                            : panes.Count,
                        SessionName = sessionName ?? string.Empty,
                        SessionCreatedUtc = createdUtc
                    });
                }
            }

            return result;
        }
    }
}
