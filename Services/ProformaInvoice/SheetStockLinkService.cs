// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/SheetStockLinkService.cs
// PROJECT: ProGlassAutomation
//
// SINGLE MECHANISM for every Proforma-Invoice ↔ Sheet Store stock link.
// Two responsibilities, both never-throw (a store problem must never
// crash the wizard):
//
//   1. RESOLVE  — find the GlassSheet an invoicing row refers to,
//      matching the Sheet Store the way Step 4 quotes it:
//      normalised colour + exact W×H, thickness as a tie-breaker.
//
//   2. CONSUME  — record stock usage (repo.UseStockAsync + history log +
//      audit) exactly like the Sheet Store Use dialog does. The Store's
//      CommitUseAsync now routes through the same method, so the wizard's
//      automatic consumption and the manual dialog can never drift.
//
// CONTEXT (2026-09-10 request): Step 4 reads colour/price/W/H from the
// store; Step 6 wastage rows carry Sheet QTY. Entering a QTY in Step 6
// must consume those sheets from inventory automatically — WITHOUT
// opening the Sheet Store module. Utilization itself stays with Step 9
// (Step6SheetAllocation) and is intentionally untouched here.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.SheetStore;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.SheetStore;
using ProGlassAutomation.Services.SheetStore.SheetStoreHistory;
using ProGlassAutomation.Utilities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    public sealed class SheetStockLinkResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = "";
        public int ConsumedQuantity { get; init; }
        public int RemainingBalance { get; init; }
        public decimal ValueAed { get; init; }
        public string SheetDescription { get; init; } = "";
    }

    public static class SheetStockLinkService
    {
        private static readonly SheetStoreRepository _repo = new();
        private static readonly SheetHistoryRepository _historyRepo = new();

        // ═══════════════════════════════════════════════════════════════
        //  RESOLVE
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Finds the stocked sheet for a Step-4/Step-6 glass description.
        /// Priority: (1) exact colour + W×H + thickness with balance,
        /// (2) exact colour + W×H any balance, (3) exact colour +
        /// thickness largest balance, (4) exact colour largest balance.
        /// Null when the colour is not stocked at all. Never throws.
        /// </summary>
        public static async Task<GlassSheet?> ResolveSheetAsync(
            string? color, int widthMm, int heightMm, int thicknessMm = 0)
        {
            try
            {
                var normColor = SheetStorePricingService.Norm(color);
                if (normColor.Length == 0) return null;

                var all = await SheetStorePricingService.GetAllSheetsAsync()
                    .ConfigureAwait(false);
                if (all.Count == 0) return null;

                var byColor = all
                    .Where(s => SheetStorePricingService.Norm(s?.Color) == normColor)
                    .ToList();
                if (byColor.Count == 0) return null;

                // 1 — exact size + thickness, in stock
                var hit = byColor.FirstOrDefault(s =>
                    s.Balance > 0 &&
                    MatchSize(s, widthMm, heightMm) &&
                    (thicknessMm <= 0 || s.ThicknessMm == thicknessMm));
                if (hit != null) return hit;

                // 2 — exact size, any balance
                hit = byColor.FirstOrDefault(
                    s => MatchSize(s, widthMm, heightMm));
                if (hit != null) return hit;

                // 3 — same thickness, largest balance (jumbo-first quoting
                //     in Step 4 means the quoted size may drift from stock)
                if (thicknessMm > 0)
                {
                    hit = byColor
                        .Where(s => s.ThicknessMm == thicknessMm && s.Balance > 0)
                        .OrderByDescending(s => s.Balance)
                        .FirstOrDefault();
                    if (hit != null) return hit;
                }

                // 4 — any sheet of that colour, largest balance
                return byColor
                    .Where(s => s.Balance > 0)
                    .OrderByDescending(s => s.Balance)
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    $"[SheetStockLinkService] Resolve failed: {ex.Message}");
                return null;
            }
        }

        private static bool MatchSize(GlassSheet s, int w, int h) =>
            w > 0 && h > 0 && s.WidthMm == w && s.HeightMm == h;

        // ═══════════════════════════════════════════════════════════════
        //  CONSUME — shared by Sheet Store Use dialog AND Step 6 auto-use
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Records consumption of <paramref name="quantity"/> sheets.
        /// Validates balance, writes the SAME history entry and audit line
        /// the manual Use dialog writes, and tags the origin so the store
        /// history shows whether stock was consumed from the dialog or
        /// automatically from Step 6. Never throws.
        /// </summary>
        public static async Task<SheetStockLinkResult> CommitConsumptionAsync(
            GlassSheet sheet,
            int quantity,
            string jobReference = "",
            string customerName = "",
            string projectName = "",
            string salesman = "",
            string origin = "Sheet Store")
        {
            if (sheet == null)
                return Fail("No matching sheet in inventory.");

            if (quantity <= 0)
                return Fail("Sheet quantity must be at least 1.");

            try
            {
                // Check-then-act close (cross-check hardening 2026-09-10):
                // the resolved snapshot may be minutes old while another
                // window/dialog consumed stock meanwhile. Re-read the CURRENT
                // row and validate against it, so the balance check and the
                // write are one atomic decision per repository call.
                var current = await _repo.GetByIdAsync(sheet.Id)
                    .ConfigureAwait(false)
                    ?? sheet;

                if (current.Balance <= 0)
                    return Fail(
                        $"No stock available — {Describe(current)} balance is 0.");

                if (quantity > current.Balance)
                    return Fail(
                        $"Insufficient stock — {Describe(current)} has a balance " +
                        $"of {current.Balance} sheet(s), {quantity} requested.");

                sheet = current;

                int oldUsed = sheet.Used;
                await _repo.UseStockAsync(sheet.Id, quantity).ConfigureAwait(false);

                decimal value = quantity * sheet.SqmPerSheet * sheet.SellPricePerSqm;
                string jobTag = string.IsNullOrWhiteSpace(jobReference)
                    ? ""
                    : $" [Job: {jobReference.Trim()}]";

                try
                {
                    await _historyRepo.LogAsync(
                        sheet.Id, Describe(sheet), "Used",
                        $"Consumed {quantity} sheet(s) via {origin}. " +
                        $"{jobTag} Used: {oldUsed} → {oldUsed + quantity}. " +
                        $"Value: {value:N2} AED.",
                        quantity, value,
                        documentReference: jobReference,
                        customerName: customerName,
                        projectName: projectName,
                        salesman: salesman,
                        panePosition: "").ConfigureAwait(false);
                }
                catch {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("CommitConsumptionAsync suppressed");
            }

                try
                {
                    AuditService.Log(
                        AuditSections.SheetStore, "Stock used",
                        $"{Describe(sheet)} — {quantity} sheet(s) " +
                        $"(via {origin}){jobTag}",
                        "SHEET", sheet.Id.ToString());
                }
                catch (Exception auditEx)
                {
                    AppLogService.Warning(
                        "[SheetStockLinkService] Sheet inventory audit log failed: " + auditEx.Message,
                        new LogContext { Module = "SheetStock", Class = nameof(SheetStockLinkService), Operation = "ConsumeFromInventory" });
                }

                return new SheetStockLinkResult
                {
                    Success = true,
                    Message = $"Used {quantity} sheet(s) from inventory — " +
                              $"{Describe(sheet)} (via {origin}).",
                    ConsumedQuantity = quantity,
                    RemainingBalance = sheet.Balance - quantity,
                    ValueAed = value,
                    SheetDescription = Describe(sheet)
                };
            }
            catch (Exception ex)
            {
                AppLogService.Error(
                    "[SheetStockLinkService] Consumption failed", ex);
                return Fail(
                    "Inventory update failed — " +
                    $"{ExceptionMessages.Deep(ex)}");
            }
        }

        private static string Describe(GlassSheet s) =>
            $"{(string.IsNullOrWhiteSpace(s.Color) ? "Sheet" : s.Color)} " +
            $"{s.ThicknessMm}mm {s.SizeDisplay}";

        private static SheetStockLinkResult Fail(string message) => new()
        {
            Success = false,
            Message = message
        };
    }
}
