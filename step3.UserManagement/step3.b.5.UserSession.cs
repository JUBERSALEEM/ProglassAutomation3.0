// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.b.5.UserSession.cs
// MODULE: step3 — the running operator, as a directory row.
//
// What this is for
//   Since round 24 the identity is the account that **signed in with a password**
//   (owner instruction 2026-10-27). AuthenticationContext holds that row for the
//   life of the process; UserSession exposes it and answers the three questions
//   the rest of the app needs:
//     • who is operating (identity),
//     • may they administer users (authorization),
//     • may they author a record (attribution).
//
//   Before sign-in nothing is identified: CurrentUser is null, every write path
//   resolves no actor and is refused, and the shell shows the gate.
//
// Prepared By
//   CaptureAsync returns a PreparedByStamp: the stable user id, plus a snapshot
//   of the name and role as they were at that moment. Records persist the id
//   and the snapshot; they never persist a typed name. When a person is later
//   renamed or re-roled, the old record keeps the wording it was signed with,
//   while the id still points at the same person — historical attribution is
//   preserved, which is exactly what a live name lookup at render time would
//   destroy.
//
//   No business record type is wired to this in the current stage, because the
//   owner scoped step3 to User Management only (PI, TI, JO, DN, operations and
//   the Inquiry editor are explicitly out of scope). The stamp is the
//   integration point those stages will call; see docs/12 for the record.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Audit;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Users
{
    /// <summary>
    /// What a prepared record stores about its author: a stable id plus the
    /// wording that was true at signing time.
    /// </summary>
    public sealed class PreparedByStamp
    {
        public int UserId { get; init; }
        public string FullName { get; init; } = "";
        public string Role { get; init; } = "";
        public string Username { get; init; } = "";
        public DateTime CapturedAtUtc { get; init; }

        /// <summary>"Fatima Al Zaabi (Estimator)" for document footers.</summary>
        public string DisplayText =>
            string.IsNullOrWhiteSpace(FullName)
                ? ""
                : $"{FullName} ({UserCatalog.RoleDisplayName(Role)})";

        public bool IsEmpty => UserId <= 0;
    }

    /// <summary>The current operator, resolved once against the user directory.</summary>
    public interface IUserSession
    {
        /// <summary>The signed-in username, or "" before sign-in.</summary>
        string OperatorUsername { get; }

        /// <summary>Last resolved row, or null when nobody matched.</summary>
        User? CurrentUser { get; }

        /// <summary>True when somebody has signed in and that row is active.</summary>
        bool IsIdentified { get; }

        /// <summary>True when the current user is an active Administrator.</summary>
        bool IsAdministrator { get; }

        /// <summary>Permission for the user-management screen, with wording.</summary>
        UserDecision ManagementDecision { get; }

        /// <summary>One-line description for the chrome ("Not identified" when unmatched).</summary>
        string StatusSummary { get; }

        /// <summary>Adopts an authenticated row as the session identity.</summary>
        void SignIn(User user);

        /// <summary>Ends the session; the shell shows the gate again.</summary>
        void SignOut();

        /// <summary>Re-reads the signed-in row, so a rename or role change takes effect.</summary>
        Task<User?> RefreshAsync(CancellationToken ct = default);

        /// <summary>Cached row, reading it once per process unless <paramref name="forceRefresh"/>.</summary>
        Task<User?> GetCurrentUserAsync(bool forceRefresh = false, CancellationToken ct = default);

        /// <summary>True when the directory has no users yet (first-run bootstrap).</summary>
        Task<bool> IsDirectoryEmptyAsync(CancellationToken ct = default);

        /// <summary>
        /// A stamp for a record this operator is about to prepare, or null when
        /// the operator is not identified or not active. Callers must treat null
        /// as "cannot attribute" and refuse the save rather than invent a name.
        /// </summary>
        Task<PreparedByStamp?> CaptureStampAsync(CancellationToken ct = default);
    }

    /// <summary>Default implementation over <see cref="IUserRepository"/>.</summary>
    public sealed class UserSession : IUserSession
    {
        private readonly IUserRepository _repository;
        private bool _loaded;

        public UserSession() : this(new UserRepository()) { }

        public UserSession(IUserRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public string OperatorUsername => AuthenticationContext.Username;

        public User? CurrentUser => AuthenticationContext.Current;

        public bool IsIdentified => AuthenticationContext.Current is { IsActive: true };

        public bool IsAdministrator => IsIdentified && AuthenticationContext.IsAdministrator;

        public UserDecision ManagementDecision => UserAuthorization.CanManageUsers(CurrentUser);

        public string StatusSummary
        {
            get
            {
                User? user = CurrentUser;
                if (user == null) return "Not signed in";
                return user.IsActive
                    ? $"{user.FullName} · {UserCatalog.RoleDisplayName(user.Role)}"
                    : $"{user.FullName} · inactive";
            }
        }

        public void SignIn(User user)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            AuthenticationContext.SignIn(user);
            _loaded = true;
        }

        public void SignOut()
        {
            AuthenticationContext.SignOut();
            _loaded = false;
        }

        public async Task<User?> RefreshAsync(CancellationToken ct = default)
        {
            int? id = AuthenticationContext.UserId;
            if (id == null)
            {
                _loaded = true;
                return null;
            }

            // Reload the signed-in row so a rename, a role change or a
            // deactivation is reflected without signing out.
            User? fresh = await _repository.GetAsync(id.Value, ct).ConfigureAwait(false);
            if (fresh == null || !fresh.IsActive)
            {
                // The account vanished or was deactivated underneath the session.
                AuthenticationContext.SignOut();
                _loaded = true;
                return null;
            }

            AuthenticationContext.Refresh(fresh);
            _loaded = true;
            return fresh;
        }

        public async Task<User?> GetCurrentUserAsync(
            bool forceRefresh = false, CancellationToken ct = default)
        {
            if (!_loaded || forceRefresh)
                return await RefreshAsync(ct).ConfigureAwait(false);
            return AuthenticationContext.Current;
        }

        public async Task<bool> IsDirectoryEmptyAsync(CancellationToken ct = default)
            => await _repository.CountAsync(ct).ConfigureAwait(false) == 0;

        public async Task<PreparedByStamp?> CaptureStampAsync(CancellationToken ct = default)
        {
            User? user = await GetCurrentUserAsync(forceRefresh: true, ct).ConfigureAwait(false);
            if (!UserAuthorization.CanAttribute(user).IsAllowed) return null;

            return new PreparedByStamp
            {
                UserId = user!.Id,
                FullName = user.FullName,
                Role = user.Role,
                Username = user.Username,
                CapturedAtUtc = DateTime.UtcNow
            };
        }
    }
}
