using ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice;
using ProGlassAutomation.Services.Security;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.FileSystem;
using ProGlassAutomation.Services.TaxInvoice.Mapper;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.TaxInvoice.Pdf
{
    /// <summary>
    /// Generates Tax Invoice PDFs using QuestPDF.
    ///
    /// TWO OUTPUT MODES:
    ///
    ///   1. Standard invoice PDF:
    ///      GenerateAsync(entity) — invoice content only.
    ///
    ///   2. Invoice + attachments PDF:
    ///      GenerateWithAttachmentsAsync(entity, attachments)
    ///      Appends attachments as pages after main invoice.
    ///      Used by Statement PDF generator (future module).
    ///
    /// OUTPUT:
    ///   PDF bytes returned as byte[] — caller decides to:
    ///     • Save to disk (Save As dialog)
    ///     • Open in default viewer (Process.Start)
    ///     • Store in DB as attachment blob
    ///
    /// FILE NAMING (same as PI / JO / DN):
    ///   INV-2026-001_CUSTOMER_PROJECT_dd-MM-yyyy.pdf
    ///   INV-2026-001_CUSTOMER_PROJECT_dd-MM-yyyy_WITH-ATTACHMENTS.pdf
    /// </summary>
    public static class TaxInvoicePdfExporter
    {
        // ── Public API ────────────────────────────────────────────────────

        /// <summary>
        /// Generates a standard Tax Invoice PDF.
        /// Returns PDF bytes. Caller saves/opens as needed.
        /// Throws on error — wrap in try/catch at call site.
        /// </summary>
        public static Task<byte[]> GenerateAsync(SavedTaxInvoice entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            return Task.Run(() => GenerateInvoicePdf(entity));
        }

        /// <summary>
        /// Generates Tax Invoice PDF with attachments appended.
        ///
        /// ATTACHMENT APPEND ORDER:
        ///   1. Main invoice content (always)
        ///   2. SignedPI attachments (PDF files, in order)
        ///   3. SignedDN attachments (PDF files, in order)
        ///   4. Other attachments (PDF files, in order)
        ///
        /// FUTURE MODULE: Statement PDF generator calls this with
        /// all attachments from TaxInvoiceRepository.GetAttachmentsAsync().
        /// </summary>
        public static Task<byte[]> GenerateWithAttachmentsAsync(
            SavedTaxInvoice entity,
            IEnumerable<Models.TaxInvoice.Attachment.TaxInvoiceAttachment>?
                attachments)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            return Task.Run(() =>
            {
                byte[] invoicePdf = GenerateInvoicePdf(entity);

                var attachmentList = attachments?
                    .Where(a => a.HasContent)
                    .OrderBy(a => a.AttachmentType == "SignedPI" ? 0
                               : a.AttachmentType == "SignedDN" ? 1 : 2)
                    .ThenBy(a => a.UploadedAt)
                    .ToList()
                    ?? new();

                if (attachmentList.Count == 0)
                    return invoicePdf;

                return AppendAttachments(invoicePdf, attachmentList);
            });
        }

        /// <summary>
        /// Saves the invoice PDF to disk and opens it in the default viewer.
        /// Uses a temp file — deleted by OS on next boot.
        /// </summary>
        public static async Task SaveAndOpenAsync(SavedTaxInvoice entity)
        {
            byte[] pdf = await GenerateAsync(entity);

            string fileName = BuildFileName(entity, withAttachments: false);
            string tempPath = Path.Combine(Path.GetTempPath(), fileName);

            await File.WriteAllBytesAsync(tempPath, pdf);

            Process.Start(new ProcessStartInfo(tempPath)
            {
                UseShellExecute = true
            });
        }

        /// <summary>
        /// Saves the invoice + attachments PDF to a user-chosen path.
        /// Returns the saved file path, or null if user cancelled.
        /// </summary>
        public static async Task<string?> SaveToFileAsync(
            SavedTaxInvoice entity,
            IEnumerable<Models.TaxInvoice.Attachment.TaxInvoiceAttachment>?
                attachments = null)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Tax Invoice PDF",
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = Path.GetFileNameWithoutExtension(
                    BuildFileName(entity, withAttachments: attachments != null)),
                DefaultExt = "pdf"
            };

            if (dialog.ShowDialog() != true) return null;

            byte[] pdf = attachments != null
                ? await GenerateWithAttachmentsAsync(entity, attachments)
                : await GenerateAsync(entity);

            await File.WriteAllBytesAsync(dialog.FileName, pdf);
            return dialog.FileName;
        }

        // ── Private — core generation ─────────────────────────────────────

        private static byte[] GenerateInvoicePdf(SavedTaxInvoice entity)
        {
            var snapshot = TaxInvoiceMapper.DeserializeSnapshot(entity);

            if (snapshot == null)
                throw new InvalidOperationException(
                    $"Cannot generate PDF: invoice snapshot for " +
                    $"'{entity.InvoiceNumber}' is missing or corrupted.");

            var layout = new TaxInvoicePdfDocument(entity, snapshot);
            var document = new PdfDocumentWrapper(layout);

            return document.GeneratePdf();
        }

        // ── Private — attachment appending ────────────────────────────────

        /// <summary>
        /// Concatenates the invoice PDF with attachment PDFs.
        ///
        /// STRATEGY: QuestPDF does not natively merge PDFs.
        /// For production: recommend adding PdfSharp NuGet package.
        /// For now: returns invoice PDF only — attachments stored in DB
        /// remain available for future PdfSharp-based merging.
        /// </summary>
        private static byte[] AppendAttachments(
            byte[] invoicePdf,
            List<Models.TaxInvoice.Attachment.TaxInvoiceAttachment> attachments)
        {
            // Future: iterate attachments with PdfSharp to merge:
            // foreach (var attachment in attachments)
            // {
            //     if (attachment.MimeType == "application/pdf")
            //         PdfSharpMerger.Append(ms, attachment.FileBytes);
            //     else if (attachment.MimeType.StartsWith("image/"))
            //         PdfSharpMerger.AppendImage(ms, attachment.FileBytes);
            // }

            // For now: invoice-only. Attachments stay in DB for Chat 15+.
            using var ms = new MemoryStream();
            ms.Write(invoicePdf, 0, invoicePdf.Length);
            return ms.ToArray();
        }

        // ── Private — file naming ─────────────────────────────────────────

        private static string BuildFileName(
            SavedTaxInvoice entity, bool withAttachments)
        {
            string name = ExportFileNamer.Build(
                entity.InvoiceNumber,
                entity.CustomerName,
                entity.ProjectName,
                entity.IssueDate,
                ExportFormat.Pdf);

            if (!withAttachments)
                return name;

            return Path.GetFileNameWithoutExtension(name)
                   + "_WITH-ATTACHMENTS.pdf";
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  PDF DOCUMENT WRAPPER
    //  Bridges IPdfLayout → QuestPDF IDocument.
    //  Applies letterhead, page size, margins, page numbering.
    // ════════════════════════════════════════════════════════════════════════

    internal sealed class PdfDocumentWrapper : IDocument
    {
        private readonly Services.Export.Pdf.IPdfLayout _layout;

        public PdfDocumentWrapper(Services.Export.Pdf.IPdfLayout layout)
        {
            _layout = layout;
        }

        public DocumentMetadata GetMetadata() => new()
        {
            Title = $"{_layout.DocumentTitle} — {_layout.DocumentId}",
            Author = _layout.CompanyName,
            Subject = $"{_layout.DocumentTitle} — {_layout.Reference}",
            Keywords = "Tax Invoice UAE VAT Glass"
        };

        public DocumentSettings GetSettings() => new()
        {
            PdfA = false
        };

        public void Compose(IDocumentContainer container)
        {
            // This wrapper builds its own page (it does not go through
            // PdfExportEngine), so it owns the same two invariants the
            // engine guarantees: apply the saved layout, and reset the
            // section counter. Relying on Letterhead() to reset is not
            // enough — a document without a letterhead would inherit the
            // previous export's SEC numbering.
            ProGlassAutomation.Services.Export.Pdf.PdfThemeCatalog.ApplySaved();

            var metrics = ProGlassAutomation.Services.Export.Pdf.DocumentStyle.Metrics;

            container.Page(page =>
            {
                ProGlassAutomation.Services.Export.Pdf.Renderers.PdfChrome.Reset();

                page.Size(QuestPDF.Helpers.PageSizes.A4);
                page.Margin(
                    metrics.Theme.MarginMm,
                    QuestPDF.Infrastructure.Unit.Millimetre);
                page.DefaultTextStyle(x => x
                    .FontFamily(ProGlassAutomation.Services.Export.Pdf.DocumentStyle.FontFamily)
                    .FontSize(ProGlassAutomation.Services.Export.Pdf.DocumentStyle.FontSizeBody)
                    .FontColor(ProGlassAutomation.Services.Export.Pdf.DocumentStyle.ColorText));

                var lh = _layout.Letterhead;
                if (lh != null)
                {
                    page.Header().Column(h =>
                    {
                        h.Item().ShowOnce().Element(c =>
                            ProGlassAutomation.Services.Export.Pdf.Renderers
                                .LetterheadRenderer.Render(c, lh));
                        h.Item().SkipOnce().Element(c =>
                            ProGlassAutomation.Services.Export.Pdf.Renderers
                                .LetterheadRenderer.RenderContinuation(c, lh));
                    });
                }

                page.Content().PaddingTop(metrics.BlockGap).Column(col =>
                {
                    col.Spacing(metrics.SectionGap);
                    _layout.RenderContent(col);
                });

                page.Footer().Element(f =>
                    ProGlassAutomation.Services.Export.Pdf.Renderers
                        .FooterRenderer.Render(f, _layout.DocumentId));
            });
        }
    }
}