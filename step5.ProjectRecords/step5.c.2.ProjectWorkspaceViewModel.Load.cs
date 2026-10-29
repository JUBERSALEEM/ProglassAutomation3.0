// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.c.2.ProjectWorkspaceViewModel.Load.cs
// MODULE: step5 — project workspace, reading.
//
// Every read reports its own failure on the error surface instead of throwing:
// a register that crashes when the ERP file is busy is worse than one that says
// why it is empty.
//
// Split from step5.c.1 so no file carries more than one job: the presenter owns
// its state and commands, this owns reading and nothing else.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProGlassAutomation.Models.Projects;
using ProGlassAutomation.Services.Projects;

namespace ProGlassAutomation.ViewModels.Projects
{
    public sealed partial class ProjectWorkspaceViewModel
    {
        // ── Loading ─────────────────────────────────────────────────────

        public async Task EnsureInitializedAsync()
        {
            if (_initialized) return;
            try
            {
                await _repository.InitializeAsync().ConfigureAwait(true);
                _initialized = true;
            }
            catch (Exception error)
            {
                ErrorMessage = "The project register could not be opened. " + error.Message;
                return;
            }

            await LoadClientsAsync().ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }

        public async Task ReloadAsync()
        {
            if (!_initialized) return;

            // Retire the previous load BEFORE creating the next one, and never
            // Cancel a source we have already disposed: a disposed
            // CancellationTokenSource throws ObjectDisposedException from
            // Cancel(), which is not a cancellation — it is a defect. It used to
            // surface as "The project could not be saved: The
            // CancellationTokenSource has been disposed." on the SECOND save,
            // because the save's refresh cancelled the source the first refresh
            // had disposed (owner screenshot, round 42).
            var previous = _loadCts;
            _loadCts = null;
            if (previous != null)
            {
                try { previous.Cancel(); }
                catch (ObjectDisposedException) { /* already retired */ }
                previous.Dispose();
            }

            var cts = new CancellationTokenSource();
            _loadCts = cts;
            var token = cts.Token;

            IsBusy = true;
            try
            {
                var query = new ProjectQuery
                {
                    Search = SearchText,
                    IncludeClosed = true
                };
                var rows = await Task.Run(() => _repository.QueryAsync(query, token), token).ConfigureAwait(true);
                if (token.IsCancellationRequested) return;

                int? keepId = _selectedRow?.Id;
                _allRowsCache.Clear();
                foreach (var row in rows) _allRowsCache.Add(row);

                // The chips count what the query returned, before the chip filter:
                // a chip states how many rows it would show, not how many exist.
                int active = rows.Count(row => !row.IsClosed);
                int closed = rows.Count(row => row.IsClosed);
                SetTabCount("All", rows.Count);
                SetTabCount(ProjectStatus.Active, active);
                SetTabCount(ProjectStatus.Closed, closed);

                ApplyStatusFilter(keepId);
                OnPropertyChanged(nameof(RegisterSummary));
                ErrorMessage = "";
            }
            catch (OperationCanceledException)
            {
                // A newer load owns the list now.
            }
            catch (Exception error)
            {
                ErrorMessage = "The project register could not be read: " + error.Message;
            }
            finally
            {
                // Clear the field before disposing, so the next load can never
                // reach a disposed source through it.
                if (ReferenceEquals(_loadCts, cts))
                {
                    _loadCts = null;
                    IsBusy = false;
                }
                cts.Dispose();
            }
        }

        private void SetTabCount(string key, int count)
        {
            var tab = StatusTabs.FirstOrDefault(item => item.Key == key);
            if (tab != null) tab.Count = count;
        }

        /// <summary>
        /// The chips filter what is already loaded. The query fetches every status
        /// so the counts are real; filtering again here would issue a second read
        /// for data this screen already has.
        /// </summary>
        private void ApplyStatusFilter(int? keepId)
        {
            var shown = SelectedStatus == "All"
                ? _allRowsCache
                : _allRowsCache.Where(row =>
                    string.Equals(row.Status, SelectedStatus, StringComparison.OrdinalIgnoreCase)).ToList();

            Rows.Clear();
            foreach (var row in shown) Rows.Add(row);

            // NO IMPLICIT SELECTION. Owner, round 43: the inquiry panel was
            // open with nothing clicked, because this line selected the first
            // row on every load and CanUseInquiryLink is satisfied by any
            // selection at all. A panel that opens by itself is a panel that
            // says "an inquiry is linked here" about a project nobody chose.
            // The register therefore starts with no row selected, and a row is
            // selected only by a click — or by a save, which selects the row it
            // just wrote so the operator can see what happened.
            SelectedRow = keepId.HasValue
                ? Rows.FirstOrDefault(row => row.Id == keepId.Value)
                : null;
        }

        private async Task LoadClientsAsync()
        {
            try
            {
                var clients = await _repository.GetClientChoicesAsync().ConfigureAwait(true);
                Clients.Clear();
                foreach (var client in clients) Clients.Add(client);
            }
            catch (Exception error)
            {
                // A client picker that cannot load must not block the register:
                // the free-text client name still works.
                ErrorMessage = "The client list could not be read, so a client can still be typed by hand. "
                               + error.Message;
            }
        }

        private async Task LoadLinkedInquiriesAsync()
        {
            LinkedInquiries.Clear();
            CandidateInquiries.Clear();
            OnPropertyChanged(nameof(CanAttach));
            OnPropertyChanged(nameof(CanDetach));
            if (_selectedRow == null) return;

            try
            {
                var linked = await _repository.GetLinkedInquiriesAsync(_selectedRow.Id).ConfigureAwait(true);
                foreach (var row in linked) LinkedInquiries.Add(row);

                var candidates = await _repository.GetUnlinkedInquiriesAsync(InquirySearch).ConfigureAwait(true);
                foreach (var row in candidates) CandidateInquiries.Add(row);
            }
            catch (Exception error)
            {
                ErrorMessage = "The inquiries for this project could not be read: " + error.Message;
            }
        }

        private async Task LoadCandidatesAsync()
        {
            if (!CanUseInquiryLink) return;
            try
            {
                var candidates = await _repository.GetUnlinkedInquiriesAsync(InquirySearch).ConfigureAwait(true);
                CandidateInquiries.Clear();
                foreach (var row in candidates) CandidateInquiries.Add(row);
            }
            catch (Exception error)
            {
                ErrorMessage = "The unlinked inquiries could not be read: " + error.Message;
            }
        }
    }
}
