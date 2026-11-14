// ═══════════════════════════════════════════════════════════════════════
// FILE: SpecGroup.Part2.cs (Phase 3.8 split — member census verified)
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Behaviors;
using ProGlassAutomation.Services.ProformaInvoice.SpecificationsRepository;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.Specifications;
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
            // ══════════════════════════════════════════════════════════════════
        //  IEXCELPASTEABLE IMPLEMENTATION — CORE
        // ══════════════════════════════════════════════════════════════════

        public void PasteFromText(
            string clipboardText,
            object startRowContext = null,
            int startColumnIndex = 0)
        {
            _ = PasteFromTextAsync(clipboardText, startRowContext, startColumnIndex);
        }

        public int ApplyImportedRows(
            System.Collections.Generic.IReadOnlyList<ProGlassAutomation.Services.ProformaInvoice.Step5Grid.CutListImportRow> rows)
        {
            if (rows == null || rows.Count == 0) return 0;
            int factor = _selectedGlassType == "LAM" ? 4 : 2;
            int added = 0;
            _isBulkAdding = true;
            try
            {
                bool firstEmpty = ItemRows.Count == 1
                    && string.IsNullOrWhiteSpace(ItemRows[0].GlassRef)
                    && ItemRows[0].W1 == 0 && ItemRows[0].H1 == 0;

                for (int i = 0; i < rows.Count; i++)
                {
                    var src = rows[i];
                    GlassItemRow dest;
                    if (i == 0 && firstEmpty)
                    {
                        dest = ItemRows[0];
                    }
                    else
                    {
                        dest = new GlassItemRow
                        {
                            Qty = 1,
                            Rate = SurchargeRate,
                            PerimeterFactor = factor
                        };
                        ItemRows.Add(dest);
                        dest.PropertyChanged += OnRowChanged;
                    }

                    dest.GlassRef = src.GlassRef ?? "";
                    dest.W1 = src.W1;
                    dest.H1 = src.H1;
                    dest.W2 = src.W2;
                    dest.H2 = src.H2;
                    dest.Qty = src.Qty > 0 ? src.Qty : 1;
                    added++;
                }
            }
            finally
            {
                _isBulkAdding = false;
                TotalRows = ItemRows.Count;
                RenumberRows();
                RecalculateFooter();
            }
            ItemRows.RaiseReset();
            return added;
        }

        public void AddRows(int count)
        {
            if (count <= 0) return;
            if (count > 10_000) count = 10_000;

            int factor = _selectedGlassType == "LAM" ? 4 : 2;
            var newRows = new List<GlassItemRow>(count);

            for (int i = 0; i < count; i++)
            {
                newRows.Add(new GlassItemRow
                {
                    SerialNumber = 0,
                    GlassRef = "",
                    Qty = 1,
                    Rate = SurchargeRate,
                    PerimeterFactor = factor
                });
            }

            _isBulkAdding = true;
            try
            {
                ItemRows.AddRangeSilent(newRows);

                foreach (var row in newRows)
                    row.PropertyChanged += OnRowChanged;
            }
            finally
            {
                _isBulkAdding = false;
                TotalRows = ItemRows.Count;
                RenumberRows();
                RecalculateFooter();
            }

            ItemRows.RaiseReset();
        }

        public void RemoveRow(object rowContext)
        {
            if (rowContext is not GlassItemRow row) return;
            if (ItemRows.Count <= 1)
            {
                row.GlassRef = "";
                row.W1 = 0; row.H1 = 0; row.W2 = 0; row.H2 = 0;
                row.Qty = 1;
                return;
            }
            row.PropertyChanged -= OnRowChanged;
            ItemRows.Remove(row);
        }

        // ══════════════════════════════════════════════════════════════════
        //  IEXCELPASTEABLE — FIND / REPLACE
        // ══════════════════════════════════════════════════════════════════

        public object FindNext(string searchText, object fromRowContext)
        {
            if (string.IsNullOrWhiteSpace(searchText)) return null;

            var term = searchText.Trim();
            int startIdx = 0;

            if (fromRowContext is GlassItemRow fromRow)
            {
                int idx = ItemRows.IndexOf(fromRow);
                if (idx >= 0) startIdx = idx + 1;
            }

            for (int i = startIdx; i < ItemRows.Count; i++)
            {
                if (RowMatchesSearch(ItemRows[i], term))
                    return ItemRows[i];
            }

            return null;
        }

        private static bool RowMatchesSearch(GlassItemRow row, string term)
        {
            if (row.GlassRef.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (row.W1.ToString().Contains(term)) return true;
            if (row.H1.ToString().Contains(term)) return true;
            if (row.W2.ToString().Contains(term)) return true;
            if (row.H2.ToString().Contains(term)) return true;
            if (row.Qty.ToString().Contains(term)) return true;
            if (row.Rate.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                    .Contains(term)) return true;
            return false;
        }

        public int FindAndReplace(string findText, string replaceText)
        {
            if (string.IsNullOrWhiteSpace(findText)) return 0;

            int count = 0;
            var replace = replaceText ?? "";

            foreach (var row in ItemRows)
            {
                if (row.GlassRef.IndexOf(
                        findText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    row.GlassRef = System.Text.RegularExpressions.Regex.Replace(
                        row.GlassRef,
                        System.Text.RegularExpressions.Regex.Escape(findText),
                        replace,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    count++;
                }

                if (row.W1.ToString() == findText &&
                    int.TryParse(replace, out int newW1))
                { row.W1 = newW1; count++; }

                if (row.H1.ToString() == findText &&
                    int.TryParse(replace, out int newH1))
                { row.H1 = newH1; count++; }

                if (row.W2.ToString() == findText &&
                    int.TryParse(replace, out int newW2))
                { row.W2 = newW2; count++; }

                if (row.H2.ToString() == findText &&
                    int.TryParse(replace, out int newH2))
                { row.H2 = newH2; count++; }

                if (row.Qty.ToString() == findText &&
                    int.TryParse(replace, out int newQty) && newQty > 0)
                { row.Qty = newQty; count++; }
            }

            if (count > 0)
            {
                ItemRows.RaiseReset();
            }

            return count;
        }

        // ══════════════════════════════════════════════════════════════════
        //  IEXCELPASTEABLE — FILL DOWN
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Single-column fill down. Copies focused column value from row above
        /// targetRow into targetRow. Used when 0 or 1 row is selected.
        /// </summary>
        public void FillDown(object targetRowContext, int columnIndex)
        {
            if (targetRowContext is not GlassItemRow targetRow) return;

            int targetIdx = ItemRows.IndexOf(targetRow);
            if (targetIdx <= 0) return;

            var sourceRow = ItemRows[targetIdx - 1];

            switch (columnIndex)
            {
                case GridColumnIndex.GlassRef:
                    targetRow.GlassRef = sourceRow.GlassRef;
                    break;
                case GridColumnIndex.W1:
                    targetRow.W1 = sourceRow.W1;
                    break;
                case GridColumnIndex.H1:
                    targetRow.H1 = sourceRow.H1;
                    break;
                case GridColumnIndex.W2:
                    targetRow.W2 = sourceRow.W2;
                    break;
                case GridColumnIndex.H2:
                    targetRow.H2 = sourceRow.H2;
                    break;
                case GridColumnIndex.Qty:
                    targetRow.Qty = sourceRow.Qty;
                    break;
                case GridColumnIndex.Rate:
                    targetRow.Rate = sourceRow.Rate;
                    break;
            }
        }

        /// <summary>
        /// Multi-row fill down.
        /// Copies source row's writable columns (GlassRef, W1, H1, W2, H2, Qty)
        /// into ALL target rows.
        /// Rate is excluded — it is auto-calculated by row logic based on Sqm1.
        /// Called by Ctrl+D when 2+ rows are selected.
        /// </summary>
        public void FillDownAcrossRows(object sourceRow, List<object> targetRows)
        {
            if (sourceRow is not GlassItemRow src) return;
            if (targetRows == null || targetRows.Count == 0) return;

            _isBulkAdding = true;
            try
            {
                foreach (var t in targetRows)
                {
                    if (t is not GlassItemRow target) continue;
                    if (ReferenceEquals(target, src)) continue;

                    // Copy writable columns only.
                    // Setters trigger RecalculateAll on each target row.
                    target.GlassRef = src.GlassRef;
                    target.W1 = src.W1;
                    target.H1 = src.H1;
                    target.W2 = src.W2;
                    target.H2 = src.H2;
                    target.Qty = src.Qty;
                }
            }
            finally
            {
                _isBulkAdding = false;

                // Apply Rate auto-adjust for each target row after bulk fill.
                // OnRowChanged was suppressed during the loop by _isBulkAdding,
                // so we manually apply the same rate logic here to ensure
                // target rows get the correct rate based on their new Sqm1.
                foreach (var t in targetRows)
                {
                    if (t is not GlassItemRow target) continue;
                    if (ReferenceEquals(target, src)) continue;
                    if (target.SuppressRateAutoAdjust) continue;

                    var targetRate = target.Sqm1 >= 4m ? SurchargeRate : BaseRate;
                    if (target.Rate != targetRate)
                        target.Rate = targetRate;
                }

                RecalculateFooter();
            }

            // Single Reset — batches all UI updates after multi-row change
            ItemRows.RaiseReset();
        }

        // ══════════════════════════════════════════════════════════════════
        //  IEXCELPASTEABLE — SELECTION SUPPORT
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Clears IsSelected on all rows.</summary>
        public void ClearRowSelection()
        {
            foreach (var row in ItemRows)
                row.IsSelected = false;
        }

        /// <summary>Sets IsSelected=true on all rows in this group.</summary>
        public void SelectAllRows()
        {
            foreach (var row in ItemRows)
                row.IsSelected = true;
        }

        /// <summary>Sets IsSelected on a specific row.</summary>
        public void SetRowSelection(object rowContext, bool isSelected)
        {
            if (rowContext is not GlassItemRow row) return;
            row.IsSelected = isSelected;
        }

        /// <summary>
        /// Selects rows from anchorRow to targetRow (inclusive).
        /// Clears selection outside this range.
        /// Range order is normalised — works whether anchor is before or after target.
        /// </summary>
        public void SelectRowRange(object anchorRow, object targetRow)
        {
            if (anchorRow is not GlassItemRow anchor) return;
            if (targetRow is not GlassItemRow target) return;

            int anchorIdx = ItemRows.IndexOf(anchor);
            int targetIdx = ItemRows.IndexOf(target);

            if (anchorIdx < 0 || targetIdx < 0) return;

            int start = Math.Min(anchorIdx, targetIdx);
            int end = Math.Max(anchorIdx, targetIdx);

            for (int i = 0; i < ItemRows.Count; i++)
            {
                ItemRows[i].IsSelected = (i >= start && i <= end);
            }
        }

        /// <summary>
        /// Returns currently selected rows in original collection order.
        /// </summary>
        public List<object> GetSelectedRows()
        {
            var result = new List<object>();
            foreach (var row in ItemRows)
            {
                if (row.IsSelected)
                    result.Add(row);
            }
            return result;
        }

        // ══════════════════════════════════════════════════════════════════
        //  HIGHLIGHT ROW — 2 second yellow flash after Find match
        // ══════════════════════════════════════════════════════════════════

        public void HighlightRow(object rowContext)
        {
            if (rowContext is not GlassItemRow row) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            dispatcher.InvokeAsync(() =>
            {
                row.IsHighlighted = true;

                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(2000)
                };

                EventHandler tickHandler = null;
                tickHandler = (s, e) =>
                {
                    row.IsHighlighted = false;
                    if (s is DispatcherTimer t)
                    {
                        t.Stop();
                        t.Tick -= tickHandler;
                    }
                };

                timer.Tick += tickHandler;
                timer.Start();

            }, DispatcherPriority.Background);
        }

        // ══════════════════════════════════════════════════════════════════
        //  GENERATE ROWS
        // ══════════════════════════════════════════════════════════════════

        private void ExecuteGenerateRows()
        {
            var text = (_bulkRowCountText ?? "").Trim();

            if (!int.TryParse(text, out int count) || count <= 0)
            {
                _ = ShowToastAsync(
                    "⚠  Please enter a valid number of rows to generate.",
                    isError: true);
                return;
            }

            if (count > 10_000)
            {
                _ = ShowToastAsync(
                    "⚠  Maximum 10,000 rows per spec group. " +
                    "Please use import for larger datasets.",
                    isError: true);
                return;
            }

            AddRows(count);
            BulkRowCountText = "";

            _ = ShowToastAsync($"✓  {count} row(s) generated successfully.");
        }

        // ══════════════════════════════════════════════════════════════════
        //  FIND / REPLACE HANDLERS — persistent dialogs via callback pattern
        // ══════════════════════════════════════════════════════════════════

        private void ExecuteOpenFind()
        {
            object lastFoundRow = null;

            ProGlassAutomation.Behaviors.FindReplaceDialog.Show(
                isReplace: false,
                onFindNext: (searchText) =>
                {
                    if (string.IsNullOrWhiteSpace(searchText)) return;

                    var foundRow = FindNext(searchText, lastFoundRow);

                    // Wrap around
                    if (foundRow == null && lastFoundRow != null)
                        foundRow = FindNext(searchText, null);

                    if (foundRow == null)
                    {
                        _ = ShowToastAsync(
                            $"No rows found containing \"{searchText}\".",
                            isError: true);
                        lastFoundRow = null;
                    }
                    else
                    {
                        HighlightRow(foundRow);
                        _ = ShowToastAsync($"✓  Found match — highlighted.");
                        lastFoundRow = foundRow;
                    }
                });
        }

        private void ExecuteOpenFindReplace()
        {
            ProGlassAutomation.Behaviors.FindReplaceDialog.Show(
                isReplace: true,
                onFindNext: null,
                onReplaceAll: (findText, replaceText) =>
                {
                    if (string.IsNullOrWhiteSpace(findText)) return;

                    int count = FindAndReplace(findText, replaceText ?? "");

                    _ = ShowToastAsync(
                        count > 0
                            ? $"✓  Replaced {count} occurrence(s) successfully."
                            : "No matches found.",
                        isError: count == 0);
                });
        }

        // ══════════════════════════════════════════════════════════════════
        //  FILTER PREDICATE
        // ══════════════════════════════════════════════════════════════════

        private bool SpecFilterPredicate(object item)
        {
            if (item is not SavedSpecification spec) return false;
            var typed = _specTypedText?.Trim() ?? "";
            if (string.IsNullOrEmpty(typed)) return true;
            if (string.IsNullOrEmpty(spec.SpecName)) return false;
            return spec.SpecName.IndexOf(
                typed, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool ViewHasAnyItems()
        {
            if (_filteredSpecsView == null) return false;
            var enumerator = _filteredSpecsView.GetEnumerator();
            return enumerator.MoveNext();
        }

        // ══════════════════════════════════════════════════════════════════
        //  LOAD SPECS
        // ══════════════════════════════════════════════════════════════════

        public async Task LoadSpecsAsync()
        {
            // The selected type can change while GetAllAsync is awaiting.
            // Capture both the type and repository, then refuse to publish a
            // stale response. Without this guard an older SGU request could
            // append its results after a Step 4 DGU handoff.
            int generation = ++_specLoadGeneration;
            string selectedType = _selectedGlassType;
            var repository = Specifications;

            _specSource.Clear();
            _sourceKeys.Clear();
            _allTypeSpecsMerged = false;

            bool keepCommittedTypedText =
                !string.IsNullOrEmpty(_selectedSpecName) &&
                string.Equals(_specTypedText, _selectedSpecName,
                    StringComparison.Ordinal);

            if (!_isAutoSwitchingGlassType &&
                !string.IsNullOrEmpty(_specTypedText) &&
                !keepCommittedTypedText)
            {
                _isProgrammaticSpecChange = true;
                try
                {
                    _specTypedText = "";
                    OnPropertyChanged(nameof(SpecTypedText));
                }
                finally { _isProgrammaticSpecChange = false; }
            }

            if (string.IsNullOrWhiteSpace(selectedType) || repository == null)
            {
                _filteredSpecsView?.Refresh();
                return;
            }

            try
            {
                var specs = await repository.GetAllAsync(selectedType);
                if (!IsCurrentSpecLoad(generation, selectedType, repository))
                    return;

                if (specs != null)
                {
                    foreach (var s in specs)
                    {
                        if (s?.SpecName == null) continue;
                        if (_sourceKeys.Add(s.SpecName))
                            _specSource.Add(s);
                    }
                }
                _filteredSpecsView?.Refresh();

                // After reloading specs, re-select the current spec if
                // SelectedSpecName is set but SelectedSpecItem is null or stale.
                // This happens when a spec is synced from Step 4 before the
                // auto-save completes, then the catalog reloads with the new spec.
                if (!string.IsNullOrWhiteSpace(_selectedSpecName))
                {
                    var match = _specSource.FirstOrDefault(s =>
                        string.Equals(s.SpecName, _selectedSpecName,
                            StringComparison.OrdinalIgnoreCase));

                    if (!ReferenceEquals(_selectedSpecItem, match))
                    {
                        _isProgrammaticSpecChange = true;
                        try
                        {
                            _selectedSpecItem = match;
                            OnPropertyChanged(nameof(SelectedSpecItem));
                        }
                        finally { _isProgrammaticSpecChange = false; }
                    }
                }
            }
            catch (Exception ex)
            {
                // A superseded request is no longer relevant; avoid emitting
                // confusing errors after a valid newer selection is on screen.
                if (IsCurrentSpecLoad(generation, selectedType, repository))
                    AppLogService.Debug($"[SpecGroup.LoadSpecs] {ExceptionMessages.Deep(ex)}");
            }
        }

        private bool IsCurrentSpecLoad(
            int generation,
            string selectedType,
            ISpecificationsRepository repository) =>
            generation == _specLoadGeneration
            && ReferenceEquals(repository, Specifications)
            && string.Equals(selectedType, _selectedGlassType,
                StringComparison.OrdinalIgnoreCase);

        public async Task LoadFilteredSpecsAsync() => await LoadSpecsAsync();
        internal void LoadFilteredSpecs() => _ = LoadSpecsAsync();

        private async Task LoadAllTypesFallbackAsync()
        {
            var repository = Specifications;
            if (_allTypeSpecsMerged || repository == null) return;

            int generation = _specLoadGeneration;
            string selectedType = _selectedGlassType;
            _allTypeSpecsMerged = true;

            try
            {
                foreach (var type in new[] { "SGU", "DGU", "LAM", "DGU+LAM" })
                {
                    if (string.Equals(type, selectedType,
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    var specs = await repository.GetAllAsync(type);
                    if (!IsCurrentSpecLoad(generation, selectedType, repository))
                        return;
                    if (specs == null) continue;

                    foreach (var s in specs)
                    {
                        if (s?.SpecName == null) continue;
                        if (_sourceKeys.Add(s.SpecName))
                            _specSource.Add(s);
                    }
                }

                if (!IsCurrentSpecLoad(generation, selectedType, repository))
                    return;

                _filteredSpecsView?.Refresh();
                IsSpecDropdownOpen =
                    !string.IsNullOrEmpty(_specTypedText) && ViewHasAnyItems();
            }
            catch (Exception ex)
            {
                if (IsCurrentSpecLoad(generation, selectedType, repository))
                    AppLogService.Debug($"[SpecGroup.LoadAllTypesFallback] {ExceptionMessages.Deep(ex)}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  SPEC COMMIT + GLASS TYPE AUTO-SWITCH
        // ══════════════════════════════════════════════════════════════════

        private void HandleSpecCommit(string specName)
        {
            if (string.IsNullOrWhiteSpace(specName)) return;

            var detected = GlassTypeDetector.Detect(specName);

            if (detected == "Unknown")
            {
                // ──────────────────────────────────────────────────────────
                //  MULTI-PANE SPEC WITHOUT SPACER/INTERLAYER DETAIL
                //
                //  The spec name lists several panes but omits the build
                //  markers (e.g. "6mm Clear FT + 6mm Clear FT" instead of
                //  "... + 12mm ASP"). The text cannot say DGU vs LAM vs
                //  hybrid, so the authoritative glass type (Step 4 selection
                //  or the previous type) is KEPT. Guessing SGU here auto-
                //  switched the field to single glazing and Step 6 advanced
                //  automation stopped suggesting the inner pane / cavity
                //  charges — the "auto charge is not triggering" report.
                // ──────────────────────────────────────────────────────────
                if (GlassTypeDetector.IsMultiPaneWithoutBuildMarkers(specName))
                {
                    int paneCount = PnLSpecParser.GlassMatches(specName).Count;
                    string kept = string.IsNullOrWhiteSpace(_selectedGlassType)
                        ? "(not set)"
                        : _selectedGlassType;

                    _ = ShowToastAsync(
                        $"Spec lists {paneCount} glass panes without " +
                        "spacer/interlayer detail — keeping glass type " +
                        $"{kept}. Add the build markers to the spec name " +
                        "to auto-switch it.");
                    return;
                }

                _ = ShowToastAsync(
                    "⚠  Cannot detect glass type for this specification. " +
                    "Please update it in Price & Specs Generator.",
                    isError: true);
                return;
            }

            if (!string.Equals(
                    detected, _selectedGlassType,
                    StringComparison.OrdinalIgnoreCase))
            {
                // Catalogue pick must not override an operator type, a Step 4
                // handoff, or any type already on the group. Auto-switch only
                // when the field is still empty (bootstrap).
                if (HasManualStep5GlassTypeOverride
                    || IsStep4OwnedSelection
                    || !string.IsNullOrWhiteSpace(_selectedGlassType))
                {
                    string kept = string.IsNullOrWhiteSpace(_selectedGlassType)
                        ? "(not set)"
                        : _selectedGlassType;
                    _ = ShowToastAsync(
                        $"Keeping glass type {kept} (spec name suggests {detected}).");
                    return;
                }

                _isAutoSwitchingGlassType = true;
                try
                {
                    var previous = _selectedGlassType;
                    SelectedGlassType = detected;
                    RefreshGlassColorsFromSpecName();

                    _ = ShowToastAsync(
                        $"✓  Auto-switched glass type: {previous} → {detected}");
                }
                finally { _isAutoSwitchingGlassType = false; }
            }
        }

        /// <summary>
        /// Manual glass-type changes must not leave a ready spec from a
        /// different build family on screen. That stale pairing made Step 6
        /// advanced automation read the wrong type/spec combination and miss
        /// charges that were clearly present in the spec text.
        ///
        /// We only clear when the current spec is recognisably of another
        /// type. Unknown free-typed text is left alone so the operator can
        /// keep editing it.
        /// </summary>
        private void ResetSpecSelectionForManualGlassTypeChange(string previousType, string nextType)
        {
            if (string.IsNullOrWhiteSpace(_selectedSpecName)
                || string.IsNullOrWhiteSpace(nextType)
                || string.Equals(previousType, nextType, StringComparison.OrdinalIgnoreCase))
                return;

            string detected = GlassTypeDetector.Detect(_selectedSpecName);
            if (detected == "Unknown"
                || string.Equals(detected, nextType, StringComparison.OrdinalIgnoreCase))
                return;

            // CARRY THE GLASS FORWARD ACROSS A TYPE SWITCH.
            //
            // Requested behaviour: a DGU of
            //     "6mm Clear FT Glass + 12mm ASP + 6mm Clear FT Glass"
            // switched to SGU should land on "6mm Clear FT Glass" rather
            // than clearing the box and making the operator retype it. The
            // outer pane is the shared component of every build family, so
            // it is the correct thing to keep going DGU/LAM/DGU+LAM -> SGU.
            //
            // Only attempted when the target type is SGU and the source name
            // actually parses into panes. Anything else still clears, which
            // preserves the original safety property: a stale spec from a
            // different build family must never stay on screen (it made
            // Step 6 automation read the wrong type/spec pair).
            string? carried = TryCarryForwardSpecName(nextType);

            _isProgrammaticSpecChange = true;
            try
            {
                _selectedSpecName = carried ?? "";
                _specTypedText = carried ?? "";
                _selectedSpecItem = null;
                OnPropertyChanged(nameof(SelectedSpecName));
                OnPropertyChanged(nameof(SpecTypedText));
                OnPropertyChanged(nameof(SelectedSpecItem));
                OnPropertyChanged(nameof(SpecDisplayName));
                IsSpecDropdownOpen = false;
                ImportedPrice = 0m;
                _isAutoFillingRate = true;
                try { BaseRate = 0m; }
                finally { _isAutoFillingRate = false; }
                HasManualStep5BaseRateOverride = false;
                RefreshGlassColorsFromSpecName();

                // Re-adopt for the carried name so the sheet chosen in Step 4
                // for that pane follows too; "" correctly resets to zero.
                AdoptSheetSelection(carried ?? "");
            }
            finally { _isProgrammaticSpecChange = false; }

            // The carried name is a real saved spec in most cases, so pull its
            // price back in. Runs after the programmatic flag is cleared so
            // the normal auto-fill path applies.
            if (!string.IsNullOrEmpty(carried))
                TryAutoFillRate();
        }

        /// <summary>
        /// Returns the single-pane spec name to keep when switching to SGU,
        /// or null when nothing sensible can be carried (caller then clears).
        ///
        /// Uses the OUTER pane, matching how Step 4 composes a build: the
        /// outer leaf is listed first for DGU, LAM and DGU+LAM alike.
        /// </summary>
        private string? TryCarryForwardSpecName(string nextType)
        {
            if (!string.Equals(nextType, "SGU", StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                var panes = PnLSpecParser.GlassMatches(_selectedSpecName);
                if (panes.Count == 0) return null;

                // Rebuild the pane exactly as Step 4 would name it, so the
                // result matches a saved SGU spec and the dropdown/price
                // lookup can find it.
                var m = panes[0];
                string thk = (m.Groups["thk"].Value ?? "").Trim();
                string color = (m.Groups["color"].Value ?? "").Trim();
                if (thk.Length == 0 || color.Length == 0) return null;

                // m.Value is the whole matched pane including its treatment
                // and the trailing "Glass", which is precisely the SGU form.
                string candidate = m.Value.Trim();
                return candidate.Length > 0 ? candidate : null;
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[SpecGroup.CarryForward] {ExceptionMessages.Deep(ex)}");
                return null;
            }
        }

        /// <summary>
        /// When a spec is picked from autocomplete, sets BaseRate from the
        /// spec's PricePerSqm. Also sets ImportedPrice on FIRST pick (when
        /// ImportedPrice is still 0m) — this preserves the imported reference
        /// even if user later manually edits BaseRate.
        /// </summary>
        private void TryAutoFillRate()
        {
            if (string.IsNullOrWhiteSpace(_selectedSpecName)) return;

            var match = _specSource.FirstOrDefault(s =>
                string.Equals(
                    s.SpecName, _selectedSpecName,
                    StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                // Set ImportedPrice ONCE — first time a spec is picked.
                // This preserves the original imported reference even if
                // the user later edits BaseRate manually.
                if (_importedPrice == 0m)
                {
                    ImportedPrice = match.PricePerSqm;
                }

                if (!HasManualStep5BaseRateOverride)
                {
                    _isAutoFillingRate = true;
                    try { BaseRate = match.PricePerSqm; }
                    finally { _isAutoFillingRate = false; }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  COLLECTION HANDLERS
        // ══════════════════════════════════════════════════════════════════

        private void OnItemRowsChanged(
            object s, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (GlassItemRow r in e.OldItems)
                    r.PropertyChanged -= OnRowChanged;

            if (e.NewItems != null)
                foreach (GlassItemRow r in e.NewItems)
                    r.PropertyChanged += OnRowChanged;

            if (_isBulkAdding) return;

            TotalRows = ItemRows.Count;
            RenumberRows();
            RecalculateFooter();
        }

        private void OnRowChanged(object s, PropertyChangedEventArgs e)
        {
            if (_isBulkAdding) return;

            if (e.PropertyName is
                nameof(GlassItemRow.Qty) or
                nameof(GlassItemRow.TotalSqm) or
                nameof(GlassItemRow.TotalSqm2) or
                nameof(GlassItemRow.Lm1) or
                nameof(GlassItemRow.Lm2) or
                nameof(GlassItemRow.Value))
            {
                RecalculateFooter();
            }

            if (e.PropertyName == nameof(GlassItemRow.Sqm1)
                && s is GlassItemRow row
                && !row.SuppressRateAutoAdjust)
            {
                var targetRate = row.Sqm1 >= 4m ? SurchargeRate : BaseRate;
                if (row.Rate != targetRate)
                    row.Rate = targetRate;
            }
        }

        private void RecalculateFooter()
        {
            if (ItemRows.Count == 0)
            {
                FooterTotalQty = 0;
                FooterTotalSqm = 0m;
                FooterTotalSqm2 = 0m;
                FooterTotalLm1 = 0m;
                FooterTotalLm2 = 0m;
                FooterTotalValue = 0m;
                return;
            }

            int qty = 0;
            decimal sqm = 0m, sqm2 = 0m,
                    lm1 = 0m, lm2 = 0m, val = 0m;

            foreach (var r in ItemRows)
            {
                qty += r.Qty;
                sqm += r.TotalSqm;
                sqm2 += r.TotalSqm2;
                lm1 += r.Lm1;
                lm2 += r.Lm2;
                val += r.Value;
            }

            FooterTotalQty = qty;
            FooterTotalSqm = Math.Round(sqm, 3);
            FooterTotalSqm2 = Math.Round(sqm2, 3);
            FooterTotalLm1 = Math.Round(lm1, 3);
            FooterTotalLm2 = Math.Round(lm2, 3);
            FooterTotalValue = MoneyRounding.Round(val);
        }
    }
}
