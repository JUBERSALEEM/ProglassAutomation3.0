// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.b.9.RememberedSignIn.cs
// MODULE: step3 — turning this device's remembered sign-in back into a session.
//
// The rule (owner instruction, round 32)
//   "Remember the last login until Sign out." So a launch asks for a password
//   only when this device has nothing trustworthy to resume — and the word that
//   matters is TRUSTWORTHY. Resuming is a decision, not a file read, so every
//   check lives here, in one place, in one order:
//
//     1. the file names a row            (DeviceSessionStore.Load)
//     2. it was written for THIS Windows account
//     3. the row still exists            (re-read from the directory, never cached)
//     4. the row is still active
//     5. the row still has a password
//     6. the row is not waiting on a password change
//     7. its password stamp is the one that was remembered
//
//   Any failure clears the record and returns "sign in", never an error screen:
//   a stale remembered session is not a problem to solve, it is simply no
//   session. Only a genuine read fault is reported, and even then the gate is
//   the answer.
//
// What it does NOT do
//   It does not verify a password — there is none to verify, which is exactly
//   why the checks above are strict and why the record is cleared the moment it
//   stops being true. It does not invent an operator: the row is re-read, so
//   role, name and status are the directory's current ones.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Audit;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Users
{
    /// <summary>Outcome of resuming this device's remembered sign-in.</summary>
    public sealed class RememberedSignInResult
    {
        /// <summary>True only when a session was actually established.</summary>
        public bool Restored { get; init; }

        /// <summary>The row that is now signed in, when <see cref="Restored"/>.</summary>
        public User? User { get; init; }

        /// <summary>
        /// Why the sign-in was not resumed, in the operator's words. Empty when
        /// there was nothing remembered (the ordinary first run of a shared
        /// workstation is not a failure to report).
        /// </summary>
        public string Message { get; init; } = "";

        /// <summary>The username to prefill on the gate, when one is known.</summary>
        public string Username { get; init; } = "";

        public static RememberedSignInResult None(string message = "", string username = "") =>
            new() { Restored = false, Message = message, Username = username };

        public static RememberedSignInResult Ok(User user) =>
            new() { Restored = true, User = user, Username = user.Username };
    }

    /// <summary>Resumes the session this device remembers, or says why it will not.</summary>
    public static class RememberedSignIn
    {
        /// <summary>
        /// Signs this device's remembered user back in when every check in the
        /// file header passes, and clears the record when one does not.
        /// </summary>
        public static async Task<RememberedSignInResult> TryRestoreAsync(
            IUserRepository repository, CancellationToken ct = default)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));

            // Somebody is already operating: never overwrite a live session with
            // the one on disk.
            if (AuthenticationContext.IsSignedIn)
                return RememberedSignInResult.Ok(AuthenticationContext.Current!);

            RememberedSession? remembered = DeviceSessionStore.Load();
            if (remembered == null)
                return RememberedSignInResult.None(DeviceSessionStore.LastProblem);

            // 2. This device remembers per Windows account. A different person at
            //    the same keyboard is a different session, not an inherited one.
            string windowsAccount = CurrentOperator.Username;
            if (!string.Equals(remembered.WindowsAccount, windowsAccount, StringComparison.OrdinalIgnoreCase))
            {
                DeviceSessionStore.Forget();
                return RememberedSignInResult.None(
                    "The last sign-in on this device was made under a different Windows account, "
                    + "so it has been forgotten. Sign in to continue.", remembered.Username);
            }

            // 3. Re-read the row. A remembered id is a pointer, never a snapshot of
            //    authority: a role change or a deactivation must take effect at the
            //    next launch, not at the next save.
            User? user = await repository.GetAsync(remembered.UserId, ct).ConfigureAwait(false);

            // 4–6. The row must still be able to hold a session at all.
            if (user == null || !user.IsActive || !user.HasPassword || user.MustChangePassword)
            {
                DeviceSessionStore.Forget();
                return RememberedSignInResult.None(
                    user == null
                        ? "The account this device remembered is no longer in the directory. Sign in to continue."
                        : "The account this device remembered can no longer hold a session "
                          + "(it is inactive, has no password, or must choose a new one). Sign in to continue.",
                    remembered.Username);
            }

            // 7. A password set since then — by the person or by an administrator
            //    resetting it — invalidates the record. This is what makes a reset
            //    mean something on a workstation that was left signed in.
            if (!SameStamp(remembered.PasswordUpdatedAtUtc, user.PasswordUpdatedAt))
            {
                DeviceSessionStore.Forget();
                return RememberedSignInResult.None(
                    "The password for this account changed since this device remembered the sign-in. "
                    + "Sign in with the new password.", user.Username);
            }

            AuthenticationContext.SignIn(user);
            return RememberedSignInResult.Ok(user);
        }

        /// <summary>
        /// Compares the stored password stamp with the row's, with one second of
        /// slack: SQLite keeps the value as text, so the last fraction of a second
        /// is not worth locking an operator out over. A null on either side is a
        /// different story — a row with no password stamp is a row whose password
        /// state this record cannot vouch for.
        /// </summary>
        private static bool SameStamp(DateTime? remembered, DateTime? stored)
        {
            if (remembered == null && stored == null) return true;
            if (remembered == null || stored == null) return false;
            return Math.Abs((remembered.Value - stored.Value).TotalSeconds) < 1;
        }
    }
}
