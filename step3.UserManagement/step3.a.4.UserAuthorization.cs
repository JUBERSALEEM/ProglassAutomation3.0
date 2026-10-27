// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.a.4.UserAuthorization.cs
// MODULE: step3 — who may do what, expressed as pure decisions.
//
// This file exists because hiding a button is not authorization. Every check
// here is called by the repository (step3.b.4) inside the write path as well as
// by the view models, so a caller that bypasses the UI is still refused.
//
// What is enforced — only what the owner actually specified:
//   1. Only a user whose role is Administrator may add, edit, activate,
//      deactivate or re-role a user.
//   2. An inactive user may perform no protected operation, including user
//      management. Deactivation therefore takes effect immediately.
//   3. The last active Administrator may not be deactivated, demoted or have
//      their role changed away from Administrator. The count is read in the
//      same transaction as the write (step3.b.4), not from a stale list.
//   4. A caller whose Windows account matches no user row is not identified
//      and is refused; there is no anonymous "Operator" fallback in a
//      privileged path.
//
// What is deliberately NOT enforced here:
//   • Any per-module matrix for Accountant / Salesman / Estimator / Foreman /
//     Cutting / Double Glazing. Those modules do not exist yet (step6-step11),
//     and inventing a permission grid now would be a fabricated requirement
//     that later modules would have to undo. See docs/12.
//   • Department as an authorization input. It is never consulted.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.Users
{
    /// <summary>Why a protected operation was allowed or refused.</summary>
    public enum UserDecisionCode
    {
        Allowed,
        NotIdentified,
        Inactive,
        NotAuthorized,
        LastAdministrator,
        InvalidTarget,
        SelfDeactivation
    }

    /// <summary>Outcome of a permission question, with operator-facing wording.</summary>
    public sealed class UserDecision
    {
        private UserDecision(UserDecisionCode code, string message)
        {
            Code = code;
            Message = message;
        }

        public UserDecisionCode Code { get; }

        public string Message { get; }

        public bool IsAllowed => Code == UserDecisionCode.Allowed;

        public static readonly UserDecision Allowed = new(UserDecisionCode.Allowed, "");

        public static UserDecision Refuse(UserDecisionCode code, string message) => new(code, message);

        public override string ToString() => IsAllowed ? "Allowed" : $"{Code}: {Message}";
    }

    /// <summary>The permission rules of the user directory, as pure functions.</summary>
    public static class UserAuthorization
    {
        /// <summary>
        /// May this person manage the user directory? Requires an identified,
        /// active Administrator. <paramref name="actor"/> is the row that matched
        /// the workstation account, or null when nobody matched.
        /// </summary>
        public static UserDecision CanManageUsers(User? actor)
        {
            if (actor == null)
                return UserDecision.Refuse(
                    UserDecisionCode.NotIdentified,
                    "This workstation account is not registered as a user, so user management is unavailable. "
                    + "Ask an administrator to add a user whose username matches this Windows account.");

            if (!actor.IsActive)
                return UserDecision.Refuse(
                    UserDecisionCode.Inactive,
                    $"{actor.FullName} is inactive and cannot manage users.");

            if (!UserCatalog.IsAdministrator(actor.Role))
                return UserDecision.Refuse(
                    UserDecisionCode.NotAuthorized,
                    $"Only an Administrator can manage users. {actor.FullName} is signed in as "
                    + $"{UserCatalog.RoleDisplayName(actor.Role)}.");

            return UserDecision.Allowed;
        }

        /// <summary>
        /// May this person be recorded as the author of a business record? The
        /// same bar as acting at all: identified and active. Role and department
        /// are irrelevant here — they describe what a person may do, not who
        /// they are.
        /// </summary>
        public static UserDecision CanAttribute(User? actor)
        {
            if (actor == null)
                return UserDecision.Refuse(
                    UserDecisionCode.NotIdentified,
                    "The operator could not be identified, so this record cannot be attributed.");

            if (!actor.IsActive)
                return UserDecision.Refuse(
                    UserDecisionCode.Inactive,
                    $"{actor.FullName} is inactive and cannot create or prepare records.");

            return UserDecision.Allowed;
        }

        /// <summary>
        /// Guard the change itself, independent of who is making it.
        /// <paramref name="activeAdministratorsExcludingTarget"/> is the count of
        /// other active administrators read inside the write transaction.
        /// </summary>
        public static UserDecision ValidateChange(UserDraft draft, int activeAdministratorsExcludingTarget)
        {
            if (draft == null)
                return UserDecision.Refuse(UserDecisionCode.InvalidTarget, "No user was supplied.");

            bool wasAdministrator = UserCatalog.IsAdministrator(draft.OriginalRole);
            bool willBeAdministrator = UserCatalog.IsAdministrator(UserRules.NormalizeRole(draft.Role));

            // Deactivating the last administrator would leave the directory
            // with nobody able to unlock it — an unrecoverable state.
            bool losingLastAdministrator = wasAdministrator && draft.OriginalIsActive
                && (!draft.IsActive || !willBeAdministrator);

            if (losingLastAdministrator && activeAdministratorsExcludingTarget <= 0)
            {
                string action = !draft.IsActive ? "deactivate" : "change the role of";
                return UserDecision.Refuse(
                    UserDecisionCode.LastAdministrator,
                    $"You cannot {action} the last active Administrator. "
                    + "Give another user the Administrator role first, then try again.");
            }

            return UserDecision.Allowed;
        }

        /// <summary>
        /// A person may not switch their own account off: on a single-station
        /// install that locks the operator out of the screen they are using, and
        /// it is almost always a mis-click. (Deactivating the last administrator
        /// is already refused above; this covers the remaining cases.)
        /// </summary>
        public static UserDecision ValidateSelfDeactivation(UserDraft draft, User? actor)
        {
            if (draft == null) return UserDecision.Allowed;
            if (actor == null) return UserDecision.Allowed;
            if (draft.Id != actor.Id) return UserDecision.Allowed;
            if (draft.OriginalIsActive && !draft.IsActive)
                return UserDecision.Refuse(
                    UserDecisionCode.SelfDeactivation,
                    "You cannot deactivate your own account. Ask another administrator to do it.");
            return UserDecision.Allowed;
        }
    }
}
