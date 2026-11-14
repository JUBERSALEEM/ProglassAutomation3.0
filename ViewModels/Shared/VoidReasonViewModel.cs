// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/Shared/VoidReasonViewModel.cs
// PROJECT: ProGlassAutomation
// MODULE: Shared — mandatory-reason capture for a Void / soft-close
//
// ERP lifecycle hardening (Phase 1). Voiding a posted document requires a
// reason (an auditor's first question). This tiny VM backs VoidReasonDialog,
// mirroring the RenameAttachmentViewModel pattern already used in the app:
//   • RequestClose(bool) — the view wires this to DialogResult + Close().
//   • Confirm is disabled until a non-blank reason is entered.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Windows;
using System.Windows.Input;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Localization;

namespace ProGlassAutomation.ViewModels.Shared
{
    /// <summary>Backs the mandatory void-reason dialog.</summary>
    public sealed class VoidReasonViewModel : ViewModelBase
    {
        /// <summary>Set by the view: closes the window with the given result.</summary>
        public Action<bool>? RequestClose { get; set; }

        public VoidReasonViewModel(string documentLabel)
        {
            DocumentLabel = string.IsNullOrWhiteSpace(documentLabel)
                ? LocalizationService.Instance.T("Void.ThisDocument")
                : documentLabel;

            ConfirmCommand = new RelayCommand(
                _ => RequestClose?.Invoke(true),
                _ => !string.IsNullOrWhiteSpace(_reason));
            CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false));
        }

        /// <summary>Human-readable document being voided, e.g. "PI-2026-001".</summary>
        public string DocumentLabel { get; }

        private string _reason = string.Empty;
        /// <summary>The mandatory reason. Trimmed value is read after confirm.</summary>
        public string Reason
        {
            get => _reason;
            set
            {
                if (_reason == value) return;
                _reason = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ValidationVisibility));
                (ConfirmCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        /// <summary>The trimmed reason, safe to persist.</summary>
        public string ResultReason => _reason?.Trim() ?? string.Empty;

        /// <summary>Shows the "reason required" hint only while blank.</summary>
        public Visibility ValidationVisibility =>
            string.IsNullOrWhiteSpace(_reason) ? Visibility.Visible : Visibility.Hidden;

        public ICommand ConfirmCommand { get; }
        public ICommand CancelCommand { get; }
    }
}
