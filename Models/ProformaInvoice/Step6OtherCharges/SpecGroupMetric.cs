using ProGlassAutomation.ViewModels;

namespace ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges
{
    /// <summary>
    /// Aggregated metrics for a single spec group imported from Step 5.
    /// Bound on the Step 6 top panel so LM1 / LM2 / SQM stay live.
    /// </summary>
    public class SpecGroupMetric : ViewModelBase
    {
        private int _specGroupNumber;
        public int SpecGroupNumber
        {
            get => _specGroupNumber;
            set { if (_specGroupNumber == value) return; _specGroupNumber = value; OnPropertyChanged(); }
        }

        private string _specDisplayName = "";
        public string SpecDisplayName
        {
            get => _specDisplayName;
            set { if (_specDisplayName == value) return; _specDisplayName = value; OnPropertyChanged(); }
        }

        private string _glassType = "";
        public string GlassType
        {
            get => _glassType;
            set { if (_glassType == value) return; _glassType = value; OnPropertyChanged(); }
        }

        private bool _isDoubleGlass;
        public bool IsDoubleGlass
        {
            get => _isDoubleGlass;
            set
            {
                if (_isDoubleGlass == value) return;
                _isDoubleGlass = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PhysicalPanes));
            }
        }

        private int _paneFactor = 1;
        /// <summary>1 = SGU, 2 = DGU/LAM, 3 = DGU+LAM hybrid.</summary>
        public int PaneFactor
        {
            get => _paneFactor;
            set
            {
                if (_paneFactor == value) return;
                _paneFactor = value <= 0 ? 1 : value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PhysicalPanes));
            }
        }

        private bool _isMatchedSameGlass;
        public bool IsMatchedSameGlass
        {
            get => _isMatchedSameGlass;
            set
            {
                if (_isMatchedSameGlass == value) return;
                _isMatchedSameGlass = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MatchHint));
            }
        }

        private string _outerGlassColor = "";
        public string OuterGlassColor
        {
            get => _outerGlassColor;
            set { if (_outerGlassColor == value) return; _outerGlassColor = value ?? ""; OnPropertyChanged(); }
        }

        private string _innerGlassColor = "";
        public string InnerGlassColor
        {
            get => _innerGlassColor;
            set { if (_innerGlassColor == value) return; _innerGlassColor = value ?? ""; OnPropertyChanged(); }
        }

        public string MatchHint
        {
            get
            {
                if (IsMatchedSameGlass)
                {
                    if (!string.IsNullOrWhiteSpace(OuterGlassColor)
                        && !string.IsNullOrWhiteSpace(InnerGlassColor))
                        return $"{OuterGlassColor} + {InnerGlassColor} · 2× polish";
                    return "Same glass · 2× polish";
                }

                if (!string.IsNullOrWhiteSpace(OuterGlassColor)
                    && !string.IsNullOrWhiteSpace(InnerGlassColor)
                    && !string.Equals(OuterGlassColor, InnerGlassColor, System.StringComparison.OrdinalIgnoreCase))
                    return $"{OuterGlassColor} + {InnerGlassColor} · regular polish";

                return "";
            }
        }

        private decimal _lm1;
        public decimal Lm1
        {
            get => _lm1;
            set { if (_lm1 == value) return; _lm1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(LmCombined)); }
        }

        private decimal _lm2;
        public decimal Lm2
        {
            get => _lm2;
            set { if (_lm2 == value) return; _lm2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(LmCombined)); }
        }

        public decimal LmCombined => Lm1 + Lm2;

        private decimal _sqm1;
        public decimal Sqm1
        {
            get => _sqm1;
            set { if (_sqm1 == value) return; _sqm1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(SqmCombined)); }
        }

        private decimal _sqm2;
        public decimal Sqm2
        {
            get => _sqm2;
            set { if (_sqm2 == value) return; _sqm2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(SqmCombined)); }
        }

        public decimal SqmCombined => Sqm1 + Sqm2;

        private int _qty;
        public int Qty
        {
            get => _qty;
            set
            {
                if (_qty == value) return;
                _qty = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PhysicalPanes));
            }
        }

        public int PhysicalPanes => Qty * (PaneFactor <= 0 ? 1 : PaneFactor);
    }
}
