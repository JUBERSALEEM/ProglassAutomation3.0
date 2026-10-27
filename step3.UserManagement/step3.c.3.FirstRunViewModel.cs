// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.c.3.FirstRunViewModel.cs
// MODULE: step3 — presentation for the startup first-run gate.
//
// The gate is shown by the shell before the operator can use the database, and
// only while the user directory is empty. Everything it decides comes from
// IFirstRunService (step3.b.6), so the shell, this gate and the Users page all
// share ONE implementation of "register the first administrator".
//
// Deliberately absent: a username field. The account that gets registered is the
// Windows account the process runs under, shown read-only. That is what makes
// "register me" actually work — the row created is the row this workstation will
// match on the next launch. A typed username can be anything, and a first
// administrator whose username does not match the workstation is exactly the
// deadlock this gate exists to prevent.
//
// Two states, one card
//   1. An empty directory: register the workstation account as the first
//      Administrator, with a password chosen on this card.
//   2. A directory that has users but no password anywhere (created before
//      migration 1015): give the workstation's own Administrator row its first
//      password. Without this the upgrade would greet every existing
//      installation with "ask an administrator" and no administrator able to
//      sign in — the same circular instruction, one version later.
// There is no "skip authorisation" switch in either state.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Users;
// DelegateCommand lives in the Theme Studio shell namespace (step1.b.4); the
// Users module uses it for its commands exactly as step2 does.
using ProGlassAutomation.ThemeStudio;
using ProGlassAutomation.ViewModels.Inquiry;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.Users
{
    /// <summary>Drives the startup gate that registers the first Administrator.</summary>
    public sealed class FirstRunViewModel : ViewModelBase
    {
        private readonly IFirstRunService _firstRun;

        private bool _isBusy;
        private bool _isVisible;
        private string _operatorUsername = "";
        private string _fullName = "";
        private string _selectedDepartment = "";
        private CatalogRow? _selectedDepartmentItem;
        private string _password = "";
        private string _passwordConfirm = "";
        private string _passwordError = "";
        private string _fullNameError = "";
        private string _errorMessage = "";
        private string _statusMessage = "";
        private string _explanation = "";
        private bool _isCredentialSetup;
        private bool _canClaimFirstPassword;
        private string _administratorUsername = "";

        public FirstRunViewModel(IFirstRunService firstRun)
        {
            _firstRun = firstRun ?? throw new ArgumentNullException(nameof(firstRun));
            RegisterCommand = new DelegateCommand(async () => await RegisterAsync());
            DismissCommand = new DelegateCommand(() => Dismissed?.Invoke(this, EventArgs.Empty));
        }

        /// <summary>Raised after the first Administrator exists and is recognised.</summary>
        public event EventHandler? Completed;

        /// <summary>Raised when the operator chooses to continue without setup.</summary>
        public event EventHandler? Dismissed;

        /// <summary>True while the gate should cover the application.</summary>
        public bool IsVisible
        {
            get => _isVisible;
            private set => SetField(ref _isVisible, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set { if (SetField(ref _isBusy, value)) OnPropertyChanged(nameof(CanRegister)); }
        }

        /// <summary>The Windows account this workstation runs under — read, not typed.</summary>
        public string OperatorUsername
        {
            get => _operatorUsername;
            private set => SetField(ref _operatorUsername, value);
        }

        public string Explanation
        {
            get => _explanation;
            private set => SetField(ref _explanation, value);
        }

        public ObservableCollection<CatalogRow> Departments { get; } = new();

        public string FullName
        {
            get => _fullName;
            set
            {
                if (!SetField(ref _fullName, value)) return;
                FullNameError = UserRules.ValidateFullName(value) ?? "";
                ErrorMessage = "";
                OnPropertyChanged(nameof(CanRegister));
            }
        }

        public string SelectedDepartment
        {
            get => _selectedDepartment;
            set => SetField(ref _selectedDepartment, value ?? "");
        }

        /// <summary>
        /// The department row the first-run picker shows.
        /// </summary>
        /// <remarks>
        /// The box is bound to the ROW, not to the code string: the catalogue is
        /// filled after the view binds, and a `SelectedValue` that cannot be
        /// resolved yet leaves the control with no face at all — the blank box the
        /// owner reported on the register filters. The row setter keeps the code in
        /// step, so everything downstream (validation, registration) is unchanged.
        /// </remarks>
        public CatalogRow? SelectedDepartmentItem
        {
            get => _selectedDepartmentItem;
            set
            {
                if (!SetField(ref _selectedDepartmentItem, value)) return;
                _selectedDepartment = value?.Code ?? "";
                OnPropertyChanged(nameof(SelectedDepartment));
            }
        }

        /// <summary>The password the first administrator chooses for themselves.</summary>
        public string Password
        {
            get => _password;
            set { if (SetField(ref _password, value ?? "")) { PasswordError = ""; ErrorMessage = ""; } }
        }

        /// <summary>Confirmation; never stored, only compared.</summary>
        public string PasswordConfirm
        {
            get => _passwordConfirm;
            set { if (SetField(ref _passwordConfirm, value ?? "")) { PasswordError = ""; ErrorMessage = ""; } }
        }

        public string PasswordError
        {
            get => _passwordError;
            private set { if (SetField(ref _passwordError, value)) OnPropertyChanged(nameof(HasPasswordError)); }
        }

        public bool HasPasswordError => PasswordError.Length > 0;

        public string FullNameError
        {
            get => _fullNameError;
            private set { if (SetField(ref _fullNameError, value)) OnPropertyChanged(nameof(HasFullNameError)); }
        }

        public bool HasFullNameError => FullNameError.Length > 0;

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { if (SetField(ref _errorMessage, value)) OnPropertyChanged(nameof(HasErrorMessage)); }
        }

        public bool HasErrorMessage => ErrorMessage.Length > 0;

        public string StatusMessage
        {
            get => _statusMessage;
            private set { if (SetField(ref _statusMessage, value)) OnPropertyChanged(nameof(HasStatusMessage)); }
        }

        public bool HasStatusMessage => StatusMessage.Length > 0;

        /// <summary>
        /// True when the card is setting a FIRST PASSWORD on an existing
        /// administrator row, rather than registering the first administrator.
        /// </summary>
        public bool IsCredentialSetup
        {
            get => _isCredentialSetup;
            private set
            {
                if (!SetField(ref _isCredentialSetup, value)) return;
                OnPropertyChanged(nameof(ShowIdentityFields));
                OnPropertyChanged(nameof(WindowAccountCaption));
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(SubmitLabel));
                OnPropertyChanged(nameof(SubmitAccessibleName));
            }
        }

        /// <summary>True when this workstation is the account that needs the password.</summary>
        public bool CanClaimFirstPassword
        {
            get => _canClaimFirstPassword;
            private set => SetField(ref _canClaimFirstPassword, value);
        }

        /// <summary>The administrator row waiting for its first password.</summary>
        public string AdministratorUsername
        {
            get => _administratorUsername;
            private set => SetField(ref _administratorUsername, value);
        }

        /// <summary>Name and department only exist when a row is being created.</summary>
        public bool ShowIdentityFields => !IsCredentialSetup;

        /// <summary>
        /// What the read-only Windows-account plate means in the current state.
        /// Saying "this account becomes the first Administrator" while it is
        /// actually only receiving a password would be untrue.
        /// </summary>
        public string WindowAccountCaption => IsCredentialSetup
            ? "This is the account the application will sign you in as. The password below is set on the "
              + "administrator row this account already owns — nothing about the person changes."
            : "This account becomes the first Administrator. The application recognises people by this name, "
              + "so it is read from Windows and cannot be typed.";

        public string Title => IsCredentialSetup
            ? "Set the administrator's password"
            : "Set up this installation";

        /// <summary>
        /// The primary button's wording. Sentence case; the theme owns casing.
        /// </summary>
        public string SubmitLabel => IsCredentialSetup
            ? "Set my password"
            : "Register me as administrator";

        public string SubmitAccessibleName => IsCredentialSetup
            ? "Set the first password for this administrator account"
            : "Register this account as the first administrator";

        /// <summary>The button stays usable while a name is missing: the form
        /// explains what is wrong instead of silently doing nothing.</summary>
        public bool CanRegister => !IsBusy;

        public ICommand RegisterCommand { get; }

        public ICommand DismissCommand { get; }

        /// <summary>
        /// Checks the directory and shows the gate when setup is required.
        /// Returns true when the gate is visible.
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            ErrorMessage = "";
            StatusMessage = "";

            FirstRunSnapshot snapshot;
            try
            {
                snapshot = await _firstRun.InspectAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // A failed probe must not lock the application: the Users page
                // reports the same problem in its own error surface.
                IsVisible = false;
                return false;
            }

            OperatorUsername = snapshot.OperatorUsername;

            Departments.Clear();
            foreach (CatalogRow department in snapshot.Departments) Departments.Add(department);
            if (Departments.Count > 0 &&
                !Departments.Any(d => d.Code == _selectedDepartment))
            {
                CatalogRow administration = Departments.FirstOrDefault(
                    d => d.Code == UserCatalog.AdministrationDepartment)
                    ?? Departments[0];
                _selectedDepartment = administration.Code;
                OnPropertyChanged(nameof(SelectedDepartment));
            }

            // Point the picker at its row once the catalogue exists.
            _selectedDepartmentItem = Departments.FirstOrDefault(d => d.Code == _selectedDepartment)
                ?? Departments.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedDepartmentItem));

            if (!snapshot.DirectoryAvailable)
            {
                IsVisible = false;
                return false;
            }

            IsCredentialSetup = snapshot.IsCredentialSetupRequired;
            CanClaimFirstPassword = snapshot.CanClaimFirstPassword;
            AdministratorUsername = snapshot.FirstAdministrator?.Username ?? "";

            IsVisible = snapshot.IsSetupRequired || snapshot.IsCredentialSetupRequired;
            Explanation = snapshot.IsSetupRequired
                ? "This installation has no users yet, so there is no administrator to ask. "
                  + "The Windows account shown below is the account this workstation is already "
                  + "running under — register it as the first Administrator and you can add the "
                  + "rest of the team immediately."
                : snapshot.IsCredentialSetupRequired
                    ? (snapshot.CanClaimFirstPassword
                        ? "Every account in this directory was created before passwords existed, so "
                          + "nobody can sign in yet — and there is therefore no administrator who could "
                          + "set one. Choose a password for the Administrator below; it is yours, so it "
                          + "is not temporary. This step is offered only while no account has a password "
                          + "and closes as soon as one does."
                        : "No account in this installation has a password yet, so nobody can sign in. "
                          + $"The Administrator waiting for its first password is \"{snapshot.FirstAdministrator?.Username}\". "
                          + "Run the application from that Windows account — the password can only be set "
                          + "on the machine that is signed in as it, never for somebody else.")
                    : "";
            // The second state is not a dead end: when this workstation is the
            // wrong account, the card names the account to run from.
            return IsVisible;
        }

        /// <summary>
        /// Registers the workstation account. A failure leaves the gate open with
        /// the reason and the form usable, so a retry is one click away and nothing
        /// is left half-created.
        /// </summary>
        public async Task<bool> RegisterAsync()
        {
            if (IsBusy) return false;

            ErrorMessage = "";
            StatusMessage = "";

            if (IsCredentialSetup) return await SetFirstPasswordAsync();

            string? nameError = UserRules.ValidateFullName(FullName);
            FullNameError = nameError ?? "";
            if (nameError != null)
            {
                ErrorMessage = nameError;
                return false;
            }

            // There is no round trip to set this later: the account is created with
            // the password chosen here, and it is the only way in.
            string? passwordError = UserPassword.Validate(Password);
            if (passwordError == null &&
                !string.Equals(Password, PasswordConfirm, StringComparison.Ordinal))
                passwordError = "The two passwords do not match.";

            PasswordError = passwordError ?? "";
            if (passwordError != null)
            {
                ErrorMessage = passwordError;
                return false;
            }

            IsBusy = true;
            try
            {
                UserSaveResult result = await _firstRun
                    .RegisterAsync(FullName, SelectedDepartment, Password)
                    .ConfigureAwait(true);

                if (!result.Succeeded || result.User == null)
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(result.Message)
                        ? "The first administrator could not be registered. Try again."
                        : result.Message;
                    return false;
                }

                // There is no automatic session: the shell asks for the password
                // once, so a session always starts with a verified sign-in.
                StatusMessage = string.IsNullOrWhiteSpace(result.Message)
                    ? "The first administrator was created. Sign in with the password you just chose."
                    : result.Message;

                // Only close the gate when the account is really recognised —
                // the service has already verified that against the directory.
                IsVisible = false;
                Completed?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = "The first administrator could not be registered ("
                    + ex.GetType().Name + "): " + ex.Message;
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// The upgrade card: an existing administrator row gets its first password.
        /// No name, no department — the row already exists, and this step must not
        /// be able to change who a person is.
        /// </summary>
        private async Task<bool> SetFirstPasswordAsync()
        {
            if (!CanClaimFirstPassword)
            {
                ErrorMessage = string.IsNullOrEmpty(AdministratorUsername)
                    ? "This directory has no active Administrator, so there is nobody the first password could belong to."
                    : $"Run the application from the Windows account \"{AdministratorUsername}\" — the first password can only be set by the person it belongs to.";
                return false;
            }

            string? passwordError = UserPassword.Validate(Password);
            if (passwordError == null &&
                !string.Equals(Password, PasswordConfirm, StringComparison.Ordinal))
                passwordError = "The two passwords do not match.";

            PasswordError = passwordError ?? "";
            if (passwordError != null)
            {
                ErrorMessage = passwordError;
                return false;
            }

            IsBusy = true;
            try
            {
                UserSaveResult result = await _firstRun
                    .SetFirstPasswordAsync(Password)
                    .ConfigureAwait(true);

                if (!result.Succeeded || result.User == null)
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(result.Message)
                        ? "The first password could not be set. Try again."
                        : result.Message;
                    return false;
                }

                StatusMessage = string.IsNullOrWhiteSpace(result.Message)
                    ? "The password is set. Sign in with it now."
                    : result.Message;

                // Never hand the boxes back with a password in them.
                Password = "";
                PasswordConfirm = "";
                IsVisible = false;
                Completed?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = "The first password could not be set ("
                    + ex.GetType().Name + "): " + ex.Message;
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
