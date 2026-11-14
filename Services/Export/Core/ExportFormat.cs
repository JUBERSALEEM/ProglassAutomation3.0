namespace ProGlassAutomation.Services.Export.Core
{
    /// <summary>
    /// Supported export formats. Used by ExportFileNamer to build filenames
    /// and by module layouts to declare what formats they support.
    /// </summary>
    public enum ExportFormat
    {
        Pdf,
        Excel,
        Csv
    }
}