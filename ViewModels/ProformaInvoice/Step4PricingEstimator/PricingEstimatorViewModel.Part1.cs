// ═══════════════════════════════════════════════════════════════════════
// FILE: PricingEstimatorViewModel.Part1.cs (Phase 3.8 split — member census verified)
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Services.ProformaInvoice.SpecificationsRepository;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ProGlassAutomation.Services.Localization;
using ProGlassAutomation.Services.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator
{
        public partial class PricingEstimatorViewModel : ViewModelBase
    {
            private readonly IDialogService _dialogs = null!;
        private readonly ISpecificationsRepository _specs = null!;

        public PricingEstimatorViewModel(IDialogService? dialogs, ISpecificationsRepository? specs)
        {
            _dialogs = dialogs!;
            _specs = specs!;
        
            // ERR-633EEB: run shared initialization on EVERY construction
            // path. DI selects this (greediest) ctor, so without this call the
            // parameterless ctor's setup never ran and commands stayed null.
            InitializeCore();
        }

        // === Panel ViewModels (one per glass type) ===
        public SguParameters.SguParametersViewModel SguVM { get; private set; }
        public DguParameters.DguParametersViewModel DguVM { get; private set; }
        public LamParameters.LamParametersViewModel LamVM { get; private set; }
        public DguLamParameters.DguLamParametersViewModel DguLamVM { get; private set; }

        // === Glass Type Cards ===
        public ObservableCollection<GlassTypeCard> GlassTypes { get; private set; }

        private GlassTypeCard? _selectedGlassType;
        public bool IsRestoringPricingSnapshot => _isRestoringSnapshot;

        public GlassTypeCard? SelectedGlassType
        {
            get => _selectedGlassType;
            set
            {
                if (_selectedGlassType == value) return;

                if (_selectedGlassType != null)
                    _selectedGlassType.IsSelected = false;

                _selectedGlassType = value;

                if (_selectedGlassType != null)
                    _selectedGlassType.IsSelected = true;

                OnPropertyChanged();

                // Recalculate rate when glass type changes
                RecalculateEstimatedRate();
                if (!_isRestoringSnapshot)
                {
                    UpdateCurrentCalculationSectionSnapshot();
                    // The snapshot now carries the NEW glass type. Panel edits
                    // raise the fingerprint through OnAnyPanelPropertyChanged,
                    // but a pure glass-type switch edits no panel property —
                    // without this the hash (and therefore the Step 5
                    // force-sync) would not fire whenever the estimated rate
                    // happens to be unchanged, leaving Step 5 on the old type.
                    NotifySpecFingerprintChanged();
                }
            }
        }

        // === Processing Treatments (shared) ===

        private bool _polishedEdges;
        public bool PolishedEdges
        {
            get => _polishedEdges;
            set { _polishedEdges = value; OnPropertyChanged(); }
        }

        private bool _drillCutouts;
        public bool DrillCutouts
        {
            get => _drillCutouts;
            set { _drillCutouts = value; OnPropertyChanged(); }
        }

        private bool _countersunkHoles;
        public bool CountersunkHoles
        {
            get => _countersunkHoles;
            set { _countersunkHoles = value; OnPropertyChanged(); }
        }

        private bool _edgeDeletionLayer;
        public bool EdgeDeletionLayer
        {
            get => _edgeDeletionLayer;
            set { _edgeDeletionLayer = value; OnPropertyChanged(); }
        }

        // === Specification Target Groups (Step 4 preview only — see BUG-43) ===
        public ObservableCollection<SpecTargetGroup> SpecGroups { get; private set; }

        private SpecTargetGroup? _selectedCalculationSection;
        public SpecTargetGroup? SelectedCalculationSection
        {
            get => _selectedCalculationSection;
            private set
            {
                if (ReferenceEquals(_selectedCalculationSection, value)) return;
                _selectedCalculationSection = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentCalculationSectionLabel));
                OnPropertyChanged(nameof(TotalSpecsActive));
                OnPropertyChanged(nameof(CanRemoveCalculationSection));
                ProGlassAutomation.Utilities.AsyncUtility.InvalidateCommandsThrottled();
            }
        }

        public int TotalSpecsActive => SelectedCalculationSection == null ? 0 : 1;
        public int CalculationSectionCount => SpecGroups?.Count ?? 0;
        public bool CanRemoveCalculationSection => CalculationSectionCount > 1;
        public string CurrentCalculationSectionLabel => SelectedCalculationSection?.Name ?? "Spec Group 1";

        // === Estimated Rate (calculated live) ===

        private decimal _estimatedRatePerSqm;
        public decimal EstimatedRatePerSqm
        {
            get => _estimatedRatePerSqm;
            set
            {
                _estimatedRatePerSqm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCalculatedRate));
                ProGlassAutomation.Utilities.AsyncUtility.InvalidateCommandsThrottled();
            }
        }

        public bool HasCalculatedRate => EstimatedRatePerSqm > 0m;

        // BUG-40/41: warning message when yield factor is invalid
        private string _yieldFactorWarning = "";
        public string YieldFactorWarning
        {
            get => _yieldFactorWarning;
            private set
            {
                if (_yieldFactorWarning == value) return;
                _yieldFactorWarning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasYieldFactorWarning));
            }
        }
        public bool HasYieldFactorWarning => !string.IsNullOrEmpty(_yieldFactorWarning);

        // BUG-44: non-blocking status line for save success/failure
        // R-2: re-entry guard for ApplyRateAsync. ApplyRateCommand's
        // canExecute checks spec validity only, and the async lambda is
        // async void, so Execute() returns at the first await and a
        // second click would run SaveOrUpdateAsync concurrently
        // (double-counting UsageCount). Plain bool, not Interlocked:
        // commands are raised on the UI thread only.
        private bool _isApplyingRate;

        private string _applyStatusMessage = "";
        public string ApplyStatusMessage
        {
            get => _applyStatusMessage;
            private set
            {
                if (_applyStatusMessage == value) return;
                _applyStatusMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasApplyStatus));
            }
        }
        public bool HasApplyStatus => !string.IsNullOrEmpty(_applyStatusMessage);

        private bool _isRestoringSnapshot;
        public bool HasRestoredSnapshotState { get; private set; }

        // === Commands ===
        public ICommand SelectGlassTypeCommand { get; private set; }
        public ICommand ToggleSpecGroupCommand { get; private set; }
        public ICommand AddCalculationSectionCommand { get; private set; }
        public ICommand RemoveCalculationSectionCommand { get; private set; }
        public ICommand ApplyRateCommand { get; private set; }

        /// <summary>
        /// 2026-09-10: raised by the "Open Sheet Store — Add Sheet" button.
        /// MainViewModel wires this to a license-gated navigation, so the
        /// operator can add missing inventory without hunting for the
        /// module in the rail.
        /// </summary>
        public event EventHandler? NavigateToSheetStoreRequested;
        public ICommand OpenSheetStoreCommand { get; private set; }

        public PricingEstimatorViewModel()
         : this(
            App.ServiceProvider?.GetService<IDialogService>()?? new DialogService(),
            App.ServiceProvider?.GetService<ISpecificationsRepository>()!){ }

        // ── Shared initialization (ERR-633EEB) ─────────────────────
        // Extracted verbatim from the parameterless ctor so both it
        // and the DI-selected parameterized ctor initialize fully.
        private void InitializeCore()
        {
            // Initialize panel VMs
            SguVM = new SguParameters.SguParametersViewModel();
            DguVM = new DguParameters.DguParametersViewModel();
            LamVM = new LamParameters.LamParametersViewModel();
            DguLamVM = new DguLamParameters.DguLamParametersViewModel();

            // BUG-45: blanket recalc when the active glass type panel
            // changes ANY property — no more brittle property-name switches
            SguVM.PropertyChanged += OnAnyPanelPropertyChanged;
            DguVM.PropertyChanged += OnAnyPanelPropertyChanged;
            LamVM.PropertyChanged += OnAnyPanelPropertyChanged;
            DguLamVM.PropertyChanged += OnAnyPanelPropertyChanged;

            // Seed 4 glass type options
            GlassTypes = new ObservableCollection<GlassTypeCard>
            {
                new GlassTypeCard
                {
                    Code        = "SGU",
                    Title       = "Single Glass Unit (SGU)",
                    Description = "Monolithic pane. Direct pricing with custom color & treatments.",
                    Icon        = "\U0001F4E6",
                    AccentColor = "#6366F1"
                },
                new GlassTypeCard
                {
                    Code        = "DGU",
                    Title       = "Double Glass Unit (DGU)",
                    Description = "Insulated glass assembly. Configures spacers, gas insertion & outer/inner treatments.",
                    Icon        = "\U0001F4DA",
                    AccentColor = "#A855F7"
                },
                new GlassTypeCard
                {
                    Code        = "LAM",
                    Title       = "Laminated Assembly (LAM)",
                    Description = "Multi-layer safety sandwich. Customizes PVB thickness, outer/inner prices & margins.",
                    Icon        = "\U0001F517",
                    AccentColor = "#22C55E"
                },
                new GlassTypeCard
                {
                    Code        = "DGU+LAM",
                    Title       = "DGU + Laminated (Hybrid)",
                    Description = "Double glazed unit with one laminated layer. Combines thermal and safety performance.",
                    Icon        = "\U0001F3D7",
                    AccentColor = "#F97316"
                }
            };

            SpecGroups = new ObservableCollection<SpecTargetGroup>();

            SelectGlassTypeCommand = new RelayCommand(param =>
            {
                if (param is GlassTypeCard card)
                    SelectedGlassType = card;
            });

            ToggleSpecGroupCommand = new RelayCommand(param =>
            {
                if (param is SpecTargetGroup group)
                    SelectCalculationSection(group);
            });

            AddCalculationSectionCommand = new RelayCommand(_ => AddCalculationSection());
            RemoveCalculationSectionCommand = new RelayCommand(
                _ => RemoveCalculationSection(),
                _ => CanRemoveCalculationSection);

            // CanExecute still requires a real spec name, which is what
            // keeps an unimplemented glass type from saving garbage. All
            // four types now produce one.
            ApplyRateCommand = new RelayCommand(
                async _ => await ApplyRateAsync(),
                _ => TotalSpecsActive > 0
                     && EstimatedRatePerSqm > 0m
                     && !string.IsNullOrWhiteSpace(
                            GetCurrentSpecName(SelectedGlassType?.Code ?? "")));

            OpenSheetStoreCommand = new RelayCommand(_ =>
                NavigateToSheetStoreRequested?.Invoke(this, EventArgs.Empty));

            // Default selection = SGU
            SelectedGlassType = GlassTypes[0];

            // Default treatments
            PolishedEdges = false;
            DrillCutouts = false;
            CountersunkHoles = false;
            EdgeDeletionLayer = false;

            // Initial calc
            RecalculateEstimatedRate();
            InitializeCalculationSections();

            // PHASE A/J: populate the Sheet Store dropdowns and prices.
            // Fire-and-forget with its own guard — a constructor cannot
            // await, and an empty/locked Sheet Store must never stop the
            // pricing panels from opening.
            AsyncUtility.SafeFireAndForget(LoadSheetStoreDataAsync, "PricingEstimator");
        }

        /// <summary>
        /// PHASE A/J — pulls Sheet Store colours, sizes and prices into the
        /// SGU, DGU and LAM panels. Without this the sheet-size dropdowns
        /// stay empty and every price box reads 0.00.
        /// Never throws; each panel guards itself internally.
        /// </summary>
        private async Task LoadSheetStoreDataAsync()
        {
            try
            {
                await SguVM.LoadSheetStoreAsync().ConfigureAwait(true);
                await DguVM.LoadSheetStoreAsync().ConfigureAwait(true);
                await LamVM.LoadSheetStoreAsync().ConfigureAwait(true);
                await DguLamVM.LoadSheetStoreAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    $"[PricingEstimator] Sheet Store load failed: {ExceptionMessages.Deep(ex)}");
            }
        }

        /// <summary>
        /// Re-reads Sheet Store data. Call after the user edits stock so
        /// Step 4 reflects the new prices without reopening the wizard.
        /// </summary>
        public void RefreshSheetStoreData() => AsyncUtility.SafeFireAndForget(LoadSheetStoreDataAsync, "PricingEstimator Refresh");

        /// <summary>
        /// True once any calculation section has produced a quoted rate — i.e.
        /// Step 4 holds value the operator would lose.
        /// <para>
        /// RC-016 (2026-09-20): <see cref="RefreshSheetStoreData"/> is not a
        /// read-only refresh. It ends in <c>RefreshOuterFromStoreAsync</c> /
        /// <c>RefreshInnerFromStoreAsync</c>, which call
        /// <c>ReplaceSizes(...)</c> — and that does <c>target.Clear()</c>
        /// UNCONDITIONALLY, even when the Sheet Store returns no matching
        /// sizes — and then <c>RepriceOuterAsync</c> / <c>RepriceInnerAsync</c>,
        /// which set <c>OuterBasePrice = 0m</c> / <c>InnerBasePrice = 0m</c> on
        /// a store miss ("never keep a stale previous pane price"). A colour
        /// the store does not stock under that exact spelling therefore wipes
        /// the size dropdown and destroys the quoted selling rate.
        /// </para>
        /// <para>
        /// The wizard fired that refresh on EVERY navigation into Step 4 while
        /// <c>HasRestoredSnapshotState</c> was false — which is the normal state
        /// for any newly created invoice, since only a snapshot restore sets it.
        /// So switching stations and coming back blanked Step 4, the Step 4 →
        /// Step 5 handoff then carried the zeroed rate and blanked colours into
        /// Step 5, and the silent draft save persisted the damage without the
        /// operator pressing Save. Step 9 recomputed on a zero selling rate
        /// against a live cost side, which is how a profitable quote printed as
        /// a loss on Step 7.
        /// </para>
        /// <para>
        /// This flag lets the caller refresh a Step 4 that has nothing to lose
        /// and leave a priced one alone. Nothing is lost by gating: the store
        /// is still re-read when stock actually changes
        /// (<c>MainViewModel</c> hooks <c>SheetStoreVM.StockLedgerChanged</c> to
        /// <see cref="RefreshSheetStoreData"/>) and on snapshot restore.
        /// </para>
        /// </summary>
        public bool HasPricedStep4Data =>
            CaptureSectionSnapshots().Any(s => s != null && s.EstimatedRatePerSqm > 0m);

        public IReadOnlyList<Step4PricingSnapshot> CaptureSectionSnapshots()
        {
            UpdateCurrentCalculationSectionSnapshot();
            return SpecGroups.Select(g => ClonePricingSnapshot(g.Snapshot)).ToList();
        }

        public int SelectedCalculationSectionNumber => SelectedCalculationSection?.SectionNumber ?? 1;

        public void EnsureCalculationSectionCount(int count)
        {
            if (count < 1) count = 1;
            if (count == CalculationSectionCount) return;

            UpdateCurrentCalculationSectionSnapshot();

            while (SpecGroups.Count < count)
                AddCalculationSection();

            while (SpecGroups.Count > count && CanRemoveCalculationSection)
                RemoveLastCalculationSection();

            OnPropertyChanged(nameof(CalculationSectionCount));
            OnPropertyChanged(nameof(CanRemoveCalculationSection));
            OnPropertyChanged(nameof(TotalSpecsActive));
            ProGlassAutomation.Utilities.AsyncUtility.InvalidateCommandsThrottled();
        }

        private void InitializeCalculationSections()
        {
            SpecGroups.Clear();
            var group = new SpecTargetGroup
            {
                SectionNumber = 1,
                Name = "Spec Group 1",
                Description = "Primary calculation section",
                Snapshot = BuildCurrentPricingSnapshot()
            };
            SpecGroups.Add(group);
            SelectCalculationSection(group, captureCurrent: false);
            OnPropertyChanged(nameof(CalculationSectionCount));
            ProGlassAutomation.Utilities.AsyncUtility.InvalidateCommandsThrottled();
        }

        private void AddCalculationSection()
        {
            var snapshot = BuildCurrentPricingSnapshot();
            int next = SpecGroups.Count + 1;
            var group = new SpecTargetGroup
            {
                SectionNumber = next,
                Name = $"Spec Group {next}",
                Description = $"Calculation section {next}",
                Snapshot = ClonePricingSnapshot(snapshot)
            };

            SpecGroups.Add(group);
            SelectCalculationSection(group, captureCurrent: false);
            OnPropertyChanged(nameof(CalculationSectionCount));
            OnPropertyChanged(nameof(CanRemoveCalculationSection));
            ProGlassAutomation.Utilities.AsyncUtility.InvalidateCommandsThrottled();
        }

        private void RemoveCalculationSection()
        {
            if (SelectedCalculationSection == null || !CanRemoveCalculationSection)
                return;

            int index = SpecGroups.IndexOf(SelectedCalculationSection);
            if (index < 0) return;

            SpecGroups.RemoveAt(index);
            RenumberCalculationSections();

            int nextIndex = Math.Min(index, SpecGroups.Count - 1);
            if (nextIndex >= 0)
                SelectCalculationSection(SpecGroups[nextIndex], captureCurrent: false);

            OnPropertyChanged(nameof(CalculationSectionCount));
            OnPropertyChanged(nameof(CanRemoveCalculationSection));
            ProGlassAutomation.Utilities.AsyncUtility.InvalidateCommandsThrottled();
        }

        private void RemoveLastCalculationSection()
        {
            if (!CanRemoveCalculationSection) return;

            var current = SelectedCalculationSection;
            int removeIndex = SpecGroups.Count - 1;
            bool removingCurrent = current != null && SpecGroups.IndexOf(current) == removeIndex;

            SpecGroups.RemoveAt(removeIndex);
            RenumberCalculationSections();

            if (removingCurrent)
                SelectCalculationSection(SpecGroups[^1], captureCurrent: false);

            OnPropertyChanged(nameof(CalculationSectionCount));
            OnPropertyChanged(nameof(CanRemoveCalculationSection));
        }

        private void RenumberCalculationSections()
        {
            for (int i = 0; i < SpecGroups.Count; i++)
            {
                SpecGroups[i].SectionNumber = i + 1;
                SpecGroups[i].Name = $"Spec Group {i + 1}";
                SpecGroups[i].Description = $"Calculation section {i + 1}";
            }

            OnPropertyChanged(nameof(CurrentCalculationSectionLabel));
        }

        private void SelectCalculationSection(
            SpecTargetGroup? group,
            bool captureCurrent = true)
        {
            if (group == null) return;
            if (ReferenceEquals(SelectedCalculationSection, group) && group.IsSelected)
                return;

            if (captureCurrent)
                UpdateCurrentCalculationSectionSnapshot();

            foreach (var item in SpecGroups)
                item.IsSelected = ReferenceEquals(item, group);

            SelectedCalculationSection = group;
            ApplyPricingSnapshot(group.Snapshot);
            ResetSpecFingerprintCache();
        }

        private void UpdateCurrentCalculationSectionSnapshot()
        {
            if (_isRestoringSnapshot || SelectedCalculationSection == null)
                return;

            SelectedCalculationSection.Snapshot = BuildCurrentPricingSnapshot();
        }

        private Step4PricingSnapshot BuildCurrentPricingSnapshot()
        {
            var snapshot = new Step4PricingSnapshot
            {
                SelectedGlassTypeCode = SelectedGlassType?.Code ?? "",
                GeneratedSpecName = GetCurrentSpecName(SelectedGlassType?.Code ?? ""),
                EstimatedRatePerSqm = EstimatedRatePerSqm,
                Sgu = new SguPricingPanelSnapshot
                {
                    SelectedThickness = SguVM.SelectedThickness ?? "",
                    GlassColor = SguVM.GlassColor ?? "",
                    SelectedTreatment = SguVM.SelectedTreatment ?? "",
                    BasePricePerSqm = SguVM.BasePricePerSqm,
                    CuttingCostPerSqm = SguVM.CuttingCostPerSqm,
                    TemperingCostPerSqm = SguVM.TemperingCostPerSqm,
                    YieldFactor = SguVM.YieldFactor,
                    TargetMarginPercent = SguVM.TargetMarginPercent,
                    SelectedPolishOption = SguVM.SelectedPolishOption ?? "",
                    PanelExtra = SguVM.PanelExtra,
                    UseCustomPanelExtra = SguVM.UseCustomPanelExtra,
                    CustomPanelExtra = SguVM.CustomPanelExtra,
                    SheetWidthMm = SguVM.SheetWidthMm,
                    SheetHeightMm = SguVM.SheetHeightMm,
                    PriceResolvedFromStore = SguVM.PanePriceResolvedFromStore
                },
                Dgu = new DguPricingPanelSnapshot
                {
                    OuterThickness = DguVM.OuterThickness ?? "",
                    OuterGlassColor = DguVM.OuterGlassColor ?? "",
                    OuterTreatment = DguVM.OuterTreatment ?? "",
                    OuterBasePrice = DguVM.OuterBasePrice,
                    OuterSheetWidthMm = DguVM.OuterSheetWidthMm,
                    OuterSheetHeightMm = DguVM.OuterSheetHeightMm,
                    SpacerWidth = DguVM.SpacerWidth ?? "",
                    SpacerMaterial = DguVM.SpacerMaterial ?? "",
                    SpacerPrice = DguVM.SpacerPrice,
                    SpacerExtra = DguVM.SpacerExtra ?? "",
                    UInsertPricePerSqm = DguVM.UInsertPricePerSqm,
                    PanelExtra = DguVM.EffectivePanelExtra ?? "",
                    InnerThickness = DguVM.InnerThickness ?? "",
                    InnerGlassColor = DguVM.InnerGlassColor ?? "",
                    InnerTreatment = DguVM.InnerTreatment ?? "",
                    InnerBasePrice = DguVM.InnerBasePrice,
                    InnerSheetWidthMm = DguVM.InnerSheetWidthMm,
                    InnerSheetHeightMm = DguVM.InnerSheetHeightMm,
                    YieldFactor = DguVM.YieldFactor,
                    MarginProfitPercent = DguVM.MarginProfitPercent,
                    OuterPriceResolvedFromStore = DguVM.OuterPriceResolvedFromStore,
                    InnerPriceResolvedFromStore = DguVM.InnerPriceResolvedFromStore
                },
                Lam = new LamPricingPanelSnapshot
                {
                    OuterThickness = LamVM.OuterThickness ?? "",
                    OuterGlassColor = LamVM.OuterGlassColor ?? "",
                    OuterTreatment = LamVM.OuterTreatment ?? "",
                    OuterBasePrice = LamVM.OuterBasePrice,
                    OuterSheetWidthMm = LamVM.OuterSheetWidthMm,
                    OuterSheetHeightMm = LamVM.OuterSheetHeightMm,
                    InnerThickness = LamVM.InnerThickness ?? "",
                    InnerGlassColor = LamVM.InnerGlassColor ?? "",
                    InnerTreatment = LamVM.InnerTreatment ?? "",
                    InnerBasePrice = LamVM.InnerBasePrice,
                    InnerSheetWidthMm = LamVM.InnerSheetWidthMm,
                    InnerSheetHeightMm = LamVM.InnerSheetHeightMm,
                    PvbThickness = LamVM.PvbThickness ?? "",
                    PvbColor = LamVM.PvbColor ?? "",
                    PvbMaterialRate = LamVM.PvbMaterialRate,
                    LaminationPolish = LamVM.LaminationPolish ?? "",
                    OuterCuttingCost = LamVM.OuterCuttingCost,
                    InnerCuttingCost = LamVM.InnerCuttingCost,
                    OuterTemperingCost = LamVM.OuterTemperingCost,
                    InnerTemperingCost = LamVM.InnerTemperingCost,
                    YieldFactor = LamVM.YieldFactor,
                    MarginProfitPercent = LamVM.MarginProfitPercent,
                    OuterPriceResolvedFromStore = LamVM.OuterPriceResolvedFromStore,
                    InnerPriceResolvedFromStore = LamVM.InnerPriceResolvedFromStore
                },
                DguLam = new DguLamPricingPanelSnapshot
                {
                    OuterThickness = DguLamVM.OuterThickness ?? "",
                    OuterGlassColor = DguLamVM.OuterGlassColor ?? "",
                    OuterTreatment = DguLamVM.OuterTreatment ?? "",
                    OuterBasePrice = DguLamVM.OuterBasePrice,
                    OuterSheetWidthMm = DguLamVM.OuterSheetWidthMm,
                    OuterSheetHeightMm = DguLamVM.OuterSheetHeightMm,
                    SpacerThickness = DguLamVM.SpacerThickness ?? "",
                    SpacerPrice = DguLamVM.SpacerPrice,
                    SpacerExtra = DguLamVM.SpacerExtra ?? "",
                    LamOuterThickness = DguLamVM.LamOuterThickness ?? "",
                    LamOuterColor = DguLamVM.LamOuterColor ?? "",
                    LamOuterTreatment = DguLamVM.LamOuterTreatment ?? "",
                    LamOuterBasePrice = DguLamVM.LamOuterBasePrice,
                    LamOuterSheetWidthMm = DguLamVM.LamOuterSheetWidthMm,
                    LamOuterSheetHeightMm = DguLamVM.LamOuterSheetHeightMm,
                    InterlayerType = DguLamVM.InterlayerType ?? "",
                    InterlayerThickness = DguLamVM.InterlayerThickness ?? "",
                    InterlayerColor = DguLamVM.InterlayerColor ?? "",
                    InterlayerBuyPrice = DguLamVM.InterlayerBuyPrice,
                    InterlayerSellPrice = DguLamVM.InterlayerSellPrice,
                    LamInnerThickness = DguLamVM.LamInnerThickness ?? "",
                    LamInnerColor = DguLamVM.LamInnerColor ?? "",
                    LamInnerTreatment = DguLamVM.LamInnerTreatment ?? "",
                    LamInnerBasePrice = DguLamVM.LamInnerBasePrice,
                    LamInnerSheetWidthMm = DguLamVM.LamInnerSheetWidthMm,
                    LamInnerSheetHeightMm = DguLamVM.LamInnerSheetHeightMm,
                    PanelExtra = DguLamVM.EffectivePanelExtra ?? "",
                    EdgePolish = DguLamVM.EdgePolish ?? "",
                    OuterCuttingCost = DguLamVM.OuterCuttingCost,
                    OuterTemperingCost = DguLamVM.OuterTemperingCost,
                    LamOuterCuttingCost = DguLamVM.LamOuterCuttingCost,
                    LamOuterTemperingCost = DguLamVM.LamOuterTemperingCost,
                    LamInnerCuttingCost = DguLamVM.LamInnerCuttingCost,
                    LamInnerTemperingCost = DguLamVM.LamInnerTemperingCost,
                    YieldFactor = DguLamVM.YieldFactor,
                    MarginProfitPercent = DguLamVM.MarginProfitPercent,
                    OuterPriceResolvedFromStore = DguLamVM.OuterPriceResolvedFromStore,
                    LamOuterPriceResolvedFromStore = DguLamVM.LamOuterPriceResolvedFromStore,
                    LamInnerPriceResolvedFromStore = DguLamVM.LamInnerPriceResolvedFromStore
                }
            };

            var verdict = Services.ProformaInvoice.Step4MaterialIntegrity.Evaluate(snapshot);
            snapshot.MaterialIsValid = verdict.IsValid;
            snapshot.MaterialInvalidReason = verdict.Reason;
            return snapshot;
        }

        internal void ApplyPricingSnapshot(Step4PricingSnapshot? snapshot)
        {
            if (snapshot == null) return;

            _isRestoringSnapshot = true;
            try
            {
                SguVM.RunWithoutSheetStoreRefresh(() =>
                {
                    SguVM.SelectedThickness = snapshot.Sgu.SelectedThickness;
                    SguVM.GlassColor = snapshot.Sgu.GlassColor;
                    SguVM.SelectedTreatment = snapshot.Sgu.SelectedTreatment;
                    SguVM.BasePricePerSqm = snapshot.Sgu.BasePricePerSqm;
                    SguVM.CuttingCostPerSqm = snapshot.Sgu.CuttingCostPerSqm;
                    SguVM.TemperingCostPerSqm = snapshot.Sgu.TemperingCostPerSqm;
                    SguVM.YieldFactor = snapshot.Sgu.YieldFactor;
                    SguVM.TargetMarginPercent = snapshot.Sgu.TargetMarginPercent;
                    SguVM.SelectedPolishOption = snapshot.Sgu.SelectedPolishOption;
                    SguVM.PanelExtra = snapshot.Sgu.PanelExtra;
                    SguVM.UseCustomPanelExtra = snapshot.Sgu.UseCustomPanelExtra;
                    SguVM.CustomPanelExtra = snapshot.Sgu.CustomPanelExtra;
                    SguVM.SheetWidthMm = snapshot.Sgu.SheetWidthMm;
                    SguVM.SheetHeightMm = snapshot.Sgu.SheetHeightMm;
                });
                SguVM.SetPanePriceResolvedFromStore(
                    Services.ProformaInvoice.Step4MaterialIntegrity
                        .IsStorePriceResolved(snapshot.Sgu.PriceResolvedFromStore));

                DguVM.RunWithoutSheetStoreRefresh(() =>
                {
                    DguVM.OuterThickness = snapshot.Dgu.OuterThickness;
                    DguVM.OuterGlassColor = snapshot.Dgu.OuterGlassColor;
                    DguVM.OuterTreatment = snapshot.Dgu.OuterTreatment;
                    DguVM.OuterBasePrice = snapshot.Dgu.OuterBasePrice;
                    DguVM.OuterSheetWidthMm = snapshot.Dgu.OuterSheetWidthMm;
                    DguVM.OuterSheetHeightMm = snapshot.Dgu.OuterSheetHeightMm;
                    DguVM.SpacerWidth = snapshot.Dgu.SpacerWidth;
                    DguVM.SpacerMaterial = snapshot.Dgu.SpacerMaterial;
                    DguVM.SpacerPrice = snapshot.Dgu.SpacerPrice;
                    DguVM.SpacerExtra = snapshot.Dgu.SpacerExtra;
                    DguVM.UInsertPricePerSqm = snapshot.Dgu.UInsertPricePerSqm;
                    DguVM.PanelExtra = snapshot.Dgu.PanelExtra;
                    DguVM.InnerThickness = snapshot.Dgu.InnerThickness;
                    DguVM.InnerGlassColor = snapshot.Dgu.InnerGlassColor;
                    DguVM.InnerTreatment = snapshot.Dgu.InnerTreatment;
                    DguVM.InnerBasePrice = snapshot.Dgu.InnerBasePrice;
                    DguVM.InnerSheetWidthMm = snapshot.Dgu.InnerSheetWidthMm;
                    DguVM.InnerSheetHeightMm = snapshot.Dgu.InnerSheetHeightMm;
                    DguVM.YieldFactor = snapshot.Dgu.YieldFactor;
                    DguVM.MarginProfitPercent = snapshot.Dgu.MarginProfitPercent;
                });
                DguVM.SetPanePriceResolvedFromStore(
                    Services.ProformaInvoice.Step4MaterialIntegrity
                        .IsStorePriceResolved(snapshot.Dgu.OuterPriceResolvedFromStore),
                    Services.ProformaInvoice.Step4MaterialIntegrity
                        .IsStorePriceResolved(snapshot.Dgu.InnerPriceResolvedFromStore));

                LamVM.RunWithoutSheetStoreRefresh(() =>
                {
                    LamVM.OuterThickness = snapshot.Lam.OuterThickness;
                    LamVM.OuterGlassColor = snapshot.Lam.OuterGlassColor;
                    LamVM.OuterTreatment = snapshot.Lam.OuterTreatment;
                    LamVM.OuterBasePrice = snapshot.Lam.OuterBasePrice;
                    LamVM.OuterSheetWidthMm = snapshot.Lam.OuterSheetWidthMm;
                    LamVM.OuterSheetHeightMm = snapshot.Lam.OuterSheetHeightMm;
                    LamVM.InnerThickness = snapshot.Lam.InnerThickness;
                    LamVM.InnerGlassColor = snapshot.Lam.InnerGlassColor;
                    LamVM.InnerTreatment = snapshot.Lam.InnerTreatment;
                    LamVM.InnerBasePrice = snapshot.Lam.InnerBasePrice;
                    LamVM.InnerSheetWidthMm = snapshot.Lam.InnerSheetWidthMm;
                    LamVM.InnerSheetHeightMm = snapshot.Lam.InnerSheetHeightMm;
                    LamVM.PvbThickness = snapshot.Lam.PvbThickness;
                    LamVM.PvbColor = snapshot.Lam.PvbColor;
                    LamVM.PvbMaterialRate = snapshot.Lam.PvbMaterialRate;
                    LamVM.LaminationPolish = snapshot.Lam.LaminationPolish;
                    LamVM.OuterCuttingCost = snapshot.Lam.OuterCuttingCost;
                    LamVM.InnerCuttingCost = snapshot.Lam.InnerCuttingCost;
                    LamVM.OuterTemperingCost = snapshot.Lam.OuterTemperingCost;
                    LamVM.InnerTemperingCost = snapshot.Lam.InnerTemperingCost;
                    LamVM.YieldFactor = snapshot.Lam.YieldFactor;
                    LamVM.MarginProfitPercent = snapshot.Lam.MarginProfitPercent;
                });
                LamVM.SetPanePriceResolvedFromStore(
                    Services.ProformaInvoice.Step4MaterialIntegrity
                        .IsStorePriceResolved(snapshot.Lam.OuterPriceResolvedFromStore),
                    Services.ProformaInvoice.Step4MaterialIntegrity
                        .IsStorePriceResolved(snapshot.Lam.InnerPriceResolvedFromStore));

                DguLamVM.RunWithoutSheetStoreRefresh(() =>
                {
                    DguLamVM.OuterThickness = snapshot.DguLam.OuterThickness;
                    DguLamVM.OuterGlassColor = snapshot.DguLam.OuterGlassColor;
                    DguLamVM.OuterTreatment = snapshot.DguLam.OuterTreatment;
                    DguLamVM.OuterBasePrice = snapshot.DguLam.OuterBasePrice;
                    DguLamVM.OuterSheetWidthMm = snapshot.DguLam.OuterSheetWidthMm;
                    DguLamVM.OuterSheetHeightMm = snapshot.DguLam.OuterSheetHeightMm;
                    DguLamVM.SpacerThickness = snapshot.DguLam.SpacerThickness;
                    DguLamVM.SpacerPrice = snapshot.DguLam.SpacerPrice;
                    DguLamVM.SpacerExtra = snapshot.DguLam.SpacerExtra;
                    DguLamVM.LamOuterThickness = snapshot.DguLam.LamOuterThickness;
                    DguLamVM.LamOuterColor = snapshot.DguLam.LamOuterColor;
                    DguLamVM.LamOuterTreatment = snapshot.DguLam.LamOuterTreatment;
                    DguLamVM.LamOuterBasePrice = snapshot.DguLam.LamOuterBasePrice;
                    DguLamVM.LamOuterSheetWidthMm = snapshot.DguLam.LamOuterSheetWidthMm;
                    DguLamVM.LamOuterSheetHeightMm = snapshot.DguLam.LamOuterSheetHeightMm;
                    DguLamVM.InterlayerType = snapshot.DguLam.InterlayerType;
                    DguLamVM.InterlayerThickness = snapshot.DguLam.InterlayerThickness;
                    DguLamVM.InterlayerColor = snapshot.DguLam.InterlayerColor;
                    DguLamVM.InterlayerBuyPrice = snapshot.DguLam.InterlayerBuyPrice;
                    DguLamVM.InterlayerSellPrice = snapshot.DguLam.InterlayerSellPrice;
                    DguLamVM.LamInnerThickness = snapshot.DguLam.LamInnerThickness;
                    DguLamVM.LamInnerColor = snapshot.DguLam.LamInnerColor;
                    DguLamVM.LamInnerTreatment = snapshot.DguLam.LamInnerTreatment;
                    DguLamVM.LamInnerBasePrice = snapshot.DguLam.LamInnerBasePrice;
                    DguLamVM.LamInnerSheetWidthMm = snapshot.DguLam.LamInnerSheetWidthMm;
                    DguLamVM.LamInnerSheetHeightMm = snapshot.DguLam.LamInnerSheetHeightMm;
                    DguLamVM.PanelExtra = snapshot.DguLam.PanelExtra;
                    DguLamVM.EdgePolish = snapshot.DguLam.EdgePolish;
                    DguLamVM.OuterCuttingCost = snapshot.DguLam.OuterCuttingCost;
                    DguLamVM.OuterTemperingCost = snapshot.DguLam.OuterTemperingCost;
                    DguLamVM.LamOuterCuttingCost = snapshot.DguLam.LamOuterCuttingCost;
                    DguLamVM.LamOuterTemperingCost = snapshot.DguLam.LamOuterTemperingCost;
                    DguLamVM.LamInnerCuttingCost = snapshot.DguLam.LamInnerCuttingCost;
                    DguLamVM.LamInnerTemperingCost = snapshot.DguLam.LamInnerTemperingCost;
                    DguLamVM.YieldFactor = snapshot.DguLam.YieldFactor;
                    DguLamVM.MarginProfitPercent = snapshot.DguLam.MarginProfitPercent;
                });
                DguLamVM.SetPanePriceResolvedFromStore(
                    Services.ProformaInvoice.Step4MaterialIntegrity
                        .IsStorePriceResolved(snapshot.DguLam.OuterPriceResolvedFromStore),
                    Services.ProformaInvoice.Step4MaterialIntegrity
                        .IsStorePriceResolved(snapshot.DguLam.LamOuterPriceResolvedFromStore),
                    Services.ProformaInvoice.Step4MaterialIntegrity
                        .IsStorePriceResolved(snapshot.DguLam.LamInnerPriceResolvedFromStore));

                SelectGlassTypeCard(snapshot.SelectedGlassTypeCode);
                YieldFactorWarning = "";
                ApplyStatusMessage = "";
                RecalculateEstimatedRate();
            }
            finally
            {
                _isRestoringSnapshot = false;
            }
        }

        private static Step4PricingSnapshot ClonePricingSnapshot(Step4PricingSnapshot snapshot)
        {
            return JsonSerializer.Deserialize<Step4PricingSnapshot>(
                JsonSerializer.Serialize(snapshot)) ?? new Step4PricingSnapshot();
        }

        /// <summary>
        /// Margin % that Step 4 panels seed when a NEW invoice is started.
        /// Reads the company default from Settings → Features → Proforma
        /// Invoice → Step 9 → Profit margin % (15% default), so a settings
        /// change flows into every new quote's Step 4 margin boxes. Yield
        /// stays at the per-panel 0.85 — the Step 9 WASTAGE % box drives the
        /// yield used by the Step 9 target formula, not these rows.
        /// </summary>
        private static decimal ExpectedMarginSeedForNewInvoice =>
            ProGlassAutomation.Services.Features.Step9PricingDefaults.ExpectedProfitMarginPercent;

        public void ResetForNewInvoice()
        {
            _isRestoringSnapshot = true;
            try
            {
                HasRestoredSnapshotState = false;

                SguVM.RunWithoutSheetStoreRefresh(() =>
                {
                    SguVM.SelectedThickness = "6mm";
                    SguVM.GlassColor = "Clear";
                    SguVM.SelectedTreatment = "FT Glass";
                    SguVM.BasePricePerSqm = 46m;
                    SguVM.CuttingCostPerSqm = 15m;
                    SguVM.TemperingCostPerSqm = 25m;
                    SguVM.YieldFactor = 0.85m;
                    SguVM.TargetMarginPercent = ExpectedMarginSeedForNewInvoice;
                    SguVM.SelectedPolishOption = "None (Standard)";
                    SguVM.PanelExtra = "None";
                    SguVM.UseCustomPanelExtra = false;
                    SguVM.CustomPanelExtra = "";
                    SguVM.SheetWidthMm = 0;
                    SguVM.SheetHeightMm = 0;
                });
                SguVM.SetPanePriceResolvedFromStore(false);

                DguVM.RunWithoutSheetStoreRefresh(() =>
                {
                    DguVM.OuterThickness = "6mm";
                    DguVM.OuterGlassColor = "Clear";
                    DguVM.OuterTreatment = "FT";
                    DguVM.OuterBasePrice = 46m;
                    DguVM.OuterSheetWidthMm = 0;
                    DguVM.OuterSheetHeightMm = 0;
                    DguVM.SpacerWidth = "12mm ASP";
                    DguVM.SpacerMaterial = "Normal Mill Aluminum";
                    DguVM.SpacerPrice = 45m;
                    DguVM.SpacerExtra = "None";
                    DguVM.UInsertPricePerSqm = 10m;
                    DguVM.PanelExtra = "None";
                    DguVM.InnerThickness = "6mm";
                    DguVM.InnerGlassColor = "Clear";
                    DguVM.InnerTreatment = "FT";
                    DguVM.InnerBasePrice = 46m;
                    DguVM.InnerSheetWidthMm = 0;
                    DguVM.InnerSheetHeightMm = 0;
                    DguVM.YieldFactor = 0.85m;
                    DguVM.MarginProfitPercent = ExpectedMarginSeedForNewInvoice;
                });
                DguVM.SetPanePriceResolvedFromStore(false, false);

                LamVM.RunWithoutSheetStoreRefresh(() =>
                {
                    LamVM.OuterThickness = "6mm";
                    LamVM.OuterGlassColor = "Clear";
                    LamVM.OuterTreatment = "FT";
                    LamVM.OuterBasePrice = 46m;
                    LamVM.OuterSheetWidthMm = 0;
                    LamVM.OuterSheetHeightMm = 0;
                    LamVM.InnerThickness = "6mm";
                    LamVM.InnerGlassColor = "Clear";
                    LamVM.InnerTreatment = "FT";
                    LamVM.InnerBasePrice = 46m;
                    LamVM.InnerSheetWidthMm = 0;
                    LamVM.InnerSheetHeightMm = 0;
                    LamVM.PvbThickness = "1.52mm";
                    LamVM.PvbColor = "Clear";
                    LamVM.PvbMaterialRate = 80m;
                    LamVM.LaminationPolish = "None";
                    LamVM.OuterCuttingCost = 15m;
                    LamVM.InnerCuttingCost = 15m;
                    LamVM.OuterTemperingCost = 25m;
                    LamVM.InnerTemperingCost = 25m;
                    LamVM.YieldFactor = 0.85m;
                    LamVM.MarginProfitPercent = ExpectedMarginSeedForNewInvoice;
                });
                LamVM.SetPanePriceResolvedFromStore(false, false);

                DguLamVM.RunWithoutSheetStoreRefresh(() =>
                {
                DguLamVM.OuterThickness = "6mm";
                DguLamVM.OuterGlassColor = "HD Grey";
                DguLamVM.OuterTreatment = "FT";
                DguLamVM.OuterBasePrice = 48m;
                DguLamVM.OuterSheetWidthMm = 0;
                DguLamVM.OuterSheetHeightMm = 0;
                DguLamVM.SpacerThickness = "12mm";
                DguLamVM.SpacerPrice = 0m;
                DguLamVM.SpacerExtra = "None";
                DguLamVM.LamOuterThickness = "6mm";
                DguLamVM.LamOuterColor = "Clear";
                DguLamVM.LamOuterTreatment = "FT";
                DguLamVM.LamOuterBasePrice = 25m;
                DguLamVM.LamOuterSheetWidthMm = 0;
                DguLamVM.LamOuterSheetHeightMm = 0;
                DguLamVM.InterlayerType = "PVB";
                DguLamVM.InterlayerThickness = "1.52mm";
                DguLamVM.InterlayerColor = "Clear";
                DguLamVM.InterlayerBuyPrice = 100m;
                DguLamVM.InterlayerSellPrice = 110m;
                DguLamVM.LamInnerThickness = "6mm";
                DguLamVM.LamInnerColor = "Clear";
                DguLamVM.LamInnerTreatment = "FT";
                DguLamVM.LamInnerBasePrice = 25m;
                DguLamVM.LamInnerSheetWidthMm = 0;
                DguLamVM.LamInnerSheetHeightMm = 0;
                DguLamVM.PanelExtra = "None";
                DguLamVM.EdgePolish = "None";
                DguLamVM.OuterCuttingCost = 10m;
                DguLamVM.OuterTemperingCost = 12m;
                DguLamVM.LamOuterCuttingCost = 10m;
                DguLamVM.LamOuterTemperingCost = 10m;
                DguLamVM.LamInnerCuttingCost = 10m;
                DguLamVM.LamInnerTemperingCost = 10m;
                DguLamVM.YieldFactor = 0.85m;
                DguLamVM.MarginProfitPercent = ExpectedMarginSeedForNewInvoice;
                });
                DguLamVM.SetPanePriceResolvedFromStore(false, false, false);

                SelectGlassTypeCard("SGU");
                YieldFactorWarning = "";
                ApplyStatusMessage = "";
                RecalculateEstimatedRate();
                InitializeCalculationSections();
            }
            finally
            {
                _isRestoringSnapshot = false;
            }

            RefreshSheetStoreData();
        }

        public void RestoreFromSnapshot(
            Step4PricingSnapshot? snapshot,
            IEnumerable<ProGlassAutomation.Models.ProformaInvoice.SavedInvoice.SpecGroupSnapshot>? specGroups = null,
            IEnumerable<Step4PricingSnapshot>? pricingSections = null,
            int selectedSectionNumber = 1)
        {
            bool hasStep4Snapshot = !string.IsNullOrWhiteSpace(snapshot?.SelectedGlassTypeCode);
            var sectionList = pricingSections?.ToList() ?? new List<Step4PricingSnapshot>();

            _isRestoringSnapshot = true;
            try
            {
                if (sectionList.Count > 0)
                {
                    LoadCalculationSections(sectionList, selectedSectionNumber);
                    HasRestoredSnapshotState = true;
                }
                else if (hasStep4Snapshot && snapshot != null)
                {
                    int desiredCount = Math.Max(1, specGroups?.Count() ?? 1);
                    var clonedSections = Enumerable.Range(0, desiredCount)
                        .Select(_ => ClonePricingSnapshot(snapshot))
                        .ToList();
                    LoadCalculationSections(clonedSections, Math.Max(1, selectedSectionNumber));
                    HasRestoredSnapshotState = true;
                }
                else
                {
                    int desiredCount = Math.Max(1, specGroups?.Count() ?? 1);
                    InitializeCalculationSections();
                    EnsureCalculationSectionCount(desiredCount);

                    if (SpecGroups.Count > 0)
                        SelectCalculationSection(SpecGroups[0], captureCurrent: false);

                    string selectedCode = specGroups?.Select(g => g.SelectedGlassType)
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
                        ?? snapshot?.SelectedGlassTypeCode;
                    if (!string.IsNullOrWhiteSpace(selectedCode))
                        SelectGlassTypeCard(selectedCode);
                    UpdateCurrentCalculationSectionSnapshot();
                    HasRestoredSnapshotState = false;
                }

                YieldFactorWarning = "";
                ApplyStatusMessage = "";
                RecalculateEstimatedRate();
            }
            finally
            {
                _isRestoringSnapshot = false;
            }
        }
    }
}
