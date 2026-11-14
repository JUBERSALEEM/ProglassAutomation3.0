using ProGlassAutomation.ViewModels;

namespace ProGlassAutomation.Models.ProformaInvoice.Step5DataGrid.GrandTotalSummary
{
    /// <summary>
    /// Read-only row model representing one spec group's totals inside
    /// the DataGrid Grand Total Summary section.
    /// </summary>
    public class SpecGroupSummaryRow : ViewModelBase
    {
        private int _groupNumber;
        public int GroupNumber
        {
            get => _groupNumber;
            set { if (_groupNumber == value) return; _groupNumber = value; OnPropertyChanged(); }
        }

        // Format: "DGU · 6mm HD Grey FT"  (GlassType · SpecName)
        private string _displayLabel = "";
        public string DisplayLabel
        {
            get => _displayLabel;
            set { if (_displayLabel == value) return; _displayLabel = value; OnPropertyChanged(); }
        }

        private int _totalQty;
        public int TotalQty
        {
            get => _totalQty;
            set { if (_totalQty == value) return; _totalQty = value; OnPropertyChanged(); }
        }

        private decimal _totalSqm;
        public decimal TotalSqm
        {
            get => _totalSqm;
            set { if (_totalSqm == value) return; _totalSqm = value; OnPropertyChanged(); }
        }

        private decimal _totalLm;
        public decimal TotalLm
        {
            get => _totalLm;
            set { if (_totalLm == value) return; _totalLm = value; OnPropertyChanged(); }
        }

        private decimal _totalValue;
        public decimal TotalValue
        {
            get => _totalValue;
            set { if (_totalValue == value) return; _totalValue = value; OnPropertyChanged(); }
        }
    }
}