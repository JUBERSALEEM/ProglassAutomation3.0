using ProGlassAutomation.ViewModels;
using System;

namespace ProGlassAutomation.Models.ProformaInvoice.SavedInvoice
{
    /// <summary>
    /// Row model for the SavedInvoicesList UI. Only surfaces indexed
    /// columns (fast to load, no JSON deserialization needed for browsing).
    /// The full FullSnapshotJson is only fetched when user clicks Load.
    /// </summary>
    public class SavedInvoiceListItem : ViewModelBase
    {
        public int Id { get; set; }

        private string _proformaId = "";
        public string ProformaId
        {
            get => _proformaId;
            set { if (_proformaId == value) return; _proformaId = value; OnPropertyChanged(); }
        }

        private string _companyName = "";
        public string CompanyName
        {
            get => _companyName;
            set { if (_companyName == value) return; _companyName = value; OnPropertyChanged(); }
        }

        private string _customerName = "";
        public string CustomerName
        {
            get => _customerName;
            set { if (_customerName == value) return; _customerName = value; OnPropertyChanged(); }
        }

        private string _projectName = "";
        public string ProjectName
        {
            get => _projectName;
            set { if (_projectName == value) return; _projectName = value; OnPropertyChanged(); }
        }

        private string _status = "Draft";
        public string Status
        {
            get => _status;
            set { if (_status == value) return; _status = value; OnPropertyChanged(); }
        }

        private decimal _grandTotal;
        public decimal GrandTotal
        {
            get => _grandTotal;
            set { if (_grandTotal == value) return; _grandTotal = value; OnPropertyChanged(); }
        }

        private DateTime _createdAt;
        public DateTime CreatedAt
        {
            get => _createdAt;
            set { if (_createdAt == value) return; _createdAt = value; OnPropertyChanged(); }
        }

        private DateTime _updatedAt;
        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set { if (_updatedAt == value) return; _updatedAt = value; OnPropertyChanged(); }
        }

        // ERP lifecycle hardening (Phase 1): voided (soft-closed) invoices are
        // hidden from the list by default, shown via the "Show voided" toggle.
        private bool _isVoid;
        public bool IsVoid
        {
            get => _isVoid;
            set { if (_isVoid == value) return; _isVoid = value; OnPropertyChanged(); }
        }

        // ── computed list status (business lifecycle) ─────────────────────
        // Populated by SavedInvoicesListViewModel from DocumentProgressSnapshot.
        // Draft tax invoices do not count as invoiced.

        private string _displayStatus = "";
        public string DisplayStatus
        {
            get => _displayStatus;
            set { if (_displayStatus == value) return; _displayStatus = value; OnPropertyChanged(); }
        }

        private string _displayStatusBrushKey = "NeutralBrush";
        public string DisplayStatusBrushKey
        {
            get => _displayStatusBrushKey;
            set
            {
                if (_displayStatusBrushKey == value) return;
                _displayStatusBrushKey = value;
                OnPropertyChanged();
            }
        }
    }
}