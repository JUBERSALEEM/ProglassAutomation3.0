using System.Collections.Generic;

namespace ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges
{
    /// <summary>
    /// Immutable seed data for the predefined Other Charges rows shown in Step 6.
    /// User can enable/disable and edit rate per invoice — templates just provide defaults.
    /// </summary>
    public static class PredefinedChargeTemplates
    {
        public class Template
        {
            public string Key { get; set; } = "";
            public string ChargeName { get; set; } = "";
            public string Description { get; set; } = "";
            public OtherChargeUnit Unit { get; set; }
            public decimal DefaultRate { get; set; }
            public string DefaultSourceKey { get; set; } = ""; // suggested source when checked
        }

        public static List<Template> GetAll() => new()
        {
            new Template
            {
                Key = "ARGON_GAS",
                ChargeName = "Argon Gas (Sqm)",
                Description = "Inert Argon gas infusion inside DGU cavity for thermal insulation.",
                Unit = OtherChargeUnit.Sqm,
                DefaultRate = 45m,
                DefaultSourceKey = "SQM1"
            },
            new Template
            {
                Key = "SILICON_BED",
                ChargeName = "Silicon bed (lm)",
                Description = "Secondary sealant structural silicone bed barrier for double glazing.",
                Unit = OtherChargeUnit.Lm,
                DefaultRate = 6m,
                DefaultSourceKey = "LM1"
            },
            new Template
            {
                Key = "SILICON_BED_SQM",
                ChargeName = "Silicon bed (Sqm)",
                Description = "Secondary sealant structural silicone bed barrier for special overlap jobs charged by area.",
                Unit = OtherChargeUnit.Sqm,
                DefaultRate = 6m,
                DefaultSourceKey = "SQM1"
            },
            new Template
            {
                Key = "OVERLAP",
                ChargeName = "Overlap (lm)",
                Description = "Stepped laminate offset or double glazed spacer overlap edge charged by perimeter.",
                Unit = OtherChargeUnit.Lm,
                DefaultRate = 6m,
                DefaultSourceKey = "LM1"
            },
            new Template
            {
                Key = "OVERLAP_SQM",
                ChargeName = "Overlap (Sqm)",
                Description = "Special overlap mode charged by glass area instead of perimeter.",
                Unit = OtherChargeUnit.Sqm,
                DefaultRate = 6m,
                DefaultSourceKey = "SQM1"
            },
            new Template
            {
                Key = "SHUTTER_BONDING",
                ChargeName = "Shutter Bonding (lm)",
                Description = "Structural bonding of glazing glass sheets into the window shutter.",
                Unit = OtherChargeUnit.Lm,
                DefaultRate = 25m,
                DefaultSourceKey = "LM_COMBINED"
            },
            new Template
            {
                Key = "MITRING",
                ChargeName = "Mitring (lm)",
                Description = "Beveled edge cutting (45 degrees) for custom joint assemblies.",
                Unit = OtherChargeUnit.Lm,
                DefaultRate = 30m,
                DefaultSourceKey = "LM1"
            },

            // ═══════════════ Phase 7.11b ═══════════════

            new Template
            {
                Key = "POLISH",
                ChargeName = "Polish (lm)",
                Description = "SGU, mixed laminate (HD Grey + Clear) or single-pane polish. Sub-type controls formula.",
                Unit = OtherChargeUnit.Lm,
                DefaultRate = 6m,
                DefaultSourceKey = "" // Polish uses sub-type, not source dropdown
            },
            new Template
            {
                Key = "POLISH_MATCHED_LAM",
                ChargeName = "Polish — same-glass laminate (2×)",
                Description = "Auto 2× when both panes match (Clear + Clear LAM / DGU). Mixed glass stays on regular Polish.",
                Unit = OtherChargeUnit.Lm,
                DefaultRate = 6m,
                DefaultSourceKey = ""
            },
            new Template
            {
                Key = "CUTOUT",
                ChargeName = "Cutout (Qty)",
                Description = "Water-jet internal precision cutouts for hinges and structural patches.",
                Unit = OtherChargeUnit.Qty,
                DefaultRate = 50m,
                DefaultSourceKey = "TOTAL_PIECES"
            },
            new Template
            {
                Key = "HOLES",
                ChargeName = "Holes (Qty)",
                Description = "Structural glass hole drilling (8mm to 15mm) on CNC drillers.",
                Unit = OtherChargeUnit.Qty,
                DefaultRate = 15m,
                DefaultSourceKey = "TOTAL_PIECES"
            },

            // ═══════════════ Phase 7.1 (Chat 7) — 5 additional templates ═══════════════

            new Template
            {
                Key = "SANDBLASTING",
                ChargeName = "Sandblasting (Sqm)",
                Description = "Surface abrasive frosting via silica sand blasting for decorative privacy.",
                Unit = OtherChargeUnit.Sqm,
                DefaultRate = 45m,
                DefaultSourceKey = "SQM1"
            },
            new Template
            {
                Key = "EDGE_GRINDING",
                ChargeName = "Edge Grinding (lm)",
                Description = "Rough edge grinding before polish — smooths raw glass cut edges.",
                Unit = OtherChargeUnit.Lm,
                DefaultRate = 4m,
                DefaultSourceKey = "LM1"
            },
            new Template
            {
                Key = "COUNTERSUNK_HOLES",
                ChargeName = "Countersunk Holes (Qty)",
                Description = "Countersunk hole drilling for flush-mounted screws (bathroom/shower fittings).",
                Unit = OtherChargeUnit.Qty,
                DefaultRate = 25m,
                DefaultSourceKey = "TOTAL_PIECES"
            },
            new Template
            {
                Key = "FAN_HOLES",
                ChargeName = "Fan Holes (Qty)",
                Description = "Large-diameter fan/vent holes for exhaust and ventilation.",
                Unit = OtherChargeUnit.Qty,
                DefaultRate = 35m,
                DefaultSourceKey = "TOTAL_PIECES"
            },
            new Template
            {
                Key = "DELIVERY",
                ChargeName = "Delivery (per trip)",
                Description = "Site delivery charge — flat rate per trip.",
                Unit = OtherChargeUnit.Qty,
                DefaultRate = 150m,
                DefaultSourceKey = "TOTAL_PIECES"
            },

            // ═══════════════ Phase 7.1 (Chat 7) — U-Insert added per developer request ═══════════════

            new Template
            {
                Key = "U_INSERT",
                ChargeName = "U-Insert (Sqm)",
                Description = "U-channel gasket/rubber insertion for structural glazing edge sealing. Rate editable per invoice.",
                Unit = OtherChargeUnit.Sqm,
                DefaultRate = 10m,
                DefaultSourceKey = "SQM1"
            }

            // Phase 7.11c will add: Wastages (color-aggregated)
        };
    }
}