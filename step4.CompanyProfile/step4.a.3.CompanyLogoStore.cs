// ═══════════════════════════════════════════════════════════════════════
// FILE: step4.CompanyProfile/step4.a.3.CompanyLogoStore.cs
// MODULE: step4 — where the company logo actually lives.
//
// The problem this solves
//   The ADV reference stored the picked file's absolute path. That works only
//   while the file stays exactly where it was: move the data folder, restore a
//   backup on another machine, or clean out Downloads, and the letterhead
//   silently loses its logo — or the profile throws while loading it.
//
// The rule here
//   A picked image is COPIED into the application's own data folder, under
//   `CompanyProfile\`, with a generated name. The database stores that name
//   (step4.a.1.LogoFileName). Nothing outside the app data folder is ever
//   referenced, so a backup or a move carries the logo with it:
//
//       %APPDATA%\ProGlassAutomation\CompanyProfile\logo-<guid>.png
//
//   • The folder is created on demand and is the only place this module writes.
//   • A stored value is re-validated on read: a name with a separator, an
//     unexpected extension or a file that no longer exists resolves to null
//     instead of throwing. A deleted logo therefore shows as "no logo" rather
//     than crashing the page.
//   • Import and delete are separate calls so the repository can keep the old
//     asset until the new profile row has actually committed.
//   • This class knows nothing about WPF or previews: persistence only. The
//     preview bitmap is loaded by step4.c.2, so the storage rules can be tested
//     without a display.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Database;
using System;
using System.Collections.Generic;
using System.IO;

namespace ProGlassAutomation.Services.Company
{
    /// <summary>Outcome of importing a picked image into the app data folder.</summary>
    public sealed class CompanyLogoImportResult
    {
        public bool Succeeded { get; init; }

        /// <summary>The stored file name, or "" when the import failed.</summary>
        public string FileName { get; init; } = "";

        /// <summary>Operator-facing reason when it failed; "" on success.</summary>
        public string Message { get; init; } = "";

        public static CompanyLogoImportResult Ok(string fileName) =>
            new() { Succeeded = true, FileName = fileName };

        public static CompanyLogoImportResult Fail(string message) =>
            new() { Succeeded = false, Message = message };
    }

    /// <summary>Copy-and-forget storage for the one company logo.</summary>
    public sealed class CompanyLogoStore
    {
        /// <summary>Folder name under the application data root.</summary>
        public const string FolderName = "CompanyProfile";

        /// <summary>An image larger than this is refused rather than copied.</summary>
        public const long MaxSourceBytes = 12L * 1024 * 1024;

        /// <summary>Formats a company logo realistically arrives in.</summary>
        public static readonly IReadOnlyList<string> AllowedExtensions =
            new[] { ".png", ".jpg", ".jpeg" };

        /// <summary>The dialog filter shown to the operator, built from the list above.</summary>
        public static string FileDialogFilter =>
            "Logo images (" + string.Join(";", Prefix(AllowedExtensions, "*")) + ")|"
            + string.Join(";", Prefix(AllowedExtensions, "*"))
            + "|All files (*.*)|*.*";

        /// <summary>Absolute folder the logo assets live in.</summary>
        public static string Folder => Path.Combine(AppDataRoot.CurrentFolder, FolderName);

        /// <summary>Full path for a stored file name, or null when it is not one.</summary>
        public static string? ResolvePath(string? fileName)
        {
            if (!IsSafeFileName(fileName)) return null;
            string path = Path.Combine(Folder, fileName!);
            return File.Exists(path) ? path : null;
        }

        /// <summary>
        /// True when the stored value is a bare file name this module could have
        /// written. Separators and ".." are refused, so a hand-edited database
        /// row can never point the store at an arbitrary file on disk.
        /// </summary>
        public static bool IsSafeFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            string name = fileName!.Trim();
            if (name.Length > 120) return false;
            if (name.Contains('/') || name.Contains('\\')) return false;
            if (name.Contains("..")) return false;
            if (Path.GetFileName(name) != name) return false;
            return HasAllowedExtension(name);
        }

        public static bool HasAllowedExtension(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            foreach (string allowed in AllowedExtensions)
                if (extension == allowed) return true;
            return false;
        }

        /// <summary>
        /// Checks a picked file before anything is copied. Returns null when the
        /// file is usable, otherwise the sentence to show the operator.
        /// </summary>
        public static string? ValidateSource(string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return "Choose an image file.";
            string path = sourcePath!.Trim();

            if (!File.Exists(path))
                return "That file is no longer there. Choose the logo again.";
            if (!HasAllowedExtension(path))
                return "The logo must be a PNG, JPG or JPEG image.";

            try
            {
                long length = new FileInfo(path).Length;
                if (length == 0) return "That image file is empty.";
                if (length > MaxSourceBytes)
                    return $"That image is {length / (1024 * 1024)} MB. Choose one under "
                           + $"{MaxSourceBytes / (1024 * 1024)} MB.";
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return "That file could not be read (" + error.GetType().Name + "). Choose another image.";
            }

            return null;
        }

        /// <summary>
        /// Copies a picked file into the app data folder under a new name.
        /// </summary>
        /// <remarks>
        /// The copy is deliberately NOT named after the source file: two picks of
        /// "logo.png" must not overwrite each other, and a name is never taken from
        /// the operator's filesystem into the database.
        /// </remarks>
        public static CompanyLogoImportResult Import(string? sourcePath)
        {
            string? problem = ValidateSource(sourcePath);
            if (problem != null) return CompanyLogoImportResult.Fail(problem);

            try
            {
                Directory.CreateDirectory(Folder);
                string extension = Path.GetExtension(sourcePath!.Trim()).ToLowerInvariant();
                string fileName = "logo-" + Guid.NewGuid().ToString("N") + extension;
                string destination = Path.Combine(Folder, fileName);

                // Copy to a temporary name first, then move into place: a crash or a
                // full disk mid-copy leaves no half-written asset that a profile row
                // could already be pointing at.
                string temporary = destination + ".tmp";
                File.Copy(sourcePath!.Trim(), temporary, overwrite: true);
                File.Move(temporary, destination);

                return CompanyLogoImportResult.Ok(fileName);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                                              or NotSupportedException or ArgumentException)
            {
                return CompanyLogoImportResult.Fail(
                    "The logo could not be stored (" + error.GetType().Name + "): " + error.Message);
            }
        }

        /// <summary>
        /// Removes a stored asset. Best effort and never throws: the profile row
        /// has already been saved by the time this runs, so a locked file must not
        /// turn a successful save into an error message.
        /// </summary>
        public static void Delete(string? fileName)
        {
            string? path = ResolvePath(fileName);
            if (path == null) return;
            try
            {
                File.Delete(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine("Company logo not deleted: " + error.Message);
            }
        }

        /// <summary>Asset names currently on disk, for verification and diagnostics.</summary>
        public static string[] Files()
        {
            try
            {
                if (!Directory.Exists(Folder)) return Array.Empty<string>();
                string[] files = Directory.GetFiles(Folder);
                var names = new List<string>(files.Length);
                foreach (string file in files) names.Add(Path.GetFileName(file));
                names.Sort(StringComparer.Ordinal);
                return names.ToArray();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> Prefix(IReadOnlyList<string> extensions, string prefix)
        {
            foreach (string extension in extensions) yield return prefix + extension;
        }
    }
}
