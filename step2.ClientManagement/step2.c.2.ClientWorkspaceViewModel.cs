// ═══════════════════════════════════════════════════════════════════════
// FILE: step2.ClientManagement/step2.c.2.ClientWorkspaceViewModel.cs
// MODULE: step2 — client workspace (register + lifecycle commands).
//
// Read path: search + optional inactive filter → repository projection.
// Write path: open the editor for a new/existing client; activate or
// deactivate a selected row. There is no delete command anywhere in this
// module: deactivation is the only lifecycle transition.
//
// Presentation state only. Every rule lives in step2.a, every query/write in
// step2.b. The editor is a child view model exposed as Editor so the view can
// switch between register and form without a modal window.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Clients;
using ProGlassAutomation.Services.Clients;
using ProGlassAutomation.ThemeStudio;
using ProGlassAutomation.ViewModels.Inquiry;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.Clients
{
    /// <summary>
    /// One status filter on the register, with a live count.
    ///
    /// Owner feedback: the old register hid its filter inside an "Include
    /// inactive" checkbox, so the state of the list was something you had to read
    /// a control to learn. These are the visible equivalent - a fixed, tiny set
    /// (All / Active / Inactive), each carrying the number it would show. The
    /// Inquiry register already uses this shape, so both registers now behave the
    /// same way, and the selected chip paints itself from the theme's
    /// StateSelected / AccentPrimary tokens rather than a hard-coded colour.
    /// </summary>
    public sealed class ClientStatusTab : ViewModelBase
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        private int _count;
        public int Count
        {
            get => _count;
            set { if (SetField(ref _count, value)) OnPropertyChanged(nameof(LabelWithCount)); }
        }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }

        public string CountText => Count.ToString("N0", CultureInfo.CurrentCulture);

        /// <summary>"Active · 3" — the chip states what it would show.</summary>
        public string LabelWithCount => $"{Label} · {CountText}";
    }

    /// <summary>Client directory register plus editor presentation.</summary>
    public sealed class ClientWorkspaceViewModel : ViewModelBase, IDisposable
    {
        private readonly IClientRepository _repository;
        private readonly bool _ownsRepository;
        private CancellationTokenSource? _loadCts;

        private string _searchText = "";
        private bool _includeInactive;
        private ClientListRow? _selectedRow;
        private ClientEditorViewModel? _editor;
        private string _errorMessage = "";
        private string _statusMessage = "";
        private bool _isBusy;
        private bool _isInitialized;
        private IReadOnlyList<ClientRepresentative> _representatives = Array.Empty<ClientRepresentative>();
        private int _activeCount;
        private int _inactiveCount;

        public ClientWorkspaceViewModel() : this(new ClientRepository(), ownsRepository: true) { }

        public ClientWorkspaceViewModel(IClientRepository repository, bool ownsRepository = false)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _ownsRepository = ownsRepository;

            NewClientCommand = new DelegateCommand(() => OpenEditorForNew());
            EditSelectedCommand = new DelegateCommand(() => OpenEditorForSelected());
            RefreshCommand = new DelegateCommand(() => _ = ReloadAsync());
            ClearSearchCommand = new DelegateCommand(() =>
            {
                SearchText = "";
                _ = ReloadAsync();
            });
            ToggleActiveCommand = new DelegateCommand<ClientListRow>(row => _ = ToggleActiveAsync(row));
            EditClientCommand = new DelegateCommand<ClientListRow>(row =>
            {
                // The row menu edits the row it was opened on, not whatever
                // happens to be selected elsewhere on the page.
                if (row == null) return;
                SelectedRow = row;
                _ = OpenEditorForRowAsync(row.Id);
            });
            SelectStatusTabCommand = new DelegateCommand<ClientStatusTab>(tab => SelectStatusTab(tab));
            CloseEditorCommand = new DelegateCommand(() => CloseEditor());

            StatusTabs.Add(new ClientStatusTab { Key = "", Label = "All", IsSelected = true });
            StatusTabs.Add(new ClientStatusTab { Key = "Active", Label = "Active" });
            StatusTabs.Add(new ClientStatusTab { Key = "Inactive", Label = "Inactive" });
        }

        public ObservableCollection<ClientListRow> Rows { get; } = new();

        // ── Filter state ────────────────────────────────────────────────

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!SetField(ref _searchText, value)) return;
                _ = ReloadAsync();
            }
        }

        /// <summary>All / Active / Inactive, in that order. See <see cref="ClientStatusTab"/>.</summary>
        public ObservableCollection<ClientStatusTab> StatusTabs { get; } = new();

        /// <summary>"" = All, otherwise the selected tab's key.</summary>
        public string SelectedStatusKey =>
            StatusTabs.FirstOrDefault(tab => tab.IsSelected)?.Key ?? "";

        /// <summary>
        /// Kept as the backing switch for the repository query: the register always
        /// loads the whole directory (inactive included) so the chip counts are
        /// honest, and the tab decides what is shown. Setting it still reloads, so
        /// any caller that used it before keeps working.
        /// </summary>
        public bool IncludeInactive
        {
            get => _includeInactive;
            set
            {
                if (!SetField(ref _includeInactive, value)) return;
                _ = ReloadAsync();
            }
        }

        public ClientListRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (!SetField(ref _selectedRow, value)) return;
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedToggleLabel));
                OnPropertyChanged(nameof(SelectedSummary));
            }
        }

        public bool HasSelection => SelectedRow != null;

        /// <summary>Label for the row action: deactivate an active client, re-activate an inactive one.</summary>
        public string SelectedToggleLabel =>
            SelectedRow == null ? "Deactivate" : (SelectedRow.IsActive ? "Deactivate" : "Re-activate");

        public string SelectedSummary
        {
            get
            {
                if (SelectedRow == null) return "Select a client to edit or change its status.";
                var row = SelectedRow;
                string attention = row.AttentionSummary;
                string baseText = row.ClientName + (row.IsActive ? "" : "  ·  inactive");
                return attention.Length == 0 ? baseText : baseText + "  ·  " + attention;
            }
        }

        // ── Counters ────────────────────────────────────────────────────

        public int ActiveCount
        {
            get => _activeCount;
            private set => SetField(ref _activeCount, value);
        }

        public int InactiveCount
        {
            get => _inactiveCount;
            private set => SetField(ref _inactiveCount, value);
        }

        public string RegisterSummary =>
            $"{ActiveCount} active  ·  {InactiveCount} inactive  ·  {Rows.Count} shown";

        // ── Editor / feedback ───────────────────────────────────────────

        public ClientEditorViewModel? Editor
        {
            get => _editor;
            private set
            {
                if (!SetField(ref _editor, value)) return;
                OnPropertyChanged(nameof(IsEditorOpen));
            }
        }

        public bool IsEditorOpen => Editor != null;

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { if (SetField(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); }
        }

        public bool HasError => ErrorMessage.Length > 0;

        public string StatusMessage
        {
            get => _statusMessage;
            private set { if (SetField(ref _statusMessage, value)) OnPropertyChanged(nameof(HasStatus)); }
        }

        public bool HasStatus => StatusMessage.Length > 0;

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetField(ref _isBusy, value);
        }

        // ── Commands ────────────────────────────────────────────────────

        public ICommand NewClientCommand { get; }
        public ICommand EditClientCommand { get; }
        public ICommand SelectStatusTabCommand { get; }
        public ICommand EditSelectedCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand ToggleActiveCommand { get; }
        public ICommand CloseEditorCommand { get; }

        // ── Load ────────────────────────────────────────────────────────

        /// <summary>Initializes the database once, then loads the register.</summary>
        public async Task EnsureInitializedAsync()
        {
            if (_isInitialized) return;
            IsBusy = true;
            try
            {
                await _repository.InitializeAsync().ConfigureAwait(true);
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = "The client directory could not be opened (" +
                    ex.GetType().Name + "): " + ex.Message;
                IsBusy = false;
                return;
            }
            IsBusy = false;
            await ReloadAsync().ConfigureAwait(true);
        }

        /// <summary>Reloads the register for the current filter. Cancels a previous load.</summary>
        public async Task ReloadAsync()
        {
            if (!_isInitialized) return;

            _loadCts?.Cancel();
            var cts = new CancellationTokenSource();
            _loadCts = cts;

            IsBusy = true;
            try
            {
                // Always the whole directory (inactive included): the chips count
                // what a tab would show, so they cannot be derived from an already
                // filtered page. The tab then decides what reaches the register.
                var rows = await _repository.QueryAsync(
                    new ClientQuery { Search = SearchText, IncludeInactive = true },
                    cts.Token).ConfigureAwait(true);

                // The editor's representative picker is filled from the same load,
                // so opening the editor never waits on a second database read.
                _representatives = await _repository.GetRepresentativesAsync(cts.Token).ConfigureAwait(true);

                if (cts.IsCancellationRequested) return;

                int selectedId = SelectedRow?.Id ?? 0;

                Rows.Clear();
                int active = 0;
                int inactive = 0;
                string statusKey = SelectedStatusKey;
                foreach (ClientListRow row in rows)
                {
                    if (row.IsActive) active++; else inactive++;
                    if (statusKey == "Active" && !row.IsActive) continue;
                    if (statusKey == "Inactive" && row.IsActive) continue;
                    Rows.Add(row);
                }

                ActiveCount = active;
                InactiveCount = inactive;
                foreach (ClientStatusTab tab in StatusTabs)
                    tab.Count = tab.Key switch
                    {
                        "Active" => active,
                        "Inactive" => inactive,
                        _ => active + inactive
                    };
                OnPropertyChanged(nameof(RegisterSummary));

                // Keep the operator's selection across a refresh.
                SelectedRow = null;
                if (selectedId > 0)
                {
                    foreach (ClientListRow row in Rows)
                    {
                        if (row.Id != selectedId) continue;
                        SelectedRow = row;
                        break;
                    }
                }

                if (rows.Count == 0)
                {
                    StatusMessage = SearchText.Trim().Length == 0
                        ? "The client directory is empty. Add the first client to begin."
                        : "No client matches that search.";
                }
                else if (StatusMessage.StartsWith("No client matches", StringComparison.Ordinal))
                {
                    StatusMessage = "";
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer search; nothing to report.
            }
            catch (Exception ex)
            {
                ErrorMessage = "The client list could not be loaded (" +
                    ex.GetType().Name + "): " + ex.Message;
            }
            finally
            {
                // Clearing the field before disposing matters: Cancel() on a
                // disposed CancellationTokenSource throws, and the next search
                // cancels whatever _loadCts currently holds.
                if (ReferenceEquals(_loadCts, cts))
                {
                    _loadCts = null;
                    IsBusy = false;
                }
                cts.Dispose();
            }
        }

        // ── Lifecycle ───────────────────────────────────────────────────

        private void SelectStatusTab(ClientStatusTab? tab)
        {
            if (tab == null) return;
            foreach (ClientStatusTab candidate in StatusTabs)
                candidate.IsSelected = ReferenceEquals(candidate, tab);
            OnPropertyChanged(nameof(SelectedStatusKey));
            OnPropertyChanged(nameof(RegisterSummary));
            _ = ReloadAsync();
        }

        private void OpenEditorForNew()
        {
            ClearEditorError();
            OpenEditor(new ClientEditorViewModel(_repository, new ClientDraft(), 0, _representatives));
        }

        private void OpenEditorForSelected()
        {
            if (SelectedRow == null)
            {
                ErrorMessage = "Select a client first, or add a new one.";
                return;
            }
            _ = OpenEditorForRowAsync(SelectedRow.Id);
        }

        private async Task OpenEditorForRowAsync(int clientId)
        {
            ClearEditorError();
            try
            {
                Client? client = await _repository.GetAsync(clientId).ConfigureAwait(true);
                if (client == null)
                {
                    ErrorMessage = "That client no longer exists in the directory. Refresh the list.";
                    await ReloadAsync().ConfigureAwait(true);
                    return;
                }
                var editor = new ClientEditorViewModel(
                    _repository, ClientDraft.FromClient(client), client.Id, _representatives);

                // A stored reference must be visible in the picker even when that
                // person is no longer active: an unlisted id would render blank and
                // a save would clear the assignment by accident.
                if (client.RepresentativeUserId is int representativeId)
                {
                    string displayName = Rows
                        .FirstOrDefault(row => row.Id == client.Id)?.RepresentativeName ?? "";
                    editor.EnsureRepresentativeOption(representativeId, displayName);
                }

                OpenEditor(editor);
            }
            catch (Exception ex)
            {
                ErrorMessage = "The client could not be opened (" +
                    ex.GetType().Name + "): " + ex.Message;
            }
        }

        private void OpenEditor(ClientEditorViewModel editor)
        {
            if (Editor != null)
            {
                Editor.Saved -= OnEditorSaved;
                Editor.Cancelled -= OnEditorCancelled;
            }
            Editor = editor;
            editor.Saved += OnEditorSaved;
            editor.Cancelled += OnEditorCancelled;
            StatusMessage = "";
        }

        private async void OnEditorSaved(object? sender, Client client)
        {
            try
            {
                CloseEditor();
                await ReloadAsync().ConfigureAwait(true);
                StatusMessage = "Saved " + client.ClientName + ".";
            }
            catch (Exception ex)
            {
                // A handler must not take the application down.
                ErrorMessage = "The list could not be refreshed after saving (" +
                    ex.GetType().Name + "): " + ex.Message;
            }
        }

        private void OnEditorCancelled(object? sender, EventArgs e) => CloseEditor();

        private void CloseEditor()
        {
            if (Editor == null) return;
            Editor.Saved -= OnEditorSaved;
            Editor.Cancelled -= OnEditorCancelled;
            Editor = null;
        }

        private void ClearEditorError()
        {
            if (ErrorMessage.Length > 0) ErrorMessage = "";
            if (StatusMessage.Length > 0) StatusMessage = "";
        }

        /// <summary>
        /// Activates or deactivates one client. Historical rows are never
        /// deleted; they simply stop being offered for new work.
        /// </summary>
        private async Task ToggleActiveAsync(ClientListRow? row)
        {
            row ??= SelectedRow;
            if (row == null)
            {
                ErrorMessage = "Select a client first.";
                return;
            }
            if (IsBusy) return;

            IsBusy = true;
            ErrorMessage = "";
            try
            {
                ClientSaveResult result = await _repository
                    .SetActiveAsync(row.Id, !row.IsActive)
                    .ConfigureAwait(true);

                if (!result.Succeeded)
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(result.Message)
                        ? "The client status could not be changed."
                        : result.Message;
                    return;
                }

                await ReloadAsync().ConfigureAwait(true);
                StatusMessage = row.IsActive
                    ? "Deactivated " + row.ClientName + ". The record is kept for history."
                    : "Re-activated " + row.ClientName + ".";
            }
            catch (Exception ex)
            {
                ErrorMessage = "The client status could not be changed (" +
                    ex.GetType().Name + "): " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void Dispose()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
            CloseEditor();
        }
    }
}
