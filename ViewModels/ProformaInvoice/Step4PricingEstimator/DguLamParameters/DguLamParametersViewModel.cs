// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/.../DguLamParameters/DguLamParametersViewModel.cs
// PROJECT: ProGlassAutomation — Step 4 Pricing Estimator
//
// DGU + LAMINATION — a THREE-PANE hybrid.
//
//     6mm HD Grey FT Glass          <- outer pane
//   + 12mm ASP                      <- air spacer (12 / 16 / other)
//   + 6mm Clear FT Glass            <- laminated OUTER pane
//   + 1.52mm Clear PVB              <- interlayer (PVB / SGP / EVA)
//   + 6mm Clear FT Glass            <- laminated INNER pane
//
// This is NOT "a DGU with one extra layer". It carries THREE sheets of
// glass, so any code that assumes outer+inner will under-cost it by a
// whole pane — the same defect class that dropped the inner pane from
// every DGU before it was fixed.
//
// ═══════════════════════════════════════════════════════════════════════
//  PRICING RULES — TAKEN FROM THE BUSINESS'S OWN WORKED CHAIN
// ═══════════════════════════════════════════════════════════════════════
//
// The business supplied this calculation line by line, and the code
// reproduces it exactly:
//
//     48 + 25 + 25                        = 98      three panes of glass
//     98 / 0.85 (yield)                   = 115.29
//     + cutting     10 + 10 + 10   (30)   = 145.29
//     + tempering   12 + 10 + 10   (32)   = 177.29
//     + 110 outsourced PVB                = 287.29
//     x 1.20 (20% profit)                 = 344.75  per SQM
//
// 1. CUTTING AND TEMPERING ARE CHARGED ON ALL THREE PANES.
//    Not just the two laminated ones. Each pane carries its own rate and
//    they are not equal: HD Grey tempers at 12 while the two Clear panes
//    temper at 10. Per-pane fields are therefore required — a single
//    shared rate could not express this and would misprice every unit
//    whose panes differ.
//
// 2. THE AIR SPACER IS NOT A LINE IN THIS PRICE.
//    The chain goes straight from tempering to the interlayer with no 45
//    anywhere. Including it would produce 398.75 instead of 344.75, so
//    SpacerPrice defaults to 0 and is excluded from the rate. The spacer
//    width still appears in the spec name because it describes the
//    product, and the field is left editable in case a job needs it
//    priced. It is opt-in, never silently added.
//
// 3. THE INTERLAYER IS ADDED BEFORE THE MARGIN, AND IS MARKED UP.
//    The 110 enters the subtotal and is then multiplied by 1.20. The
//    business is explicit about this, so the earlier reading — that 110
//    was a fixed pass-through added after the margin — was wrong. Both
//    buy and sell prices are still stored: sell drives this quote, buy is
//    what the Profit and Loss step costs the job against.
//
// 4. THE YIELD FACTOR DIVIDES GLASS ONLY.
//    98 / 0.85 happens before anything else is added. Processing and the
//    interlayer sit outside it.
//
// If any of this changes, CalculateRate() below is the single place to
// edit — it is written as numbered steps for exactly that reason.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.ProformaInvoice;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguLamParameters
{
    /// <summary>Parameters for a DGU + laminated hybrid unit.</summary>
    public partial class DguLamParametersViewModel : ViewModelBase
    {
        // ══════════════════════════════════════════════════════════════
        //  DROPDOWN SOURCES
        // ══════════════════════════════════════════════════════════════

        public List<string> ThicknessOptions { get; } = new()
        {
            "3mm", "4mm", "5mm", "6mm", "8mm", "10mm", "12mm", "16mm", "19mm"
        };

        public List<string> TreatmentOptions { get; } = new()
        {
            "FT", "Annealed", "Heat Strengthened", "Heat Soaked"
        };

        /// <summary>Air spacer widths. 12mm and 16mm are the common ones.</summary>
        public List<string> SpacerOptions { get; } = new()
        {
            "6mm", "8mm", "9mm", "12mm", "14mm", "16mm", "18mm", "20mm"
        };

        /// <summary>
        /// Interlayer chemistries. All three are outsourced — ProGlass does
        /// not laminate in-house — so each carries its own bought and sold
        /// price rather than a single rate.
        /// </summary>
        public List<string> InterlayerTypeOptions { get; } = new()
        {
            "PVB", "SGP", "EVA"
        };

        public List<string> InterlayerThicknessOptions { get; } = new()
        {
            "0.38mm", "0.76mm", "1.14mm", "1.52mm", "2.28mm"
        };

        // 2026-09-10: built-in colour defaults REMOVED by request; the
        // GLASS PANE combos read the SAME Sheet Store colours as the other
        // panels (LoadStoreColorsAsync, fired from PricingEstimator's
        // LoadSheetStoreDataAsync). ObservableCollection so the combos
        // refresh when inventory changes.
        public System.Collections.ObjectModel.ObservableCollection<string>
            ColorOptions { get; } = new();

        // Cross-check fix (2026-09-10): the INTERLAYER is a PVB film, not a
        // stocked glass sheet — it must keep the classic interlayer palette
        // instead of inheriting the glass inventory list.
        public System.Collections.ObjectModel.ObservableCollection<string>
            InterlayerColorOptions { get; } = new(
                new[] { "Clear", "Frosted", "Milky White", "Grey", "Bronze", "Blue", "Green" });

        /// <summary>
        /// Kept for the previous colour-only load path. Sheet sizes and
        /// prices now live in the SheetStore partial.
        /// </summary>
        public Task LoadStoreColorsAsync() => LoadSheetStoreAsync();

        // ══════════════════════════════════════════════════════════════
        //  FABRICATION EXTRAS
        //
        //  The hybrid is a DGU and a laminate at once, so it can carry the
        //  add-ons of BOTH. These three lists are the same ones the DGU and
        //  LAM panels already offer, kept identical on purpose: a spec that
        //  reads one way on the DGU screen must read the same way here, or
        //  the same physical product ends up with two different names in
        //  the database.
        //
        //  SPACER EXTRA  — from DGU (U-Insert on the sealed unit)
        //  PANEL EXTRA   — from DGU (overlap, cutouts, holes, argon…)
        //  EDGE POLISH   — from LAM (which edges get polished)
        //
        //  "As Per DWG" variants exist throughout because the shop needs to
        //  know whether the detail follows a drawing rather than a default.
        // ══════════════════════════════════════════════════════════════

        public List<string> SpacerExtraOptions { get; } = new()
        {
            "None",
            "With U-Insert",
            "With U-Insert As Per DWG"
        };

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

        public List<string> EdgePolishOptions { get; } = new()
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

        private string _spacerExtra = "None";
        public string SpacerExtra
        {
            get => _spacerExtra;
            set { if (_spacerExtra == value) return; _spacerExtra = value; Changed(); }
        }

        private string _panelExtra = "None";
        public string PanelExtra
        {
            get => _panelExtra;
            set { if (_panelExtra == value) return; _panelExtra = value; Changed(); OnPropertyChanged(nameof(EffectivePanelExtra)); }
        }

        private bool _useCustomPanelExtra;
        public bool UseCustomPanelExtra
        {
            get => _useCustomPanelExtra;
            set { if (_useCustomPanelExtra == value) return; _useCustomPanelExtra = value; Changed(); OnPropertyChanged(nameof(EffectivePanelExtra)); }
        }

        private string _customPanelExtra = "With Fanhole and Argon Gas As Per DWG";
        public string CustomPanelExtra
        {
            get => _customPanelExtra;
            set { if (_customPanelExtra == value) return; _customPanelExtra = value ?? string.Empty; Changed(); OnPropertyChanged(nameof(EffectivePanelExtra)); }
        }

        public string EffectivePanelExtra => UseCustomPanelExtra
            ? (CustomPanelExtra ?? string.Empty).Trim()
            : (PanelExtra ?? string.Empty).Trim();

        private string _edgePolish = "None";
        public string EdgePolish
        {
            get => _edgePolish;
            set { if (_edgePolish == value) return; _edgePolish = value; Changed(); }
        }

        // ══════════════════════════════════════════════════════════════
        //  PANE 1 — OUTER (in front of the air spacer)
        // ══════════════════════════════════════════════════════════════

        private string _outerThickness = "6mm";
        public string OuterThickness
        {
            get => _outerThickness;
            set { if (_outerThickness == value) return; _outerThickness = value; Changed(); OnOuterGlassChanged(); }
        }

        private string _outerGlassColor = "HD Grey";

        public string OuterGlassColor
        {
            get => _outerGlassColor;
            set
            {
                if (_outerGlassColor == value) return;
                _outerGlassColor = value;
                Changed();
                OnOuterGlassChanged();
            }
        }

        private string _outerTreatment = "FT";
        public string OuterTreatment
        {
            get => _outerTreatment;
            set { if (_outerTreatment == value) return; _outerTreatment = value; Changed(); }
        }

        private decimal _outerBasePrice = 48m;
        /// <summary>AED per SQM. Auto-filled from Sheet Store selling price.</summary>
        public decimal OuterBasePrice
        {
            get => _outerBasePrice;
            set { if (_outerBasePrice == value) return; _outerBasePrice = value; Changed(); }
        }

        // ══════════════════════════════════════════════════════════════
        //  AIR SPACER
        // ══════════════════════════════════════════════════════════════

        private string _spacerThickness = "12mm";
        public string SpacerThickness
        {
            get => _spacerThickness;
            set { if (_spacerThickness == value) return; _spacerThickness = value; Changed(); }
        }

        private decimal _spacerPrice = 0m;
        /// <summary>
        /// AED per SQM — NOT per linear metre. Costing a spacer per
        /// perimetre overstated it threefold once already.
        ///
        /// DEFAULTS TO ZERO for this hybrid. The business's worked chain
        /// runs tempering straight into the interlayer with no spacer
        /// charge; adding 45 would give 398.75 instead of 344.75. Set it
        /// only if a particular job is meant to carry it — it is opt-in,
        /// never silently applied.
        /// </summary>
        public decimal SpacerPrice
        {
            get => _spacerPrice;
            set { if (_spacerPrice == value) return; _spacerPrice = value; Changed(); }
        }

        // ══════════════════════════════════════════════════════════════
        //  PANE 2 — LAMINATED OUTER
        // ══════════════════════════════════════════════════════════════

        private string _lamOuterThickness = "6mm";
        public string LamOuterThickness
        {
            get => _lamOuterThickness;
            set { if (_lamOuterThickness == value) return; _lamOuterThickness = value; Changed(); OnLamOuterGlassChanged(); }
        }

        private string _lamOuterColor = "Clear";
        public string LamOuterColor
        {
            get => _lamOuterColor;
            set { if (_lamOuterColor == value) return; _lamOuterColor = value; Changed(); OnLamOuterGlassChanged(); }
        }

        private string _lamOuterTreatment = "FT";
        public string LamOuterTreatment
        {
            get => _lamOuterTreatment;
            set { if (_lamOuterTreatment == value) return; _lamOuterTreatment = value; Changed(); }
        }

        private decimal _lamOuterBasePrice = 25m;
        public decimal LamOuterBasePrice
        {
            get => _lamOuterBasePrice;
            set { if (_lamOuterBasePrice == value) return; _lamOuterBasePrice = value; Changed(); }
        }

        // ══════════════════════════════════════════════════════════════
        //  INTERLAYER — OUTSOURCED
        // ══════════════════════════════════════════════════════════════

        private string _interlayerType = "PVB";
        public string InterlayerType
        {
            get => _interlayerType;
            set
            {
                if (_interlayerType == value) return;
                _interlayerType = value;
                ApplyInterlayerDefaults();
                Changed();
                OnPropertyChanged(nameof(InterlayerMarginDisplay));
            }
        }

        private string _interlayerThickness = "1.52mm";
        public string InterlayerThickness
        {
            get => _interlayerThickness;
            set { if (_interlayerThickness == value) return; _interlayerThickness = value; Changed(); }
        }

        private string _interlayerColor = "Clear";
        public string InterlayerColor
        {
            get => _interlayerColor;
            set { if (_interlayerColor == value) return; _interlayerColor = value; Changed(); }
        }

        private decimal _interlayerBuyPrice = 100m;
        /// <summary>
        /// AED per SQM paid to the laminator. Never shown to the customer —
        /// this is what Step 9 costs the job against.
        /// </summary>
        public decimal InterlayerBuyPrice
        {
            get => _interlayerBuyPrice;
            set
            {
                if (_interlayerBuyPrice == value) return;
                _interlayerBuyPrice = value;
                Changed();
                OnPropertyChanged(nameof(InterlayerMarginDisplay));
            }
        }

        private decimal _interlayerSellPrice = 110m;
        /// <summary>
        /// AED per SQM charged to the customer. Added BEFORE the profit
        /// margin step and therefore marked up with the rest of the rate.
        /// </summary>
        public decimal InterlayerSellPrice
        {
            get => _interlayerSellPrice;
            set
            {
                if (_interlayerSellPrice == value) return;
                _interlayerSellPrice = value;
                Changed();
                OnPropertyChanged(nameof(InterlayerMarginDisplay));
            }
        }

        /// <summary>
        /// Shows the spread being earned on the outsourced interlayer, so a
        /// mistyped buy or sell price is visible before the quote goes out.
        /// </summary>
        public string InterlayerMarginDisplay
        {
            get
            {
                decimal spread = InterlayerSellPrice - InterlayerBuyPrice;

                if (InterlayerBuyPrice <= 0m)
                    return "Enter the bought price to see the spread.";

                if (spread < 0m)
                    return $"⚠ Selling BELOW cost by AED {Math.Abs(spread):N2}/SQM.";

                decimal pct = Math.Round(spread / InterlayerBuyPrice * 100m, 1);
                return $"Spread AED {spread:N2}/SQM ({pct:0.#}%).";
            }
        }

        /// <summary>
        /// Sensible starting prices per chemistry, from the figures the
        /// business quoted. SGP is left at zero on purpose: no price was
        /// given for it, and inventing one would put a fabricated number on
        /// a customer quote.
        /// </summary>
        private void ApplyInterlayerDefaults()
        {
            switch ((_interlayerType ?? "").ToUpperInvariant())
            {
                case "PVB":
                    _interlayerBuyPrice = 100m;
                    _interlayerSellPrice = 110m;
                    break;

                case "EVA":
                    _interlayerBuyPrice = 75m;
                    _interlayerSellPrice = 95m;
                    break;

                case "SGP":
                    _interlayerBuyPrice = 0m;
                    _interlayerSellPrice = 0m;
                    break;
            }

            OnPropertyChanged(nameof(InterlayerBuyPrice));
            OnPropertyChanged(nameof(InterlayerSellPrice));
        }

        // ══════════════════════════════════════════════════════════════
        //  PANE 3 — LAMINATED INNER
        // ══════════════════════════════════════════════════════════════

        private string _lamInnerThickness = "6mm";
        public string LamInnerThickness
        {
            get => _lamInnerThickness;
            set { if (_lamInnerThickness == value) return; _lamInnerThickness = value; Changed(); OnLamInnerGlassChanged(); }
        }

        private string _lamInnerColor = "Clear";
        public string LamInnerColor
        {
            get => _lamInnerColor;
            set { if (_lamInnerColor == value) return; _lamInnerColor = value; Changed(); OnLamInnerGlassChanged(); }
        }

        private string _lamInnerTreatment = "FT";
        public string LamInnerTreatment
        {
            get => _lamInnerTreatment;
            set { if (_lamInnerTreatment == value) return; _lamInnerTreatment = value; Changed(); }
        }

        private decimal _lamInnerBasePrice = 25m;
        public decimal LamInnerBasePrice
        {
            get => _lamInnerBasePrice;
            set { if (_lamInnerBasePrice == value) return; _lamInnerBasePrice = value; Changed(); }
        }

        // ══════════════════════════════════════════════════════════════
        //  PROCESSING — ONE CUTTING AND ONE TEMPERING RATE PER PANE
        //
        //  All three panes are charged (rule 1), and the rates are NOT
        //  interchangeable: in the business's own example HD Grey tempers
        //  at 12 while both Clear panes temper at 10. A single shared rate
        //  would misprice every unit whose panes differ, so each pane owns
        //  its own pair of fields.
        // ══════════════════════════════════════════════════════════════

        private decimal _outerCuttingCost = 10m;
        public decimal OuterCuttingCost
        {
            get => _outerCuttingCost;
            set { if (_outerCuttingCost == value) return; _outerCuttingCost = value; Changed(); }
        }

        private decimal _outerTemperingCost = 12m;
        /// <summary>HD Grey tempers dearer than Clear — 12 in the worked example.</summary>
        public decimal OuterTemperingCost
        {
            get => _outerTemperingCost;
            set { if (_outerTemperingCost == value) return; _outerTemperingCost = value; Changed(); }
        }

        private decimal _lamOuterCuttingCost = 10m;
        public decimal LamOuterCuttingCost
        {
            get => _lamOuterCuttingCost;
            set { if (_lamOuterCuttingCost == value) return; _lamOuterCuttingCost = value; Changed(); }
        }

        private decimal _lamOuterTemperingCost = 10m;
        public decimal LamOuterTemperingCost
        {
            get => _lamOuterTemperingCost;
            set { if (_lamOuterTemperingCost == value) return; _lamOuterTemperingCost = value; Changed(); }
        }

        private decimal _lamInnerCuttingCost = 10m;
        public decimal LamInnerCuttingCost
        {
            get => _lamInnerCuttingCost;
            set { if (_lamInnerCuttingCost == value) return; _lamInnerCuttingCost = value; Changed(); }
        }

        private decimal _lamInnerTemperingCost = 10m;
        public decimal LamInnerTemperingCost
        {
            get => _lamInnerTemperingCost;
            set { if (_lamInnerTemperingCost == value) return; _lamInnerTemperingCost = value; Changed(); }
        }

        /// <summary>Cutting across all three panes.</summary>
        public decimal TotalCuttingCost =>
            OuterCuttingCost + LamOuterCuttingCost + LamInnerCuttingCost;

        /// <summary>Tempering across all three panes.</summary>
        public decimal TotalTemperingCost =>
            OuterTemperingCost + LamOuterTemperingCost + LamInnerTemperingCost;

        // ══════════════════════════════════════════════════════════════
        //  YIELD + MARGIN
        // ══════════════════════════════════════════════════════════════

        private decimal _yieldFactor = 0.85m;
        /// <summary>Glass wastage recovery. Divides GLASS ONLY — see rule 2.</summary>
        public decimal YieldFactor
        {
            get => _yieldFactor;
            set { if (_yieldFactor == value) return; _yieldFactor = value; Changed(); }
        }

        private decimal _marginProfitPercent = 20m;   // 20% in the worked chain
        public decimal MarginProfitPercent
        {
            get => _marginProfitPercent;
            set { if (_marginProfitPercent == value) return; _marginProfitPercent = value; Changed(); }
        }

        // ══════════════════════════════════════════════════════════════
        //  SPEC NAME
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds the full specification string, e.g.
        ///
        ///   "6mm HD Grey FT Glass + 12mm ASP + 6mm Clear FT Glass
        ///    + 1.52mm Clear PVB + 6mm Clear FT Glass"
        ///
        /// with fabrication extras woven in exactly where the DGU and LAM
        /// panels put them, so the same physical product reads the same way
        /// whichever screen produced it:
        ///
        ///   spacer extra -> attached to the ASP segment
        ///       "… + 12mm ASP With U-Insert + …"
        ///   panel extra  -> attached to the LAST glass pane
        ///       "… + 6mm Clear FT Glass With Overlap (LM) and Silicon Bed (LM) As Per DWG"
        ///   edge polish  -> a " With …" suffix on the whole string
        ///       "… With Both Height Side Polish"
        ///
        /// Both an extra and a polish can apply at once, which is why the
        /// polish is appended last rather than merged into a pane:
        ///   "… With Overlap (LM) and Silicon Bed (LM) As Per DWG With All Side Polish"
        ///
        /// The layer order is load-bearing: Step 9 parses this string to
        /// decide how many panes to cost, so the three "… Glass" segments
        /// and the two separators must survive any edit here.
        /// </summary>
        public string GeneratedSpecName
        {
            get
            {
                static bool Has(string v) =>
                    !string.IsNullOrWhiteSpace(v) &&
                    !v.Equals("None", StringComparison.OrdinalIgnoreCase);

                string outer = $"{OuterThickness} {OuterGlassColor} {OuterTreatment} Glass";

                string spacer = $"{SpacerThickness} ASP";
                if (Has(SpacerExtra)) spacer += $" {SpacerExtra}";

                string lamOuter = $"{LamOuterThickness} {LamOuterColor} {LamOuterTreatment} Glass";
                string interlayer = $"{InterlayerThickness} {InterlayerColor} {InterlayerType}";

                string lamInner = $"{LamInnerThickness} {LamInnerColor} {LamInnerTreatment} Glass";
                if (Has(EffectivePanelExtra)) lamInner += $" {EffectivePanelExtra}";

                string spec = $"{outer} + {spacer} + {lamOuter} + {interlayer} + {lamInner}";

                if (Has(EdgePolish)) spec += $" With {EdgePolish}";

                return spec;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  RATE
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// AED per SQM, following the business's own chain exactly:
        ///
        ///   1. glass    = outer + laminated outer + laminated inner
        ///   2. / yield  — glass only
        ///   3. + cutting on all three panes
        ///   4. + tempering on all three panes
        ///   5. + interlayer SELL price (outsourced)
        ///   6. × (1 + margin)
        ///
        ///   + spacer, only if one has been priced. Zero by default, so it
        ///     changes nothing unless deliberately entered.
        ///
        /// Worked example — 48/25/25 glass, yield 0.85, cutting 10+10+10,
        /// tempering 12+10+10, PVB 110, margin 20%:
        ///     98 / 0.85              = 115.29
        ///     + 30 cutting           = 145.29
        ///     + 32 tempering         = 177.29
        ///     + 110 PVB              = 287.29
        ///     × 1.20                 = 344.75 per SQM
        ///
        /// Computed in decimal, not floating point: the two disagree by a
        /// cent here, and a cent on a quote is a question from a customer.
        /// </summary>
        public decimal CalculateRate(out string warning)
        {
            warning = "";

            decimal yieldFactor = YieldFactor;

            if (yieldFactor <= 0m)
            {
                warning = $"⚠  Yield factor must be greater than 0 " +
                          $"(currently {yieldFactor:0.###}). Using 1.0 instead.";
                yieldFactor = 1m;
            }

            decimal glass = OuterBasePrice + LamOuterBasePrice + LamInnerBasePrice;

            decimal running = glass / yieldFactor;

            running += TotalCuttingCost;
            running += TotalTemperingCost;
            running += InterlayerSellPrice;

            // Zero unless this job has been given a spacer charge.
            running += SpacerPrice;

            decimal rate = running * (1m + (MarginProfitPercent / 100m));

            // An unpriced interlayer is the one mistake that silently
            // undercharges a whole job, so it is called out rather than
            // quietly producing a low rate.
            if (InterlayerSellPrice <= 0m)
            {
                string note = $"⚠  No selling price set for the {InterlayerType} " +
                              "interlayer, so it is NOT included in this rate.";

                warning = warning.Length > 0 ? warning + "  " + note : note;
            }

            return MoneyRounding.Round(rate);
        }

        /// <summary>Parameterless overload for callers that ignore warnings.</summary>
        public decimal CalculateRate() => CalculateRate(out _);

        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Raises the change notification for the property that called it,
        /// then for the two read-only values derived from every field.
        ///
        /// The caller's name MUST be forwarded explicitly. ViewModelBase
        /// fills the argument from [CallerMemberName], so relying on the
        /// default here would announce "Changed" — a property nothing binds
        /// to — and the edited box would silently stop updating.
        /// </summary>
        private void Changed([CallerMemberName] string? propertyName = null)
        {
            OnPropertyChanged(propertyName);
            OnPropertyChanged(nameof(GeneratedSpecName));
            OnPropertyChanged(nameof(EstimatedRateDisplay));

            // Computed read-onlys bound in the UI. Without these the
            // processing totals would freeze at their startup values while
            // the per-pane boxes changed underneath them.
            OnPropertyChanged(nameof(TotalCuttingCost));
            OnPropertyChanged(nameof(TotalTemperingCost));
        }

        public string EstimatedRateDisplay => $"AED {CalculateRate():N2} / SQM";
    }
}
