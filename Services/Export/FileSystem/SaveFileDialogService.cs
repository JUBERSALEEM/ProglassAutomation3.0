using Microsoft.Win32;
using System.IO;

namespace ProGlassAutomation.Services.Export.FileSystem
{
    /// <summary>
    /// Centralized SaveFileDialog wrapper.
    /// Every module gets the same dialog appearance and behavior.
    /// </summary>
    public class SaveFileDialogService
    {
        public string? PromptSavePath(
            string dialogTitle,
            string suggestedFileName,
            string filterString)
        {
            string raw = string.IsNullOrWhiteSpace(suggestedFileName)
                ? "DOCUMENT"
                : Path.GetFileName(suggestedFileName.Trim());
            string ext = Path.GetExtension(raw);
            string stem = string.IsNullOrWhiteSpace(ext)
                ? raw
                : Path.GetFileNameWithoutExtension(raw);

            var dialog = new SaveFileDialog
            {
                Title = $"Save {dialogTitle}",
                FileName = stem,
                Filter = filterString,
                DefaultExt = string.IsNullOrWhiteSpace(ext) ? null : ext.TrimStart('.'),
                AddExtension = true,
                OverwritePrompt = true,
                RestoreDirectory = true
            };

            bool? ok = dialog.ShowDialog();
            return ok == true ? dialog.FileName : null;
        }
    }
}