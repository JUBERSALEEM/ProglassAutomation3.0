// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/Providers/SystemFileShareProvider.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — system file provider (Stage 1)
//
// The honest in-process provider: stages the artifact(s) and reveals them
// in Explorer (first file selected), ready for the operator to drag into
// ANY app — Teams, a browser upload, a USB stick. No recipient needed, no
// external account involved, works offline. This is the provider that CAN
// report Completed, and the fallback when no contact exists.
//
// (The Windows Share contract / DataTransferManager is a UWP API; hosting
// it from desktop WPF needs fragile interop for little gain over "reveal
// + drag". If a future Windows App SDK share sheet becomes viable, it
// lands here as a second mode — same Id, same audit shape.)
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.Security;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Sharing.Providers
{
    public sealed class SystemFileShareProvider : ISharingProvider
    {
        public string Id => SharingProviderIds.SystemFile;
        public string DisplayName => "Show File";

        public ProviderCapabilities Capabilities { get; } = new()
        {
            SupportsText = false,
            SupportsSubject = false,
            RequiresRecipient = false,
            RequiresPhone = false,
            RequiresEmail = false,
            AttachesFile = true, // the file itself is handed to Explorer
            SupportsMultipleAttachments = true,
            CompletesInProcess = true
        };

        public string Validate(ShareExecutionRequest request)
        {
            if (request == null) return "Nothing to share.";
            if (request.Artifacts == null || request.Artifacts.Count == 0)
                return "No file was generated to share.";
            if (request.CancellationToken.IsCancellationRequested)
                return "Share cancelled.";
            return "";
        }

        public async Task<ShareOutcome> ShareAsync(ShareExecutionRequest request)
        {
            try
            {
                string invalid = Validate(request);
                if (invalid.Length > 0)
                    return ShareOutcome.Failed(Id, invalid, "validation: " + invalid);
                if (request.CancellationToken.IsCancellationRequested)
                    return ShareOutcome.Cancelled(Id);

                string folder = ShareTempFiles.CreateShareFolder();
                if (folder.Length == 0)
                    return ShareOutcome.Failed(Id,
                        "Could not prepare the file (temporary folder unavailable).",
                        "temp folder unavailable");

                var staged = request.Artifacts
                    .Select(a => ShareTempFiles.StageCopy(a, folder))
                    .Where(p => p.Length > 0)
                    .ToList();
                if (staged.Count == 0)
                    return ShareOutcome.Failed(Id,
                        "Could not prepare the file.",
                        "stage copy failed");

                if (request.CancellationToken.IsCancellationRequested)
                    return ShareOutcome.Cancelled(Id);

                // Copy to the clipboard so the file(s) can be pasted (Ctrl+V)
                // straight into any app, in addition to being revealed for
                // drag-and-drop.
                bool copied = ShareClipboard.CopyFiles(staged);

                var revealed = await Task.Run(() =>
                    ProcessStartGuard.TryOpenExplorerSelect(staged[0]))
                    .ConfigureAwait(false);
                if (!revealed.IsSuccess && !copied)
                    return ShareOutcome.Failed(Id,
                        "The file could not be shown. " + revealed.Error,
                        "reveal failed: " + revealed.Error);

                string files = string.Join(", ",
                    request.Artifacts.Select(a => a.FileName));
                string ready = copied
                    ? (staged.Count == 1
                        ? $"Ready: {request.Artifacts[0].FileName} — press Ctrl+V to paste it into any app, or drag it from the folder."
                        : $"Ready: {staged.Count} files — press Ctrl+V to paste them into any app, or drag them from the folder.")
                    : (staged.Count == 1
                        ? $"Ready: {request.Artifacts[0].FileName} — drag it into any app."
                        : $"Ready: {staged.Count} files — drag them into any app.");

                return ShareOutcome.Completed(
                    Id,
                    ready,
                    $"files: {files}; clipboard: {(copied ? "yes" : "no")}");
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Sharing.SystemFile] failed: {ex.Message}");
                return ShareOutcome.Failed(Id,
                    $"File share failed: {ex.Message}", "exception");
            }
        }
    }
}
