// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.5.PricingModels.cs
// MODULE: step6 — stage 2 (ADV step 4): what a glass build is priced from.
//
// The reference wizard's four build types are kept because they are the
// trade's own vocabulary: SGU (single glazed), DGU (double glazed with air
// spacer), LAM (laminated pair with PVB) and DGU+LAM (three panes, one air
// spacer, one interlayer).
//
// ONE DECISION IS DELIBERATELY DIFFERENT FROM THE REFERENCE
//   ADV resolved pane prices from a Sheet Store catalogue. This application
//   has no Sheet Store module yet, so a price is TYPED on the invoice (or
//   arrives on an imported inquiry rate) and the cost card records it. The
//   model therefore separates the quoted rate from an OPTIONAL purchase rate:
//   when a purchase rate is supplied, Step 9 costs with it and says so; when
//   it is not, Step 9 uses the quoted pane rate and records that basis.
//   Nothing invents a catalogue price.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Globalization;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>The four glass builds the estimator understands.</summary>
    public enum GlassBuildType
    {
        Unknown = 0,
        Sgu = 1,
        Dgu = 2,
        Lam = 3,
        DguLam = 4
    }

    /// <summary>Build codes as they are stored, imported and printed.</summary>
    public static class GlassBuilds
    {
        public const string Sgu = "SGU";
        public const string Dgu = "DGU";
        public const string Lam = "LAM";
        public const string DguLam = "DGU+LAM";

        public static readonly IReadOnlyList<string> All = new[] { Sgu, Dgu, Lam, DguLam };

        /// <summary>
        /// Normalizes a label onto the vocabulary. DGU+LAM is matched FIRST and
        /// exactly, so a compound build is never swallowed by a "DGU" prefix —
        /// the most expensive build must not be silently downgraded.
        /// </summary>
        public static string Normalize(string? value)
        {
            string text = (value ?? "").Trim();
            if (text.Equals(DguLam, StringComparison.OrdinalIgnoreCase)) return DguLam;
            if (text.Equals(Sgu, StringComparison.OrdinalIgnoreCase)) return Sgu;
            if (text.Equals(Dgu, StringComparison.OrdinalIgnoreCase)) return Dgu;
            if (text.Equals(Lam, StringComparison.OrdinalIgnoreCase)) return Lam;
            return "";
        }

        public static GlassBuildType ToBuildType(string? value) => Normalize(value) switch
        {
            Sgu => GlassBuildType.Sgu,
            Dgu => GlassBuildType.Dgu,
            Lam => GlassBuildType.Lam,
            DguLam => GlassBuildType.DguLam,
            _ => GlassBuildType.Unknown
        };

        /// <summary>Stored code for a build; Unknown has no code.</summary>
        public static string CodeOf(GlassBuildType build) => build switch
        {
            GlassBuildType.Sgu => Sgu,
            GlassBuildType.Dgu => Dgu,
            GlassBuildType.Lam => Lam,
            GlassBuildType.DguLam => DguLam,
            _ => ""
        };

        /// <summary>Panes a build consumes: 1, 2, 2 or 3.</summary>
        public static int PaneCount(GlassBuildType build) => build switch
        {
            GlassBuildType.Sgu => 1,
            GlassBuildType.Dgu => 2,
            GlassBuildType.Lam => 2,
            GlassBuildType.DguLam => 3,
            _ => 0
        };

        /// <summary>True when the build has an air spacer (a DGU cavity).</summary>
        public static bool HasSpacer(GlassBuildType build) =>
            build is GlassBuildType.Dgu or GlassBuildType.DguLam;

        /// <summary>True when the build has a PVB / interlayer material line.</summary>
        public static bool HasInterlayer(GlassBuildType build) =>
            build is GlassBuildType.Lam or GlassBuildType.DguLam;
    }

    /// <summary>One pane of glass in a build, with its price and stock sheet.</summary>
    public sealed class GlassPane
    {
        public string Thickness { get; set; } = "";
        public string Colour { get; set; } = "";
        /// <summary>Treatment wording: FT, HS, Annealed, Low-E … ("FT" = flat/tempered).</summary>
        public string Treatment { get; set; } = "";

        /// <summary>The rate the quotation is built from, per m².</summary>
        public decimal BasePricePerSqm { get; set; }

        /// <summary>What the shop actually pays per m²; 0 = not stated.</summary>
        public decimal PurchasePricePerSqm { get; set; }

        public decimal CuttingCostPerSqm { get; set; }
        public decimal TemperingCostPerSqm { get; set; }

        /// <summary>Stock sheet chosen for this pane; 0 × 0 means "not chosen".</summary>
        public int SheetWidthMm { get; set; }
        public int SheetHeightMm { get; set; }

        [JsonIgnore]
        public bool HasSheet => SheetWidthMm > 0 && SheetHeightMm > 0;

        /// <summary>"6mm Clear FT" — the wording a specification uses.</summary>
        [JsonIgnore]
        public string Display
        {
            get
            {
                var parts = new List<string>(3);
                string thickness = Thickness.Trim();
                if (thickness.Length > 0)
                    parts.Add(thickness.EndsWith("mm", StringComparison.OrdinalIgnoreCase)
                        ? thickness : thickness + "mm");
                if (Colour.Trim().Length > 0) parts.Add(Colour.Trim());
                if (Treatment.Trim().Length > 0) parts.Add(Treatment.Trim());
                return string.Join(" ", parts);
            }
        }

        public GlassPane Clone() => (GlassPane)MemberwiseClone();

        /// <summary>Cost rate Step 9 uses: the purchase rate when stated.</summary>
        [JsonIgnore]
        public decimal CostPricePerSqm =>
            PurchasePricePerSqm > 0m ? PurchasePricePerSqm : BasePricePerSqm;

        [JsonIgnore]
        public string CostBasisLabel =>
            PurchasePricePerSqm > 0m ? "purchase rate" : "quoted pane rate (no purchase rate stated)";
    }

    /// <summary>
    /// One pricing calculation. A quotation can hold several (one per distinct
    /// build) and each grid group remembers the section it came from, so a
    /// reopened invoice still re-costs against the numbers it was quoted on.
    /// </summary>
    public sealed class PricingSection
    {
        public int SectionNumber { get; set; } = 1;
        /// <summary>SGU / DGU / LAM / DGU+LAM.</summary>
        public string GlassType { get; set; } = GlassBuilds.Sgu;
        public string SpecName { get; set; } = "";

        public GlassPane Outer { get; set; } = new();
        public GlassPane Middle { get; set; } = new();
        public GlassPane Inner { get; set; } = new();

        /// <summary>Air spacer rate per m² of glazing (DGU / DGU+LAM).</summary>
        public decimal SpacerPricePerSqm { get; set; }
        /// <summary>Per-spec U-Insert rate per m²; copied to the specification snapshot.</summary>
        public bool HasUInsertExtra { get; set; }
        public decimal UInsertPricePerSqm { get; set; }
        public string SpacerWidth { get; set; } = "12";
        public string SpacerMaterial { get; set; } = "Aluminium";

        /// <summary>PVB / interlayer: what it costs and what it is sold at.</summary>
        public decimal InterlayerBuyPerSqm { get; set; }
        public decimal InterlayerSellPerSqm { get; set; }
        public string InterlayerThickness { get; set; } = "";
        public string InterlayerColour { get; set; } = "";

        /// <summary>1.0 = no yield loss. 0.85 means 15% of the glass is lost.</summary>
        public decimal YieldFactor { get; set; } = 0.85m;

        /// <summary>The profit added on top of the cost basis.</summary>
        public decimal MarginPercent { get; set; }

        /// <summary>
        /// The uplift that defines the specification target (Step 9's KPI).
        /// 15% is the reference module's approved benchmark.
        /// </summary>
        public decimal TargetUpliftPercent { get; set; } = PricingEstimator.DefaultTargetUpliftPercent;

        [JsonIgnore]
        public GlassBuildType Build => GlassBuilds.ToBuildType(GlassType);

        /// <summary>Panes this section actually consumes, in outer→inner order.</summary>
        [JsonIgnore]
        public IReadOnlyList<GlassPane> Panes
        {
            get
            {
                var panes = new List<GlassPane>(3);
                switch (Build)
                {
                    case GlassBuildType.Sgu:
                        panes.Add(Outer);
                        break;
                    case GlassBuildType.Dgu:
                    case GlassBuildType.Lam:
                        panes.Add(Outer);
                        panes.Add(Inner);
                        break;
                    case GlassBuildType.DguLam:
                        panes.Add(Outer);
                        panes.Add(Middle);
                        panes.Add(Inner);
                        break;
                }
                return panes;
            }
        }

        public PricingSection Clone() => new()
        {
            SectionNumber = SectionNumber,
            GlassType = GlassType,
            SpecName = SpecName,
            Outer = Outer.Clone(),
            Middle = Middle.Clone(),
            Inner = Inner.Clone(),
            SpacerPricePerSqm = SpacerPricePerSqm,
            HasUInsertExtra = HasUInsertExtra,
            UInsertPricePerSqm = UInsertPricePerSqm,
            SpacerWidth = SpacerWidth,
            SpacerMaterial = SpacerMaterial,
            InterlayerBuyPerSqm = InterlayerBuyPerSqm,
            InterlayerSellPerSqm = InterlayerSellPerSqm,
            InterlayerThickness = InterlayerThickness,
            InterlayerColour = InterlayerColour,
            YieldFactor = YieldFactor,
            MarginPercent = MarginPercent,
            TargetUpliftPercent = TargetUpliftPercent
        };
    }

    /// <summary>
    /// The cost inputs a grid group carries with it after pricing. This is the
    /// provenance lesson from the reference module's Phase 1 audit: a group that
    /// was quoted as DGU grey/clear must not be re-costed later with whatever
    /// section happens to be selected on screen.
    /// </summary>
    public sealed class SpecCostCard
    {
        public string GlassType { get; set; } = "";
        public string SpecName { get; set; } = "";
        public decimal QuotedRatePerSqm { get; set; }
        public decimal CostBasisPerSqm { get; set; }
        public decimal YieldFactor { get; set; } = 1m;
        public decimal TargetUpliftPercent { get; set; }

        /// <summary>Panes in outer→inner order, each with its own rate and sheet.</summary>
        public List<SpecCostPane> Panes { get; set; } = new();

        public decimal SpacerCostPerSqm { get; set; }
        public decimal InterlayerCostPerSqm { get; set; }
        public decimal CuttingCostPerSqm { get; set; }
        public decimal TemperingCostPerSqm { get; set; }

        [JsonIgnore]
        public bool HasCostData => Panes.Count > 0;
    }

    /// <summary>One pane of a cost card: the numbers Step 9 needs.</summary>
    public sealed class SpecCostPane
    {
        public string PaneLabel { get; set; } = "";
        /// <summary>The pane's glass colour, so a per-colour wastage row can own its sheet quantity.</summary>
        public string Colour { get; set; } = "";
        public int SheetWidthMm { get; set; }
        public int SheetHeightMm { get; set; }
        public decimal CostPricePerSqm { get; set; }
        public string CostBasis { get; set; } = "";
        [JsonIgnore]
        public bool HasSheet => SheetWidthMm > 0 && SheetHeightMm > 0;
    }

    /// <summary>What one pricing calculation produced.</summary>
    public sealed class PricingResult
    {
        public bool IsComplete { get; set; }
        public string SpecName { get; set; } = "";
        public decimal RatePerSqm { get; set; }
        public decimal CostBasisPerSqm { get; set; }
        public decimal YieldFactorUsed { get; set; }
        public SpecCostCard CostCard { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        public string FormulaText { get; set; } = "";
    }

    /// <summary>Formats a number for a specification name without a culture surprise.</summary>
    internal static class PricingText
    {
        public static string Number(decimal value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
