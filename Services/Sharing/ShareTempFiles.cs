// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/ShareTempFiles.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — secure temp staging (Stage 1)
//
// WHY STAGING EXISTS
// ──────────────
// Providers hand files to the OS (Explorer reveal, future real upload
// APIs). ProcessStartGuard only operates inside allowed roots (app data,
// temp, Downloads) — a Desktop-saved export cannot be revealed directly.
// So every share STAGES a copy into the app-controlled share temp folder
// and hands THAT over. The operator's original is never moved, renamed or
// deleted; only our copies are swept.
//
// SAFETY RULES
// ──────────────
//   • Root: %TEMP%\ProGlassAutomation\Share\ (same convention as
//     PdfExportEngine's temp usage). One subfolder per share (guid).
//   • Staged names are sanitized to a file-name core (no directories, no
//     traversal, invalid chars replaced) — the original name is preserved
//     when safe, so the recipient sees PI-2026-001.pdf, not a guid.
//   • SweepStale: removes share folders older than the retention (default
//     24h) — crash-proof by age, and startup calls it best-effort.
//   • Never throws: temp failures return "" and the provider reports them
//     as share failures with a user-facing reason.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Logging;
using System;
using System.IO;
using System.Linq;

namespace ProGlassAutomation.Services.Sharing
{
    public static class ShareTempFiles
    {
        public static string RootFolder =>
            Path.Combine(Path.GetTempPath(), "ProGlassAutomation", "Share");

        /// <summary>
        /// Creates a fresh per-share folder and returns its path, or ""
        /// on failure. Never throws.
        /// </summary>
        public static string CreateShareFolder()
        {
            try
            {
                string folder = Path.Combine(
                    RootFolder, DateTime.Now.ToString("yyyyMMdd"),
                    "share-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                return folder;
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Sharing.Temp] create folder failed: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Copies the artifact into the share folder under a sanitized name
        /// (original name when safe). Returns the staged path or "".
        /// Never throws; never touches the original.
        /// </summary>
        public static string StageCopy(ShareableArtifact artifact, string shareFolder)
        {
            if (artifact == null || string.IsNullOrWhiteSpace(shareFolder))
                return "";

            try
            {
                Directory.CreateDirectory(shareFolder);
                string safeName = SanitizeFileName(artifact.FileName, artifact.FileExtension);
                string dest = Path.Combine(shareFolder, safeName);

                // Never overwrite an unrelated file: uniquify on collision.
                if (File.Exists(dest))
                {
                    string stem = Path.GetFileNameWithoutExtension(safeName);
                    dest = Path.Combine(
                        shareFolder,
                        $"{stem}-{Guid.NewGuid().ToString("N").Substring(0, 6)}{artifact.FileExtension}");
                }

                File.Copy(artifact.FilePath, dest);
                return dest;
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Sharing.Temp] stage failed for {artifact.FileName}: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Best-effort removal of one share folder (after a completed
        /// in-process share). Handoff providers keep the folder until the
        /// stale sweep — the user still needs the file beside their chat
        /// app AFTER our process reports ExternalAppOpened.
        /// </summary>
        public static void RemoveShareFolder(string? shareFolder)
        {
            if (string.IsNullOrWhiteSpace(shareFolder)) return;
            try
            {
                string full = Path.GetFullPath(shareFolder);
                string root = Path.GetFullPath(RootFolder);
                if (!full.StartsWith(root + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    return; // not ours — never delete outside the root
                if (Directory.Exists(full))
                    Directory.Delete(full, recursive: true);
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Sharing.Temp] remove failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes share-day folders older than retention (crash-proof
        /// cleanup; call at startup best-effort). Never throws.
        /// </summary>
        public static void SweepStale(TimeSpan? retention = null)
        {
            try
            {
                TimeSpan keep = retention ?? TimeSpan.FromHours(24);
                if (!Directory.Exists(RootFolder)) return;

                foreach (string day in Directory.GetDirectories(RootFolder))
                {
                    try
                    {
                        var info = new DirectoryInfo(day);
                        if (DateTime.Now - info.LastWriteTime > keep)
                            Directory.Delete(day, recursive: true);
                    }
                    catch (Exception ex)
                    {
                        AppLogService.Debug($"[Sharing.Temp] sweep one failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Sharing.Temp] sweep failed: {ex.Message}");
            }
        }

        /// <summary>
        /// File-name core only: strips directories, traversal and invalid
        /// chars; preserves the real extension. Never returns "".
        /// </summary>
        public static string SanitizeFileName(string? fileName, string? fallbackExtension)
        {
            string core = "";
            try { core = Path.GetFileName(fileName ?? ""); } catch { core = ""; }
            if (string.IsNullOrWhiteSpace(core)) core = "document";

            char[] invalid = Path.GetInvalidFileNameChars();
            core = new string(core.Select(c =>
                invalid.Contains(c) || c == ';' ? '_' : c).ToArray()).Trim();

            core = core.Replace("..", "");
            if (string.IsNullOrWhiteSpace(core)) core = "document";

            string ext = "";
            try { ext = Path.GetExtension(core); } catch { ext = ""; }
            if (string.IsNullOrEmpty(ext) && !string.IsNullOrWhiteSpace(fallbackExtension))
            {
                string fb = fallbackExtension.Trim();
                if (!fb.StartsWith(".")) fb = "." + fb;
                core += fb;
            }
            if (core.Length > 120)
                core = core.Substring(0, 100) + ext;
            return core;
        }
    }
}
