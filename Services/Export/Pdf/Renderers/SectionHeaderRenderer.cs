using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace ProGlassAutomation.Services.Export.Pdf.Renderers
{
    /// <summary>
    /// Section title. Style follows the active PDF layout.
    /// </summary>
    public static class SectionHeaderRenderer
    {
        public static void Render(IContainer container, string title) =>
            PdfChrome.Section(container, title);
    }
}
