// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/ProformaInvoice/Step6OtherCharges/MetricOption.cs
// ═══════════════════════════════════════════════════════════════════════
// CHAT 17 — NEW FILE
//
//   One selectable metric type (LM1, LM2, LM_COMBINED, SQM1, etc.).
//   Used in the "SELECT METRIC" section of the two-part spec-target
//   selector popup on each OtherChargeRow.
//
//   Static registry: MetricOption.All contains all 10 metric types.
//   Filtered by Unit at row-level (LM row shows only LM metrics, etc.)
// ═══════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges
{
    /// <summary>
    /// A single metric option shown in the "SELECT METRIC" radio list.
    /// </summary>
    public class MetricOption
    {
        /// <summary>Internal key: LM1, LM2, LM_COMBINED, SQM1, SQM2, SQM_COMBINED, DGU_CAVITY, QTY, DOUBLE_GLASS.</summary>
        public string Key { get; set; } = "";

        /// <summary>Display name shown in the radio option (e.g. "LM1 (Glass 1 perimeter)").</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>Short label shown in toggle button summary (e.g. "LM1").</summary>
        public string ShortLabel { get; set; } = "";

        /// <summary>Which unit type this metric produces.</summary>
        public OtherChargeUnit Unit { get; set; }

        public override string ToString() => DisplayName;

        /// <summary>Full static registry of all metric options.</summary>
        public static List<MetricOption> All { get; } = new()
        {
            new MetricOption { Key = "LM1",          ShortLabel = "LM1",       DisplayName = "LM1 (Glass 1 perimeter)",   Unit = OtherChargeUnit.Lm },
            new MetricOption { Key = "LM2",          ShortLabel = "LM2",       DisplayName = "LM2 (Glass 2 perimeter)",   Unit = OtherChargeUnit.Lm },
            new MetricOption { Key = "LM_COMBINED",  ShortLabel = "LM1+LM2",   DisplayName = "Both LM (LM1 + LM2)",       Unit = OtherChargeUnit.Lm },

            new MetricOption { Key = "SQM1",         ShortLabel = "SQM1",      DisplayName = "SQM1 (Glass 1 area)",       Unit = OtherChargeUnit.Sqm },
            new MetricOption { Key = "SQM2",         ShortLabel = "SQM2",      DisplayName = "SQM2 (Glass 2 area)",       Unit = OtherChargeUnit.Sqm },
            new MetricOption { Key = "SQM_COMBINED", ShortLabel = "SQM1+SQM2", DisplayName = "Both SQM (SQM1 + SQM2)",    Unit = OtherChargeUnit.Sqm },
            new MetricOption { Key = "DGU_CAVITY",   ShortLabel = "Cavity",    DisplayName = "DGU Cavity Area (SQM1)",    Unit = OtherChargeUnit.Sqm },

            new MetricOption { Key = "QTY",          ShortLabel = "Qty",       DisplayName = "Piece count",               Unit = OtherChargeUnit.Qty },
            new MetricOption { Key = "DOUBLE_GLASS", ShortLabel = "Panes",     DisplayName = "Physical panes (Qty×2 DGU)",Unit = OtherChargeUnit.Qty }
        };

        /// <summary>Returns only the metrics that match the given unit type.</summary>
        public static List<MetricOption> ForUnit(OtherChargeUnit unit)
            => All.Where(m => m.Unit == unit).ToList();

        /// <summary>
        /// Resolves a saved or template key to a metric for the given unit.
        /// Accepts legacy aliases such as TOTAL_PIECES → QTY.
        /// </summary>
        public static MetricOption? FindForUnit(OtherChargeUnit unit, string? key)
        {
            string normalized = NormalizeKey(key);
            if (string.IsNullOrEmpty(normalized))
                return ForUnit(unit).FirstOrDefault();

            return ForUnit(unit).FirstOrDefault(m => m.Key == normalized)
                ?? ForUnit(unit).FirstOrDefault();
        }

        public static string NormalizeKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "";

            return key.Trim() switch
            {
                "TOTAL_PIECES" => "QTY",
                "TOTAL_QTY" => "QTY",
                _ => key.Trim()
            };
        }
    }
}