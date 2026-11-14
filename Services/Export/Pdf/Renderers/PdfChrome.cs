using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.Export.Pdf.Renderers
{
    /// <summary>
    /// Layout-aware print chrome. Every document calls these helpers
    /// instead of painting its own bars — switching Settings → PDF Layout
    /// rebuilds letterhead, sections, tables, cards, totals and
    /// signatures as five different page architectures.
    /// </summary>
    public static partial class PdfChrome
    {
    }

    public sealed class SignatureSlot
    {
        public string Title { get; init; } = "";
        public Action<ColumnDescriptor>? Body { get; init; }
    }
}
