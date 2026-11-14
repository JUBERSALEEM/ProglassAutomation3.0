// ═══════════════════════════════════════════════════════════════════════
// FILE: ProformaInvoice/Step4PricingEstimator/SpecTargetGroup.cs
// PROJECT: ProGlassAutomation
//
// SpecTargetGroup extracted from PricingEstimatorViewModel.cs (Phase 11/12
// God-VM split) — behaviour unchanged.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator
{
    public class SpecTargetGroup : ViewModelBase
    {
        private int _sectionNumber;
        public int SectionNumber
        {
            get => _sectionNumber;
            set { if (_sectionNumber == value) return; _sectionNumber = value; OnPropertyChanged(); }
        }

        private string _name = "";
        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; OnPropertyChanged(); }
        }

        private string _description = "";
        public string Description
        {
            get => _description;
            set { if (_description == value) return; _description = value; OnPropertyChanged(); }
        }

        private Step4PricingSnapshot _snapshot = new();
        public Step4PricingSnapshot Snapshot
        {
            get => _snapshot;
            set { if (ReferenceEquals(_snapshot, value)) return; _snapshot = value ?? new(); OnPropertyChanged(); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }
    }
}
