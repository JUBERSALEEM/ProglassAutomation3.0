using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ProGlassAutomation.Services.Export.Core;

namespace ProGlassAutomation.Services.Export.Pdf
{
    /// <summary>
    /// A module-owned letterhead composition.
    ///
    /// WHY THIS EXISTS (2026-09-11)
    /// ────────────────────────────
    /// The engine used to render every page-1 letterhead through the shared
    /// PdfChrome composition. The Proforma Invoice preview, however, is the
    /// approved document design ("what you see is what prints"), and its
    /// letterhead — company identity + reference box, or a full-width logo
    /// masthead — is part of that design. A layout can therefore supply its
    /// own letterhead renderer and the engine will use it verbatim on page 1
    /// (and its continuation strip on page 2+). Layouts that return null
    /// keep the shared chrome exactly as before.
    /// </summary>
    public interface IPdfLetterheadStyle
    {
        /// <summary>Page-1 letterhead, rendered inside the engine header.</summary>
        void Render(IContainer container, LetterheadInfo info);

        /// <summary>Slim identity strip for continuation pages (page 2+).</summary>
        void RenderContinuation(IContainer container, LetterheadInfo info);
    }

    /// <summary>
    /// Contract each module implements to render its PDF content.
    /// The engine calls RenderContent(content) with a QuestPDF ColumnDescriptor
    /// that has already been set up with page size, margins, letterhead,
    /// and footer.
    ///
    /// Layouts should:
    /// - Add sections via content.Item().Element(...)
    /// - Use shared renderers (SectionHeaderRenderer, LetterheadRenderer, etc.)
    /// - Never call Page(), Header(), Footer() — engine owns page structure
    /// </summary>
    public interface IPdfLayout : IDocumentLayout
    {
        /// <summary>
        /// Optional — if null, engine renders no letterhead block.
        /// Return a populated LetterheadInfo to show the shared header.
        /// </summary>
        LetterheadInfo? Letterhead { get; }

        /// <summary>
        /// Optional module-owned letterhead composition. Null = the engine
        /// renders the shared PdfChrome letterhead for the active theme.
        /// </summary>
        IPdfLetterheadStyle? LetterheadStyle => null;

        /// <summary>
        /// Page orientation. Default A4 portrait.
        /// </summary>
        PageLayout PageLayout { get; }

        /// <summary>
        /// Renders the body content inside the engine-managed page.
        /// This method is called once. Multi-page layouts should let
        /// QuestPDF's natural flow handle pagination.
        /// </summary>
        void RenderContent(ColumnDescriptor content);
    }
}
