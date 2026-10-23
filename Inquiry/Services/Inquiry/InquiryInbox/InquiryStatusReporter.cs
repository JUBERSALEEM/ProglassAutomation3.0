// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryInbox/InquiryStatusReporter.cs
// MODULE: Inquiry — optional status write-back to the customer website.
//
// Best-effort, opt-in (InquiryInboxSettings.ReportStatusToWebsite,
// default OFF). When enabled, a local workflow transition is mirrored
// to the website's submission row through the RPC
// update_customer_inquiry_status_v1 so the website's reports/chat pages
// can show what the desktop did with the inquiry.
//
// Remote failures NEVER block, delay or fail the local workflow — the
// desktop register is the source of truth; the website is a view.
// The website-side contract (columns + RPC) ships in
// docs/website-inquiry-status-writeback.sql.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;

namespace ProGlassAutomation.Services.Inquiry.InquiryInbox
{
    public sealed class InquiryStatusReporter
    {
        private readonly InquiryInboxSettings _settings;

        public InquiryStatusReporter(InquiryInboxSettings settings)
            => _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        /// <summary>
        /// Mirrors one record's current status to the website. Returns an
        /// error message when nothing was reported (or the call failed);
        /// null on success. Never throws.
        /// </summary>
        public async Task<string?> ReportAsync(InquiryRecord record, CancellationToken ct = default)
        {
            try
            {
                if (record is null || record.Id <= 0) return "No saved inquiry to report.";
                if (string.IsNullOrWhiteSpace(record.WebSubmissionId)) return null; // not a website inquiry — nothing to do
                if (!_settings.IsConfigured) return null; // not configured — stay silent
                if (!_settings.ReportStatusToWebsite) return null; // feature off — stay silent

                using var client = new InquiryInboxClient(_settings);
                await client.UpdateInquiryStatusAsync(
                    record.WebSubmissionId,
                    WireStatus(record.Status),
                    record.InquiryNumber,
                    ct).ConfigureAwait(false);
                return null;
            }
            catch (InboxRemoteException ex)
            {
                return ex.Kind == InboxRemoteErrorKind.Offline
                    ? "Status not reported (offline)."
                    : "Status not reported: " + ex.Message;
            }
            catch (Exception ex)
            {
                return "Status not reported: " + ex.Message;
            }
        }

        /// <summary>Local status vocabulary → website wire key (lowercase).</summary>
        public static string WireStatus(string status)
        {
            switch (InquiryStatuses.Normalize(status))
            {
                case InquiryStatuses.Prepared: return "prepared";
                case InquiryStatuses.Sent: return "sent";
                case InquiryStatuses.Confirmed: return "confirmed";
                case InquiryStatuses.Hold: return "hold";
                case InquiryStatuses.Cancelled: return "cancelled";
                default: return "received";
            }
        }
    }
}
