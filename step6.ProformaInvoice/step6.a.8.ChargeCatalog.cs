// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.8.ChargeCatalog.cs
// MODULE: step6 — stage 4 (ADV step 6): the predefined charge catalogue.
//
// Templates carry DEFAULTS only. The rate and the on/off state belong to the
// invoice, so two quotations for the same glass can price polishing
// differently without editing a global table.
//
// A template states the metric its quantity is measured with, because the
// charge is meaningless without one: argon gas is billed per m² of cavity,
// silicon bed per linear metre of edge, a cutout per piece. The operator can
// change the metric on a row; the default is the one the trade uses.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>What a charge's quantity is measured against.</summary>
    public enum ChargeMetric
    {
        /// <summary>m² of glass across the whole grid.</summary>
        TotalSqm = 0,
        /// <summary>Linear metres of edge across the whole grid.</summary>
        TotalLm = 1,
        /// <summary>Pieces (panels) across the whole grid.</summary>
        TotalPieces = 2,
        /// <summary>A quantity the operator types (trips, cutouts on a drawing).</summary>
        FixedQuantity = 3
    }

    /// <summary>Billing unit a charge is written in.</summary>
    public enum ChargeUnit
    {
        Sqm = 0,
        Lm = 1,
        Qty = 2
    }

    /// <summary>One predefined charge's default wording, unit and rate.</summary>
    public sealed class ChargeTemplate
    {
        public string Key { get; init; } = "";
        public string ChargeName { get; init; } = "";
        public string Description { get; init; } = "";
        public ChargeUnit Unit { get; init; }
        public ChargeMetric DefaultMetric { get; init; }
        public decimal DefaultRate { get; init; }

        /// <summary>
        /// Multiplies the measured quantity. 2 for "polish both panes" — the
        /// charge is per linear metre of edge and a laminated unit has two.
        /// </summary>
        public decimal MetricMultiplier { get; init; } = 1m;

        public string UnitText => ChargeCatalog.UnitText(Unit);
    }

    /// <summary>The immutable catalogue. Rates here are defaults, never truth.</summary>
    public static class ChargeCatalog
    {
        public static readonly IReadOnlyList<ChargeTemplate> Templates = new List<ChargeTemplate>
        {
            new() { Key = "ARGON_GAS", ChargeName = "Argon Gas (m²)",
                    Description = "Inert argon filling inside a DGU cavity for thermal insulation.",
                    Unit = ChargeUnit.Sqm, DefaultMetric = ChargeMetric.TotalSqm, DefaultRate = 45m },
            new() { Key = "SILICON_BED", ChargeName = "Silicon bed (lm)",
                    Description = "Secondary structural silicone seal to the DGU perimeter.",
                    Unit = ChargeUnit.Lm, DefaultMetric = ChargeMetric.TotalLm, DefaultRate = 6m },
            new() { Key = "SILICON_BED_SQM", ChargeName = "Silicon bed (m²)",
                    Description = "Silicone seal charged by area for high-overlap work.",
                    Unit = ChargeUnit.Sqm, DefaultMetric = ChargeMetric.TotalSqm, DefaultRate = 6m },
            new() { Key = "OVERLAP", ChargeName = "Overlap (lm)",
                    Description = "Stepped laminate offset or spacer overlap, by perimeter.",
                    Unit = ChargeUnit.Lm, DefaultMetric = ChargeMetric.TotalLm, DefaultRate = 6m },
            new() { Key = "OVERLAP_SQM", ChargeName = "Overlap (m²)",
                    Description = "Overlap work charged by glass area instead of perimeter.",
                    Unit = ChargeUnit.Sqm, DefaultMetric = ChargeMetric.TotalSqm, DefaultRate = 6m },
            new() { Key = "SHUTTER_BONDING", ChargeName = "Shutter bonding (lm)",
                    Description = "Structural bonding of the glass into the window shutter.",
                    Unit = ChargeUnit.Lm, DefaultMetric = ChargeMetric.TotalLm, DefaultRate = 25m },
            new() { Key = "MITRING", ChargeName = "Mitring (lm)",
                    Description = "45° bevel edge for a mitred joint assembly.",
                    Unit = ChargeUnit.Lm, DefaultMetric = ChargeMetric.TotalLm, DefaultRate = 30m },
            new() { Key = "POLISH", ChargeName = "Polish (lm)",
                    Description = "Edge polishing of a single pane or a mixed-glass unit.",
                    Unit = ChargeUnit.Lm, DefaultMetric = ChargeMetric.TotalLm, DefaultRate = 6m },
            new() { Key = "POLISH_MATCHED_LAM", ChargeName = "Polish — same-glass unit (2 × lm)",
                    Description = "Both panes polished when the unit is the same glass throughout.",
                    Unit = ChargeUnit.Lm, DefaultMetric = ChargeMetric.TotalLm,
                    DefaultRate = 6m, MetricMultiplier = 2m },
            new() { Key = "CUTOUT", ChargeName = "Cutout (qty)",
                    Description = "Water-jet internal cutout for hinges or structural patches.",
                    Unit = ChargeUnit.Qty, DefaultMetric = ChargeMetric.FixedQuantity, DefaultRate = 50m },
            new() { Key = "HOLES", ChargeName = "Holes (qty)",
                    Description = "Drilled fixing hole, 8–15 mm.",
                    Unit = ChargeUnit.Qty, DefaultMetric = ChargeMetric.FixedQuantity, DefaultRate = 15m },
            new() { Key = "SANDBLASTING", ChargeName = "Sandblasting (m²)",
                    Description = "Abrasive frosting for decorative privacy.",
                    Unit = ChargeUnit.Sqm, DefaultMetric = ChargeMetric.TotalSqm, DefaultRate = 45m },
            new() { Key = "EDGE_GRINDING", ChargeName = "Edge grinding (lm)",
                    Description = "Rough edge grind before polishing.",
                    Unit = ChargeUnit.Lm, DefaultMetric = ChargeMetric.TotalLm, DefaultRate = 4m },
            new() { Key = "COUNTERSUNK_HOLES", ChargeName = "Countersunk holes (qty)",
                    Description = "Countersunk hole for a flush fixing.",
                    Unit = ChargeUnit.Qty, DefaultMetric = ChargeMetric.FixedQuantity, DefaultRate = 25m },
            new() { Key = "FAN_HOLES", ChargeName = "Fan holes (qty)",
                    Description = "Large-diameter vent or exhaust hole.",
                    Unit = ChargeUnit.Qty, DefaultMetric = ChargeMetric.FixedQuantity, DefaultRate = 35m },
            new() { Key = "DELIVERY", ChargeName = "Delivery (per trip)",
                    Description = "Site delivery, flat rate per trip.",
                    Unit = ChargeUnit.Qty, DefaultMetric = ChargeMetric.FixedQuantity, DefaultRate = 150m },
            new() { Key = "U_INSERT", ChargeName = "U-Insert (m²)",
                    Description = "U-channel gasket for structural glazing edge sealing.",
                    Unit = ChargeUnit.Sqm, DefaultMetric = ChargeMetric.TotalSqm, DefaultRate = 10m }
        };

        private static readonly Dictionary<string, ChargeTemplate> ByKey =
            Templates.ToDictionary(template => template.Key, StringComparer.OrdinalIgnoreCase);

        public static ChargeTemplate? Find(string? key)
        {
            string text = (key ?? "").Trim();
            return text.Length > 0 && ByKey.TryGetValue(text, out ChargeTemplate? template)
                ? template
                : null;
        }

        public static string UnitText(ChargeUnit unit) => unit switch
        {
            ChargeUnit.Sqm => "m²",
            ChargeUnit.Lm => "lm",
            ChargeUnit.Qty => "qty",
            _ => ""
        };

        public static string MetricText(ChargeMetric metric) => metric switch
        {
            ChargeMetric.TotalSqm => "Total m² of the grid",
            ChargeMetric.TotalLm => "Total linear metres of the grid",
            ChargeMetric.TotalPieces => "Total pieces of the grid",
            ChargeMetric.FixedQuantity => "Quantity typed on the row",
            _ => ""
        };

        /// <summary>The metrics a unit can sensibly be measured with.</summary>
        public static IReadOnlyList<ChargeMetric> MetricsFor(ChargeUnit unit) => unit switch
        {
            ChargeUnit.Sqm => new[] { ChargeMetric.TotalSqm, ChargeMetric.TotalPieces, ChargeMetric.FixedQuantity },
            ChargeUnit.Lm => new[] { ChargeMetric.TotalLm, ChargeMetric.TotalPieces, ChargeMetric.FixedQuantity },
            ChargeUnit.Qty => new[] { ChargeMetric.TotalPieces, ChargeMetric.FixedQuantity, ChargeMetric.TotalSqm },
            _ => Array.Empty<ChargeMetric>()
        };
    }
}
