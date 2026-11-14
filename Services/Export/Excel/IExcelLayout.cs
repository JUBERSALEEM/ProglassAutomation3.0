using ClosedXML.Excel;
using ProGlassAutomation.Services.Export.Core;

namespace ProGlassAutomation.Services.Export.Excel
{
    /// <summary>
    /// Contract each module implements to render its Excel content.
    /// The engine calls RenderWorkbook(workbook) with a fresh IXLWorkbook
    /// after opening the file dialog.
    ///
    /// Layouts should:
    /// - Add one or more worksheets via workbook.Worksheets.Add(name)
    /// - Set column widths, headers, and data rows
    /// - Apply styles using WorkbookStyle constants for consistency
    ///
    /// Multi-sheet exports are supported — modules can add as many
    /// worksheets as they need (e.g. "Summary" + "Line Items" + "Terms").
    /// </summary>
    public interface IExcelLayout : IDocumentLayout
    {
        /// <summary>
        /// Renders content into the provided workbook.
        /// Called once per export operation.
        /// </summary>
        void RenderWorkbook(IXLWorkbook workbook);
    }
}