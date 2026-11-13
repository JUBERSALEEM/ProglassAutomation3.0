// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.c.Presentation/step6.c.7.ProformaWorkspaceViewModel.Actions.cs
// MODULE: step6 — what the register's buttons do, and how failure is reported
//
// Each action calls the stage engine once and then refreshes the projections. A
// failure is reported on the page's error surface: a database problem must never
// take the shell down, and a refusal from the engine must say why.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Proforma;

namespace ProGlassAutomation.ViewModels.Proforma
{
    public sealed partial class ProformaWorkspaceViewModel
    {
        // ── Lifecycle ───────────────────────────────────────────────────
        /// <summary>First paint: create what the page needs, then read the register.</summary>
        public async Task InitializeAsync()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                await _workspace.NewDraftAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Report("The proforma invoice tables could not be prepared: "
                    + error.GetBaseException().Message);
            }

            // A fresh, unsaved draft is not a document: keep the register on screen.
            IsEditorOpen = false;
            await LoadRegisterAsync().ConfigureAwait(false);
        }

        public async Task LoadRegisterAsync()
        {
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            CancellationToken token = _loadCts.Token;
            IsBusy = true;
            try
            {
                IReadOnlyList<ProformaInvoiceListRow> rows = await _repository
                    .QueryAsync(new ProformaInvoiceQuery { IncludeCancelled = true, MaxRows = 2000 }, token)
                    .ConfigureAwait(false);

                if (token.IsCancellationRequested) return;
                _allRows.Clear();
                _allRows.AddRange(rows.OrderByDescending(row => row.UpdatedAtUtc));
                ApplyFilter();
                ClearError();
            }
            catch (OperationCanceledException) { /* a newer read owns the list */ }
            catch (Exception error)
            {
                Report("The invoice register could not be read: " + error.GetBaseException().Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyFilter()
        {
            IEnumerable<ProformaInvoiceListRow> filtered = _allRows;
            if (_selectedStatusKey != "All")
                filtered = filtered.Where(row => string.Equals(
                    ProformaInvoiceStatus.Normalize(row.Status), _selectedStatusKey,
                    StringComparison.OrdinalIgnoreCase));

            string search = _searchText.Trim();
            if (search.Length > 0)
            {
                filtered = filtered.Where(row =>
                    Contains(row.ProformaNumber, search) || Contains(row.ClientName, search)
                    || Contains(row.ProjectName, search) || Contains(row.InquiryNumber, search)
                    || Contains(row.PreparedByName, search));
            }

            Rows.Clear();
            foreach (ProformaInvoiceListRow row in filtered) Rows.Add(row);

            foreach (ProformaStatusChip chip in StatusChips)
            {
                chip.Count = chip.Key == "All"
                    ? _allRows.Count
                    : _allRows.Count(row => string.Equals(
                        ProformaInvoiceStatus.Normalize(row.Status), chip.Key,
                        StringComparison.OrdinalIgnoreCase));
                chip.IsSelected = string.Equals(chip.Key, _selectedStatusKey, StringComparison.Ordinal);
            }

            if (SelectedRow != null && !Rows.Contains(SelectedRow)) SelectedRow = null;
            OnPropertyChanged(nameof(RegisterSummary));
            OnPropertyChanged(nameof(IsEmptyRegister));
        }

        private void SelectStatusChip(ProformaStatusChip chip)
        {
            _selectedStatusKey = chip.Key;
            ApplyFilter();
        }

        private static IEnumerable<ProformaStatusChip> BuildStatusChips()
        {
            yield return new ProformaStatusChip { Key = "All", Label = "All" };
            foreach (string status in ProformaInvoiceStatus.All)
                yield return new ProformaStatusChip { Key = status, Label = status };
        }

        private async Task NewInvoiceAsync()
        {
            try
            {
                await _workspace.NewDraftAsync().ConfigureAwait(false);
                PrepareEditor();
                SetStatus("A new draft is open. Its number is allocated when it is first saved.");
            }
            catch (Exception error)
            {
                Report("A new invoice could not be started: " + error.GetBaseException().Message);
            }
        }

        private async Task OpenSelectedAsync()
        {
            if (_selectedRow == null) return;
            int id = _selectedRow.Id;
            try
            {
                bool opened = await _workspace.LoadAsync(id).ConfigureAwait(false);
                if (!opened)
                {
                    Report(_workspace.Messages.Count > 0
                        ? _workspace.Messages[0]
                        : "The invoice could not be opened.");
                    return;
                }

                PrepareEditor();
                await LoadAttachmentsAsync().ConfigureAwait(false);
                ClearError();
            }
            catch (Exception error)
            {
                Report("The invoice could not be opened: " + error.GetBaseException().Message);
            }
        }

        private async Task SaveAsync()
        {
            try
            {
                ProformaInvoiceSaveResult result = await _workspace.SaveAsync().ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    Report(result.Message);
                    return;
                }

                ClearError();
                string message = result.NumberWasAllocated && result.Invoice != null
                    ? "Saved and numbered " + result.Invoice.ProformaNumber + "."
                    : "Saved.";
                if (result.Warnings.Count > 0) message += " " + result.Warnings[0];
                SetStatus(message);
                await LoadAttachmentsAsync().ConfigureAwait(false);
                SyncFromDraft();
                await LoadRegisterAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Report("The invoice could not be saved: " + error.GetBaseException().Message);
            }
        }

        private async Task ChangeStatusAsync(string target)
        {
            try
            {
                ProformaInvoiceStatusResult result =
                    await _workspace.ChangeStatusAsync(target).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    Report(result.Message);
                    return;
                }

                ClearError();
                SetStatus("The invoice is now " + result.Status + ".");
                SyncFromDraft();
                await LoadRegisterAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Report("The status could not be changed: " + error.GetBaseException().Message);
            }
        }

        private async Task DuplicateAsync()
        {
            try
            {
                ProformaInvoiceDraft? current = _workspace.Draft;
                if (current == null || current.Id <= 0) return;
                ProformaInvoiceSaveResult copy = await _repository
                    .DuplicateAsync(current.Id).ConfigureAwait(false);
                if (!copy.Succeeded || copy.Invoice == null)
                {
                    Report(copy.Message);
                    return;
                }

                ClearError();
                SetStatus("Revised as " + copy.Invoice.ProformaNumber + ".");
                await LoadRegisterAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Report("The revision could not be created: " + error.GetBaseException().Message);
            }
        }

        private void CloseEditor()
        {
            IsEditorOpen = false;
            _ = LoadRegisterAsync();
        }

        internal void Report(string message)
        {
            _errorMessage = message ?? "";
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ErrorMessage));
        }

        internal void ClearError()
        {
            if (_errorMessage.Length == 0) return;
            _errorMessage = "";
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(ErrorMessage));
        }

        internal void SetStatus(string message)
        {
            _statusMessage = message ?? "";
            OnPropertyChanged(nameof(HasStatus));
            OnPropertyChanged(nameof(StatusMessage));
        }

        private static bool Contains(string? haystack, string needle) =>
            (haystack ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        public void Dispose() => _loadCts?.Cancel();
    }
}
