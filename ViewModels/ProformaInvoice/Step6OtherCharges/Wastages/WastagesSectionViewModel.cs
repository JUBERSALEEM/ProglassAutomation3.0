using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges.Wastages;
using ProGlassAutomation.Services.Optimization;
using ProGlassAutomation.Services.ProformaInvoice;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges.Wastages
{
    public class WastagesSectionViewModel : ViewModelBase
    {
        private readonly DataGridViewModel _dataGrid;
        private Func<IReadOnlyList<Step4PricingSnapshot>>? _step4SectionsProvider;

        // Late-bound for the same reason the Step 4 provider is: Step 6 must not
        // depend on the optimization module being constructed, and the formula
        // tests bind nothing at all.
        private Func<IReadOnlyList<OptimizationPaneResult>>? _optimizationFeedProvider;

        // ── 2026-09-10: automatic inventory consumption ─────────────────
        // Entering SHEET QTY on a wastage row consumes that many sheets
        // from Sheet Store inventory (resolve colour + size the row's
        // sources carry → UseStockAsync + history + audit through the
        // SHARED SheetStockLinkService core). The store module is NEVER
        // opened. Utilization stays with Step 9 (Step6SheetAllocation).
        private Func<(string Reference, string Customer, string Project, string Salesman)>? _jobContextProvider;
        // Concurrent: Add happens on the UI thread, Remove on a threadpool
        // continuation when the async consumption finishes.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, byte> _autoUseInFlight = new();

        // NET sheets already consumed per row THIS SESSION. Committing a
        // quantity consumes the DELTA against this number, so correcting a
        // typo (5 → 3) or typing through intermediate keystrokes can never
        // over-consume. Cleared when a row is removed / all rows cleared;
        // a negative delta is NOT auto-reversed (no silent inventory
        // mutation) — the operator gets an explicit warning instead.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, int> _consumedNetPerRow = new();

        /// <summary>Master switch for auto-consuming sheets from inventory. Default ON.</summary>
        public bool AutoUseFromInventory
        {
            get => _autoUseFromInventory;
            set
            {
                if (_autoUseFromInventory == value) return;
                _autoUseFromInventory = value;
                OnPropertyChanged();
            }
        }
        private bool _autoUseFromInventory = true;

        public ObservableCollection<WastageRow> WastageRows { get; } = new();

        private decimal _totalWastageValue;
        public decimal TotalWastageValue
        {
            get => _totalWastageValue;
            private set
            {
                if (_totalWastageValue == value) return;
                _totalWastageValue = value;
                OnPropertyChanged();
            }
        }

        public ICommand AddWastageCommand { get; }
        public ICommand RemoveWastageCommand { get; }

        /// <summary>
        /// Job context for inventory history entries (customer / project /
        /// salesman / reference), supplied by ProformaInvoiceViewModel so
        /// the consumption ledger reads like the manual Use dialog's.
        /// </summary>
        public void BindJobContext(
            Func<(string Reference, string Customer, string Project, string Salesman)>? provider)
        {
            _jobContextProvider = provider;
        }

        /// <summary>
        /// Supplies the finished optimization run so SHEET QTY and the
        /// optimization % can be read instead of typed. Optional — with no
        /// provider bound every row stays purely manual, which is the default.
        /// </summary>
        public void BindOptimizationFeedProvider(
            Func<IReadOnlyList<OptimizationPaneResult>> provider)
        {
            _optimizationFeedProvider = provider;
            RefreshOptimizationFeed();
        }

        /// <summary>Re-offers the run figures to every row. Called after a run
        /// finishes, after a source is ticked, and after a feed refresh.</summary>
        public void RefreshOptimizationFeed()
        {
            foreach (var row in WastageRows) ApplyOptimizationFeedToRow(row);
        }

        /// <summary>
        /// Resolves one wastage row against the optimization run.
        ///
        /// A row may intentionally combine several panes, so this works on the
        /// DISTINCT RACKS those panes come from: two panes sharing one sheet are
        /// counted once, never twice. Utilization is the rack's own when there is
        /// one rack, and area-weighted (glass m² / stock m²) when there are
        /// several — the same formula the optimization module uses for its own
        /// headline utilization, so the two figures can never disagree.
        /// </summary>
        private void ApplyOptimizationFeedToRow(WastageRow row)
        {
            if (row == null) return;

            var feed = _optimizationFeedProvider?.Invoke();
            if (feed == null || feed.Count == 0)
            {
                row.SetOptimizationFeed(0, 0m, false,
                    "No optimization run yet — press QUICK or OPTIMIZE on the Step 5 top panel (or run a cut on the OPTIMIZATION tab) to read real sheet qty and utilization.");
                return;
            }

            var sources = row.AvailableSources
                .Where(s => s != null && s.IsChecked)
                .ToList();

            if (sources.Count == 0)
            {
                row.SetOptimizationFeed(0, 0m, false,
                    "Tick a pane source to read its sheet qty and utilization from the optimization run.");
                return;
            }

            var racks = new List<OptimizationPaneResult>();
            var seen = new HashSet<(int W, int H, string C)>();
            int unmatched = 0;

            foreach (var src in sources)
            {
                // SideLabel, not SemanticSideLabel: the coordinate key W1/H1 vs
                // W2/H2 is what identifies the rack, and SemanticSideLabel folds
                // "Middle" into "Outer" by fall-through.
                var hit = OptimizationResultFeed.Find(
                    feed, src.SpecNumber, src.SideLabel, src.SheetWidthMm, src.SheetHeightMm);

                if (hit == null) { unmatched++; continue; }

                var key = (hit.SheetWidthMm, hit.SheetHeightMm,
                    (hit.Composition ?? string.Empty).Trim().ToLowerInvariant());

                if (seen.Add(key)) racks.Add(hit);
            }

            if (racks.Count == 0)
            {
                row.SetOptimizationFeed(0, 0m, false,
                    "The optimization run has no row for this pane — re-run it after importing the Step 4 sheets.");
                return;
            }

            int sheets = racks.Sum(r => r.SheetQuantity);

            decimal utilization;
            if (racks.Count == 1)
            {
                utilization = racks[0].UtilizationPercent;
            }
            else
            {
                decimal stock = racks.Sum(r => r.StockSqm);
                decimal glass = racks.Sum(r => r.GlassSqm);
                // Percent, not money — stays on Math.Round so MoneyRounding
                // is not applied to a utilization figure.
                utilization = stock > 0m
                    ? Math.Min(100m, Math.Round(glass / stock * 100m, 2))
                    : 0m;
            }

            bool shared = false;
            foreach (var r in racks) if (r.IsSharedAcrossSpecs) { shared = true; break; }

            string label = racks.Count == 1
                ? sheets.ToString(CultureInfo.InvariantCulture) + " sheet(s) · " +
                  utilization.ToString("0.##", CultureInfo.InvariantCulture) + "% from the optimization run"
                : sheets.ToString(CultureInfo.InvariantCulture) + " sheet(s) across " +
                  racks.Count.ToString(CultureInfo.InvariantCulture) + " racks · " +
                  utilization.ToString("0.##", CultureInfo.InvariantCulture) + "% area-weighted";

            if (shared)
                label += " · rack is shared with another spec, so this is the shared total — split it, do not reuse it per pane";
            if (unmatched > 0)
                label += " · " + unmatched.ToString(CultureInfo.InvariantCulture) + " ticked pane(s) are not in the run";
            label += " · source: " + racks[0].SessionName;

            row.SetOptimizationFeed(sheets, utilization, shared, label);

            // The run really does cover this row's panes, so read it by default
            // instead of waiting for the operator to notice a per-row switch.
            // Called AFTER SetOptimizationFeed and with the row's own sheet
            // quantity already in place, because enabling parks whatever the row
            // is currently holding as the manual value to return to. A row the
            // operator has switched by hand is left exactly as they left it.
            row.TryEnableFeedAutomatically();
        }

        public void BindStep4SectionsProvider(Func<IReadOnlyList<Step4PricingSnapshot>> provider)
        {
            _step4SectionsProvider = provider;
            SyncAllWastageSources();
        }

        public WastagesSectionViewModel(DataGridViewModel dataGrid)
        {
            _dataGrid = dataGrid;
            AddWastageCommand = new RelayCommand(_ => AddWastage());
            RemoveWastageCommand = new RelayCommand(p =>
            {
                if (p is WastageRow r)
                {
                    DetachRow(r);
                    _consumedNetPerRow.TryRemove(r.Id, out _);
                    WastageRows.Remove(r);
                    RecalculateTotal();
                }
            });
        }

        private void AddWastage()
        {
            var row = new WastageRow
            {
                Rate = 0m,
                // Operator-owned from birth: advanced refreshes (which run on
                // every Step 5 edit) must never silently destroy this row.
                IsManuallyAdded = true
            };
            SyncSourcesForRow(row);
            AttachRow(row);
            WastageRows.Add(row);
        }

        public void SyncAllWastageSources()
        {
            foreach (var row in WastageRows) SyncSourcesForRow(row);
            RecalculateTotal();
        }

        /// <summary>
        /// Removes every current wastage row after unhooking its live total
        /// subscription. Used by reset/restore paths so Step 6 never keeps
        /// listening to rows from the previous invoice state.
        /// </summary>
        public void ClearWastageRows()
        {
            foreach (var row in WastageRows.ToList())
            {
                DetachRow(row);
                _consumedNetPerRow.TryRemove(row.Id, out _);
            }

            WastageRows.Clear();
            RecalculateTotal();
        }

        /// <summary>
        /// Advanced Step 6 mode: rebuild wastage rows from the glass sides the
        /// current specification set requires. Existing rates are preserved by
        /// display name so an operator's invoice-specific price survives a
        /// refresh even while the source selection is auto-managed.
        /// </summary>
        public void ApplyAdvancedSuggestions(IEnumerable<AutoWastageSuggestion> suggestions)
        {
            var materialized = suggestions?.ToList()
                ?? new List<AutoWastageSuggestion>();

            // ── MANUAL ROWS ARE NEVER REBUILT ────────────────────────────
            // This refresh runs on EVERY Step 5 keystroke (W/H/Qty edits).
            // Before this guard, ClearWastageRows() wiped rows the operator
            // added by hand seconds earlier — silent data loss. Manual rows
            // stay; only their source lists track the current spec set.
            var autoRows = WastageRows
                .Where(r => !r.IsManuallyAdded)
                .ToList();

            // A DGU build has TWO panes — outer and inner — and DGU+LAM has
            // three. Each side is its own wastage row with its own setup
            // SHEET QTY, so same-colour sides must NOT be folded into one
            // row: they were, and Step 9 could only read one quantity for
            // both panes. Rows therefore key on colour + side.
            var previousRows = autoRows
                .GroupBy(r => WastageRowKey(r.ColorName, RowSideKey(r)),
                    System.StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.First(),
                    System.StringComparer.OrdinalIgnoreCase);

            foreach (var row in autoRows)
                DetachRow(row);
            foreach (var row in autoRows)
                WastageRows.Remove(row);

            // Manual rows keep their rates and checked panes; their source
            // lists are re-synced so renamed/removed specs cannot leave
            // phantom references behind.
            foreach (var row in WastageRows)
                SyncSourcesForRow(row);

            foreach (var group in materialized
                .GroupBy(x => WastageRowKey(
                        SuggestionColour(x), x.SourceSideLabel),
                    System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key, System.StringComparer.OrdinalIgnoreCase))
            {
                previousRows.TryGetValue(group.Key, out var previousRow);

                // Colour is STRUCTURED data from the rules engine — never
                // scraped back out of the human display string. A
                // colourless suggestion must stay blank instead of
                // labelling the row "Outer glass wastage".
                string rowColour = FirstNonBlank(
                    group.First().ColorName,
                    ExtractColorKey(group.First().DisplayName));

                var row = new WastageRow
                {
                    IsActive = previousRow?.IsActive ?? true,
                    IsManuallyAdded = false,
                    ColorName = rowColour,
                    Rate = previousRow?.Rate ?? 0m,
                    UseOptimizationFormula = previousRow?.UseOptimizationFormula ?? true,
                    FormulaPricePerSqm = previousRow?.FormulaPricePerSqm ?? 0m,
                    OptimizationPercent = previousRow?.OptimizationPercent ?? 0m,
                    SheetQuantity = previousRow?.SheetQuantity ?? 1,

                    // ORDER MATTERS. Turning the feed on parks whatever
                    // SheetQuantity currently holds as the value to hand back on
                    // a switch to manual, so the restored number must be in place
                    // first — otherwise flipping the row off later restores the
                    // initializer's 1 instead of the saved quantity.
                    //
                    // The operator's own choice is carried too, so an explicit ON
                    // or OFF survives the rebuild that every Step 5 edit triggers.
                    FeedSwitchTouchedByUser = previousRow?.FeedSwitchTouchedByUser ?? false,
                    UseOptimizationFeed = previousRow?.UseOptimizationFeed ?? false
                };

                // A row the feed is driving reports the RUN's number in
                // SheetQuantity/OptimizationPercent, which is what the
                // initializer above just copied. Hand the parked manual pair
                // across explicitly, or the first Step 5 edit after a row went
                // onto the feed makes "back to manual" restore the run's 17
                // instead of the operator's own 5.
                row.CarryManualSnapshotFrom(previousRow);

                PopulateSourcesForRestoredRow(row);

                var selectedKeys = group
                    .Select(x => $"{x.SpecGroupNumber}-{x.SourceSideLabel}")
                    .ToHashSet(System.StringComparer.OrdinalIgnoreCase);

                foreach (var src in row.AvailableSources)
                {
                    string key = $"{src.SpecNumber}-{src.SideLabel}";
                    src.IsChecked = selectedKeys.Contains(key);
                }

                WastageRows.Add(row);
            }

            RecalculateTotal();
        }

        /// <summary>
        /// The colour a suggestion carries as data. Falls back to the
        /// legacy display-name scrape only for callers that predate the
        /// structured field; sentinel labels never become colours.
        /// </summary>
        private static string SuggestionColour(AutoWastageSuggestion suggestion) =>
            FirstNonBlank(suggestion.ColorName, ExtractColorKey(suggestion.DisplayName));

        /// <summary>
        /// BUG-16 support: Public entry point for the mapper.
        /// Populates AvailableSources on a freshly created wastage row
        /// during snapshot restore, WITHOUT firing UI commands.
        ///
        /// Also hooks the row's PropertyChanged event so total updates
        /// when the row becomes active or its rate changes.
        ///
        /// Called by ProformaInvoiceMapper.RestoreOtherCharges().
        /// Do NOT add the row to WastageRows here — caller does that
        /// after applying restored state (IsActive, ColorName, Rate,
        /// CheckedSourceKeys).
        /// </summary>
        public void PopulateSourcesForRestoredRow(WastageRow row)
        {
            if (row == null) return;

            // Populate available sources from current spec groups
            SyncSourcesForRow(row);
            AttachRow(row);
        }

        private void SyncSourcesForRow(WastageRow row)
        {
            var checkedIds = row.AvailableSources
                .Where(s => s.IsChecked)
                .Select(s => $"{s.SpecNumber}-{s.SideLabel}")
                .ToList();

            var step4Sections = _step4SectionsProvider?.Invoke();

            row.AvailableSources.Clear();

            foreach (var group in _dataGrid.SpecGroups)
            {
                // Resolve the build from the specification grammar first. A
                // persisted/default dropdown value can be stale (for example
                // SGU while the name clearly contains a DGU spacer).
                string effectiveType = OtherChargeAutomationRules.ResolveEffectiveGlassType(
                    group.SelectedGlassType, group.SelectedSpecName);
                var step4Snapshot = ResolveStep4Snapshot(
                    step4Sections, group, effectiveType);
                string sourceSpecName = SourceSpecName(group);

                // A double-glass build's second pane is the same unit size as
                // the first (W1/H1) unless the grid carries an explicit W2/H2.
                decimal innerSqm = group.FooterTotalSqm2 > 0m
                    ? group.FooterTotalSqm2
                    : group.FooterTotalSqm;

                if (effectiveType == "DGU+LAM")
                {
                    var paneColors = ResolveDguLamPaneColors(group, step4Snapshot);

                    AddSource(row, checkedIds, new SpecGroupColorSide
                    {
                        SpecNumber = group.GroupNumber,
                        SpecName = sourceSpecName,
                        SideLabel = "Outer (W1/H1)",
                        ColorName = paneColors.Outer,
                        SqmValue = group.FooterTotalSqm,
                        SheetWidthMm = group.OuterSheetWidthMm,
                        SheetHeightMm = group.OuterSheetHeightMm,
                        PricePerSqm = group.OuterSheetPricePerSqm
                    }, step4Snapshot, effectiveType, isInnerPane: false);

                    AddSource(row, checkedIds, new SpecGroupColorSide
                    {
                        SpecNumber = group.GroupNumber,
                        SpecName = sourceSpecName,
                        SideLabel = "Lam Outer (W2/H2)",
                        ColorName = paneColors.LamOuter,
                        SqmValue = innerSqm,
                        SheetWidthMm = group.InnerSheetWidthMm,
                        SheetHeightMm = group.InnerSheetHeightMm,
                        PricePerSqm = group.InnerSheetPricePerSqm
                    }, step4Snapshot, effectiveType, isInnerPane: true);

                    AddSource(row, checkedIds, new SpecGroupColorSide
                    {
                        SpecNumber = group.GroupNumber,
                        SpecName = sourceSpecName,
                        SideLabel = "Lam Inner (W2/H2)",
                        ColorName = paneColors.LamInner,
                        SqmValue = innerSqm,
                        SheetWidthMm = group.InnerSheetWidthMm,
                        SheetHeightMm = group.InnerSheetHeightMm,
                        PricePerSqm = group.InnerSheetPricePerSqm
                    }, step4Snapshot, effectiveType, isInnerPane: true);

                    continue;
                }

                var twoPaneColors = ResolveTwoPaneColors(
                    group, step4Snapshot, effectiveType);

                AddSource(row, checkedIds, new SpecGroupColorSide
                {
                    SpecNumber = group.GroupNumber,
                    SpecName = sourceSpecName,
                    SideLabel = "Outer (W1/H1)",
                    ColorName = twoPaneColors.Outer,
                    SqmValue = group.FooterTotalSqm,
                    SheetWidthMm = group.OuterSheetWidthMm,
                    SheetHeightMm = group.OuterSheetHeightMm,
                    PricePerSqm = group.OuterSheetPricePerSqm
                }, step4Snapshot, effectiveType, isInnerPane: false);

                // A resolved double-build offers the inner source; so does a
                // grid that physically carries a W2/H2 column even when the
                // type field is blank (manual mode must stay usable).
                if (effectiveType is "DGU" or "LAM" || group.FooterTotalSqm2 > 0m)
                {
                    var allPanes = PnLSpecParser.ResolveAllPanes(
                        group.SelectedSpecName);

                    // FIX-2026-09-05 (3+ pane builds): a triple-glazed
                    // DGU carries TWO spacers — the middle pane has its
                    // own sheet and wastage cost; the old flow read only
                    // outer+inner and the middle charge silently never
                    // existed. Sheet geometry mirrors the inner pane
                    // (identical build size W2/H2).
                    for (int m = 1; m < allPanes.Count - 1; m++)
                    {
                        var middle = allPanes[m];
                        AddSource(row, checkedIds, new SpecGroupColorSide
                        {
                            SpecNumber = group.GroupNumber,
                            SpecName = sourceSpecName,
                            SideLabel = allPanes.Count == 3
                                ? "Middle (W2/H2)"
                                : $"Middle {m} (W2/H2)",
                            ColorName = middle.Color,
                            SqmValue = innerSqm,
                            SheetWidthMm = group.InnerSheetWidthMm,
                            SheetHeightMm = group.InnerSheetHeightMm,
                            PricePerSqm = group.InnerSheetPricePerSqm
                        }, step4Snapshot, effectiveType, isInnerPane: true);
                    }

                    AddSource(row, checkedIds, new SpecGroupColorSide
                    {
                        SpecNumber = group.GroupNumber,
                        SpecName = sourceSpecName,
                        SideLabel = "Inner (W2/H2)",
                        ColorName = twoPaneColors.Inner,
                        SqmValue = innerSqm,
                        SheetWidthMm = group.InnerSheetWidthMm,
                        SheetHeightMm = group.InnerSheetHeightMm,
                        PricePerSqm = group.InnerSheetPricePerSqm
                    }, step4Snapshot, effectiveType, isInnerPane: true);
                }
            }

            // Per-spec picker headers: with two specs the popup lists four+
            // panes; "SPEC 1" / "SPEC 2" headers stop wrong-spec selections.
            StampSpecGroupHeaders(row);
            row.ForceRefreshSqm();

            // Sources changed, so the panes this row draws from changed, so the
            // run figures offered to it have to be resolved again.
            ApplyOptimizationFeedToRow(row);
        }

        private static void StampSpecGroupHeaders(WastageRow row)
        {
            SpecGroupColorSide? previous = null;
            foreach (var source in row.AvailableSources)
            {
                bool first = previous == null
                    || previous.SpecNumber != source.SpecNumber;
                source.IsFirstOfSpec = first;
                source.SpecHeaderText = first
                    ? $"SPEC {source.SpecNumber}"
                    : string.Empty;
                previous = source;
            }
        }

        private static string SourceSpecName(SpecGroup group) =>
            !string.IsNullOrWhiteSpace(group.SelectedSpecName)
                ? group.SelectedSpecName
                : group.GroupName ?? string.Empty;

        public void RefreshTotal() => RecalculateTotal();

        private static void AddSource(
            WastageRow row,
            IReadOnlyCollection<string> checkedIds,
            SpecGroupColorSide source,
            Step4PricingSnapshot? step4Snapshot,
            string effectiveGlassType,
            bool isInnerPane)
        {
            ApplyStep4SourceMetadata(
                source, step4Snapshot, effectiveGlassType);

            // AUTO-TICK: If this source was previously checked, restore it.
            // If this is a NEW row (no previous checks) and it is an inner
            // pane for a double-glass build, auto-tick it so the operator
            // does not miss inner glass wastage.
            if (checkedIds.Contains($"{source.SpecNumber}-{source.SideLabel}"))
            {
                source.IsChecked = true;
            }
            else if (checkedIds.Count == 0 && isInnerPane)
            {
                source.IsChecked = true;
            }

            row.AvailableSources.Add(source);
        }

        private static (string Outer, string Inner) ResolveTwoPaneColors(
            SpecGroup group,
            Step4PricingSnapshot? snapshot,
            string effectiveGlassType)
        {
            var glassMatches = PnLSpecParser.GlassMatches(group.SelectedSpecName);
            string parsedOuter = glassMatches.Count > 0
                ? (glassMatches[0].Groups["color"].Value ?? string.Empty).Trim()
                : string.Empty;
            string parsedInner = glassMatches.Count > 1
                ? (glassMatches[^1].Groups["color"].Value ?? string.Empty).Trim()
                : string.Empty;

            return effectiveGlassType switch
            {
                "DGU" => (
                    FirstNonBlank(parsedOuter, group.OuterGlassColor,
                        snapshot?.Dgu?.OuterGlassColor),
                    FirstNonBlank(parsedInner, group.InnerGlassColor,
                        snapshot?.Dgu?.InnerGlassColor)),
                "LAM" => (
                    FirstNonBlank(parsedOuter, group.OuterGlassColor,
                        snapshot?.Lam?.OuterGlassColor),
                    FirstNonBlank(parsedInner, group.InnerGlassColor,
                        snapshot?.Lam?.InnerGlassColor)),
                "SGU" => (
                    FirstNonBlank(parsedOuter, group.OuterGlassColor,
                        snapshot?.Sgu?.GlassColor),
                    string.Empty),
                _ => (
                    FirstNonBlank(parsedOuter, group.OuterGlassColor),
                    FirstNonBlank(parsedInner, group.InnerGlassColor))
            };
        }

        private static (string Outer, string LamOuter, string LamInner) ResolveDguLamPaneColors(
            SpecGroup group,
            Step4PricingSnapshot? snapshot)
        {
            var glassMatches = PnLSpecParser.GlassMatches(group.SelectedSpecName);
            string parsedOuter = glassMatches.Count > 0
                ? (glassMatches[0].Groups["color"].Value ?? string.Empty).Trim()
                : string.Empty;
            string parsedLamOuter = glassMatches.Count > 1
                ? (glassMatches[1].Groups["color"].Value ?? string.Empty).Trim()
                : string.Empty;
            string parsedLamInner = glassMatches.Count > 2
                ? (glassMatches[2].Groups["color"].Value ?? string.Empty).Trim()
                : string.Empty;

            return (
                FirstNonBlank(parsedOuter, snapshot?.DguLam?.OuterGlassColor),
                FirstNonBlank(parsedLamOuter, group.OuterGlassColor,
                    snapshot?.DguLam?.LamOuterColor),
                FirstNonBlank(parsedLamInner, group.InnerGlassColor,
                    snapshot?.DguLam?.LamInnerColor));
        }

        private static string FirstNonBlank(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private void AttachRow(WastageRow row)
        {
            if (row == null) return;
            row.PropertyChanged -= OnWastageRowChanged;
            row.PropertyChanged += OnWastageRowChanged;
            row.SheetQuantityCommittedByUser -= OnRowQuantityCommittedByUser;
            row.SheetQuantityCommittedByUser += OnRowQuantityCommittedByUser;
        }

        private void DetachRow(WastageRow row)
        {
            if (row == null) return;
            row.PropertyChanged -= OnWastageRowChanged;
            row.SheetQuantityCommittedByUser -= OnRowQuantityCommittedByUser;
        }

        private void OnRowQuantityCommittedByUser(object? sender, EventArgs e)
        {
            if (sender is not WastageRow row) return;
            if (!AutoUseFromInventory || !row.IsActive) return;

            // One consumption per row at a time; rapid retyping collapses
            // into the latest quantity once the current run finishes.
            if (!_autoUseInFlight.TryAdd(row.Id, 0)) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try { await AutoUseFromInventoryAsync(row).ConfigureAwait(false); }
                finally { _autoUseInFlight.TryRemove(row.Id, out _); }
            });
        }

        /// <summary>
        /// Resolves the stocked sheet for this row (colour + source sheet
        /// size) and consumes the entered quantity. Never throws; every
        /// outcome lands in the row's status line.
        /// </summary>
        private async System.Threading.Tasks.Task AutoUseFromInventoryAsync(WastageRow row)
        {
            int qty = row.SheetQuantity;
            if (qty <= 0) return;

            string color = string.IsNullOrWhiteSpace(row.SelectedAvailableColor)
                ? row.ColorName
                : row.SelectedAvailableColor;
            int w = row.SelectedSheetWidthMm;
            int h = row.SelectedSheetHeightMm;

            if (string.IsNullOrWhiteSpace(color) || w <= 0 || h <= 0)
            {
                SetRowInventoryStatus(row,
                    "Sheet size or colour is missing — check Step 4 / Step 5 sources.",
                    isWarning: true);
                return;
            }

            // DELTA commit: only the difference against what this row
            // already consumed this session is taken from inventory.
            int netAlready = _consumedNetPerRow.TryGetValue(row.Id, out var prev)
                ? prev : 0;
            int delta = qty - netAlready;

            if (delta == 0)
            {
                SetRowInventoryStatus(row,
                    $"Sheet QTY unchanged — {netAlready} sheet(s) already " +
                    "consumed from inventory for this row.",
                    isWarning: false);
                return;
            }

            if (delta < 0)
            {
                SetRowInventoryStatus(row,
                    $"Quantity reduced ({netAlready} consumed → {qty} now). " +
                    "Auto-return to inventory is not supported — adjust the " +
                    "balance in Sheet Store history if needed.",
                    isWarning: true);
                return;
            }

            // Thickness narrows the match: read the primary source's Step 4
            // section and parse the leading mm of the generated spec name.
            int thicknessMm = ResolveRowThicknessMm(row);

            var sheet = await SheetStockLinkService
                .ResolveSheetAsync(color, w, h, thicknessMm)
                .ConfigureAwait(false);

            if (sheet == null)
            {
                SetRowInventoryStatus(row,
                    $"\"{color}\" {w} × {h} is not in Sheet Store inventory — " +
                    "add it there or enter quantity manually later.",
                    isWarning: true);
                return;
            }

            var (reference, customer, project, salesman) =
                _jobContextProvider?.Invoke() ?? ("", "", "", "");

            var result = await SheetStockLinkService.CommitConsumptionAsync(
                sheet, delta,
                jobReference: reference,
                customerName: customer,
                projectName: project,
                salesman: salesman,
                origin: "Step 6 Wastage (automatic)").ConfigureAwait(false);

            if (result.Success)
                _consumedNetPerRow.AddOrUpdate(
                    row.Id, delta, (_, old) => old + delta);

            SetRowInventoryStatus(row,
                result.Success
                    ? $"{result.Message} Row total consumed: {netAlready + delta}."
                    : result.Message,
                isWarning: !result.Success);
        }

        /// <summary>
        /// Thickness (mm) for the inventory match: taken from the primary
        /// checked source's Step 4 section — the generated spec name leads
        /// with the pane thickness ("6mm Clear FT"), which ParseMm reads.
        /// 0 = unknown → the resolver falls back to best balance match.
        /// </summary>
        private int ResolveRowThicknessMm(WastageRow row)
        {
            try
            {
                int spec = (row.AvailableSources.FirstOrDefault(s => s.IsChecked)
                            ?? row.AvailableSources.FirstOrDefault())
                           ?.SpecNumber ?? 0;
                if (spec <= 0) return 0;

                var sections = _step4SectionsProvider?.Invoke();
                if (sections == null || spec > sections.Count) return 0;

                return SheetStorePricingService.ParseMm(
                    sections[spec - 1]?.GeneratedSpecName);
            }
            catch { return 0; }
        }

        private void SetRowInventoryStatus(WastageRow row, string text, bool isWarning)
        {
            // Row may have been removed while the async work ran.
            if (!WastageRows.Contains(row)) return;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                dispatcher.BeginInvoke(new Action(() =>
                {
                    row.InventoryStatusText = text;
                    row.IsInventoryWarning = isWarning;
                }));
            else
            {
                row.InventoryStatusText = text;
                row.IsInventoryWarning = isWarning;
            }
        }

        private void OnWastageRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(WastageRow.ResultAmount)
                || e.PropertyName == nameof(WastageRow.IsActive))
            {
                RecalculateTotal();
                return;
            }

            // Step 9 reads the setup SHEET QTY even when the wastage charge
            // row is DISABLED, so editing the box must notify downstream
            // consumers even though the disabled row's result (0) did not
            // change. An active row re-raises through ResultAmount instead.
            if (e.PropertyName == nameof(WastageRow.SheetQuantity))
            {
                bool wasDisabled = sender is WastageRow row && !row.IsActive;
                RecalculateTotal();
                if (wasDisabled)
                    OnPropertyChanged(nameof(TotalWastageValue));
            }

            // Ticking or unticking a pane source changes WHICH RACKS this row
            // draws from, so the run figures offered to it have to be resolved
            // again — a row that summed one rack must not keep that total after
            // a second pane is ticked, and one whose last pane was unticked must
            // stop offering a quantity altogether.
            //
            // SourceSummaryText is raised by WastageRow.RecalcFromSources, which
            // is what OnSourceChanged calls for every source property that can
            // change the selection, so it is the precise signal and needs no new
            // event. No cycle: re-resolving writes SheetQuantity, which the
            // branch above already handles, and never re-raises this one.
            if (e.PropertyName == nameof(WastageRow.SourceSummaryText))
                ApplyOptimizationFeedToRow(sender as WastageRow);
        }

        private static Step4PricingSnapshot? ResolveStep4Snapshot(
            IReadOnlyList<Step4PricingSnapshot>? sections,
            SpecGroup group,
            string effectiveGlassType)
        {
            if (sections == null || group.GroupNumber <= 0
                || group.GroupNumber > sections.Count)
                return null;

            var snapshot = sections[group.GroupNumber - 1];
            if (snapshot == null)
                return null;

            bool namesMatch = !string.IsNullOrWhiteSpace(snapshot.GeneratedSpecName)
                && !string.IsNullOrWhiteSpace(group.SelectedSpecName)
                && string.Equals(snapshot.GeneratedSpecName, group.SelectedSpecName,
                    StringComparison.OrdinalIgnoreCase);
            bool groupStillOwnedByStep4 = !group.HasManualStep5SpecOverride
                && !group.HasManualStep5GlassTypeOverride;
            if (!namesMatch && !groupStillOwnedByStep4)
            {
                // The group was manually pointed at another catalog spec. Do
                // not leak the indexed Step 4 section's sheet/rate metadata
                // into that unrelated source. Step-4-owned groups are allowed
                // to use the indexed live snapshot even while Step 5 is still
                // catching up to an edited generated name.
                return null;
            }

            string snapshotType = (snapshot.SelectedGlassTypeCode ?? string.Empty)
                .Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(snapshotType)
                && !string.Equals(snapshotType, effectiveGlassType,
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return snapshot;
        }

        private static void ApplyStep4SourceMetadata(
            SpecGroupColorSide source,
            Step4PricingSnapshot? snapshot,
            string effectiveGlassType)
        {
            if (source == null || snapshot == null)
                return;

            string type = (effectiveGlassType ?? string.Empty).Trim().ToUpperInvariant();
            if (type == "SGU")
            {
                var sgu = snapshot.Sgu ?? new SguPricingPanelSnapshot();
                ApplyMetadata(source, sgu.SheetWidthMm, sgu.SheetHeightMm,
                    sgu.BasePricePerSqm);
                return;
            }

            if (type == "DGU")
            {
                var dgu = snapshot.Dgu ?? new DguPricingPanelSnapshot();
                bool inner = source.SemanticSideLabel == "Inner";
                ApplyMetadata(source,
                    inner ? dgu.InnerSheetWidthMm : dgu.OuterSheetWidthMm,
                    inner ? dgu.InnerSheetHeightMm : dgu.OuterSheetHeightMm,
                    inner ? dgu.InnerBasePrice : dgu.OuterBasePrice);
                return;
            }

            if (type == "LAM")
            {
                var lam = snapshot.Lam ?? new LamPricingPanelSnapshot();
                bool inner = source.SemanticSideLabel == "Inner";
                ApplyMetadata(source,
                    inner ? lam.InnerSheetWidthMm : lam.OuterSheetWidthMm,
                    inner ? lam.InnerSheetHeightMm : lam.OuterSheetHeightMm,
                    inner ? lam.InnerBasePrice : lam.OuterBasePrice);
                return;
            }

            if (type == "DGU+LAM")
            {
                var dguLam = snapshot.DguLam ?? new DguLamPricingPanelSnapshot();

                if (source.SemanticSideLabel == "Lam Inner")
                {
                    ApplyMetadata(source, 0, 0, dguLam.LamInnerBasePrice);
                }
                else if (source.SemanticSideLabel == "Lam Outer")
                {
                    ApplyMetadata(source, 0, 0, dguLam.LamOuterBasePrice);
                }
                else
                {
                    ApplyMetadata(source, 0, 0, dguLam.OuterBasePrice);
                }
            }
        }


        private static void ApplyMetadata(
            SpecGroupColorSide source,
            int width,
            int height,
            decimal price)
        {
            if (width > 0)
                source.SheetWidthMm = width;
            if (height > 0)
                source.SheetHeightMm = height;
            if (price > 0m)
                source.PricePerSqm = price;
        }

        /// <summary>
        /// Legacy display-name colour scrape, hardened: the rules engine
        /// writes "Outer glass wastage" style labels when no colour was
        /// resolved, and those must never be treated as a colour name —
        /// doing so printed "Wastage — Outer glass wastage" on invoices.
        /// Structured colour data (AutoWastageSuggestion.ColorName) is the
        /// primary channel; this is only a compatibility fallback.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string>
            GenericWastageDisplayNames = new(System.StringComparer.OrdinalIgnoreCase)
            {
                "Outer glass wastage",
                "Inner glass wastage",
                "Lam outer glass wastage",
                "Lam inner glass wastage",
                // FIX-2026-09-05 (3+ pane builds): sentinel labels for
                // middle panes must never become COLOURS either.
                "Middle glass wastage",
                "Middle 1 glass wastage",
                "Middle 2 glass wastage",
                "Middle 3 glass wastage"
            };

        private static string ExtractColorKey(string? displayName)
        {
            string text = (displayName ?? string.Empty).Trim();
            if (text.Length == 0 || GenericWastageDisplayNames.Contains(text))
                return string.Empty;

            int emDash = text.IndexOf('—');
            if (emDash > 0)
                text = text[..emDash].Trim();
            else
            {
                int hyphen = text.IndexOf('-');
                if (hyphen > 0)
                    text = text[..hyphen].Trim();
            }

            return GenericWastageDisplayNames.Contains(text)
                ? string.Empty
                : text;
        }

        /// <summary>
        /// Row identity in advanced mode = glass colour + pane side.
        /// "Clear — outer glass wastage" and "Clear — inner glass wastage"
        /// are two separate wastage charges (and two setup SHEET QTY boxes).
        /// </summary>
        private static string WastageRowKey(string? colorName, string? sideLabel) =>
            $"{ExtractColorKey(colorName)}:::{NormalizeSideKey(sideLabel)}";

        private static string NormalizeSideKey(string? sideLabel)
        {
            string side = (sideLabel ?? string.Empty).Trim();
            if (side.IndexOf("Lam Inner", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "Lam Inner";
            if (side.IndexOf("Lam Outer", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "Lam Outer";
            // FIX-2026-09-05 (3+ pane builds): a middle pane is its own
            // wastage row — without this branch "Middle (W2/H2)" fell
            // through to Outer and every middle charge folded into the
            // outer row.
            if (side.IndexOf("Middle", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "Middle";
            if (side.IndexOf("Inner", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "Inner";
            return "Outer";
        }

        /// <summary>
        /// The side a live wastage row belongs to (WastageRow.SideDisplay).
        /// Used to preserve a row's setup across advanced refreshes; a row
        /// created before the side-aware change falls back to outer.
        /// </summary>
        private static string RowSideKey(WastageRow row) =>
            string.IsNullOrWhiteSpace(row.SideDisplay)
                ? "Outer"
                : row.SideDisplay;

        private void RecalculateTotal()
        {
            TotalWastageValue = WastageRows
                .Where(r => r.IsActive)
                .Sum(r => r.ResultAmount);
        }
    }
}