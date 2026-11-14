// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/SavedInvoicesList/InvoiceApprovalViewModel.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — approval gate dialog.
//
// 2026-09-02 (Phase 3.7): the approval dialog's controller logic moved out
// of Views/ProformaInvoice/SavedInvoicesList/InvoiceApprovalDialog.xaml.cs
// into this VM, following the repo's VM-dialog pattern (WorkflowProgress-
// Dialog): the view wires RequestClose to DialogResult.
//
// Contract preserved for callers (`_dialogs.Create<InvoiceApprovalDialog>
// (count)` then `dlg.Approved` / `dlg.IsSignedPi` / `dlg.LpoNumber`):
// the window forwards those results from this VM.
//
// Mode mapping (IndexEqualsConverter): ApprovalMode 0 = "Customer signed
// the Proforma Invoice", 1 = "Customer provided an LPO / PO". LPO mode
// requires a non-empty LPO number; Signed PI mode saves it only if typed.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using System;
using System.Windows;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.SavedInvoicesList
{
    /// <summary>Modal-dialog VM: confirm HOW the customer confirmed the
    /// quote and collect the optional/required LPO number.</summary>
    public sealed class InvoiceApprovalViewModel : ViewModelBase
    {
        private const int ModeSignedPi = 0;
        private const int ModeLpo = 1;

        /// <summary>Step-8 grounding line, e.g. "3 file(s) currently
        /// attached in Step 8 (Attachments)."</summary>
        public string AttachmentNote { get; }

        /// <summary>LPO / PO number text (TwoWay bound).</summary>
        public string LpoNumber { get; set; } = "";

        /// <summary>Validation strip text. Empty when clean.</summary>
        public string ValidationMessage { get; private set; } = "";

        /// <summary>Validation strip visibility — Hidden (never Collapsed)
        /// when clean so the fixed-height card never re-measures.</summary>
        public Visibility ValidationVisibility { get; private set; } = Visibility.Hidden;

        /// <summary>View wiring point: result=true → DialogResult=true.
        /// null until the window assigns it.</summary>
        public Action<bool>? RequestClose { get; set; }

        /// <summary>True when the user confirmed the approval.</summary>
        public bool Approved { get; private set; }

        /// <summary>True when "Customer signed the Proforma Invoice" was
        /// chosen.</summary>
        public bool IsSignedPi { get; private set; }

        private int _approvalMode = ModeSignedPi;

        /// <summary>0 = Signed PI, 1 = LPO (IndexEqualsConverter-bound).</summary>
        public int ApprovalMode
        {
            get => _approvalMode;
            set
            {
                if (value == _approvalMode) return;
                _approvalMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LpoLabelText));
                // Switching modes clears any stale validation error.
                ValidationMessage = "";
                ValidationVisibility = Visibility.Hidden;
                OnPropertyChanged(nameof(ValidationMessage));
                OnPropertyChanged(nameof(ValidationVisibility));
            }
        }

        /// <summary>Constant single-line label whose text conveys the
        /// mode — REQUIRED for LPO, OPTIONAL for Signed PI. Kept a plain
        /// string (not a DynamicResource) so the height never changes.</summary>
        public string LpoLabelText
        {
            get
            {
                string base_ = ProGlassAutomation.Services.Localization
                    .LocalizationService.Instance.T("ProformaInvoice.LPOPONumber");
                return ApprovalMode == ModeLpo
                    ? base_ + " (REQUIRED)"
                    : base_ + " (OPTIONAL)";
            }
        }

        public ICommand ApproveCommand { get; }

        public InvoiceApprovalViewModel(int attachmentCount = 0)
        {
            AttachmentNote =
                $"{attachmentCount} file(s) currently attached in Step 8 (Attachments).";
            ApproveCommand = new RelayCommand(_ => Approve());
        }

        private void Approve()
        {
            bool lpoSelected = ApprovalMode == ModeLpo;
            string lpo = LpoNumber?.Trim() ?? "";

            // LPO → the number is REQUIRED (an LPO always carries one).
            if (lpoSelected && string.IsNullOrWhiteSpace(lpo))
            {
                ValidationMessage =
                    "An LPO always carries an LPO / purchase order number. " +
                    "Enter it to approve this invoice.";
                ValidationVisibility = Visibility.Visible;
                OnPropertyChanged(nameof(ValidationMessage));
                OnPropertyChanged(nameof(ValidationVisibility));
                return;
            }

            // Signed PI → the LPO number is OPTIONAL: save it only if the
            // user entered one (sometimes it is written on the signed PI).
            LpoNumber = lpo;
            IsSignedPi = !lpoSelected;
            Approved = true;
            RequestClose?.Invoke(true);
        }
    }
}
