// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.c.Presentation/step6.c.3.ProformaWorkspaceViewModel.Editor.cs
// MODULE: step6 — the editor's stage rail, header stage and totals
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Proforma;

namespace ProGlassAutomation.ViewModels.Proforma
{
    public sealed partial class ProformaWorkspaceViewModel
    {
        private ProformaStageChip? _selectedStageChip;
        private PricingSection? _selectedSection;
        private SpecGroup? _selectedGroup;
        private SpecRow? _selectedSpecRow;
        private CustomChargeRow? _selectedCustomCharge;
        private ProformaAttachment? _selectedAttachment;
        private string _newBuild = GlassBuilds.Dgu;
        private string _newChargeName = "";
        private decimal _newChargeQuantity;
        private decimal _newChargeRate;
        private ChargeUnit _newChargeUnit = ChargeUnit.Lm;
        private DiscountOption? _selectedDiscountOption;
        private string _targetGrandTotalText = "";
        private string _importMessage = "";

        // ── Editor state ────────────────────────────────────────────────
        public ObservableCollection<ProformaStageChip> Stages { get; } = new();
        public ObservableCollection<PricingSection> Sections { get; } = new();
        public ObservableCollection<SpecGroup> GridGroups { get; } = new();
        public ObservableCollection<SpecRow> GroupRows { get; } = new();
        public ObservableCollection<PredefinedChargeRow> PredefinedCharges { get; } = new();
        public ObservableCollection<WastageChargeRow> WastageCharges { get; } = new();
        public ObservableCollection<CustomChargeRow> CustomCharges { get; } = new();
        public ObservableCollection<ProformaAttachment> Attachments { get; } = new();
        public ObservableCollection<ProfitLossRow> CostRows { get; } = new();
        public ObservableCollection<SpecTargetRow> TargetRows { get; } = new();
        public ObservableCollection<string> LossWarnings { get; } = new();

        public IReadOnlyList<string> Builds => GlassBuilds.All;
        public IReadOnlyList<DiscountOption> DiscountOptions { get; } = new[]
        {
            new DiscountOption { Value = DiscountType.Percentage, Label = "Percentage %" },
            new DiscountOption { Value = DiscountType.FixedAmount, Label = "Fixed amount (AED)" }
        };
        public IReadOnlyList<ChargeUnit> ChargeUnits { get; } = new[] { ChargeUnit.Sqm, ChargeUnit.Lm, ChargeUnit.Qty };

        public ProformaInvoiceDraft Draft => _workspace.Draft;

        public string EditorHeading
        {
            get
            {
                string number = Draft.ProformaNumber.Trim();
                return number.Length == 0 ? "NEW PROFORMA INVOICE" : "PROFORMA INVOICE " + number;
            }
        }

        public string EditorStatusText => Draft.Status;
        public string EditorStatusTag => Draft.Status switch
        {
            ProformaInvoiceStatus.Approved => "Success",
            ProformaInvoiceStatus.Sent => "Information",
            ProformaInvoiceStatus.Rejected => "Warning",
            ProformaInvoiceStatus.Expired => "Warning",
            ProformaInvoiceStatus.Cancelled => "Error",
            _ => ""
        };

        public bool IsPersisted => Draft.Id > 0;
        public bool CanEditBody => Draft.IsEditable;

        public bool CanSend => CanEditBody
            && InvoiceLifecycle.CanTransition(Draft.Status, ProformaInvoiceStatus.Sent);
        public bool CanApprove => InvoiceLifecycle.CanTransition(Draft.Status, ProformaInvoiceStatus.Approved);
        public bool CanReject => InvoiceLifecycle.CanTransition(Draft.Status, ProformaInvoiceStatus.Rejected);
        public bool CanCancelDocument => InvoiceLifecycle.CanTransition(Draft.Status, ProformaInvoiceStatus.Cancelled);

        public ProformaStageChip? SelectedStageChip
        {
            get => _selectedStageChip;
            private set => SetField(ref _selectedStageChip, value);
        }

        public string CurrentStageKey => _workspace.CurrentStage.ToString();

