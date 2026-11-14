using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace ProGlassAutomation.Services.Export.Pdf.Renderers
{
    public static class FooterRenderer
    {
        public static void Render(IContainer container, string? documentId = null) =>
            PdfChrome.Footer(container, documentId);
    }
}
