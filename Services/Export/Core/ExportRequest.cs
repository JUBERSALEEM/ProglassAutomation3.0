namespace ProGlassAutomation.Services.Export.Core
{
    /// <summary>
    /// Options passed to any export engine.
    ///
    /// PromptForLocation = true  → show SaveFileDialog
    /// PromptForLocation = false → save to OutputPath directly (batch mode)
    ///
    /// OpenAfterSave = true → launch file with default app after save
    /// </summary>
    public class ExportRequest
    {
        /// <summary>
        /// Full path to save file. Ignored when PromptForLocation = true.
        /// </summary>
        public string? OutputPath { get; set; }

        /// <summary>
        /// When true, engine shows SaveFileDialog. When false, uses OutputPath.
        /// </summary>
        public bool PromptForLocation { get; set; } = true;

        /// <summary>
        /// When true, engine launches file with default application after save.
        /// </summary>
        public bool OpenAfterSave { get; set; } = true;

        /// <summary>
        /// Suggested filename shown in SaveFileDialog. Extension added by engine.
        /// If null, engine uses ExportFileNamer with layout's DocumentId +
        /// CompanyName + Reference + DocumentDate.
        /// </summary>
        public string? SuggestedFileName { get; set; }
    }
}