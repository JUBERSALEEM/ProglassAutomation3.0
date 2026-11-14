using System;

namespace ProGlassAutomation.Services.Export.Core
{
    /// <summary>
    /// Outcome of an export operation.
    /// Success   → SavedPath contains full file path
    /// Cancelled → user closed SaveFileDialog, no file written
    /// Failed    → ErrorMessage contains reason
    /// </summary>
    public class ExportResult
    {
        public bool Success { get; set; }
        public bool Cancelled { get; set; }
        public string? SavedPath { get; set; }
        public string? ErrorMessage { get; set; }
        public Exception? Exception { get; set; }

        public static ExportResult Ok(string path) =>
            new() { Success = true, SavedPath = path };

        public static ExportResult Cancel() =>
            new() { Cancelled = true };

        public static ExportResult Fail(string message, Exception? ex = null) =>
            new() { ErrorMessage = message, Exception = ex };
    }
}