using CsvHelper;
using ProGlassAutomation.Services.Export.Core;

namespace ProGlassAutomation.Services.Export.Csv
{
    /// <summary>
    /// Contract each module implements to render its CSV content.
    /// The engine calls WriteRows(writer) with a configured CsvWriter
    /// after opening the file for writing.
    ///
    /// Layouts should:
    /// - Call writer.WriteField(...) for each cell
    /// - Call writer.NextRecord() to end each row
    /// - Write header row first, then data rows
    ///
    /// CSV is single-sheet, single-table by nature. For multi-table
    /// exports use Excel format instead.
    /// </summary>
    public interface ICsvLayout : IDocumentLayout
    {
        /// <summary>
        /// Writes all rows (header + data) to the provided CsvWriter.
        /// Called once per export operation.
        /// </summary>
        void WriteRows(CsvWriter writer);
    }
}