using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ProGlassAutomation.Services.ProformaInvoice.Export
{
    /// <summary>
    /// Shared metric aggregation for Proforma Invoice export layouts.
    ///
    /// BUG-21 [CRITICAL] FIX: Previously duplicated across
    /// ProformaInvoicePdfLayout and ProformaInvoiceExcelLayout.
    /// Any future format (Word, HTML) shares the same math.
    ///
    /// BUG-22 FIX: Uses SqmCalculator.CalculatePanel() for panel area —
    /// single source of truth for the 0.5 m² minimum floor rule.
    ///
    /// BUG-25 FIX: Unknown source keys logged via Debug.WriteLine
    /// instead of silently returning 0.
    ///
    /// Mirrors:
    ///   OtherChargesViewModel.CalculatePolishLmForRow()
    ///   MetricDiscoveryHubViewModel.Refresh()
    /// </summary>
    public static class ProformaChargeMetricsCalculator
    {
        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Aggregates all metric totals from a snapshot's spec groups.
        /// Returns zero-filled metrics if input is null or empty.
        /// </summary>
        public static SpecMetrics ComputeSpecMetrics(
            List<SpecGroupSnapshot>? specGroups)
        {
            var m = new SpecMetrics();
            if (specGroups == null) return m;

            foreach (var g in specGroups)
            {
                if (g.ItemRows == null) continue;

                bool isDoubleGlass =
                    g.SelectedGlassType == "DGU"
                    || g.SelectedGlassType == "LAM"
                    || g.SelectedGlassType == "DGU+LAM";

                int factor = g.SelectedGlassType == "LAM" ? 4 : 2;
                int paneFactor = g.SelectedGlassType == "DGU+LAM"
                    ? 3
                    : isDoubleGlass ? 2 : 1;

                foreach (var r in g.ItemRows)
                {
                    if (r.Qty <= 0) continue;

                    // Glass 1 metrics (SQM1 uses shared SqmCalculator)
                    if (r.W1 > 0 && r.H1 > 0)
                    {
                        decimal sqm1 = SqmCalculator.CalculatePanel(r.W1, r.H1);
                        m.TotalSqm1 += Math.Round(sqm1 * r.Qty, 3);

                        m.TotalLm1 += Math.Round(
                            ((decimal)r.W1 + r.H1) / 1000m * factor * r.Qty, 3);
                    }

                    // Glass 2 metrics (SQM2 uses shared SqmCalculator)
                    if (r.W2 > 0 && r.H2 > 0)
                    {
                        decimal sqm2 = SqmCalculator.CalculatePanel(r.W2, r.H2);
                        m.TotalSqm2 += Math.Round(sqm2 * r.Qty, 3);

                        m.TotalLm2 += Math.Round(
                            ((decimal)r.W2 + r.H2) / 1000m * factor * r.Qty, 3);
                    }

                    m.TotalPieces += r.Qty;

                    if (isDoubleGlass)
                    {
                        m.DoubleGlassPieces += r.Qty * paneFactor;
                        if (r.W1 > 0 && r.H1 > 0)
                        {
                            decimal sqm1 = SqmCalculator.CalculatePanel(r.W1, r.H1);
                            m.DguCavityArea += Math.Round(sqm1 * r.Qty, 3);
                        }
                    }
                    else
                    {
                        m.DoubleGlassPieces += r.Qty;
                    }
                }
            }

            m.CombinedLm = Math.Round(m.TotalLm1 + m.TotalLm2, 3);
            m.CombinedSqm = Math.Round(m.TotalSqm1 + m.TotalSqm2, 3);
            return m;
        }

        /// <summary>
        /// Returns the metric value for a given SourceKey (e.g. "SQM1").
        /// BUG-25: Unknown source keys logged for diagnostics.
        /// </summary>
        public static decimal LookupMetric(SpecMetrics m, string? sourceKey)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
                return 0m;

            string key = NormalizeSourceKey(sourceKey);
            return key switch
            {
                "LM1" => m.TotalLm1,
                "LM2" => m.TotalLm2,
                "LM_COMBINED" => m.CombinedLm,
                "SQM1" => m.TotalSqm1,
                "SQM2" => m.TotalSqm2,
                "SQM_COMBINED" => m.CombinedSqm,
                "DGU_CAVITY" => m.DguCavityArea,
                "TOTAL_PIECES" => m.TotalPieces,
                "QTY" => m.TotalPieces,
                "DOUBLE_GLASS" => m.DoubleGlassPieces,
                _ => LogUnknownAndReturnZero(sourceKey)
            };
        }

        /// <summary>
        /// Computes total polish LM across all spec groups for a given
        /// polish sub-type. Uses SqmCalculator-style perimeter math but
        /// polish is length-based (LM), not area-based (SQM).
        /// </summary>
        public static decimal ComputePolishLm(
            List<SpecGroupSnapshot>? specGroups,
            string? polishSubType,
            bool matchedSameGlassOnly = false)
        {
            if (specGroups == null || string.IsNullOrWhiteSpace(polishSubType))
                return 0m;

            decimal totalLm = 0m;

            foreach (var g in specGroups)
            {
                if (g.ItemRows == null) continue;

                bool matched = LaminateGlassMatcher.IsMatchedSameGlass(
                    g.SelectedSpecName, g.SelectedGlassType,
                    g.OuterGlassColor, g.InnerGlassColor);
                if (matchedSameGlassOnly && !matched)
                    continue;

                bool isDoubleGlass =
                    g.SelectedGlassType == "DGU"
                    || g.SelectedGlassType == "LAM"
                    || g.SelectedGlassType == "DGU+LAM";

                foreach (var r in g.ItemRows)
                {
                    if (r.Qty <= 0) continue;

                    decimal lm = 0m;
                    if (r.W1 > 0 && r.H1 > 0)
                        lm += CalcPolishSingle(r.W1, r.H1, polishSubType);

                    if (isDoubleGlass && r.W2 > 0 && r.H2 > 0)
                    {
                        lm += CalcPolishSingle(r.W2, r.H2, polishSubType);
                    }
                    else if (matchedSameGlassOnly && matched
                             && r.W1 > 0 && r.H1 > 0)
                    {
                        // Same-glass LAM often only has W1/H1 filled —
                        // still polish both panes.
                        lm += CalcPolishSingle(r.W1, r.H1, polishSubType);
                    }

                    totalLm += lm * r.Qty;
                }
            }

            return Math.Round(totalLm, 3);
        }

        /// <summary>
        /// Computes wastage area (SQM) from checked source keys.
        /// Key format: "SpecNumber-SideLabel" e.g. "1-Outer (W1/H1)".
        /// Uses SqmCalculator.CalculatePanel() for panel area with
        /// 0.5 m² minimum floor.
        /// </summary>
        public static decimal ComputeWastageQty(
            List<SpecGroupSnapshot>? specGroups, List<string>? checkedKeys)
        {
            if (specGroups == null
                || checkedKeys == null
                || checkedKeys.Count == 0)
                return 0m;

            decimal total = 0m;

            foreach (var key in checkedKeys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;

                var parts = key.Split('-', 2);
                if (parts.Length < 2) continue;
                if (!int.TryParse(parts[0], out int specNumber)) continue;

                var group = specGroups.FirstOrDefault(g => g.GroupNumber == specNumber);
                if (group?.ItemRows == null) continue;

                bool usesOuterDimensions = UsesOuterDimensions(parts[1]);

                foreach (var r in group.ItemRows)
                {
                    if (r.Qty <= 0) continue;
                    int w = usesOuterDimensions ? r.W1 : r.W2;
                    int h = usesOuterDimensions ? r.H1 : r.H2;
                    if (w > 0 && h > 0)
                    {
                        // BUG-22: use shared SqmCalculator
                        decimal sqm = SqmCalculator.CalculatePanel(w, h);
                        total += Math.Round(sqm * r.Qty, 3);
                    }
                }
            }

            return Math.Round(total, 3);
        }

        // ── Private helpers ───────────────────────────────────────────────

        /// <summary>
        /// Resolves the physical dimensions behind a persisted Step 6
        /// wastage side key. Both laminated panes deliberately use W2/H2;
        /// checking for the word "Outer" first incorrectly sent
        /// "Lam Outer (W2/H2)" through W1/H1 in PDF/Excel exports.
        /// </summary>
        private static bool UsesOuterDimensions(string? sideLabel)
        {
            string side = (sideLabel ?? string.Empty).Trim();
            if (side.IndexOf("Lam Inner", StringComparison.OrdinalIgnoreCase) >= 0
                || side.IndexOf("Lam Outer", StringComparison.OrdinalIgnoreCase) >= 0
                || side.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0
                || side.IndexOf("W2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return side.IndexOf("Outer", StringComparison.OrdinalIgnoreCase) >= 0
                || side.IndexOf("W1", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static decimal CalcPolishSingle(int w, int h, string subType)
        {
            return subType switch
            {
                "AllSidePolish" => ((decimal)w + h) / 1000m * 2m,
                "AllSidePolishAsPerDwg" => ((decimal)w + h) / 1000m * 2m,
                "BothHeightSide" => (decimal)h / 1000m * 2m,
                "BothHeightSideAsPerDwg" => (decimal)h / 1000m * 2m,
                "BothWidthSide" => (decimal)w / 1000m * 2m,
                "BothWidthSideAsPerDwg" => (decimal)w / 1000m * 2m,
                "BothHeightOneWidth" => ((decimal)h * 2m + w) / 1000m,
                "BothHeightOneWidthAsPerDwg" => ((decimal)h * 2m + w) / 1000m,
                "OneHeightOneWidth" => ((decimal)h + w) / 1000m,
                "OneHeightOneWidthAsPerDwg" => ((decimal)h + w) / 1000m,
                "OneHeightSide" => (decimal)h / 1000m,
                "OneWidthSide" => (decimal)w / 1000m,
                _ => 0m
            };
        }

        public static string FirstChargeSourceKey(params string?[] keys)
        {
            if (keys == null) return "";
            foreach (var key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    return key.Trim();
            }
            return "";
        }

        public static List<SpecGroupSnapshot>? FilterSpecs(
            List<SpecGroupSnapshot>? specGroups, List<int>? checkedNumbers)
        {
            if (specGroups == null) return null;
            if (checkedNumbers == null || checkedNumbers.Count == 0)
                return specGroups;

            var allowed = checkedNumbers.ToHashSet();
            return specGroups.Where(g => allowed.Contains(g.GroupNumber)).ToList();
        }

        public static string NormalizeSourceKey(string? sourceKey)
        {
            if (string.IsNullOrWhiteSpace(sourceKey))
                return "";

            return sourceKey.Trim() switch
            {
                "QTY" => "TOTAL_PIECES",
                "TOTAL_QTY" => "TOTAL_PIECES",
                _ => sourceKey.Trim()
            };
        }

        /// <summary>
        /// Metric for a charge row using the live selected metric key and
        /// optional per-spec filter. Falls back to the full invoice when
        /// no spec list was saved (legacy invoices).
        /// </summary>
        public static decimal ComputeTargetedMetric(
            List<SpecGroupSnapshot>? specGroups,
            string? metricKey,
            List<int>? checkedSpecNumbers)
        {
            IEnumerable<SpecGroupSnapshot> groups =
                specGroups ?? Enumerable.Empty<SpecGroupSnapshot>();

            if (checkedSpecNumbers != null && checkedSpecNumbers.Count > 0)
            {
                var allowed = checkedSpecNumbers.ToHashSet();
                groups = groups.Where(g => allowed.Contains(g.GroupNumber));
            }

            return LookupMetric(ComputeSpecMetrics(groups.ToList()), metricKey);
        }

        private static decimal LogUnknownAndReturnZero(string sourceKey)
        {
            AppLogService.Debug(
                $"[ProformaChargeMetricsCalculator] Unknown source key " +
                $"'{sourceKey}' — returning 0. Valid keys: LM1, LM2, " +
                "LM_COMBINED, SQM1, SQM2, SQM_COMBINED, DGU_CAVITY, " +
                "TOTAL_PIECES, DOUBLE_GLASS.");
            return 0m;
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    //  SpecMetrics DTO — aggregated totals shared between PDF/Excel layouts
    // ═════════════════════════════════════════════════════════════════════

    public sealed class SpecMetrics
    {
        public decimal TotalLm1 { get; set; }
        public decimal TotalLm2 { get; set; }
        public decimal CombinedLm { get; set; }
        public decimal TotalSqm1 { get; set; }
        public decimal TotalSqm2 { get; set; }
        public decimal CombinedSqm { get; set; }
        public decimal DguCavityArea { get; set; }
        public int TotalPieces { get; set; }
        public int DoubleGlassPieces { get; set; }
    }
}