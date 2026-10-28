// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.c.5.ShellSessionViewModel.cs
// MODULE: step3 — the session control that lives in the shell's banner.
//
// Why this exists (owner report, round 30)
//   The sign-in gate was the ONLY way into a session, and it lived entirely in
//   MainWindow's code-behind. When its preparation failed it collapsed itself
//   and the shell was left with nobody signed in and no control anywhere that
//   offered the step again: every page read-only until the application was
//   restarted. The Users page was not a way back either — its sign-out button
//   only ends a session, it cannot start one.
//
//   So the shell now shows the session itself, in the banner, on every page:
//     • who is operating (name, with the role and the meaning in the tooltip and
//       the accessible name — the same rule the register cards follow);
//     • one action: "Sign in" when nobody is, "Sign out" when somebody is;
//     • a short marker when the gate could not be prepared, carrying the reason
//       in its tooltip, so a failure is visible instead of silent.
//
// What it is not
//   It is not an authority. It reads AuthenticationContext and nothing else, it
//   never writes a session, and the only thing its command does is ask the
//   shell to show the gate (or to end the session, which the shell already
//   listens for). Signing in still requires a verified password in
//   step3.c.4, and the write paths still resolve the actor from the context.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Users;
using ProGlassAutomation.ThemeStudio;
using ProGlassAutomation.ViewModels.Inquiry;
using System;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.Users
{
    /// <summary>
    /// Identity and the one session action, for the shell banner.
    /// </summary>
    public sealed class ShellSessionViewModel : ViewModelBase
    {
        private string _problemDetail = "";

        public ShellSessionViewModel()
        {
            ActionCommand = new DelegateCommand(() => ActionRequested?.Invoke(this, EventArgs.Empty));
        }

        /// <summary>
        /// Raised when the banner's button is pressed. The shell decides what that
        /// means: show the gate when nobody is signed in, end the session when
        /// somebody is. Keeping the decision out of here means there is one place
        /// that knows about gates.
        /// </summary>
        public event EventHandler? ActionRequested;

        public ICommand ActionCommand { get; }

        public bool IsSignedIn => AuthenticationContext.IsSignedIn;

        /// <summary>
        /// The person's name, or a plain statement that nobody is signed in.
        /// </summary>
        public string SessionLabel
        {
            get
            {
                User? user = AuthenticationContext.Current;
                if (user == null) return "Not signed in";

                return string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;
            }
        }

        /// <summary>
        /// The sentence behind the label: role, and what the state means for the
        /// page in front of the operator. This is also the accessible name, so the
        /// information is not carried by position or colour alone.
        /// </summary>
        public string SessionTooltip
        {
            get
            {
                User? user = AuthenticationContext.Current;
                if (user == null)
                {
                    return "Nobody is signed in. Pages that write data are read-only until someone does, "
                         + "and saving is refused at the repository, not only on screen.";
                }

                string role = UserCatalog.RoleDisplayName(user.Role);
                return user.IsActive
                    ? $"Signed in as {user.FullName} · {role}. Sign out to hand the workstation to someone else."
                    : $"Signed in as {user.FullName} · {role}, but that account is now inactive.";
            }
        }

        /// <summary>"Sign in" when the way in is closed, "Sign out" when it is open.</summary>
        public string ActionLabel => IsSignedIn ? "Sign out" : "Sign in";

        public string ActionTooltip => IsSignedIn
            ? "End this session and return to the sign-in screen"
            : "Sign in with your username and password";

        /// <summary>
        /// The button's accessible name. It says who the action is for, so a screen
        /// reader never announces a bare "Sign out" without its subject.
        /// </summary>
        public string ActionAccessibleName => IsSignedIn
            ? $"Sign out of the session for {SessionLabel}"
            : "Sign in";

        /// <summary>
        /// Why the gate is not in front of the operator, or "" when it is. The
        /// banner shows a short marker and carries this sentence in its tooltip and
        /// accessible name; the card, when it can be shown at all, repeats it in
        /// full. A collapsed gate with nothing said is the defect this replaces.
        /// </summary>
        public string ProblemDetail
        {
            get => _problemDetail;
            private set
            {
                if (!SetField(ref _problemDetail, value ?? "")) return;
                OnPropertyChanged(nameof(HasProblem));
            }
        }

        public bool HasProblem => ProblemDetail.Length > 0;

        /// <summary>Re-reads the process-wide session. Call after anything changes it.</summary>
        public void Refresh()
        {
            OnPropertyChanged(nameof(IsSignedIn));
            OnPropertyChanged(nameof(SessionLabel));
            OnPropertyChanged(nameof(SessionTooltip));
            OnPropertyChanged(nameof(ActionLabel));
            OnPropertyChanged(nameof(ActionTooltip));
            OnPropertyChanged(nameof(ActionAccessibleName));
        }

        public void ReportProblem(string message)
            => ProblemDetail = string.IsNullOrWhiteSpace(message)
                ? "The sign-in screen could not be prepared."
                : message;

        public void ClearProblem() => ProblemDetail = "";
    }
}
