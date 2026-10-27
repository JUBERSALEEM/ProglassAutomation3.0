// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.c.2.UserWorkspaceViewModel.cs
// MODULE: step3 — user workspace (register + lifecycle commands).
//
// Read path: search + status/department/role filters → repository projection.
// Write path: open the editor for a new/existing user, activate or deactivate a
// row, or perform the one-time first-administrator bootstrap. There is no
// delete command anywhere in this module.
//
// Read-only mode
//   When the workstation account does not resolve to an active Administrator,
//   the register still loads (so a person can see who is in the directory) but
//   every write command is refused and the header explains why. This is
//   convenience only: the repository independently refuses the same writes, so
//   hiding a button is never the enforcement (see step3.a.4).
//
// First run
//   On a fresh database the directory is empty and nobody can be identified,
//   so no administrator could ever exist. The bootstrap card is the single,
//   explicit answer: the person at this workstation registers the Windows
//   account they are already using as the first Administrator. The repository
//   refuses that call the moment any user exists.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Users;
using ProGlassAutomation.ThemeStudio;
using ProGlassAutomation.ViewModels.Inquiry;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.Users
{
    /// <summary>One status filter on the register, with a live count.</summary>
    public sealed class UserStatusTab : ViewModelBase
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

    /// <summary>User directory register plus editor presentation.</summary>
    public sealed class UserWorkspaceViewModel : ViewModelBase, IDisposable
    {
        private readonly IUserRepository _repository;
        private readonly IUserSession _session;
        private readonly IFirstRunService _firstRun;
        private readonly bool _ownsRepository;
        private CancellationTokenSource? _loadCts;

        private string _searchText = "";
        private string _selectedDepartmentFilter = "";
        private string _selectedRoleFilter = "";
        private UserListRow? _selectedRow;
        private UserEditorViewModel? _editor;
        private string _errorMessage = "";
        private string _statusMessage = "";
        private bool _isBusy;
        private bool _isInitialized;
        private bool _hasLoaded;

        private int _activeCount;
        private int _inactiveCount;
        private int _totalCount;

        private string _bootstrapFullName = "";
        private string _bootstrapDepartment = UserCatalog.AdministrationDepartment;
        private string _bootstrapError = "";
        private bool _isBootstrapping;

        private UserDecision _managementDecision = UserAuthority_NotLoaded;

        /// <summary>Placeholder until the directory has been read once.</summary>
        private static readonly UserDecision UserAuthority_NotLoaded =
            UserDecision.Refuse(UserDecisionCode.NotIdentified, "Checking this workstation account…");

        public UserWorkspaceViewModel()
            : this(new UserRepository(), null, ownsRepository: true) { }

        public UserWorkspaceViewModel(
            IUserRepository repository, IUserSession? session = null, bool ownsRepository = false)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _session = session ?? new UserSession(_repository);
            // The shell's first-run gate and this card share one implementation
            // of the step (step3.b.6), so the two can never drift apart.
            _firstRun = new FirstRunService(_repository);
            _ownsRepository = ownsRepository;

            NewUserCommand = new DelegateCommand(() => OpenEditorForNew());
            EditSelectedCommand = new DelegateCommand(() => OpenEditorForSelected());
            RefreshCommand = new DelegateCommand(() => _ = ReloadAsync());
            ClearSearchCommand = new DelegateCommand(() =>
            {
                SearchText = "";
                _ = ReloadAsync();
            });
            ToggleActiveCommand = new DelegateCommand<UserListRow>(row => _ = ToggleActiveAsync(row));
            EditUserCommand = new DelegateCommand<UserListRow>(row =>
            {
                // The row menu edits the row it was opened on, not whatever
                // happens to be selected elsewhere on the page.
                if (row == null) return;
                SelectedRow = row;
                _ = OpenEditorForRowAsync(row.Id);
            });
            SelectStatusTabCommand = new DelegateCommand<UserStatusTab>(tab => SelectStatusTab(tab));
            RegisterFirstAdministratorCommand = new DelegateCommand(async () => await RegisterFirstAdministratorAsync());

            StatusTabs.Add(new UserStatusTab { Key = "", Label = "All", IsSelected = true });
            StatusTabs.Add(new UserStatusTab { Key = "Active", Label = "Active" });
            StatusTabs.Add(new UserStatusTab { Key = "Inactive", Label = "Inactive" });

            Departments = new ObservableCollection<CatalogRow>();
            Roles = new ObservableCollection<CatalogRow>();
            DepartmentFilters = new ObservableCollection<CatalogRow>();
            RoleFilters = new ObservableCollection<CatalogRow>();
            ApplyCatalogueFilters(new List<CatalogRow>(), new List<CatalogRow>());
        }

        public ObservableCollection<UserListRow> Rows { get; } = new();

        /// <summary>Departments for the editor's selector (catalogue rows).</summary>
        public ObservableCollection<CatalogRow> Departments { get; }

        /// <summary>Roles for the editor's selector (catalogue rows).</summary>
        public ObservableCollection<CatalogRow> Roles { get; }

        /// <summary>Departments for the register filter, with a leading "All departments".</summary>
        public ObservableCollection<CatalogRow> DepartmentFilters { get; }

        /// <summary>Roles for the register filter, with a leading "All roles".</summary>
        public ObservableCollection<CatalogRow> RoleFilters { get; }

        // ── Operator identity (this stage's read-only banner) ────────────

        /// <summary>"Signed in as …" wording, from the workstation account and its directory row.</summary>
        public string OperatorSummary => _session.StatusSummary;

        /// <summary>True when the operator may administer users.</summary>
        public bool CanManageUsers => _managementDecision.IsAllowed;

        /// <summary>True when the screen must be read-only for this operator.</summary>
        public bool IsReadOnly => !CanManageUsers;

        /// <summary>
        /// Why the screen is read-only; empty when the operator may act.
        ///
        /// Empty directory is the one case where the refusal must NOT be shown.
        /// "This workstation account is not registered … ask an administrator"
        /// is circular on a brand-new installation: no administrator can exist
        /// yet, and the setup card directly below is the way to create one. The
        /// first build displayed that refusal and the setup card together, so
        /// the operator was told the feature was unavailable while the form to
        /// enable it sat underneath. The refusal is now suppressed exactly while
        /// the directory is empty, and it returns the moment a user row exists
        /// and still does not match this account.
        /// </summary>
        public string PermissionMessage =>
            _managementDecision.IsAllowed || IsDirectoryEmpty ? "" : _managementDecision.Message;

        public bool HasPermissionMessage => PermissionMessage.Length > 0;

        /// <summary>True when the directory has no users and the bootstrap card should show.</summary>
        public bool IsDirectoryEmpty => _hasLoaded && _totalCount == 0;

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

        /// <summary>All / Active / Inactive, in that order.</summary>
        public ObservableCollection<UserStatusTab> StatusTabs { get; } = new();

        /// <summary>"" = All, otherwise the selected tab's key.</summary>
        public string SelectedStatusKey =>
            StatusTabs.FirstOrDefault(tab => tab.IsSelected)?.Key ?? "";

        /// <summary>Department code filter; "" means every department.</summary>
        public string SelectedDepartmentFilter
        {
            get => _selectedDepartmentFilter;
            set
            {
                if (!SetField(ref _selectedDepartmentFilter, value ?? "")) return;
                _ = ReloadAsync();
            }
        }

        /// <summary>Role code filter; "" means every role.</summary>
        public string SelectedRoleFilter
        {
            get => _selectedRoleFilter;
            set
            {
                if (!SetField(ref _selectedRoleFilter, value ?? "")) return;
                _ = ReloadAsync();
            }
        }

        public UserListRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (!SetField(ref _selectedRow, value)) return;
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedSummary));
                OnPropertyChanged(nameof(SelectedToggleLabel));
            }
        }

        public bool HasSelection => SelectedRow != null;

        /// <summary>Label for the bar's status action on the selected row.</summary>
        public string SelectedToggleLabel =>
            SelectedRow == null ? "Deactivate" : (SelectedRow.IsActive ? "Deactivate" : "Re-activate");

        public string SelectedSummary =>
            SelectedRow == null
                ? "Select a person to edit, or add a new user."
                : $"{SelectedRow.FullName}  ·  {SelectedRow.RoleDepartmentText}"
                  + (SelectedRow.IsActive ? "" : "  ·  inactive");

        // ── Counters ────────────────────────────────────────────────────

        public int ActiveCount { get => _activeCount; private set => SetField(ref _activeCount, value); }

        public int InactiveCount { get => _inactiveCount; private set => SetField(ref _inactiveCount, value); }

        public string RegisterSummary =>
            $"{ActiveCount} active  ·  {InactiveCount} inactive  ·  {Rows.Count} shown";

        // ── Bootstrap ───────────────────────────────────────────────────

        public string BootstrapOperatorUsername => _session.OperatorUsername;

        public string BootstrapFullName
        {
            get => _bootstrapFullName;
            set { if (SetField(ref _bootstrapFullName, value)) BootstrapError = ""; }
        }

        public string BootstrapDepartment
        {
            get => _bootstrapDepartment;
            set => SetField(ref _bootstrapDepartment, value ?? "");
        }

        public string BootstrapError
        {
            get => _bootstrapError;
            private set { if (SetField(ref _bootstrapError, value)) OnPropertyChanged(nameof(HasBootstrapError)); }
        }

        public bool HasBootstrapError => BootstrapError.Length > 0;

        public bool IsBootstrapping
        {
            get => _isBootstrapping;
            private set => SetField(ref _isBootstrapping, value);
        }

        public string BootstrapExplanation =>
            "This directory has no users yet, so nobody can administer it. Register the Windows account "
            + "this workstation is already using as the first Administrator. This is only possible while the "
            + "directory is empty.";

        // ── Editor / feedback ───────────────────────────────────────────

        public UserEditorViewModel? Editor
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

        public ICommand NewUserCommand { get; }
        public ICommand EditUserCommand { get; }
        public ICommand EditSelectedCommand { get; }
        public ICommand SelectStatusTabCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand ToggleActiveCommand { get; }
        public ICommand RegisterFirstAdministratorCommand { get; }

        // ── Load ────────────────────────────────────────────────────────

        /// <summary>Initializes the database once, resolves the operator, then loads the register.</summary>
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
                ErrorMessage = "The user directory could not be opened ("
                    + ex.GetType().Name + "): " + ex.Message;
                IsBusy = false;
                return;
            }
            finally
            {
                IsBusy = false;
            }

            await RefreshOperatorAsync().ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }

        /// <summary>Re-reads the identity row and the catalogues after a change.</summary>
        /// <summary>
        /// Re-reads the operator and the register. The shell calls this when the
        /// page becomes visible again — after the first-run gate has created the
        /// administrator, the rows and the permission decision are stale until the
        /// page looks again. Safe before initialization: it simply does nothing
        /// until the one-time load has run.
        /// </summary>
        public async Task RefreshAsync()
        {
            if (!_isInitialized) return;
            await RefreshOperatorAsync().ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }

        public async Task RefreshOperatorAsync()
        {
            try
            {
                await _session.RefreshAsync().ConfigureAwait(true);
                _managementDecision = _session.ManagementDecision;

                var departments = await _repository.GetDepartmentsAsync().ConfigureAwait(true);
                var roles = await _repository.GetRolesAsync().ConfigureAwait(true);
                ApplyCatalogueFilters(departments, roles);

                OnPropertyChanged(nameof(OperatorSummary));
                OnPropertyChanged(nameof(CanManageUsers));
                OnPropertyChanged(nameof(IsReadOnly));
                OnPropertyChanged(nameof(PermissionMessage));
                OnPropertyChanged(nameof(HasPermissionMessage));
            }
            catch (Exception ex)
            {
                ErrorMessage = "The operator could not be identified ("
                    + ex.GetType().Name + "): " + ex.Message;
            }
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
                // The status chips count what a tab would show, so both statuses
                // are always read; the tab then decides what reaches the register.
                var activeRows = await _repository.QueryAsync(
                    new UserQuery
                    {
                        Search = SearchText,
                        IsActive = true,
                        Department = SelectedDepartmentFilter,
                        Role = SelectedRoleFilter
                    }, cts.Token).ConfigureAwait(true);

                var inactiveRows = await _repository.QueryAsync(
                    new UserQuery
                    {
                        Search = SearchText,
                        IsActive = false,
                        Department = SelectedDepartmentFilter,
                        Role = SelectedRoleFilter
                    }, cts.Token).ConfigureAwait(true);

                int total = await _repository.CountAsync(cts.Token).ConfigureAwait(true);

                if (cts.IsCancellationRequested) return;

                int selectedId = SelectedRow?.Id ?? 0;
                string statusKey = SelectedStatusKey;

                Rows.Clear();
                if (statusKey != "Inactive")
                    foreach (var row in activeRows) Rows.Add(row);
                if (statusKey != "Active")
                    foreach (var row in inactiveRows) Rows.Add(row);

                ActiveCount = activeRows.Count;
                InactiveCount = inactiveRows.Count;
                _totalCount = total;
                _hasLoaded = true;
                foreach (UserStatusTab tab in StatusTabs)
                    tab.Count = tab.Key switch
                    {
                        "Active" => activeRows.Count,
                        "Inactive" => inactiveRows.Count,
                        _ => activeRows.Count + inactiveRows.Count
                    };

                OnPropertyChanged(nameof(RegisterSummary));
                OnPropertyChanged(nameof(IsDirectoryEmpty));
                // The refusal notice is suppressed while the directory is empty,
                // so it has to be re-evaluated whenever that answer changes —
                // including after the bootstrap row is created.
                OnPropertyChanged(nameof(PermissionMessage));
                OnPropertyChanged(nameof(HasPermissionMessage));

                SelectedRow = null;
                if (selectedId > 0)
                    SelectedRow = Rows.FirstOrDefault(row => row.Id == selectedId);

                if (Rows.Count == 0 && total > 0)
                    StatusMessage = "No user matches the current search and filters.";
                else if (Rows.Count == 0)
                    StatusMessage = "";
                else if (StatusMessage.StartsWith("No user matches", StringComparison.Ordinal))
                    StatusMessage = "";
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer search; nothing to report.
            }
            catch (Exception ex)
            {
                ErrorMessage = "The user list could not be loaded ("
                    + ex.GetType().Name + "): " + ex.Message;
            }
            finally
            {
                if (ReferenceEquals(_loadCts, cts))
                {
                    _loadCts = null;
                    IsBusy = false;
                }
                cts.Dispose();
            }
        }

        // ── Lifecycle ───────────────────────────────────────────────────

        private void ApplyCatalogueFilters(
            IReadOnlyList<CatalogRow> departments, IReadOnlyList<CatalogRow> roles)
        {
            Departments.Clear();
            foreach (var department in departments) Departments.Add(department);

            Roles.Clear();
            foreach (var role in roles) Roles.Add(role);

            DepartmentFilters.Clear();
            DepartmentFilters.Add(new CatalogRow { Code = "", DisplayName = "All departments" });
            foreach (var department in departments) DepartmentFilters.Add(department);

            RoleFilters.Clear();
            RoleFilters.Add(new CatalogRow { Code = "", DisplayName = "All roles" });
            foreach (var role in roles) RoleFilters.Add(role);

            // Re-apply the current filter selection now that the lists have
            // items. A ComboBox whose SelectedValue is set while its ItemsSource
            // is still empty stays unselected, and because the bound value never
            // changed afterwards nothing re-applies it: the box renders blank and
            // the operator cannot tell which filter it is. Raising the property
            // pushes the (unchanged) code back into the control, which now finds
            // its item — including the "" row that means "All departments/roles".
            OnPropertyChanged(nameof(SelectedDepartmentFilter));
            OnPropertyChanged(nameof(SelectedRoleFilter));

            if (!Departments.Any(d => d.Code == _bootstrapDepartment) && Departments.Count > 0)
            {
                var administration = Departments.FirstOrDefault(d => d.Code == UserCatalog.AdministrationDepartment);
                _bootstrapDepartment = administration?.Code ?? Departments[0].Code;
                OnPropertyChanged(nameof(BootstrapDepartment));
            }
        }

        private void SelectStatusTab(UserStatusTab? tab)
        {
            if (tab == null) return;
            foreach (UserStatusTab candidate in StatusTabs)
                candidate.IsSelected = ReferenceEquals(candidate, tab);
            OnPropertyChanged(nameof(SelectedStatusKey));
            _ = ReloadAsync();
        }

        private void OpenEditorForNew()
        {
            if (RefuseWhenReadOnly()) return;
            var draft = new UserDraft
            {
                Department = Departments.Any(d => d.Code == UserCatalog.DefaultDepartment)
                    ? UserCatalog.DefaultDepartment
                    : Departments.FirstOrDefault()?.Code ?? UserCatalog.DefaultDepartment,
                Role = UserCatalog.DefaultRole,
                IsActive = true
            };
            OpenEditor(new UserEditorViewModel(
                _repository, draft, Departments.ToList(), Roles.ToList(), _session.OperatorUsername, 0));
        }

        private void OpenEditorForSelected()
        {
            if (SelectedRow == null)
            {
                ErrorMessage = "Select a person first, or add a new user.";
                return;
            }
            _ = OpenEditorForRowAsync(SelectedRow.Id);
        }

        private async Task OpenEditorForRowAsync(int userId)
        {
            if (RefuseWhenReadOnly()) return;
            try
            {
                User? user = await _repository.GetAsync(userId).ConfigureAwait(true);
                if (user == null)
                {
                    ErrorMessage = "That user no longer exists in the directory. Refresh the list.";
                    await ReloadAsync().ConfigureAwait(true);
                    return;
                }

                var editor = new UserEditorViewModel(
                    _repository, UserDraft.From(user), Departments.ToList(), Roles.ToList(),
                    _session.OperatorUsername, user.Id);
                OpenEditor(editor);
                await editor.LoadAsync(user.CreatedByUserId, user.UpdatedByUserId, user.CreatedAt, user.UpdatedAt)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ErrorMessage = "The user could not be opened ("
                    + ex.GetType().Name + "): " + ex.Message;
            }
        }

        private void OpenEditor(UserEditorViewModel editor)
        {
            if (Editor != null)
            {
                Editor.Saved -= OnEditorSaved;
                Editor.StatusChanged -= OnEditorStatusChanged;
                Editor.Cancelled -= OnEditorCancelled;
            }
            Editor = editor;
            editor.Saved += OnEditorSaved;
            editor.StatusChanged += OnEditorStatusChanged;
            editor.Cancelled += OnEditorCancelled;
            StatusMessage = "";
        }

        private void CloseEditor()
        {
            if (Editor != null)
            {
                Editor.Saved -= OnEditorSaved;
                Editor.StatusChanged -= OnEditorStatusChanged;
                Editor.Cancelled -= OnEditorCancelled;
            }
            Editor = null;
        }

        private async void OnEditorSaved(object? sender, User user)
        {
            try
            {
                StatusMessage = $"{user.FullName} saved.";
                CloseEditor();
                // The operator's own row may have just changed (rename, role),
                // so identity is re-read before the register refreshes.
                await RefreshOperatorAsync().ConfigureAwait(true);
                await ReloadAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ErrorMessage = "The list could not be refreshed ("
                    + ex.GetType().Name + "): " + ex.Message;
            }
        }

        private async void OnEditorStatusChanged(object? sender, User user)
        {
            try
            {
                StatusMessage = $"{user.FullName} is now {(user.IsActive ? "active" : "inactive")}.";
                await RefreshOperatorAsync().ConfigureAwait(true);
                await ReloadAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ErrorMessage = "The list could not be refreshed ("
                    + ex.GetType().Name + "): " + ex.Message;
            }
        }

        private void OnEditorCancelled(object? sender, EventArgs e) => CloseEditor();

        private async Task ToggleActiveAsync(UserListRow? row)
        {
            if (row == null) return;
            if (RefuseWhenReadOnly()) return;

            ClearFeedback();
            try
            {
                var result = await _repository
                    .SetActiveAsync(row.Id, !row.IsActive, _session.OperatorUsername)
                    .ConfigureAwait(true);

                if (!result.Succeeded)
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(result.Message)
                        ? "The user's status could not be changed."
                        : result.Message;
                    return;
                }

                StatusMessage = row.IsActive
                    ? $"{row.FullName} deactivated."
                    : $"{row.FullName} re-activated.";
                await RefreshOperatorAsync().ConfigureAwait(true);
                await ReloadAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ErrorMessage = "The status could not be changed ("
                    + ex.GetType().Name + "): " + ex.Message;
            }
        }

        private async Task RegisterFirstAdministratorAsync()
        {
            if (IsBootstrapping) return;
            ClearFeedback();
            IsBootstrapping = true;
            try
            {
                var result = await _firstRun.RegisterAsync(BootstrapFullName, BootstrapDepartment)
                    .ConfigureAwait(true);

                if (!result.Succeeded)
                {
                    string message = string.IsNullOrWhiteSpace(result.Message)
                        ? "The first administrator could not be registered."
                        : result.Message;

                    // If the directory is no longer empty, this card is about to
                    // disappear (IsDirectoryEmpty false). The reason has to
                    // survive that switch, so it goes to the always-visible
                    // error surface instead of the card's own message slot.
                    await ReloadAsync().ConfigureAwait(true);
                    if (IsDirectoryEmpty) BootstrapError = message;
                    else ErrorMessage = message;
                    return;
                }

                BootstrapFullName = "";
                StatusMessage = result.Message;
                await RefreshOperatorAsync().ConfigureAwait(true);
                await ReloadAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                BootstrapError = "The first administrator could not be registered ("
                    + ex.GetType().Name + "): " + ex.Message;
            }
            finally
            {
                IsBootstrapping = false;
            }
        }

        /// <summary>
        /// Refuses a write in the UI when the operator lacks authority, with the
        /// repository's own wording. The repository refuses again regardless.
        /// </summary>
        private bool RefuseWhenReadOnly()
        {
            if (CanManageUsers) return false;
            ErrorMessage = _managementDecision.Message;
            return true;
        }

        private void ClearFeedback()
        {
            ErrorMessage = "";
            StatusMessage = "";
            BootstrapError = "";
        }

        public void Dispose()
        {
            CloseEditor();
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
            if (_ownsRepository && _repository is IDisposable disposable) disposable.Dispose();
        }
    }
}
