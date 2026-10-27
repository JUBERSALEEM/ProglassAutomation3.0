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
            string fullName, string department, string operatorUsername, CancellationToken ct = default);

        /// <summary>True when the step3 tables are present and shaped as expected.</summary>
        Task<bool> IsSchemaReadyAsync(CancellationToken ct = default);
    }
}
