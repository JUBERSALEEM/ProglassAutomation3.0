// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/GuillotinePacker.FillFirstPlate.cs
//
// Plate pattern applied AFTER the stage's yield winner.
//
// Yield (the default) is untouched: fewest unplaced, then fewest sheets,
// then utilization, and an equal sheet count keeps the incumbent. A fuller
// first plate at the same sheet count is not a better yield.
//
// Fill first plate is a second legal packer. It finishes the open sheet
// with any remaining piece that fits before it opens another sheet, and it
// scores a candidate by the glass a one-step completion can still land on
// that sheet — not by the next piece alone. Greedy largest-first reproduces
// the shelf on mixed panes; the completion estimate is what lets a smaller
// opener win when the finished plate holds more glass.
//
// Higher stages add candidate windows without dropping the lower ones, the
// same prefix rule as the pattern beam. Kerf, margins, rotation and the
// minimum breakout are enforced on every trial. An illegal fill never
// replaces the yield plan.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace ProGlassAutomation.Services.Optimization
{
    public static partial class GuillotinePacker
    {
        // Above this, nested completion is too expensive for a shop click.
        // The pattern still fills each plate before opening the next; it just
        // stops estimating the rest of the sheet.
        private const int FillFirstLookaheadPieceLimit = 36;

        // A full window prefix on a large list is several complete passes.
        // Keep the stage-1 window and the widest window so the search still
        // widens, without five full passes of a multi-thousand-piece list.
        private const int FillFirstPrefixWindowPieceLimit = 400;

        private const int FillFirstTrialCap = 96;

        private static readonly int[] FillWindowStage1 = { 8 };
        private static readonly int[] FillWindowStage2 = { 8, 12 };
        private static readonly int[] FillWindowStage3 = { 8, 12, 16 };
        private static readonly int[] FillWindowStage4 = { 8, 12, 16, 24 };
        private static readonly int[] FillWindowStage5 = { 8, 12, 16, 24, 32 };

        private static readonly SplitMode[] FillSplits =
        {
            SplitMode.HorizontalFirst,
            SplitMode.VerticalFirst
        };

        private static readonly OrderBy[] FillOrderArea = { OrderBy.Area };
        private static readonly OrderBy[] FillOrderStage2 = { OrderBy.Area, OrderBy.MaxSide };
        private static readonly OrderBy[] FillOrderStage3 = { OrderBy.Area, OrderBy.MaxSide, OrderBy.MinSide };
        private static readonly OrderBy[] FillOrderStage4 = { OrderBy.Area, OrderBy.MaxSide, OrderBy.MinSide, OrderBy.Height };
        private static readonly OrderBy[] FillOrderStage5 =
        {
            OrderBy.Area, OrderBy.MaxSide, OrderBy.MinSide, OrderBy.Height, OrderBy.Width
        };

        /// <summary>
        /// Candidate windows for a fill-first stage. Higher stages start with
        /// the lower stage's windows, so a wider search cannot drop a plan
        /// the narrower search already found.
        /// </summary>
        internal static int[] FillFirstWindows(int effort)
        {
            effort = effort < 1 ? 1 : effort > 5 ? 5 : effort;
            return effort switch
            {
                1 => FillWindowStage1,
                2 => FillWindowStage2,
                3 => FillWindowStage3,
                4 => FillWindowStage4,
                _ => FillWindowStage5
            };
        }

        private struct SheetSnap
        {
            public List<PlacedPiece> Pieces;
            public List<FreeRect> Free;
            // PHASE 0: cuts are part of the sheet state being rolled back. A
            // trial that is undone must not leave its cut behind.
            public List<CutStep> Cuts;
            public long Area;
        }

        private struct FillTrial
        {
            public int Index;
            public int X, Y, W, H;
            public bool Rotated;
            public SplitMode Split;
        }

        private static SheetSnap CaptureSheet(SheetState sheet) => new()
        {
            Pieces = sheet.Layout.Pieces.ToList(),
            Free = sheet.Free.ToList(),
            Cuts = sheet.Layout.Cuts.ToList(),
            Area = sheet.PlacedArea
        };

        private static void RestoreSheet(SheetState sheet, SheetSnap snap)
        {
            sheet.Layout.Pieces.Clear();
            sheet.Layout.Pieces.AddRange(snap.Pieces);
            sheet.Free.Clear();
            sheet.Free.AddRange(snap.Free);
            sheet.Layout.Cuts.Clear();
            sheet.Layout.Cuts.AddRange(snap.Cuts);
            sheet.RefreshAll();
        }

        private static bool SheetBreakoutOk(SheetState sheet, MarginProfile margins)
        {
            int min = margins.MinBreakoutMm;
            if (min <= 0) return true;
            int kerf = margins.KerfMm;
            var ps = sheet.Layout.Pieces;
            for (int i = 0; i < ps.Count; i++)
            {
                var a = ps[i];
                for (int j = i + 1; j < ps.Count; j++)
                {
                    var b = ps[j];
                    bool xOverlap = a.X < b.X + b.WidthMm && b.X < a.X + a.WidthMm;
                    bool yOverlap = a.Y < b.Y + b.HeightMm && b.Y < a.Y + a.HeightMm;
                    if (xOverlap == yOverlap) continue;

                    int gap = xOverlap
                        ? Math.Max(b.Y - (a.Y + a.HeightMm), a.Y - (b.Y + b.HeightMm))
                        : Math.Max(b.X - (a.X + a.WidthMm), a.X - (b.X + b.WidthMm));
                    int offcut = gap - kerf;
                    if (offcut > 0 && offcut < min) return false;
                }
            }
            return true;
        }

        private static bool TryCommitFill(
            SheetState sheet, CutPiece piece, int w, int h, bool rotated,
            FreeRect rect, SplitMode split, MarginProfile margins)
        {
            try
            {
                Commit(sheet, new PlacedPiece
                {
                    Piece = piece,
                    X = rect.X,
                    Y = rect.Y,
                    WidthMm = w,
                    HeightMm = h,
                    Rotated = rotated
                }, margins, split, margins.MinBreakoutMm);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            return SheetBreakoutOk(sheet, margins);
        }

        private static bool CommitFirstLegal(
            SheetState sheet, CutPiece piece, MarginProfile margins, RotationPolicy rotation)
        {
            foreach (var ori in Orientations(piece, rotation))
            {
                if (ori.W > sheet.MaxFreeW || ori.H > sheet.MaxFreeH) continue;
                for (int r = 0; r < sheet.Free.Count; r++)
                {
                    var rect = sheet.Free[r];
                    if (ori.W > rect.W || ori.H > rect.H) continue;
                    foreach (var split in FillSplits)
                    {
                        var snap = CaptureSheet(sheet);
                        if (TryCommitFill(sheet, piece, ori.W, ori.H, ori.Rotated, rect, split, margins))
                            return true;
                        RestoreSheet(sheet, snap);
                    }
                }
            }
            return false;
        }

        private static long GreedyComplete(
            SheetState sheet, List<CutPiece> remaining, int skip,
            MarginProfile margins, RotationPolicy rotation, int window,
            CancellationToken cancellationToken)
        {
            var snap = CaptureSheet(sheet);
            var used = new bool[remaining.Count];
            if (skip >= 0 && skip < used.Length) used[skip] = true;
            int guard = 0;
            while (guard++ < remaining.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int best = -1;
                long bestArea = -1;
                int seen = 0;
                for (int i = 0; i < remaining.Count; i++)
                {
                    if (used[i]) continue;
                    if (seen >= window) break;
                    seen++;
                    long area = (long)remaining[i].WidthMm * remaining[i].HeightMm;
                    if (area <= bestArea) continue;
                    var probe = CaptureSheet(sheet);
                    bool fits = CommitFirstLegal(sheet, remaining[i], margins, rotation);
                    RestoreSheet(sheet, probe);
                    if (!fits) continue;
                    best = i;
                    bestArea = area;
                }
                if (best < 0) break;
                if (!CommitFirstLegal(sheet, remaining[best], margins, rotation)) break;
                used[best] = true;
            }
            long total = sheet.PlacedArea;
            RestoreSheet(sheet, snap);
            return total;
        }

        private static List<FillTrial> CollectFillTrials(
            SheetState sheet, List<CutPiece> remaining, MarginProfile margins,
            RotationPolicy rotation, int window, bool unlimitedTrials)
        {
            var trials = new List<FillTrial>(64);
            int scan = Math.Min(window, remaining.Count);
            for (int i = 0; i < scan; i++)
            {
                var piece = remaining[i];
                foreach (var ori in Orientations(piece, rotation))
                {
                    if (ori.W > sheet.MaxFreeW || ori.H > sheet.MaxFreeH) continue;
                    for (int r = 0; r < sheet.Free.Count; r++)
                    {
                        var rect = sheet.Free[r];
                        if (ori.W > rect.W || ori.H > rect.H) continue;
                        foreach (var split in FillSplits)
                        {
                            trials.Add(new FillTrial
                            {
                                Index = i,
                                X = rect.X,
                                Y = rect.Y,
                                W = ori.W,
                                H = ori.H,
                                Rotated = ori.Rotated,
                                Split = split
                            });
                            if (!unlimitedTrials && trials.Count >= FillFirstTrialCap)
                                return trials;
                        }
                    }
                }
            }
            return trials;
        }

        private static bool PlaceNextFill(
            SheetState sheet, List<CutPiece> remaining, MarginProfile margins,
            RotationPolicy rotation, int window, bool deep, bool unlimitedTrials,
            CancellationToken cancellationToken)
        {
            if (remaining.Count == 0) return false;
            cancellationToken.ThrowIfCancellationRequested();
            var trials = CollectFillTrials(sheet, remaining, margins, rotation, window, unlimitedTrials);
            bool have = false;
            long bestEstimated = -1;
            FillTrial best = default;
            for (int t = 0; t < trials.Count; t++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trial = trials[t];
                var snap = CaptureSheet(sheet);
                var host = new FreeRect(trial.X, trial.Y, trial.W, trial.H);
                bool committed = TryCommitFill(
                    sheet, remaining[trial.Index], trial.W, trial.H, trial.Rotated,
                    host, trial.Split, margins);
                if (!committed)
                {
                    RestoreSheet(sheet, snap);
                    continue;
                }
                long estimated = deep
                    ? GreedyComplete(sheet, remaining, trial.Index, margins, rotation, window, cancellationToken)
                    : sheet.PlacedArea;
                RestoreSheet(sheet, snap);
                if (!have || estimated > bestEstimated)
                {
                    have = true;
                    bestEstimated = estimated;
                    best = trial;
                }
            }
            if (!have) return false;

            var commitSnap = CaptureSheet(sheet);
            if (!TryCommitFill(sheet, remaining[best.Index], best.W, best.H, best.Rotated,
                    new FreeRect(best.X, best.Y, best.W, best.H), best.Split, margins))
            {
                RestoreSheet(sheet, commitSnap);
                return false;
            }
            remaining.RemoveAt(best.Index);
            return true;
        }

        private static SheetState OpenSheetForAny(
            List<CutPiece> remaining, IReadOnlyList<StockSize> stocks, int[] per,
            MarginProfile margins, RotationPolicy rotation, int alreadyOpen)
        {
            for (int i = 0; i < remaining.Count; i++)
            {
                var state = OpenBlank(stocks, per, remaining[i], margins, rotation, alreadyOpen);
                if (state == null) continue;
                if (i != 0)
                {
                    var opener = remaining[i];
                    remaining.RemoveAt(i);
                    remaining.Insert(0, opener);
                }
                return state;
            }
            return null;
        }

        private static long LeadingArea(OptimizationResult result)
        {
            if (result == null || result.Sheets.Count == 0) return 0;
            long area = 0;
            var placed = result.Sheets[0].Pieces;
            for (int i = 0; i < placed.Count; i++)
                area += (long)placed[i].WidthMm * placed[i].HeightMm;
            return area;
        }

        private static long TotalGlass(OptimizationResult result)
        {
            if (result == null) return 0;
            long area = 0;
            for (int s = 0; s < result.Sheets.Count; s++)
            {
                var placed = result.Sheets[s].Pieces;
                for (int i = 0; i < placed.Count; i++)
                    area += (long)placed[i].WidthMm * placed[i].HeightMm;
            }
            return area;
        }

        private static string SheetPct(OptimizationResult result)
        {
            if (result == null || result.Sheets.Count == 0) return "0.00%";
            return result.Sheets[0].UtilizationPct.ToString("0.00", CultureInfo.InvariantCulture) + "%";
        }

        private static OptimizationResult ClonePlan(OptimizationResult source, string strategy)
        {
            var copy = new OptimizationResult { Rotation = source.Rotation, Strategy = strategy };
            for (int i = 0; i < source.Unplaced.Count; i++)
                copy.Unplaced.Add(source.Unplaced[i]);
            for (int s = 0; s < source.Sheets.Count; s++)
            {
                var sheet = source.Sheets[s];
                var layout = new SheetLayout
                {
                    SheetNo = sheet.SheetNo,
                    Stock = sheet.Stock,
                    UsableX = sheet.UsableX,
                    UsableY = sheet.UsableY,
                    UsableWidth = sheet.UsableWidth,
                    UsableHeight = sheet.UsableHeight
                };
                for (int p = 0; p < sheet.Pieces.Count; p++)
                {
                    var piece = sheet.Pieces[p];
                    layout.Pieces.Add(new PlacedPiece
                    {
                        Piece = piece.Piece,
                        X = piece.X,
                        Y = piece.Y,
                        WidthMm = piece.WidthMm,
                        HeightMm = piece.HeightMm,
                        Rotated = piece.Rotated
                    });
                }
                // PHASE 0: the clone is a repack of an already-cut plan, so it
                // inherits that plan's cut sequence rather than losing it.
                for (int c = 0; c < sheet.Cuts.Count; c++)
                {
                    var cut = sheet.Cuts[c];
                    layout.Cuts.Add(new CutStep
                    {
                        Seq = cut.Seq,
                        Axis = cut.Axis,
                        AtMm = cut.AtMm,
                        FromX = cut.FromX,
                        FromY = cut.FromY,
                        FromW = cut.FromW,
                        FromH = cut.FromH,
                        Stage = cut.Stage,
                        IsTrim = cut.IsTrim
                    });
                }
                copy.Sheets.Add(layout);
            }
            return copy;
        }

        private static bool FillPlanOk(
            OptimizationResult plan, IReadOnlyList<CutPiece> pieces, MarginProfile margins)
            => plan != null && VerifyComprehensive(plan, pieces, margins).Count == 0;

        private static bool FillBetter(
            OptimizationResult challenger, OptimizationResult incumbent,
            IReadOnlyList<CutPiece> pieces, MarginProfile margins)
        {
            bool challengerOk = FillPlanOk(challenger, pieces, margins);
            if (!challengerOk) return false;
            if (incumbent == null || !FillPlanOk(incumbent, pieces, margins)) return true;
            if (challenger.Unplaced.Count != incumbent.Unplaced.Count)
                return challenger.Unplaced.Count < incumbent.Unplaced.Count;
            if (challenger.Sheets.Count != incumbent.Sheets.Count)
                return challenger.Sheets.Count < incumbent.Sheets.Count;
            long challengerLead = LeadingArea(challenger);
            long incumbentLead = LeadingArea(incumbent);
            if (challengerLead != incumbentLead) return challengerLead > incumbentLead;
            return TotalGlass(challenger) > TotalGlass(incumbent);
        }

        private static OrderBy[] FillOrders(int effort, bool widestAndIncomplete)
        {
            if (!widestAndIncomplete) return FillOrderArea;
            return effort switch
            {
                <= 1 => FillOrderArea,
                2 => FillOrderStage2,
                3 => FillOrderStage3,
                4 => FillOrderStage4,
                _ => FillOrderStage5
            };
        }

        private static int[] WindowsForJob(int pieceCount, int effort)
        {
            var full = FillFirstWindows(effort);
            if (pieceCount <= FillFirstPrefixWindowPieceLimit || full.Length <= 2)
                return full;
            return new[] { full[0], full[full.Length - 1] };
        }

        private static OptimizationResult RunFillFirstPass(
            IReadOnlyList<CutPiece> pieces, IReadOnlyList<StockSize> stocks,
            MarginProfile margins, RotationPolicy rotation, OrderBy order, int window,
            CancellationToken cancellationToken, IProgress<OptimizeProgress> progress)
        {
            var remaining = Sort(pieces, order);
            var sheets = new List<SheetState>();
            var per = new int[stocks.Count];
            var unplaced = new List<CutPiece>();
            int total = pieces.Count;
            bool unlimitedTrials = total <= 24;
            string strategy = "FILL FIRST PLATE";

            while (remaining.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool deep = total <= FillFirstLookaheadPieceLimit
                    && (total <= 18 || sheets.Count == 0);
                var state = OpenSheetForAny(remaining, stocks, per, margins, rotation, sheets.Count);
                if (state == null)
                {
                    unplaced.AddRange(remaining);
                    break;
                }

                // Stay near the end of the bar. Yield has already reported its own
                // pass; dropping back to 0 here reads as a restart.
                progress?.Report(OptimizeProgress.Of("fill", 99, 100,
                    "Filling plate " + (sheets.Count + 1).ToString(CultureInfo.InvariantCulture)));
                sheets.Add(state);
                int guard = 0;
                int before = remaining.Count;
                while (remaining.Count > 0 && guard++ <= before
                    && PlaceNextFill(state, remaining, margins, rotation, window, deep,
                        unlimitedTrials, cancellationToken))
                {
                }

                if (state.Layout.Pieces.Count == 0)
                {
                    per[state.StockIndex] = Math.Max(0, per[state.StockIndex] - 1);
                    sheets.RemoveAt(sheets.Count - 1);
                    unplaced.Add(remaining[0]);
                    remaining.RemoveAt(0);
                }
            }

            var preserved = ClonePlan(
                FinishFromSheets(sheets, unplaced, rotation, strategy), strategy);
            if (sheets.Count > 1)
            {
                ConsolidateAll(sheets, margins, rotation, SplitMode.HorizontalFirst,
                    int.MaxValue, margins.MinBreakoutMm, cancellationToken);
                var squeezed = FinishFromSheets(sheets, unplaced, rotation, strategy);
                if (FillBetter(squeezed, preserved, pieces, margins))
                    return squeezed;
            }
            return preserved;
        }

        /// <summary>
        /// Legal fill-first plan for one stage. Does not consult the yield
        /// portfolio — the caller decides whether it may replace that plan.
        /// </summary>
        internal static OptimizationResult PackFillFirstPlate(
            IReadOnlyList<CutPiece> pieces,
            IReadOnlyList<StockSize> stocks,
            MarginProfile margins,
            RotationPolicy rotation,
            int effortLevel,
            CancellationToken cancellationToken = default,
            IProgress<OptimizeProgress> progress = null)
        {
            if (pieces == null) throw new ArgumentNullException(nameof(pieces));
            if (stocks == null || stocks.Count == 0)
                throw new ArgumentException("At least one stock size is required.", nameof(stocks));
            if (margins == null) throw new ArgumentNullException(nameof(margins));

            int effort = effortLevel < 1 ? 1 : effortLevel > 5 ? 5 : effortLevel;
            if (pieces.Count == 0)
                return new OptimizationResult { Rotation = rotation, Strategy = "FILL FIRST PLATE" };

            var windows = WindowsForJob(pieces.Count, effort);
            int widest = 0;
            for (int i = 0; i < windows.Length; i++)
                if (windows[i] > widest) widest = windows[i];

            OptimizationResult best = null;
            int bestWindow = windows[0];
            int previous = 0;
            for (int w = 0; w < windows.Length; w++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int window = Math.Min(windows[w], Math.Max(pieces.Count, 1));
                if (window <= previous) continue;
                previous = window;
                bool extraOrders = window == Math.Min(widest, pieces.Count) && window < pieces.Count;
                var orders = FillOrders(effort, extraOrders);
                for (int o = 0; o < orders.Length; o++)
                {
                    var plan = RunFillFirstPass(pieces, stocks, margins, rotation, orders[o], window,
                        cancellationToken, progress);
                    if (FillBetter(plan, best, pieces, margins))
                    {
                        best = plan;
                        bestWindow = window;
                    }
                }
            }

            best ??= new OptimizationResult { Rotation = rotation };
            best.Strategy = string.Format(CultureInfo.InvariantCulture,
                "FILL FIRST PLATE (stage {0}, window {1})", effort, bestWindow);
            return best;
        }

        private static OptimizationResult PreferFillFirstPlate(
            OptimizationResult current, OptimizationResult fill)
        {
            if (current == null) return fill;
            if (fill == null)
            {
                current.ScaleNotes.Add("Plate pattern: fill first plate produced no plan. Yield plan kept.");
                return current;
            }
            if (fill.HasValidationFailure)
            {
                current.ScaleNotes.Add("Plate pattern: fill first plate failed layout validation. Yield plan kept.");
                return current;
            }
            if (current.HasValidationFailure && fill.Unplaced.Count <= current.Unplaced.Count)
            {
                fill.ScaleNotes.Add(
                    "Plate pattern: fill first plate replaced a yield plan that failed layout validation.");
                return fill;
            }
            if (fill.Unplaced.Count > current.Unplaced.Count)
            {
                current.ScaleNotes.Add("Plate pattern: fill first plate left more pieces unplaced. Yield plan kept.");
                return current;
            }
            if (fill.Unplaced.Count < current.Unplaced.Count)
            {
                fill.ScaleNotes.Add("Plate pattern: fill first plate placed pieces the yield plan left out.");
                return fill;
            }
            if (fill.Sheets.Count > current.Sheets.Count)
            {
                current.ScaleNotes.Add("Plate pattern: fill first plate needed more sheets. Yield plan kept.");
                return current;
            }
            if (fill.Sheets.Count < current.Sheets.Count)
            {
                fill.ScaleNotes.Add(string.Format(CultureInfo.InvariantCulture,
                    "Plate pattern: fill first plate used {0} sheet(s) against the yield plan's {1}, so it replaces that plan.",
                    fill.Sheets.Count, current.Sheets.Count));
                return fill;
            }
            if (LeadingArea(fill) > LeadingArea(current))
            {
                fill.ScaleNotes.Add(string.Format(CultureInfo.InvariantCulture,
                    "Plate pattern: fill first plate — sheet 1 is {0} (yield plan sheet 1 was {1} on the same sheet count).",
                    SheetPct(fill), SheetPct(current)));
                return fill;
            }
            current.ScaleNotes.Add(
                "Plate pattern: fill first plate could not fill sheet 1 fuller without more sheets or unplaced pieces. Yield plan kept.");
            return current;
        }

        private static OptimizationResult NoteFillSkipped(OptimizationResult result, PlatePattern platePattern)
        {
            if (platePattern == PlatePattern.FillFirstPlate && result != null)
            {
                result.ScaleNotes.Add(
                    "Plate pattern: fill first plate is not run at industrial quantity. Yield plan kept.");
            }
            return result;
        }

        /// <summary>
        /// Yield returns <paramref name="current"/> unchanged. Fill first plate
        /// may replace it only with a legal plan that does not add unplaced
        /// pieces or sheets, and then only to fill the leading sheet — or
        /// because that plan also uses fewer sheets.
        /// </summary>
        private static OptimizationResult ApplyPlatePattern(
            OptimizationResult current,
            IReadOnlyList<CutPiece> pieces,
            IReadOnlyList<StockSize> stocks,
            MarginProfile margins,
            RotationPolicy rotation,
            int fillEffort,
            PlatePattern platePattern,
            CancellationToken cancellationToken,
            IProgress<OptimizeProgress> progress,
            CutPolicy policy = null)
        {
            if (platePattern != PlatePattern.FillFirstPlate || current == null)
                return current;
            if (pieces == null || pieces.Count == 0)
                return current;
            if (pieces.Count >= IndustrialQuantityThreshold)
                return NoteFillSkipped(current, platePattern);

            // PHASE 1: fill-first-plate rebuilds a sheet by re-pack trials that
            // score placements without the stage budget, so it cannot honour a
            // staged pattern. It is skipped rather than allowed to dilute it.
            if (policy != null && policy.IsDepthConstrained)
            {
                current.ScaleNotes.Add(
                    "Fill first plate skipped: it cannot hold a staged cut pattern. " +
                    "Choose Free guillotine to use it.");
                return current;
            }

            var fill = PackFillFirstPlate(pieces, stocks, margins, rotation, fillEffort,
                cancellationToken, progress);
            fill = CheckCandidate(fill, pieces, margins, cancellationToken);
            var chosen = PreferFillFirstPlate(current, fill);
            if (pieces.Count > FillFirstLookaheadPieceLimit)
            {
                chosen.ScaleNotes.Add(
                    "Fill first plate used the bounded sheet fill — this quantity is above the lookahead limit.");
            }
            return chosen;
        }
    }
}
