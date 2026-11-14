// ═══════════════════════════════════════════════════════════════════════
// FILE: SpecGroup.Part1.cs (Phase 3.8 split — member census verified)
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Behaviors;
using ProGlassAutomation.Services.ProformaInvoice.SpecificationsRepository;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.Specifications;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid.GrandTotalSummary;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid
{
        public partial class SpecGroup : ViewModelBase, IExcelPasteable
    {
            private readonly ObservableCollection<SavedSpecification> _specSource = new();
        private ICollectionView _filteredSpecsView;
        private HashSet<string> _sourceKeys = new(StringComparer.OrdinalIgnoreCase);
        private bool _allTypeSpecsMerged = false;
        private bool _isAutoSwitchingGlassType = false;
        private bool _isProgrammaticSpecChange = false;

        // A Step 4 handoff changes the type before the name. Do not run the
        // manual type-switch cleanup in the short interval between those two
        // assignments, otherwise it can clear/carry the incoming spec.
        private bool _isSynchronizingFromStep4;

        // Catalogue auto-fill / type-switch reset writes BaseRate without
        // that write counting as a typed override.
        private bool _isAutoFillingRate;

        // Async catalogue reads are deliberately fire-and-forget. A monotonic
        // token prevents an older type/repository read from adding stale specs
        // after the user (or the Step 4 coordinator) has selected another type.
        private int _specLoadGeneration;
        private ISpecificationsRepository? _specifications;

        // ── Group identity ─────────────────────────────────────────────────

        private int _groupNumber;
        public int GroupNumber
        {
            get => _groupNumber;
            set
            {
                if (_groupNumber == value) return;
                _groupNumber = value;
                OnPropertyChanged();
            }
        }

        private string _groupName = "";
        public string GroupName
        {
            get => _groupName;
            set
            {
                if (_groupName == value) return;
                _groupName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpecDisplayName));
            }
        }

        // ── PRICING ───────────────────────────────────────────────────────

        private decimal _importedPrice;
        /// <summary>
        /// Fixed reference price imported from Step 4 (Spec &amp; Price Generator).
        /// Set ONCE on first spec pick — preserved thereafter even if user
        /// manually edits BaseRate. Displayed read-only in UI.
        /// </summary>
        public decimal ImportedPrice
        {
            get => _importedPrice;
            set
            {
                if (_importedPrice == value) return;
                _importedPrice = value;
                OnPropertyChanged();
            }
        }

        private decimal _baseRate;
        public decimal BaseRate
        {
            get => _baseRate;
            set
            {
                if (_baseRate == value) return;
                _baseRate = value;
                // A Step 4 handoff / catalogue auto-fill / restore must be
                // able to write the quoted rate without that write itself
                // counting as a typed override. The Step 5 box is the only
                // path that owns the field from then on.
                if (!_isSynchronizingFromStep4 && !_isAutoFillingRate)
                    HasManualStep5BaseRateOverride = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SurchargeRate));
                UpdateAllRowRates();
            }
        }

        private decimal _surchargePercent;
        public decimal SurchargePercent
        {
            get => _surchargePercent;
            set
            {
                if (_surchargePercent == value) return;
                _surchargePercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SurchargeRate));
                UpdateAllRowRates();
            }
        }

        public decimal SurchargeRate =>
            MoneyRounding.Round(_baseRate * (1m + (_surchargePercent / 100m)));

        public string SpecDisplayName => !string.IsNullOrWhiteSpace(SelectedSpecName)
            ? SelectedSpecName
            : (GroupName ?? $"Spec Group {GroupNumber}");

        /// <summary>
        /// Step 4 → Step 5 synchronization markers. They travel with the
        /// invoice snapshot so a reopened quote retains the same ownership
        /// boundary: a manual Step 5 edit is never silently reclaimed by
        /// Step 4 on the next Continue.
        /// </summary>
        public string LastSyncedStep4SpecName { get; set; } = "";
        public string LastSyncedStep4GlassType { get; set; } = "";
        /// <summary>
        /// Content fingerprint of the Step 4 section at the last sync.
        /// Lets the coordinator force-update Step 5 when Step 4 moves to a
        /// NEW selection (any pane/sheet/rate edit), while still respecting
        /// operator overrides when Step 4 is untouched. Persisted with the
        /// invoice; "" on legacy data (no hash baseline).
        /// </summary>
        public string LastSyncedStep4ContentHash { get; set; } = "";

        /// <summary>
        /// Explicit Step 5 intent survives a blank selection too. A manual
        /// type switch can deliberately clear a mismatched spec, and that
        /// blank must not be mistaken for a brand-new bootstrap group.
        /// Persisted alongside the sync markers for reopen safety.
        /// </summary>
        public bool HasManualStep5SpecOverride { get; set; }
        public bool HasManualStep5GlassTypeOverride { get; set; }

        /// <summary>
        /// True once the operator has typed a Step 5 base price that differs
        /// from the last Step 4 handoff. Later Continue / Step 9 snapshot
        /// refreshes must keep that price instead of silently restoring the
        /// estimated rate.
        /// </summary>
        public bool HasManualStep5BaseRateOverride { get; set; }

        private string _outerGlassColor = "";
        /// <summary>Step 4 outer / laminated-outer color for this spec.</summary>
        public string OuterGlassColor
        {
            get => _outerGlassColor;
            set
            {
                var next = value ?? "";
                if (_outerGlassColor == next) return;
                _outerGlassColor = next;
                OnPropertyChanged();
            }
        }

        private string _innerGlassColor = "";
        /// <summary>Step 4 inner / laminated-inner color for this spec.</summary>
        public string InnerGlassColor
        {
            get => _innerGlassColor;
            set
            {
                var next = value ?? "";
                if (_innerGlassColor == next) return;
                _innerGlassColor = next;
                OnPropertyChanged();
            }
        }

        private string _thirdGlassColor = "";
        /// <summary>DGU+LAM laminated-inner colour. Empty on other builds.</summary>
        public string ThirdGlassColor
        {
            get => _thirdGlassColor;
            set
            {
                var next = value ?? "";
                if (_thirdGlassColor == next) return;
                _thirdGlassColor = next;
                OnPropertyChanged();
            }
        }

        /// <summary>Null on invoices saved before Phase A.</summary>
        public bool? MaterialIsValid { get; set; }
        public string MaterialInvalidReason { get; set; } = "";

        // ══════════════════════════════════════════════════════════════
        //  PHASE G — SHEET PROVENANCE (Step 4 → Step 5 → Step 9)
        //
        //  These carry the stock sheet the user actually CHOSE in Step 4
        //  so the profit calculation in Step 9 costs that sheet, not one
        //  it re-guesses from the store. Zero means "not chosen" and
        //  Step 9 then falls back (store lookup, then shop default) while
        //  telling the user which source it used.
        //
        //  Display-only in Step 5: the grid never edits these.
        // ══════════════════════════════════════════════════════════════

        private int _outerSheetWidthMm;
        /// <summary>Chosen OUTER stock sheet width in mm (0 = not chosen).</summary>
        public int OuterSheetWidthMm
        {
            get => _outerSheetWidthMm;
            set
            {
                if (_outerSheetWidthMm == value) return;
                _outerSheetWidthMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OuterSheetDisplay));
                OnPropertyChanged(nameof(HasOuterSheet));
                OnPropertyChanged(nameof(SheetSelectionSummary));
                OnPropertyChanged(nameof(Step4RateTooltip));
                OnPropertyChanged(nameof(HasSheetSelectionSummary));
            }
        }

        private int _outerSheetHeightMm;
        /// <summary>Chosen OUTER stock sheet height in mm (0 = not chosen).</summary>
        public int OuterSheetHeightMm
        {
            get => _outerSheetHeightMm;
            set
            {
                if (_outerSheetHeightMm == value) return;
                _outerSheetHeightMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OuterSheetDisplay));
                OnPropertyChanged(nameof(HasOuterSheet));
                OnPropertyChanged(nameof(SheetSelectionSummary));
                OnPropertyChanged(nameof(Step4RateTooltip));
                OnPropertyChanged(nameof(HasSheetSelectionSummary));
            }
        }

        private int _innerSheetWidthMm;
        /// <summary>Chosen INNER stock sheet width in mm (DGU only).</summary>
        public int InnerSheetWidthMm
        {
            get => _innerSheetWidthMm;
            set
            {
                if (_innerSheetWidthMm == value) return;
                _innerSheetWidthMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InnerSheetDisplay));
                OnPropertyChanged(nameof(HasInnerSheet));
                OnPropertyChanged(nameof(SheetSelectionSummary));
                OnPropertyChanged(nameof(Step4RateTooltip));
                OnPropertyChanged(nameof(HasSheetSelectionSummary));
            }
        }

        private int _innerSheetHeightMm;
        /// <summary>Chosen INNER stock sheet height in mm (DGU only).</summary>
        public int InnerSheetHeightMm
        {
            get => _innerSheetHeightMm;
            set
            {
                if (_innerSheetHeightMm == value) return;
                _innerSheetHeightMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InnerSheetDisplay));
                OnPropertyChanged(nameof(HasInnerSheet));
                OnPropertyChanged(nameof(SheetSelectionSummary));
                OnPropertyChanged(nameof(Step4RateTooltip));
                OnPropertyChanged(nameof(HasSheetSelectionSummary));
            }
        }

        private decimal _outerSheetPricePerSqm;
        public decimal OuterSheetPricePerSqm
        {
            get => _outerSheetPricePerSqm;
            set
            {
                if (_outerSheetPricePerSqm == value) return;
                _outerSheetPricePerSqm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SheetSelectionSummary));
                OnPropertyChanged(nameof(Step4RateTooltip));
                OnPropertyChanged(nameof(HasSheetSelectionSummary));
            }
        }

        private decimal _innerSheetPricePerSqm;
        public decimal InnerSheetPricePerSqm
        {
            get => _innerSheetPricePerSqm;
            set
            {
                if (_innerSheetPricePerSqm == value) return;
                _innerSheetPricePerSqm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SheetSelectionSummary));
                OnPropertyChanged(nameof(Step4RateTooltip));
                OnPropertyChanged(nameof(HasSheetSelectionSummary));
            }
        }

        private int _thirdSheetWidthMm;
        public int ThirdSheetWidthMm
        {
            get => _thirdSheetWidthMm;
            set
            {
                if (_thirdSheetWidthMm == value) return;
                _thirdSheetWidthMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThirdSheet));
                OnPropertyChanged(nameof(SheetSelectionSummary));
                OnPropertyChanged(nameof(Step4RateTooltip));
                OnPropertyChanged(nameof(HasSheetSelectionSummary));
            }
        }

        private int _thirdSheetHeightMm;
        public int ThirdSheetHeightMm
        {
            get => _thirdSheetHeightMm;
            set
            {
                if (_thirdSheetHeightMm == value) return;
                _thirdSheetHeightMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThirdSheet));
                OnPropertyChanged(nameof(SheetSelectionSummary));
                OnPropertyChanged(nameof(Step4RateTooltip));
                OnPropertyChanged(nameof(HasSheetSelectionSummary));
            }
        }

        private decimal _thirdSheetPricePerSqm;
        public decimal ThirdSheetPricePerSqm
        {
            get => _thirdSheetPricePerSqm;
            set
            {
                if (_thirdSheetPricePerSqm == value) return;
                _thirdSheetPricePerSqm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SheetSelectionSummary));
                OnPropertyChanged(nameof(Step4RateTooltip));
                OnPropertyChanged(nameof(HasSheetSelectionSummary));
            }
        }

        private decimal _uInsertPricePerSqm;
        /// <summary>
        /// Step 4 seeded U-Insert rate for this spec. Step 5 does not render
        /// it; Step 6 advanced mode consumes it when the spec carries a
        /// U-Insert spacer extra.
        /// </summary>
        public decimal UInsertPricePerSqm
        {
            get => _uInsertPricePerSqm;
            set
            {
                if (_uInsertPricePerSqm == value) return;
                _uInsertPricePerSqm = value;
                OnPropertyChanged();
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  PHASE 1-AUDIT — per-group production-cost provenance.
        //  Written by ApplyStep4PaneAndSheetMetadata at every Step 4 → 5
        //  handoff; consumed by Step 9's production rows with live-panel
        //  fallback so single-section invoices behave exactly as before.
        //  Null = "no Step 4 production rates captured for this group".
        // ══════════════════════════════════════════════════════════════

        private decimal? _step4SpacerPricePerSqm;
        public decimal? Step4SpacerPricePerSqm
        {
            get => _step4SpacerPricePerSqm;
            set
            {
                if (_step4SpacerPricePerSqm == value) return;
                _step4SpacerPricePerSqm = value;
                OnPropertyChanged();
            }
        }

        private decimal? _step4InterlayerCostPerSqm;
        public decimal? Step4InterlayerCostPerSqm
        {
            get => _step4InterlayerCostPerSqm;
            set
            {
                if (_step4InterlayerCostPerSqm == value) return;
                _step4InterlayerCostPerSqm = value;
                OnPropertyChanged();
            }
        }

        private decimal? _step4CuttingCostPerSqm;
        public decimal? Step4CuttingCostPerSqm
        {
            get => _step4CuttingCostPerSqm;
            set
            {
                if (_step4CuttingCostPerSqm == value) return;
                _step4CuttingCostPerSqm = value;
                OnPropertyChanged();
            }
        }

        private decimal? _step4TemperingCostPerSqm;
        public decimal? Step4TemperingCostPerSqm
        {
            get => _step4TemperingCostPerSqm;
            set
            {
                if (_step4TemperingCostPerSqm == value) return;
                _step4TemperingCostPerSqm = value;
                OnPropertyChanged();
            }
        }

        /// <summary>True when an outer stock sheet was chosen in Step 4.</summary>
        public bool HasOuterSheet =>
            OuterSheetWidthMm > 0 && OuterSheetHeightMm > 0;

        /// <summary>True when an inner stock sheet was chosen in Step 4.</summary>
        public bool HasInnerSheet =>
            InnerSheetWidthMm > 0 && InnerSheetHeightMm > 0;

        public bool HasThirdSheet =>
            ThirdSheetWidthMm > 0 && ThirdSheetHeightMm > 0;

        /// <summary>e.g. "3210 × 2250 · 7.2225 SQM", or "—".</summary>
        public string OuterSheetDisplay => HasOuterSheet
            ? $"{OuterSheetWidthMm} × {OuterSheetHeightMm} · " +
              $"{(OuterSheetWidthMm * (decimal)OuterSheetHeightMm) / 1_000_000m:0.####} SQM"
            : "—";

        /// <summary>e.g. "3210 × 2250 · 7.2225 SQM", or "—".</summary>
        public string InnerSheetDisplay => HasInnerSheet
            ? $"{InnerSheetWidthMm} × {InnerSheetHeightMm} · " +
              $"{(InnerSheetWidthMm * (decimal)InnerSheetHeightMm) / 1_000_000m:0.####} SQM"
            : "—";

        /// <summary>
        /// Human-readable sheet provenance carried from Step 4 so Step 5 can
        /// show exactly which third-party stock sheet this spec will use.
        /// </summary>
        public string SheetSelectionSummary
        {
            get
            {
                var parts = new List<string>();
                if (HasOuterSheet)
                    parts.Add($"{(SelectedGlassType == "SGU" ? "Single pane" : "Outer")} sheet {OuterSheetDisplay}");
                if (HasInnerSheet)
                    parts.Add($"Inner sheet {InnerSheetDisplay}");
                if (HasThirdSheet)
                    parts.Add($"Lam inner sheet {ThirdSheetWidthMm} × {ThirdSheetHeightMm}");
                return parts.Count == 0 ? "" : string.Join("  ·  ", parts);
            }
        }

        public bool HasSheetSelectionSummary =>
            !string.IsNullOrWhiteSpace(SheetSelectionSummary);

        // ── Glass type ────────────────────────────────────────────────────

        private string _selectedGlassType = "";
        public string SelectedGlassType
        {
            get => _selectedGlassType;
            set
            {
                string next = value ?? "";
                if (_selectedGlassType == next) return;

                string previous = _selectedGlassType;
                _selectedGlassType = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Step4RateTooltip));
                OnPropertyChanged(nameof(SheetSelectionSummary));
                UpdatePerimeterFactors();

                if (!_isAutoSwitchingGlassType && !_isSynchronizingFromStep4)
                {
                    // An explicit type choice means Step 5 now owns both
                    // parts of the selection. Clearing the provenance markers
                    // lets the coordinator distinguish it from a failed old
                    // bootstrap handoff (DGU name + default SGU type).
                    LastSyncedStep4GlassType = "";
                    LastSyncedStep4SpecName = "";
                    Step4EstimatedRatePerSqm = 0m;
                    if (!string.IsNullOrWhiteSpace(previous))
                    {
                        HasManualStep5GlassTypeOverride = true;
                        // A manual type switch can clear/carry the name below;
                        // keep that resulting blank under operator ownership.
                        HasManualStep5SpecOverride = true;
                    }
                    ResetSpecSelectionForManualGlassTypeChange(previous, next);
                }

                _ = LoadSpecsAsync();
            }
        }

        // ── Spec autocomplete ─────────────────────────────────────────────

        private string _selectedSpecName = "";
        public string SelectedSpecName
        {
            get => _selectedSpecName;
            set
            {
                var newValue = value ?? "";
                if (string.Equals(
                        _selectedSpecName, newValue,
                        StringComparison.Ordinal)) return;

                _selectedSpecName = newValue;

                if (!_isProgrammaticSpecChange && !_isSynchronizingFromStep4)
                {
                    LastSyncedStep4SpecName = "";
                    Step4EstimatedRatePerSqm = 0m;
                    HasManualStep5SpecOverride = true;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(SpecDisplayName));
                RefreshGlassColorsFromSpecName();
                OnPropertyChanged(nameof(SheetSelectionSummary));
                OnPropertyChanged(nameof(Step4RateTooltip));
                OnPropertyChanged(nameof(HasSheetSelectionSummary));

                // PHASE G: adopt the stock sheet chosen for this spec in
                // Step 4. A miss (spec from an earlier session) leaves the
                // fields at zero and Step 9 falls back, stating the source.
                AdoptSheetSelection(newValue);

                if (!_isProgrammaticSpecChange)
                {
                    _isProgrammaticSpecChange = true;
                    try
                    {
                        if (!string.Equals(
                                _specTypedText, newValue,
                                StringComparison.Ordinal))
                        {
                            _specTypedText = newValue;
                            OnPropertyChanged(nameof(SpecTypedText));
                        }

                        var match = _specSource.FirstOrDefault(s =>
                            string.Equals(
                                s.SpecName, newValue,
                                StringComparison.OrdinalIgnoreCase));

                        if (!ReferenceEquals(_selectedSpecItem, match))
                        {
                            _selectedSpecItem = match;
                            OnPropertyChanged(nameof(SelectedSpecItem));
                        }
                    }
                    finally { _isProgrammaticSpecChange = false; }
                }

                TryAutoFillRate();
            }
        }

        private string _specTypedText = "";
        public string SpecTypedText
        {
            get => _specTypedText;
            set
            {
                var newValue = value ?? "";
                if (string.Equals(
                        _specTypedText, newValue,
                        StringComparison.Ordinal)) return;

                _specTypedText = newValue;
                OnPropertyChanged();

                if (_isProgrammaticSpecChange) return;

                if (!string.Equals(newValue, LastSyncedStep4SpecName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    LastSyncedStep4SpecName = "";
                    Step4EstimatedRatePerSqm = 0m;
                    HasManualStep5SpecOverride = true;
                }

                _filteredSpecsView?.Refresh();
                IsSpecDropdownOpen =
                    !string.IsNullOrEmpty(_specTypedText) && ViewHasAnyItems();

                if (!string.IsNullOrEmpty(_specTypedText) &&
                    !_allTypeSpecsMerged && !ViewHasAnyItems())
                {
                    _ = LoadAllTypesFallbackAsync();
                }

                // 2026-09-10: SUGGEST-ONLY typing. Matching the popup
                // list is the operator's job — a typed spec name that
                // happens to equal an existing spec must NOT auto-commit,
                // auto-switch the glass type or auto-fill the rate. The
                // dropdown suggests; ONLY an explicit pick (click or
                // Enter on the highlighted suggestion — SelectedSpecItem)
                // commits. Free-typed names remain fully supported below.
                _selectedSpecName = _specTypedText;
                OnPropertyChanged(nameof(SelectedSpecName));
                OnPropertyChanged(nameof(SpecDisplayName));

                if (_selectedSpecItem != null &&
                    !_isProgrammaticSpecChange &&
                    !string.Equals(
                        _selectedSpecItem.SpecName, _specTypedText,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _selectedSpecItem = null;
                    OnPropertyChanged(nameof(SelectedSpecItem));
                }
            }
        }

        private SavedSpecification _selectedSpecItem;
        public SavedSpecification SelectedSpecItem
        {
            get => _selectedSpecItem;
            set
            {
                if (ReferenceEquals(_selectedSpecItem, value)) return;
                _selectedSpecItem = value;
                OnPropertyChanged();

                if (value == null || _isProgrammaticSpecChange) return;

                if (!string.Equals(value.SpecName, LastSyncedStep4SpecName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    LastSyncedStep4SpecName = "";
                    Step4EstimatedRatePerSqm = 0m;
                    HasManualStep5SpecOverride = true;
                }

                _isProgrammaticSpecChange = true;
                try
                {
                    _specTypedText = value.SpecName;
                    OnPropertyChanged(nameof(SpecTypedText));

                    _selectedSpecName = value.SpecName;
                    OnPropertyChanged(nameof(SelectedSpecName));
                    OnPropertyChanged(nameof(SpecDisplayName));

                    IsSpecDropdownOpen = false;
                    RefreshGlassColorsFromSpecName();
                    TryAutoFillRate();
                    HandleSpecCommit(value.SpecName);
                }
                finally { _isProgrammaticSpecChange = false; }
            }
        }

        private bool _isSpecDropdownOpen;
        public bool IsSpecDropdownOpen
        {
            get => _isSpecDropdownOpen;
            set
            {
                if (_isSpecDropdownOpen == value) return;
                _isSpecDropdownOpen = value;
                OnPropertyChanged();
            }
        }

        public ICollectionView FilteredSpecs => _filteredSpecsView;

        // ── ItemRows ──────────────────────────────────────────────────────

        public BulkObservableCollection<GlassItemRow> ItemRows { get; } = new();

        // ── Bulk row generation ───────────────────────────────────────────

        private string _bulkRowCountText = "";
        public string BulkRowCountText
        {
            get => _bulkRowCountText;
            set
            {
                if (_bulkRowCountText == value) return;
                _bulkRowCountText = value;
                OnPropertyChanged();
            }
        }

        // ── Paste progress ────────────────────────────────────────────────

        private bool _isPasting;
        public bool IsPasting
        {
            get => _isPasting;
            private set
            {
                if (_isPasting == value) return;
                _isPasting = value;
                OnPropertyChanged();
            }
        }

        private int _pasteProgress;
        public int PasteProgress
        {
            get => _pasteProgress;
            private set
            {
                if (_pasteProgress == value) return;
                _pasteProgress = value;
                OnPropertyChanged();
            }
        }

        private string _pasteStatusText = "";
        public string PasteStatusText
        {
            get => _pasteStatusText;
            private set
            {
                if (_pasteStatusText == value) return;
                _pasteStatusText = value;
                OnPropertyChanged();
            }
        }

        // ── Toast ─────────────────────────────────────────────────────────

        private bool _isToastVisible;
        public bool IsToastVisible
        {
            get => _isToastVisible;
            private set
            {
                if (_isToastVisible == value) return;
                _isToastVisible = value;
                OnPropertyChanged();
            }
        }

        private string _toastMessage = "";
        public string ToastMessage
        {
            get => _toastMessage;
            private set
            {
                if (_toastMessage == value) return;
                _toastMessage = value;
                OnPropertyChanged();
            }
        }

        private bool _toastIsError;
        public bool ToastIsError
        {
            get => _toastIsError;
            private set
            {
                if (_toastIsError == value) return;
                _toastIsError = value;
                OnPropertyChanged();
            }
        }

        // ── Footer totals ─────────────────────────────────────────────────

        private int _totalRows;
        public int TotalRows
        {
            get => _totalRows;
            private set
            {
                if (_totalRows == value) return;
                _totalRows = value;
                OnPropertyChanged();
            }
        }

        private int _footerTotalQty;
        public int FooterTotalQty
        {
            get => _footerTotalQty;
            private set
            {
                if (_footerTotalQty == value) return;
                _footerTotalQty = value;
                OnPropertyChanged();
            }
        }

        private decimal _footerTotalSqm;
        public decimal FooterTotalSqm
        {
            get => _footerTotalSqm;
            private set
            {
                if (_footerTotalSqm == value) return;
                _footerTotalSqm = value;
                OnPropertyChanged();
            }
        }

        private decimal _footerTotalSqm2;
        public decimal FooterTotalSqm2
        {
            get => _footerTotalSqm2;
            private set
            {
                if (_footerTotalSqm2 == value) return;
                _footerTotalSqm2 = value;
                OnPropertyChanged();
            }
        }

        private decimal _footerTotalLm1;
        public decimal FooterTotalLm1
        {
            get => _footerTotalLm1;
            private set
            {
                if (_footerTotalLm1 == value) return;
                _footerTotalLm1 = value;
                OnPropertyChanged();
            }
        }

        private decimal _footerTotalLm2;
        public decimal FooterTotalLm2
        {
            get => _footerTotalLm2;
            private set
            {
                if (_footerTotalLm2 == value) return;
                _footerTotalLm2 = value;
                OnPropertyChanged();
            }
        }

        private decimal _footerTotalValue;
        public decimal FooterTotalValue
        {
            get => _footerTotalValue;
            private set
            {
                if (_footerTotalValue == value) return;
                _footerTotalValue = value;
                OnPropertyChanged();
            }
        }

        // ── Commands ──────────────────────────────────────────────────────

        public ICommand AddRowCommand { get; }
        public ICommand RemoveRowCommand { get; }
        public ICommand PasteExcelCommand { get; }
        public ICommand GenerateRowsCommand { get; }
        public ICommand OpenFindCommand { get; }
        public ICommand OpenFindReplaceCommand { get; }

        private bool _isBulkAdding = false;

        // ══════════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Refresh quote rates without turning an automatic update into a manual override.</summary>
        public void ApplyStep4RateSnapshot(Step4PricingSnapshot snapshot)
        {
            bool wasSynchronizing = _isSynchronizingFromStep4;
            _isSynchronizingFromStep4 = true;
            try
            {
                Step4EstimatedRatePerSqm = snapshot.EstimatedRatePerSqm;
                if (snapshot.EstimatedRatePerSqm > 0m && !HasManualStep5BaseRateOverride)
                    BaseRate = snapshot.EstimatedRatePerSqm;
                if (snapshot.EstimatedRatePerSqm > 0m && ImportedPrice <= 0m)
                    ImportedPrice = snapshot.EstimatedRatePerSqm;
            }
            finally { _isSynchronizingFromStep4 = wasSynchronizing; }
        }

        private decimal _step4EstimatedRatePerSqm;
        public decimal Step4EstimatedRatePerSqm
        {
            get => _step4EstimatedRatePerSqm;
            set { _step4EstimatedRatePerSqm = value; OnPropertyChanged(); OnPropertyChanged(nameof(Step4RateTooltip)); }
        }

        public string Step4RateTooltip => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "Step 4 estimated selling rate: {0}\nOuter / single-pane sheet rate: AED {1:0.00} /m²{2}{3}\nSheet rates are material inputs, not separate selling estimates. Base rate may be manually overridden.",
            Step4EstimatedRatePerSqm > 0m
                ? "AED " + Step4EstimatedRatePerSqm.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " /m²"
                : "not available for this selection", OuterSheetPricePerSqm,
            SelectedGlassType == "SGU" ? "" : $"\nInner sheet rate: AED {InnerSheetPricePerSqm:0.00} /m²",
            SelectedGlassType == "DGU+LAM" ? $"\nLam inner sheet rate: AED {ThirdSheetPricePerSqm:0.00} /m²" : "");

        /// <summary>
        /// Applies the full, per-section Step 4 selection to an owned Step 5
        /// group. This is intentionally a direct snapshot handoff instead of
        /// waiting for SpecSheetRegistry: Step 4's catalogue write is async,
        /// while Continue must immediately preserve the selected sheets,
        /// pane prices, colours and U-insert seed for this exact group.
        /// </summary>
        public void ApplyStep4PricingSnapshot(Step4PricingSnapshot snapshot)
        {
            if (snapshot == null) return;

            string glassType = NormalizeSupportedGlassType(snapshot.SelectedGlassTypeCode);
            bool incomingSpecChangesSelection =
                !string.IsNullOrWhiteSpace(snapshot.GeneratedSpecName)
                && !string.Equals(_selectedSpecName, snapshot.GeneratedSpecName,
                    StringComparison.OrdinalIgnoreCase);

            _isSynchronizingFromStep4 = true;
            try
            {
                HasManualStep5SpecOverride = false;
                HasManualStep5GlassTypeOverride = false;
                // Type MUST be assigned before the spec name. The normal
                // setter protects manual changes by clearing a mismatched name;
                // the synchronization guard above keeps that behavior from
                // affecting this atomic Step 4 handoff.
                if (glassType.Length > 0)
                    SelectedGlassType = glassType;

                if (!string.IsNullOrWhiteSpace(snapshot.GeneratedSpecName))
                    SelectedSpecName = snapshot.GeneratedSpecName;

                ApplyStep4PaneAndSheetMetadata(snapshot, glassType);
                Step4EstimatedRatePerSqm = snapshot.EstimatedRatePerSqm;
                MaterialIsValid = snapshot.MaterialIsValid;
                MaterialInvalidReason = snapshot.MaterialInvalidReason ?? "";

                // Keep provenance inside the atomic handoff. Besides avoiding a
                // transient type/name mismatch, this makes any future caller of
                // this public method retain Step 4 ownership on the next refresh.
                LastSyncedStep4SpecName = string.IsNullOrWhiteSpace(snapshot.GeneratedSpecName)
                    ? string.Empty
                    : snapshot.GeneratedSpecName;
                LastSyncedStep4GlassType = glassType;
                LastSyncedStep4ContentHash = snapshot.ComputeContentHash();

                // A typed Step 5 base price is operator-owned even when the
                // spec/type are still Step-4-owned. Step 9 live recalc used
                // to re-apply this snapshot and wipe that price.
                if (snapshot.EstimatedRatePerSqm > 0m
                    && !HasManualStep5BaseRateOverride)
                {
                    BaseRate = snapshot.EstimatedRatePerSqm;
                }
            }
            finally
            {
                _isSynchronizingFromStep4 = false;
            }

            if (snapshot.EstimatedRatePerSqm > 0m
                && (incomingSpecChangesSelection || ImportedPrice <= 0m))
            {
                ImportedPrice = snapshot.EstimatedRatePerSqm;
            }
        }

        private void ApplyStep4PaneAndSheetMetadata(
            Step4PricingSnapshot snapshot,
            string glassType)
        {
            // Do NOT blank every field to 0 before writing the new values.
            // Setters fire PropertyChanged on 25 → 0 → 25, and Step 6
            // advanced automation treats that as a real sheet-price change,
            // re-running ApplyAdvancedSuggestions and resetting operator
            // Other Charges rates / targets. Assign the incoming values
            // directly; unused fields for the new build are cleared once.

            switch (glassType)
            {
                case "SGU":
                {
                    var sgu = snapshot.Sgu ?? new SguPricingPanelSnapshot();
                    OuterGlassColor = sgu.GlassColor ?? "";
                    InnerGlassColor = "";
                    OuterSheetWidthMm = sgu.SheetWidthMm;
                    OuterSheetHeightMm = sgu.SheetHeightMm;
                    OuterSheetPricePerSqm = sgu.BasePricePerSqm;
                    InnerSheetWidthMm = 0;
                    InnerSheetHeightMm = 0;
                    InnerSheetPricePerSqm = 0m;
                    ThirdGlassColor = "";
                    ThirdSheetWidthMm = 0;
                    ThirdSheetHeightMm = 0;
                    ThirdSheetPricePerSqm = 0m;
                    UInsertPricePerSqm = 0m;
                    Step4SpacerPricePerSqm = null;
                    Step4InterlayerCostPerSqm = null;
                    Step4CuttingCostPerSqm = sgu.CuttingCostPerSqm;
                    Step4TemperingCostPerSqm = sgu.TemperingCostPerSqm;
                    break;
                }

                case "DGU":
                {
                    var dgu = snapshot.Dgu ?? new DguPricingPanelSnapshot();
                    OuterGlassColor = dgu.OuterGlassColor ?? "";
                    InnerGlassColor = dgu.InnerGlassColor ?? "";
                    OuterSheetWidthMm = dgu.OuterSheetWidthMm;
                    OuterSheetHeightMm = dgu.OuterSheetHeightMm;
                    InnerSheetWidthMm = dgu.InnerSheetWidthMm;
                    InnerSheetHeightMm = dgu.InnerSheetHeightMm;
                    OuterSheetPricePerSqm = dgu.OuterBasePrice;
                    InnerSheetPricePerSqm = dgu.InnerBasePrice;
                    ThirdGlassColor = "";
                    ThirdSheetWidthMm = 0;
                    ThirdSheetHeightMm = 0;
                    ThirdSheetPricePerSqm = 0m;
                    UInsertPricePerSqm = HasUInsertExtra(dgu.SpacerExtra)
                        ? dgu.UInsertPricePerSqm
                        : 0m;
                    Step4SpacerPricePerSqm = dgu.SpacerPrice;
                    Step4InterlayerCostPerSqm = null;
                    Step4CuttingCostPerSqm = null;
                    Step4TemperingCostPerSqm = null;
                    break;
                }

                case "LAM":
                {
                    var lam = snapshot.Lam ?? new LamPricingPanelSnapshot();
                    OuterGlassColor = lam.OuterGlassColor ?? "";
                    InnerGlassColor = lam.InnerGlassColor ?? "";
                    OuterSheetWidthMm = lam.OuterSheetWidthMm;
                    OuterSheetHeightMm = lam.OuterSheetHeightMm;
                    InnerSheetWidthMm = lam.InnerSheetWidthMm;
                    InnerSheetHeightMm = lam.InnerSheetHeightMm;
                    OuterSheetPricePerSqm = lam.OuterBasePrice;
                    InnerSheetPricePerSqm = lam.InnerBasePrice;
                    ThirdGlassColor = "";
                    ThirdSheetWidthMm = 0;
                    ThirdSheetHeightMm = 0;
                    ThirdSheetPricePerSqm = 0m;
                    UInsertPricePerSqm = 0m;
                    // LAM's interlayer is the PVB material rate; cutting and
                    // tempering are stored as the summed pane-pair rates —
                    // exactly the quantities Step 9's LAM branch adds.
                    Step4SpacerPricePerSqm = null;
                    Step4InterlayerCostPerSqm = lam.PvbMaterialRate;
                    Step4CuttingCostPerSqm =
                        lam.OuterCuttingCost + lam.InnerCuttingCost;
                    Step4TemperingCostPerSqm =
                        lam.OuterTemperingCost + lam.InnerTemperingCost;
                    break;
                }

                case "DGU+LAM":
                {
                    var dguLam = snapshot.DguLam ?? new DguLamPricingPanelSnapshot();

                    // SpecGroup has two colour fields. For the three-pane
                    // hybrid they deliberately model the laminated pair; the
                    // Step 6 source builder reads the physical outer/lam outer/
                    // lam inner values independently from this same snapshot.
                    OuterGlassColor = dguLam.LamOuterColor ?? "";
                    InnerGlassColor = dguLam.LamInnerColor ?? "";
                    ThirdGlassColor = dguLam.LamInnerColor ?? "";
                    OuterSheetWidthMm = dguLam.OuterSheetWidthMm;
                    OuterSheetHeightMm = dguLam.OuterSheetHeightMm;
                    InnerSheetWidthMm = dguLam.LamOuterSheetWidthMm;
                    InnerSheetHeightMm = dguLam.LamOuterSheetHeightMm;
                    ThirdSheetWidthMm = dguLam.LamInnerSheetWidthMm;
                    ThirdSheetHeightMm = dguLam.LamInnerSheetHeightMm;
                    OuterSheetPricePerSqm = dguLam.OuterBasePrice;
                    InnerSheetPricePerSqm = dguLam.LamOuterBasePrice;
                    ThirdSheetPricePerSqm = dguLam.LamInnerBasePrice;
                    UInsertPricePerSqm = 0m;
                    // Interlayer is costed at the laminator BUY price — the
                    // sell spread belongs to the quoted rate, not to cost.
                    Step4SpacerPricePerSqm = dguLam.SpacerPrice;
                    Step4InterlayerCostPerSqm = dguLam.InterlayerBuyPrice;
                    Step4CuttingCostPerSqm =
                        dguLam.OuterCuttingCost
                        + dguLam.LamOuterCuttingCost
                        + dguLam.LamInnerCuttingCost;
                    Step4TemperingCostPerSqm =
                        dguLam.OuterTemperingCost
                        + dguLam.LamOuterTemperingCost
                        + dguLam.LamInnerTemperingCost;
                    break;
                }

                default:
                    // A malformed/legacy type should not leave colour data
                    // from the previously selected build on screen.
                    OuterGlassColor = "";
                    InnerGlassColor = "";
                    ThirdGlassColor = "";
                    OuterSheetWidthMm = 0;
                    OuterSheetHeightMm = 0;
                    InnerSheetWidthMm = 0;
                    InnerSheetHeightMm = 0;
                    ThirdSheetWidthMm = 0;
                    ThirdSheetHeightMm = 0;
                    OuterSheetPricePerSqm = 0m;
                    InnerSheetPricePerSqm = 0m;
                    ThirdSheetPricePerSqm = 0m;
                    UInsertPricePerSqm = 0m;
                    Step4SpacerPricePerSqm = null;
                    Step4InterlayerCostPerSqm = null;
                    Step4CuttingCostPerSqm = null;
                    Step4TemperingCostPerSqm = null;
                    break;
            }
        }

        private static string NormalizeSupportedGlassType(string? value)
        {
            string type = (value ?? "").Trim().ToUpperInvariant();
            return type is "SGU" or "DGU" or "LAM" or "DGU+LAM" ? type : "";
        }

        private static bool HasUInsertExtra(string? value) =>
            !string.IsNullOrWhiteSpace(value)
            && value.IndexOf("U-INSERT", StringComparison.OrdinalIgnoreCase) >= 0;

        public void ApplyLaminateColors(string? outerColor, string? innerColor)
        {
            OuterGlassColor = outerColor ?? "";
            InnerGlassColor = innerColor ?? "";
        }

        /// <summary>
        /// Public entry point for Step 6's RefreshWorkspace to force a
        /// re-extraction of outer/inner glass colors from the current
        /// spec name. A non-empty Step 4 colour is retained when legacy or
        /// free-typed spec text cannot be parsed; blanking it would erase the
        /// only reliable outer/inner source identity for wastage.
        /// </summary>
        public void RefreshColorsFromSpecName()
        {
            // Step 6 calls this after every Step 4 sync and on entering
            // Step 6. When the group is Step-4-owned with authoritative
            // snapshot colours already present, re-parsing the spec NAME
            // can only degrade them (the name does not losslessly encode
            // pane/substrate/supplier detail) — keep the snapshot colours.
            // Blank colours still fall through to the name parser below.
            if (IsStep4OwnedSelection && !string.IsNullOrWhiteSpace(OuterGlassColor))
                return;
            RefreshGlassColorsFromSpecName();
        }

        /// <summary>
        /// True when the current selection is exactly what Step 4 last
        /// synced (markers match, no operator override since).
        /// </summary>
        private bool IsStep4OwnedSelection =>
            !HasManualStep5SpecOverride
            && !HasManualStep5GlassTypeOverride
            && !string.IsNullOrWhiteSpace(LastSyncedStep4SpecName)
            && string.Equals(_selectedSpecName, LastSyncedStep4SpecName,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(_selectedGlassType, LastSyncedStep4GlassType,
                StringComparison.OrdinalIgnoreCase);

        private void RefreshGlassColorsFromSpecName()
        {
            // During the Step 4 handoff the snapshot carries authoritative
            // pane colours (applied right after the name assignment) — the
            // name parser must not overwrite them mid-handoff.
            if (_isSynchronizingFromStep4)
                return;

            if (string.IsNullOrWhiteSpace(_selectedSpecName))
            {
                OuterGlassColor = "";
                InnerGlassColor = "";
                return;
            }

            var panes = PnLSpecParser.GlassMatches(_selectedSpecName);
            if (panes.Count == 0)
                return; // Preserve the explicit Step 4 colours for unparseable text.

            string effectiveType = GlassTypeDetector.Detect(_selectedSpecName);
            if (effectiveType == "Unknown")
                effectiveType = NormalizeSupportedGlassType(_selectedGlassType);

            // DGU+LAM has three panes. The group fields intentionally represent
            // the laminated pair; its physical outer pane is retained by the
            // Step 4 snapshot and resolved separately in the wastage builder.
            int outerIndex = effectiveType == "DGU+LAM" && panes.Count >= 3 ? 1 : 0;
            int innerIndex = effectiveType == "DGU+LAM" && panes.Count >= 3
                ? 2
                : panes.Count - 1;

            string parsedOuter = (panes[outerIndex].Groups["color"].Value ?? "").Trim();
            string parsedInner = (panes[innerIndex].Groups["color"].Value ?? "").Trim();

            if (!string.IsNullOrWhiteSpace(parsedOuter))
                OuterGlassColor = parsedOuter;

            if (panes.Count > 1 && !string.IsNullOrWhiteSpace(parsedInner))
                InnerGlassColor = parsedInner;
            else if (effectiveType == "SGU")
                InnerGlassColor = "";
        }

        private void AdoptSheetSelection(string specName)
        {
            try
            {
                // Step 4 snapshot apply writes pane/sheet metadata itself.
                // Adopting from the registry here would zero every sheet
                // field first and fire the Step 6 reset cascade.
                if (_isSynchronizingFromStep4) return;

                // A miss is normal (spec from an earlier session). Reset to
                // zero so Step 9 / Step 6 fall back honestly instead of
                // inheriting the previous spec's sheet or U-Insert rate.
                OuterSheetWidthMm = 0;
                OuterSheetHeightMm = 0;
                InnerSheetWidthMm = 0;
                InnerSheetHeightMm = 0;
                ThirdSheetWidthMm = 0;
                ThirdSheetHeightMm = 0;
                OuterSheetPricePerSqm = 0m;
                InnerSheetPricePerSqm = 0m;
                ThirdSheetPricePerSqm = 0m;
                UInsertPricePerSqm = 0m;

                var selection = ProGlassAutomation.Services.ProformaInvoice
                    .SpecSheetRegistry.TryGet(specName);

                if (selection == null) return;

                if (selection.HasOuter)
                {
                    OuterSheetWidthMm = selection.OuterWidthMm;
                    OuterSheetHeightMm = selection.OuterHeightMm;
                }

                if (selection.HasInner)
                {
                    InnerSheetWidthMm = selection.InnerWidthMm;
                    InnerSheetHeightMm = selection.InnerHeightMm;
                }

                OuterSheetPricePerSqm = selection.OuterPricePerSqm;
                InnerSheetPricePerSqm = selection.InnerPricePerSqm;
                UInsertPricePerSqm = selection.UInsertPricePerSqm;
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    $"[SpecGroup] sheet selection not adopted: {ExceptionMessages.Deep(ex)}");
            }
        }

        /// <summary>
        /// Injected specifications store (replaces the App-level static).
        /// A restored group receives its repository after its selected type;
        /// trigger a guarded reload here so that ordering cannot leave the
        /// Step 5 catalogue permanently empty.
        /// </summary>
        public ISpecificationsRepository? Specifications
        {
            get => _specifications;
            set
            {
                if (ReferenceEquals(_specifications, value)) return;
                _specifications = value;

                // Invalidate an in-flight request from a previous repository.
                _specLoadGeneration++;

                if (_specifications != null
                    && !string.IsNullOrWhiteSpace(_selectedGlassType))
                {
                    _ = LoadSpecsAsync();
                }
            }
        }

        // INSTANT CATALOGUE SYNC (2026-09-02).
        //
        // Held as a field so the delegate has a stable identity and can be
        // detached. Subscribed WEAKLY: SpecGroups are dropped via
        // SpecGroups.Clear()/Remove() with no Dispose() call anywhere, so a
        // direct handler on a static event would root every group of every
        // invoice ever opened. The handler only keeps a WeakReference to
        // this instance and unhooks itself once the target is collected.
        private EventHandler? _specCatalogHandler;

        private void HookSpecCatalogNotifications()
        {
            var weakSelf = new WeakReference<SpecGroup>(this);

            _specCatalogHandler = (_, __) =>
            {
                if (!weakSelf.TryGetTarget(out var self))
                {
                    // Target is gone; stop listening so the closure dies too.
                    if (_specCatalogHandler != null)
                        Services.ProformaInvoice.SpecificationsRepository
                            .SpecCatalogNotifier.Changed -= _specCatalogHandler;
                    return;
                }

                // NotifyChanged can fire on a background thread (the write is
                // async) while _specSource is bound to the dropdown, so the
                // reload must be marshalled.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                    _ = self.LoadSpecsAsync();
                else
                    dispatcher.BeginInvoke(new Action(() => _ = self.LoadSpecsAsync()));
            };

            Services.ProformaInvoice.SpecificationsRepository
                .SpecCatalogNotifier.Changed += _specCatalogHandler;
        }

        public SpecGroup()
        {
            _filteredSpecsView = CollectionViewSource.GetDefaultView(_specSource);
            _filteredSpecsView.Filter = SpecFilterPredicate;

            HookSpecCatalogNotifications();

            ItemRows.CollectionChanged += OnItemRowsChanged;

            AddRowCommand = new RelayCommand(_ => AddNewRow());

            RemoveRowCommand = new RelayCommand(p =>
            {
                if (p is not GlassItemRow r) return;
                // Keep one empty workspace row so the grid never collapses.
                if (ItemRows.Count <= 1)
                {
                    r.GlassRef = "";
                    r.W1 = 0; r.H1 = 0; r.W2 = 0; r.H2 = 0;
                    r.Qty = 1;
                    return;
                }
                r.PropertyChanged -= OnRowChanged;
                ItemRows.Remove(r);
            });

            PasteExcelCommand = new RelayCommand(
                _ => _ = PasteFromClipboardAsync());

            GenerateRowsCommand = new RelayCommand(
                _ => ExecuteGenerateRows());

            OpenFindCommand = new RelayCommand(
                _ => ExecuteOpenFind());

            OpenFindReplaceCommand = new RelayCommand(
                _ => ExecuteOpenFindReplace());
        }
    }
}
