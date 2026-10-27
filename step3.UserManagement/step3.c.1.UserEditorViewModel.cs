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
        private string _roleError = "";

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

            Departments = departments ?? Array.Empty<CatalogRow>();
            Roles = roles ?? Array.Empty<CatalogRow>();

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

        public IReadOnlyList<CatalogRow> Departments { get; }

        public IReadOnlyList<CatalogRow> Roles { get; }

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

        /// <summary>Current field values as an un-normalized draft.</summary>
        public UserDraft ToDraft() => new()
        {
            Id = _userId,
            FullName = FullName,
            Username = Username,
            Department = Department,
            Role = Role,
            IsActive = IsActive
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

                StatusMessage = _isNew ? "User added." : "User updated.";
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
        }

        private void ClearErrors()
        {
            FullNameError = "";
            UsernameError = "";
            DepartmentError = "";
            RoleError = "";
        }

        private void ClearSaveFeedback()
        {
            if (ErrorMessage.Length > 0) ErrorMessage = "";
            if (StatusMessage.Length > 0) StatusMessage = "";
        }
    }
}
