// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/DeliveryNote/DeliveryNoteLineService.cs
// PROJECT: ProGlassAutomation
// MODULE: Delivery Note — Line Item Domain Service (Phase 6: God VM Split)
// VERSION: Phase 6 — 2026-08-21 — Extracted from DeliveryNoteDetailViewModel 2184 LOC
//
// PURPOSE (Phase 6: Decompose God ViewModels):
//   DeliveryNoteDetailViewModel was 2184 LOC with 201 members — no unit boundary.
//   This service extracts pure domain logic for line items:
//     • Qty calculations (Ordered, PreviouslyDelivered, Delivered, Balance)
//     • Delivered lookup (spec key + glass key fallback)
//     • Display filtering (Master vs Real DN, remaining only vs full)
//     • Totals recalculation
//     • FillRemaining / ResetQty business rules
//
//   Benefits:
//   • Testable — pure functions, no UI, no DB, no statics
//   • Reusable — same logic used in reporting, GodView, export
//   • Reduces Detail VM by ~400 LOC
//
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.ViewModels.DeliveryNote.DeliveryNoteDetail;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.DeliveryNote
{
    /// <summary>
    /// Domain service for Delivery Note line items — Phase 6 extracted.
    /// </summary>
    public sealed class DeliveryNoteLineService
    {
        // ── Keys for delivered lookup ──────────────────────────────────

        public static string SpecKey(DeliveryNoteItem item) =>
            $"{item.SpecGroupNumber}|{item.SerialNumber}";

        public static string SpecKey(DnItemRowViewModel row) =>
            $"{row.SpecGroupNumber}|{row.SerialNumber}";

        public static string GlassKey(DeliveryNoteItem item) =>
            $"G|{item.GlassRef}|{item.W1}|{item.H1}|{item.W2}|{item.H2}";

        public static string GlassKey(DnItemRowViewModel row) =>
            $"G|{row.GlassRef}|{row.W1}|{row.H1}|{row.W2}|{row.H2}";

        // ── Delivered lookup building ──────────────────────────────────

        public static Dictionary<string, int> BuildDeliveredLookup(IEnumerable<DeliveryNoteItem> items)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (item.DeliveredQty <= 0) continue;
                Accumulate(map, SpecKey(item), item.DeliveredQty);
            }
            return map;
        }

        public static int LookupDelivered(Dictionary<string, int> map, DeliveryNoteItem item)
        {
            if (map.TryGetValue(SpecKey(item), out var bySpec) && bySpec > 0) return bySpec;
            return 0;
        }

        public static int LookupPreviouslyDelivered(Dictionary<string, int> map, DeliveryNoteItem item)
        {
            if (map.TryGetValue($"{item.SpecGroupNumber}|{item.SerialNumber}", out var bySpec) && bySpec > 0) return bySpec;
            return 0;
        }

        private static void Accumulate(Dictionary<string, int> map, string key, int qty)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            map[key] = map.TryGetValue(key, out var n) ? n + qty : qty;
        }

        // ── Display filtering ──────────────────────────────────────────

        public static bool ShouldDisplayRow(DnItemRowViewModel row, bool isMasterDn, bool showFullMaster)
        {
            if (showFullMaster) return true;
            if (isMasterDn) return row.BalanceQty > 0;
            return row.DeliveredQty > 0 || row.BalanceQty > 0;
        }

        public static (List<DnItemRowViewModel> Visible, int HiddenCount) FilterVisibleRows(
            IEnumerable<DnItemRowViewModel> allRows,
            bool isMasterDn,
            bool showFullMaster)
        {
            var visible = allRows.Where(r => ShouldDisplayRow(r, isMasterDn, showFullMaster)).ToList();
            var hidden = allRows.Count() - visible.Count;
            return (visible, hidden);
        }

        // ── Totals ─────────────────────────────────────────────────────

        public static (int Ordered, int PrevDelivered, int Delivered, int Balance) CalculateTotals(
            IEnumerable<DnItemRowViewModel> rows)
        {
            int ordered = rows.Sum(r => r.OrderedQty);
            int prev = rows.Sum(r => r.PreviouslyDeliveredQty);
            int delivered = rows.Sum(r => r.DeliveredQty);
            int balance = rows.Sum(r => r.BalanceQty);
            return (ordered, prev, delivered, balance);
        }

        public static (int Ordered, int PrevDelivered, int Delivered, int Balance) CalculateTotals(
            IEnumerable<Models.DeliveryNote.SavedDeliveryNote.DeliveryNoteItem> items)
        {
            int ordered = items.Sum(i => i.OrderedQty);
            int prev = items.Sum(i => i.PreviouslyDeliveredQty);
            int delivered = items.Sum(i => i.DeliveredQty);
            int balance = items.Sum(i => i.ComputedBalanceQty);
            return (ordered, prev, delivered, balance);
        }

        public static double CalculateProgressPercent(int ordered, int prevDelivered, int delivered)
        {
            if (ordered == 0) return 0;
            int totalDelivered = prevDelivered + delivered;
            return Math.Min(100.0, (double)totalDelivered / ordered * 100.0);
        }

        // ── Business rules ─────────────────────────────────────────────

        public static void FillAllRemaining(IEnumerable<DnItemRowViewModel> rows)
        {
            foreach (var row in rows)
            {
                if (row.BalanceQty > 0)
                    row.DeliveredQty = row.BalanceQty;
            }
        }

        public static void ResetAllQty(IEnumerable<DnItemRowViewModel> rows)
        {
            foreach (var row in rows)
                row.DeliveredQty = 0;
        }

        public static bool HasAnyDelivery(IEnumerable<DnItemRowViewModel> rows) =>
            rows.Any(r => r.DeliveredQty > 0);

        public static bool HasRemainingBalance(IEnumerable<DnItemRowViewModel> rows) =>
            rows.Any(r => r.BalanceQty > 0);
    }
}
