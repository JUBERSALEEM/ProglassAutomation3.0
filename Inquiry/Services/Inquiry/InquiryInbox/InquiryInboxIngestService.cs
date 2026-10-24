// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxIngestService.cs
// MODULE: Inquiry — turn a downloaded website delivery into a local
// InquiryRecord, idempotently.
//
// Field mapping (locked with the owner):
//   company_name            → CustomerName      (fallback contact_name)
//   project_location        → CustomerLocation
//   project_name            → ProjectName       (fallback "Website Inquiry {ref}")
//   description             → ProjectDescription
//   salesman_reference      → SalesmanName
//   primary estimator       → PreparedByName    (displayName from the
//   handover_estimator_name     assignment snapshot, fallback
//                               handover name)
//   inquiry_type            → InquiryReference  ("Website · {type}")
//   submission_reference    → WebSubmissionReference
//   submission_id           → WebSubmissionId
//   status/priority/value   → Received / Normal / 0
//   submitted_at            → ReceivedAt
//
// A "Website" communication entry preserves the handoff context, and
// every document is copied through the normal attachment service (same
// allowlist + storage as manual attachments). Idempotency: the ledger
// row links LocalInquiryId — re-ingesting returns the existing record
// and never duplicates it.
// ═══════════════════════════════════════════════════════════════════════

using System.IO;
using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Services.Database;

namespace ProGlassAutomation.Services.Inquiry.InquiryInbox
{
    public sealed class InquiryInboxIngestResult
    {
        public bool Success { get; init; }
        public bool AlreadyIngested { get; init; }
        public int? InquiryId { get; init; }
        public string? InquiryNumber { get; init; }
        public int AttachmentCount { get; init; }
        public string? Error { get; init; }
    }

    public sealed class InquiryInboxIngestService
    {
        private readonly IInquiryRepository _repository;
        private readonly IInquiryWorkflowService _workflow;
        private readonly InquiryAttachmentService _attachments;
        private readonly InquiryInboxService _inbox;

        public InquiryInboxIngestService(
            IInquiryRepository repository,
            IInquiryWorkflowService workflow,
            InquiryAttachmentService attachments,
            InquiryInboxService inbox)
        {
            _repository = repository;
            _workflow = workflow;
            _attachments = attachments;
            _inbox = inbox;
        }

        public async Task<InquiryInboxIngestResult> IngestAsync(string submissionId, CancellationToken ct = default)
        {
            try
            {
                var result = await IngestCoreAsync(submissionId, ct).ConfigureAwait(false);
                if (!result.Success) MarkImportPending(submissionId, result.Error ?? "Import did not complete.");
                return result;
            }
            catch (Exception ex)
            {
                var message = DescribeException(ex);
                MarkImportPending(submissionId, message);
                return Fail(message);
            }
        }

