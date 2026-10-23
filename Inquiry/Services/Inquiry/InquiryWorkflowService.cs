// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryWorkflowService.cs
// MODULE: Inquiry — guarded status transitions + write operations.
//
// Ported from the legacy build. The status machine is the contract:
//
//   Received  → Prepared | Hold | Cancelled
//   Prepared  → Sent | Hold | Cancelled
//   Sent      → Confirmed | Hold | Cancelled
//   Hold      → Prepared | Cancelled
//   Confirmed → Cancelled
//
// Every transition goes through the repository with an optimistic
// RowVersion token, so a stale editor can never clobber a newer save.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Services.Audit;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Inquiry
{
    public interface IInquiryWorkflowService
    {
        Task<InquiryRecord> CreateAsync(InquiryRecord inquiry, CancellationToken ct = default);
        Task<InquiryRecord> UpdateAsync(
            InquiryRecord inquiry, int expectedRowVersion, CancellationToken ct = default);
        Task<bool> ChangeStatusAsync(
            int id, string current, string next, int expectedRowVersion,
            CancellationToken ct = default);

        /// <summary>
        /// Clone an existing inquiry as a brand new draft. The clone
        /// gets a fresh number and Status=Received; the source is
        /// unchanged.
        /// </summary>
        Task<InquiryRecord> DuplicateAsync(int sourceId, CancellationToken ct = default);

        /// <summary>
        /// Permanently delete an inquiry that has no downstream
        /// documents. Refuses to delete when a PI / JO / DN / TI /
        /// Statement exists; the caller should cancel the inquiry
        /// instead.
        /// </summary>
        Task<DeleteInquiryResult> DeleteAsync(int id, CancellationToken ct = default);

        /// <summary>
        /// Update the priority field only. Useful for the right-click
        /// "Change priority" sub-menu.
        /// </summary>
        Task<bool> SetPriorityAsync(int id, string priority, CancellationToken ct = default);
    }

    public sealed class InquiryWorkflowService : IInquiryWorkflowService
    {
        private readonly IInquiryRepository _repository;
        public InquiryWorkflowService(IInquiryRepository repository) => _repository = repository;

        public async Task<InquiryRecord> CreateAsync(InquiryRecord inquiry, CancellationToken ct = default)
        {
            Validate(inquiry);
            inquiry.PreparedByName = string.IsNullOrWhiteSpace(inquiry.PreparedByName)
                ? CurrentOperator.DisplayName : inquiry.PreparedByName;
            inquiry.PreparedByRole = string.IsNullOrWhiteSpace(inquiry.PreparedByRole)
                ? "" : inquiry.PreparedByRole;
            inquiry.UpdatedBy = CurrentOperator.Username;
            var saved = await _repository.CreateAsync(inquiry, ct).ConfigureAwait(false);
            return saved;
        }

        public async Task<InquiryRecord> UpdateAsync(
            InquiryRecord inquiry, int expectedRowVersion, CancellationToken ct = default)
        {
            Validate(inquiry);
            inquiry.UpdatedBy = CurrentOperator.Username;
            var saved = await _repository.UpdateAsync(inquiry, expectedRowVersion, ct).ConfigureAwait(false);
            return saved;
        }

        public async Task<bool> ChangeStatusAsync(
            int id, string current, string next, int expectedRowVersion,
            CancellationToken ct = default)
        {
            next = InquiryStatuses.Normalize(next);
            if (!CanTransition(current, next))
                throw new InvalidOperationException($"Inquiry cannot move from {current} to {next}.");
            bool changed = await _repository.ChangeStatusAsync(
                id, next, CurrentOperator.Username, expectedRowVersion, ct).ConfigureAwait(false);
            return changed;
        }

        public async Task<InquiryRecord> DuplicateAsync(int sourceId, CancellationToken ct = default)
        {
            var clone = await _repository.DuplicateAsync(sourceId, ct).ConfigureAwait(false);
            return clone;
        }

        public async Task<DeleteInquiryResult> DeleteAsync(int id, CancellationToken ct = default)
        {
            var result = await _repository.DeleteAsync(id, ct).ConfigureAwait(false);
            return result;
        }

        public async Task<bool> SetPriorityAsync(int id, string priority, CancellationToken ct = default)
        {
            var detail = await _repository.GetDetailAsync(id, ct).ConfigureAwait(false);
            if (detail == null) return false;
            string normalized = NormalizePriority(priority);
            if (string.Equals(detail.Priority, normalized, StringComparison.OrdinalIgnoreCase))
                return true; // idempotent no-op
            detail.Priority = normalized;
            detail.UpdatedBy = CurrentOperator.Username;
            await _repository.UpdateAsync(detail, detail.RowVersion, ct).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// The legal transition matrix. Pure and unit-testable; shared
        /// by the view models (to disable impossible buttons) and by
        /// this service (to refuse impossible writes).
        /// </summary>
        public static bool CanTransition(string current, string next) => InquiryStatuses.Normalize(current) switch
        {
            InquiryStatuses.Received => next is InquiryStatuses.Prepared or InquiryStatuses.Hold or InquiryStatuses.Cancelled,
            InquiryStatuses.Prepared => next is InquiryStatuses.Sent or InquiryStatuses.Hold or InquiryStatuses.Cancelled,
            InquiryStatuses.Sent => next is InquiryStatuses.Confirmed or InquiryStatuses.Hold or InquiryStatuses.Cancelled,
            InquiryStatuses.Hold => next is InquiryStatuses.Prepared or InquiryStatuses.Cancelled,
            InquiryStatuses.Confirmed => next == InquiryStatuses.Cancelled,
            _ => false
        };

        private static string NormalizePriority(string? value)
        {
            string t = (value ?? "").Trim();
            if (t.Length == 0) return "Normal";
            // Allow common short forms: "U"/"H"/"N"/"L".
            return t.ToLowerInvariant() switch
            {
                "urgent" or "u" or "critical" => "Urgent",
                "high" or "h" => "High",
                "low" or "l" => "Low",
                _ => "Normal"
            };
        }

        private static void Validate(InquiryRecord x)
        {
            if (string.IsNullOrWhiteSpace(x.CustomerName)) throw new InvalidOperationException("Customer is required.");
            if (string.IsNullOrWhiteSpace(x.ProjectName)) throw new InvalidOperationException("Project is required.");
            if (x.ReceivedAt == default) x.ReceivedAt = DateTime.Now;
        }
    }
}
