using System;
using System.Globalization;
using System.IO;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Company;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>Prints the Company Profile snapshot and its owned logo asset.</summary>
    public static class ProformaPdfLetterheadRenderer
    {
        public static void Render(IContainer container, ProformaInvoiceDraft invoice, ProformaPdfStyle style)
        {
            var company = invoice.Parties.Company;
            string? logoPath = CompanyLogoStore.ResolvePath(company.LogoFileName);
            bool topPanel = string.Equals(company.LogoLayout, "TopPanel", StringComparison.OrdinalIgnoreCase);
            bool leftPortrait = string.Equals(company.LogoLayout, "LeftPortrait", StringComparison.OrdinalIgnoreCase);
            byte[]? logo = logoPath != null && (topPanel || leftPortrait)
                ? File.ReadAllBytes(logoPath) : null;
            float logoPoints = (float)(Math.Clamp(
                company.LogoSizeMm > 0d ? company.LogoSizeMm : 22d, 12d, 40d) * 2.83465d);

            container.Column(column =>
            {
                if (logo != null && topPanel)
                    column.Item().AlignCenter().Height(logoPoints).Width(220)
                        .Image(logo).FitArea();

                column.Item().Row(row =>
                {
                    if (logo != null && leftPortrait)
                        row.ConstantItem(logoPoints).Height(logoPoints).Image(logo).FitArea();

                    row.RelativeItem().Column(identity =>
                    {
                        identity.Item().Text(EmptyFallback(company.CompanyName, "Company"))
                            .FontSize(style.TitleSize).Bold().FontColor(style.Accent);
                        AddLine(identity, company.Address, style);
                        AddLine(identity, company.Telephone, style);
                        AddLine(identity, company.Email, style);
                        AddLine(identity, company.Website, style);
                        AddLine(identity, company.Trn.Length == 0 ? "" : "TRN " + company.Trn, style);
                        AddLine(identity, company.RepresentativeName, style);
                        AddLine(identity, company.RepresentativeMobile, style);
                    });

                    row.ConstantItem(160).AlignRight().Column(reference =>
                    {
                        reference.Item().Text("PROFORMA INVOICE").Bold()
                            .FontSize(style.TableFontSize + 1).FontColor(style.Accent);
                        reference.Item().Text(EmptyFallback(invoice.ProformaNumber, "UNNUMBERED DRAFT"))
                            .FontSize(style.TitleSize).Bold().FontColor(style.Text);
                        reference.Item().Text("Issue: " + Date(invoice.IssueDate)).FontSize(8).FontColor(style.Muted);
                        reference.Item().Text("Valid until: " + Date(invoice.ValidUntil)).FontSize(8).FontColor(style.Muted);
                    });
                });

                column.Item().PaddingTop(5).Height(2).Background(style.Highlight);
            });
        }

        private static void AddLine(ColumnDescriptor column, string value, ProformaPdfStyle style)
        {
            if (!string.IsNullOrWhiteSpace(value))
                column.Item().Text(value).FontSize(7.5f).FontColor(style.Muted);
        }

        private static string EmptyFallback(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static string Date(DateTime value) =>
            value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    }
}