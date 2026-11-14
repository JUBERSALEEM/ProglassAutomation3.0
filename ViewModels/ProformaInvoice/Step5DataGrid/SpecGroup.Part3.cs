// ═══════════════════════════════════════════════════════════════════════
// FILE: SpecGroup.Part3.cs (Phase 3.8 split — member census verified)
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

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid
{
        public partial class SpecGroup : ViewModelBase, IExcelPasteable
    {
            // ══════════════════════════════════════════════════════════════════
        //  ROW MANAGEMENT
        // ══════════════════════════════════════════════════════════════════

        private void AddNewRow()
        {
            var row = new GlassItemRow
            {
                SerialNumber = ItemRows.Count + 1,
                GlassRef = "",
                Qty = 1,
                Rate = SurchargeRate,
                PerimeterFactor = _selectedGlassType == "LAM" ? 4 : 2
            };
            ItemRows.Add(row);
        }

        // ══════════════════════════════════════════════════════════════════
        //  PASTE ENGINE
        // ══════════════════════════════════════════════════════════════════

        private async Task PasteFromClipboardAsync()
        {
            await ShowToastAsync(
                "⚠  Please click on the cell where you want to start " +
                "pasting, then press Ctrl+V.",
                isError: true);
        }

        private async Task PasteFromTextAsync(
            string clipboardText,
            object startRowContext,
            int startColumnIndex)
        {
            if (startColumnIndex < 0)
            {
                await ShowToastAsync(
                    "⚠  Please click on the cell where you want to start " +
                    "pasting before pressing Ctrl+V.",
                    isError: true);
                return;
            }

            if (string.IsNullOrWhiteSpace(clipboardText)) return;

            List<string[]> rawRows;
            try
            {
                rawRows = await Task.Run(() => TextParsing.SplitClipboardRows(clipboardText));
            }
            catch (Exception ex)
            {
                await ShowToastAsync($"Parse error: {ExceptionMessages.Deep(ex)}", isError: true);
                return;
            }

            if (rawRows.Count == 0)
            {
                await ShowToastAsync("No valid rows found in clipboard.",
                    isError: true);
                return;
            }

            if (startColumnIndex > GridColumnIndex.Rate)
            {
                await ShowToastAsync(
                    "⚠  Cannot paste into a read-only column.",
                    isError: true);
                return;
            }

            bool showProgress = rawRows.Count >= 10;
            if (showProgress)
            {
                IsPasting = true;
                PasteProgress = 0;
                PasteStatusText = $"Preparing {rawRows.Count} rows…";
                await Task.Yield();
            }

            int startIdx = -1;
            if (startRowContext is GlassItemRow anchorRow)
            {
                int anchorIdx = ItemRows.IndexOf(anchorRow);
                if (anchorIdx >= 0)
                    startIdx = anchorIdx;
            }

            int factor = _selectedGlassType == "LAM" ? 4 : 2;
            var newAppendRows = new List<GlassItemRow>();

            _isBulkAdding = true;
            try
            {
                int parsedIdx = 0;

                if (startIdx >= 0)
                {
                    for (int i = startIdx;
                         i < ItemRows.Count && parsedIdx < rawRows.Count;
                         i++, parsedIdx++)
                    {
                        var columnValues = TextParsing.BuildColumnValues(
                            rawRows[parsedIdx], startColumnIndex, GridColumnIndex.Rate);

                        ItemRows[i].ApplyColumnValues(
                            columnValues, factor, SurchargeRate, BaseRate);

                        if (showProgress && parsedIdx % 50 == 0)
                        {
                            PasteProgress =
                                (int)((double)parsedIdx / rawRows.Count * 80);
                            PasteStatusText =
                                $"Applying row {parsedIdx + 1} of {rawRows.Count}…";
                            await Task.Yield();
                        }
                    }
                }

                while (parsedIdx < rawRows.Count)
                {
                    var row = new GlassItemRow
                    {
                        Qty = 1,
                        Rate = SurchargeRate,
                        PerimeterFactor = factor
                    };

                    var columnValues = TextParsing.BuildColumnValues(
                        rawRows[parsedIdx], startColumnIndex, GridColumnIndex.Rate);

                    row.ApplyColumnValues(
                        columnValues, factor, SurchargeRate, BaseRate);

                    newAppendRows.Add(row);
                    parsedIdx++;

                    if (showProgress && parsedIdx % 50 == 0)
                    {
                        PasteProgress =
                            (int)((double)parsedIdx / rawRows.Count * 80);
                        PasteStatusText =
                            $"Building row {parsedIdx} of {rawRows.Count}…";
                        await Task.Yield();
                    }
                }

                if (newAppendRows.Count > 0)
                {
                    ItemRows.AddRangeSilent(newAppendRows);

                    foreach (var row in newAppendRows)
                        row.PropertyChanged += OnRowChanged;
                }
            }
            finally
            {
                _isBulkAdding = false;
            }

            TotalRows = ItemRows.Count;
            RenumberRows();
            RecalculateFooter();

            if (showProgress)
            {
                PasteProgress = 90;
                PasteStatusText = "Refreshing display…";
                await Task.Yield();
            }

            ItemRows.RaiseReset();

            if (showProgress)
            {
                PasteProgress = 100;
                PasteStatusText = $"✓  {rawRows.Count} row(s) ready.";
                await Task.Yield();
                IsPasting = false;
                PasteProgress = 0;
                PasteStatusText = "";
            }

            await ShowToastAsync($"✓  Pasted {rawRows.Count} row(s) successfully.");
        }

        // ══════════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════════

        private async Task ShowToastAsync(string message, bool isError = false)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            await dispatcher.InvokeAsync(() =>
            {
                ToastMessage = message;
                ToastIsError = isError;
                IsToastVisible = true;

                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(3500)
                };

                EventHandler tickHandler = null;
                tickHandler = (s, e) =>
                {
                    IsToastVisible = false;
                    ToastMessage = "";
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

        private void RenumberRows()
        {
            for (int i = 0; i < ItemRows.Count; i++)
                ItemRows[i].SerialNumber = i + 1;
        }

        private void UpdatePerimeterFactors()
        {
            int factor = _selectedGlassType == "LAM" ? 4 : 2;
            foreach (var row in ItemRows)
                row.PerimeterFactor = factor;
        }

        private void UpdateAllRowRates()
        {
            foreach (var row in ItemRows)
            {
                if (row.SuppressRateAutoAdjust) continue;
                var targetRate = row.Sqm1 >= 4m ? SurchargeRate : BaseRate;
                if (row.Rate != targetRate)
                    row.Rate = targetRate;
            }
        }
    }
}
