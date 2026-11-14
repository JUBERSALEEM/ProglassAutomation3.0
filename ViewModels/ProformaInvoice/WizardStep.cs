// ═══════════════════════════════════════════════════════════════════════
// FILE: ProformaInvoice/WizardStep.cs
// PROJECT: ProGlassAutomation
//
// WizardStep extracted from ProformaInvoiceViewModel.cs (Phase 11/12
// God-VM split) — behaviour unchanged.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.Step7PrintPreview.TermsAndConditions;
using ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.ProformaInvoice
{
    public class WizardStep : ViewModelBase
    {
        public int StepNumber { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }

        /// <summary>
        /// Short rail caption. Existing <see cref="Title"/> stays for
        /// tooltips and any other consumer; the 32 px step rail needs
        /// a single word so nine pills fit without wrapping.
        /// </summary>
        public string RailLabel => StepNumber switch
        {
            1 => "Company",
            2 => "Client",
            3 => "Project",
            4 => "Glass",
            5 => "Grid",
            6 => "Charges",
            7 => "Preview",
            8 => "Files",
            9 => "P&L",
            _ => Title
        };

        /// <summary>Zero-padded station code for the industrial rail (01–09).</summary>
        public string StationCode => StepNumber.ToString("00");

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        private bool _isCompleted;
        public bool IsCompleted
        {
            get => _isCompleted;
            set { _isCompleted = value; OnPropertyChanged(); }
        }
    }
}