        // ── Stage 1: header ─────────────────────────────────────────────
        public string CompanyNameText => Draft.Parties.Company.CompanyName;
        public string CompanyTrnText => Draft.Parties.Company.Trn.Length == 0 ? "TRN not recorded" : "TRN " + Draft.Parties.Company.Trn;
        public string CompanyAddressText => Draft.Parties.Company.Address;
        public string ClientNameText => Draft.Parties.Client.ClientName;
        public string ClientTrnText => Draft.Parties.Client.Trn.Length == 0 ? "" : "TRN " + Draft.Parties.Client.Trn;
        public string ClientAttentionText => Draft.Parties.Client.AttentionName;
        public string ClientAddressText => Draft.Parties.Client.BillingAddress;
        public string ProjectText => Draft.Parties.Project.DisplayText;
        public string PreparedByText => Draft.Parties.PreparedBy.DisplayText;
        public string InquiryText => Draft.Parties.Inquiry.InquiryNumber;
        public bool HasInquiry => Draft.Parties.Inquiry.InquiryId is > 0;
        public string PartiesRefreshedText => Draft.Parties.PartiesRefreshedAtUtc == default
            ? "read when this draft was started"
            : "read " + Draft.Parties.PartiesRefreshedAtUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm", CultureInfo.CurrentCulture);

        public DateTime? IssueDate
        {
            get => Draft.IssueDate == default ? null : Draft.IssueDate;
            set
            {
                if (value is not DateTime date) return;
                _workspace.SetDates(date, Draft.ValidUntil);
                OnPropertyChanged();
                OnPropertyChanged(nameof(ValidUntil));
            }
        }

        public DateTime? ValidUntil
        {
            get => Draft.ValidUntil == default ? null : Draft.ValidUntil;
            set
            {
                if (value is not DateTime date) return;
                _workspace.SetDates(Draft.IssueDate, date);
                OnPropertyChanged();
            }
        }

        /// <summary>The customer typed onto the document instead of chosen from the register.</summary>
        public string WalkInName
        {
            get => Draft.Parties.Client.ClientName;
            set
            {
                string name = (value ?? "").Trim();
                if (string.Equals(name, Draft.Parties.Client.ClientName, StringComparison.Ordinal)) return;
                _workspace.SetWalkInCustomer(name,
                    Draft.Parties.Client.AttentionName, Draft.Parties.Client.Phone,
                    Draft.Parties.Client.Email, Draft.Parties.Client.Trn,
                    Draft.Parties.Client.BillingAddress);
                OnPropertyChanged();
                OnPropertyChanged(nameof(ClientNameText));
                Reapply();
            }
        }

        /// <summary>The issue date as the operator types it, parsed on the way in.</summary>
        public string IssueDateText
        {
            get => FormatDate(Draft.IssueDate);
            set
            {
                if (TryReadDate(value, out DateTime date)) IssueDate = date;
                OnPropertyChanged();
            }
        }

        /// <summary>The validity date as the operator types it, parsed on the way in.</summary>
        public string ValidUntilDateText
        {
            get => FormatDate(Draft.ValidUntil);
            set
            {
                if (TryReadDate(value, out DateTime date)) ValidUntil = date;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// What this stage still wants, in the engine's own words. The rail prints
        /// a count; this prints the reasons, so the operator is never guessing.
        /// </summary>
        public string StageHeadline
        {
            get
            {
                StageStatus? status = _workspace.StageStatuses.FirstOrDefault(
                    item => item.Stage == _workspace.CurrentStage);
                if (status == null) return "";
                if (status.IsComplete) return "This stage is complete.";
                return status.Problems.Count == 0
                    ? "Nothing recorded for this stage yet."
                    : "Still to answer: " + string.Join(" · ", status.Problems);
            }
        }

        private static string FormatDate(DateTime value) =>
            value == default ? "" : value.ToString("dd MMM yyyy", CultureInfo.CurrentCulture);

        private static bool TryReadDate(string? text, out DateTime date)
        {
            date = default;
            string trimmed = (text ?? "").Trim();
            if (trimmed.Length == 0) return false;
            string[] formats = { "dd MMM yyyy", "d MMM yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" };
            return DateTime.TryParseExact(trimmed, formats, CultureInfo.CurrentCulture,
                       DateTimeStyles.AllowWhiteSpaces, out date)
                || DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
        }

        public string ImportMessage
        {
            get => _importMessage;
            private set { if (SetField(ref _importMessage, value)) OnPropertyChanged(nameof(HasImportMessage)); }
        }

        public bool HasImportMessage => _importMessage.Length > 0;
    }
}
