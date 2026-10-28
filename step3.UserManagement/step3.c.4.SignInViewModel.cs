// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.c.4.SignInViewModel.cs
// MODULE: step3 — the sign-in gate's behaviour.
//
// Owner instruction (2026-10-27): every user has a password, and a normal user
// must not be able to act as an administrator. This is the screen where that
// starts: nothing in the application is reachable until a username and password
// have been verified against the directory.
//
// Two steps, one card
//   1. Sign in. The repository compares the password (PBKDF2, constant time) and
//      returns the row. Three refusal kinds matter to the operator: unknown
//      username, wrong password (with the attempts left), and locked/inactive —
//      each with its own sentence, never "invalid credentials".
//   2. Replace a temporary password. A password an administrator set is marked
//      "must change"; the same card asks for a new one and only then hands the
//      session over. The temporary password that was just typed is what proves the
//      right to replace it, so no weaker rule is introduced.
//
// The view model never stores a password beyond the two text boxes it is given,
// never logs one, and clears them as soon as the session is handed over.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Users;
using ProGlassAutomation.ThemeStudio;
using ProGlassAutomation.ViewModels.Inquiry;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.Users
{
    /// <summary>Sign-in gate: username + password, then an optional password change.</summary>
    public sealed class SignInViewModel : ViewModelBase
    {
        private readonly IUserRepository _repository;
        private readonly IUserSession? _session;

        private string _username = "";
        private string _password = "";
        private string _newPassword = "";
        private string _newPasswordConfirm = "";
        private string _errorMessage = "";
        private string _statusMessage = "";
        private bool _isBusy;
        private bool _isChangingPassword;
        private int _pendingUserId;
        private string _temporaryPassword = "";

        public SignInViewModel(IUserRepository repository, IUserSession? session = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _session = session;

            SignInCommand = new DelegateCommand(async () => await SignInAsync());
            ChangePasswordCommand = new DelegateCommand(async () => await ChangePasswordAsync());
        }

        /// <summary>Raised once a password has been verified and a new one set if required.</summary>
        public event EventHandler? SignedIn;

        public ICommand SignInCommand { get; }

        public ICommand ChangePasswordCommand { get; }

        /// <summary>Who the workstation itself reports, for orientation only.</summary>
        public string WorkstationAccount => CurrentOperator.Username;

        /// <summary>
        /// Neutral on purpose: the person signing in may have set their own password
        /// a moment ago (the first-password card) rather than been handed one, so
        /// "an administrator gave you" would be wrong on a fresh or upgraded
        /// installation.
        /// </summary>
        public string Explanation =>
            "Sign in with your username and password. "
            + "Nothing in the application is available before you do.";

        public string Username
        {
            get => _username;
            set { if (SetField(ref _username, value ?? "")) { ErrorMessage = ""; } }
        }

        public string Password
        {
            get => _password;
            set { if (SetField(ref _password, value ?? "")) ErrorMessage = ""; }
        }

        public string NewPassword
        {
            get => _newPassword;
            set { if (SetField(ref _newPassword, value ?? "")) ErrorMessage = ""; }
        }

        public string NewPasswordConfirm
        {
            get => _newPasswordConfirm;
            set { if (SetField(ref _newPasswordConfirm, value ?? "")) ErrorMessage = ""; }
        }

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

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetField(ref _isBusy, value)) OnPropertyChanged(nameof(IsIdle));
            }
        }

        public bool IsIdle => !IsBusy;

        /// <summary>True while the card is asking for a replacement password.</summary>
        public bool IsChangingPassword
        {
            get => _isChangingPassword;
            private set
            {
                if (!SetField(ref _isChangingPassword, value)) return;
                OnPropertyChanged(nameof(IsSigningIn));
            }
        }

        /// <summary>The username/password step; hidden while a new password is chosen.</summary>
        public bool IsSigningIn => !IsChangingPassword;

        public string PasswordPrompt => IsChangingPassword
            ? "This password was set by an administrator. Choose your own to continue."
            : Explanation;

        /// <summary>
        /// Puts a failure the person must know about on the card's own error
        /// surface, above the buttons where the retry is.
        /// </summary>
        /// <remarks>
        /// Round 30: the shell no longer collapses this gate when preparing it
        /// fails — that left the application with no session and no way in. The
        /// gate now stays on screen and this is how it says why it is there.
        /// </remarks>
        public void ReportProblem(string message)
        {
            IsBusy = false;                        // a stuck spinner would hide the retry
            StatusMessage = "";
            ErrorMessage = string.IsNullOrWhiteSpace(message)
                ? "The sign-in screen could not be prepared. Try again."
                : message;
        }

        /// <summary>
        /// Prepares the card. The directory is expected to be initialized already
        /// (the shell runs first-run setup first).
        /// </summary>
        public Task InitializeAsync(string? prefillUsername = null)
        {
            if (!string.IsNullOrWhiteSpace(prefillUsername)) Username = prefillUsername!.Trim();
            ErrorMessage = "";
            StatusMessage = "";
            return Task.CompletedTask;
        }

        private async Task SignInAsync()
        {
            if (IsBusy) return;

            IsBusy = true;
            ErrorMessage = "";
            StatusMessage = "";

            try
            {
                SignInResult result = await _repository
                    .SignInAsync(Username, Password)
                    .ConfigureAwait(true);

                if (!result.Succeeded)
                {
                    ErrorMessage = result.Message;
                    Password = "";
                    return;
                }

                if (result.MustChangePassword)
                {
                    _pendingUserId = result.User!.Id;
                    _temporaryPassword = Password;
                    Password = "";
                    NewPassword = "";
                    NewPasswordConfirm = "";
                    IsChangingPassword = true;
                    StatusMessage = "Choose a new password to finish signing in.";
                    return;
                }

                Complete(result.User!);
            }
            catch (Exception error)
            {
                ErrorMessage = "Sign-in failed: " + error.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ChangePasswordAsync()
        {
            if (IsBusy) return;

            string? policy = UserPassword.Validate(NewPassword);
            if (policy != null)
            {
                ErrorMessage = policy;
                return;
            }

            if (!string.Equals(NewPassword, NewPasswordConfirm, StringComparison.Ordinal))
            {
                ErrorMessage = "The two passwords do not match.";
                return;
            }

            IsBusy = true;
            ErrorMessage = "";

            try
            {
                UserSaveResult saved = await _repository
                    .ChangePasswordAsync(_pendingUserId, _temporaryPassword, NewPassword, Username)
                    .ConfigureAwait(true);

                if (!saved.Succeeded)
                {
                    ErrorMessage = saved.Message;
                    return;
                }

                User? user = await _repository.FindByUsernameAsync(Username).ConfigureAwait(true);
                if (user == null)
                {
                    ErrorMessage = "The account could not be re-read after the password change. Try signing in again.";
                    IsChangingPassword = false;
                    return;
                }

                _temporaryPassword = "";
                Complete(user);
            }
            catch (Exception error)
            {
                ErrorMessage = "The password could not be changed: " + error.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Complete(User user)
        {
            _session?.SignIn(user);

            // Never leave a typed password in memory longer than the session needs
            // it, and never hand the boxes back with a value in them.
            Password = "";
            NewPassword = "";
            NewPasswordConfirm = "";
            _temporaryPassword = "";
            IsChangingPassword = false;
            StatusMessage = "";
            ErrorMessage = "";

            SignedIn?.Invoke(this, EventArgs.Empty);
        }
    }
}
