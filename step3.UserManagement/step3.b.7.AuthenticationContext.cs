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
