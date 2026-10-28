// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.b.7.AuthenticationContext.cs
// MODULE: step3 — who is signed in, for the life of the process.
//
// Why a process-wide holder
//   The repository resolves the operating user for every write (see
//   CurrentOperatorUsername) and the session answers "may this person administer
//   users". Both must agree, and both are called from view models that do not
//   share a lifetime. One holder, written only by the sign-in gate and cleared by
//   sign-out, keeps them in step.
//
// Rules
//   • Nothing is signed in until a password has been verified. `Current` is null
//     before that, so every write path resolves no actor and refuses.
//   • Sign-out clears the holder and raises SignedOut, which the shell listens to
//     so the gate comes back the instant the session ends.
//   • The holder never stores a password or a hash — only the directory row.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Users;
using System;

namespace ProGlassAutomation.Services.Users
{
    /// <summary>The signed-in user, process-wide.</summary>
    public static class AuthenticationContext
    {
        private static readonly object Gate = new();

        private static User? _current;

        /// <summary>Raised after the session ends, so the shell can show the gate.</summary>
        public static event EventHandler? SignedOut;

        /// <summary>
        /// Raised after a password has been verified and the session exists.
        /// </summary>
        /// <remarks>
        /// The shell listens so the identity in the banner is never stale, and so
        /// this device can remember the sign-in (step3.b.8) in exactly one place —
        /// whether the session came from the gate or from the remembered record.
        /// </remarks>
        public static event EventHandler? SignedIn;

        /// <summary>
        /// Raised when something needs the gate shown while nobody is signed in.
        /// </summary>
        /// <remarks>
        /// <see cref="SignedOut"/> cannot carry this: it only fires when a session
        /// actually ended. The case that needs this one is the first password on an
        /// upgraded directory being set from the Users page — the operator is not
        /// signed in yet, so "sign out" would be a lie, and without the gate the
        /// page would sit there with no way to become a session.
        /// </remarks>
        public static event EventHandler? SignInRequested;

        /// <summary>The signed-in user, or null when nobody is signed in.</summary>
        public static User? Current
        {
            get { lock (Gate) return _current; }
        }

        public static bool IsSignedIn => Current != null;

        public static int? UserId => Current?.Id;

        /// <summary>The signed-in username, or "" — the key every write uses.</summary>
        public static string Username
        {
            get { lock (Gate) return _current?.Username ?? ""; }
        }

        public static bool IsAdministrator =>
            Current is { } user && UserCatalog.IsAdministrator(user.Role);

        /// <summary>Records the user whose password was just verified.</summary>
        public static void SignIn(User user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            lock (Gate) _current = user;

            // Outside the lock: a handler must never be able to deadlock the
            // context by reading it back (the shell's does exactly that).
            SignedIn?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Refreshes the cached row after it changed (rename, role change).</summary>
        public static void Refresh(User user)
        {
            if (user == null) return;
            lock (Gate)
            {
                if (_current != null && _current.Id == user.Id) _current = user;
            }
        }

        /// <summary>Asks the shell to show its sign-in gate.</summary>
        public static void RequestSignIn() => SignInRequested?.Invoke(null, EventArgs.Empty);

        /// <summary>Ends the session and tells the shell.</summary>
        public static void SignOut()
        {
            bool had;
            lock (Gate)
            {
                had = _current != null;
                _current = null;
            }

            if (had) SignedOut?.Invoke(null, EventArgs.Empty);
        }
    }
}
