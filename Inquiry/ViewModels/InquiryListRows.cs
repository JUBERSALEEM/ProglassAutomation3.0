// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/ViewModels/InquiryListRows.cs
// MODULE: Inquiry — row models for the register.
//
// Ported from the legacy list view-model. Display text is precomputed
// for cheap DataGrid binding; workflow affordances are computed from
// the same transition matrix the workflow service enforces, so a
// button that is enabled is a write that will succeed.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Services.Inquiry;
using System;
using System.Globalization;

namespace ProGlassAutomation.ViewModels.Inquiry
{
    /// <summary>Status chip with a live count.</summary>
    public sealed class InquiryStatusTab : ViewModelBase
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        private int _count;
        public int Count { get => _count; set { if (SetField(ref _count, value)) OnPropertyChanged(nameof(CountText)); } }
        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set { if (SetField(ref _isSelected, value)) OnPropertyChanged(nameof(LabelWithCount)); } }
        public string CountText => Count.ToString("N0", CultureInfo.CurrentCulture);
        public string LabelWithCount => $"{Label} · {CountText}";
    }

    /// <summary>One register row. Display text is precomputed for cheap binding.</summary>
    public sealed class InquiryListRow
    {
        public int Id { get; private set; }
        public int RowVersion { get; private set; }
        public string InquiryNumber { get; private set; } = "";
        public string InquiryReference { get; private set; } = "";
        public string CustomerName { get; private set; } = "";
        public string CustomerLocation { get; private set; } = "";
        public string Project { get; private set; } = "";
        public string Subject { get; private set; } = "";
        public string Salesman { get; private set; } = "";
        public string PreparedBy { get; private set; } = "";
        public string PreparedByRole { get; private set; } = "";
        public string Status { get; internal set; } = InquiryStatuses.Received;
        public string Priority { get; private set; } = "Normal";
        public decimal InquiryValue { get; private set; }
        public string ValueText => InquiryValue == 0 ? "—" : $"AED {InquiryValue:N0}";
        public DateTime ReceivedAt { get; private set; }
        public DateTime UpdatedAt { get; private set; }
        public string ReceivedDateText => ReceivedAt == default ? "" : ReceivedAt.ToString("dd MMM yyyy", CultureInfo.CurrentCulture);
        public string UpdatedText => UpdatedAt == default ? "" : UpdatedAt.ToString("dd MMM yyyy · hh:mm tt", CultureInfo.CurrentCulture);
        public string UpdatedBy { get; private set; } = "";
        public string UpdatedByDisplay => string.IsNullOrWhiteSpace(UpdatedBy) ? "" : $"by {UpdatedBy}";
        public int AttachmentCount { get; internal set; }
        public string AttachmentBadgeText { get; private set; } = "";
        public bool HasAttachments => AttachmentCount > 0;
        public string AttachmentChipText => AttachmentCount == 0 ? "None" : $"{AttachmentCount} file{(AttachmentCount == 1 ? "" : "s")}";
        public string ProformaNumber { get; private set; } = "";
        public string JobOrderNumber { get; private set; } = "";
        public bool IsConverted => !string.IsNullOrWhiteSpace(ProformaNumber);
        /// <summary>
        /// A PI can be created from a Prepared or Confirmed inquiry.
        /// Shown in the PI column once the Proforma module exists; the
        /// convert action itself ships with that module.
        /// </summary>
        public bool CanConvert =>
            (Status == InquiryStatuses.Prepared || Status == InquiryStatuses.Confirmed)
            && !IsConverted;
        // Workflow affordances for the row context menu (guarded by the
        // same state machine the workflow service enforces server-side).
        public bool CanPrepareWorkflow => Services.Inquiry.InquiryWorkflowService.CanTransition(Status, InquiryStatuses.Prepared);
        public bool CanSendWorkflow => Services.Inquiry.InquiryWorkflowService.CanTransition(Status, InquiryStatuses.Sent);
        public bool CanConfirmWorkflow => Services.Inquiry.InquiryWorkflowService.CanTransition(Status, InquiryStatuses.Confirmed);
        public bool CanHoldWorkflow => Services.Inquiry.InquiryWorkflowService.CanTransition(Status, InquiryStatuses.Hold);
        public bool CanCancelWorkflow => Services.Inquiry.InquiryWorkflowService.CanTransition(Status, InquiryStatuses.Cancelled);
        public string ChainText => IsConverted
            ? "PI " + ProformaNumber + (string.IsNullOrWhiteSpace(JobOrderNumber) ? " · JO not released" : " · JO " + JobOrderNumber)
            : "Not converted to PI yet";
        public string Notes { get; internal set; } = "";
        public string ContactSummary => string.IsNullOrWhiteSpace(CustomerLocation) ? "" : CustomerLocation;
        /// <summary>Avatar initials (first letters of the first two words).</summary>
        public string CustomerInitials
        {
            get
            {
                var parts = CustomerName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return "·";
                return parts.Length == 1
                    ? parts[0][..1].ToUpperInvariant()
                    : (parts[0][..1] + parts[1][..1]).ToUpperInvariant();
            }
        }

        // ── Full-lifecycle display fields ────────────────────────────
        // Bound from the InquiryLifecycleSnapshot hydrated by the
        // repository via IInquiryLifecycleService. Every property here
        // is a precomputed string the XAML can bind to directly.

        public InquiryLifecycleSnapshot Lifecycle { get; private set; } = new();

        /// <summary>Status → StatusBadge Tag vocabulary (see ThemeContract).</summary>
        public string StatusTag => Status switch
        {
            InquiryStatuses.Confirmed => "Success",
            InquiryStatuses.Prepared => "Information",
            InquiryStatuses.Sent => "Information",
            InquiryStatuses.Hold => "Warning",
            InquiryStatuses.Cancelled => "Error",
            _ => ""
        };

        // Per-stage cell text for the lifecycle columns / detail cards.
        // Empty when the stage has not started.
        public string ProformaCellText => Lifecycle.HasProforma
            ? $"{Lifecycle.ProformaNumber} · {Lifecycle.ProformaStatus}"
            : "—";
        public string JobOrderCellText => Lifecycle.HasJobOrder
            ? $"{Lifecycle.JobOrderNumber} · {Lifecycle.JobOrderStatus}"
            : "—";
        public string DeliveryNoteCellText => Lifecycle.HasDeliveryNote
            ? (Lifecycle.DeliveryNoteCount > 1
                ? $"{Lifecycle.LatestDeliveryNoteNumber} · {Lifecycle.LatestDeliveryNoteStatus} (+{Lifecycle.DeliveryNoteCount - 1})"
                : $"{Lifecycle.LatestDeliveryNoteNumber} · {Lifecycle.LatestDeliveryNoteStatus}")
            : "—";
        public string TaxInvoiceCellText => Lifecycle.HasTaxInvoice
            ? (Lifecycle.TaxInvoiceCount > 1
                ? $"{Lifecycle.LatestTaxInvoiceNumber} · {Lifecycle.LatestTaxInvoiceStatus} (+{Lifecycle.TaxInvoiceCount - 1})"
                : $"{Lifecycle.LatestTaxInvoiceNumber} · {Lifecycle.LatestTaxInvoiceStatus}")
            : "—";
        public string StatementCellText => Lifecycle.HasStatement
            ? Lifecycle.LatestStatementNumber ?? "—"
            : "—";

        // Per-stage coarse state → StatusBadge Tag vocabulary.
        public string ProformaStateTag => StateTag(Lifecycle.StateFor(InquiryLifecycleKind.ProformaInvoice));
        public string JobOrderStateTag => StateTag(Lifecycle.StateFor(InquiryLifecycleKind.JobOrder));
        public string DeliveryNoteStateTag => StateTag(Lifecycle.StateFor(InquiryLifecycleKind.DeliveryNote));
        public string TaxInvoiceStateTag => StateTag(Lifecycle.StateFor(InquiryLifecycleKind.TaxInvoice));
        public string StatementStateTag => StateTag(Lifecycle.StateFor(InquiryLifecycleKind.Statement));
        public string InquiryStateTag => StateTag(Lifecycle.StateFor(InquiryLifecycleKind.Inquiry));

        private static string StateTag(InquiryLifecycleState state) => state switch
        {
            InquiryLifecycleState.Completed => "Success",
            InquiryLifecycleState.Active => "Information",
            InquiryLifecycleState.Draft => "Warning",
            InquiryLifecycleState.Skipped => "Error",
            _ => ""
        };

        // Pipeline progress — "n/6" of stages reached, with a percent.
        public string PipelineProgressText =>
            $"{Lifecycle.FurthestStageReached + 1}/{(int)InquiryLifecycleKind.Statement + 1}";
        public int PipelineProgressPercent =>
            (int)Math.Round(100.0 * (Lifecycle.FurthestStageReached + 1) /
                ((int)InquiryLifecycleKind.Statement + 1));

        public static InquiryListRow FromProjection(InquiryListProjection x) => new()
        {
            Id = x.Id, RowVersion = x.RowVersion,
            InquiryNumber = x.InquiryNumber, InquiryReference = x.InquiryReference,
            CustomerName = x.CustomerName, CustomerLocation = x.CustomerLocation,
            Project = x.ProjectName, Subject = x.ProjectDescription,
            Salesman = x.SalesmanName, PreparedBy = x.PreparedByName, PreparedByRole = x.PreparedByRole,
            Status = InquiryStatuses.Normalize(x.Status), Priority = x.Priority,
            InquiryValue = x.InquiryValue, ReceivedAt = x.ReceivedAt, UpdatedAt = x.UpdatedAt,
            UpdatedBy = x.UpdatedBy, AttachmentCount = x.AttachmentCount,
            AttachmentBadgeText = x.AttachmentBadgeText, ProformaNumber = x.ProformaNumber,
            JobOrderNumber = x.JobOrderNumber, Lifecycle = x.Lifecycle
        };
    }

    /// <summary>Attachment chip in the editor.</summary>
    public sealed class InquiryAttachmentItem
    {
        public long Id { get; private set; }
        public string FileName { get; private set; } = "";
        public string Extension { get; private set; } = "";
        public string TypeLabel { get; private set; } = "";
        public long FileSize { get; private set; }
        public string FileSizeDisplay => FileSize < 1024 * 1024
            ? $"{FileSize / 1024d:N0} KB" : $"{FileSize / (1024d * 1024d):N1} MB";
        public string UploadedBy { get; private set; } = "";
        public DateTime UploadedAt { get; private set; }
        public string UploadedText => UploadedAt == default ? "" : UploadedAt.ToString("dd MMM yyyy", CultureInfo.CurrentCulture);
        public string DetailText
        {
            get
            {
                var parts = new List<string>();
                if (FileSize > 0) parts.Add(FileSizeDisplay);
                if (UploadedAt != default) parts.Add(UploadedText);
                if (!string.IsNullOrWhiteSpace(UploadedBy)) parts.Add("by " + UploadedBy);
                return string.Join(" · ", parts);
            }
        }
        public InquiryAttachment Attachment { get; private set; } = new();

        public static InquiryAttachmentItem FromModel(InquiryAttachment x) => new()
        {
            Id = x.Id, FileName = x.FileName, Extension = x.Extension,
            TypeLabel = (x.Extension ?? "").TrimStart('.').ToUpperInvariant(),
            FileSize = x.FileSize, UploadedBy = x.UploadedBy, UploadedAt = x.UploadedAt,
            Attachment = x
        };
    }

    /// <summary>Timeline entry for the editor activity feed.</summary>
    public sealed class InquiryActivityItem
    {
        public string Title { get; init; } = "";
        public string Description { get; init; } = "";
        public string Actor { get; init; } = "";
        public DateTime OccurredAt { get; init; }
        public string OccurredText => OccurredAt == default ? "" : OccurredAt.ToString("dd MMM yyyy · hh:mm tt", CultureInfo.CurrentCulture);
    }
}
