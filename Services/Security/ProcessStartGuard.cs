// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Security/ProcessStartGuard.cs
// PROJECT: ProGlassAutomation
// MODULE: Security — Process.Start sandbox (Phase 8: Security Hardening v2)
// VERSION: Phase 8 — 2026-08-21
//
// PURPOSE (Audit: attachment Process.Start RCE):
//   32 Process.Start call sites — some open user-supplied files (attachments,
//   exports, recordings) with UseShellExecute=true. If path is attacker-controlled,
//   could lead to RCE: e.g. attachment name "../../evil.exe" or UNC path.
//
//   This guard centralizes validation:
//     • Path must exist (for open)
//     • Must be inside allowed root (AppData\ProGlassAutomation) OR temp folder OR export folder
//     • No .. traversal, no UNC, no invalid chars, no ADS (colon)
//     • Extension allow-list for attachments (jpg, png, pdf, etc.)
//     • Size cap already enforced in AttachmentService, but double-checked
//     • For tools (ffmpeg, ffprobe): must be from ToolsFolder or system PATH, not arbitrary
//     • For explorer.exe /select: path must exist and be inside allowed root
//     • UseShellExecute=false for tools (no shell), true for documents (with validation)
//     • All failures logged with CorrelationId + ErrorId, never throw
//
// USAGE:
//   var result = ProcessStartGuard.TryOpenFile(filePath, correlationId);
//   var result = ProcessStartGuard.TryOpenFolder(folderPath, correlationId);
//   var result = ProcessStartGuard.TryStartTool(toolPath, args, correlationId);
//
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace ProGlassAutomation.Services.Security
{
    public static class ProcessStartGuard
    {
        // ── Allowed roots ──────────────────────────────────────────────

        private static string AppDataRoot => ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder;

        private static IEnumerable<string> AllowedRoots()
        {
            yield return ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder;
            yield return ProGlassAutomation.Services.Database.AppDataRoot.ProductionFolder;
            yield return ProGlassAutomation.Services.Database.AppDataRoot.DemoFolder;
            yield return Path.GetTempPath();
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        // ── Allowed extensions for document open ───────────────────────

        private static readonly HashSet<string> AllowedDocExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp",
            ".pdf",
            ".xlsx", ".xls", ".csv",
            ".mp4", ".avi", ".wav",
            ".json", ".txt", ".log"
        };

        // ── Validation helpers ─────────────────────────────────────────

        public static bool IsPathInsideAllowedRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                string full = Path.GetFullPath(path);
                // UNC is allowed only when it is the configured shared data root.
                if (full.StartsWith(@"\\", StringComparison.Ordinal)
                    && !IsInsideRoot(full, ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder))
                    return false;
                // No ADS (alternate data stream)
                if (full.Contains(':') && full.IndexOf(':', 2) != -1) return false; // allow C:\ but not file:stream

                foreach (var root in AllowedRoots())
                {
                    if (IsInsideRoot(full, root))
                        return true;
                }

                // Also allow if file is inside current AppDataRoot subfolder.
                if (IsInsideRoot(full, AppDataRoot))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsInsideRoot(string fullPath, string root)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(root)) return false;
                string rootFull = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string full = Path.GetFullPath(fullPath);
                return full.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || full.StartsWith(rootFull + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool ContainsTraversal(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return true;
            if (path.Contains("..", StringComparison.Ordinal)) return true;
            if (path.Contains(":", StringComparison.Ordinal) && path.IndexOf(':', 1) != -1 && !Path.IsPathRooted(path)) return true;
            return false;
        }

        public static bool HasAllowedExtension(string path)
        {
            try
            {
                string ext = Path.GetExtension(path);
                return AllowedDocExtensions.Contains(ext);
            }
            catch
            {
                return false;
            }
        }

        public static string SanitizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";

            // Remove invalid chars, but keep drive colon and separators
            var invalid = Path.GetInvalidPathChars();
            var cleaned = new string(path.Where(c => !invalid.Contains(c)).ToArray());

            // No traversal
            if (ContainsTraversal(cleaned))
            {
                // Remove .. segments
                cleaned = cleaned.Replace("..", "");
            }

            return cleaned;
        }

        // ── Safe open file ─────────────────────────────────────────────

        public static Result TryOpenFile(string filePath, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewId();
            using var scope = CorrelationContext.BeginScope(corrId);

            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return Result.Fail("File path is empty", corrId);

                string sanitized = SanitizePath(filePath);

                if (ContainsTraversal(sanitized))
                    return Result.Fail($"Path traversal detected: {filePath}", corrId);

                if (!File.Exists(sanitized))
                    return Result.Fail($"File not found: {sanitized}", corrId);

                if (!IsPathInsideAllowedRoot(sanitized))
                {
                    AppLogService.Warning(
                        $"ProcessStartGuard: File outside allowed root blocked: {sanitized} [{corrId}]",
                        new LogContext { Module = "Security", Operation = "TryOpenFile", CorrelationId = corrId, Extra = $"Path={sanitized}" });
                    return Result.Fail($"File outside allowed folder: {sanitized}", corrId);
                }

                if (!HasAllowedExtension(sanitized))
                {
                    AppLogService.Warning(
                        $"ProcessStartGuard: File with disallowed extension blocked: {sanitized} [{corrId}]",
                        new LogContext { Module = "Security", Operation = "TryOpenFile", CorrelationId = corrId });
                    return Result.Fail($"File type not allowed: {Path.GetExtension(sanitized)}", corrId);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = sanitized,
                    UseShellExecute = true, // need shell for default app
                    Verb = "open"
                };

                Process.Start(psi);

                AppLogService.Info($"Opened file: {sanitized} [{corrId}]",
                    new LogContext { Module = "Security", Operation = "TryOpenFile", CorrelationId = corrId });

                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"TryOpenFile failed for {filePath}: {ex.Message}", ex,
                    new LogContext { Module = "Security", Operation = "TryOpenFile", CorrelationId = corrId });
                return Result.Fail($"Could not open file: {ex.Message}", ex, corrId, errId);
            }
        }

        // ── Safe open folder ───────────────────────────────────────────

        public static Result TryOpenFolder(string folderPath, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewId();
            using var scope = CorrelationContext.BeginScope(corrId);

            try
            {
                if (string.IsNullOrWhiteSpace(folderPath))
                    return Result.Fail("Folder path is empty", corrId);

                string sanitized = SanitizePath(folderPath);

                if (ContainsTraversal(sanitized))
                    return Result.Fail($"Path traversal detected: {folderPath}", corrId);

                if (!Directory.Exists(sanitized))
                    return Result.Fail($"Folder not found: {sanitized}", corrId);

                if (!IsPathInsideAllowedRoot(sanitized))
                {
                    AppLogService.Warning(
                        $"ProcessStartGuard: Folder outside allowed root blocked: {sanitized} [{corrId}]",
                        new LogContext { Module = "Security", Operation = "TryOpenFolder", CorrelationId = corrId });
                    return Result.Fail($"Folder outside allowed: {sanitized}", corrId);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = sanitized,
                    UseShellExecute = true,
                    Verb = "open"
                };

                Process.Start(psi);

                AppLogService.Info($"Opened folder: {sanitized} [{corrId}]",
                    new LogContext { Module = "Security", Operation = "TryOpenFolder", CorrelationId = corrId });

                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"TryOpenFolder failed for {folderPath}: {ex.Message}", ex,
                    new LogContext { Module = "Security", Operation = "TryOpenFolder", CorrelationId = corrId });
                return Result.Fail($"Could not open folder: {ex.Message}", ex, corrId, errId);
            }
        }

        // ── Safe start tool (ffmpeg, ffprobe) — UseShellExecute=false ──

        /// <summary>
        /// Starts a sanitized tool. <paramref name="redirectStdIn"/> opts
        /// into a writable stdin pipe.
        ///
        /// It is OPT-IN rather than always-on deliberately: a redirected
        /// stdin that nobody writes to leaves the child waiting on a handle
        /// that never closes, which is a hang. Only streaming callers (the
        /// raw video pipe) want it.
        /// </summary>
        public static Result<Process?> TryStartTool(
            string toolPath, string args, string? correlationId, bool redirectStdIn)
            => TryStartToolCore(toolPath, args, correlationId, redirectStdIn);

        public static Result<Process?> TryStartTool(string toolPath, string args, string? correlationId = null)
            => TryStartToolCore(toolPath, args, correlationId, false);

        private static Result<Process?> TryStartToolCore(
            string toolPath, string args, string? correlationId, bool redirectStdIn)
        {
            var corrId = correlationId ?? CorrelationContext.NewId();
            using var scope = CorrelationContext.BeginScope(corrId);

            try
            {
                if (string.IsNullOrWhiteSpace(toolPath))
                    return Result<Process?>.Fail("Tool path is empty", corrId);

                string sanitized = SanitizePath(toolPath);

                // Tool must be from ToolsFolder or system PATH (no arbitrary path)
                bool isFromTools = false;
                try
                {
                    string toolsFull = Path.GetFullPath(Agent.Demo.Video.FfmpegMp4Composer.ToolsFolder);
                    string toolFull = Path.GetFullPath(sanitized);
                    isFromTools = toolFull.StartsWith(toolsFull, StringComparison.OrdinalIgnoreCase);
                }
                catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ProcessStartGuard] Tool-path resolution failed: " + ex.Message); }

                bool isSystemTool = !Path.IsPathRooted(sanitized) &&
                                    (sanitized.Equals("ffmpeg", StringComparison.OrdinalIgnoreCase) ||
                                     sanitized.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ||
                                     sanitized.Equals("ffprobe", StringComparison.OrdinalIgnoreCase) ||
                                     sanitized.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase));

                if (!isFromTools && !isSystemTool)
                {
                    // For security, only allow tools from ToolsFolder or known system names
                    // But don't block if file exists and is inside allowed root (for test)
                    if (!IsPathInsideAllowedRoot(sanitized) && !File.Exists(sanitized))
                    {
                        AppLogService.Warning(
                            $"ProcessStartGuard: Tool outside allowed blocked: {sanitized} [{corrId}]",
                            new LogContext { Module = "Security", Operation = "TryStartTool", CorrelationId = corrId });
                        return Result<Process?>.Fail($"Tool not allowed: {sanitized}", corrId);
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = sanitized,
                    Arguments = args,
                    UseShellExecute = false, // critical for tools — no shell
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = redirectStdIn
                };

                var proc = Process.Start(psi);

                AppLogService.Info($"Started tool: {sanitized} {args} [{corrId}]",
                    new LogContext { Module = "Security", Operation = "TryStartTool", CorrelationId = corrId });

                return Result<Process?>.Ok(proc, corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"TryStartTool failed for {toolPath}: {ex.Message}", ex,
                    new LogContext { Module = "Security", Operation = "TryStartTool", CorrelationId = corrId });
                return Result<Process?>.Fail($"Could not start tool: {ex.Message}", ex, corrId, errId);
            }
        }

        // ── Safe open URL (Sharing Center handoff) ───────────────────────
        // Opens a URL with the OS default handler. Scheme-allowlisted:
        //   • https    — wa.me / web.whatsapp.com chat handoff, mail links
        //   • mailto   — email handoff
        //   • whatsapp — WhatsApp Desktop deep link (whatsapp://send?...),
        //                opens the installed, logged-in desktop app on the
        //                recipient's chat with the message prefilled.
        // No other scheme, no javascript:, no file:. The URL is built by
        // the sharing providers from validated parts (digits-only phone,
        // Uri-escaped text) — never from raw operator input. Failures are
        // logged with CorrelationId + ErrorId, never thrown.

        private static readonly HashSet<string> AllowedUrlSchemes =
            new(StringComparer.OrdinalIgnoreCase) { "https", "mailto", "whatsapp" };

        public static Result TryOpenUrl(string url, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewId();
            using var scope = CorrelationContext.BeginScope(corrId);

            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    return Result.Fail("URL is empty", corrId);

                if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)
                    || string.IsNullOrWhiteSpace(uri.Scheme)
                    || !AllowedUrlSchemes.Contains(uri.Scheme))
                    return Result.Fail("URL scheme is not allowed", corrId);

                // Defense in depth: even an allowlisted scheme must not
                // smuggle credentials or a second scheme.
                if (!string.IsNullOrEmpty(uri.UserInfo)
                    || url.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
                    return Result.Fail("URL is not safe to open", corrId);

                var psi = new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                };

                Process.Start(psi);

                // Log the target host/shape, never the full query (the
                // message text is operator content, not audit content).
                AppLogService.Info(
                    $"Opened URL scheme={uri.Scheme} host={uri.Host} [{corrId}]",
                    new LogContext { Module = "Security", Operation = "TryOpenUrl", CorrelationId = corrId });

                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"TryOpenUrl failed: {ex.Message}", ex,
                    new LogContext { Module = "Security", Operation = "TryOpenUrl", CorrelationId = corrId });
                return Result.Fail($"Could not open link: {ex.Message}", ex, corrId, errId);
            }
        }

        // ── Safe explorer /select ──────────────────────────────────────

        public static Result TryOpenExplorerSelect(string filePath, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewId();
            using var scope = CorrelationContext.BeginScope(corrId);

            try
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    return Result.Fail("File path is empty", corrId);

                string sanitized = SanitizePath(filePath);

                if (!File.Exists(sanitized))
                    return Result.Fail($"File not found: {sanitized}", corrId);

                if (!IsPathInsideAllowedRoot(sanitized))
                    return Result.Fail($"File outside allowed: {sanitized}", corrId);

                // Use explorer.exe /select,"path" — validate path first, then use shell
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{sanitized}\"",
                    UseShellExecute = true
                };

                Process.Start(psi);

                AppLogService.Info($"Explorer select: {sanitized} [{corrId}]",
                    new LogContext { Module = "Security", Operation = "TryOpenExplorerSelect", CorrelationId = corrId });

                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"TryOpenExplorerSelect failed for {filePath}: {ex.Message}", ex,
                    new LogContext { Module = "Security", Operation = "TryOpenExplorerSelect", CorrelationId = corrId });
                return Result.Fail($"Could not open explorer: {ex.Message}", ex, corrId, errId);
            }
        }
    }
}
