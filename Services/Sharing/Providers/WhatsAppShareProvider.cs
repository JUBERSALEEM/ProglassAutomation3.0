// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/Providers/WhatsAppShareProvider.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — WhatsApp provider (Stage 1, Stage 3 upgrade)
//
// LEGITIMATE HANDOFF ONLY. This opens the machine's WhatsApp on the
// recipient's chat with the message prefilled. No scraping, no DOM
// automation, no session hijack, no paid API, no third-party upload.
//
// CHANNEL SELECTION (Sept 2026 senior audit — "WhatsApp never opened")
// ────────────────────────────────────────────────────────────────────
//   Delegates the actual launch to WhatsAppLauncher, which deep-links
//   into whichever WhatsApp the machine has, on the recipient's chat:
//     1. WhatsApp Desktop  (whatsapp://send)   — installed, logged-in app
//     2. WhatsApp Web      (web.whatsapp.com/send) — logged-in web chat
//     3. wa.me                                   — universal last resort
//   The previous bare wa.me link opened a browser INTERSTITIAL on desktop
//   (not a chat), so operators saw only the Explorer reveal and thought
//   sharing was broken. It now lands directly in the chat.
//
// HOW THE FILE ATTACHES (Sept 2026 senior audit — "nothing attaches")
// ──────────────
// WhatsApp click-to-chat links carry TEXT ONLY — no attachment parameter
// exists on ANY channel. So the provider stages the artifact(s) and puts
// them on the CLIPBOARD (CF_HDROP) before opening the chat: the operator
// presses Ctrl+V in the chat and the file attaches (WhatsApp Desktop and
// Web both accept a pasted file). If the clipboard is unavailable it falls
// back to revealing the file in Explorer for drag-and-drop. Either way the
// outcome is ExternalAppOpened — never "Sent"; delivery/read status is
// unknowable and never claimed.
//
// FROM/TO HONESTY. FROM is the ProGlass user identity shown in OUR
// composer for context; the WhatsApp account that actually sends is
// whichever is logged in on the machine, and the UI says so.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.Security;
using ProGlassAutomation.Services.Sharing.Contacts;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Sharing.Providers
{
    public sealed class WhatsAppShareProvider : ISharingProvider
    {
        public string Id => SharingProviderIds.WhatsApp;
        public string DisplayName => "WhatsApp";

        public ProviderCapabilities Capabilities { get; } = new()
        {
            SupportsText = true,
            SupportsSubject = false,
            RequiresRecipient = true,
            RequiresPhone = true,
            RequiresEmail = false,
            AttachesFile = false, // wa.me is text-only; file is revealed beside
            SupportsMultipleAttachments = true, // staged + listed; user attaches
            CompletesInProcess = false
        };

        private readonly string _defaultCountryCode;

        public WhatsAppShareProvider(string defaultCountryCode = "971")
        {
            _defaultCountryCode = string.IsNullOrWhiteSpace(defaultCountryCode)
                ? "971" : defaultCountryCode.Trim();
        }

        public string Validate(ShareExecutionRequest request)
        {
            if (request == null) return "Nothing to share.";
            if (request.Artifacts == null || request.Artifacts.Count == 0)
                return "No file was generated to share.";
            if (request.CancellationToken.IsCancellationRequested)
                return "Share cancelled.";

            if (request.Recipient == null)
                return "Choose who should receive this document.";
            if (string.IsNullOrWhiteSpace(request.Recipient.Phone))
                return $"No phone number on file for {request.Recipient.Name}. " +
                       "Pick another contact or enter one manually.";

            string digits = PhoneNumbers.NormalizeValidated(
                request.Recipient.Phone, _defaultCountryCode);
            if (digits.Length == 0)
                return $"The phone number for {request.Recipient.Name} " +
                       $"('{request.Recipient.Phone.Trim()}') is not a valid " +
                       "WhatsApp number. Enter it in full international form " +
                       $"(e.g. +{_defaultCountryCode} 50 123 4567) or pick another contact.";

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

                string digits = PhoneNumbers.NormalizeValidated(
                    request.Recipient!.Phone, _defaultCountryCode);

                // Stage copies (originals may live outside allowed roots;
                // the staged folder is always revealable).
                string folder = ShareTempFiles.CreateShareFolder();
                if (folder.Length == 0)
                    return ShareOutcome.Failed(Id,
                        "Could not prepare the attachment (temporary folder unavailable).",
                        "temp folder unavailable");

                var staged = request.Artifacts
                    .Select(a => ShareTempFiles.StageCopy(a, folder))
                    .Where(p => p.Length > 0)
                    .ToList();
                if (staged.Count == 0)
                    return ShareOutcome.Failed(Id,
                        "Could not prepare the attachment.",
                        "stage copy failed");

                if (request.CancellationToken.IsCancellationRequested)
                    return ShareOutcome.Cancelled(Id);

                string text = request.Message ?? "";
                if (text.Length == 0)
                    text = $"Please find attached: {request.Artifacts[0].FileName}";

                // Put the staged file(s) on the clipboard BEFORE opening the
                // chat so the operator can paste them straight in with
                // Ctrl+V — WhatsApp Desktop and Web both accept a pasted
                // file as an attachment. This is the real "attach" step;
                // no WhatsApp link can carry a file itself.
                bool copied = ShareClipboard.CopyFiles(staged);

                // Deep-link into whichever WhatsApp the machine has, on the
                // recipient's chat: Desktop app → logged-in Web → wa.me.
                var launched = await Task.Run(() =>
                    WhatsAppLauncher.OpenChat(digits, text)).ConfigureAwait(false);
                if (!launched.IsSuccess)
                    return ShareOutcome.Failed(Id,
                        "WhatsApp could not be opened. " +
                        (launched.Error.Length > 0 ? launched.Error
                            : "Install WhatsApp Desktop, or sign in to WhatsApp Web in your browser, and try again."),
                        "launch failed: " + launched.Error);

                // When the clipboard copy failed, fall back to revealing the
                // staged file(s) in Explorer so the operator can still drag
                // them in. Its failure degrades, never fails, the share.
                if (!copied)
                {
                    try
                    {
                        ProcessStartGuard.TryOpenExplorerSelect(staged[0]);
                    }
                    catch (Exception ex)
                    {
                        AppLogService.Debug($"[Sharing.WhatsApp] reveal failed: {ex.Message}");
                    }
                }

                string files = string.Join(", ",
                    request.Artifacts.Select(a => a.FileName));
                string howToAttach = copied
                    ? $"Your message is prefilled in {launched.ChannelName} — " +
                      $"press Ctrl+V to attach {files}, then send. " +
                      "(Nothing was sent automatically.)"
                    : $"Your message is prefilled in {launched.ChannelName}. " +
                      $"WhatsApp links can't carry files, so drag {files} " +
                      "from the folder that just opened into the chat, then send.";

                return ShareOutcome.ExternalAppOpened(
                    Id,
                    $"Opened {launched.ChannelName} chat with {request.Recipient.Name}.",
                    howToAttach,
                    $"via {launched.ChannelName} to {request.Recipient.AuditIdentity}; " +
                    $"files: {files}; clipboard: {(copied ? "yes" : "no")}");
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Sharing.WhatsApp] failed: {ex.Message}");
                return ShareOutcome.Failed(Id,
                    $"WhatsApp share failed: {ex.Message}", "exception");
            }
        }
    }
}
