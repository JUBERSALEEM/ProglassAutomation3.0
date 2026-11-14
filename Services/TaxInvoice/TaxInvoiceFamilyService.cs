// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/TaxInvoice/TaxInvoiceFamilyService.cs
// PROJECT: ProGlassAutomation
// MODULE: Tax Invoice — Family Service (Phase 6b)
// VERSION: Phase 6b — 2026-08-21 — Extracted from TaxInvoiceDetailViewModel

using ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice;
using ProGlassAutomation.Models.TaxInvoice.TaxInvoiceFamily;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.TaxInvoice.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.TaxInvoice
{
    public sealed class TaxInvoiceFamilyService
    {
        public static async Task<Result<List<TaxInvoiceListItem>>> GetFamilyAsync(string proformaId, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewIdFor("TaxInvoice", proformaId);
            using var scope = CorrelationContext.BeginDocumentScope("TaxInvoice", proformaId, corrId);

            try
            {
                if (App.TaxInvoices == null)
                    return Result<List<TaxInvoiceListItem>>.Fail("TaxInvoices repo not initialized", corrId);

                var family = await App.TaxInvoices.GetByProformaIdAsync(proformaId).ConfigureAwait(false);
                return Result<List<TaxInvoiceListItem>>.Ok(family, corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"Get TI family failed for PI {proformaId}: {ex.Message}", ex,
                    new LogContext { Module = "TaxInvoice", Operation = "GetFamily", DocumentId = proformaId, DocumentType = "ProformaInvoice", CorrelationId = corrId });
                return Result<List<TaxInvoiceListItem>>.Fail(ex.Message, ex, corrId, errId);
            }
        }

        public static async Task<Result<InvoiceFamilySummary>> GetFamilySummaryAsync(string proformaId, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewIdFor("TaxInvoice", proformaId);
            using var scope = CorrelationContext.BeginDocumentScope("TaxInvoice", proformaId, corrId);

            try
            {
                if (App.TaxInvoices == null)
                    return Result<InvoiceFamilySummary>.Fail("TaxInvoices repo not initialized", corrId);

                var summary = await App.TaxInvoices.GetFamilySummaryAsync(proformaId).ConfigureAwait(false);
                return Result<InvoiceFamilySummary>.Ok(summary, corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"Get TI family summary failed for PI {proformaId}: {ex.Message}", ex,
                    new LogContext { Module = "TaxInvoice", Operation = "GetFamilySummary", DocumentId = proformaId, DocumentType = "ProformaInvoice", CorrelationId = corrId });
                return Result<InvoiceFamilySummary>.Fail(ex.Message, ex, corrId, errId);
            }
        }

        public static Result<TaxInvoiceFamily> BuildFamilyFromItems(List<TaxInvoiceListItem> items, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();
            try
            {
                var families = TaxInvoiceFamily.BuildFrom(items).ToList();
                var family = families.FirstOrDefault();
                if (family == null)
                    return Result<TaxInvoiceFamily>.Fail("No family built from items", corrId);

                return Result<TaxInvoiceFamily>.Ok(family, corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"Build TI family failed: {ex.Message}", ex,
                    new LogContext { Module = "TaxInvoice", Operation = "BuildFamily", CorrelationId = corrId });
                return Result<TaxInvoiceFamily>.Fail(ex.Message, ex, corrId, errId);
            }
        }

        public static string BuildInvoiceNumber(string proformaId, string sequenceLetter = "")
        {
            string core = proformaId.StartsWith("PI-", StringComparison.OrdinalIgnoreCase) ? proformaId.Substring(3) : proformaId;
            string baseNumber = $"INV-{core}";
            return string.IsNullOrWhiteSpace(sequenceLetter) ? baseNumber : $"{baseNumber}-{sequenceLetter.ToUpperInvariant().Trim()}";
        }
    }
}
