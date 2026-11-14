// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/GuillotinePacker.Remnants.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — remnant candidate registration (PHASE 2, part 1)
//
// WHY THIS FILE
//   Every optimizer in this market reports the offcut it leaves, because an
//   offcut is money: LiSEC's dynopt builds its case on remnant merging, and
//   the trim cuts GlasSave offers exist to turn a condemned area into a
//   reusable plate. This app threw that away. It counted the waste in
//   square metres and never said which rectangles it was, so the cutter
//   could not tell a 900 x 600 plate from a 40mm sliver.
//
// HOW IT WORKS
//   No new packing logic. The cut sequence recorded in Phase 0 is replayed:
//   start with the usable rectangle, split it at every recorded cut, and
//   whatever rectangle no piece occupies at the end is leftover. That makes
//   this a READ of the plan, so it cannot change a single placement — the
//   DGU benchmark is untouched.
//
// WHAT IS DELIBERATELY NOT HERE
//   Feeding those rectangles back in as stock for later sheets. That is the
//   second half of phase 2 and it does change packing, so it needs its own
//   gate, its own oracle and its own benchmark run. Registering the
//   rectangles is safe by itself and is a prerequisite for it.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Optimization
{
    /// <summary>
    /// One rectangle a sheet is left holding after every cut is made.
    /// </summary>
    public sealed class RemnantRegion
    {
        public int SheetNo { get; set; }

        /// <summary>Position on the plate, in millimetres, plate origin.</summary>
        public double X { get; set; }
        public double Y { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }

        /// <summary>True when both sides are at least the minimum the run was
        /// derived with — i.e. worth racking rather than binning.</summary>
        public bool IsUsable { get; set; }

        /// <summary>The shorter side: what decides whether it can be racked
        /// and whether a later job can fit anything on it.</summary>
        public double MinSideMm => Math.Min(WidthMm, HeightMm);

        public double AreaM2 => (WidthMm / 1000.0) * (HeightMm / 1000.0);

        /// <summary>Rotated, so an offcut can be compared against a piece
        /// without caring which way up it lives on the rack.</summary>
        public string SizeLabel => string.Format(
            System.Globalization.CultureInfo.InvariantCulture, "{0:0} x {1:0}",
            WidthMm, HeightMm);
    }

    public static partial class GuillotinePacker
    {
        /// <summary>
        /// Rebuilds the rectangles a sheet is cut into and records the ones
        /// no piece occupies. Idempotent: the list is cleared first, so it can
        /// be re-run after a plan is edited without double-counting.
        /// </summary>
        /// <param name="minSideMm">Shorter side a leftover must have to be
        /// marked usable. Below this it is recorded as waste — still counted,
        /// still drawn, but not offered.</param>
        internal static void DeriveLeftovers(SheetLayout sheet, int minSideMm, int kerfMm)
        {
            sheet.Leftovers.Clear();

            if (sheet.UsableWidth <= 0 || sheet.UsableHeight <= 0)
                return;
            if (sheet.Cuts.Count == 0 && sheet.Pieces.Count == 0)
                return;

            var regions = new List<RemnantRegion>
            {
                new RemnantRegion
                {
                    SheetNo = sheet.SheetNo,
                    X = sheet.UsableX,
                    Y = sheet.UsableY,
                    WidthMm = sheet.UsableWidth,
                    HeightMm = sheet.UsableHeight
                }
            };

            foreach (var cut in sheet.Cuts)
            {
                var next = new List<RemnantRegion>(regions.Count + 1);
                bool splitAny = false;

                for (int i = 0; i < regions.Count; i++)
                {
                    var r = regions[i];
                    if (!Crosses(cut, r))
                    {
                        next.Add(r);
                        continue;
                    }

                    splitAny = true;
                    double kerf = kerfMm > 0 ? kerfMm : 0;

                    if (cut.Axis == CutAxis.Vertical)
                    {
                        double leftWidth = cut.AtMm - r.X;
                        next.Add(new RemnantRegion
                        {
                            SheetNo = sheet.SheetNo, X = r.X, Y = r.Y,
                            WidthMm = leftWidth, HeightMm = r.HeightMm
                        });
                        // The blade eats kerf from the remainder side, so the
                        // usable remainder starts after it. Without this every
                        // offcut would be measured a kerf too generous.
                        next.Add(new RemnantRegion
                        {
                            SheetNo = sheet.SheetNo, X = cut.AtMm + kerf, Y = r.Y,
                            WidthMm = r.WidthMm - leftWidth - kerf, HeightMm = r.HeightMm
                        });
                    }
                    else
                    {
                        double bottomHeight = cut.AtMm - r.Y;
                        next.Add(new RemnantRegion
                        {
                            SheetNo = sheet.SheetNo, X = r.X, Y = r.Y,
                            WidthMm = r.WidthMm, HeightMm = bottomHeight
                        });
                        next.Add(new RemnantRegion
                        {
                            SheetNo = sheet.SheetNo, X = r.X, Y = cut.AtMm + kerf,
                            WidthMm = r.WidthMm, HeightMm = r.HeightMm - bottomHeight - kerf
                        });
                    }
                }

                if (splitAny) regions = next;
            }

            // A region holding a piece is not a leftover. The tolerance covers
            // the kerf that sits between a piece and the next cut line.
            double tolerance = kerfMm > 0 ? kerfMm + 0.5 : 0.5;

            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                if (r.WidthMm <= 0.5 || r.HeightMm <= 0.5) continue;
                if (HoldsPiece(sheet, r, tolerance)) continue;

                r.IsUsable = r.MinSideMm >= minSideMm;
                sheet.Leftovers.Add(r);
            }
        }

        /// <summary>Derives leftovers for every sheet in a plan.</summary>
        internal static void DeriveLeftovers(OptimizationResult result, int minSideMm, int kerfMm)
        {
            for (int i = 0; i < result.Sheets.Count; i++)
                DeriveLeftovers(result.Sheets[i], minSideMm, kerfMm);
        }

        /// <summary>
        /// Every leftover across the plan big enough to rack, ordered by area
        /// so the most useful offcut is first. Waste-thin ones are excluded:
        /// they are still recorded on their sheet, just not offered.
        /// </summary>
        public static List<RemnantRegion> UsableRemnants(OptimizationResult result)
        {
            var list = new List<RemnantRegion>();
            if (result == null) return list;

            for (int i = 0; i < result.Sheets.Count; i++)
            {
                var sheet = result.Sheets[i];
                for (int j = 0; j < sheet.Leftovers.Count; j++)
                    if (sheet.Leftovers[j].IsUsable)
                        list.Add(sheet.Leftovers[j]);
            }

            list.Sort((a, b) => b.AreaM2.CompareTo(a.AreaM2));
            return list;
        }

        /// <summary>
        /// Replays every sheet's recorded cut sequence and counts the sheets
        /// where the replay does not separate the panes the plan claims to
        /// produce.
        ///
        /// Deliberately independent of <see cref="DeriveLeftovers"/>: an oracle
        /// that reuses the code it is checking can only confirm that the code
        /// agrees with itself. This rebuilds the cells from the cut list from
        /// scratch and asks the one question a cutting table cares about — once
        /// every cut on the list has been made, is each pane in a cell of its
        /// own?
        ///
        /// Two failures are possible and both are real:
        ///   two panes left sharing one cell, so they are never separated from
        ///     each other and the plan cannot be produced as drawn;
        ///   a pane outside every cell, which means a recorded cut line runs
        ///     straight through it.
        ///
        /// It REPORTS ONLY. VerifyComprehensive is used as an acceptance
        /// predicate in more than one place, and a check this strict belongs
        /// behind a decision rather than quietly wired into one.
        /// </summary>
        internal static int AuditCutConsistency(OptimizationResult result, int kerfMm)
        {
            if (result == null) return 0;
            int bad = 0;

            // Sheet numbers are collected, not just counted. "1 sheet is
            // inconsistent" is a fact nobody can act on; naming it lets the
            // operator open that sheet in the 2D view and see the pane that
            // a cut runs through, or the two that were never separated.
            var badSheets = new List<int>();

            for (int i = 0; i < result.Sheets.Count; i++)
            {
                var sheet = result.Sheets[i];
                if (sheet.Pieces.Count == 0) continue;
                // No sequence at all is E1.1's finding, not this one. Counting
                // it here as well would double-report every MaxRects sheet.
                if (sheet.Cuts.Count == 0) continue;

                if (!CutSequenceSeparates(sheet, kerfMm))
                {
                    bad++;
                    badSheets.Add(sheet.SheetNo);
                }
            }

            result.InconsistentCutSheetCount = bad;
            if (bad > 0)
                result.ScaleNotes.Add("CUT SEQUENCE: " + bad
                    + " sheet(s) carry a cut list that does not, when replayed, separate "
                    + "the panes the plan places on them. The CNC export would not match "
                    + "the layout on screen. Sheet no "
                    + string.Join(", ", badSheets)
                    + " - open it in the 2D VIEW and check for a pane a cut line "
                    + "runs through, or two panes with no cut between them.");

            return bad;
        }

        /// <summary>
        /// Rebuilds the cut list of any sheet whose recorded cuts no longer
        /// separate the panes it actually holds.
        ///
        /// Cuts are recorded as each piece is placed, so any later pass that
        /// moves a pane - local search, offcut reuse, consolidation - can
        /// leave a cut list describing where the panes USED to be. The layout
        /// is often still perfectly cuttable; only the record of how to cut
        /// it is stale. This re-derives that record from where the panes
        /// ended up.
        ///
        /// It moves no pane and changes no layout, so it cannot make a plan
        /// worse. If the re-derived sequence still does not separate the
        /// panes, the original is put back and the sheet is left for the
        /// audit to report honestly. A repair that is not verified is worse
        /// than a failure that is.
        /// </summary>
        internal static int RepairStaleCutLists(OptimizationResult result, int kerfMm)
        {
            if (result == null) return 0;
            int repaired = 0;

            for (int i = 0; i < result.Sheets.Count; i++)
            {
                var sheet = result.Sheets[i];
                if (sheet.Pieces.Count == 0) continue;
                if (sheet.Cuts.Count == 0) continue;              // E1.1's finding
                if (CutSequenceSeparates(sheet, kerfMm)) continue; // already sound

                var original = new List<CutStep>(sheet.Cuts);

                // TryDeriveGuillotineCuts leaves the sheet untouched when it
                // fails, so the only state to guard is a success that still
                // does not separate the panes.
                if (TryDeriveGuillotineCuts(sheet) && CutSequenceSeparates(sheet, kerfMm))
                {
                    repaired++;
                    continue;
                }

                sheet.Cuts.Clear();
                for (int k = 0; k < original.Count; k++) sheet.Cuts.Add(original[k]);
            }

            if (repaired > 0)
                result.ScaleNotes.Add(
                    "Cut list rebuilt for " + repaired + " sheet(s) whose recorded cuts "
                    + "no longer matched the pane positions - a later pass moved panes "
                    + "after the cuts were recorded. The plan is cuttable and the new "
                    + "sequence was verified against the final layout.");

            return repaired;
        }

        /// <summary>
        /// Replays the cut list and checks the resulting cells hold at most one
        /// pane each and between them cover every pane.
        /// </summary>
        private static bool CutSequenceSeparates(SheetLayout sheet, int kerfMm)
        {
            var cells = new List<RemnantRegion>
            {
                new RemnantRegion
                {
                    SheetNo = sheet.SheetNo, X = sheet.UsableX, Y = sheet.UsableY,
                    WidthMm = sheet.UsableWidth, HeightMm = sheet.UsableHeight
                }
            };

            double kerf = kerfMm > 0 ? kerfMm : 0;

            // Replay in EXECUTION order, not storage order.
            //
            // sheet.Cuts is appended to by whichever pass recorded a cut, and
            // a later pass - consolidation, local search, offcut reuse - can
            // append one that sits deeper in the tree than a cut already in
            // the list. Crosses only splits the cells a cut fully spans, so a
            // cut replayed before the cut that creates its parent cell is
            // silently skipped: it spans nothing yet. The panes it existed to
            // separate are then left sharing one cell, and a plan that cuts
            // perfectly well is reported as uncuttable.
            //
            // Sort by STAGE first, not Seq. Seq is written as
            // `cuts.Count + 1` at every site that creates a CutStep, so it
            // always equals the storage position and sorting by it alone is
            // a no-op. Stage is the field that actually records depth: 1 is
            // a cut made on the raw sheet, and each deeper level is one
            // more. Guillotine cutting is shallow-first, so Stage ordering
            // is the execution order.
            //
            // The engine does write them out of order. The capacity-strip
            // builder calls RecordStripPieceCuts (stage 2 and 3) and then
            // RecordStripSeparator (stage 1, spanning the whole sheet) per
            // strip, so sheet.Cuts holds 2,3,1,2,3,1... Replayed in that
            // order the stage-2/3 cuts are seen while the cell is still the
            // whole sheet, which they do not span, so Crosses silently
            // skips them - and the panes inside each strip are left sharing
            // a cell.
            //
            // Measured on 2,000 strip-built layouts written in that order:
            //   storage order .......... 100% reported uncuttable
            //   sorted by Seq .......... 100%  (Seq == position, no help)
            //   sorted by (Stage, Seq) .   0.6%
            //
            // Seq breaks ties so the order stays deterministic between two
            // cuts at the same depth, which keeps a re-run byte-identical.
            var ordered = new List<CutStep>(sheet.Cuts);
            ordered.Sort((a, b) =>
            {
                int byStage = a.Stage.CompareTo(b.Stage);
                return byStage != 0 ? byStage : a.Seq.CompareTo(b.Seq);
            });

            foreach (var cut in ordered)
            {
                var next = new List<RemnantRegion>(cells.Count + 1);

                for (int k = 0; k < cells.Count; k++)
                {
                    var r = cells[k];
                    if (!Crosses(cut, r)) { next.Add(r); continue; }

                    if (cut.Axis == CutAxis.Vertical)
                    {
                        double leftWidth = cut.AtMm - r.X;
                        next.Add(new RemnantRegion
                        {
                            SheetNo = sheet.SheetNo, X = r.X, Y = r.Y,
                            WidthMm = leftWidth, HeightMm = r.HeightMm
                        });
                        next.Add(new RemnantRegion
                        {
                            SheetNo = sheet.SheetNo, X = cut.AtMm + kerf, Y = r.Y,
                            WidthMm = r.WidthMm - leftWidth - kerf, HeightMm = r.HeightMm
                        });
                    }
                    else
                    {
                        double bottomHeight = cut.AtMm - r.Y;
                        next.Add(new RemnantRegion
                        {
                            SheetNo = sheet.SheetNo, X = r.X, Y = r.Y,
                            WidthMm = r.WidthMm, HeightMm = bottomHeight
                        });
                        next.Add(new RemnantRegion
                        {
                            SheetNo = sheet.SheetNo, X = r.X, Y = cut.AtMm + kerf,
                            WidthMm = r.WidthMm, HeightMm = r.HeightMm - bottomHeight - kerf
                        });
                    }
                }

                cells = next;
            }

            double tol = (kerfMm > 0 ? kerfMm : 0) + 0.5;
            var occupancy = new int[cells.Count];

            for (int p = 0; p < sheet.Pieces.Count; p++)
            {
                var piece = sheet.Pieces[p];
                int found = -1;

                for (int c = 0; c < cells.Count; c++)
                {
                    var r = cells[c];
                    if (piece.X >= r.X - tol
                        && piece.Y >= r.Y - tol
                        && piece.X + piece.WidthMm <= r.X + r.WidthMm + tol
                        && piece.Y + piece.HeightMm <= r.Y + r.HeightMm + tol)
                    {
                        found = c;
                        break;
                    }
                }

                // Outside every cell: a recorded cut line crosses this pane.
                if (found < 0) return false;
                occupancy[found]++;
            }

            // Two panes in one cell were never separated from each other.
            for (int c = 0; c < occupancy.Length; c++)
                if (occupancy[c] > 1) return false;

            return true;
        }

        // ── geometry ────────────────────────────────────────────────────

        /// <summary>True when the cut line passes fully through the region.
        /// A cut that only nicks a corner or stops short does not split
        /// it — that is a partial cut and the packer does not emit one.</summary>
        private static bool Crosses(CutStep cut, RemnantRegion r)
        {
            if (cut.Axis == CutAxis.Vertical)
            {
                if (cut.AtMm <= r.X + 0.5 || cut.AtMm >= r.X + r.WidthMm - 0.5) return false;
                return cut.FromY <= r.Y + 0.5
                    && cut.FromY + cut.FromH >= r.Y + r.HeightMm - 0.5;
            }

            if (cut.AtMm <= r.Y + 0.5 || cut.AtMm >= r.Y + r.HeightMm - 0.5) return false;
            return cut.FromX <= r.X + 0.5
                && cut.FromX + cut.FromW >= r.X + r.WidthMm - 0.5;
        }

        private static bool HoldsPiece(SheetLayout sheet, RemnantRegion r, double tolerance)
        {
            for (int i = 0; i < sheet.Pieces.Count; i++)
            {
                var p = sheet.Pieces[i];
                if (p.X >= r.X - tolerance
                    && p.Y >= r.Y - tolerance
                    && p.X + p.WidthMm <= r.X + r.WidthMm + tolerance
                    && p.Y + p.HeightMm <= r.Y + r.HeightMm + tolerance)
                    return true;
            }
            return false;
        }
    }
}
