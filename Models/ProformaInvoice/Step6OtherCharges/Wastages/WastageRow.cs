using ProGlassAutomation.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges.Wastages
{
    public class WastageRow : ViewModelBase
    {
        private const decimal BaselineOptimizationPercent = 85m;

        public Guid Id { get; } = Guid.NewGuid();

        private bool _isActive = true;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                OnPropertyChanged();
                Recalculate();
            }
        }

        // ── Colour identity ─────────────────────────────────────────────
        //
        // The row colour is OPERATOR-OWNED once it is typed, picked from
        // the detected list, restored from a snapshot or assigned by the
        // advanced engine. Until then the row auto-imports the colour of
        // its checked panes, so a freshly added row never stays nameless
        // when the specification already knows the glass colours.
        //
        //   _colorManuallyEdited   — an explicit value was assigned.
        //   _lastAutoImportedColor — the value the auto-import wrote last;
        //                            auto-import may replace it while it is
        //                            still the visible value (pane retargets).
        //   _isApplyingDetectedColor — re-entrancy guard for the setter.
        //
        private bool _colorManuallyEdited;
        private string _lastAutoImportedColor = "";
        private bool _isApplyingDetectedColor;

        private string _colorName = "";
        public string ColorName
        {
            get => _colorName;
            set
            {
                if (_colorName == value) return;
                _colorName = value;

                if (!_isApplyingDetectedColor)
                {
                    _colorManuallyEdited = !string.IsNullOrWhiteSpace(value);
                    // A deliberate clear hands the row back to auto-import.
                    if (!_colorManuallyEdited)
                        _lastAutoImportedColor = "";
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(FormulaDisplay));
                PropagateRowColorToSources();
                ResyncColourPickerSelection();

                // A deliberate clear (or any external colour write) must
                // reconcile with the checked panes right away: blanking
                // the box hands the row back to auto-import immediately
                // instead of waiting for the next source edit. Guarded
                // against the auto-import's own writes.
                if (!_isApplyingDetectedColor)
                    SyncColourStateFromSources();
            }
        }

        /// <summary>
        /// True when the operator attached this row by hand (the + ADD ROW
        /// command) instead of the advanced engine generating it. Advanced
        /// refreshes rebuild generated rows on every Step 5 edit; a manual
        /// row must NEVER be silently destroyed — only its source list is
        /// re-synced. Persisted in the invoice snapshot.
        /// </summary>
        private bool _isManuallyAdded;
        public bool IsManuallyAdded
        {
            get => _isManuallyAdded;
            set
            {
                if (_isManuallyAdded == value) return;
                _isManuallyAdded = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Every distinct non-blank pane colour detected across all spec
        /// groups, in spec/side order. Drives the detected-colour picker so
        /// the operator never has to type a colour the system already knows.
        /// </summary>
        public ObservableCollection<string> AvailableColors { get; } = new();

        private bool _hasAvailableColors;
        public bool HasAvailableColors
        {
            get => _hasAvailableColors;
            private set
            {
                if (_hasAvailableColors == value) return;
                _hasAvailableColors = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// True when the CHECKED panes carry more than one detected colour —
        /// the row would charge one colour against another pane's SQM.
        /// Guidance only: the operator's deliberate mix is never blocked.
        /// </summary>
        private bool _hasColorConflict;
        public bool HasColorConflict
        {
            get => _hasColorConflict;
            private set
            {
                if (_hasColorConflict == value) return;
                _hasColorConflict = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// The colour picked from <see cref="AvailableColors"/>. Picking one
        /// is the fast path for two-spec bills: it names the row and reticks
        /// the source panes to exactly the panes of that colour — Spec 1
        /// W1/H1 or W2/H2, Spec 2 W1/H1 or W2/H2 — while panes detected in a
        /// different colour are unticked so they cannot be charged by
        /// accident. Colourless panes are left to the operator.
        /// </summary>
        private string? _selectedAvailableColor;
        public string? SelectedAvailableColor
        {
            get => _selectedAvailableColor;
            set
            {
                if (string.Equals(_selectedAvailableColor, value, StringComparison.Ordinal))
                    return;
                _selectedAvailableColor = value;
                OnPropertyChanged();
                if (!string.IsNullOrWhiteSpace(value))
                    ApplyColourSelection(value.Trim());
            }
        }

        private void ApplyColourSelection(string colour)
        {
            ColorName = colour;

            foreach (var src in AvailableSources)
            {
                string srcColour = (src.ColorName ?? string.Empty).Trim();
                if (srcColour.Length == 0)
                    continue; // Colourless panes: operator decides per pane.

                bool matches = string.Equals(
                    srcColour, colour, StringComparison.OrdinalIgnoreCase);
                if (src.IsChecked != matches)
                    src.IsChecked = matches;
            }
        }

        /// <summary>
        /// Auto-imports the row colour from the checked panes when the row
        /// has no colour of its own yet (or still shows last tick's
        /// auto-import). Also keeps the colour-conflict flag and the
        /// detected-colour list current. Runs on every source change.
        /// </summary>
        private void SyncColourStateFromSources()
        {
            var checkedColours = AvailableSources
                .Where(s => s.IsChecked)
                .Select(s => (s.ColorName ?? string.Empty).Trim())
                .Where(c => c.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            HasColorConflict = checkedColours.Count > 1;

            string current = (_colorName ?? string.Empty).Trim();
            bool mayAutoImport = current.Length == 0
                || (!_colorManuallyEdited
                    && string.Equals(current, _lastAutoImportedColor,
                        StringComparison.OrdinalIgnoreCase));
            if (!mayAutoImport)
                return;

            if (checkedColours.Count == 1)
            {
                if (!string.Equals(current, checkedColours[0],
                        StringComparison.OrdinalIgnoreCase))
                    SetDetectedColour(checkedColours[0]);
            }
            else if (current.Length > 0
                && string.Equals(current, _lastAutoImportedColor,
                    StringComparison.OrdinalIgnoreCase))
            {
                // The selection became ambiguous (or lost all identity):
                // the auto-imported label no longer describes it — withdraw
                // the label so the row cannot charge one colour under
                // another colour's name. Only auto values are withdrawn;
                // an operator-owned colour is never touched.
                ClearDetectedColour();
            }
        }

        private void SetDetectedColour(string colour)
        {
            _lastAutoImportedColor = colour;
            _isApplyingDetectedColor = true;
            try
            {
                ColorName = colour;
            }
            finally
            {
                _isApplyingDetectedColor = false;
            }
        }

        private void ClearDetectedColour()
        {
            _lastAutoImportedColor = "";
            _isApplyingDetectedColor = true;
            try
            {
                ColorName = "";
            }
            finally
            {
                _isApplyingDetectedColor = false;
            }
        }

        /// <summary>
        /// Rebuilds the detected-colour list from ALL pane sources (checked
        /// or not) without churning the collection when nothing changed —
        /// an open colour picker must not collapse under the operator.
        /// </summary>
        private void RefreshAvailableColors()
        {
            var colours = new List<string>();
            foreach (var src in AvailableSources)
            {
                string colour = (src.ColorName ?? string.Empty).Trim();
                if (colour.Length == 0) continue;
                if (!colours.Contains(colour, StringComparer.OrdinalIgnoreCase))
                    colours.Add(colour);
            }

            bool unchanged = colours.SequenceEqual(
                AvailableColors, StringComparer.Ordinal);
            if (unchanged)
                return;

            AvailableColors.Clear();
            foreach (var colour in colours)
                AvailableColors.Add(colour);
            HasAvailableColors = colours.Count > 0;

            ResyncColourPickerSelection();
        }

        /// <summary>
        /// Keeps the detected-colour picker honest: when the row colour is
        /// one of the detected colours it is shown as the selection (the
        /// picker doubles as a colour indicator); a custom/typed colour
        /// clears the pick instead of leaving a stale one. The row colour
        /// itself is never modified here.
        /// </summary>
        private void ResyncColourPickerSelection()
        {
            string currentRowColour = (_colorName ?? string.Empty).Trim();
            string? colourMatch = currentRowColour.Length == 0
                ? null
                : AvailableColors.FirstOrDefault(c => string.Equals(
                    c, currentRowColour, StringComparison.OrdinalIgnoreCase));

            if (colourMatch != null)
            {
                if (!string.Equals(_selectedAvailableColor, colourMatch,
                        StringComparison.Ordinal))
                {
                    _selectedAvailableColor = colourMatch;
                    OnPropertyChanged(nameof(SelectedAvailableColor));
                }
            }
            else if (!string.IsNullOrWhiteSpace(_selectedAvailableColor))
            {
                _selectedAvailableColor = null;
                OnPropertyChanged(nameof(SelectedAvailableColor));
            }
        }

        /// <summary>
        /// Pushes the row colour into every source so the picker can draw
        /// match / mismatch badges (SpecGroupColorSide.MatchesRowColor).
        /// </summary>
        private void PropagateRowColorToSources()
        {
            foreach (var src in AvailableSources)
            {
                if (!string.Equals(src.RowColorName, _colorName, StringComparison.Ordinal))
                    src.RowColorName = _colorName ?? string.Empty;
            }
        }

        private int _sheetQuantity = 1;
        public int SheetQuantity
        {
            get => _sheetQuantity;
            set
            {
                if (value < 1) value = 1;
                if (_sheetQuantity == value) return;
                _sheetQuantity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SheetQuantityText));
                Recalculate();
            }
        }

        public string SheetQuantityText
        {
            get => _sheetQuantity.ToString();
            set
            {
                if (int.TryParse(value, out int parsed) && parsed > 0)
                {
                    bool changed = parsed != _sheetQuantity;
                    SheetQuantity = parsed;

                    // 2026-09-10: raised ONLY for a real operator edit of
                    // the SHEET QTY box (the TextBox binds this property).
                    // Programmatic flows (restore, suggestions) assign
                    // SheetQuantity directly and never pass through here,
                    // so saved invoices can never double-consume stock.
                    if (changed)
                        SheetQuantityCommittedByUser?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Fired when the OPERATOR commits a new SHEET QTY. Step 6 uses it
        /// to auto-consume the sheets from Sheet Store inventory.
        /// </summary>
        public event EventHandler? SheetQuantityCommittedByUser;

        // ═══ Inventory link status (auto-use feedback) ═══

        private string _inventoryStatusText = "";
        /// <summary>One-line result of the last inventory auto-use attempt.</summary>
        public string InventoryStatusText
        {
            get => _inventoryStatusText;
            set
            {
                if (_inventoryStatusText == value) return;
                _inventoryStatusText = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasInventoryStatus));
            }
        }

        private bool _isInventoryWarning;
        /// <summary>True when the status is a problem (missing/insufficient stock).</summary>
        public bool IsInventoryWarning
        {
            get => _isInventoryWarning;
            set
            {
                if (_isInventoryWarning == value) return;
                _isInventoryWarning = value;
                OnPropertyChanged();
            }
        }

        public bool HasInventoryStatus => !string.IsNullOrWhiteSpace(InventoryStatusText);

        public ObservableCollection<SpecGroupColorSide> AvailableSources { get; } = new();

        public string SourceSummaryText
        {
            get
            {
                var checkedSources = AvailableSources.Where(s => s.IsChecked).ToList();
                if (checkedSources.Count == 0)
                    return "Select sources";

                if (checkedSources.Count == 1)
                {
                    var s = checkedSources[0];
                    return $"{s.ShortLabel} · {s.SqmValue:0.000} sqm";
                }

                // Keep a short semantic summary for the common two-pane
                // DGU case. It lets the closed picker still tell the operator
                // exactly which outer/inner colours are selected.
                if (checkedSources.Count <= 2)
                    return $"{string.Join(" + ", checkedSources.Select(s => s.ShortLabel))} · {TotalSqm:0.000} sqm";

                return $"{checkedSources.Count} sources · {TotalSqm:0.000} sqm";
            }
        }

        /// <summary>
        /// The pane side this wastage charge belongs to, shown beside the
        /// colour so a DGU's OUTER and INNER rows stay distinguishable even
        /// when both panes share the same colour.
        /// </summary>
        public string SideDisplay
        {
            get
            {
                var first = AvailableSources.FirstOrDefault(s => s.IsChecked);
                if (first == null) return "";

                string side = first.SideLabel ?? "";
                if (side.IndexOf("Lam Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Lam Inner";
                if (side.IndexOf("Lam Outer", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Lam Outer";
                if (side.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Inner";
                return "Outer";
            }
        }

        private decimal _totalSqm;
        public decimal TotalSqm
        {
            get => _totalSqm;
            private set
            {
                if (_totalSqm == value) return;
                _totalSqm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ChargeableSqm));
                OnPropertyChanged(nameof(SelectedSourceSqm));
                Recalculate();
            }
        }

        public decimal ChargeableSqm => TotalSqm;
        public decimal SelectedSourceSqm => ChargeableSqm;
        public decimal UtilizedSqm => BaseResultValue;

        private decimal _selectedSourcePricePerSqm;
        public decimal SelectedSourcePricePerSqm
        {
            get => _selectedSourcePricePerSqm;
            private set
            {
                if (_selectedSourcePricePerSqm == value) return;
                _selectedSourcePricePerSqm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedSourcePriceDisplay));
            }
        }

        public string SelectedSourcePriceDisplay =>
            SelectedSourcePricePerSqm > 0m ? SelectedSourcePricePerSqm.ToString("0.####", CultureInfo.InvariantCulture) : "";

        private int _selectedSheetWidthMm;
        public int SelectedSheetWidthMm
        {
            get => _selectedSheetWidthMm;
            private set
            {
                if (_selectedSheetWidthMm == value) return;
                _selectedSheetWidthMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedSheetWidthText));
                OnPropertyChanged(nameof(SelectedSheetSummary));
            }
        }

        private int _selectedSheetHeightMm;
        public int SelectedSheetHeightMm
        {
            get => _selectedSheetHeightMm;
            private set
            {
                if (_selectedSheetHeightMm == value) return;
                _selectedSheetHeightMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedSheetHeightText));
                OnPropertyChanged(nameof(SelectedSheetSummary));
            }
        }

        private bool _hasMixedSelectedSheets;
        public bool HasMixedSelectedSheets
        {
            get => _hasMixedSelectedSheets;
            private set
            {
                if (_hasMixedSelectedSheets == value) return;
                _hasMixedSelectedSheets = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedSheetWidthText));
                OnPropertyChanged(nameof(SelectedSheetHeightText));
                OnPropertyChanged(nameof(SelectedSheetSummary));
            }
        }

        public string SelectedSheetWidthText =>
            HasMixedSelectedSheets ? "Mixed" : (SelectedSheetWidthMm > 0 ? SelectedSheetWidthMm.ToString(CultureInfo.InvariantCulture) : "");

        public string SelectedSheetHeightText =>
            HasMixedSelectedSheets ? "Mixed" : (SelectedSheetHeightMm > 0 ? SelectedSheetHeightMm.ToString(CultureInfo.InvariantCulture) : "");

        public string SelectedSheetSummary
        {
            get
            {
                if (HasMixedSelectedSheets) return "Mixed selected sheets";
                if (SelectedSheetWidthMm > 0 && SelectedSheetHeightMm > 0)
                    return $"{SelectedSheetWidthMm} × {SelectedSheetHeightMm}";
                return "";
            }
        }

        private decimal _rate;
        private decimal _manualRate;
        private bool _isApplyingCalculatedRate;

        public decimal Rate
        {
            get => _rate;
            set
            {
                if (_rate == value) return;
                _rate = value;
                if (!_isApplyingCalculatedRate)
                    _manualRate = value;
                OnPropertyChanged();
                Recalculate();
            }
        }

        private bool _useOptimizationFormula;
        public bool UseOptimizationFormula
        {
            get => _useOptimizationFormula;
            set
            {
                if (_useOptimizationFormula == value) return;
                _useOptimizationFormula = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsManualRateMode));
                OnPropertyChanged(nameof(IsOptimizationFormulaMode));
                Recalculate();
            }
        }

        public bool IsManualRateMode => !UseOptimizationFormula;
        public bool IsOptimizationFormulaMode => UseOptimizationFormula;

        // ════════════════════════════════════════════════════════════════
        //  SHEET QTY / UTILIZATION SOURCE — manual (default) or the
        //  optimization run.
        //
        //  This is a second, independent switch. The one above chooses how the
        //  wastage RATE is derived; this one chooses where the SHEET QTY and the
        //  optimization % come from. Manual stays the default and stays
        //  authoritative: nothing here ever writes to a manual field unless the
        //  operator turns the feed on, and turning it back off restores exactly
        //  what they had typed — the same contract the rate switch already
        //  guarantees ("Switching back to manual mode restores manual rate").
        //
        //  Primitives only on purpose. WastageRow lives in Models and must not
        //  depend on Services; the section ViewModel resolves the feed and hands
        //  the numbers over.
        // ════════════════════════════════════════════════════════════════

        private bool _useOptimizationFeed;

        // Set when the switch was moved by anyone OTHER than the automatic
        // enable below — in practice the operator clicking it in the grid. An
        // explicit choice has to survive every feed refresh and every Step 5
        // rebuild, so the default never fights the operator.
        private bool _feedSwitchTouchedByUser;

        // Guards the setter while the automatic default is being applied, so
        // enabling a row by default is not recorded as a user decision.
        private bool _applyingAutomaticFeed;

        private int _optimizationSheetQuantity;
        private decimal _optimizationUtilizationPercent;
        private bool _isSharedOptimizationRack;
        private string _optimizationFeedLabel = "";

        // Manual values parked while the feed is driving, so switching back is
        // lossless rather than leaving the run's numbers behind.
        private int _manualSheetQuantity = 1;
        private decimal _manualOptimizationPercent;

        /// <summary>Read SHEET QTY and optimization % from the optimization run
        /// instead of typing them. Off by default.</summary>
        public bool UseOptimizationFeed
        {
            get => _useOptimizationFeed;
            set
            {
                if (_useOptimizationFeed == value) return;
                _useOptimizationFeed = value;

                if (!_applyingAutomaticFeed) _feedSwitchTouchedByUser = true;

                // Park the operator's numbers ONCE, on the way in. This has to
                // happen here and not in ApplySheetSourceMode: the feed is
                // re-resolved every time a pane source is ticked, and parking on
                // each application would capture the PREVIOUS RUN'S number as if
                // the operator had typed it, so switching back to manual would
                // restore 17 instead of the 5 they actually entered.
                if (value)
                {
                    _manualSheetQuantity = _sheetQuantity;
                    _manualOptimizationPercent = _optimizationPercent;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsOptimizationFeedMode));
                OnPropertyChanged(nameof(IsManualSheetSourceMode));
                ApplySheetSourceMode();
            }
        }

        /// <summary>True once the operator has moved the feed switch themselves.
        /// Carried across Step 5 rebuilds so an explicit OFF is not undone by the
        /// next refresh. Deliberately not persisted: it describes this editing
        /// session, and a reopened quote re-derives its default from whatever run
        /// is available for it.</summary>
        public bool FeedSwitchTouchedByUser
        {
            get => _feedSwitchTouchedByUser;
            set => _feedSwitchTouchedByUser = value;
        }

        /// <summary>
        /// Turns the feed on for a row the run actually resolved, so real figures
        /// are read without the operator having to flip a per-row switch first.
        /// </summary>
        /// <remarks>
        /// Refuses once the operator has moved the switch themselves — an
        /// explicit OFF must survive every refresh and every rebuild.
        ///
        /// Goes through the property setter, never the field: parking happens on
        /// the false→true transition only, so writing the field directly would
        /// leave <c>_manualSheetQuantity</c> at its initializer and a later
        /// switch back to manual would restore 1 instead of the operator's own
        /// number. Callers must therefore have put the row's real sheet quantity
        /// in place BEFORE calling this.
        /// </remarks>
        /// <returns>True when the row is reading the feed afterwards.</returns>
        public bool TryEnableFeedAutomatically()
        {
            if (_feedSwitchTouchedByUser) return _useOptimizationFeed;
            if (_useOptimizationFeed) return true;

            _applyingAutomaticFeed = true;
            try { UseOptimizationFeed = true; }
            finally { _applyingAutomaticFeed = false; }

            return _useOptimizationFeed;
        }

        public bool IsOptimizationFeedMode => _useOptimizationFeed;
        public bool IsManualSheetSourceMode => !_useOptimizationFeed;

        /// <summary>
        /// Carries the parked manual numbers from the row this one replaces.
        /// </summary>
        /// <remarks>
        /// While the feed drives a row, <c>SheetQuantity</c> and
        /// <c>OptimizationPercent</c> hold the RUN's figures — so a rebuild that
        /// copies those properties and then turns the feed back on parks the run's
        /// own number as though the operator had typed it. Switching back to
        /// manual after any Step 5 edit would then restore 17 instead of the 5
        /// they actually entered. Copying the parked pair keeps the switch
        /// lossless across the rebuild every Step 5 edit triggers.
        ///
        /// Call AFTER the row initializer: the initializer's
        /// <c>UseOptimizationFeed</c> assignment parks whatever it was given, and
        /// this has to be the last word on what is handed back.
        /// </remarks>
        public void CarryManualSnapshotFrom(WastageRow? previous)
        {
            if (previous == null) return;
            _manualSheetQuantity = previous._manualSheetQuantity;
            _manualOptimizationPercent = previous._manualOptimizationPercent;
        }

        /// <summary>Sheet count the run actually produced for this pane.</summary>
        public int OptimizationSheetQuantity
        {
            get => _optimizationSheetQuantity;
            private set { _optimizationSheetQuantity = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasOptimizationFeed)); }
        }

        /// <summary>Utilization the run actually produced. Exact even when the
        /// rack is shared, because utilization belongs to the rack.</summary>
        public decimal OptimizationUtilizationPercent
        {
            get => _optimizationUtilizationPercent;
            private set { _optimizationUtilizationPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasOptimizationFeed)); }
        }

        /// <summary>True when more than one Step 5 spec cuts from this rack, so
        /// the sheet count is a shared total and must not be reused per pane
        /// without the weighted split.</summary>
        public bool IsSharedOptimizationRack
        {
            get => _isSharedOptimizationRack;
            private set { _isSharedOptimizationRack = value; OnPropertyChanged(); }
        }

        /// <summary>Provenance, always shown — the operator must be able to see
        /// whether a number came from the run, from a shared rack, or from
        /// nowhere at all.</summary>
        public string OptimizationFeedLabel
        {
            get => _optimizationFeedLabel;
            private set { _optimizationFeedLabel = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasOptimizationFeedNote)); }
        }

        public bool HasOptimizationFeed =>
            _optimizationSheetQuantity > 0 || _optimizationUtilizationPercent > 0m;

        public bool HasOptimizationFeedNote => !string.IsNullOrWhiteSpace(_optimizationFeedLabel);

        /// <summary>
        /// Offers this pane's slice of an optimization run. Sheets &lt;= 0 means
        /// "the run has nothing for this pane", which keeps the manual numbers
        /// and says so rather than writing a zero over them.
        /// </summary>
        public void SetOptimizationFeed(
            int sheets, decimal utilizationPercent, bool isSharedRack, string label)
        {
            OptimizationSheetQuantity = sheets > 0 ? sheets : 0;
            OptimizationUtilizationPercent = utilizationPercent > 0m ? utilizationPercent : 0m;
            IsSharedOptimizationRack = isSharedRack;
            OptimizationFeedLabel = label ?? "";

            if (_useOptimizationFeed) ApplySheetSourceMode();
        }

        /// <summary>Clears any feed, returning the row to purely manual input.</summary>
        public void ClearOptimizationFeed() => SetOptimizationFeed(0, 0m, false, "");

        private void ApplySheetSourceMode()
        {
            if (_useOptimizationFeed)
            {
                if (_optimizationSheetQuantity <= 0 && _optimizationUtilizationPercent <= 0m)
                {
                    // Nothing to apply. Hand the operator's own numbers back
                    // rather than leaving a previous run's figure on screen with
                    // no run behind it — a stale 17 with an empty feed reads as
                    // though it were still measured.
                    SheetQuantity = _manualSheetQuantity;
                    OptimizationPercent = _manualOptimizationPercent;

                    OptimizationFeedLabel = string.IsNullOrWhiteSpace(_optimizationFeedLabel)
                        ? "No optimization run for this pane yet — keeping your manual values."
                        : _optimizationFeedLabel;
                    return;
                }

                // Direct field assignment, NOT SheetQuantityText /
                // OptimizationPercentText. Those setters raise
                // SheetQuantityCommittedByUser, which consumes stock; a feed
                // refresh must never be mistaken for an operator edit.
                if (_optimizationSheetQuantity > 0)
                    SheetQuantity = _optimizationSheetQuantity;

                if (_optimizationUtilizationPercent > 0m)
                    OptimizationPercent = _optimizationUtilizationPercent;
            }
            else
            {
                // Hand the operator's own numbers back, untouched.
                SheetQuantity = _manualSheetQuantity;
                OptimizationPercent = _manualOptimizationPercent;
            }
        }

        private decimal _formulaPricePerSqm;
        public decimal FormulaPricePerSqm
        {
            get => _formulaPricePerSqm;
            set
            {
                if (_formulaPricePerSqm == value) return;
                _formulaPricePerSqm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormulaPricePerSqmText));
                Recalculate();
            }
        }

        public string FormulaPricePerSqmText =>
            FormulaPricePerSqm > 0m ? FormulaPricePerSqm.ToString("0.####", CultureInfo.InvariantCulture) : "";

        private decimal _optimizationPercent;
        private string _optimizationPercentText = "";
        private bool _isSyncingOptimizationText;

        public decimal OptimizationPercent
        {
            get => _optimizationPercent;
            set
            {
                if (_optimizationPercent == value) return;
                _optimizationPercent = value;
                SyncOptimizationTextFromValue();
                OnPropertyChanged();
                Recalculate();
            }
        }

        public string OptimizationPercentText
        {
            get => _optimizationPercentText;
            set
            {
                var next = value ?? string.Empty;
                if (string.Equals(_optimizationPercentText, next, StringComparison.Ordinal))
                    return;

                _optimizationPercentText = next;
                OnPropertyChanged();

                if (_isSyncingOptimizationText) return;

                decimal parsed = ParseDecimalText(next);
                if (_optimizationPercent != parsed)
                {
                    _optimizationPercent = parsed;
                    OnPropertyChanged(nameof(OptimizationPercent));
                }

                Recalculate();
            }
        }

        private decimal _section1Value;
        public decimal Section1Value
        {
            get => _section1Value;
            private set { if (_section1Value == value) return; _section1Value = value; OnPropertyChanged(); }
        }

        private decimal _section2Value;
        public decimal Section2Value
        {
            get => _section2Value;
            private set { if (_section2Value == value) return; _section2Value = value; OnPropertyChanged(); }
        }

        private decimal _baseResultValue;
        public decimal BaseResultValue
        {
            get => _baseResultValue;
            private set
            {
                if (_baseResultValue == value) return;
                _baseResultValue = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UtilizedSqm));
            }
        }

        private decimal _finalResultValue;
        public decimal FinalResultValue
        {
            get => _finalResultValue;
            private set { if (_finalResultValue == value) return; _finalResultValue = value; OnPropertyChanged(); }
        }

        public string OptimizationSummaryText
        {
            get
            {
                if (!UseOptimizationFormula)
                    return "Manual rate mode — selected SQM × entered rate.";

                if (SelectedSourceSqm <= 0m)
                    return "Optimization formula — select wastage sources first so the wastage SQM can be calculated.";

                if (FormulaPricePerSqm <= 0m)
                    return "Optimization formula — sheet price could not be resolved from Step 4 for the selected source(s).";

                if (OptimizationPercent <= 0m)
                    return "Optimization formula — enter optimization % (decimals allowed, e.g. 15.60).";

                return $"{FormulaPricePerSqm:0.####} ÷ {OptimizationPercent:0.##}% = {Section1Value:N2}  ·  {FormulaPricePerSqm:0.####} ÷ {BaselineOptimizationPercent:0.##}% = {Section2Value:N2}  ·  Utilized SQM: {UtilizedSqm:N2}";
            }
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
                if (!IsActive) return "DISABLED";

                int count = AvailableSources.Count(s => s.IsChecked);
                string colorSuffix = string.IsNullOrWhiteSpace(ColorName)
                    ? ""
                    : $" ({ColorName.ToUpper()} — {count} sources)";

                if (UseOptimizationFormula)
                {
                    if (FormulaPricePerSqm <= 0m || OptimizationPercent <= 0m || SelectedSourceSqm <= 0m)
                        return $"Optimization formula pending input{colorSuffix}";

                    return $"{FormulaPricePerSqm:0.####} ÷ {OptimizationPercent:0.##}% = {Section1Value:N2}  ·  {FormulaPricePerSqm:0.####} ÷ {BaselineOptimizationPercent:0.##}% = {Section2Value:N2}  ·  RATE {BaseResultValue:N2} × {SelectedSourceSqm:0.000} SQM = {ResultAmount:N2} AED{colorSuffix}";
                }

                return $"{TotalSqm:0.000} SQM × {Rate:0.##} AED = {ResultAmount:N2} AED{colorSuffix}";
            }
        }

        public WastageRow()
        {
            AvailableSources.CollectionChanged += OnSourcesCollectionChanged;
        }

        private void OnSourcesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (SpecGroupColorSide item in e.NewItems)
                {
                    // Colour badges must be right from the first render —
                    // before any later PropertyChanged arrives.
                    item.RowColorName = _colorName ?? string.Empty;
                    item.PropertyChanged += OnSourceChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (SpecGroupColorSide item in e.OldItems)
                    item.PropertyChanged -= OnSourceChanged;
            }

            RecalcFromSources();
            OnPropertyChanged(nameof(SideDisplay));
        }

        private void OnSourceChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SpecGroupColorSide.IsChecked)
                or nameof(SpecGroupColorSide.SqmValue)
                or nameof(SpecGroupColorSide.PricePerSqm)
                or nameof(SpecGroupColorSide.SheetWidthMm)
                or nameof(SpecGroupColorSide.SheetHeightMm)
                or nameof(SpecGroupColorSide.SpecNumber)
                or nameof(SpecGroupColorSide.SpecName)
                or nameof(SpecGroupColorSide.SideLabel)
                or nameof(SpecGroupColorSide.ColorName)
                or null
                or "")
            {
                RecalcFromSources();
                OnPropertyChanged(nameof(SideDisplay));
            }
        }

        private void Recalculate()
        {
            if (UseOptimizationFormula)
            {
                RecalculateOptimizationResults();
                ResultAmount = IsActive ? MoneyRounding.Round(FinalResultValue) : 0m;
            }
            else
            {
                ResetOptimizationResults();
                RestoreManualRateDisplay();
                ResultAmount = IsActive ? Math.Round(TotalSqm * Rate, 2) : 0m;
            }

            OnPropertyChanged(nameof(FormulaDisplay));
            OnPropertyChanged(nameof(SourceSummaryText));
            OnPropertyChanged(nameof(OptimizationSummaryText));
        }

        public void ForceRefreshSqm() => RecalcFromSources();

        private void RecalcFromSources()
        {
            TotalSqm = AvailableSources.Where(s => s.IsChecked).Sum(s => s.SqmValue);
            RefreshAvailableColors();
            SyncColourStateFromSources();
            PropagateRowColorToSources();
            RefreshSelectedSourceDefaults();
            OnPropertyChanged(nameof(FormulaDisplay));
            OnPropertyChanged(nameof(SourceSummaryText));
            OnPropertyChanged(nameof(OptimizationSummaryText));
        }

        private void RefreshSelectedSourceDefaults()
        {
            var checkedSources = AvailableSources
                .Where(s => s.IsChecked)
                .ToList();

            if (checkedSources.Count == 0)
            {
                HasMixedSelectedSheets = false;
                SelectedSheetWidthMm = 0;
                SelectedSheetHeightMm = 0;
                SelectedSourcePricePerSqm = 0m;
                SetAutoFormulaPrice(0m);
                return;
            }

            // Senior ERP hardening: Step 6 must display the exact Step 4 pane
            // box price for the selected wastage source. Never weighted-average
            // different panes (52 Low-E + 25 Clear became 50.4486), and never
            // let a name containing "Clear" borrow the generic Clear rate.
            // If a row has a colour identity, use only checked sources with an
            // exact colour match for the price/sheet default display.
            var priceSources = PricingSourcesForRow(checkedSources);

            var uniqueSizes = priceSources
                .Where(s => s.SheetWidthMm > 0 && s.SheetHeightMm > 0)
                .Select(s => (s.SheetWidthMm, s.SheetHeightMm))
                .Distinct()
                .ToList();

            HasMixedSelectedSheets = uniqueSizes.Count > 1;
            if (uniqueSizes.Count == 1)
            {
                SelectedSheetWidthMm = uniqueSizes[0].SheetWidthMm;
                SelectedSheetHeightMm = uniqueSizes[0].SheetHeightMm;
            }
            else
            {
                SelectedSheetWidthMm = 0;
                SelectedSheetHeightMm = 0;
            }

            var uniquePrices = priceSources
                .Where(s => s.PricePerSqm > 0m)
                .Select(s => s.PricePerSqm)
                .Distinct()
                .ToList();

            decimal autoPrice = uniquePrices.Count == 1
                ? uniquePrices[0]
                : 0m;

            SelectedSourcePricePerSqm = autoPrice;
            SetAutoFormulaPrice(autoPrice);
        }

        private List<SpecGroupColorSide> PricingSourcesForRow(List<SpecGroupColorSide> checkedSources)
        {
            string rowColour = (ColorName ?? string.Empty).Trim();
            if (rowColour.Length == 0)
                return checkedSources;

            var exactColourSources = checkedSources
                .Where(s => string.Equals(
                    (s.ColorName ?? string.Empty).Trim(),
                    rowColour,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            return exactColourSources.Count > 0 ? exactColourSources : checkedSources;
        }

        private void RecalculateOptimizationResults()
        {
            if (FormulaPricePerSqm <= 0m || OptimizationPercent <= 0m || SelectedSourceSqm <= 0m)
            {
                Section1Value = 0m;
                Section2Value = 0m;
                BaseResultValue = 0m;
                FinalResultValue = 0m;
                SetCalculatedRate(0m);
                return;
            }

            decimal optimizationFactor = OptimizationPercent / 100m;
            if (optimizationFactor == 0m)
            {
                Section1Value = 0m;
                Section2Value = 0m;
                BaseResultValue = 0m;
                FinalResultValue = 0m;
                SetCalculatedRate(0m);
                return;
            }

            Section1Value = Math.Round(FormulaPricePerSqm / optimizationFactor, 6);
            Section2Value = Math.Round(FormulaPricePerSqm / (BaselineOptimizationPercent / 100m), 6);
            BaseResultValue = Math.Round(Section1Value - Section2Value, 6);
            FinalResultValue = Math.Round(BaseResultValue * SelectedSourceSqm, 6);
            SetCalculatedRate(BaseResultValue);
        }

        private void ResetOptimizationResults()
        {
            Section1Value = 0m;
            Section2Value = 0m;
            BaseResultValue = 0m;
            FinalResultValue = 0m;
        }

        private void RestoreManualRateDisplay()
        {
            if (Rate == _manualRate) return;

            _isApplyingCalculatedRate = true;
            try
            {
                _rate = _manualRate;
                OnPropertyChanged(nameof(Rate));
            }
            finally
            {
                _isApplyingCalculatedRate = false;
            }
        }

        private void SetCalculatedRate(decimal value)
        {
            if (Rate == value) return;

            _isApplyingCalculatedRate = true;
            try
            {
                _rate = value;
                OnPropertyChanged(nameof(Rate));
            }
            finally
            {
                _isApplyingCalculatedRate = false;
            }
        }

        private void SetAutoFormulaPrice(decimal value)
        {
            if (FormulaPricePerSqm == value) return;
            _formulaPricePerSqm = value;
            OnPropertyChanged(nameof(FormulaPricePerSqm));
            OnPropertyChanged(nameof(FormulaPricePerSqmText));
            Recalculate();
        }

        private void SyncOptimizationTextFromValue()
        {
            string next = _optimizationPercent > 0m
                ? _optimizationPercent.ToString("0.####", CultureInfo.InvariantCulture)
                : string.Empty;

            if (string.Equals(_optimizationPercentText, next, StringComparison.Ordinal))
                return;

            _isSyncingOptimizationText = true;
            try
            {
                _optimizationPercentText = next;
                OnPropertyChanged(nameof(OptimizationPercentText));
            }
            finally
            {
                _isSyncingOptimizationText = false;
            }
        }

        private static decimal ParseDecimalText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0m;

            string normalized = text.Trim().Replace(',', '.');
            return decimal.TryParse(
                normalized,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out decimal result)
                ? result
                : 0m;
        }
    }
}
