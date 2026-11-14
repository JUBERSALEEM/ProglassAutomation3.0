// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  ViewModels/ProformaInvoice/Step4PricingEstimator/LamParameters/         ║
// ║    LamParametersViewModel.cs                                             ║
// ║                                                                          ║
// ║  CHAT 13 HOTFIX-2 (revised):                                             ║
// ║    Lamination Polish dropdown expanded to 13 real polish types           ║
// ║    used in UAE glass fabrication — normal + "As Per DWG" variants:      ║
// ║                                                                          ║
// ║       1. None                                                            ║
// ║       2. All Side Polish                                                 ║
// ║       3. All Side Polish As Per DWG                                      ║
// ║       4. Both Height Side Polish                                         ║
// ║       5. Both Height Side Polish As Per DWG                              ║
// ║       6. Both Width Side Polish                                          ║
// ║       7. Both Width Side Polish As Per DWG                               ║
// ║       8. Both Height + One Width Side Polish                             ║
// ║       9. Both Height + One Width Side Polish As Per DWG                  ║
// ║      10. One Height + One Width Side Polish                              ║
// ║      11. One Height + One Width Side Polish As Per DWG                   ║
// ║      12. One Height Side Polish                                          ║
// ║      13. One Width Side Polish                                           ║
// ║                                                                          ║
// ║    Also updated GeneratedSpecName so it appends "With <polish>"          ║
// ║    only when polish != "None".                                           ║
// ║                                                                          ║
// ║  SCOPE:                                                                  ║
// ║    ✅ Dropdown options + spec name suffix only                           ║
// ║    ❌ NO changes to rate calculation (polish cost lives in "Other       ║
// ║        Charges" section — will be wired separately later)               ║
// ╚══════════════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.LamParameters
{
    /// <summary>
    /// ViewModel for LAM (Laminated Assembly) parameter panel.
    /// Handles outer/inner glass, PVB interlayer, per-pane cutting & tempering costs,
    /// yield factor, and margin.
    /// </summary>
    public partial class LamParametersViewModel : ViewModelBase
    {
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
                OnOuterGlassChanged();   // PHASE J: Sheet Store refresh
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
                OnOuterGlassChanged();   // PHASE J: Sheet Store refresh
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

        // ===================== Row 2: Inner Glass =====================

        private string _innerThickness;
        public string InnerThickness
        {
            get => _innerThickness;
            set
            {
                _innerThickness = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
                OnInnerGlassChanged();   // PHASE J: Sheet Store refresh
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
                OnInnerGlassChanged();   // PHASE J: Sheet Store refresh
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

        // ===================== Row 3: PVB Interlayer =====================

        private string _pvbThickness;
        public string PvbThickness
        {
            get => _pvbThickness;
            set
            {
                _pvbThickness = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
            }
        }

        private string _pvbColor;
        public string PvbColor
        {
            get => _pvbColor;
            set
            {
                _pvbColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
            }
        }

        private decimal _pvbMaterialRate;
        public decimal PvbMaterialRate
        {
            get => _pvbMaterialRate;
            set { _pvbMaterialRate = value; OnPropertyChanged(); }
        }

        private string _laminationPolish;
        public string LaminationPolish
        {
            get => _laminationPolish;
            set
            {
                _laminationPolish = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GeneratedSpecName));
            }
        }

        // ===================== Row 4: Per-Pane Cutting & Tempering Costs =====================

        private decimal _outerCuttingCost;
        public decimal OuterCuttingCost
        {
            get => _outerCuttingCost;
            set { _outerCuttingCost = value; OnPropertyChanged(); }
        }

        private decimal _innerCuttingCost;
        public decimal InnerCuttingCost
        {
            get => _innerCuttingCost;
            set { _innerCuttingCost = value; OnPropertyChanged(); }
        }

        private decimal _outerTemperingCost;
        public decimal OuterTemperingCost
        {
            get => _outerTemperingCost;
            set { _outerTemperingCost = value; OnPropertyChanged(); }
        }

        private decimal _innerTemperingCost;
        public decimal InnerTemperingCost
        {
            get => _innerTemperingCost;
            set { _innerTemperingCost = value; OnPropertyChanged(); }
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
        /// Human-readable spec name auto-built from current LAM parameters.
        /// Format: "{Outer Glass} + {PVB Layer} + {Inner Glass}[ With {Polish}]"
        ///
        /// Examples:
        ///   "6mm Clear FT Glass + 1.52mm Clear PVB + 6mm Clear FT Glass"
        ///   "6mm Clear FT Glass + 1.52mm Clear PVB + 6mm Clear FT Glass With All Side Polish"
        ///   "6mm Clear FT Glass + 0.76mm Frosted PVB + 6mm Clear FT Glass With Both Height Side Polish As Per DWG"
        ///
        /// The " With <polish>" suffix is appended only when polish is not "None".
        /// Saved to specifications.db when user clicks APPLY in main VM.
        /// </summary>
        public string GeneratedSpecName
        {
            get
            {
                var outer = $"{OuterThickness} {OuterGlassColor} {OuterTreatment} Glass";
                var pvb = $"{PvbThickness} {PvbColor} PVB";
                var inner = $"{InnerThickness} {InnerGlassColor} {InnerTreatment} Glass";

                var baseName = $"{outer} + {pvb} + {inner}";

                // HOTFIX-2: Only append when a real polish is selected.
                // "None" means no polish → no suffix.
                if (!string.IsNullOrWhiteSpace(LaminationPolish) &&
                    !LaminationPolish.Equals("None", System.StringComparison.OrdinalIgnoreCase))
                {
                    baseName += $" With {LaminationPolish}";
                }

                return baseName;
            }
        }

        // ===================== Shared Dropdown Options =====================

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

        // ===================== LAM-Specific Dropdown Options =====================

        public List<string> PvbThicknessOptions { get; } = new()
        {
            "0.38mm",
            "0.76mm",
            "1.14mm",
            "1.52mm",
            "2.28mm",
            "3.04mm"
        };

        public List<string> PvbColorOptions { get; } = new()
        {
            "Clear",
            "White",
            "Frosted",
            "Milky White",
            "Black",
            "Grey",
            "Bronze"
        };

        // ══════════════════════════════════════════════════════════════════
        //  HOTFIX-2: 13 real polish options used in UAE glass fabrication.
        //  Formulas (for future "Other Charges" LM calculation, NOT here):
        //
        //    None                                              → 0 LM
        //    All Side Polish                                   → (W + H) / 1000 × 2
        //    All Side Polish As Per DWG                        → (W + H) / 1000 × 2
        //    Both Height Side Polish                           → H / 1000 × 2
        //    Both Height Side Polish As Per DWG                → H / 1000 × 2
        //    Both Width Side Polish                            → W / 1000 × 2
        //    Both Width Side Polish As Per DWG                 → W / 1000 × 2
        //    Both Height + One Width Side Polish               → (H × 2 + W) / 1000
        //    Both Height + One Width Side Polish As Per DWG    → (H × 2 + W) / 1000
        //    One Height + One Width Side Polish                → (H + W) / 1000
        //    One Height + One Width Side Polish As Per DWG     → (H + W) / 1000
        //    One Height Side Polish                            → H / 1000
        //    One Width Side Polish                             → W / 1000
        //
        //  "As Per DWG" variants use the same LM formula as their normal
        //  counterpart — the difference is only WHICH specific edges get
        //  polished (customer's drawing dictates location).
        // ══════════════════════════════════════════════════════════════════
        public List<string> LaminationPolishOptions { get; } = new()
        {
            "None",
            "All Side Polish",
            "All Side Polish As Per DWG",
            "Both Height Side Polish",
            "Both Height Side Polish As Per DWG",
            "Both Width Side Polish",
            "Both Width Side Polish As Per DWG",
            "Both Height + One Width Side Polish",
            "Both Height + One Width Side Polish As Per DWG",
            "One Height + One Width Side Polish",
            "One Height + One Width Side Polish As Per DWG",
            "One Height Side Polish",
            "One Width Side Polish"
        };

        public LamParametersViewModel()
        {
            // === Row 1 Defaults ===
            OuterThickness = "6mm";
            OuterGlassColor = "Clear";
            OuterTreatment = "FT";
            OuterBasePrice = 46m;

            // === Row 2 Defaults ===
            InnerThickness = "6mm";
            InnerGlassColor = "Clear";
            InnerTreatment = "FT";
            InnerBasePrice = 46m;

            // === Row 3 Defaults ===
            PvbThickness = "1.52mm";
            PvbColor = "Clear";
            PvbMaterialRate = 80m;
            LaminationPolish = "None";   // HOTFIX-2: matches new dropdown

            // === Row 4 Defaults (per-pane cutting + tempering) ===
            OuterCuttingCost = 15m;
            InnerCuttingCost = 15m;
            OuterTemperingCost = 25m;
            InnerTemperingCost = 25m;

            // === Row 5 Defaults ===
            YieldFactor = 0.85m;
            MarginProfitPercent = 15m;
        }
    }
}