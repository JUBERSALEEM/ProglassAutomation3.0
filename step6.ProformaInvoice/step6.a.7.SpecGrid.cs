// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.7.SpecGrid.cs
// MODULE: step6 — stage 3 (ADV step 5): the specification grid.
//
// A group is one priced specification ("6mm Clear FT + 12mm ASP + 6mm Clear
// FT — outer HD Grey / inner Clear"), and its rows are the measured panels:
// W1/H1 is the primary panel, W2/H2 the secondary pane size when the build
// needs a different one. Every row is priced per m² of the panel it describes,
// so quantity, area and value reconcile to the printed grid.
//
// PROVENANCE, KEPT DELIBERATELY
//   A group carries the cost card of the pricing section it was created from
//   and one flag per field an operator may override. Without the card, Step 9
//   would have to re-cost from whatever section is selected on screen; without
//   the flags, an operator's corrected rate would be silently overwritten the
//   next time the estimator changes. Both are the reference module's recorded
//   defects, and both are prevented here rather than documented as risks.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Linq;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>One measured panel line inside a specification group.</summary>
    public sealed class SpecRow
    {
        public int SerialNumber { get; set; }
        /// <summary>Optional pane reference an operator writes on the drawing.</summary>
        public string GlassRef { get; set; } = "W1";
        public int W1 { get; set; }
        public int H1 { get; set; }
        public int W2 { get; set; }
        public int H2 { get; set; }
        public int Qty { get; set; } = 1;

        /// <summary>Per-row rate override in AED/m²; 0 = the group's rate.</summary>
        public decimal RateOverridePerSqm { get; set; }

        /// <summary>Billable area of ONE unit: panel 1 plus panel 2 when given.</summary>
        [JsonIgnore]
        public decimal PanelSqm => MoneyRounding.RoundArea(
            GlassMeasure.PanelSqm(W1, H1) +
            (W2 > 0 && H2 > 0 ? GlassMeasure.PanelSqm(W2, H2) : 0m));

        /// <summary>Billable area for the whole quantity.</summary>
        [JsonIgnore]
        public decimal TotalSqm => Qty <= 0 ? 0m : MoneyRounding.RoundArea(PanelSqm * Qty);

        /// <summary>Edge length of the whole quantity, in linear metres.</summary>
        [JsonIgnore]
        public decimal TotalLm
        {
            get
            {
                if (Qty <= 0) return 0m;
                decimal unit = GlassMeasure.PanelLm(W1, H1) +
                    (W2 > 0 && H2 > 0 ? GlassMeasure.PanelLm(W2, H2) : 0m);
                return Math.Round(unit * Qty, 3);
            }
        }

        /// <summary>A row can be priced only when it describes a real panel.</summary>
        [JsonIgnore]
        public bool IsMeasurable => W1 > 0 && H1 > 0 && Qty > 0;

        /// <summary>Value of this row at the supplied group rate.</summary>
        public decimal ValueAt(decimal groupRatePerSqm)
        {
            decimal rate = RateOverridePerSqm > 0m ? RateOverridePerSqm : groupRatePerSqm;
            return MoneyRounding.Round(TotalSqm * rate);
        }

        public SpecRow Clone() => (SpecRow)MemberwiseClone();
    }

    /// <summary>One priced specification with its measured rows and its cost provenance.</summary>
    public sealed class SpecGroup
    {
        public int GroupNumber { get; set; }
        public string GroupName { get; set; } = "";
        /// <summary>SGU / DGU / LAM / DGU+LAM.</summary>
        public string GlassType { get; set; } = GlassBuilds.Sgu;
        public string SpecName { get; set; } = "";

        public string OuterGlassColor { get; set; } = "";
        public string InnerGlassColor { get; set; } = "";
        /// <summary>Third pane colour of a DGU+LAM build; empty on other builds.</summary>
        public string ThirdGlassColor { get; set; } = "";

        /// <summary>The estimator rate this group was created with (AED/m²).</summary>
        public decimal ImportedRatePerSqm { get; set; }

        /// <summary>The rate the grid prices at (AED/m²).</summary>
        public decimal BaseRatePerSqm { get; set; }

        /// <summary>An operator uplift on the base rate, in percent.</summary>
        public decimal SurchargePercent { get; set; }

        // ── Manual overrides, so a re-price never silently discards an edit ──
        public bool HasManualSpecOverride { get; set; }
        public bool HasManualGlassTypeOverride { get; set; }
        public bool HasManualRateOverride { get; set; }

        /// <summary>The cost inputs this group was quoted on (Step 9 provenance).</summary>
        public SpecCostCard CostCard { get; set; } = new();

        public List<SpecRow> Rows { get; set; } = new();

        /// <summary>The rate a row without its own override is priced at.</summary>
        [JsonIgnore]
        public decimal EffectiveRatePerSqm =>
            MoneyRounding.Round(BaseRatePerSqm * (1m + SurchargePercent / 100m));

        [JsonIgnore]
        public decimal TotalSqm => MoneyRounding.RoundArea(Rows.Sum(row => row.TotalSqm));

        [JsonIgnore]
        public int TotalPieces => Rows.Where(row => row.Qty > 0).Sum(row => row.Qty);

        [JsonIgnore]
        public decimal TotalLm => Math.Round(Rows.Sum(row => row.TotalLm), 3);

        /// <summary>Every row priced at the group rate unless it carries an override.</summary>
        [JsonIgnore]
        public decimal TotalValue => MoneyRounding.Round(
            Rows.Sum(row => row.ValueAt(EffectiveRatePerSqm)));

        /// <summary>
        /// The group is priced when it names a build, a specification, a
        /// non-negative rate and has at least one measurable row. A typo such as
        /// a 0 mm width must block the save, not print a zero-value line.
        /// </summary>
        [JsonIgnore]
        public bool IsPriced =>
            GlassBuilds.Normalize(GlassType).Length > 0 &&
            SpecName.Trim().Length > 0 &&
            BaseRatePerSqm >= 0m &&
            Rows.Any(row => row.IsMeasurable);

        [JsonIgnore]
        public string DisplayText
        {
            get
            {
                string name = GroupName.Trim().Length > 0 ? GroupName.Trim() : SpecName.Trim();
                return name.Length == 0 ? "(unnamed specification)" : name;
            }
        }

        /// <summary>Area of the outer pane, used by per-colour wastage rows.</summary>
        [JsonIgnore]
        public decimal OuterColorSqm => ColorSqm(OuterGlassColor);

        [JsonIgnore]
        public decimal InnerColorSqm => ColorSqm(InnerGlassColor);

        [JsonIgnore]
        public decimal ThirdColorSqm => ColorSqm(ThirdGlassColor);

        /// <summary>
        /// Billable area belonging to one colour, counted ONCE PER PANE that uses
        /// it: a DGU whose two panes are both Clear consumes two panes of Clear
        /// glass for every unit of area, and costing it as one would understate
        /// the sheets the job buys.
        /// </summary>
        public decimal ColorSqm(string color)
        {
            string wanted = (color ?? "").Trim();
            if (wanted.Length == 0) return 0m;
            int panes = 0;
            foreach (string candidate in new[] { OuterGlassColor, InnerGlassColor, ThirdGlassColor })
                if (string.Equals((candidate ?? "").Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                    panes++;
            return panes == 0 ? 0m : MoneyRounding.RoundArea(TotalSqm * panes);
        }

        /// <summary>Renumbers the serial column after an add, remove or reorder.</summary>
        public void Renumber()
        {
            for (int index = 0; index < Rows.Count; index++)
                Rows[index].SerialNumber = index + 1;
        }

        /// <summary>
        /// Applies a pricing result to a NEW group. The manual-override flags are
        /// respected: an existing operator edit survives, a fresh group takes the
        /// estimator's values.
        /// </summary>
        public void ApplyPricing(PricingSection section, PricingResult result)
        {
            if (section == null || result == null) return;

            if (!HasManualGlassTypeOverride)
                GlassType = GlassBuilds.CodeOf(section.Build);
            if (!HasManualSpecOverride || SpecName.Trim().Length == 0)
                SpecName = result.SpecName;
            if (!HasManualRateOverride)
            {
                ImportedRatePerSqm = result.RatePerSqm;
                BaseRatePerSqm = result.RatePerSqm;
            }
            CostCard = result.CostCard ?? new SpecCostCard();

            var panes = result.CostCard?.Panes ?? new List<SpecCostPane>();
            if (!HasManualGlassTypeOverride)
            {
                OuterGlassColor = section.Outer.Colour.Trim();
                if (panes.Count >= 2) InnerGlassColor = section.Inner.Colour.Trim();
                if (panes.Count >= 3) ThirdGlassColor = section.Middle.Colour.Trim();
            }
        }

        public SpecGroup Clone() => new()
        {
            GroupNumber = GroupNumber,
            GroupName = GroupName,
            GlassType = GlassType,
            SpecName = SpecName,
            OuterGlassColor = OuterGlassColor,
            InnerGlassColor = InnerGlassColor,
            ThirdGlassColor = ThirdGlassColor,
            ImportedRatePerSqm = ImportedRatePerSqm,
            BaseRatePerSqm = BaseRatePerSqm,
            SurchargePercent = SurchargePercent,
            HasManualSpecOverride = HasManualSpecOverride,
            HasManualGlassTypeOverride = HasManualGlassTypeOverride,
            HasManualRateOverride = HasManualRateOverride,
            CostCard = new SpecCostCard
            {
                GlassType = CostCard.GlassType,
                SpecName = CostCard.SpecName,
                QuotedRatePerSqm = CostCard.QuotedRatePerSqm,
                CostBasisPerSqm = CostCard.CostBasisPerSqm,
                YieldFactor = CostCard.YieldFactor,
                TargetUpliftPercent = CostCard.TargetUpliftPercent,
                SpacerCostPerSqm = CostCard.SpacerCostPerSqm,
                InterlayerCostPerSqm = CostCard.InterlayerCostPerSqm,
                CuttingCostPerSqm = CostCard.CuttingCostPerSqm,
                TemperingCostPerSqm = CostCard.TemperingCostPerSqm,
                Panes = CostCard.Panes.Select(pane => new SpecCostPane
                {
                    PaneLabel = pane.PaneLabel,
                    Colour = pane.Colour,
                    SheetWidthMm = pane.SheetWidthMm,
                    SheetHeightMm = pane.SheetHeightMm,
                    CostPricePerSqm = pane.CostPricePerSqm,
                    CostBasis = pane.CostBasis
                }).ToList()
            },
            Rows = Rows.Select(row => row.Clone()).ToList()
        };
    }

    /// <summary>Grid totals the summary stage consumes.</summary>
    public sealed class SpecGridTotals
    {
        public int GroupCount { get; init; }
        public decimal TotalSqm { get; init; }
        public decimal TotalLm { get; init; }
        public int TotalPieces { get; init; }
        public decimal TotalValue { get; init; }

        public static SpecGridTotals From(IReadOnlyList<SpecGroup>? groups)
        {
            var list = groups ?? Array.Empty<SpecGroup>();
            return new SpecGridTotals
            {
                GroupCount = list.Count,
                TotalSqm = MoneyRounding.RoundArea(list.Sum(group => group.TotalSqm)),
                TotalLm = Math.Round(list.Sum(group => group.TotalLm), 3),
                TotalPieces = list.Sum(group => group.TotalPieces),
                TotalValue = MoneyRounding.Round(list.Sum(group => group.TotalValue))
            };
        }
    }
}
