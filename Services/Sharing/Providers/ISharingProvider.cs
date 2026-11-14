// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/Providers/ISharingProvider.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — provider abstraction (Stage 1)
//
// ONE provider per channel (WhatsApp, Email, SystemFile…), each handling
// EVERY artifact format. A provider that only accepts some formats says so
// in its Capabilities, and the composer enables/disables from that —
// never by provider-per-format classes.
//
// HONEST OUTCOMES (the rule the whole center stands on)
// ──────────────
// External-app handoffs (wa.me, mailto) report ExternalAppOpened — the app
// was launched with prefilled content, and nothing more is known. Only a
// provider that finishes the work inside our process reports Completed.
// "Sent"/"Delivered"/"Read" are never claimed unless a provider's API
// explicitly supplies them (none of the free/local ones do).
// ═══════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Sharing.Providers
{
    /// <summary>Well-known provider ids (stable; stored in settings).</summary>
    public static class SharingProviderIds
    {
        public const string WhatsApp = "WHATSAPP";
        public const string Email = "EMAIL";
        public const string SystemFile = "SYSTEMFILE";
    }

    /// <summary>What a provider can do — the composer binds to this.</summary>
    public sealed class ProviderCapabilities
    {
        /// <summary>Prefilled text/message body.</summary>
        public bool SupportsText { get; init; }

        /// <summary>Subject line (email).</summary>
        public bool SupportsSubject { get; init; }

        /// <summary>Needs a TO recipient (chat/email target).</summary>
        public bool RequiresRecipient { get; init; }

        /// <summary>Needs the recipient's phone.</summary>
        public bool RequiresPhone { get; init; }

        /// <summary>Needs the recipient's email.</summary>
        public bool RequiresEmail { get; init; }

        /// <summary>Hands the file itself to the target (vs. reveal-beside).</summary>
        public bool AttachesFile { get; init; }

        /// <summary>More than one artifact in one share.</summary>
        public bool SupportsMultipleAttachments { get; init; }

        /// <summary>True when the provider finishes in-process (Completed).</summary>
        public bool CompletesInProcess { get; init; }
    }

    /// <summary>Honest outcome of one share execution.</summary>
    public enum ShareOutcomeStatus
    {
        /// <summary>Work finished inside our process (file revealed/copied).</summary>
        Completed,

        /// <summary>External app launched with prefilled content. NOT sent.</summary>
        ExternalAppOpened,

        /// <summary>Operator cancelled before launch.</summary>
        Cancelled,

        /// <summary>Failed with <see cref="ShareOutcome.ErrorMessage"/>.</summary>
        Failed
    }

    /// <summary>Provider result + user-facing follow-up guidance.</summary>
    public sealed class ShareOutcome
    {
        public ShareOutcomeStatus Status { get; }
        public string ProviderId { get; }

        /// <summary>Short result line for the composer ("Opened in WhatsApp").</summary>
        public string Message { get; }

        /// <summary>
        /// Follow-up guidance ("Attach PI-2026-001.pdf from the opened
        /// folder"). Empty when nothing further is needed.
        /// </summary>
        public string FollowUp { get; }

        /// <summary>Failure reason (Failed only).</summary>
        public string ErrorMessage { get; }

        /// <summary>Audit-safe detail (no message content, masked contact).</summary>
        public string AuditDetail { get; }

        private ShareOutcome(
            ShareOutcomeStatus status, string providerId, string message,
            string followUp = "", string error = "", string auditDetail = "")
        {
            Status = status;
            ProviderId = providerId;
            Message = message;
            FollowUp = followUp;
            ErrorMessage = error;
            AuditDetail = auditDetail;
        }

        public static ShareOutcome Completed(
            string providerId, string message, string auditDetail = "") =>
            new(ShareOutcomeStatus.Completed, providerId, message, "", "", auditDetail);

        public static ShareOutcome ExternalAppOpened(
            string providerId, string message, string followUp,
            string auditDetail = "") =>
            new(ShareOutcomeStatus.ExternalAppOpened, providerId, message, followUp, "", auditDetail);

        public static ShareOutcome Cancelled(string providerId) =>
            new(ShareOutcomeStatus.Cancelled, providerId, "Share cancelled.");

        public static ShareOutcome Failed(
            string providerId, string error, string auditDetail = "") =>
            new(ShareOutcomeStatus.Failed, providerId, "Share failed.", "", error, auditDetail);

        public bool IsSuccess =>
            Status == ShareOutcomeStatus.Completed
            || Status == ShareOutcomeStatus.ExternalAppOpened;
    }

    /// <summary>Everything a provider needs for one execution.</summary>
    public sealed class ShareExecutionRequest
    {
        public ShareContext Context { get; }
        public IReadOnlyList<ShareableArtifact> Artifacts { get; }
        public ShareRecipient? Recipient { get; }
        public string Message { get; }
        public string Subject { get; }
        public CancellationToken CancellationToken { get; }

        public ShareExecutionRequest(
            ShareContext context,
            IReadOnlyList<ShareableArtifact> artifacts,
            ShareRecipient? recipient,
            string message,
            string subject,
            CancellationToken cancellationToken)
        {
            Context = context;
            Artifacts = artifacts;
            Recipient = recipient;
            Message = message ?? "";
            Subject = subject ?? "";
            CancellationToken = cancellationToken;
        }
    }

    /// <summary>One sharing channel. Format-agnostic by contract.</summary>
    public interface ISharingProvider
    {
        /// <summary>Stable id (SharingProviderIds.*).</summary>
        string Id { get; }

        /// <summary>Display name (localized at the composer).</summary>
        string DisplayName { get; }

        ProviderCapabilities Capabilities { get; }

        /// <summary>
        /// Validates the request for THIS provider (recipient present?
        /// phone usable? files exist?). Returns "" when valid, else the
        /// user-facing reason. Runs before ShareAsync and by the composer
        /// to enable/disable.
        /// </summary>
        string Validate(ShareExecutionRequest request);

        /// <summary>
        /// Executes the share. Checks cancellation before launching; after
        /// an external app is launched the outcome is reported as-is.
        /// Never throws — failures return ShareOutcome.Failed.
        /// </summary>
        Task<ShareOutcome> ShareAsync(ShareExecutionRequest request);
    }
}
