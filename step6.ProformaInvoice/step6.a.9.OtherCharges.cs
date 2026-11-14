// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.9.OtherCharges.cs
// MODULE: step6 — stage 4 (ADV step 6): predefined, wastage and custom charges.
//
// Three kinds, kept apart on purpose:
//   • predefined — the trade's standard extras (argon, silicon, polish …).
//     A row is measured against the grid, so its quantity follows Step 5 and
//     its amount is rate × quantity. It is never a hard-coded number.
//   • wastage — one row per GLASS COLOUR present in the grid. The colour list
//     is derived, never typed: three colours in the grid means three wastage
//     rows. At least one sheet is bought for a pane whatever the gloss, which
//     is what the sheet-quantity box records for Step 9.
//   • custom — anything else, priced as quantity × rate, typed by the operator.
//
// MANUAL WORK IS NEVER LOST
//   Automation rebuilds only what it generated. A row the operator switched on
//   keeps its switch (HasManualActiveOverride), and a wastage row added by hand
//   survives every refresh. This is the reference module's "manual-row
//   preservation" rule and its recorded defect when it was missing.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>One standard charge line on the invoice.</summary>
    public sealed class PredefinedChargeRow
    {
        public string TemplateKey { get; set; } = "";
        public string ChargeName { get; set; } = "";
        public string Description { get; set; } = "";
        public ChargeUnit Unit { get; set; }
        public ChargeMetric Metric { get; set; }
        public decimal MetricMultiplier { get; set; } = 1m;

        public bool IsActive { get; set; }
        public decimal Rate { get; set; }

        /// <summary>The quantity when <see cref="Metric"/> is FixedQuantity.</summary>
        public decimal FixedQuantity { get; set; } = 1m;

        /// <summary>Measured/typed quantity at the last recalculation.</summary>
        public decimal Quantity { get; set; }

        /// <summary>Amount at the last recalculation, in AED.</summary>
        public decimal Amount { get; set; }

        /// <summary>True once the operator chose this row's on/off state.</summary>
        public bool HasManualActiveOverride { get; set; }

        [JsonIgnore]
        public string UnitText => ChargeCatalog.UnitText(Unit);

        /// <summary>Automation may suggest a state; it may not win an argument.</summary>
        public void ApplySuggestedActive(bool active)
        {
            if (HasManualActiveOverride) return;
            IsActive = active;
        }

        /// <summary>Restores a saved state as an explicit operator choice.</summary>
        public void RestoreActive(bool active)
        {
            IsActive = active;
            HasManualActiveOverride = true;
        }

        public void SetManualActive(bool active)
        {
            IsActive = active;
            HasManualActiveOverride = true;
        }

        /// <summary>Quantity from the grid, or the typed quantity for a fixed row.</summary>
        public decimal ResolveQuantity(SpecGridTotals totals)
        {
            decimal measured = Metric switch
            {
                ChargeMetric.TotalSqm => string.Equals(TemplateKey, "U_INSERT", StringComparison.OrdinalIgnoreCase)
                    ? totals.UInsertSqm : totals.TotalSqm,
                ChargeMetric.TotalLm => totals.TotalLm,
                ChargeMetric.PolishLm => totals.PolishLm,
                ChargeMetric.MatchedPolishLm => totals.MatchedPolishLm,
                ChargeMetric.TotalPieces => totals.TotalPieces,
                ChargeMetric.FixedQuantity => FixedQuantity,
                _ => 0m
            };
            decimal multiplier = MetricMultiplier <= 0m ? 1m : MetricMultiplier;
            return Math.Round(measured * multiplier, 3);
        }

        public void Recalculate(SpecGridTotals totals)
        {
            Quantity = ResolveQuantity(totals);
            if (!IsActive)
            {
                Amount = 0m;
                return;
            }
            Amount = string.Equals(TemplateKey, "U_INSERT", StringComparison.OrdinalIgnoreCase)
                ? totals.UInsertAmount(Rate)
                : MoneyRounding.Round(Quantity * Rate);
        }
    }

    /// <summary>One wastage line, per glass colour present in the grid.</summary>
    public sealed class WastageChargeRow
    {
        public bool IsActive { get; set; }
        public string ColorName { get; set; } = "";
        public decimal Rate { get; set; }
        public bool IsOptionalSuggestion { get; set; }
        public string SuggestionReason { get; set; } = "";

        /// <summary>Billable area of that colour at the last recalculation.</summary>
        public decimal AreaSqm { get; set; }

        /// <summary>Amount at the last recalculation, in AED.</summary>
        public decimal Amount { get; set; }

        /// <summary>An operator added this row; automation must not remove it.</summary>
        public bool IsManuallyAdded { get; set; }

        /// <summary>Derive the rate from the optimization formula instead of <see cref="Rate"/>.</summary>
        public bool UseOptimizationFormula { get; set; }
        public decimal FormulaPricePerSqm { get; set; }
        public decimal OptimizationPercent { get; set; }

        /// <summary>
        /// Whole sheets the shop buys for this colour. Read by Step 9 even when
        /// the wastage CHARGE is switched off: the switch bills the allowance,
        /// it does not decide how much glass the job consumes.
        /// </summary>
        private int _sheetQuantity = 1;
        public int SheetQuantity
        {
            get => _sheetQuantity;
            set => _sheetQuantity = Math.Max(0, value);
        }

        /// <summary>The rate actually used, from the formula when it is enabled.</summary>
        public decimal EffectiveRate
        {
            get
            {
                if (!UseOptimizationFormula) return Rate;
                decimal percent = MoneyRounding.ClampPercent(OptimizationPercent, 0m, 500m);
                return MoneyRounding.Round(FormulaPricePerSqm * percent / 100m);
            }
        }

        public void Recalculate()
        {
            Amount = IsActive ? MoneyRounding.Round(AreaSqm * EffectiveRate) : 0m;
        }
    }

    /// <summary>One operator-defined charge line.</summary>
    public sealed class CustomChargeRow
    {
        public bool IsActive { get; set; } = true;
        public string ChargeName { get; set; } = "";
        public decimal Qty { get; set; }
        public ChargeUnit Unit { get; set; }
        public decimal Rate { get; set; }

        [JsonIgnore]
        public decimal Amount => IsActive ? MoneyRounding.Round(Qty * Rate) : 0m;

        [JsonIgnore]
        public string UnitText => ChargeCatalog.UnitText(Unit);
    }

    /// <summary>The whole stage-4 worksheet.</summary>
    public sealed class OtherChargesSheet
    {
        /// <summary>
        /// True: the grid drives quantities and suggestions. False: the
        /// operator's toggles win and nothing is refreshed automatically.
        /// </summary>
        public bool UseAdvancedAutomation { get; set; } = true;

        public List<PredefinedChargeRow> Predefined { get; set; } = new();
        public List<WastageChargeRow> Wastages { get; set; } = new();
        public List<CustomChargeRow> Custom { get; set; } = new();

        [JsonIgnore]
        public decimal PredefinedTotal => MoneyRounding.Round(
            Predefined.Where(row => row.IsActive).Sum(row => row.Amount));

        [JsonIgnore]
        public decimal WastageTotal => MoneyRounding.Round(
            Wastages.Where(row => row.IsActive).Sum(row => row.Amount));

        [JsonIgnore]
        public decimal CustomTotal => MoneyRounding.Round(Custom.Sum(row => row.Amount));

        [JsonIgnore]
        public decimal Total => MoneyRounding.Round(PredefinedTotal + WastageTotal + CustomTotal);

        [JsonIgnore]
        public int ActiveRowCount =>
            Predefined.Count(row => row.IsActive) +
            Wastages.Count(row => row.IsActive) +
            Custom.Count(row => row.IsActive);

        /// <summary>
        /// Creates any catalogue row this worksheet is missing, keeping the rates
        /// and switches already stored. Safe to call on every load.
        /// </summary>
        public void EnsureCatalogueRows()
        {
            foreach (ChargeTemplate template in ChargeCatalog.Templates)
            {
                if (Predefined.Any(row => string.Equals(row.TemplateKey, template.Key,
                        StringComparison.OrdinalIgnoreCase)))
                    continue;

                Predefined.Add(new PredefinedChargeRow
                {
                    TemplateKey = template.Key,
                    ChargeName = template.ChargeName,
                    Description = template.Description,
                    Unit = template.Unit,
                    Metric = template.DefaultMetric,
                    MetricMultiplier = template.MetricMultiplier,
                    Rate = template.DefaultRate,
                    IsActive = false
                });
            }

            // JSON bodies saved before the geometry-aware polish metrics used
            // TotalLm × 2 for matched glass. Upgrade only those exact catalogue
            // defaults; active/manual on-off choices remain untouched.
            PredefinedChargeRow? regularPolish = Predefined.FirstOrDefault(row =>
                string.Equals(row.TemplateKey, "POLISH", StringComparison.OrdinalIgnoreCase));
            if (regularPolish?.Metric == ChargeMetric.TotalLm && regularPolish.MetricMultiplier == 1m)
                regularPolish.Metric = ChargeMetric.PolishLm;

            PredefinedChargeRow? matchedPolish = Predefined.FirstOrDefault(row =>
                string.Equals(row.TemplateKey, "POLISH_MATCHED_LAM", StringComparison.OrdinalIgnoreCase));
            if (matchedPolish?.Metric == ChargeMetric.TotalLm && matchedPolish.MetricMultiplier == 2m)
            {
                matchedPolish.Metric = ChargeMetric.MatchedPolishLm;
                matchedPolish.MetricMultiplier = 1m;
            }
        }

        /// <summary>
        /// Rebuilds the generated wastage rows from the grid's colours, keeps the
        /// manual ones, and recomputes every amount. The sheet quantity a row
        /// carried is preserved for a colour that still exists.
        /// </summary>
        public void RebuildWastages(IReadOnlyList<SpecGroup>? groups)
        {
            var colours = new List<string>();
            var areas = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (SpecGroup group in groups ?? Array.Empty<SpecGroup>())
            {
                foreach (string colour in new[]
                         { group.OuterGlassColor, group.InnerGlassColor, group.ThirdGlassColor })
                {
                    string text = (colour ?? "").Trim();
                    if (text.Length == 0) continue;
                    if (!areas.ContainsKey(text)) areas[text] = 0m;
                    if (!colours.Contains(text, StringComparer.OrdinalIgnoreCase)) colours.Add(text);
                }
            }

            foreach (string colour in colours)
            {
                decimal area = 0m;
                foreach (SpecGroup group in groups ?? Array.Empty<SpecGroup>())
                    area += group.ColorSqm(colour);
                areas[colour] = MoneyRounding.RoundArea(area);
            }

            var rebuilt = new List<WastageChargeRow>();
            foreach (string colour in colours)
            {
                WastageChargeRow existing = Wastages.FirstOrDefault(row =>
                    !row.IsManuallyAdded &&
                    string.Equals(row.ColorName.Trim(), colour, StringComparison.OrdinalIgnoreCase));

                rebuilt.Add(new WastageChargeRow
                {
                    IsActive = existing?.IsActive ?? false,
                    ColorName = colour,
                    Rate = existing?.Rate ?? 0m,
                    IsOptionalSuggestion = string.Equals(colour, "Clear", StringComparison.OrdinalIgnoreCase),
                    SuggestionReason = string.Equals(colour, "Clear", StringComparison.OrdinalIgnoreCase)
                        ? "Clear glass — optional because offcuts may be reusable."
                        : "Coloured or coated glass — offcuts are less likely to be reusable.",
                    IsManuallyAdded = false,
                    UseOptimizationFormula = existing?.UseOptimizationFormula ?? false,
                    FormulaPricePerSqm = existing?.FormulaPricePerSqm ?? 0m,
                    OptimizationPercent = existing?.OptimizationPercent ?? 0m,
                    SheetQuantity = existing?.SheetQuantity ?? 1,
                    AreaSqm = areas[colour]
                });
            }

            // Manual rows stay exactly as the operator left them.
            rebuilt.AddRange(Wastages.Where(row => row.IsManuallyAdded));
            Wastages = rebuilt;
        }

        /// <summary>Recomputes every amount against the current grid.</summary>
        public void Recalculate(IReadOnlyList<SpecGroup>? groups)
        {
            SpecGridTotals totals = SpecGridTotals.From(groups);
            foreach (PredefinedChargeRow row in Predefined) row.Recalculate(totals);
            foreach (WastageChargeRow row in Wastages)
            {
                if (groups != null && !row.IsManuallyAdded)
                {
                    decimal area = 0m;
                    foreach (SpecGroup group in groups) area += group.ColorSqm(row.ColorName);
                    row.AreaSqm = MoneyRounding.RoundArea(area);
                }
                row.Recalculate();
            }
        }

        public OtherChargesSheet Clone() => new()
        {
            UseAdvancedAutomation = UseAdvancedAutomation,
            Predefined = Predefined.Select(row => new PredefinedChargeRow
            {
                TemplateKey = row.TemplateKey,
                ChargeName = row.ChargeName,
                Description = row.Description,
                Unit = row.Unit,
                Metric = row.Metric,
                MetricMultiplier = row.MetricMultiplier,
                IsActive = row.IsActive,
                Rate = row.Rate,
                FixedQuantity = row.FixedQuantity,
                Quantity = row.Quantity,
                Amount = row.Amount,
                HasManualActiveOverride = row.HasManualActiveOverride
            }).ToList(),
            Wastages = Wastages.Select(row => new WastageChargeRow
            {
                IsActive = row.IsActive,
                ColorName = row.ColorName,
                Rate = row.Rate,
                IsOptionalSuggestion = row.IsOptionalSuggestion,
                SuggestionReason = row.SuggestionReason,
                AreaSqm = row.AreaSqm,
                Amount = row.Amount,
                IsManuallyAdded = row.IsManuallyAdded,
                UseOptimizationFormula = row.UseOptimizationFormula,
                FormulaPricePerSqm = row.FormulaPricePerSqm,
                OptimizationPercent = row.OptimizationPercent,
                SheetQuantity = row.SheetQuantity
            }).ToList(),
            Custom = Custom.Select(row => new CustomChargeRow
            {
                IsActive = row.IsActive,
                ChargeName = row.ChargeName,
                Qty = row.Qty,
                Unit = row.Unit,
                Rate = row.Rate
            }).ToList()
        };
    }
}
