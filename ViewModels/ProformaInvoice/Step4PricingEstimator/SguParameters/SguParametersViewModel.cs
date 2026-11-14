using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.SguParameters
{
    /// <summary>
    /// ViewModel for SGU (Single Glass Unit) parameter panel.
    /// Handles thickness, color, treatment, pricing factors,
    /// processing treatments, and live rate calculation.
    ///
    /// FIX 1: GeneratedSpecName no longer appends " Glass" —
    ///        TreatmentOptions already contain "Glass" suffix
    ///        (e.g. "FT Glass") producing correct "6mm Clear FT Glass".
    ///        Polish suffix appended when not "None (Standard)".
    ///
    /// FIX 2: PolishOptions expanded to cover real UAE glass industry
    ///        fabrication types — holes, cutouts, DWG refs, edge polish.
    /// </summary>
    public partial class SguParametersViewModel : ViewModelBase
    {
        // === Parameter Row 1: Thickness, Color, Treatment, Base Price ===

        private string _selectedThickness;
        public string SelectedThickness
        {
            get => _selectedThickness;
            set
            {
                _selectedThickness = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
                OnGlassChanged();   // PHASE A: Sheet Store refresh
            }
        }

        private string _glassColor;
        public string GlassColor
        {
            get => _glassColor;
            set
            {
                _glassColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
                OnGlassChanged();   // PHASE A: Sheet Store refresh
            }
        }

        private string _selectedTreatment;
        public string SelectedTreatment
        {
            get => _selectedTreatment;
            set
            {
                _selectedTreatment = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
            }
        }

        private decimal _basePricePerSqm;
        public decimal BasePricePerSqm
        {
            get => _basePricePerSqm;
            set { _basePricePerSqm = value; OnPropertyChanged(); }
        }

        // === Parameter Row 2: Cutting, Tempering, Yield, Margin ===

        private decimal _cuttingCostPerSqm;
        public decimal CuttingCostPerSqm
        {
            get => _cuttingCostPerSqm;
            set { _cuttingCostPerSqm = value; OnPropertyChanged(); }
        }

        private decimal _temperingCostPerSqm;
        public decimal TemperingCostPerSqm
        {
            get => _temperingCostPerSqm;
            set { _temperingCostPerSqm = value; OnPropertyChanged(); }
        }

        private decimal _yieldFactor;
        public decimal YieldFactor
        {
            get => _yieldFactor;
            set { _yieldFactor = value; OnPropertyChanged(); }
        }

        private decimal _targetMarginPercent;
        public decimal TargetMarginPercent
        {
            get => _targetMarginPercent;
            set { _targetMarginPercent = value; OnPropertyChanged(); }
        }

        // === Parameter Row 3: Polish / Detail Option ===

        private string _selectedPolishOption;
        public string SelectedPolishOption
        {
            get => _selectedPolishOption;
            set
            {
                _selectedPolishOption = value;
                OnPropertyChanged();
                // FIX 1: polish also affects the generated spec name
                OnPropertyChanged(nameof(GeneratedSpecName));
            }
        }

        // ===================== Auto-Generated Spec Name =====================

        /// <summary>
        /// Human-readable spec name auto-built from current parameters.
        /// Format: "{Thickness} {Color} {Treatment}[ {Polish}]"
        /// Examples:
        ///   Base:            "6mm Clear FT Glass"
        ///   With polish:     "6mm Clear FT Glass With Fanhole"
        ///   With DWG ref:    "6mm Clear FT Glass With Holes As Per DWG"
        /// Saved to specifications.db when user clicks APPLY in main VM.
        ///
        /// FIX 1: No longer appends redundant " Glass" — TreatmentOptions
        ///        already contain "Glass" suffix (e.g. "FT Glass").
        /// FIX 2: Polish suffix appended only when meaningful
        ///        (skipped for "None (Standard)").
        /// </summary>
        public string GeneratedSpecName
        {
            get
            {
                string baseName =
                    $"{SelectedThickness} {GlassColor} {SelectedTreatment}"
                    .Trim();

                // Append polish suffix only when it adds meaning
                if (!string.IsNullOrWhiteSpace(SelectedPolishOption) &&
                    SelectedPolishOption != "None (Standard)")
                {
                    baseName += $" {SelectedPolishOption}";
                }

                if (!string.IsNullOrWhiteSpace(EffectivePanelExtra)
                    && !string.Equals(EffectivePanelExtra, "None", System.StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(EffectivePanelExtra, SelectedPolishOption, System.StringComparison.OrdinalIgnoreCase))
                    baseName += " " + EffectivePanelExtra;

                return baseName;
            }
        }

        // Fabrication notes are independent of edge polish, like DGU's panel extra.
        private string _panelExtra = "None";
        public string PanelExtra
        {
            get => _panelExtra;
            set { if (_panelExtra == value) return; _panelExtra = value ?? "None"; NotifyPanelExtra(); }
        }
        private bool _useCustomPanelExtra;
        public bool UseCustomPanelExtra
        {
            get => _useCustomPanelExtra;
            set { if (_useCustomPanelExtra == value) return; _useCustomPanelExtra = value; NotifyPanelExtra(); }
        }
        private string _customPanelExtra = "";
        public string CustomPanelExtra
        {
            get => _customPanelExtra;
            set { if (_customPanelExtra == value) return; _customPanelExtra = value ?? ""; NotifyPanelExtra(); }
        }
        public string EffectivePanelExtra => (UseCustomPanelExtra ? CustomPanelExtra : PanelExtra).Trim();
        private void NotifyPanelExtra()
        {
            OnPropertyChanged(nameof(PanelExtra));
            OnPropertyChanged(nameof(UseCustomPanelExtra));
            OnPropertyChanged(nameof(CustomPanelExtra));
            OnPropertyChanged(nameof(EffectivePanelExtra));
            OnPropertyChanged(nameof(GeneratedSpecName));
        }
        // No spacer/argon options: a single pane has no sealed cavity.
        public List<string> PanelExtraOptions { get; } = new()
        {
            "None", "With Overlap", "With Overlap As Per DWG", "With Fanhole",
            "With Fanhole As Per DWG", "With Cutout", "With Cutout As Per DWG",
            "With Holes", "With Holes As Per DWG", "With Cutout And Holes",
            "With Cutout And Holes As Per DWG", "With Shutter Bonding", "With Shutter Bonding As Per DWG"
        };

        // === Dropdown Options ===

        public ObservableCollection<string> ThicknessOptions { get; } = new ObservableCollection<string>
        {
            "3mm", "4mm", "5mm", "6mm", "8mm", "10mm", "12mm", "15mm", "19mm"
        };

        // 2026-09-10: built-in colour defaults REMOVED by request.
        // The combo is filled exclusively from Sheet Store inventory
        // (LoadColorsIntoAsync) — the dropdown must not suggest glass
        // the store does not actually stock.
        public ObservableCollection<string> ColorOptions { get; } = new();

        public List<string> TreatmentOptions { get; } = new List<string>
        {
            "FT Glass",
            "Annealed",
            "Heat Strengthened",
            "Heat Soaked"
        };

        /// <summary>
        /// UAE glass industry standard fabrication options.
        /// Selected value is appended to spec name when != "None (Standard)".
        /// Grouped logically:
        ///   • Default            : None (Standard)
        ///   • Edge finishes      : Polished Edges, Beveled, Mitring
        ///   • Surface treatments : Frosted, Sandblasted
        ///   • Fabrication holes  : Fanhole, Holes, Holes & Cutouts, Cutouts
        ///   • Polish variants    : All Side, Both Height Side
        ///   • DWG references     : "As Per DWG" suffix variants
        /// </summary>
        public List<string> PolishOptions { get; } = new List<string>
        {
            // Default
            "None (Standard)",

            // Edge finishes
            "Polished Edges",
            "Beveled Edges",
            "Mitring (45° Bevel)",

            // Surface treatments
            "Frosted",
            "Sandblasted",

            // Fabrication — Fanhole
            "With Fanhole",
            "With Fanhole As Per DWG",

            // Fabrication — Holes
            "With Holes",
            "With Holes As Per DWG",

            // Fabrication — Holes + Cutouts
            "With Holes And Cutouts",
            "With Holes And Cutouts As Per DWG",

            // Fabrication — Cutouts
            "With Cutouts",
            "With Cutouts As Per DWG",

            // Polish variants
            "With All Side Polish",
            "With Both Height Side Polish",
            "With Both Height Side Polish As Per DWG"
        };

        public SguParametersViewModel()
        {
            // === Row 1 Defaults ===
            SelectedThickness = "6mm";
            GlassColor = "Clear";
            SelectedTreatment = "FT Glass";
            BasePricePerSqm = 46m;

            // === Row 2 Defaults ===
            CuttingCostPerSqm = 15m;
            TemperingCostPerSqm = 25m;
            YieldFactor = 0.85m;
            TargetMarginPercent = 15m;

            // === Row 3 Defaults ===
            SelectedPolishOption = "None (Standard)";
        }
    }
}