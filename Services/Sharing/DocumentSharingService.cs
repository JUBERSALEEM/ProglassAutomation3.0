// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/DocumentSharingService.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — orchestration (Stage 1)
//
// THE one entry point for sharing. Modules call ShareAsync with an
// already-generated artifact list + context; the service resolves the
// provider, validates, executes, audits, and cleans up. No sharing logic
// lives in ViewModels — they build the context and open the composer,
// which calls here.
//
// FLOW
// ──────────────
//   validate artifacts (exist/locked — re-proven at execution time, not
//   just at composer-open time) → provider.Validate → provider.ShareAsync
//   → audit (initiated + outcome, no message content, masked contact) →
//   temp sweep best-effort.
//
// Never throws: every failure surfaces as ShareOutcome.Failed with a
// user-facing reason. Cancellation is honored before launch; after an
// external app opens, the outcome stands (documented on the provider).
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.Sharing.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Sharing
{
    public interface IDocumentSharingService
    {
        /// <summary>Providers enabled in settings, in display order.</summary>
        IReadOnlyList<ISharingProvider> EnabledProviders { get; }

        ISharingProvider? FindProvider(string? providerId);

        /// <summary>Records "composer opened" (called by the composer VM).</summary>
        void LogComposerOpened(ShareContext context);

        Task<ShareOutcome> ShareAsync(
            string providerId,
            ShareContext context,
            IReadOnlyList<ShareableArtifact> artifacts,
            ShareRecipient? recipient,
            string message,
            string subject,
            CancellationToken cancellationToken = default);
    }

    public sealed class DocumentSharingService : IDocumentSharingService
    {
        private readonly List<ISharingProvider> _providers;

        public DocumentSharingService() : this(null)
        {
        }

        /// <summary>
        /// Settings override seam (tests / future DI). Null = App.Config.
        /// </summary>
        public DocumentSharingService(SharingSettings? settings)
        {
            SharingSettings effective;
            try { effective = settings ?? App.Config?.Sharing ?? new SharingSettings(); }
            catch { effective = new SharingSettings(); }

            string cc;
            try { cc = effective.DefaultCountryCode ?? "971"; }
            catch { cc = "971"; }

            long emailWarnBytes;
            try { emailWarnBytes = Math.Max(1, effective.EmailLargeAttachmentMb) * 1024L * 1024L; }
            catch { emailWarnBytes = 20L * 1024 * 1024; }

            var all = new List<ISharingProvider>
            {
                new WhatsAppShareProvider(cc),
                new EmailShareProvider(emailWarnBytes),
                new SystemFileShareProvider()
            };

            try
            {
                _providers = all
                    .Where(p => effective.IsProviderEnabled(p.Id))
                    .ToList();
                if (_providers.Count == 0)
                    _providers = all; // never strand the user with zero providers
            }
            catch
            {
                _providers = all;
            }
        }

        public IReadOnlyList<ISharingProvider> EnabledProviders => _providers;

        public ISharingProvider? FindProvider(string? providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return null;
            return _providers.FirstOrDefault(p =>
                string.Equals(p.Id, providerId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public void LogComposerOpened(ShareContext context)
        {
            try
            {
                AuditService.Log(
                    AuditSections.Sharing, "Share composer opened",
                    DescribeContext(context),
                    context?.DocumentTypeKey ?? "",
                    context?.DocumentNumber ?? "");
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Sharing] composer-open audit failed: {ex.Message}");
            }
        }

        public async Task<ShareOutcome> ShareAsync(
            string providerId,
            ShareContext context,
            IReadOnlyList<ShareableArtifact> artifacts,
            ShareRecipient? recipient,
            string message,
            string subject,
            CancellationToken cancellationToken = default)
        {
            ISharingProvider? provider = FindProvider(providerId);
            if (provider == null)
                return ShareOutcome.Failed(providerId ?? "",
                    "That sharing method is not available.",
                    "unknown/disabled provider");

            if (cancellationToken.IsCancellationRequested)
                return ShareOutcome.Cancelled(provider.Id);

            // Re-prove the files at execution time: the composer may have
            // sat open while the operator moved or deleted the export.
            if (artifacts == null || artifacts.Count == 0)
                return ShareOutcome.Failed(provider.Id,
                    "No file was generated to share.", "no artifacts");

            foreach (var artifact in artifacts)
            {
                if (artifact == null)
                    return ShareOutcome.Failed(provider.Id,
                        "One of the files is missing. Generate it again, then share.",
                        "null artifact");
                try
                {
                    if (!System.IO.File.Exists(artifact.FilePath))
                        return ShareOutcome.Failed(provider.Id,
                            $"The file '{artifact.FileName}' no longer exists. " +
                            "Generate it again, then share.",
                            "file vanished: " + artifact.FileName);
                }
                catch (Exception ex)
                {
                    AppLogService.Debug($"[Sharing] pre-flight failed: {ex.Message}");
                    return ShareOutcome.Failed(provider.Id,
                        $"The file '{artifact.FileName}' cannot be accessed.",
                        "pre-flight exception");
                }
            }

            LogInitiated(provider, context, recipient, artifacts);

            ShareOutcome outcome;
            try
            {
                var request = new ShareExecutionRequest(
                    context, artifacts, recipient,
                    message ?? "", subject ?? "", cancellationToken);
                outcome = await provider.ShareAsync(request).ConfigureAwait(false)
                    ?? ShareOutcome.Failed(provider.Id,
                        "The sharing method returned no result.", "null outcome");
            }
            catch (OperationCanceledException)
            {
                outcome = ShareOutcome.Cancelled(provider.Id);
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Sharing] provider threw: {ex.Message}");
                outcome = ShareOutcome.Failed(provider.Id,
                    $"Share failed: {ex.Message}", "provider exception");
            }

            LogOutcome(provider, context, recipient, outcome);

            // Crash-tolerant temp hygiene (best-effort, never blocks).
            try
            {
                int hours = 24;
                try { hours = App.Config?.Sharing?.TempRetentionHours ?? 24; }
                catch (Exception ex)
                {
                    AppLogService.Debug($"[Sharing] retention read failed: {ex.Message}");
                }
                if (hours < 1) hours = 1;
                ShareTempFiles.SweepStale(TimeSpan.FromHours(hours));
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Sharing] sweep failed: {ex.Message}");
            }

            return outcome;
        }

        // ── Audit (no message content, masked contact) ────────────────

        private static void LogInitiated(
            ISharingProvider provider, ShareContext? context,
            ShareRecipient? recipient, IReadOnlyList<ShareableArtifact> artifacts)
        {
            try
            {
                string files = string.Join(", ",
                    artifacts.Select(a => a?.FileName ?? "?"));
                string to = recipient != null ? $" → {recipient.AuditIdentity}" : "";
                AuditService.Log(
                    AuditSections.Sharing, $"Share initiated ({provider.DisplayName})",
                    $"{DescribeContext(context)} → {provider.DisplayName}{to}; files: {files}",
                    context?.DocumentTypeKey ?? "",
                    context?.DocumentNumber ?? "");
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Sharing] initiated audit failed: {ex.Message}");
            }
        }

        private static void LogOutcome(
            ISharingProvider provider, ShareContext? context,
            ShareRecipient? recipient, ShareOutcome outcome)
        {
            try
            {
                string action = outcome.Status switch
                {
                    ShareOutcomeStatus.Completed => "Share completed",
                    ShareOutcomeStatus.ExternalAppOpened => "External app opened",
                    ShareOutcomeStatus.Cancelled => "Share cancelled",
                    _ => "Share failed"
                };
                string detail = outcome.Status == ShareOutcomeStatus.Failed
                    ? outcome.ErrorMessage
                    : outcome.AuditDetail;
                string to = recipient != null ? $" → {recipient.AuditIdentity}" : "";
                AuditService.Log(
                    AuditSections.Sharing,
                    $"{action} ({provider.DisplayName})",
                    $"{DescribeContext(context)}{to}; {detail}".Trim(),
                    context?.DocumentTypeKey ?? "",
                    context?.DocumentNumber ?? "");
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Sharing] outcome audit failed: {ex.Message}");
            }
        }

        private static string DescribeContext(ShareContext? context)
        {
            if (context == null) return "document";
            string title = context.DisplayTitle ?? "document";
            if (!string.IsNullOrWhiteSpace(context.CustomerName))
                title += $" for {context.CustomerName.Trim()}";
            return title;
        }
    }
}
