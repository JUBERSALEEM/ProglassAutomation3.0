using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace ProGlassAutomation.Services.Export.Pdf.Renderers
{
    /// <summary>
    /// Page-1 letterhead. Composition comes from the active PDF layout.
    /// </summary>
    public static class LetterheadRenderer
    {
        public static void Render(IContainer container, LetterheadInfo info) =>
            PdfChrome.Letterhead(container, info);

        public static void RenderContinuation(IContainer container, LetterheadInfo info) =>
            PdfChrome.Continuation(container, info);
    }
}
