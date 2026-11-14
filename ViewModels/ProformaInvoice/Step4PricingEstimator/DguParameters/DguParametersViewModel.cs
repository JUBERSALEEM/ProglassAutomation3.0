// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  ViewModels/ProformaInvoice/Step4PricingEstimator/DguParameters/         ║
// ║    DguParametersViewModel.cs                                             ║
// ║                                                                          ║
// ║  CHAT 13 HOTFIX-3:                                                       ║
// ║    Row 3 redesigned. Old confusing controls removed:                     ║
// ║      ❌ UInsertOption (kept but list corrected)                          ║
// ║      ❌ OverlapOption (replaced by PanelExtra)                           ║
// ║      ❌ InnerAsPerDwg (removed — obsolete)                               ║
// ║                                                                          ║
// ║    New clean 2-dropdown design:                                          ║
// ║      ✅ SpacerExtra  — 3 options (U-Insert variants only)                ║
// ║      ✅ PanelExtra   — 19 options (normal/special overlap modes,      ║
// ║                        Shutter Bonding, Argon Gas, all with As Per DWG   ║
// ║                        variants)                                         ║
// ║                                                                          ║
// ║    Spec name format:                                                     ║
// ║      {Outer} + {Spacer}[ {SpacerExtra}] + {Inner}[ {PanelExtra}]        ║
// ║                                                                          ║
// ║    Example:                                                              ║
// ║      6mm HD Grey FT Glass + 12mm ASP With U-Insert +                    ║
// ║      6mm Clear FT Glass With Argon Gas As Per DWG                        ║
// ║                                                                          ║
// ║  PRESERVED:                                                              ║
// ║    ✅ All outer/inner glass properties + dropdown options                ║
// ║    ✅ Spacer width / material / auto-price logic                         ║
// ║    ✅ Yield factor + margin                                              ║
// ╚══════════════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguParameters
{
    /// <summary>
    /// ViewModel for DGU (Double Glass Unit) parameter panel.
    /// Handles outer/inner glass parameters, spacer configuration (with
    /// auto-suggested pricing based on width + material surcharge),
    /// spacer extras (U-Insert), panel extras (Overlap, Fanhole, Cutout,
    /// Holes, Shutter Bonding, Argon Gas), yield factor, and margin.
    /// </summary>
    public partial class DguParametersViewModel : ViewModelBase
    {
        // Guard flag to prevent recursion when auto-updating spacer price
        private bool _isSuggestingSpacerPrice;

        // ===================== Row 1: Outer Glass =====================

        private string _outerThickness;
        public string OuterThickness
        {
            get => _outerThickness;
            set
            {
                _outerThickness = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
                OnOuterGlassChanged();   // PHASE A: Sheet Store refresh
            }
        }

        private string _outerGlassColor;
        public string OuterGlassColor
        {
            get => _outerGlassColor;
            set
            {
                _outerGlassColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
                OnOuterGlassChanged();   // PHASE A: Sheet Store refresh
            }
        }

        private string _outerTreatment;
        public string OuterTreatment
        {
            get => _outerTreatment;
            set
            {
                _outerTreatment = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
            }
        }

        private decimal _outerBasePrice;
        public decimal OuterBasePrice
        {
            get => _outerBasePrice;
            set { _outerBasePrice = value; OnPropertyChanged(); }
        }

        // ===================== Row 2: Spacer Width + Material + Price =====================

        private string _spacerWidth;
        public string SpacerWidth
        {
            get => _spacerWidth;
            set
            {
                _spacerWidth = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
                SuggestSpacerPrice();
            }
        }

        private string _spacerMaterial;
        public string SpacerMaterial
        {
            get => _spacerMaterial;
            set
            {
                _spacerMaterial = value;
                OnPropertyChanged();
                SuggestSpacerPrice();
            }
        }

        private decimal _spacerPrice;
        public decimal SpacerPrice
        {
            get => _spacerPrice;
            set { _spacerPrice = value; OnPropertyChanged(); }
        }

        // ===================== Row 3: Spacer Extra + Panel Extra =====================

        /// <summary>
        /// HOTFIX-3: New unified property for spacer-side extras
        /// (U-Insert variants only). Placed AFTER spacer in spec name.
        /// Was: UInsertOption (kept name-compatible for smoother migration).
        /// </summary>
        private string _spacerExtra;
        public string SpacerExtra
        {
            get => _spacerExtra;
            set
            {
                _spacerExtra = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
                OnPropertyChanged(nameof(HasUInsertExtra));
            }
        }

        /// <summary>
        /// True when the selected spacer extra requires a U-Insert price box.
        /// </summary>
        public bool HasUInsertExtra =>
            !string.IsNullOrWhiteSpace(SpacerExtra)
            && SpacerExtra.IndexOf("U-Insert", System.StringComparison.OrdinalIgnoreCase) >= 0;

        private decimal _uInsertPricePerSqm = 10m;
        /// <summary>
        /// Additional U-Insert charge per SQM. Step 4 seeds this value and
        /// Step 6 advanced mode consumes it for the auto-enabled U-Insert row.
        /// It is NOT silently folded into the base DGU rate to avoid double
        /// charging once Step 6 applies the separate fabrication line.
        /// </summary>
        public decimal UInsertPricePerSqm
        {
            get => _uInsertPricePerSqm;
            set { _uInsertPricePerSqm = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// HOTFIX-3: New unified property for panel-side extras
        /// (Overlap, Fanhole, Cutout, Holes, Shutter Bonding, Argon Gas —
        /// all with As Per DWG variants). Placed AFTER inner glass in
        /// spec name.
        /// Replaces old OverlapOption.
        /// </summary>
        private string _panelExtra;
        public string PanelExtra
        {
            get => _panelExtra;
            set
            {
                _panelExtra = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectivePanelExtra));
                OnPropertyChanged(nameof(GeneratedSpecName));
            }
        }

        private bool _useCustomPanelExtra;
        public bool UseCustomPanelExtra
        {
            get => _useCustomPanelExtra;
            set
            {
                if (_useCustomPanelExtra == value) return;
                _useCustomPanelExtra = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectivePanelExtra));
                OnPropertyChanged(nameof(GeneratedSpecName));
            }
        }

        private string _customPanelExtra = "With Fanhole and Argon Gas As Per DWG";
        public string CustomPanelExtra
        {
            get => _customPanelExtra;
            set
            {
                _customPanelExtra = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectivePanelExtra));
                OnPropertyChanged(nameof(GeneratedSpecName));
            }
        }

        public string EffectivePanelExtra => UseCustomPanelExtra
            ? (CustomPanelExtra ?? string.Empty).Trim()
            : (PanelExtra ?? string.Empty).Trim();

        // ===================== Row 4: Inner Glass =====================

        private string _innerThickness;
        public string InnerThickness
        {
            get => _innerThickness;
            set
            {
                _innerThickness = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
                OnInnerGlassChanged();   // PHASE A: Sheet Store refresh
            }
        }

        private string _innerGlassColor;
        public string InnerGlassColor
        {
            get => _innerGlassColor;
            set
            {
                _innerGlassColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
                OnInnerGlassChanged();   // PHASE A: Sheet Store refresh
            }
        }

        private string _innerTreatment;
        public string InnerTreatment
        {
            get => _innerTreatment;
            set
            {
                _innerTreatment = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
            }
        }

        private decimal _innerBasePrice;
        public decimal InnerBasePrice
        {
            get => _innerBasePrice;
            set { _innerBasePrice = value; OnPropertyChanged(); }
        }

        // ===================== Row 5: Yield + Margin =====================

        private decimal _yieldFactor;
        public decimal YieldFactor
        {
            get => _yieldFactor;
            set { _yieldFactor = value; OnPropertyChanged(); }
        }

        private decimal _marginProfitPercent;
        public decimal MarginProfitPercent
        {
            get => _marginProfitPercent;
            set { _marginProfitPercent = value; OnPropertyChanged(); }
        }

        // ===================== Auto-Generated Spec Name =====================

        /// <summary>
        /// Human-readable spec name auto-built from current DGU parameters.
        /// Format: "{Outer} + {Spacer}[ {SpacerExtra}] + {Inner}[ {PanelExtra}]"
        ///
        /// Examples:
        ///   "6mm Clear FT Glass + 12mm ASP + 6mm Clear FT Glass"
        ///   "6mm HD Grey FT Glass + 12mm ASP With U-Insert + 6mm Clear FT Glass"
        ///   "6mm Clear FT Glass + 12mm ASP + 6mm Clear FT Glass With Overlap (LM) and Silicon Bed (LM) As Per DWG"
        ///   "6mm HD Grey FT Glass + 12mm ASP With U-Insert +
        ///    6mm Clear FT Glass With Argon Gas As Per DWG"
        ///
        /// SpacerExtra and PanelExtra suffixes are appended only when
        /// they are not "None".
        /// Saved to specifications.db when user clicks APPLY in main VM.
        /// </summary>
        public string GeneratedSpecName
        {
            get
            {
                // Outer glass segment
                var outer = $"{OuterThickness} {OuterGlassColor} {OuterTreatment} Glass";

                // Spacer segment — may include a spacer extra (U-Insert variants)
                var spacer = SpacerWidth ?? "";
                if (!string.IsNullOrWhiteSpace(SpacerExtra) &&
                    !SpacerExtra.Equals("None", System.StringComparison.OrdinalIgnoreCase))
                {
                    spacer += $" {SpacerExtra}";
                }

                // Inner glass segment — may include a panel extra
                // (Overlap, Fanhole, Cutout, Holes, Shutter Bonding, Argon Gas)
                var inner = $"{InnerThickness} {InnerGlassColor} {InnerTreatment} Glass";
                var panelExtra = EffectivePanelExtra;
                if (!string.IsNullOrWhiteSpace(panelExtra) &&
                    !panelExtra.Equals("None", System.StringComparison.OrdinalIgnoreCase))
                {
                    inner += $" {panelExtra}";
                }

                return $"{outer} + {spacer} + {inner}";
            }
        }

        // ===================== Dropdown Options =====================

        public ObservableCollection<string> ThicknessOptions { get; } = new()
        {
            "3mm", "4mm", "5mm", "6mm", "8mm", "10mm", "12mm", "15mm", "19mm"
        };

        // 2026-09-10: built-in colour defaults REMOVED by request.
        // Filled exclusively from Sheet Store inventory — see .SheetStore.cs
        public ObservableCollection<string> ColorOptions { get; } = new();

        public List<string> TreatmentOptions { get; } = new()
        {
            "FT",
            "Annealed",
            "Heat Strengthened",
            "Heat Soaked"
        };

        public List<string> SpacerWidthOptions { get; } = new()
        {
            "6mm ASP",
            "8mm ASP",
            "10mm ASP",
            "12mm ASP",
            "14mm ASP",
            "16mm ASP",
            "18mm ASP",
            "20mm ASP",
            "22mm ASP",
            "24mm ASP",
            "26mm ASP",
            "28mm ASP",
            "30mm ASP",
            "32mm ASP"
        };

        public List<string> SpacerMaterialOptions { get; } = new()
        {
            "Normal Mill Aluminum",
            "Black Aluminum",
            "Warm Edge (Super Spacer)",
            "Black Warm Edge",
            "Stainless Steel",
            "TGI Spacer"
        };

        // ══════════════════════════════════════════════════════════════════
        //  HOTFIX-3: SPACER EXTRA — 3 options (U-Insert variants only)
        //  Placed AFTER spacer in generated spec name.
        // ══════════════════════════════════════════════════════════════════
        public List<string> SpacerExtraOptions { get; } = new()
        {
            "None",
            "With U-Insert",
            "With U-Insert As Per DWG"
        };

        // ══════════════════════════════════════════════════════════════════
        //  HOTFIX-3: PANEL EXTRA — 19 options (all real DGU add-ons)
        //  Includes both the normal overlap+silicon LM mode and the
        //  special SQM overlap variants requested by the business.
        //  Placed AFTER inner glass in generated spec name.
        // ══════════════════════════════════════════════════════════════════
        public List<string> PanelExtraOptions { get; } = new()
        {
            "None",
            "With Overlap",
            "With Overlap As Per DWG",
            "With Overlap As Per Details",
            "With Overlap (SQM)",
            "With Overlap (SQM) As Per DWG",
            "With Fanhole",
            "With Fanhole As Per DWG",
            "With Cutout",
            "With Cutout As Per DWG",
            "With Holes",
            "With Holes As Per DWG",
            "With Cutout And Holes",
            "With Cutout And Holes As Per DWG",
            "With Shutter Bonding",
            "With Shutter Bonding As Per DWG",
            "With Argon Gas",
            "With Argon Gas As Per DWG"
        };

        // ===================== Constructor =====================

        public DguParametersViewModel()
        {
            // Row 1 defaults
            _outerThickness = "6mm";
            _outerGlassColor = "Clear";
            _outerTreatment = "FT";
            _outerBasePrice = 46m;

            // Row 2 defaults
            _spacerWidth = "12mm ASP";
            _spacerMaterial = "Normal Mill Aluminum";
            _spacerPrice = CalculateSuggestedSpacerPrice(_spacerWidth, _spacerMaterial);

            // Row 3 defaults — HOTFIX-3
            _spacerExtra = "None";
            _panelExtra = "None";

            // Row 4 defaults
            _innerThickness = "6mm";
            _innerGlassColor = "Clear";
            _innerTreatment = "FT";
            _innerBasePrice = 46m;

            // Row 5 defaults
            _yieldFactor = 0.85m;
            _marginProfitPercent = 15m;
        }

        // ===================== Auto-Suggest Spacer Price =====================

        private void SuggestSpacerPrice()
        {
            if (_isSuggestingSpacerPrice) return;
            _isSuggestingSpacerPrice = true;
            SpacerPrice = CalculateSuggestedSpacerPrice(_spacerWidth, _spacerMaterial);
            _isSuggestingSpacerPrice = false;
        }

        private decimal CalculateSuggestedSpacerPrice(string widthText, string material)
        {
            int widthMm = ParseWidthMm(widthText);
            decimal basePrice = 37.5m + ((widthMm - 6) / 2m) * 2.5m;
            decimal materialSurcharge = GetMaterialSurcharge(material);
            return basePrice + materialSurcharge;
        }

        private int ParseWidthMm(string widthText)
        {
            if (string.IsNullOrWhiteSpace(widthText)) return 12;
            var mmIndex = widthText.IndexOf("mm", System.StringComparison.OrdinalIgnoreCase);
            if (mmIndex <= 0) return 12;
            var digits = widthText.Substring(0, mmIndex).Trim();
            return int.TryParse(digits, out int mm) ? mm : 12;
        }

        private decimal GetMaterialSurcharge(string material)
        {
            return material switch
            {
                "Normal Mill Aluminum" => 0m,
                "Black Aluminum" => 5m,
                "Warm Edge (Super Spacer)" => 15m,
                // PHASE A: black-finish warm edge — premium over the
                // standard warm edge spacer (matched to supplier quotes).
                "Black Warm Edge" => 20m,
                "Stainless Steel" => 10m,
                "TGI Spacer" => 12m,
                _ => 0m
            };
        }
    }
}