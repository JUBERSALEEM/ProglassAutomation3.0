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
// First run — two states, one card
//   • On a fresh database the directory is empty and nobody can be identified,
//     so no administrator could ever exist. The card registers the Windows
//     account this workstation is already using as the first Administrator.
//   • On a directory written before migration 1015 the rows exist but none has a
//     password, so nobody can sign in and no administrator exists to set one.
//     The same card then gives the workstation's own Administrator row its first
//     password. Both doors are closed by the repository the moment they would
//     stop being true, and neither is reachable from a second place.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Audit;
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
        private CatalogRow? _selectedDepartmentFilterItem;
        private CatalogRow? _selectedRoleFilterItem;
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
        private string _bootstrapPassword = "";
        private string _bootstrapPasswordConfirm = "";
        private string _bootstrapDepartment = UserCatalog.AdministrationDepartment;
        private CatalogRow? _bootstrapDepartmentItem;
        private string _bootstrapError = "";
        private bool _isBootstrapping;
        private bool _isCredentialSetup;
        private bool _canSetFirstPassword;
        private string _firstPasswordAdministrator = "";

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
            SignOutCommand = new DelegateCommand(SignOut);
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

        /// <summary>
        /// The department box's selection as an ITEM, not as a code.
        /// </summary>
        /// <remarks>
        /// Binding <c>SelectedValue</c> to a string put the empty "everything"
        /// code in front of the item list while that list was still loading; a
        /// ComboBox resolves SelectedValue only when it has items, so the control
        /// stayed unselected and rendered blank — twice reported by the owner.
        /// Selecting the row object itself removes the lookup entirely: the box
        /// shows whatever item the view model points at, or nothing if the list
        /// is genuinely empty.
        /// </remarks>
        public CatalogRow? SelectedDepartmentFilterItem
        {
            get => _selectedDepartmentFilterItem;
            set
            {
                if (!SetField(ref _selectedDepartmentFilterItem, value)) return;
                SelectedDepartmentFilter = value?.Code ?? "";
                OnPropertyChanged(nameof(SelectedDepartmentFilterText));
            }
        }

        /// <summary>The role box's selection as an ITEM; see the department note.</summary>
        public CatalogRow? SelectedRoleFilterItem
        {
            get => _selectedRoleFilterItem;
            set
            {
                if (!SetField(ref _selectedRoleFilterItem, value)) return;
                SelectedRoleFilter = value?.Code ?? "";
                OnPropertyChanged(nameof(SelectedRoleFilterText));
            }
        }

        /// <summary>What the department box shows; "" when the list is empty.</summary>
        public string SelectedDepartmentFilterText => SelectedDepartmentFilterItem?.DisplayName ?? "";

        /// <summary>What the role box shows; "" when the list is empty.</summary>
        public string SelectedRoleFilterText => SelectedRoleFilterItem?.DisplayName ?? "";

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

        /// <summary>
        /// The account the bootstrap row will be registered under: the Windows
        /// account running this process, never the session (a fresh directory has
        /// nobody signed in, so the session would be empty).
        /// </summary>
        public string BootstrapOperatorUsername => CurrentOperator.Username;

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

        /// <summary>
        /// The first-run card's department box, bound as an item for the same
        /// reason the register filters are: the code list arrives after the view
        /// binds, and a SelectedValue resolved too early renders blank.
        /// </summary>
        public CatalogRow? BootstrapDepartmentItem
        {
            get => _bootstrapDepartmentItem;
            set
            {
                if (!SetField(ref _bootstrapDepartmentItem, value)) return;
                BootstrapDepartment = value?.Code ?? "";
            }
        }

        /// <summary>Password chosen for the first administrator; never stored as text.</summary>
        public string BootstrapPassword
        {
            get => _bootstrapPassword;
            set { if (SetField(ref _bootstrapPassword, value ?? "")) BootstrapError = ""; }
        }

        public string BootstrapPasswordConfirm
        {
            get => _bootstrapPasswordConfirm;
            set { if (SetField(ref _bootstrapPasswordConfirm, value ?? "")) BootstrapError = ""; }
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

        /// <summary>
        /// True when the card is giving an existing administrator row its FIRST
        /// password (a directory from before migration 1015), rather than creating
        /// the first administrator.
        /// </summary>
        public bool IsCredentialSetup
        {
            get => _isCredentialSetup;
            private set
            {
                if (!SetField(ref _isCredentialSetup, value)) return;
                OnPropertyChanged(nameof(ShowBootstrapCard));
                OnPropertyChanged(nameof(ShowBootstrapIdentityFields));
                OnPropertyChanged(nameof(BootstrapTitle));
                OnPropertyChanged(nameof(BootstrapExplanation));
                OnPropertyChanged(nameof(BootstrapSubmitLabel));
                OnPropertyChanged(nameof(BootstrapAccessibleName));
            }
        }

        /// <summary>True when this workstation owns the row that needs the password.</summary>
        public bool CanSetFirstPassword
        {
            get => _canSetFirstPassword;
            private set => SetField(ref _canSetFirstPassword, value);
        }

        /// <summary>The administrator row waiting for its first password.</summary>
        public string FirstPasswordAdministrator
        {
            get => _firstPasswordAdministrator;
            private set => SetField(ref _firstPasswordAdministrator, value);
        }

        /// <summary>
        /// The card shows when the directory is empty (create the first
        /// administrator) or when nobody in it has a password yet (give the
        /// workstation's own administrator row one). Without the second case an
        /// upgraded installation has no way in at all.
        /// </summary>
        public bool ShowBootstrapCard => IsDirectoryEmpty || IsCredentialSetup;

        public string BootstrapTitle => IsCredentialSetup
            ? "Set the administrator's password"
            : "Register the first administrator";

        /// <summary>Nobody's identity is edited by the first-password step.</summary>
        public bool ShowBootstrapIdentityFields => !IsCredentialSetup;

        public string BootstrapSubmitLabel => IsCredentialSetup
            ? "Set my password"
            : "Register me as administrator";

        public string BootstrapAccessibleName => IsCredentialSetup
            ? "Set the first password for this administrator account"
            : "Register this account as the first administrator";

        public string BootstrapExplanation => IsCredentialSetup
            ? (CanSetFirstPassword
                ? "Every account in this directory was created before passwords existed, so nobody can sign "
                  + "in and there is no administrator who could set one. Choose a password for the "
                  + "Administrator below — it is yours, so it is not temporary. This closes as soon as any "
                  + "account has a password."
                : (FirstPasswordAdministrator.Length > 0
                    ? $"No account in this directory has a password yet. Run the application from the Windows "
                      + $"account \"{FirstPasswordAdministrator}\" — the first password can only be set by the "
                      + "person it belongs to."
                    : "No account in this directory has a password yet, and no active Administrator row exists "
                      + "to set one for. Sign in from the machine that owns the administrator account."))
            : "This directory has no users yet, so nobody can administer it. Register the Windows account "
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

        /// <summary>Ends the session; the shell puts the sign-in gate back.</summary>
        public ICommand SignOutCommand { get; }

        /// <summary>
        /// Says whose data this page is showing. An administrator sees the whole
        /// directory; everybody else sees only their own row, because the directory
        /// is staff data and the read path enforces that, not the filter boxes.
        /// </summary>
        public string ScopeNote => _session.IsAdministrator || !_session.IsIdentified
            ? ""
            : $"Signed in as {_session.StatusSummary}. Only your own record is listed.";

        public bool HasScopeNote => ScopeNote.Length > 0;

        private void SignOut() => _session.SignOut();
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

                // The same question the startup gate asks, asked again here: the
                // gate can be dismissed or skipped, and the card below is then the
                // only way into an installation that has no password yet.
                FirstRunSnapshot firstRunState = await _firstRun.InspectAsync().ConfigureAwait(true);
                CanSetFirstPassword = firstRunState.CanClaimFirstPassword;
                FirstPasswordAdministrator = firstRunState.FirstAdministrator?.Username ?? "";
                IsCredentialSetup = firstRunState.IsCredentialSetupRequired;

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
        /// <summary>
        /// The row id a non-administrator may read: their own, or null for an
        /// administrator. Applied to every query this page makes.
        /// </summary>
        /// <remarks>
        /// Nobody signed in is not the same as an administrator: before a
        /// verified sign-in the id is 0, which matches no row, so a page that
        /// somehow loads early shows nothing instead of the whole directory.
        /// </remarks>
        private int? ReadScope => AuthenticationContext.IsAdministrator
            ? null
            : AuthenticationContext.UserId ?? 0;

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
                        Role = SelectedRoleFilter,
                        ScopeToUserId = ReadScope
                    }, cts.Token).ConfigureAwait(true);

                var inactiveRows = await _repository.QueryAsync(
                    new UserQuery
                    {
                        Search = SearchText,
                        IsActive = false,
                        Department = SelectedDepartmentFilter,
                        Role = SelectedRoleFilter,
                        ScopeToUserId = ReadScope
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
                // The bootstrap card shows for either first-run state.
                OnPropertyChanged(nameof(ShowBootstrapCard));
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

            // The face shows the SELECTION and nothing else — the owner's
            // direction: "remove department and roles from each dropdowns". The
            // field's identity lives in its accessible name and tooltip, not on the
            // button. The unfiltered row names what it covers ("All departments" /
            // "All roles") so a closed box is never blank and never a riddle; after
            // a choice the face is the catalogue's own wording, verbatim.
            DepartmentFilters.Clear();
            DepartmentFilters.Add(new CatalogRow { Code = "", DisplayName = "All departments" });
            // The catalogue's own wording, verbatim. The field name is the
            // label's job, not the value's: a row saying "Department: Cutting"
            // is the same word twice, in a form that only makes sense in a
            // toolbar and reads like a placeholder everywhere else.
            foreach (var department in departments) DepartmentFilters.Add(department);

            RoleFilters.Clear();
            RoleFilters.Add(new CatalogRow { Code = "", DisplayName = "All roles" });
            foreach (var role in roles) RoleFilters.Add(role);

            // Point the boxes at their "everything" rows as OBJECTS. Assigning the
            // backing fields directly keeps the current filter values (a raise of
            // SelectedDepartmentFilter here would re-run the query for nothing).
            _selectedDepartmentFilterItem = DepartmentFilters[0];
            _selectedRoleFilterItem = RoleFilters[0];
            OnPropertyChanged(nameof(SelectedDepartmentFilterItem));
            OnPropertyChanged(nameof(SelectedRoleFilterItem));
            OnPropertyChanged(nameof(SelectedDepartmentFilterText));
            OnPropertyChanged(nameof(SelectedRoleFilterText));
            OnPropertyChanged(nameof(ScopeNote));
            OnPropertyChanged(nameof(HasScopeNote));

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

            // Point the card's box at its row object once the catalogue exists.
            _bootstrapDepartmentItem = Departments.FirstOrDefault(d => d.Code == _bootstrapDepartment)
                ?? Departments.FirstOrDefault();
            OnPropertyChanged(nameof(BootstrapDepartmentItem));
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
                editor.ApplyPasswordState(user);
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
                // The same policy as every other password in the module: the
                // card cannot create an administrator nobody could sign in as.
                string? mismatch = string.IsNullOrEmpty(BootstrapPassword)
                    ? (IsCredentialSetup
                        ? "Choose a password for this administrator."
                        : "Choose a password for the first administrator.")
                    : UserPassword.Validate(BootstrapPassword)
                      ?? (string.Equals(BootstrapPassword, BootstrapPasswordConfirm, StringComparison.Ordinal)
                          ? null
                          : "The two passwords do not match.");

                if (mismatch != null)
                {
                    BootstrapError = mismatch;
                    return;
                }

                // One card, two writes: create the first administrator, or give the
                // existing administrator row the first password. Neither can be
                // reached twice — each closes as soon as it would stop being true.
                var result = IsCredentialSetup
                    ? await _firstRun.SetFirstPasswordAsync(BootstrapPassword).ConfigureAwait(true)
                    : await _firstRun
                        .RegisterAsync(BootstrapFullName, BootstrapDepartment, BootstrapPassword)
                        .ConfigureAwait(true);

                if (!result.Succeeded)
                {
                    string message = string.IsNullOrWhiteSpace(result.Message)
                        ? (IsCredentialSetup
                            ? "The first password could not be set."
                            : "The first administrator could not be registered.")
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
                BootstrapPassword = "";
                BootstrapPasswordConfirm = "";
                StatusMessage = string.IsNullOrWhiteSpace(result.Message)
                    ? (IsCredentialSetup
                        ? "The password is set. Sign in with it now."
                        : "The first administrator was created.")
                    : result.Message;
                bool askingToSignIn = IsCredentialSetup;
                await RefreshOperatorAsync().ConfigureAwait(true);
                await ReloadAsync().ConfigureAwait(true);

                // The first password was set from this page, which means the gate
                // was dismissed: there is no session yet, so ask the shell for the
                // sign-in screen instead of leaving the operator on a page that
                // cannot act.
                if (askingToSignIn) AuthenticationContext.RequestSignIn();
            }
            catch (Exception ex)
            {
                BootstrapError = (IsCredentialSetup
                        ? "The first password could not be set ("
                        : "The first administrator could not be registered (")
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
