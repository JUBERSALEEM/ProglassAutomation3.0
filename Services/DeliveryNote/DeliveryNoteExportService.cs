// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/DeliveryNote/DeliveryNoteExportService.cs
// PROJECT: ProGlassAutomation
// MODULE: Delivery Note — Export Service (Phase 6b: God VM Split)
// VERSION: Phase 6b — 2026-08-21 — Extracted from DeliveryNoteDetailViewModel
//
// PURPOSE:
//   DeliveryNoteDetailViewModel had 3 export methods (PDF, Excel, CSV) plus
//   GuardedExportAsync wrapper — all duplicated across DN, JO, TI, PI.
//   This service centralizes export with Result<T> + CorrelationId.
//
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.DeliveryNote.Export;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Csv;
using ProGlassAutomation.Services.Export.Excel;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.Services.Logging;
using System;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.DeliveryNote
{
    public sealed class DeliveryNoteExportService
    {
        public static async Task<Result<ExportResult>> ExportPdfAsync(
            SavedDeliveryNote entity,
            DeliveryNoteSnapshot snapshot,
            string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewIdFor("DeliveryNote", entity?.DeliveryNoteId);
            using var scope = CorrelationContext.BeginDocumentScope("DeliveryNote", entity?.DeliveryNoteId ?? "", corrId);

            try
            {
                if (entity == null)
                    return Result<ExportResult>.Fail("No delivery note loaded", corrId);

                var layout = new DeliveryNotePdfLayout(entity, snapshot);
                var engine = new PdfExportEngine();
                var request = new ExportRequest { PromptForLocation = true, OpenAfterSave = true };
                var result = await engine.ExportAsync(layout, request).ConfigureAwait(false);

                if (result.Success)
                {
                    AppLogService.Info($"DN {entity.DeliveryNoteId} PDF exported to {result.SavedPath}",
                        new LogContext { Module = "Delivery", Operation = "ExportPdf", DocumentId = entity.DeliveryNoteId, DocumentType = "DeliveryNote", CorrelationId = corrId });
                }

                return Result<ExportResult>.Ok(result, corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"DN {entity?.DeliveryNoteId} PDF export failed: {ex.Message}", ex,
                    new LogContext { Module = "Delivery", Operation = "ExportPdf", DocumentId = entity?.DeliveryNoteId ?? "", DocumentType = "DeliveryNote", CorrelationId = corrId });
                return Result<ExportResult>.Fail($"PDF export failed: {ex.Message}", ex, corrId, errId);
            }
        }

        public static async Task<Result<ExportResult>> ExportExcelAsync(
            SavedDeliveryNote entity,
            DeliveryNoteSnapshot snapshot,
            string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewIdFor("DeliveryNote", entity?.DeliveryNoteId);
            using var scope = CorrelationContext.BeginDocumentScope("DeliveryNote", entity?.DeliveryNoteId ?? "", corrId);

            try
            {
                if (entity == null)
                    return Result<ExportResult>.Fail("No delivery note loaded", corrId);

                var layout = new DeliveryNoteExcelLayout(entity, snapshot);
                var engine = new ExcelExportEngine();
                var request = new ExportRequest { PromptForLocation = true, OpenAfterSave = true };
                var result = await engine.ExportAsync(layout, request).ConfigureAwait(false);

                if (result.Success)
                {
                    AppLogService.Info($"DN {entity.DeliveryNoteId} Excel exported to {result.SavedPath}",
                        new LogContext { Module = "Delivery", Operation = "ExportExcel", DocumentId = entity.DeliveryNoteId, DocumentType = "DeliveryNote", CorrelationId = corrId });
                }

                return Result<ExportResult>.Ok(result, corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"DN {entity?.DeliveryNoteId} Excel export failed: {ex.Message}", ex,
                    new LogContext { Module = "Delivery", Operation = "ExportExcel", DocumentId = entity?.DeliveryNoteId ?? "", DocumentType = "DeliveryNote", CorrelationId = corrId });
                return Result<ExportResult>.Fail($"Excel export failed: {ex.Message}", ex, corrId, errId);
            }
        }

        public static async Task<Result<ExportResult>> ExportCsvAsync(
            SavedDeliveryNote entity,
            DeliveryNoteSnapshot snapshot,
            string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewIdFor("DeliveryNote", entity?.DeliveryNoteId);
            using var scope = CorrelationContext.BeginDocumentScope("DeliveryNote", entity?.DeliveryNoteId ?? "", corrId);

            try
            {
                if (entity == null)
                    return Result<ExportResult>.Fail("No delivery note loaded", corrId);

                var layout = new DeliveryNoteCsvLayout(entity, snapshot);
                var engine = new CsvExportEngine();
                var request = new ExportRequest { PromptForLocation = true, OpenAfterSave = true };
                var result = await engine.ExportAsync(layout, request).ConfigureAwait(false);

                return Result<ExportResult>.Ok(result, corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"DN {entity?.DeliveryNoteId} CSV export failed: {ex.Message}", ex,
                    new LogContext { Module = "Delivery", Operation = "ExportCsv", DocumentId = entity?.DeliveryNoteId ?? "", DocumentType = "DeliveryNote", CorrelationId = corrId });
                return Result<ExportResult>.Fail($"CSV export failed: {ex.Message}", ex, corrId, errId);
            }
        }

        public static async Task<Result> GuardedExportAsync(Func<Task> export, string kind, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();
            try
            {
                await export().ConfigureAwait(false);
                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"{kind} export failed: {ex.Message}", ex,
                    new LogContext { Module = "Delivery", Operation = $"Export{kind}", CorrelationId = corrId });
                return Result.Fail($"{kind} export failed: {ex.Message}", ex, corrId, errId);
            }
        }
    }
}
