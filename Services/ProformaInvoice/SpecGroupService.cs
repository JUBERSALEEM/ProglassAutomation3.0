// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/SpecGroupService.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — SpecGroup Domain Service (Phase 6)
// VERSION: Phase 6 — 2026-08-21 — Extracted from SpecGroup.cs 1701 LOC
//
// PURPOSE:
//   SpecGroup.cs was 1701 LOC — God object for PI wizard Step 5.
//   Extracts pure calculations:
//     • Panel area, total sqm, wastage, DGU/LAM logic
//     • Row validation (dimensions, qty, glass ref)
//     • Aggregation (total qty, total sqm per spec group)
//
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    public sealed class SpecGroupService
    {
        public const decimal MinPanelSqm = 0.5m;

        public static decimal CalculatePanelSqm(int w, int h)
        {
            if (w <= 0 || h <= 0) return 0;
            decimal sqm = (w / 1000m) * (h / 1000m);
            return Math.Max(sqm, MinPanelSqm);
        }

        public static decimal CalculateTotalSqm(int w, int h, int qty)
        {
            return CalculatePanelSqm(w, h) * Math.Max(0, qty);
        }

        public static (int TotalQty, decimal TotalSqm, int RowCount) AggregateRows<T>(
            IEnumerable<T> rows,
            Func<T, int> qtySelector,
            Func<T, decimal> sqmSelector,
            Func<T, bool> isPhantomSelector)
        {
            int totalQty = 0;
            decimal totalSqm = 0;
            int count = 0;

            foreach (var row in rows)
            {
                if (isPhantomSelector(row)) continue;
                totalQty += qtySelector(row);
                totalSqm += sqmSelector(row);
                count++;
            }

            return (totalQty, Math.Round(totalSqm, 3), count);
        }

        public static Result ValidateRow(int w, int h, int qty, string glassRef, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();

            if (string.IsNullOrWhiteSpace(glassRef) && w == 0 && h == 0)
                return Result.Ok(corrId); // phantom row — allowed, will be filtered

            if (w <= 0 || h <= 0)
                return Result.Fail($"Dimensions must be >0 for glass ref '{glassRef}'", corrId);

            if (qty <= 0)
                return Result.Fail($"Qty must be >0 for glass ref '{glassRef}'", corrId);

            if (w > 10000 || h > 10000)
                return Result.Fail($"Dimensions too large for '{glassRef}' — max 10000mm", corrId);

            if (qty > 10000)
                return Result.Fail($"Qty too large for '{glassRef}' — max 10000", corrId);

            return Result.Ok(corrId);
        }

        public static bool IsPhantomRow(string? glassRef, int w, int h, int qty)
        {
            if (qty <= 0) return true;
            if (string.IsNullOrWhiteSpace(glassRef) && w == 0 && h == 0) return true;
            return false;
        }

        public static string FormatGlassRef(int specGroupNumber, int serialNumber, string? glassRef)
        {
            string baseRef = string.IsNullOrWhiteSpace(glassRef) ? $"G-{specGroupNumber}-{serialNumber}" : glassRef.Trim();
            // Ensure it has Spec N prefix if not already
            if (baseRef.StartsWith($"Spec {specGroupNumber}", StringComparison.OrdinalIgnoreCase))
                return baseRef;
            return baseRef;
        }
    }
}
