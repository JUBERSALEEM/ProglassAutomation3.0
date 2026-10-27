// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.b.5.UserSession.cs
// MODULE: step3 — the running operator, as a directory row.
//
// What this is for
//   The application has no login screen and no password store, and this stage
//   does not add one. The one identity the desktop app already has is the
//   Windows workstation account, read by Inquiry/Services/Audit/CurrentOperator
//   and used today to stamp PreparedByName / UploadedBy / AcknowledgedBy.
//   UserSession turns that account into a directory row once, caches it, and
//   answers the three questions the rest of the app needs:
//     • who is operating (identity),
//     • may they administer users (authorization),
//     • may they author a record (attribution).
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
        /// <summary>The workstation account name the app will match on.</summary>
        string OperatorUsername { get; }

        /// <summary>Last resolved row, or null when nobody matched.</summary>
        User? CurrentUser { get; }

        /// <summary>True when the workstation account matches an active user row.</summary>
        bool IsIdentified { get; }

        /// <summary>True when the current user is an active Administrator.</summary>
        bool IsAdministrator { get; }

        /// <summary>Permission for the user-management screen, with wording.</summary>
        UserDecision ManagementDecision { get; }

        /// <summary>One-line description for the chrome ("Not identified" when unmatched).</summary>
        string StatusSummary { get; }

        /// <summary>Re-reads the directory. Pass true after the row may have changed.</summary>
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
        private User? _currentUser;
        private bool _loaded;

        public UserSession() : this(new UserRepository()) { }

        public UserSession(IUserRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public string OperatorUsername => CurrentOperator.Username;

        public User? CurrentUser => _currentUser;

        public bool IsIdentified => _currentUser != null && _currentUser.IsActive;

        public bool IsAdministrator =>
            IsIdentified && UserCatalog.IsAdministrator(_currentUser!.Role);

        public UserDecision ManagementDecision => UserAuthorization.CanManageUsers(_currentUser);

        public string StatusSummary
        {
            get
            {
                if (_currentUser == null)
                    return _loaded
                        ? $"{OperatorUsername} is not registered"
                        : "Checking operator…";
                return _currentUser.IsActive
                    ? $"{_currentUser.FullName} · {UserCatalog.RoleDisplayName(_currentUser.Role)}"
                    : $"{_currentUser.FullName} · inactive";
            }
        }

        public async Task<User?> RefreshAsync(CancellationToken ct = default)
        {
            _currentUser = await _repository.FindCurrentUserAsync(ct).ConfigureAwait(false);
            _loaded = true;
            return _currentUser;
        }

        public async Task<User?> GetCurrentUserAsync(
            bool forceRefresh = false, CancellationToken ct = default)
        {
            if (!_loaded || forceRefresh)
                return await RefreshAsync(ct).ConfigureAwait(false);
            return _currentUser;
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
