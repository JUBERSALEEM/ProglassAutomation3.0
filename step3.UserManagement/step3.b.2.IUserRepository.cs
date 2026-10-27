// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.b.2.IUserRepository.cs
// MODULE: step3 — user persistence contract.
//
// Two deliberate choices in this contract:
//
//   1. Every write takes the operator's workstation username, not a caller-
//      supplied User object. The repository resolves that username to a row
//      INSIDE the same transaction as the write, so permission is evaluated
//      against committed state. A view model cannot grant itself access by
//      handing over a hand-made User with Role = "ADMINISTRATOR", and a user
//      deactivated a moment ago cannot slip a stale session through.
//
//   2. Reads return projections. The register never tracks entities, and the
//      row projection carries the display wording (status, role · department,
//      last-changed text) so the view model has no formatting logic to drift.
//
// Nothing here deletes a user: deactivation is the only lifecycle transition,
// which is what keeps historical attribution pointing at a real row.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Users;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Users
{
    /// <summary>Register filter for the user list.</summary>
    public sealed class UserQuery
    {
        /// <summary>Free text matched against full name, username, role and department.</summary>
        public string Search { get; init; } = "";

        /// <summary>true = active only, false = inactive only, null = both.</summary>
        public bool? IsActive { get; init; }

        /// <summary>Department code filter; empty means every department.</summary>
        public string Department { get; init; } = "";

        /// <summary>Role code filter; empty means every role.</summary>
        public string Role { get; init; } = "";

        /// <summary>Safety cap; this is a staff directory, not a ledger.</summary>
        public int MaxRows { get; init; } = 2000;

        /// <summary>
        /// Restricts the result to one person's own row.
        /// </summary>
        /// <remarks>
        /// Set by the register for a signed-in non-administrator: a staff directory
        /// is other people's data. Null — the administrator's view — reads the whole
        /// directory. The write path is unaffected: permission there comes from
        /// <see cref="UserAuthorization"/> and is checked in the repository itself.
        /// </remarks>
        public int? ScopeToUserId { get; init; }
    }

    /// <summary>Register row projection.</summary>
    public sealed class UserListRow
    {
        public int Id { get; init; }
        public string FullName { get; init; } = "";
        public string Username { get; init; } = "";
        public string Department { get; init; } = "";
        public string Role { get; init; } = "";
        public bool IsActive { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }

        /// <summary>StatusBadge tag vocabulary: Success for active, neutral otherwise.</summary>
        public string StatusTag => IsActive ? "Success" : "";

        public string StatusText => IsActive ? "Active" : "Inactive";

        /// <summary>Role · Department, using catalogue wording.</summary>
        public string RoleDepartmentText =>
            $"{UserCatalog.RoleDisplayName(Role)} · {UserCatalog.DepartmentDisplayName(Department)}";

        public string DepartmentText => UserCatalog.DepartmentDisplayName(Department);

        public string RoleText => UserCatalog.RoleDisplayName(Role);

        public string ToggleLabel => IsActive ? "Deactivate user" : "Re-activate user";

        public string UpdatedText => UpdatedAt == default
            ? "—"
            : UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    }

    /// <summary>One catalogue row as stored (so an added department appears without a rebuild).</summary>
    public sealed class CatalogRow
    {
        public string Code { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public int SortOrder { get; init; }
        public bool IsDefault { get; init; }

        public override string ToString() => DisplayName;
    }

    /// <summary>Outcome of a create/update/activation call.</summary>
    public sealed class UserSaveResult
    {
        public bool Succeeded { get; init; }
        public User? User { get; init; }

        /// <summary>Operator-facing message; empty on success.</summary>
        public string Message { get; init; } = "";

        /// <summary>Permission/validation code, for the view model to react to.</summary>
        public UserDecisionCode Code { get; init; } = UserDecisionCode.Allowed;

        public static UserSaveResult Ok(User user, string message = "") =>
            new() { Succeeded = true, User = user, Message = message };

        public static UserSaveResult Fail(string message, UserDecisionCode code = UserDecisionCode.InvalidTarget) =>
            new() { Succeeded = false, Message = message, Code = code };
    }

    /// <summary>Why a sign-in ended the way it did.</summary>
    public enum SignInCode
    {
        Success,

        /// <summary>No row carries that username.</summary>
        UnknownUser,

        /// <summary>The row exists but has no credential yet (created before 1015).</summary>
        NoPassword,

        /// <summary>The password did not match.</summary>
        WrongPassword,

        /// <summary>The row was deactivated.</summary>
        Inactive,

        /// <summary>Too many failures: sign-in is refused until the lock expires.</summary>
        Locked
    }

    /// <summary>Outcome of a sign-in attempt, with wording for the gate.</summary>
    public sealed class SignInResult
    {
        public bool Succeeded { get; init; }

        public SignInCode Code { get; init; } = SignInCode.UnknownUser;

        public string Message { get; init; } = "";

        /// <summary>The authenticated row; null unless <see cref="Succeeded"/>.</summary>
        public User? User { get; init; }

        /// <summary>Attempts left before the account locks (0 when not applicable).</summary>
        public int AttemptsLeft { get; init; }

        /// <summary>True when the password works but must be replaced now.</summary>
        public bool MustChangePassword { get; init; }

        public static SignInResult Ok(User user, bool mustChangePassword) => new()
        {
            Succeeded = true,
            Code = SignInCode.Success,
            User = user,
            MustChangePassword = mustChangePassword
        };

        public static SignInResult Fail(SignInCode code, string message, int attemptsLeft = 0) => new()
        {
            Succeeded = false,
            Code = code,
            Message = message,
            AttemptsLeft = attemptsLeft
        };
    }

    /// <summary>User directory persistence.</summary>
    public interface IUserRepository
    {
        /// <summary>
        /// Ensures the ERP database file exists at the current schema and that the
        /// step3 tables (migration 1013) are present. Safe to call more than once.
        /// </summary>
        Task InitializeAsync(CancellationToken ct = default);

        /// <summary>Register rows: active first, then by full name.</summary>
        Task<IReadOnlyList<UserListRow>> QueryAsync(UserQuery query, CancellationToken ct = default);

        /// <summary>One user by stable id, or null.</summary>
        Task<User?> GetAsync(int id, CancellationToken ct = default);

        /// <summary>
        /// Display name for a stored user id, or "" when the id is null or no
        /// longer resolves. Used to render audit pointers (which are ids) as
        /// names at display time instead of storing a name that would go stale.
        /// </summary>
        Task<string> ResolveDisplayNameAsync(int? userId, CancellationToken ct = default);

        /// <summary>
        /// The user row whose username matches the workstation account, or null.
        /// This is the ONE place the running operator becomes a directory row.
        /// </summary>
        Task<User?> FindCurrentUserAsync(CancellationToken ct = default);

        /// <summary>Total users, needed to tell "empty directory" from "no match".</summary>
        Task<int> CountAsync(CancellationToken ct = default);

        /// <summary>
        /// Rows that actually have a password. Zero means nobody in this
        /// installation can sign in yet — the state every directory created before
        /// migration 1015 starts in.
        /// </summary>
        /// <remarks>
        /// This is what separates "this one person has no password yet"
        /// (an administrator sets it on the Users page) from "nobody has one"
        /// (there is no administrator who could sign in to set anything, so the
        /// first password must be claimable at the machine itself).
        /// </remarks>
        Task<int> CountCredentialedUsersAsync(CancellationToken ct = default);

        /// <summary>
        /// The first active Administrator, in id order — the row that can be given
        /// the first password, and the name to tell the operator when this
        /// workstation does not match it.
        /// </summary>
        Task<User?> FirstActiveAdministratorAsync(CancellationToken ct = default);

        /// <summary>One row by username (case/width-insensitive), or null.</summary>
        Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default);

        /// <summary>
        /// Checks a username and password against the directory.
        /// </summary>
        /// <remarks>
        /// This is the only place a password is compared. It refuses an inactive
        /// row, refuses a row with no credential, and locks the account for a short
        /// time after repeated failures so a workstation cannot be used to guess.
        /// The returned row is the identity the session adopts.
        /// </remarks>
        Task<SignInResult> SignInAsync(
            string username, string password, CancellationToken ct = default);

        /// <summary>
        /// Sets a new password for the signed-in user, who must supply the current
        /// one. Their own change, so it clears the must-change flag.
        /// </summary>
        Task<UserSaveResult> ChangePasswordAsync(
            int userId, string currentPassword, string newPassword,
            string operatorUsername, CancellationToken ct = default);

        /// <summary>
        /// Administrator reset: sets somebody's password without knowing the old
        /// one, and marks it temporary so the person must choose their own at the
        /// next sign-in. Refused for anybody who is not an active Administrator.
        /// </summary>
        Task<UserSaveResult> ResetPasswordAsync(
            int userId, string newPassword, string operatorUsername, CancellationToken ct = default);

        /// <summary>
        /// True when the directory has no users at all, so the application still
        /// needs its first administrator. The first-run gate asks this before it
        /// shows anything, and the write path re-checks it inside its transaction.
        /// </summary>
        Task<bool> IsFirstRunRequiredAsync(CancellationToken ct = default);

        /// <summary>Departments in display order.</summary>
        Task<IReadOnlyList<CatalogRow>> GetDepartmentsAsync(CancellationToken ct = default);

        /// <summary>Roles in display order.</summary>
        Task<IReadOnlyList<CatalogRow>> GetRolesAsync(CancellationToken ct = default);

        /// <summary>
        /// Inserts a user. Refused unless <paramref name="operatorUsername"/>
        /// resolves to an active Administrator.
        /// </summary>
        Task<UserSaveResult> CreateAsync(UserDraft draft, string operatorUsername, CancellationToken ct = default);

        /// <summary>
        /// Updates a user. Never changes the id. Refused unless the operator is an
        /// active Administrator, and refused when the change would remove the last
        /// active Administrator.
        /// </summary>
        Task<UserSaveResult> UpdateAsync(UserDraft draft, string operatorUsername, CancellationToken ct = default);

        /// <summary>Activates or deactivates a user; same guards as <see cref="UpdateAsync"/>.</summary>
        Task<UserSaveResult> SetActiveAsync(int id, bool isActive, string operatorUsername, CancellationToken ct = default);

        /// <summary>
        /// First-run bootstrap: registers the workstation account itself as the
        /// first Administrator. Only possible while the directory is empty, so it
        /// cannot be used to escalate later.
        /// </summary>
        Task<UserSaveResult> RegisterFirstAdministratorAsync(
            string fullName, string department, string password,
            string operatorUsername, CancellationToken ct = default);

        /// <summary>
        /// Upgrade path: gives the workstation's own Administrator row its first
        /// password, and closes the moment ANY row has one.
        /// </summary>
        /// <remarks>
        /// Migration 1015 deliberately kept every existing row, which left a
        /// directory where nobody could sign in and no administrator existed who
        /// could set a password — "ask an administrator" with no administrator.
        /// This is the one door out of that state, and it is deliberately narrow:
        ///   • it only works while no row anywhere has a credential, so it can
        ///     never be used to take over an installation that has passwords;
        ///   • the row must be the workstation account itself (resolved inside the
        ///     write transaction), so it cannot be aimed at somebody else;
        ///   • the row must be active and an Administrator.
        /// The password chosen here is the person's own, so it is not temporary and
        /// no change is forced at the next sign-in.
        /// </remarks>
        Task<UserSaveResult> SetFirstPasswordAsync(
            string password, string operatorUsername, CancellationToken ct = default);

        /// <summary>True when the step3 tables are present and shaped as expected.</summary>
        Task<bool> IsSchemaReadyAsync(CancellationToken ct = default);
    }
}