        private async Task<InquiryInboxIngestResult> IngestCoreAsync(string submissionId, CancellationToken ct)
        {
            using (var db = new InquiryDbContext())
            {
                var item = db.InboxItems.AsNoTracking().FirstOrDefault(i => i.SubmissionId == submissionId);
                if (item == null)
                    return Fail("This inbox item is not in the local ledger.");
                if (item.State == InquiryInboxStates.Ingested && item.LocalInquiryId != null)
                    return new InquiryInboxIngestResult
                    {
                        Success = true,
                        AlreadyIngested = true,
                        InquiryId = item.LocalInquiryId,
                        InquiryNumber = item.LocalInquiryNumber
                    };
            }

            var folder = _inbox.GetFolderPath(submissionId);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return Fail("The downloaded folder is missing from the inbox. Check the connection and sync again.");

            // Verify integrity again right before ingest (fail closed).
            var (manifest, _) = InquiryInboxService.ReadReadyFolder(folder);
            var plan = System.Text.Json.JsonSerializer.Deserialize<InboxSubmissionPlan>(manifest.SubmissionRaw, InboxJson.Options);
            if (plan == null)
                return Fail("The inbox manifest is unreadable.");

            var estimator = plan.PrimaryEstimatorDisplay();
            if (string.IsNullOrWhiteSpace(estimator)) estimator = plan.HandoverEstimatorName ?? "";

            var record = new InquiryRecord
            {
                CustomerName = FirstNonEmpty(plan.CompanyName, plan.ContactName, "Website Customer"),
                CustomerLocation = plan.ProjectLocation ?? "",
                ProjectName = string.IsNullOrWhiteSpace(plan.ProjectName)
                    ? "Website Inquiry " + plan.SubmissionReference
                    : plan.ProjectName,
                ProjectDescription = plan.Description ?? "",
                SalesmanName = plan.SalesmanReference ?? "",
                PreparedByName = estimator,
                InquiryReference = string.IsNullOrWhiteSpace(plan.InquiryType)
                    ? "Website Inquiry"
                    : "Website · " + plan.InquiryType,
                WebSubmissionId = plan.SubmissionId,
                WebSubmissionReference = plan.SubmissionReference,
                Status = InquiryStatuses.Received,
                Priority = "Normal",
                InquiryValue = 0m,
                Notes = BuildNotes(plan),
                ReceivedAt = ParseUtc(plan.SubmittedAt)
            };
            record.Communications.Add(new InquiryCommunication
            {
                EventType = "Website",
                Title = "Received from customer website",
                Description = $"Web submission {plan.SubmissionReference} " +
                    (string.IsNullOrWhiteSpace(plan.InquiryType) ? "" : $"({plan.InquiryType}) ") +
                    (string.IsNullOrWhiteSpace(estimator) ? "" : $"— estimator: {estimator}"),
                OccurredAt = DateTime.UtcNow,
                ActorName = "Customer website"
            });

            var saved = await _workflow.CreateAsync(record, ct).ConfigureAwait(false);

            // Copy every document through the normal attachment pipeline.
            var copied = 0;
            foreach (var doc in manifest.Documents)
            {
                var path = Path.Combine(folder, doc.LocalFile.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) continue;
                try
                {
                    await _attachments.AddAsync(saved.Id, path,
                        string.IsNullOrWhiteSpace(doc.Filename) ? null : doc.Filename, ct)
                        .ConfigureAwait(false);
                    copied++;
                }
                catch { /* one bad attachment must not lose the inquiry itself */ }
            }

            using (var db2 = new InquiryDbContext())
            {
                var item = db2.InboxItems.FirstOrDefault(i => i.SubmissionId == submissionId);
                if (item != null)
                {
                    item.State = InquiryInboxStates.Ingested;
                    item.IngestedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    item.LocalInquiryId = saved.Id;
                    item.LocalInquiryNumber = saved.InquiryNumber;
                    item.Error = "";
                    await db2.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }

            return new InquiryInboxIngestResult
            {
                Success = true,
                InquiryId = saved.Id,
                InquiryNumber = saved.InquiryNumber,
                AttachmentCount = copied
            };
        }

        private static string DescribeException(Exception exception)
        {
            var messages = new List<string>();
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                var message = current.Message?.Trim();
                if (!string.IsNullOrWhiteSpace(message)
                    && (messages.Count == 0 || !string.Equals(messages[^1], message, StringComparison.Ordinal)))
                    messages.Add(message);
            }
            return string.Join(" → ", messages);
        }

        private static void MarkImportPending(string submissionId, string error)
        {
            try
            {
                using var db = new InquiryDbContext();
                var item = db.InboxItems.FirstOrDefault(i => i.SubmissionId == submissionId);
                if (item == null || item.State == InquiryInboxStates.Ingested) return;
                item.State = InquiryInboxStates.ImportPending;
                item.Error = error.Length > 490 ? error[..490] : error;
                db.SaveChanges();
            }
            catch { /* the durable READY folder remains available for a later re-import */ }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
            return values[^1];
        }

        private static string BuildNotes(InboxSubmissionPlan plan)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(plan.ContactName)) parts.Add("Contact: " + plan.ContactName);
            if (!string.IsNullOrWhiteSpace(plan.Phone)) parts.Add("Phone: " + plan.Phone);
            if (!string.IsNullOrWhiteSpace(plan.CustomerReference)) parts.Add("Customer ref: " + plan.CustomerReference);
            if (!string.IsNullOrWhiteSpace(plan.AttentionName)) parts.Add("Attention: " + plan.AttentionName);
            if (!string.IsNullOrWhiteSpace(plan.VatTrnNumber)) parts.Add("VAT/TRN: " + plan.VatTrnNumber);
            if (!string.IsNullOrWhiteSpace(plan.SubmissionReference))
                parts.Add("Website reference: " + plan.SubmissionReference);
            return string.Join("  ·  ", parts);
        }

        private static DateTime ParseUtc(string iso)
        {
            if (DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var dt))
                return dt;
            return DateTime.UtcNow;
        }

        private static InquiryInboxIngestResult Fail(string error) =>
            new() { Success = false, Error = error };
    }
}
