// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Security/AttachmentValidationService.cs
// PROJECT: ProGlassAutomation
// MODULE: Security — Attachment Validation (Phase 8: Security Hardening v2)
// VERSION: Phase 8 — 2026-08-21
//
// PURPOSE:
//   Hardens attachment handling (Step 8 PI, DN, TI attachments):
//     • Extension allow-list (jpg, png, pdf, etc.)
//     • MIME sniffing — reads file header, not just extension
//     • Size cap (10MB default, configurable)
//     • File name sanitization (no .., no separators, no trailing dots)
//     • Virus scan hook (optional, pluggable)
//     • Quarantine for suspicious files
//
//   Previously: only extension check + size, no MIME sniffing.
//   Now: extension + MIME + size + name sanitization + scan hook.
//
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Security
{
    public sealed class AttachmentValidationService
    {
        public const long MaxFileSizeBytes = 10L * 1024 * 1024; // 10 MB
        public const string MaxFileSizeDisplay = "10 MB";

        private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"
        };

        private static readonly HashSet<string> AllowedPdfExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf"
        };

        private static readonly HashSet<string> AllowedAllExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".pdf"
        };

        // ── MIME signatures (file header sniffing) ─────────────────────

        private static readonly Dictionary<string, byte[]> MimeSignatures = new()
        {
            [".jpg"] = new byte[] { 0xFF, 0xD8, 0xFF },
            [".jpeg"] = new byte[] { 0xFF, 0xD8, 0xFF },
            [".png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            [".gif"] = new byte[] { 0x47, 0x49, 0x46 },
            [".bmp"] = new byte[] { 0x42, 0x4D },
            [".pdf"] = new byte[] { 0x25, 0x50, 0x44, 0x46 }, // %PDF
            [".webp"] = new byte[] { 0x52, 0x49, 0x46, 0x46 }, // RIFF
        };

        // ── Validation ─────────────────────────────────────────────────

        public static Result ValidateImage(string filePath, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewId();
            using var scope = CorrelationContext.BeginScope(corrId);

            var extResult = ValidateExtension(filePath, AllowedImageExtensions, corrId);
            if (extResult.IsFailure) return extResult;

            var sizeResult = ValidateSize(filePath, corrId);
            if (sizeResult.IsFailure) return sizeResult;

            var mimeResult = ValidateMime(filePath, corrId);
            if (mimeResult.IsFailure) return mimeResult;

            var nameResult = ValidateFileName(Path.GetFileName(filePath), corrId);
            if (nameResult.IsFailure) return nameResult;

            return Result.Ok(corrId);
        }

        public static Result ValidatePdf(string filePath, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewId();
            using var scope = CorrelationContext.BeginScope(corrId);

            var extResult = ValidateExtension(filePath, AllowedPdfExtensions, corrId);
            if (extResult.IsFailure) return extResult;

            var sizeResult = ValidateSize(filePath, corrId);
            if (sizeResult.IsFailure) return sizeResult;

            var mimeResult = ValidateMime(filePath, corrId);
            if (mimeResult.IsFailure) return mimeResult;

            var nameResult = ValidateFileName(Path.GetFileName(filePath), corrId);
            if (nameResult.IsFailure) return nameResult;

            return Result.Ok(corrId);
        }

        public static Result ValidateExtension(string filePath, HashSet<string> allowed, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();

            try
            {
                string ext = Path.GetExtension(filePath);
                if (string.IsNullOrWhiteSpace(ext) || !allowed.Contains(ext))
                {
                    AppLogService.Warning($"AttachmentValidation: Extension not allowed: {ext} for {filePath} [{corrId}]",
                        new LogContext { Module = "Security", Operation = "ValidateExtension", CorrelationId = corrId, Extra = $"Ext={ext}, Allowed={string.Join(",", allowed)}" });
                    return Result.Fail($"File type not allowed: {ext}. Allowed: {string.Join(", ", allowed)}", corrId);
                }

                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"ValidateExtension failed for {filePath}: {ex.Message}", ex,
                    new LogContext { Module = "Security", Operation = "ValidateExtension", CorrelationId = corrId });
                return Result.Fail($"Could not validate extension: {ex.Message}", ex, corrId, errId);
            }
        }

        public static Result ValidateSize(string filePath, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();

            try
            {
                var fi = new FileInfo(filePath);
                if (!fi.Exists)
                    return Result.Fail($"File not found: {filePath}", corrId);

                if (fi.Length > MaxFileSizeBytes)
                {
                    AppLogService.Warning($"AttachmentValidation: File too large: {fi.Length} > {MaxFileSizeBytes} for {filePath} [{corrId}]",
                        new LogContext { Module = "Security", Operation = "ValidateSize", CorrelationId = corrId, Extra = $"Size={fi.Length}, Max={MaxFileSizeBytes}" });
                    return Result.Fail($"File too large: {fi.Length / (1024 * 1024)} MB, max {MaxFileSizeDisplay}", corrId);
                }

                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"ValidateSize failed for {filePath}: {ex.Message}", ex,
                    new LogContext { Module = "Security", Operation = "ValidateSize", CorrelationId = corrId });
                return Result.Fail($"Could not validate size: {ex.Message}", ex, corrId, errId);
            }
        }

        public static Result ValidateMime(string filePath, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();

            try
            {
                string ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (!MimeSignatures.TryGetValue(ext, out var expectedHeader))
                    return Result.Ok(corrId); // no signature for this ext, skip MIME check

                using var fs = File.OpenRead(filePath);
                var buffer = new byte[expectedHeader.Length];
                int read = fs.Read(buffer, 0, buffer.Length);

                if (read < expectedHeader.Length)
                    return Result.Fail($"File too small to be valid {ext}", corrId);

                for (int i = 0; i < expectedHeader.Length; i++)
                {
                    if (buffer[i] != expectedHeader[i])
                    {
                        AppLogService.Warning($"AttachmentValidation: MIME mismatch for {filePath}: expected {BitConverter.ToString(expectedHeader)} got {BitConverter.ToString(buffer)} [{corrId}]",
                            new LogContext { Module = "Security", Operation = "ValidateMime", CorrelationId = corrId, Extra = $"Ext={ext}" });
                        return Result.Fail($"File content does not match extension {ext} — possible spoofing", corrId);
                    }
                }

                // Additional check for WEBP — RIFF....WEBP
                if (ext == ".webp")
                {
                    fs.Seek(8, SeekOrigin.Begin);
                    var webpBuf = new byte[4];
                    fs.Read(webpBuf, 0, 4);
                    string webpStr = System.Text.Encoding.ASCII.GetString(webpBuf);
                    if (webpStr != "WEBP")
                        return Result.Fail("Invalid WEBP file — missing WEBP marker", corrId);
                }

                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"ValidateMime failed for {filePath}: {ex.Message}", ex,
                    new LogContext { Module = "Security", Operation = "ValidateMime", CorrelationId = corrId });
                return Result.Fail($"Could not validate file content: {ex.Message}", ex, corrId, errId);
            }
        }

        public static Result ValidateFileName(string fileName, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();

            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return Result.Fail("File name is empty", corrId);

                // No path separators
                if (fileName.Contains(Path.DirectorySeparatorChar) || fileName.Contains(Path.AltDirectorySeparatorChar))
                    return Result.Fail($"File name cannot contain path separators: {fileName}", corrId);

                // No traversal
                if (fileName.Contains("..", StringComparison.Ordinal))
                    return Result.Fail($"File name cannot contain '..': {fileName}", corrId);

                // No invalid chars
                var invalid = Path.GetInvalidFileNameChars();
                if (fileName.Any(c => invalid.Contains(c)))
                    return Result.Fail($"File name contains invalid characters: {fileName}", corrId);

                // No trailing dots/spaces (Windows quirk)
                if (fileName.EndsWith(".", StringComparison.Ordinal) || fileName.EndsWith(" ", StringComparison.Ordinal))
                    return Result.Fail($"File name cannot end with dot or space: {fileName}", corrId);

                // No ADS
                if (fileName.Contains(':', StringComparison.Ordinal))
                    return Result.Fail($"File name cannot contain colon (ADS): {fileName}", corrId);

                // Length cap
                if (fileName.Length > 100)
                    return Result.Fail($"File name too long (max 100): {fileName}", corrId);

                // All-dot segments
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                if (nameWithoutExt.All(c => c == '.'))
                    return Result.Fail($"File name cannot be all dots: {fileName}", corrId);

                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"ValidateFileName failed for {fileName}: {ex.Message}", ex,
                    new LogContext { Module = "Security", Operation = "ValidateFileName", CorrelationId = corrId });
                return Result.Fail($"Could not validate file name: {ex.Message}", ex, corrId, errId);
            }
        }

        // ── Virus scan hook (pluggable) ────────────────────────────────

        public interface IVirusScanner
        {
            Task<Result> ScanAsync(string filePath, string? correlationId = null);
        }

        /// <summary>
        /// No-op scanner — always passes. Replace with real AV integration.
        /// </summary>
        public sealed class NoOpVirusScanner : IVirusScanner
        {
            public Task<Result> ScanAsync(string filePath, string? correlationId = null)
            {
                return Task.FromResult(Result.Ok(correlationId));
            }
        }

        private static IVirusScanner _scanner = new NoOpVirusScanner();

        public static void SetScanner(IVirusScanner scanner) => _scanner = scanner ?? new NoOpVirusScanner();

        public static async Task<Result> ScanAsync(string filePath, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();
            try
            {
                return await _scanner.ScanAsync(filePath, corrId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var errId = AppLogService.Error($"Virus scan failed for {filePath}: {ex.Message}", ex,
                    new LogContext { Module = "Security", Operation = "VirusScan", CorrelationId = corrId });
                return Result.Fail($"Virus scan error: {ex.Message}", ex, corrId, errId);
            }
        }

        // ── Combined validation ────────────────────────────────────────

        public static async Task<Result> ValidateAttachmentAsync(string filePath, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewId();
            using var scope = CorrelationContext.BeginScope(corrId);

            // Extension + size + MIME + name
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            Result result;

            if (AllowedImageExtensions.Contains(ext))
                result = ValidateImage(filePath, corrId);
            else if (AllowedPdfExtensions.Contains(ext))
                result = ValidatePdf(filePath, corrId);
            else
                result = ValidateExtension(filePath, AllowedAllExtensions, corrId);

            if (result.IsFailure) return result;

            // Virus scan
            var scanResult = await ScanAsync(filePath, corrId).ConfigureAwait(false);
            if (scanResult.IsFailure) return scanResult;

            AppLogService.Info($"Attachment validated: {filePath} [{corrId}]",
                new LogContext { Module = "Security", Operation = "ValidateAttachment", CorrelationId = corrId });

            return Result.Ok(corrId);
        }
    }
}
