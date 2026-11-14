using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Proforma;

namespace ProGlassAutomation.Step6Tests
{
    internal static partial class Program
    {
        private static void PdfExport()
        {
            string[] expected =
            {
                "ClassicNavy", "ModernTeal", "EditorialInk",
                "IndustrialSteel", "MinimalIvory"
            };
            Check("all five customer PDF styles are registered",
                ProformaPdfStyles.All.Count == 5
                && expected.All(id => ProformaPdfStyles.All.Any(style => style.Id == id)));

            var draft = new ProformaInvoiceDraft();
            draft.EnsureStructure();
            draft.ProformaNumber = "PI-2026-TEST";
            draft.Parties.Company.CompanyName = "ProGlass Automation";
            draft.Parties.Client.ClientName = "Test Client";

            foreach (ProformaPdfStyle style in ProformaPdfStyles.All)
            {
                byte[] bytes = ProformaPdfLayout.Render(draft, style);
                Check(style.Name + " renders a customer invoice PDF",
                    IsPdf(bytes));

                byte[] dossier = ProformaDossierPdfLayout.Render(
                    draft, style, includeProfitLoss: true);
                Check(style.Name + " renders an internal dossier PDF",
                    IsPdf(dossier));
            }

            byte[] safeDossier = ProformaDossierPdfLayout.Render(
                draft, ProformaPdfStyles.Default);
            Check("the dossier renders without internal P&L by default",
                IsPdf(safeDossier));
        }
        private static bool IsPdf(byte[] bytes) =>
            bytes.Length > 5
            && bytes[0] == (byte)'%'
            && bytes[1] == (byte)'P'
            && bytes[2] == (byte)'D'
            && bytes[3] == (byte)'F'
            && bytes[4] == (byte)'-';

    }
}
