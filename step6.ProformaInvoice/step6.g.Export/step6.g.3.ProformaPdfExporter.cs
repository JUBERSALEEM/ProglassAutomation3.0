using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProGlassAutomation.Models.Proforma;
using QuestPDF.Infrastructure;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>One QuestPDF export service for every Proforma Invoice PDF style.</summary>
    public sealed class ProformaPdfExporter
    {
        static ProformaPdfExporter()
        {
            // Keep the same QuestPDF engine used by the legacy product.
            // Confirm the Community licence remains appropriate before commercial distribution.
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public async Task ExportAsync(
            ProformaInvoiceDraft draft,
            ProformaPdfStyle style,
            string outputPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(draft);
            ArgumentNullException.ThrowIfNull(style);
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Choose a PDF output path.", nameof(outputPath));

            string fullPath = Path.GetFullPath(outputPath);
            string? folder = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            ProformaInvoiceDraft snapshot = draft.Clone();
            byte[] pdf = await Task.Run(
                () => ProformaPdfLayout.Render(snapshot, style), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllBytesAsync(fullPath, pdf, cancellationToken).ConfigureAwait(false);
        }

        public static string SuggestedFileName(ProformaInvoiceDraft draft)
        {
            string number = string.IsNullOrWhiteSpace(draft.ProformaNumber)
                ? "DRAFT" : draft.ProformaNumber.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
                number = number.Replace(invalid, '-');
            return "PI_" + number + ".pdf";
        }
    }
}