using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.ProformaInvoice;
using ProGlassAutomation.Services.ProformaInvoice.Export;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguLamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.LamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges.Wastages;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges.CustomCharges;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges
{
    /// <summary>
    /// Main ViewModel for Step 6 (Other Charges).
    ///
    /// CHAT 17 — Per-Spec Targeting:
    ///   Each OtherChargeRow now owns a SpecTargetSelection (multi-select
    ///   specs + metric). BuildSourcePool() has been replaced with
    ///   BuildTargetSelectionForRow() which creates one selector per row.
    ///
    ///   When PerSpecMetrics changes (spec added/removed/renamed) each row's
    ///   SpecTargetSelection is refreshed by SyncTargetSelectionsFromHub().
    ///
    /// BUG-53, BUG-54, BUG-55 all preserved.
    /// </summary>
    public class OtherChargesViewModel : ViewModelBase, IDisposable
    {
        private readonly DataGridViewModel _dataGridVM;
        private LamParametersViewModel? _lamVm;
        private DguLamParametersViewModel? _dguLamVm;
        private readonly DispatcherTimer _polishDebouncer;
        private const int POLISH_DEBOUNCE_MS = 150;

        public string Title { get; } = "OTHER CHARGES";
        public string Subtitle { get; } =
            "Supplementary, fabrication and delivery charges — " +
            "select which specs to charge and which metric to use.";

        public MetricDiscoveryHubViewModel MetricHub { get; }

        public ObservableCollection<OtherChargeRow> ChargeRows { get; } = new();

        public WastagesSectionViewModel WastagesSection { get; }
        public CustomChargesSectionViewModel CustomChargesSection { get; }

        private bool _useAdvancedAutomation = true;
        private bool _isApplyingAdvancedAutomation;
        private bool _isRestoringSavedState;

        /// <summary>
        /// Advanced = Step 4 / Step 5 auto-detect and preselect matching
        /// Step 6 rows. Manual = leave the rows entirely under the user's
        /// control.
        /// </summary>
        public bool UseAdvancedAutomation
        {
            get => _useAdvancedAutomation;
            set
            {
                if (_useAdvancedAutomation == value) return;
                _useAdvancedAutomation = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsManualMode));
                OnPropertyChanged(nameof(AutomationModeLabel));
                // Only auto-trigger if NOT restoring saved state.
                // When reopening a saved PI, we want to preserve the saved
                // charge selections, not re-run automation.
                if (_useAdvancedAutomation && !_isRestoringSavedState)
                    ApplyAdvancedSuggestions();
            }
        }

        public bool IsManualMode => !UseAdvancedAutomation;

        public string AutomationModeLabel => UseAdvancedAutomation
            ? "Advanced mode — Step 4 / Step 5 drive the charge selection automatically."
            : "Manual mode — no auto-triggering; all Step 6 rows stay user-controlled.";

        public ICommand SelectAdvancedModeCommand { get; }
        public ICommand SelectManualModeCommand { get; }

        // ── Total ──────────────────────────────────────────────────────────

        private decimal _totalOtherCharges;
        public decimal TotalOtherCharges
        {
            get => _totalOtherCharges;
            private set
            {
                if (_totalOtherCharges == value) return;
                _totalOtherCharges = value;
                OnPropertyChanged();
            }
        }

        // ── Constructor ────────────────────────────────────────────────────

        public OtherChargesViewModel(DataGridViewModel dataGridVM)
        {
            _dataGridVM = dataGridVM
                ?? throw new ArgumentNullException(nameof(dataGridVM));

            _polishDebouncer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(POLISH_DEBOUNCE_MS)
            };
            _polishDebouncer.Tick += OnPolishDebounceElapsed;

            MetricHub = new MetricDiscoveryHubViewModel(_dataGridVM.SpecGroups);
            MetricHub.PropertyChanged += OnHubPropertyChanged;
            MetricHub.PerSpecMetrics.CollectionChanged += OnPerSpecMetricsChanged;

            BuildChargeRows();

            WastagesSection = new WastagesSectionViewModel(_dataGridVM);
            WastagesSection.PropertyChanged += OnSubSectionChanged;

            CustomChargesSection = new CustomChargesSectionViewModel();
            CustomChargesSection.PropertyChanged += OnSubSectionChanged;

            SelectAdvancedModeCommand = new RelayCommand(_ => UseAdvancedAutomation = true);
            SelectManualModeCommand = new RelayCommand(_ => UseAdvancedAutomation = false);

            _dataGridVM.SpecGroups.CollectionChanged += OnSpecGroupsCollectionChanged;
            foreach (var g in _dataGridVM.SpecGroups)
                HookSpecGroup(g);

            RecalculatePolishRowsImmediate();
            ApplyAdvancedSuggestions();
        }

        public void BindStep4Pricing(Func<IReadOnlyList<ProGlassAutomation.Models.ProformaInvoice.SavedInvoice.Step4PricingSnapshot>> sectionsProvider)
        {
            if (sectionsProvider == null) return;
            WastagesSection.BindStep4SectionsProvider(sectionsProvider);
            RefreshWastageSelectionSurface();
        }

        /// <summary>
        /// Offers a finished optimization run to the wastage section, so SHEET QTY
        /// and the optimization % can be read from the real cut instead of typed.
        /// Mirrors BindStep4Pricing: a late-bound provider, optional, and Step 6
        /// stays purely manual when nothing is bound.
        /// </summary>
        public void BindOptimizationFeed(
            Func<IReadOnlyList<ProGlassAutomation.Services.Optimization.OptimizationPaneResult>> provider)
        {
            if (provider == null) return;
            WastagesSection.BindOptimizationFeedProvider(provider);
        }

        /// <summary>Re-resolves every wastage row against the current run.</summary>
        public void RefreshOptimizationFeed() => WastagesSection.RefreshOptimizationFeed();

        /// <summary>
        /// 2026-09-10: job context (customer / project / salesman / ref)
        /// forwarded to the wastage section so inventory consumption
        /// history entries carry the same identification the manual
        /// Sheet Store Use dialog writes.
        /// </summary>
        public void BindJobContext(
            Func<(string Reference, string Customer, string Project, string Salesman)>? provider)
        {
            WastagesSection.BindJobContext(provider);
        }

        public void BindLaminationEditors(
            LamParametersViewModel lamVm,
            DguLamParametersViewModel dguLamVm)
        {
            if (_lamVm != null)
                _lamVm.PropertyChanged -= OnLaminationEditorChanged;
            if (_dguLamVm != null)
                _dguLamVm.PropertyChanged -= OnLaminationEditorChanged;

            _lamVm = lamVm;
            _dguLamVm = dguLamVm;

            if (_lamVm != null)
                _lamVm.PropertyChanged += OnLaminationEditorChanged;
            if (_dguLamVm != null)
                _dguLamVm.PropertyChanged += OnLaminationEditorChanged;

            SyncLaminationColorsFromStep4();
        }

        private void OnLaminationEditorChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is
                nameof(LamParametersViewModel.OuterGlassColor)
                or nameof(LamParametersViewModel.InnerGlassColor)
                or nameof(LamParametersViewModel.GeneratedSpecName)
                or nameof(DguLamParametersViewModel.LamOuterColor)
                or nameof(DguLamParametersViewModel.LamInnerColor)
                or nameof(DguLamParametersViewModel.GeneratedSpecName)
                or nameof(DguLamParametersViewModel.OuterGlassColor)
                or null
                or "")
            {
                SyncLaminationColorsFromStep4();
            }
        }

        private void SyncLaminationColorsFromStep4()
        {
            foreach (var group in _dataGridVM.SpecGroups)
            {
                string effectiveType = OtherChargeAutomationRules.ResolveEffectiveGlassType(
                    group.SelectedGlassType, group.SelectedSpecName);

                if (effectiveType == "LAM" && _lamVm != null)
                {
                    // The live Step 4 editor is authoritative only for the
                    // section it currently represents. A manually selected
                    // Step 5 LAM spec with parsed colours must not be replaced
                    // by whatever happens to be open in the Step 4 panel.
                    bool hasNoKnownColors = string.IsNullOrWhiteSpace(group.OuterGlassColor)
                        && string.IsNullOrWhiteSpace(group.InnerGlassColor);
                    bool takeLive = hasNoKnownColors
                        || LaminateGlassMatcher.SpecNameMatchesGenerated(
                            group.SelectedSpecName, _lamVm.GeneratedSpecName);
                    if (takeLive)
                        group.ApplyLaminateColors(
                            _lamVm.OuterGlassColor, _lamVm.InnerGlassColor);
                }
                else if (effectiveType == "DGU+LAM" && _dguLamVm != null)
                {
                    bool hasNoKnownColors = string.IsNullOrWhiteSpace(group.OuterGlassColor)
                        && string.IsNullOrWhiteSpace(group.InnerGlassColor);
                    bool takeLive = hasNoKnownColors
                        || LaminateGlassMatcher.SpecNameMatchesGenerated(
                            group.SelectedSpecName, _dguLamVm.GeneratedSpecName);
                    if (takeLive)
                        group.ApplyLaminateColors(
                            _dguLamVm.LamOuterColor, _dguLamVm.LamInnerColor);
                }
            }

            MetricHub.Refresh();
            RequestPolishRecalculation();
            if (UseAdvancedAutomation)
                ApplyAdvancedSuggestions();
        }

        // ══════════════════════════════════════════════════════════════════
        //  SPEC TARGET SELECTION BUILDING
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a new SpecTargetSelection for the given row.
        /// All specs default to checked; metric defaults from template.
        /// </summary>
        private SpecTargetSelection BuildTargetSelectionForRow(OtherChargeRow row)
        {
            var selection = new SpecTargetSelection(row.Unit);

            // Populate specs (all checked by default — Decision 3)
            foreach (var specMetric in MetricHub.PerSpecMetrics)
            {
                selection.AvailableSpecs.Add(new SpecCheckOption
                {
                    SpecGroupNumber = specMetric.SpecGroupNumber,
                    DisplayName = specMetric.SpecDisplayName,
                    IsDoubleGlass = specMetric.IsDoubleGlass,
                    PaneFactor = specMetric.PaneFactor,
                    Lm1 = specMetric.Lm1,
                    Lm2 = specMetric.Lm2,
                    Sqm1 = specMetric.Sqm1,
                    Sqm2 = specMetric.Sqm2,
                    Qty = specMetric.Qty,
                    IsChecked = true
                });
            }

            // Default metric from template DefaultSourceKey (Decision 3).
            // TOTAL_PIECES is a legacy alias for QTY — NormalizeKey maps it.
            var template = PredefinedChargeTemplates.GetAll()
                .FirstOrDefault(t => t.Key == row.TemplateKey);

            string defaultKey = template?.DefaultSourceKey ?? "";
            string normalized = MetricOption.NormalizeKey(defaultKey);
            selection.SelectedMetric = selection.AvailableMetrics
                .FirstOrDefault(m => m.Key == normalized)
                ?? selection.AvailableMetrics.FirstOrDefault();

            return selection;
        }

        /// <summary>
        /// Refreshes all rows' TargetSelection with latest PerSpecMetrics.
        /// Preserves existing check states and metric selection when possible.
        /// Adds new specs (checked) or removes deleted ones.
        /// </summary>
        private void SyncTargetSelectionsFromHub()
        {
            foreach (var row in ChargeRows)
            {
                if (row.IsDeliveryRow) continue;

                var selection = row.TargetSelection;
                if (selection == null)
                {
                    row.TargetSelection = BuildTargetSelectionForRow(row);
                    continue;
                }

                // Sync spec list — add new specs, remove missing ones
                var currentKeys = MetricHub.PerSpecMetrics
                    .Select(s => s.SpecGroupNumber).ToHashSet();

                // Remove specs no longer present
                for (int i = selection.AvailableSpecs.Count - 1; i >= 0; i--)
                {
                    if (!currentKeys.Contains(selection.AvailableSpecs[i].SpecGroupNumber))
                        selection.AvailableSpecs.RemoveAt(i);
                }

                // Update existing + add new
                foreach (var specMetric in MetricHub.PerSpecMetrics)
                {
                    var existing = selection.AvailableSpecs
                        .FirstOrDefault(s => s.SpecGroupNumber == specMetric.SpecGroupNumber);

                    if (existing != null)
                    {
                        // Update metric values (preserves IsChecked)
                        existing.DisplayName = specMetric.SpecDisplayName;
                        existing.IsDoubleGlass = specMetric.IsDoubleGlass;
                        existing.PaneFactor = specMetric.PaneFactor;
                        existing.Lm1 = specMetric.Lm1;
                        existing.Lm2 = specMetric.Lm2;
                        existing.Sqm1 = specMetric.Sqm1;
                        existing.Sqm2 = specMetric.Sqm2;
                        existing.Qty = specMetric.Qty;
                    }
                    else
                    {
                        // New spec — default to checked
                        selection.AvailableSpecs.Add(new SpecCheckOption
                        {
                            SpecGroupNumber = specMetric.SpecGroupNumber,
                            DisplayName = specMetric.SpecDisplayName,
                            IsDoubleGlass = specMetric.IsDoubleGlass,
                            PaneFactor = specMetric.PaneFactor,
                            Lm1 = specMetric.Lm1,
                            Lm2 = specMetric.Lm2,
                            Sqm1 = specMetric.Sqm1,
                            Sqm2 = specMetric.Sqm2,
                            Qty = specMetric.Qty,
                            IsChecked = true
                        });
                    }
                }

                // Notify computed value / summary changed
                selection.RaiseComputed();
                row.ForceRefresh();
            }

            RecalculateTotal();
        }

        private void RefreshWastageSelectionSurface()
        {
            if (UseAdvancedAutomation)
                ApplyAdvancedSuggestions();
            else
                WastagesSection?.SyncAllWastageSources();
        }

        private void ApplyAdvancedSuggestions()
        {
            if (!UseAdvancedAutomation || _isApplyingAdvancedAutomation)
                return;

            _isApplyingAdvancedAutomation = true;
            try
            {
                SyncTargetSelectionsFromHub();

                var metricsByGroup = MetricHub.PerSpecMetrics
                    .ToDictionary(x => x.SpecGroupNumber);

                var desiredRows = new Dictionary<string, (HashSet<int> Specs, string MetricKey, PolishSubType? Polish)>(StringComparer.OrdinalIgnoreCase);
                var wastage = new List<AutoWastageSuggestion>();

                foreach (var group in _dataGridVM.SpecGroups)
                {
                    metricsByGroup.TryGetValue(group.GroupNumber, out var metric);

                    foreach (var suggestion in OtherChargeAutomationRules.GetChargeSuggestions(
                        group.SelectedGlassType,
                        group.SelectedSpecName,
                        metric?.IsMatchedSameGlass ?? false))
                    {
                        if (!desiredRows.TryGetValue(suggestion.TemplateKey, out var plan))
                            plan = (new HashSet<int>(), suggestion.MetricKey, suggestion.PolishSubType);

                        plan.Specs.Add(group.GroupNumber);
                        if (!string.IsNullOrWhiteSpace(suggestion.MetricKey))
                            plan.MetricKey = suggestion.MetricKey;
                        if (suggestion.PolishSubType.HasValue)
                            plan.Polish = suggestion.PolishSubType;

                        desiredRows[suggestion.TemplateKey] = plan;
                    }

                    wastage.AddRange(OtherChargeAutomationRules.GetWastageSuggestions(
                        group.GroupNumber,
                        group.SelectedGlassType,
                        group.SelectedSpecName,
                        group.OuterGlassColor,
                        group.InnerGlassColor));
                }

                foreach (var row in ChargeRows)
                {
                    if (row.IsDeliveryRow)
                        continue;

                    if (!desiredRows.TryGetValue(row.TemplateKey, out var desired))
                    {
                        // No current match. Auto-driven rows should switch
                        // back off, but any operator-restored/manual toggle
                        // remains authoritative.
                        row.ApplySuggestedIsActive(false);

                        // If the row is OFF, clear stale auto-targeting so the
                        // Step 6 surface reflects the current detection set.
                        // Never fight a restored/manual row — that is how
                        // Other Charges values were getting reset on Step 5
                        // snapshot refreshes.
                        if (!row.HasManualActiveOverride
                            && !row.IsActive
                            && row.TargetSelection != null)
                        {
                            foreach (var spec in row.TargetSelection.AvailableSpecs)
                                spec.IsChecked = false;
                            row.TargetSelection.RaiseComputed();
                        }
                        continue;
                    }

                    if (!row.HasManualActiveOverride && row.TargetSelection != null)
                    {
                        var selectedMetric = MetricOption.FindForUnit(row.Unit, desired.MetricKey);
                        if (selectedMetric != null)
                            row.TargetSelection.SelectedMetric = selectedMetric;

                        foreach (var spec in row.TargetSelection.AvailableSpecs)
                            spec.IsChecked = desired.Specs.Contains(spec.SpecGroupNumber);

                        row.TargetSelection.RaiseComputed();
                    }

                    if (!row.HasManualActiveOverride
                        && row.IsPolishRow
                        && desired.Polish.HasValue)
                    {
                        var subType = row.AvailablePolishSubTypes
                            .FirstOrDefault(x => x.Type == desired.Polish.Value);
                        if (subType != null)
                            row.SelectedPolishSubType = subType;
                    }

                    if (TryResolveAutoRate(row.TemplateKey, desired.Specs, out decimal autoRate))
                        row.ApplySuggestedRate(autoRate);

                    row.ApplySuggestedIsActive(desired.Specs.Count > 0);
                }

                WastagesSection?.ApplyAdvancedSuggestions(wastage);
                RecalculatePolishRowsImmediate();
                RecalculateTotal();
            }
            finally
            {
                _isApplyingAdvancedAutomation = false;
            }
        }

        private bool TryResolveAutoRate(
            string templateKey,
            HashSet<int> specNumbers,
            out decimal rate)
        {
            rate = 0m;
            if (specNumbers == null || specNumbers.Count == 0)
                return false;

            // Right now only U-Insert has an explicit Step 4 rate field.
            // Other auto-mapped charges continue to use their Step 6 row rate
            // (template default or user-edited override).
            if (!string.Equals(templateKey, "U_INSERT", StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (var group in _dataGridVM.SpecGroups)
            {
                if (!specNumbers.Contains(group.GroupNumber)) continue;
                if (group.UInsertPricePerSqm <= 0m) continue;
                rate = group.UInsertPricePerSqm;
                return true;
            }

            return false;
        }

        // ── Hub property changed → sync ─────────────────────────────────

        private void OnHubPropertyChanged(
            object? sender, PropertyChangedEventArgs e)
        {
            SyncTargetSelectionsFromHub();
            RefreshWastageSelectionSurface();
        }

        private void OnPerSpecMetricsChanged(
            object? sender, NotifyCollectionChangedEventArgs e)
        {
            SyncTargetSelectionsFromHub();
            RefreshWastageSelectionSurface();
        }

        // ── Charge rows ────────────────────────────────────────────────────

        private void BuildChargeRows()
        {
            ChargeRows.Clear();

            foreach (var template in PredefinedChargeTemplates.GetAll())
            {
                var row = new OtherChargeRow
                {
                    TemplateKey = template.Key,
                    ChargeName = template.ChargeName,
                    Description = template.Description,
                    Unit = template.Unit,
                    Rate = template.DefaultRate
                };

                // Default OFF must not count as a manual operator override,
                // otherwise advanced mode can never auto-activate the row.
                row.ClearManualActiveOverride();

                if (row.IsPolishRow)
                {
                    foreach (var info in PolishSubTypeInfo.All)
                        row.AvailablePolishSubTypes.Add(info);

                    row.SelectedPolishSubType =
                        row.AvailablePolishSubTypes[0];
                }

                // Chat 17: Every non-delivery row gets a TargetSelection
                if (!row.IsDeliveryRow)
                {
                    row.TargetSelection = BuildTargetSelectionForRow(row);
                }

                row.PropertyChanged += OnChargeRowChanged;
                ChargeRows.Add(row);
            }
        }

        private void OnChargeRowChanged(
            object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(OtherChargeRow.ResultAmount)
                or nameof(OtherChargeRow.IsActive))
            {
                RecalculateTotal();
            }

            if (sender is OtherChargeRow row && row.IsPolishRow
                && e.PropertyName is
                    nameof(OtherChargeRow.SelectedPolishSubType)
                    or nameof(OtherChargeRow.IsActive)
                    or nameof(OtherChargeRow.TargetSelection))
            {
                RequestPolishRecalculation();
            }
        }

        // ── Sub-section change tracking ────────────────────────────────────

        private void OnSubSectionChanged(
            object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName ==
                    nameof(WastagesSectionViewModel.TotalWastageValue)
                || e.PropertyName ==
                    nameof(CustomChargesSectionViewModel.TotalCustomChargesValue))
            {
                RecalculateTotal();
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  POLISH LM CALCULATION (BUG-53 + BUG-54)
        // ══════════════════════════════════════════════════════════════════

        private void RequestPolishRecalculation()
        {
            _polishDebouncer.Stop();
            _polishDebouncer.Start();
        }

        private void OnPolishDebounceElapsed(object? sender, EventArgs e)
        {
            _polishDebouncer.Stop();
            RecalculatePolishRowsImmediate();
        }

        private void RecalculatePolishRowsImmediate()
        {
            var specSnapshots = BuildSpecSnapshotsForCalculator();

            foreach (var row in ChargeRows.Where(r => r.IsPolishRow))
            {
                if (row.SelectedPolishSubType == null
                    || row.SelectedPolishSubType.Type == PolishSubType.Custom)
                {
                    continue;
                }

                var scoped = FilterSnapshotsForRow(specSnapshots, row);
                decimal totalLm =
                    ProformaChargeMetricsCalculator.ComputePolishLm(
                        scoped,
                        row.SelectedPolishSubType.Type.ToString(),
                        matchedSameGlassOnly: row.IsMatchedLamPolishRow);

                row.SetCalculatedPolishQty(totalLm);
            }
        }

        private static List<SpecGroupSnapshot> FilterSnapshotsForRow(
            List<SpecGroupSnapshot> all, OtherChargeRow row)
        {
            if (row.TargetSelection == null)
                return all;

            var checkedNumbers = row.TargetSelection.AvailableSpecs
                .Where(s => s.IsChecked)
                .Select(s => s.SpecGroupNumber)
                .ToHashSet();

            if (checkedNumbers.Count == 0)
                return new List<SpecGroupSnapshot>();

            return all.Where(s => checkedNumbers.Contains(s.GroupNumber)).ToList();
        }

        private List<SpecGroupSnapshot> BuildSpecSnapshotsForCalculator()
        {
            var result = new List<SpecGroupSnapshot>(
                _dataGridVM.SpecGroups.Count);

            foreach (var g in _dataGridVM.SpecGroups)
            {
                var snap = new SpecGroupSnapshot
                {
                    GroupNumber = g.GroupNumber,
                    GroupName = g.GroupName ?? "",
                    SelectedGlassType = g.SelectedGlassType ?? "",
                    SelectedSpecName = g.SelectedSpecName ?? "",
                    OuterGlassColor = g.OuterGlassColor ?? "",
                    InnerGlassColor = g.InnerGlassColor ?? ""
                };

                foreach (var r in g.ItemRows)
                {
                    snap.ItemRows.Add(new GlassItemRowSnapshot
                    {
                        SerialNumber = r.SerialNumber,
                        GlassRef = r.GlassRef ?? "",
                        W1 = r.W1,
                        H1 = r.H1,
                        W2 = r.W2,
                        H2 = r.H2,
                        Qty = r.Qty
                    });
                }

                result.Add(snap);
            }

            return result;
        }

        // ── Spec group change tracking ─────────────────────────────────────

        private void OnSpecGroupsCollectionChanged(
            object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (SpecGroup g in e.OldItems)
                    UnhookSpecGroup(g);

            if (e.NewItems != null)
                foreach (SpecGroup g in e.NewItems)
                    HookSpecGroup(g);

            RequestPolishRecalculation();
            RefreshWastageSelectionSurface();
        }

        private void HookSpecGroup(SpecGroup group)
        {
            group.PropertyChanged += OnSpecGroupPropertyChanged;
            group.ItemRows.CollectionChanged += OnItemRowsCollectionChanged;
            foreach (var item in group.ItemRows)
                item.PropertyChanged += OnItemRowPropertyChanged;
        }

        private void UnhookSpecGroup(SpecGroup group)
        {
            group.PropertyChanged -= OnSpecGroupPropertyChanged;
            group.ItemRows.CollectionChanged -= OnItemRowsCollectionChanged;
            foreach (var item in group.ItemRows)
                item.PropertyChanged -= OnItemRowPropertyChanged;
        }

        private void OnSpecGroupPropertyChanged(
            object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SpecGroup.SelectedGlassType)
                or nameof(SpecGroup.SelectedSpecName)
                or nameof(SpecGroup.OuterGlassColor)
                or nameof(SpecGroup.InnerGlassColor))
            {
                RequestPolishRecalculation();
                RefreshWastageSelectionSurface();
            }

            // Step 4 may re-price/re-size the same selected spec. Its name
            // and type then do not change, but Step 6 still needs to rebuild
            // source provenance and re-seed the U-insert auto rate.
            if (e.PropertyName is nameof(SpecGroup.OuterSheetWidthMm)
                or nameof(SpecGroup.OuterSheetHeightMm)
                or nameof(SpecGroup.InnerSheetWidthMm)
                or nameof(SpecGroup.InnerSheetHeightMm)
                or nameof(SpecGroup.OuterSheetPricePerSqm)
                or nameof(SpecGroup.InnerSheetPricePerSqm)
                or nameof(SpecGroup.UInsertPricePerSqm))
            {
                RefreshWastageSelectionSurface();
            }

            if (e.PropertyName is nameof(SpecGroup.FooterTotalSqm)
                or nameof(SpecGroup.FooterTotalSqm2)
                or nameof(SpecGroup.GroupName))
            {
                RefreshWastageSelectionSurface();
            }
        }

        private void OnItemRowsCollectionChanged(
            object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (GlassItemRow r in e.OldItems)
                    r.PropertyChanged -= OnItemRowPropertyChanged;

            if (e.NewItems != null)
                foreach (GlassItemRow r in e.NewItems)
                    r.PropertyChanged += OnItemRowPropertyChanged;

            RequestPolishRecalculation();
            RefreshWastageSelectionSurface();
        }

        private void OnItemRowPropertyChanged(
            object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(GlassItemRow.W1)
                or nameof(GlassItemRow.H1)
                or nameof(GlassItemRow.W2)
                or nameof(GlassItemRow.H2)
                or nameof(GlassItemRow.Qty))
            {
                RequestPolishRecalculation();
                RefreshWastageSelectionSurface();
            }
        }

        // ── Total ──────────────────────────────────────────────────────────

        private void RecalculateTotal()
        {
            decimal predefinedSum = 0m;
            foreach (var row in ChargeRows)
                if (row.IsActive)
                    predefinedSum += row.ResultAmount;

            decimal wastageSum = WastagesSection?.TotalWastageValue ?? 0m;
            decimal customSum = CustomChargesSection?.TotalCustomChargesValue ?? 0m;

            TotalOtherCharges = Math.Round(
                predefinedSum + wastageSum + customSum, 2);
        }

        /// <summary>
        /// Re-syncs live Step 5 metrics into every charge row. Called when
        /// the user enters Step 6 so QTY / RESULT match the current grid.
        /// </summary>
        public void RefreshWorkspace()
        {
            // Force-refresh outer/inner glass colors from spec names
            // for ALL glass types (SGU, DGU, LAM, DGU+LAM). This ensures
            // wastage suggestions always have color data, fixing the
            // "outer and inner not getting selected automatically" issue
            // when the user enters Step 6 after editing Step 4/5.
            foreach (var group in _dataGridVM.SpecGroups)
                group.RefreshColorsFromSpecName();

            SyncLaminationColorsFromStep4();
            MetricHub.Refresh();
            SyncTargetSelectionsFromHub();
            RecalculatePolishRowsImmediate();
            RefreshWastageSelectionSurface();
            RecalculateTotal();
        }

        /// <summary>
        /// Begins a restore operation from saved state. While in restore mode,
        /// UseAdvancedAutomation changes do NOT trigger ApplyAdvancedSuggestions,
        /// preserving the saved charge selections.
        /// </summary>
        public void BeginRestore()
        {
            _isRestoringSavedState = true;
        }

        /// <summary>
        /// Ends a restore operation. After this, UseAdvancedAutomation changes
        /// will trigger ApplyAdvancedSuggestions as normal.
        /// </summary>
        public void EndRestore()
        {
            _isRestoringSavedState = false;
        }

        /// <summary>
        /// New invoice: predefined rows back to defaults, wastage and
        /// custom lists emptied. Previous invoice selections must not leak.
        /// </summary>
        public void ResetForNewInvoice()
        {
            foreach (var row in ChargeRows)
                row.PropertyChanged -= OnChargeRowChanged;

            BuildChargeRows();

            UseAdvancedAutomation = true;
            WastagesSection.ClearWastageRows();
            WastagesSection.RefreshTotal();

            CustomChargesSection.ClearCharges();
            CustomChargesSection.RefreshTotal();

            RecalculatePolishRowsImmediate();
            RecalculateTotal();
            ApplyAdvancedSuggestions();
        }

        public void Dispose()
        {
            _polishDebouncer.Stop();
            _polishDebouncer.Tick -= OnPolishDebounceElapsed;

            MetricHub.PropertyChanged -= OnHubPropertyChanged;
            MetricHub.PerSpecMetrics.CollectionChanged -= OnPerSpecMetricsChanged;
            _dataGridVM.SpecGroups.CollectionChanged -= OnSpecGroupsCollectionChanged;

            if (_lamVm != null)
                _lamVm.PropertyChanged -= OnLaminationEditorChanged;
            if (_dguLamVm != null)
                _dguLamVm.PropertyChanged -= OnLaminationEditorChanged;

            if (WastagesSection != null)
                WastagesSection.PropertyChanged -= OnSubSectionChanged;
            if (CustomChargesSection != null)
                CustomChargesSection.PropertyChanged -= OnSubSectionChanged;

            foreach (var row in ChargeRows)
                row.PropertyChanged -= OnChargeRowChanged;

            foreach (var g in _dataGridVM.SpecGroups)
                UnhookSpecGroup(g);
        }
    }
}