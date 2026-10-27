// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.c.1.UserEditorViewModel.cs
// MODULE: step3 — user editor (add / edit).
//
// Presentation state only: field values, per-field validation messages,
// feedback, the audit footer and the save/cancel commands. All rules come from
// UserRules / UserAuthorization, all persistence from IUserRepository — which
// re-checks permission itself, so this view model is a convenience and never
// the only gate.
//
// Selection
//   • Department and Role bind to the catalogue ROWS (code + wording), not to
//     free text. A ComboBox with SelectedValuePath="Code" means the operator
//     cannot type a department that does not exist, and an added catalogue row
//     appears without touching this code.
//   • A new user starts as Estimator (the catalogue's IsDefault role) and in
//     Estimation. That default comes from the data, not from a literal here.
//
// No password fields exist on this screen. There is no credentialed login in
// the application to authenticate against, so a password box would be a
// fabricated feature that stores nothing verifiable.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Users;
using ProGlassAutomation.ThemeStudio;
using ProGlassAutomation.ViewModels.Inquiry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.Users
{
    /// <summary>Edits one user; new or existing.</summary>
    public sealed class UserEditorViewModel : ViewModelBase
    {
        private readonly IUserRepository _repository;
        private readonly string _operatorUsername;
        private readonly int _userId;
        private readonly bool _isNew;

        private string _fullName = "";
        private string _username = "";
        private string _department = UserCatalog.DefaultDepartment;
        private string _role = UserCatalog.DefaultRole;
        private bool _isActive = true;

        private string _fullNameError = "";
        private string _usernameError = "";
        private string _departmentError = "";
        private CatalogRow? _departmentItem;
        private CatalogRow? _roleItem;
        private string _roleError = "";

        private string _password = "";
        private string _passwordConfirm = "";
        private string _currentPassword = "";
        private string _passwordError = "";
        private string _currentPasswordError = "";
        private string _passwordStateText = "";
        private string _statusMessage = "";
        private string _errorMessage = "";
        private string _auditText = "";
        private bool _isBusy;

        public UserEditorViewModel(
            IUserRepository repository,
            UserDraft draft,
            IReadOnlyList<CatalogRow> departments,
            IReadOnlyList<CatalogRow> roles,
            string operatorUsername,
            int userId = 0)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _operatorUsername = operatorUsername ?? "";
            _userId = userId;
            _isNew = userId <= 0;

            if (draft != null)
            {
                _fullName = draft.FullName;
                _username = draft.Username;
                _department = draft.Department;
                _role = draft.Role;
                _isActive = draft.IsActive;

                // An existing row keeps its values. A new row falls back to the
                // catalogue's default role when the draft left it blank.
                if (_department.Length == 0) _department = DefaultDepartmentCode();
                if (_role.Length == 0) _role = DefaultRoleCode();
            }
            else
            {
                _department = DefaultDepartmentCode();
                _role = DefaultRoleCode();
            }

            // The pickers are bound by ROW, not by code string: a SelectedValue
            // resolved before ItemsSource is filled leaves the box blank, which is
            // the defect the owner reported on the register filters. The lists are
            // built here — after the draft has been read — so the stored code is
            // already known when the rows are matched, and an unknown code still
            // gets a row of its own instead of an empty face.
            Departments = BuildCatalogue(departments, _department);
            Roles = BuildCatalogue(roles, _role);
            _departmentItem = Departments.FirstOrDefault(row => row.Code == _department);
            _roleItem = Roles.FirstOrDefault(row => row.Code == _role);

            SaveCommand = new DelegateCommand(async () => await SaveAsync());
            CancelCommand = new DelegateCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
            DeactivateCommand = new DelegateCommand(async () => await SetActiveAsync(!IsActive));
        }

        /// <summary>Raised after a successful save, with the saved user.</summary>
        public event EventHandler<User>? Saved;

        /// <summary>Raised after the active/inactive flag changed, with the saved user.</summary>
        public event EventHandler<User>? StatusChanged;

        /// <summary>Raised when the operator leaves the editor without saving.</summary>
        public event EventHandler? Cancelled;

        public bool IsNew => _isNew;

        public int UserId => _userId;

        public IReadOnlyList<CatalogRow> Departments { get; private set; } = Array.Empty<CatalogRow>();

        public IReadOnlyList<CatalogRow> Roles { get; private set; } = Array.Empty<CatalogRow>();

        /// <summary>The department row the picker shows; the code is what is saved.</summary>
        public CatalogRow? DepartmentItem
        {
            get => _departmentItem;
            set
            {
                if (!SetField(ref _departmentItem, value) || value == null) return;
                Department = value.Code;
            }
        }

        /// <summary>The role row the picker shows; the code is what is saved.</summary>
        public CatalogRow? RoleItem
        {
            get => _roleItem;
            set
            {
                if (!SetField(ref _roleItem, value) || value == null) return;
                Role = value.Code;
            }
        }

        /// <summary>
        /// Copies the catalogue and guarantees the stored code has a row.
        /// </summary>
        /// <remarks>
        /// A code that is no longer in the catalogue (a hand-edited database, or a
        /// catalogue that lost a row) would otherwise leave the picker blank and
        /// silently rewrite the person's department on save. The row is labelled
        /// so the state is visible rather than silent.
        /// </remarks>
        private static IReadOnlyList<CatalogRow> BuildCatalogue(
            IReadOnlyList<CatalogRow>? catalogue, string selectedCode)
        {
            var rows = new List<CatalogRow>(catalogue ?? Array.Empty<CatalogRow>());
            if (rows.Count == 0 || rows.Any(row => row.Code == selectedCode)) return rows;

            rows.Add(new CatalogRow
            {
                Code = selectedCode,
                DisplayName = selectedCode + " (not in the catalogue)",
            });
            return rows;
        }

        public string Title => _isNew ? "Add user" : "Edit user";

        public string Subtitle => _isNew
            ? "Register a person who uses ProGlass. Full name and username are required."
            : $"Editing user #{_userId}. Changing the name or role keeps old records attributed to this person.";

        /// <summary>A new row has no status to change yet; the footer button waits until it is saved.</summary>
        public bool CanChangeStatus => !_isNew;

        // ── Fields ───────────────────────────────────────────────────────

        public string FullName
        {
            get => _fullName;
            set
            {
                if (!SetField(ref _fullName, value)) return;
                FullNameError = UserRules.ValidateFullName(value) ?? "";
                ClearSaveFeedback();
            }
        }

        /// <summary>
        /// The login identifier. On a single-station install this must match the
        /// Windows account name, because that is what the app reads at runtime.
        /// </summary>
        public string Username
        {
            get => _username;
            set
            {
                if (!SetField(ref _username, value)) return;
                UsernameError = UserRules.ValidateUsername(value) ?? "";
                ClearSaveFeedback();
            }
        }

        /// <summary>Department CODE (catalogue key), selected from the list.</summary>
        public string Department
        {
            get => _department;
            set
            {
                if (!SetField(ref _department, value)) return;
                DepartmentError = UserRules.ValidateDepartment(value) ?? "";
                OnPropertyChanged(nameof(DepartmentText));
                ClearSaveFeedback();
            }
        }

        /// <summary>Role CODE (catalogue key), selected from the list.</summary>
        public string Role
        {
            get => _role;
            set
            {
                if (!SetField(ref _role, value)) return;
                RoleError = UserRules.ValidateRole(value) ?? "";
                OnPropertyChanged(nameof(RoleText));
                ClearSaveFeedback();
            }
        }

        public string DepartmentText => UserCatalog.DepartmentDisplayName(Department);

        public string RoleText => UserCatalog.RoleDisplayName(Role);

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (!SetField(ref _isActive, value)) return;
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(DeactivateCommandText));
                ClearSaveFeedback();
            }
        }

        public string StatusText => IsActive
            ? "Active — may operate the application."
            : "Inactive — kept for history; cannot operate or be attributed.";

        public string DeactivateCommandText => IsActive ? "Deactivate" : "Re-activate";

        // ── Validation messages ──────────────────────────────────────────

        public string FullNameError
        {
            get => _fullNameError;
            private set { if (SetField(ref _fullNameError, value)) OnPropertyChanged(nameof(HasFullNameError)); }
        }

        public string UsernameError
        {
            get => _usernameError;
            private set { if (SetField(ref _usernameError, value)) OnPropertyChanged(nameof(HasUsernameError)); }
        }

        public string DepartmentError
        {
            get => _departmentError;
            private set { if (SetField(ref _departmentError, value)) OnPropertyChanged(nameof(HasDepartmentError)); }
        }

        public string RoleError
        {
            get => _roleError;
            private set { if (SetField(ref _roleError, value)) OnPropertyChanged(nameof(HasRoleError)); }
        }

        public bool HasFullNameError => FullNameError.Length > 0;
        public bool HasUsernameError => UsernameError.Length > 0;
        public bool HasDepartmentError => DepartmentError.Length > 0;
        public bool HasRoleError => RoleError.Length > 0;

        // ── Feedback ─────────────────────────────────────────────────────

        public string StatusMessage
        {
            get => _statusMessage;
            private set { if (SetField(ref _statusMessage, value)) OnPropertyChanged(nameof(HasStatusMessage)); }
        }

        public bool HasStatusMessage => StatusMessage.Length > 0;

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { if (SetField(ref _errorMessage, value)) OnPropertyChanged(nameof(HasErrorMessage)); }
        }

        public bool HasErrorMessage => ErrorMessage.Length > 0;

        /// <summary>"Added by … · Last changed by …" resolved from stored ids.</summary>
        public string AuditText
        {
            get => _auditText;
            private set { if (SetField(ref _auditText, value)) OnPropertyChanged(nameof(HasAuditText)); }
        }

        public bool HasAuditText => AuditText.Length > 0;

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetField(ref _isBusy, value);
        }

        public ICommand SaveCommand { get; }

        public ICommand CancelCommand { get; }

        public ICommand DeactivateCommand { get; }

        // ── Password ─────────────────────────────────────────────────────
        // Three cases, one section:
        //   new user            → a password is required (the administrator sets it)
        //   editing somebody else → reset is optional; the person must replace it
        //   editing yourself    → change it, and prove the current one

        /// <summary>True when the row being edited is the signed-in person.</summary>
        public bool IsSelf => !_isNew && AuthenticationContext.UserId == _userId;

        /// <summary>Only a person changing their own password must prove the old one.</summary>
        public bool ShowCurrentPassword => IsSelf;

        public string PasswordSectionTitle => _isNew
            ? "Password *"
            : IsSelf ? "Change my password" : "Reset this person's password";

        public string PasswordSectionHint => _isNew
            ? "The person signs in with this password the first time, then must choose their own."
            : IsSelf
                ? "Leave both boxes empty to keep the current password."
                : "Optional. A reset password is temporary: the person must choose their own at the next sign-in.";

        public string Password
        {
            get => _password;
            set { if (SetField(ref _password, value ?? "")) { PasswordError = ""; ClearSaveFeedback(); } }
        }

        public string PasswordConfirm
        {
            get => _passwordConfirm;
            set { if (SetField(ref _passwordConfirm, value ?? "")) { PasswordError = ""; ClearSaveFeedback(); } }
        }

        public string CurrentPassword
        {
            get => _currentPassword;
            set { if (SetField(ref _currentPassword, value ?? "")) { CurrentPasswordError = ""; ClearSaveFeedback(); } }
        }

        public string PasswordError
        {
            get => _passwordError;
            private set { if (SetField(ref _passwordError, value)) OnPropertyChanged(nameof(HasPasswordError)); }
        }

        public bool HasPasswordError => PasswordError.Length > 0;

        public string CurrentPasswordError
        {
            get => _currentPasswordError;
            private set { if (SetField(ref _currentPasswordError, value)) OnPropertyChanged(nameof(HasCurrentPasswordError)); }
        }

        public bool HasCurrentPasswordError => CurrentPasswordError.Length > 0;

        /// <summary>"Password set on …" / "No password yet" for an existing row.</summary>
        public string PasswordStateText
        {
            get => _passwordStateText;
            private set { if (SetField(ref _passwordStateText, value)) OnPropertyChanged(nameof(HasPasswordStateText)); }
        }

        public bool HasPasswordStateText => PasswordStateText.Length > 0;

        /// <summary>Fills the password state line for an existing row.</summary>
        public void ApplyPasswordState(User user)
        {
            if (user == null || _isNew)
            {
                PasswordStateText = "";
                return;
            }

            if (!user.HasPassword)
            {
                PasswordStateText = "No password yet — this person cannot sign in until one is set.";
                return;
            }

            string when = user.PasswordUpdatedAt.HasValue
                ? UserRules.FormatTimestamp(user.PasswordUpdatedAt.Value)
                : "an unknown date";
            PasswordStateText = user.MustChangePassword
                ? $"Temporary password set {when}; the person must replace it at the next sign-in."
                : $"Password set {when}.";
        }

        /// <summary>Current field values as an un-normalized draft.</summary>
        public UserDraft ToDraft() => new()
        {
            Id = _userId,
            FullName = FullName,
            Username = Username,
            Department = Department,
            Role = Role,
            IsActive = IsActive,
            Password = Password,
            PasswordConfirm = PasswordConfirm,
            CurrentPassword = CurrentPassword
        };

        // ── Load ─────────────────────────────────────────────────────────

        /// <summary>
        /// Renders the audit footer for an existing row: the stored ids are
        /// resolved to current names, so the footer follows a rename while the
        /// record itself still holds the id.
        /// </summary>
        public async Task LoadAsync(int? createdByUserId, int? updatedByUserId, DateTime createdAt, DateTime updatedAt)
        {
            try
            {
                string createdByName = createdByUserId.HasValue
                    ? await _repository.ResolveDisplayNameAsync(createdByUserId).ConfigureAwait(true)
                    : "";
                string updatedByName = updatedByUserId.HasValue
                    ? await _repository.ResolveDisplayNameAsync(updatedByUserId).ConfigureAwait(true)
                    : "";

                string created = createdByName.Length > 0
                    ? $"Added by {createdByName} · {UserRules.FormatTimestamp(createdAt)}"
                    : createdAt == default ? "" : $"Added {UserRules.FormatTimestamp(createdAt)}";

                string changed = updatedByName.Length > 0
                    ? $"Last changed by {updatedByName} · {UserRules.FormatTimestamp(updatedAt)}"
                    : updatedAt == default ? "" : $"Last changed {UserRules.FormatTimestamp(updatedAt)}";

                AuditText = string.Join("     ", new[] { created, changed }.Where(part => part.Length > 0));
            }
            catch (Exception)
            {
                // The footer is decoration; a failed lookup must not block editing.
                AuditText = "";
            }
        }

        // ── Commands ─────────────────────────────────────────────────────

        public async Task<bool> SaveAsync()
        {
            if (IsBusy) return false;
            ErrorMessage = "";
            StatusMessage = "";

            IReadOnlyDictionary<string, string> errors = UserRules.Validate(ToDraft());
            if (errors.Count > 0)
            {
                ApplyErrors(errors);
                ErrorMessage = "Correct the highlighted field(s) before saving.";
                return false;
            }

            // Changing your own password without knowing the current one would make
            // the check pointless, so it is refused before anything is written.
            if (ShowCurrentPassword && Password.Length > 0 && CurrentPassword.Length == 0)
            {
                CurrentPasswordError = "Enter your current password.";
                ErrorMessage = CurrentPasswordError;
                return false;
            }

            CurrentPasswordError = "";
            ClearErrors();

            IsBusy = true;
            try
            {
                UserSaveResult result = _isNew
                    ? await _repository.CreateAsync(ToDraft(), _operatorUsername).ConfigureAwait(true)
                    : await _repository.UpdateAsync(ToDraft(), _operatorUsername).ConfigureAwait(true);

                if (!result.Succeeded || result.User == null)
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(result.Message)
                        ? "The user could not be saved."
                        : result.Message;
                    return false;
                }

                // A new user's password travelled with the draft and was hashed by
                // CreateAsync. For an existing row it is a separate, deliberate act:
                // a reset for somebody else, a self-change for yourself.
                if (!_isNew && (Password.Length > 0 || PasswordConfirm.Length > 0))
                {
                    UserSaveResult passwordResult = IsSelf
                        ? await _repository.ChangePasswordAsync(
                            result.User.Id, CurrentPassword, Password, _operatorUsername).ConfigureAwait(true)
                        : await _repository.ResetPasswordAsync(
                            result.User.Id, Password, _operatorUsername).ConfigureAwait(true);

                    if (!passwordResult.Succeeded)
                    {
                        // The row is already saved; say exactly what did not happen
                        // so the operator does not believe the password changed.
                        ErrorMessage = "The user was saved, but the password was not changed: "
                            + passwordResult.Message;
                        PasswordError = passwordResult.Message;
                        return false;
                    }

                    Password = "";
                    PasswordConfirm = "";
                    CurrentPassword = "";
                }

                StatusMessage = _isNew
                    ? "User added. Give them the password you set, so they can sign in."
                    : "User updated.";
                Saved?.Invoke(this, result.User);
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = "The user could not be saved (" + ex.GetType().Name + "): " + ex.Message;
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task<bool> SetActiveAsync(bool isActive)
        {
            if (_isNew || IsBusy) return false;
            IsBusy = true;
            ErrorMessage = "";
            StatusMessage = "";
            try
            {
                UserSaveResult result = await _repository
                    .SetActiveAsync(_userId, isActive, _operatorUsername)
                    .ConfigureAwait(true);

                if (!result.Succeeded || result.User == null)
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(result.Message)
                        ? "The user's status could not be changed."
                        : result.Message;
                    return false;
                }

                IsActive = result.User.IsActive;
                StatusMessage = result.User.IsActive ? "User re-activated." : "User deactivated.";
                StatusChanged?.Invoke(this, result.User);
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = "The status could not be changed (" + ex.GetType().Name + "): " + ex.Message;
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private string DefaultRoleCode()
        {
            var entry = Roles.FirstOrDefault(r => r.IsDefault);
            return entry?.Code ?? UserCatalog.DefaultRole;
        }

        private string DefaultDepartmentCode()
        {
            bool known = Departments.Any(d => d.Code == UserCatalog.DefaultDepartment);
            if (known) return UserCatalog.DefaultDepartment;
            var first = Departments.FirstOrDefault();
            return first?.Code ?? UserCatalog.DefaultDepartment;
        }

        private void ApplyErrors(IReadOnlyDictionary<string, string> errors)
        {
            FullNameError = errors.TryGetValue(nameof(UserDraft.FullName), out string? name) ? name : "";
            UsernameError = errors.TryGetValue(nameof(UserDraft.Username), out string? username) ? username : "";
            DepartmentError = errors.TryGetValue(nameof(UserDraft.Department), out string? department) ? department : "";
            RoleError = errors.TryGetValue(nameof(UserDraft.Role), out string? role) ? role : "";
            PasswordError = errors.TryGetValue(nameof(UserDraft.Password), out string? password) ? password : "";
            if (PasswordError.Length == 0)
                PasswordError = errors.TryGetValue(nameof(UserDraft.PasswordConfirm), out string? confirm) ? confirm : "";
        }

        private void ClearErrors()
        {
            FullNameError = "";
            UsernameError = "";
            DepartmentError = "";
            RoleError = "";
            PasswordError = "";
            CurrentPasswordError = "";
        }

        private void ClearSaveFeedback()
        {
            if (ErrorMessage.Length > 0) ErrorMessage = "";
            if (StatusMessage.Length > 0) StatusMessage = "";
        }
    }
}
