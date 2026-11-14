// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/OptimizationModels.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — glass cutting optimization (design 2026-09-12)
//
// Pure domain models for the sheet-optimization engine. No WPF, no EF —
// the engine is fully unit-testable and pinned by oracle tests
// (no-overlap, margins, kerf, rotation). See
// docs/OPTIMIZATION_MODULE_DESIGN_2026-09-12.md.
// ═══════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ProGlassAutomation.Services.Optimization
{
    /// <summary>
    /// How pieces may be turned on the sheet. Free90 is ADAPTIVE: the
    /// piece keeps its original orientation whenever that fits — rotation
    /// is used only when the piece needs it (the floor's preferred 0/90
    /// mix). This is the default for every run.
    /// </summary>
    public enum RotationPolicy
    {
        /// <summary>Pieces stay exactly as entered (W×H).</summary>
        Locked,
        /// <summary>Original orientation first; rotate 90° only if it doesn't fit.</summary>
        Free90
    }

    /// <summary>Guillotine split direction after a placement.</summary>
    public enum SplitMode
    {
        /// <summary>Bottom remainder keeps the full width (calibrated default).</summary>
        HorizontalFirst,
        /// <summary>Right remainder keeps the full height.</summary>
        VerticalFirst,
        /// <summary>Split along the side with the longer leftover.</summary>
        LongerLeftover
    }

    /// <summary>
    /// Which plate the operator wants a stage to prefer. Yield is the
    /// historical winner and the default every existing caller keeps.
    /// FillFirstPlate is applied after that winner and can replace it only
    /// when the replacement is legal and does not add sheets or unplaced pieces.
    /// </summary>
    public enum PlatePattern
    {
        /// <summary>Fewest unplaced, then fewest sheets, then utilization. An equal sheet count keeps the incumbent.</summary>
        Yield = 0,
        /// <summary>Same sheet and unplaced limits, then the fuller leading sheet.</summary>
        FillFirstPlate = 1,
        /// <summary>Same sheet and unplaced limits, then the plan with fewer
        /// guillotine cuts. Decides between plans that already use the same
        /// number of sheets — it never buys a cut by adding a sheet.</summary>
        FewestCuts = 2,
        /// <summary>Same sheet and unplaced limits, then fewer distinct stock
        /// sizes and fewer switches between them. Every switch is a rack move
        /// and a setup on the floor.</summary>
        MinimiseStockChanges = 3,
        /// <summary>Same sheet and unplaced limits, then the plan that pushes
        /// its leftover onto ONE sheet, so the waste is big enough to come off
        /// as a usable remnant instead of several slivers. This is the pattern
        /// WITHOUT the remnant inventory: it chooses plans that leave reusable
        /// leftovers, it does not yet reuse them.</summary>
        RemnantFirst = 4,
        /// <summary>Same sheet and unplaced limits, then the plan whose sheets
        /// are most evenly loaded — three even plates beat two full ones and a
        /// nearly empty one.</summary>
        BalancedSpread = 5
    }

    /// <summary>Direction a guillotine cut line runs.</summary>
    public enum CutAxis
    {
        /// <summary>Cut line runs horizontally (constant Y) — splits a rectangle
        /// into a top and a bottom part.</summary>
        Horizontal = 0,
        /// <summary>Cut line runs vertically (constant X) — splits a rectangle
        /// into a left and a right part.</summary>
        Vertical = 1
    }

    /// <summary>
    /// One guillotine cut, in the order the floor executes it.
    ///
    /// PHASE 0 (2026-09-26). Before this existed a sheet knew WHERE its pieces
    /// sat but not HOW the table had to cut them out, so there was nothing to
    /// hand a CNC file, nothing to count Z/W trims from, and nothing to prove
    /// a staged pattern was honoured. Cuts are recorded by SplitRemainder —
    /// the same code that decides the free rectangles — so the cut list and
    /// the free-rect ledger can never disagree.
    /// </summary>
    public sealed class CutStep
    {
        /// <summary>1-based execution order on the table.</summary>
        public int Seq { get; init; }

        /// <summary>Direction of the cut line.</summary>
        public CutAxis Axis { get; init; }

        /// <summary>Position of the cut line in mm from the sheet origin: the Y
        /// of a horizontal cut, the X of a vertical cut. The score sits on the
        /// piece's trailing edge; kerf is consumed from the remainder side.</summary>
        public int AtMm { get; init; }

        /// <summary>Rectangle the cut passes through, in sheet coordinates.</summary>
        public int FromX { get; init; }
        public int FromY { get; init; }
        public int FromW { get; init; }
        public int FromH { get; init; }

        /// <summary>1-based cut stage: 1 is the first cut made on the raw sheet.</summary>
        public int Stage { get; init; }

        /// <summary>True when the far side of this cut yields no usable stock,
        /// i.e. the cut exists only to separate the piece from material that
        /// becomes waste. Phase 2 will add remnant-releasing trims, which are
        /// NOT marked this way because they do produce usable glass.</summary>
        public bool IsTrim { get; init; }
    }

    /// <summary>
    /// How complex a cut sequence the floor is willing to run — the
    /// industry's cutting-complexity levels (CutGLib ships 2 XY / 3 two-stage
    /// XY / 4 XYZ / 5 XYZW / 6 Standard; GlasSave offers X, Y, Z and W cuts
    /// with trim cuts on the sheet sides).
    ///
    /// These are PRODUCTION constraints, not yield features. Staged levels
    /// trade material utilization for fewer, simpler cuts: the literature is
    /// explicit that two-stage patterns have the lowest utilization and the
    /// simplest cutting, three-stage the opposite.
    /// </summary>
    public enum CutComplexity
    {
        /// <summary>Unlimited guillotine depth, no trim cuts. Today's behaviour
        /// and the default — every existing run is bit-identical under it.</summary>
        FreeGuillotine = 0,
        /// <summary>Two stages, first cut horizontal (along the sheet width),
        /// producing stacked strips.</summary>
        SimpleFirstX = 1,
        /// <summary>Two stages, first cut vertical (along the sheet height),
        /// producing side-by-side columns.</summary>
        SimpleFirstY = 2,
        /// <summary>Two stages, first direction chosen per sheet (2SGP).</summary>
        Simple = 3,
        /// <summary>Three stages (3SGP) — the fullest of the staged family.</summary>
        ThreeStage = 4,
        /// <summary>Unlimited depth PLUS permission to cut trims (Z/W cuts) that
        /// release reusable remnants.</summary>
        ComplexTrims = 5
    }

    /// <summary>
    /// Immutable, resolved form of a <see cref="CutComplexity"/> — the numbers
    /// the packer actually reads.
    ///
    /// Deliberately NOT part of <see cref="MarginProfile"/>: that type is
    /// per-thickness and is serialized into saved sessions, so extending it
    /// would force a session migration. This travels beside it and defaults to
    /// <see cref="Free"/>, which reproduces today's engine exactly.
    /// </summary>
    public sealed class CutPolicy
    {
        /// <summary>Unlimited depth, no trims. The default, and the only policy
        /// under which existing calibration figures are guaranteed exact.</summary>
        public static readonly CutPolicy Free =
            new CutPolicy(CutComplexity.FreeGuillotine, int.MaxValue, false, false, false, 0);

        private CutPolicy(CutComplexity complexity, int maxStages, bool forceFirstHorizontal,
            bool forceFirstVertical, bool allowTrimCuts, int minTrimWidthMm)
        {
            Complexity = complexity;
            MaxStages = maxStages;
            ForceFirstCutHorizontal = forceFirstHorizontal;
            ForceFirstCutVertical = forceFirstVertical;
            AllowTrimCuts = allowTrimCuts;
            MinTrimWidthMm = minTrimWidthMm;
        }

        public CutComplexity Complexity { get; }
        public int MaxStages { get; }
        public bool ForceFirstCutHorizontal { get; }
        public bool ForceFirstCutVertical { get; }
        public bool AllowTrimCuts { get; }
        public int MinTrimWidthMm { get; }

        /// <summary>True when the pattern limits how deep the guillotine tree may
        /// grow. Only these levels are subject to staged enforcement; the free
        /// levels take no constraint path at all.</summary>
        public bool IsDepthConstrained => MaxStages < int.MaxValue;

        /// <summary>Resolves a complexity level into the numbers the packer reads.
        /// <paramref name="minTrimWidthMm"/> only applies to
        /// <see cref="CutComplexity.ComplexTrims"/> — the narrowest strip worth
        /// cutting to release a remnant.</summary>
        public static CutPolicy For(CutComplexity complexity, int minTrimWidthMm = 0)
        {
            int width = minTrimWidthMm > 0 ? minTrimWidthMm : 0;
            switch (complexity)
            {
                case CutComplexity.SimpleFirstX:
                    return new CutPolicy(complexity, 2, true, false, false, 0);
                case CutComplexity.SimpleFirstY:
                    return new CutPolicy(complexity, 2, false, true, false, 0);
                case CutComplexity.Simple:
                    return new CutPolicy(complexity, 2, false, false, false, 0);
                case CutComplexity.ThreeStage:
                    return new CutPolicy(complexity, 3, false, false, false, 0);
                case CutComplexity.ComplexTrims:
                    return new CutPolicy(complexity, int.MaxValue, false, false, true, width);
                default:
                    return Free;
            }
        }

        /// <summary>
        /// Resolves the policy for one cutting group.
        ///
        /// LAMINATE RULE: Z/W trims are never cut into laminated stock. The
        /// plies are not hand-broken at the table, so a trim cut would leave an
        /// unseparated edge — which is why commercial optimisers (GlasSave
        /// among them) let the operator switch W and Z cuts off for laminate.
        /// Refusing is the safe direction: a refused trim costs a little yield,
        /// a trim cut into laminate costs the sheet.
        /// </summary>
        public static CutPolicy ResolveFor(CutComplexity complexity, bool isLaminatedGroup,
            int minTrimWidthMm = 0) =>
            isLaminatedGroup && complexity == CutComplexity.ComplexTrims
                ? Free
                : For(complexity, minTrimWidthMm);

        /// <summary>Short human label for report stamping and export headers.</summary>
        public string Describe() => Complexity switch
        {
            CutComplexity.SimpleFirstX => "Simple — first X cut (2-stage)",
            CutComplexity.SimpleFirstY => "Simple — first Y cut (2-stage)",
            CutComplexity.Simple => "Simple (2-stage, direction auto)",
            CutComplexity.ThreeStage => "3-stage",
            CutComplexity.ComplexTrims => "Complex — trims allowed (Z/W)",
            _ => "Free guillotine"
        };
    }

    /// <summary>
    /// Which house-margin profile a glass thickness falls into.
    ///
    /// PHASE 1 (2026-09-20, owner-specified): the old table interpolated
    /// 15 / 20 / 25mm by thickness with a breakout fixed at 15mm for every
    /// glass. The floor runs three real profiles, and the third is
    /// deliberately ASYMMETRIC — per the 2026-09-21 correction, thick glass
    /// takes no trim on the top edge, with 50mm on the other three edges.
    /// </summary>
    public enum ThicknessBucket
    {
        /// <summary>Up to 6mm — 15mm on all four edges, breakout 15mm, kerf 3mm.</summary>
        Thin,
        /// <summary>7–8mm — 30mm on all four edges, breakout 30mm, kerf 3mm.</summary>
        Medium,
        /// <summary>9mm and above — LM/RM/BM 50mm, TM 0mm, breakout 50mm (switchable to 40mm), kerf 4mm.</summary>
        Thick
    }

    /// <summary>
    /// Cutting allowances for one glass thickness. Margins are the edge
    /// trims cut off the raw stock before piece placement; kerf is the
    /// blade gap between cuts; MinBreakout is the narrowest offcut worth
    /// keeping (thinner strips are reported as trim waste).
    /// </summary>
    public sealed class MarginProfile
    {
        public int ThicknessMm { get; init; }
        public int LeftMm { get; init; }
        public int RightMm { get; init; }
        public int TopMm { get; init; }
        public int BottomMm { get; init; }
        public int KerfMm { get; init; }
        public int MinBreakoutMm { get; init; }

        /// <summary>
        /// True when these values came from the operator rather than from the
        /// house table for this thickness.
        ///
        /// PHASE 1 / finding 3: <c>ResolveGroupMargins</c> used to INFER
        /// "the operator customized this" from <c>LeftMm != 0 || TopMm != 0</c>
        /// — two of the four margin fields. So an operator who deliberately
        /// set Left = 0 and Top = 0 (a completely normal trim pattern, and the
        /// house pattern for the thick bucket's bottom edge) was read as
        /// NOT having customized anything, and house defaults for the parsed
        /// thickness were substituted with an empty note: wrong usable area,
        /// wrong sheet count, nothing on screen to explain it. Setting
        /// Right = 15 / Bottom = 15 with Left = Top = 0 was invisible too.
        ///
        /// Customization is now an explicit fact about the profile, decided
        /// where the operator's input is read (TryBuildInputs), not guessed
        /// from which numbers happen to be zero.
        /// </summary>
        public bool IsCustomized { get; init; }

        /// <summary>The breakout the floor prefers on thick glass.</summary>
        public const int BreakoutPrimaryMm = 50;

        /// <summary>
        /// The breakout the floor accepts instead on thick glass, when the
        /// offcut is worth keeping. Owner-specified as switchable; it is the
        /// only margin value with a sanctioned alternative.
        /// </summary>
        public const int BreakoutAlternateMm = 40;

        /// <summary>
        /// Maps a thickness onto its house bucket. Unlisted thicknesses ROUND
        /// UP to the next known bucket — thicker glass needs more trim, so
        /// rounding up is the conservative direction: 7mm takes the 8mm
        /// profile, 9mm and 11mm take the thick profile, and anything above
        /// 12mm stays thick. An unknown or non-positive thickness (a label the
        /// parser could not read) takes the thin profile, matching the
        /// long-standing 6mm fallback.
        /// </summary>
        public static ThicknessBucket BucketFor(int thicknessMm) =>
            thicknessMm <= 6 ? ThicknessBucket.Thin
            : thicknessMm <= 8 ? ThicknessBucket.Medium
            : ThicknessBucket.Thick;

        /// <summary>
        /// True when the thickness offers the 50 / 40 breakout choice. Only the
        /// thick bucket does — on thin and medium glass the breakout equals the
        /// margin, and there is nothing to choose between.
        /// </summary>
        public static bool HasBreakoutChoice(int thicknessMm) =>
            BucketFor(thicknessMm) == ThicknessBucket.Thick;

        /// <summary>
        /// House defaults for a thickness — editable in the SETTINGS tab.
        /// </summary>
        /// <param name="thicknessMm">Glass thickness in mm.</param>
        /// <param name="minBreakoutMm">
        /// Optional breakout override. Honoured ONLY where the floor has a
        /// choice (the thick bucket) and clamped to the two sanctioned values,
        /// so a stray 0 can never disable breakout protection on thin glass.
        /// </param>
        public static MarginProfile DefaultFor(int thicknessMm, int? minBreakoutMm = null)
        {
            ThicknessBucket bucket = BucketFor(thicknessMm);

            int left, right, top, bottom, breakout;
            switch (bucket)
            {
                case ThicknessBucket.Thick:
                    // Owner-confirmed 2026-09-21: LM 50 · RM 50 · TM 0 · BM 50.
                    left = right = bottom = 50;
                    top = 0;
                    breakout = minBreakoutMm == BreakoutAlternateMm
                        ? BreakoutAlternateMm
                        : BreakoutPrimaryMm;
                    break;

                case ThicknessBucket.Medium:
                    left = right = top = bottom = 30;
                    breakout = 30;
                    break;

                default:
                    left = right = top = bottom = 15;
                    breakout = 15;
                    break;
            }

            return new MarginProfile
            {
                ThicknessMm = thicknessMm,
                LeftMm = left,
                RightMm = right,
                TopMm = top,
                BottomMm = bottom,
                KerfMm = thicknessMm >= 10 ? 4 : 3,
                MinBreakoutMm = breakout
            };
        }

        /// <summary>
        /// True when every cutting-relevant value matches. Used to decide
        /// whether the operator has touched the house defaults, so an
        /// untouched SETTINGS tab can follow a thickness change automatically
        /// and a customized one is never overwritten underneath them.
        /// </summary>
        public bool SameGeometryAs(MarginProfile? other) =>
            other != null
            && LeftMm == other.LeftMm
            && RightMm == other.RightMm
            && TopMm == other.TopMm
            && BottomMm == other.BottomMm
            && KerfMm == other.KerfMm
            && MinBreakoutMm == other.MinBreakoutMm;
    }

    /// <summary>One stock sheet size in the run's pool.</summary>
    public sealed class StockSize
    {
        public int WidthMm { get; init; }
        public int HeightMm { get; init; }
        public int ThicknessMm { get; init; }
        public string Color { get; init; } = "";
        public string Label { get; init; } = "";
        public string MaterialKey { get; init; } = "";

        /// <summary>
        /// Optional cap (Sheet Store balance). <see cref="UnlimitedSheets"/> = no limit.
        /// </summary>
        public int MaxSheets { get; init; }

        /// <summary>
        /// The value <see cref="MaxSheets"/> takes when a rack has no balance
        /// limit. Named so no call site has to read a bare 0 as "infinite
        /// glass", and so the three packer strategies that special-case it
        /// (GuillotinePacker.OpenNewSheet, Advanced, Smart) can be traced from
        /// here.
        ///
        /// PHASE 2 / finding 1: this value must only ever be reached from an
        /// EXPLICITLY blank or "∞" availability field. An unreadable balance
        /// used to fall through to it, which handed the engine unlimited stock
        /// for a rack the operator had typed something into.
        /// </summary>
        public const int UnlimitedSheets = 0;

        /// <summary>
        /// The value the STOCKS <em>Available</em> field defaults to — a very large
        /// finite number meaning "no cap; usage is tracked, not limited".
        ///
        /// PHASE 9 (safe): this is NOT the same thing as
        /// <see cref="UnlimitedSheets"/>. 0 is the sentinel the three packer
        /// strategies special-case; 99999999 is an ordinary int they honour as a
        /// genuinely enormous cap. Any code asking "is this rack limited?" has to
        /// test BOTH, or a rack nobody set a balance on gets counted, deducted and
        /// reported as though an operator had typed a number into it. Named here so
        /// the places that used to hard-code the literal — StockEntryVm's default,
        /// the session DTO and its restore — share one definition.
        /// </summary>
        public const int EffectivelyUnlimitedSheets = 99999999;

        /// <summary>
        /// Returns a copy of this rack carrying a different Sheet Store balance.
        /// Every property here is <c>init</c>, so re-pricing a rack part-way
        /// through a run means making a new one rather than mutating the instance
        /// the packer is still holding.
        ///
        /// PHASE 9 / finding B2. Callers must never pass
        /// <see cref="UnlimitedSheets"/> to mean "exhausted" — 0 reads as
        /// INFINITE GLASS in all three packer strategies. A rack with nothing
        /// left has to be dropped from the pool, not zeroed.
        /// </summary>
        public StockSize WithMaxSheets(int maxSheets) => new StockSize
        {
            WidthMm = WidthMm,
            HeightMm = HeightMm,
            ThicknessMm = ThicknessMm,
            Color = Color,
            Label = Label,
            MaterialKey = MaterialKey,
            MaxSheets = maxSheets
        };

        public string SizeLabel => string.IsNullOrWhiteSpace(Label)
            ? $"{WidthMm.ToString(CultureInfo.InvariantCulture)} × {HeightMm.ToString(CultureInfo.InvariantCulture)}"
            : Label;
    }

    /// <summary>One piece to cut (already expanded from row quantities).</summary>
    public sealed class CutPiece
    {
        public string JobRef { get; init; } = "";
        public string GlassRef { get; init; } = "";
        public string SpecLabel { get; init; } = "";
        public string Position { get; init; } = "";
        public string Color { get; init; } = "";
        public int ThicknessMm { get; init; }
        public int WidthMm { get; init; }
        public int HeightMm { get; init; }
        public bool AllowRotate { get; init; } = true;

        /// <summary>Identity for the 2D view, e.g. "G-101 · 3/24".</summary>
        public string PieceLabel { get; init; } = "";

        // Canonical provenance — copied from the source row, never invented.
        public string ProformaId { get; init; } = "";
        public string JobOrderId { get; init; } = "";
        public int SpecGroupNumber { get; init; }
        public int SourceRowSerial { get; init; }
        public string GlassType { get; init; } = "";
        public string Processing { get; init; } = "";
        public string Coating { get; init; } = "";
        public int W1 { get; init; }
        public int H1 { get; init; }
        public int W2 { get; init; }
        public int H2 { get; init; }
        public string SheetStoreSku { get; init; } = "";
        public int SheetWidthMm { get; init; }
        public int SheetHeightMm { get; init; }
        public decimal SheetPricePerSqm { get; init; }
        public string MaterialKey { get; init; } = "";
        public int Qty { get; init; } = 1;
    }

    /// <summary>One placed piece on one sheet (integer mm coordinates).</summary>
    public sealed class PlacedPiece
    {
        public CutPiece Piece { get; init; } = null!;
        public int X { get; init; }
        public int Y { get; init; }
        public int WidthMm { get; init; }
        public int HeightMm { get; init; }
        public bool Rotated { get; init; }
    }

    /// <summary>One optimized sheet with its placements and metrics.</summary>
    public sealed class SheetLayout
    {
        public int SheetNo { get; set; }
        public StockSize Stock { get; init; } = null!;

        /// <summary>Usable rectangle inside the edge trims.</summary>
        public int UsableX { get; init; }
        public int UsableY { get; init; }
        public int UsableWidth { get; init; }
        public int UsableHeight { get; init; }

        public List<PlacedPiece> Pieces { get; } = new();

        /// <summary>
        /// The guillotine cut sequence that produces <see cref="Pieces"/>, in
        /// execution order. Empty for plans produced before cut capture existed
        /// and for paths that rebuild a sheet, so consumers must treat an empty
        /// list as "not recorded" rather than "no cuts needed".
        /// </summary>
        public List<CutStep> Cuts { get; } = new();

        /// <summary>Rectangles the sheet is left holding once every cut is
        /// made. Filled in by <see cref="GuillotinePacker.DeriveLeftovers(SheetLayout, int, int)"/>
        /// at the end of the run; empty means the plan predates it, not that
        /// there is no waste.</summary>
        public List<RemnantRegion> Leftovers { get; } = new();

        /// <summary>How many of this sheet's cuts release no usable stock.</summary>
        public int TrimCutCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Cuts.Count; i++)
                    if (Cuts[i].IsTrim) n++;
                return n;
            }
        }

        public decimal UsedSqm =>
            System.Math.Round(Pieces.Sum(p => (decimal)p.WidthMm * p.HeightMm) / 1_000_000m,
                4, System.MidpointRounding.AwayFromZero);

        public decimal StockSqm =>
            System.Math.Round((decimal)Stock.WidthMm * Stock.HeightMm / 1_000_000m,
                4, System.MidpointRounding.AwayFromZero);

        /// <summary>Utilization % = placed glass area / full sheet area.</summary>
        public decimal UtilizationPct => StockSqm == 0 ? 0m :
            System.Math.Round(UsedSqm / StockSqm * 100m, 2, System.MidpointRounding.AwayFromZero);

        public string UtilizationLabel =>
            UtilizationPct.ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }

    /// <summary>Whole-run result.</summary>
    /// <summary>Which scale band an input falls into. Decided purely by part
    /// count so that a given job always gets the same search budget and the
    /// engine stays deterministic.</summary>
    public enum ScaleTier
    {
        /// <summary>Full search. Everything the calibration benchmark covers
        /// lands here, so results stay bit-identical to earlier releases.</summary>
        Standard = 0,
        /// <summary>Search breadth trimmed; ordering and split diversity kept.</summary>
        Large = 1,
        /// <summary>Minimum viable search, chosen so the run terminates.</summary>
        Huge = 2
    }

    /// <summary>
    /// How much search the optimizer is allowed to perform for a given input
    /// size. The engine's cost grows roughly with parts x sheets, so a group of
    /// 100,000 parts is not 1,000 times a group of 100 — it is about a million
    /// times. Rather than run for hours or exhaust memory, large inputs trade
    /// search BREADTH (deterministic shuffles, beam widening) for termination,
    /// while keeping the ordering and split-rule diversity that produces most
    /// of the quality. Every reduction is recorded on the result so it is
    /// visible to the operator instead of silently degrading the plan.
    /// </summary>
    public sealed class ScaleProfile
    {
        public ScaleProfile(int pieceCount, int requestedEffort, ScaleTier tier,
            int shuffleCap, bool allowBeam, int maxRectsOrderings, int consolidationSweeps)
        {
            PieceCount = pieceCount;
            RequestedEffort = requestedEffort;
            Tier = tier;
            ShuffleCap = shuffleCap;
            AllowBeam = allowBeam;
            MaxRectsOrderings = maxRectsOrderings;
            ConsolidationSweeps = consolidationSweeps;
        }

        public int PieceCount { get; }
        public int RequestedEffort { get; }
        public ScaleTier Tier { get; }
        /// <summary>Maximum number of deterministic shuffles per ordering/split pair.</summary>
        public int ShuffleCap { get; }
        /// <summary>Beam search copies the whole open-sheet list per candidate.</summary>
        public bool AllowBeam { get; }
        /// <summary>How many orderings the MaxRects candidate may run.</summary>
        public int MaxRectsOrderings { get; }
        /// <summary>Consolidation sweeps cap.</summary>
        public int ConsolidationSweeps { get; }

        /// <summary>True when any search breadth was reduced because of size.</summary>
        public bool IsReduced => Tier != ScaleTier.Standard;

        /// <summary>Whether sheet-at-a-time quadratic strategies are allowed.</summary>
        public bool AllowQuadraticStrategies => Tier != ScaleTier.Huge;

        public static ScaleProfile For(int pieceCount, int requestedEffort)
        {
            var plan = OptimizationSearchGovernor.PlanForOptimize(pieceCount, requestedEffort);
            var tier = plan.Tier switch
            {
                SearchTier.Full => ScaleTier.Standard,
                SearchTier.Narrowed => ScaleTier.Large,
                _ => ScaleTier.Huge
            };

            return new ScaleProfile(
                pieceCount,
                requestedEffort,
                tier,
                plan.ShuffleCap,
                plan.Tier == SearchTier.Full,
                plan.MaxRectOrders,
                plan.MaxConsolidationSweeps);
        }

        /// <summary>Plain-language description of what was reduced and why.</summary>
        public string Summary
        {
            get
            {
                var plan = OptimizationSearchGovernor.PlanForOptimize(PieceCount, RequestedEffort);
                return !string.IsNullOrWhiteSpace(plan.Note) ? plan.Note : $"{PieceCount} parts: adaptive search at effort {RequestedEffort}.";
            }
        }
    }

    /// <summary>Progress checkpoint reported by the optimizer. Emitted between
    /// candidate passes, which is the natural granularity: each pass is one
    /// complete packing of the group.</summary>
    public sealed class OptimizeProgress
    {
        public string Stage { get; init; } = "";
        public int Completed { get; init; }
        public int Total { get; init; }
        public string Message { get; init; } = "";

        public double Fraction => Total <= 0
            ? 0d
            : System.Math.Min(1d, (double)Completed / Total);

        public static OptimizeProgress Of(string stage, int completed, int total, string message = "") =>
            new OptimizeProgress { Stage = stage, Completed = completed, Total = total, Message = message };
    }

    public sealed class OptimizationResult
    {
        public List<SheetLayout> Sheets { get; } = new();
        public List<CutPiece> Unplaced { get; } = new();
        public RotationPolicy Rotation { get; init; }

        /// <summary>Search reductions applied because of input size, in plain
        /// language. Always empty for standard-size groups, so existing reports
        /// and tests are unaffected.</summary>
        public List<string> ScaleNotes { get; } = new();
        /// <summary>Which strategy produced this plan. Empty for every legacy
        /// candidate, so nothing that reads a result today changes. It exists
        /// because a multi-start run evaluates up to 147 complete plans and
        /// keeps the best — without this the operator cannot tell whether a
        /// better number came from the placement policy or from luck.</summary>
        public string Strategy { get; set; } = "";

        // ── PHASE 4 / findings 7, 8, 9 (2026-09-20): the oracle verdict ──
        //
        // Two independent oracles exist in GuillotinePacker and they check
        // DIFFERENT things:
        //   Verify             — geometry: margins, rotation policy, dimension
        //                        integrity, overlap, kerf.
        //   ValidateResult     — CONSERVATION: every input piece placed exactly
        //                        once, nothing missing, nothing duplicated,
        //                        input count == placed + unplaced.
        // The run header used to label a group "Verified" from Verify alone, and
        // PackQuick never called ValidateResult at all — so a QUICK plan that
        // had LOST pieces displayed a green "Verified". The verdict now travels
        // with the result so every consumer (header, export, Step 6 feed) can
        // tell what was actually checked.

        /// <summary>Every problem any oracle reported against this plan.</summary>
        public List<string> ValidationProblems { get; } = new();

        /// <summary>
        /// True when conservation was CHECKED and passed. Distinct from
        /// <see cref="HasValidationFailure"/> so a caller can tell "verified"
        /// from "not checked".
        /// </summary>
        public bool ConservationVerified { get; set; }

        /// <summary>True when geometry was CHECKED and passed.</summary>
        public bool GeometryVerified { get; set; }

        /// <summary>True when the conservation oracle ran against this plan at
        /// all, regardless of outcome. Lets the run header distinguish
        /// "verified" from "failed" from "never checked" — the QUICK path used
        /// to be in the third state while displaying the first.</summary>
        public bool ConservationChecked { get; set; }

        /// <summary>True when the geometry oracle ran against this plan at all,
        /// regardless of outcome.</summary>
        public bool GeometryChecked { get; set; }

        /// <summary>
        /// True when any oracle reported a problem. Such a plan is still shown
        /// on screen so the operator can inspect what went wrong, but it is
        /// never exported to a cut file and never feeds Step 6 — a plan that
        /// fails geometry or conservation must not become production work.
        /// </summary>
        public bool HasValidationFailure => ValidationProblems.Count > 0;

        /// <summary>
        /// Records oracle output. Kept as one method so no call site can set the
        /// flag without also recording WHY.
        /// </summary>
        public void RecordValidation(IEnumerable<string> problems, bool conservationChecked, bool geometryChecked)
        {
            if (problems != null)
                foreach (var p in problems)
                    if (!string.IsNullOrWhiteSpace(p) && !ValidationProblems.Contains(p))
                        ValidationProblems.Add(p);

            if (conservationChecked) ConservationChecked = true;
            if (geometryChecked) GeometryChecked = true;

            ConservationVerified = ConservationChecked && !HasValidationFailure;
            GeometryVerified = GeometryChecked && !HasValidationFailure;
        }

        // ── PHASE E1: instrumentation ─────────────────────────────────
        // Written once at the end of a finished plan by
        // OptimizationInstrumentation. Pure measurement: nothing in the engine
        // reads these to make a decision, so filling them in cannot move a
        // benchmark result. They exist so the two questions the engine could
        // never answer about its own output have answers.

        /// <summary>Sheets that hold panes but carry no cut sequence —
        /// layouts no guillotine table can produce. Zero is the only good
        /// value. See finding F1.</summary>
        public int UncuttableSheetCount { get; set; }

        /// <summary>Sheets that DO carry a cut sequence, but whose sequence,
        /// replayed, does not separate the panes the plan places on them. Zero
        /// is the only good value. Distinct from
        /// <see cref="UncuttableSheetCount"/>: that one is "no sequence could
        /// be found", this one is "the recorded sequence is wrong".</summary>
        public int InconsistentCutSheetCount { get; set; }

        /// <summary>Provable lower bound on the sheet count for this job.
        /// Deliberately weak (area only): the true minimum is at least this.
        /// See finding F2.</summary>
        public int SheetsLowerBound { get; set; }

        /// <summary>How far the plan is from that bound, in sheets. Zero means
        /// no plan can use fewer plates. Never trusted to be negative — a
        /// negative gap would mean the bound is wrong, not that the plan is
        /// better than possible.</summary>
        public int OptimalityGapSheets => SheetsUsed - SheetsLowerBound;

        /// <summary>The same gap as a percentage of the bound.</summary>
        public double OptimalityGapPercent => SheetsLowerBound <= 0
            ? 0.0
            : (double)OptimalityGapSheets / SheetsLowerBound * 100.0;

        public int SheetsUsed => Sheets.Count;

        public decimal TotalUsedSqm =>
            System.Math.Round(Sheets.Sum(s => s.UsedSqm), 4, System.MidpointRounding.AwayFromZero);

        public decimal TotalStockSqm =>
            System.Math.Round(Sheets.Sum(s => s.StockSqm), 4, System.MidpointRounding.AwayFromZero);

        /// <summary>Weighted utilization across all sheets.</summary>
        public decimal UtilizationPct => TotalStockSqm == 0 ? 0m :
            System.Math.Round(TotalUsedSqm / TotalStockSqm * 100m, 2, System.MidpointRounding.AwayFromZero);

        public bool Success => Unplaced.Count == 0 && Sheets.Count > 0;
    }
}
