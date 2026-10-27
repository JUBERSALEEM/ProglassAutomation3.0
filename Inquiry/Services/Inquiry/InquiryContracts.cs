// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryContracts.cs
// MODULE: Inquiry — query, projection, statistics and repository
// contracts.
//
// Ported from the legacy build. V3 additions (marked): the two
// distinct-value lookups that feed the list filter dropdowns, which the
// legacy build sourced from the master-data module (not yet ported).
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Services.Reporting;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Inquiry
{
    /// <summary>
    /// Filter definition for the Inquiry register. Values map 1:1 to the
    /// status chips / filter card controls of the list page.
    /// </summary>
    public sealed class InquiryQuery
    {
        public string Search { get; init; } = "";
        /// <summary>"All statuses" disables the status predicate.</summary>
        public string Status { get; init; } = "All statuses";
        public int? SalesmanId { get; init; }
        public string SalesmanName { get; init; } = "";
        public int? PreparedByUserId { get; init; }
        public string PreparedByName { get; init; } = "";
        public DateTime? From { get; init; }
        public DateTime? ToExclusive { get; init; }
        public bool HasAttachmentsOnly { get; init; }
        public string Priority { get; init; } = "";
        public string Sort { get; init; } = "Updated newest";
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 50;

        /// <summary>
        /// Restricts the register to the inquiries one person is part of.
        /// </summary>
        /// <remarks>
        /// Owner instruction (round 24): "user should be able to read the data
        /// related to them only". An inquiry belongs to a person when they are its
        /// salesman or its preparer; an Administrator passes null and reads the
        /// whole book. The repository also applies this as a ceiling for a signed-in
        /// non-administrator, so a caller that forgets cannot widen it.
        /// </remarks>
        public int? ScopeToUserId { get; init; }
    }

    /// <summary>
    /// Database-side projection for register rows. No file bytes, no
    /// tracking.
    /// </summary>
    public sealed class InquiryListProjection
    {
        public int Id { get; init; }
        public string InquiryNumber { get; init; } = "";
        public string InquiryReference { get; init; } = "";
        public string CustomerName { get; init; } = "";
        public string CustomerLocation { get; init; } = "";
        public string ProjectName { get; init; } = "";
        public string ProjectDescription { get; init; } = "";
        public string SalesmanName { get; init; } = "";
        public string PreparedByName { get; init; } = "";
        public string PreparedByRole { get; init; } = "";
        public string Status { get; init; } = "";
        public string Priority { get; init; } = "";
        public decimal InquiryValue { get; init; }
        public string Notes { get; init; } = "";
        public DateTime ReceivedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public string UpdatedBy { get; init; } = "";
        public int AttachmentCount { get; init; }
        public string AttachmentBadgeText { get; set; } = "";
        public int? ProformaInvoiceId { get; init; }
        public string ProformaNumber { get; init; } = "";
        public string JobOrderNumber { get; init; } = "";
        public int RowVersion { get; init; }

        // ── Full-lifecycle projection (hydrated by IInquiryLifecycleService) ──
        // Defaults represent "not started" so a row that fails the
        // lifecycle hydration still renders correctly. The service
        // overwrites these before the page is returned to the UI.
        public InquiryLifecycleSnapshot Lifecycle { get; set; } =
            new InquiryLifecycleSnapshot();
    }

    /// <summary>
    /// Live counts for the status chips. Statistics ignore the status
    /// filter (so chip counts stay stable while a chip is active) but
    /// honour every other filter.
    /// </summary>
    public sealed class InquiryStatistics
    {
        public int All { get; init; }
        public int Received { get; init; }
        public int Prepared { get; init; }
        public int Sent { get; init; }
        public int Confirmed { get; init; }
        public int Hold { get; init; }
        public int Cancelled { get; init; }
        /// <summary>Sum of inquiry values, cancelled rows excluded.</summary>
        public decimal OpenPipelineValue { get; init; }
    }

    public interface IInquiryRepository
    {
        Task InitializeAsync(CancellationToken ct = default);
        Task<PagedResult<InquiryListProjection>> QueryAsync(InquiryQuery query, CancellationToken ct = default);
        Task<InquiryStatistics> GetStatisticsAsync(InquiryQuery query, CancellationToken ct = default);
        /// <summary>
        /// One inquiry with its BOQ. When <paramref name="scopeToUserId"/> is set,
        /// an inquiry that person is not part of comes back as null — the same
        /// rule the register applies, so a record cannot be opened by id alone.
        /// </summary>
        Task<InquiryRecord?> GetDetailAsync(
            int id, CancellationToken ct = default, int? scopeToUserId = null);
        Task<InquiryRecord> CreateAsync(InquiryRecord inquiry, CancellationToken ct = default);
        Task<InquiryRecord> UpdateAsync(
            InquiryRecord inquiry, int expectedRowVersion, CancellationToken ct = default);

        /// <summary>
        /// Replace-all persistence for an inquiry's scope of supply
        /// (BOQ). One atomic commit; a communication entry is written so
        /// scope reshapes are visible in the inquiry history.
        /// </summary>
        Task<int> SaveLineItemsAsync(
            int inquiryId,
            IReadOnlyList<InquiryLineItem> items,
            int expectedRowVersion,
            string updatedBy,
            CancellationToken ct = default);
        Task<bool> ChangeStatusAsync(
            int id, string status, string actor, int expectedRowVersion,
            CancellationToken ct = default);
        Task<IReadOnlyList<InquiryAttachment>> GetAttachmentsAsync(int inquiryId, CancellationToken ct = default);
        Task<InquiryAttachment> AddAttachmentAsync(InquiryAttachment attachment, CancellationToken ct = default);
        Task<bool> DeleteAttachmentAsync(long attachmentId, CancellationToken ct = default);
        Task<IReadOnlyList<InquiryCommunication>> GetCommunicationsAsync(int inquiryId, CancellationToken ct = default);

        /// <summary>
        /// Permanently delete an inquiry and its attachments /
        /// communications rows. Returns false when the row no longer
        /// exists. Refuses to delete when downstream documents exist:
        /// an inquiry that has spawned a Proforma Invoice or Job Order
        /// is part of the audit trail and must be cancelled, not
        /// erased. The call returns a Blocked result in that case.
        /// </summary>
        Task<DeleteInquiryResult> DeleteAsync(int id, CancellationToken ct = default);

        /// <summary>
        /// Clone an existing inquiry as a brand new draft. The clone
        /// gets a fresh InquiryNumber, resets Status to Received and
        /// drops the Proforma / Job-Order link fields. Attachments are
        /// NOT copied — the new inquiry starts empty. Returns the new
        /// InquiryRecord.
        /// </summary>
        Task<InquiryRecord> DuplicateAsync(int sourceId, CancellationToken ct = default);

        /// <summary>Drop the lifecycle cache (call after a downstream edit).</summary>
        void InvalidateLifecycle(int inquiryId);

        /// <summary>
        /// Set (or clear) the denormalised Proforma-Invoice back-link on
        /// the Inquiry row. Called by the ProformaInvoiceRepository
        /// after each Insert / Update / Delete of a PI that has an
        /// InquiryId, so the inquiry list can show "Converted → PI xxx"
        /// without a second query and the Convert-to-PI button
        /// correctly disables once a PI already exists.
        ///
        /// Pass <paramref name="proformaInvoiceId"/> = null and
        /// <paramref name="proformaNumber"/> = "" to clear the link
        /// (used when the PI is deleted).
        ///
        /// The method never throws — link writing is best-effort
        /// bookkeeping. A missing inquiry, a race with a concurrent
        /// edit, or a locked file returns quietly. Returns true when
        /// the row was updated, false when it was skipped.
        /// </summary>
        Task<bool> SetProformaLinkAsync(
            int inquiryId, int? proformaInvoiceId, string proformaNumber,
            CancellationToken ct = default);

        // ── V3 additions: filter-source lookups (master data not yet ported) ──

        /// <summary>Distinct, non-blank salesman names in the register (filter dropdown).</summary>
        Task<IReadOnlyList<string>> GetDistinctSalesmenAsync(CancellationToken ct = default);

        /// <summary>Distinct, non-blank prepared-by names in the register (filter dropdown).</summary>
        Task<IReadOnlyList<string>> GetDistinctPreparedByAsync(CancellationToken ct = default);

        /// <summary>Distinct, non-blank customer names (editor suggestion list).</summary>
        Task<IReadOnlyList<string>> GetDistinctCustomersAsync(CancellationToken ct = default);

        /// <summary>
        /// Distinct customer → locations pairs (editor location
        /// suggestion list). Returns (customer, location) tuples.
        /// </summary>
        Task<IReadOnlyList<(string Customer, string Location)>> GetCustomerLocationsAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// Outcome of a delete attempt. Distinguishes "gone" from
    /// "blocked because downstream documents exist" so the UI can show
    /// a precise message instead of a generic success/failure.
    /// </summary>
    public enum DeleteInquiryOutcome
    {
        Deleted = 0,
        NotFound = 1,
        BlockedByDownstream = 2
    }

    public readonly record struct DeleteInquiryResult(
        DeleteInquiryOutcome Outcome,
        IReadOnlyList<string> BlockingDocuments)
    {
        public bool IsSuccess => Outcome == DeleteInquiryOutcome.Deleted;
        public static DeleteInquiryResult Deleted() => new(DeleteInquiryOutcome.Deleted, Array.Empty<string>());
        public static DeleteInquiryResult NotFound() => new(DeleteInquiryOutcome.NotFound, Array.Empty<string>());
        public static DeleteInquiryResult Blocked(IReadOnlyList<string> blocking) =>
            new(DeleteInquiryOutcome.BlockedByDownstream, blocking);
    }
}
