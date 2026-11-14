using ProGlassAutomation.ViewModels;
using System;
using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges.CustomCharges
{
    public class CustomChargeRow : ViewModelBase
    {
        public Guid Id { get; } = Guid.NewGuid();

        private bool _isActive = true;
        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive == value) return; _isActive = value; OnPropertyChanged(); Recalculate(); }
        }

        private string _chargeName = "";
        public string ChargeName
        {
            get => _chargeName;
            set { if (_chargeName == value) return; _chargeName = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormulaDisplay)); }
        }

        private decimal _qty = 1;
        public decimal Qty
        {
            get => _qty;
            set { if (_qty == value) return; _qty = value; OnPropertyChanged(); Recalculate(); }
        }

        private OtherChargeUnit _unit = OtherChargeUnit.Qty;
        public OtherChargeUnit Unit
        {
            get => _unit;
            set { if (_unit == value) return; _unit = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormulaDisplay)); }
        }

        private decimal _rate;
        public decimal Rate
        {
            get => _rate;
            set { if (_rate == value) return; _rate = value; OnPropertyChanged(); Recalculate(); }
        }

        private decimal _resultAmount;
        public decimal ResultAmount
        {
            get => _resultAmount;
            private set { if (_resultAmount == value) return; _resultAmount = value; OnPropertyChanged(); }
        }

        public string FormulaDisplay
        {
            get
            {
                if (!_isActive) return "DISABLED";
                string unitLabel = Unit.ToString().ToLower();
                return $"{Qty:0.##} {unitLabel} × {Rate:0.##} AED = {ResultAmount:N2} AED";
            }
        }

        private void Recalculate()
        {
            ResultAmount = _isActive ? MoneyRounding.Round(Qty * Rate) : 0m;
            OnPropertyChanged(nameof(FormulaDisplay));
        }
    }
}