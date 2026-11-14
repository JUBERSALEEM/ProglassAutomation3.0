// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Audit/OperationGuard.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — service-layer authorization (audit finding A-1)
//
// WHAT WAS WRONG
// ──────────────
// Permission checks existed in EIGHTEEN places, and every one of them
// was in a ViewModel. Zero checks existed in the service or repository
// layer. Meanwhile there are THIRTY-SEVEN mutating repository methods
// (Insert/Update/Delete/SaveOrUpdate across 8 repositories).
//
// The brief states the rule plainly: "Never rely only on hiding a
// button. Authorization must also exist at the operation/service layer."
// It was not. Any caller that reached a repository directly — the AI
// agent's tool layer, a background task, a future feature, or simply a
// ViewModel whose author forgot the check — performed the write with no
// permission evaluation at all.
//
// WHY A SEPARATE TYPE RATHER THAN REUSING PermissionService.Require
// ─────────────────────────────────────────────────────────────────
// PermissionService.Require() shows a themed prompt (Prompts.Show). That is correct
// for a ViewModel and WRONG for a service:
//
//   • It blocks a background thread on a modal UI dialog.
//   • It hard-couples the data layer to WPF.
//   • It cannot be unit-tested without a message pump.
//   • It would show a SECOND dialog when the ViewModel has already
//     shown one, so a denied action would pop twice.
//
// OperationGuard is the headless equivalent: it evaluates the same
// RolePermissions map, writes the same audit entry, and THROWS instead
// of showing UI. The ViewModel layer keeps Require() for the friendly
// dialog; the service layer gets a hard boundary that cannot be
// bypassed by forgetting a call in the UI.
//
// DEFENCE IN DEPTH, NOT A REPLACEMENT
// ───────────────────────────────────
// The existing 18 ViewModel checks are deliberately left in place. They
// give the user a clear explanation before anything happens. This guard
// is the backstop that makes the rule actually enforced. Two layers,
// each doing what it is good at.
//
// FAIL-CLOSED
// ───────────
// If no user is signed in, CurrentUser is null and RolePermissions.For
// returns an empty set, so every permission evaluates to DENY. That is
// the correct default for a security gate: absence of identity must
// never mean absence of restriction.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Audit
{
    /// <summary>
    /// Thrown when a service-layer operation is attempted without the
    /// required permission. Carries the permission name so the UI can
    /// render a precise message.
    /// </summary>
    public class PermissionDeniedException : Exception
    {
        /// <summary>The permission that was required but missing.</summary>
        public string Permission { get; }

        /// <summary>Human-readable description of the blocked operation.</summary>
        public string Operation { get; }

        /// <summary>Role of the user at the time of the denial.</summary>
        public string Role { get; }

        public PermissionDeniedException(
            string permission, string operation, string role)
            : base($"Permission denied: '{operation}' requires " +
                   $"'{permission}' but the current role is " +
                   $"'{(string.IsNullOrEmpty(role) ? "none" : role)}'.")
        {
            Permission = permission;
            Operation = operation;
            Role = role;
        }

        /// <summary>
        /// Message safe to show a user: states what was blocked and why,
        /// without leaking internals.
        /// </summary>
        public string UserMessage =>
            "You don't have permission for this action.\n\n" +
            $"Action: {Operation}\n" +
            $"Required permission: {Permission}\n" +
            $"Your role: {(string.IsNullOrEmpty(Role) ? "none" : Role)}\n\n" +
            "Ask an administrator to change your role.";
    }

    /// <summary>
    /// Headless permission gate for the service and repository layer.
    /// Never shows UI. Throws <see cref="PermissionDeniedException"/>.
    /// </summary>
    public static class OperationGuard
    {
        /// <summary>
        /// When true the guard evaluates permissions. Set to false only
        /// by automated tests that exercise repositories directly
        /// without an authenticated session.
        /// </summary>
        /// <remarks>
        /// This exists so the test suite does not have to fake a signed-in
        /// administrator for every repository test. It is deliberately
        /// NOT exposed in configuration or settings — nothing the end
        /// user or a config file can reach may switch authorization off.
        /// </remarks>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Throws when the active user lacks <paramref name="permission"/>.
        /// Denials are written to the audit trail.
        /// </summary>
        /// <param name="permission">An <see cref="AppPermissions"/> value.</param>
        /// <param name="operation">
        /// Short description used in the audit entry and the user-facing
        /// message, e.g. "delete a glass sheet".
        /// </param>
        public static void Demand(string permission, string operation)
        {
            if (!Enabled) return;

            if (PermissionService.Can(permission)) return;

            string role = AuditService.CurrentUser?.Role ?? "";

            // Audit BEFORE throwing: a denial that is never recorded is
            // a security event that never happened as far as the trail
            // is concerned.
            try
            {
                AuditService.Log(
                    AuditSections.Users,
                    "Permission denied (service layer)",
                    $"{AuditService.CurrentUsername} " +
                    $"({(string.IsNullOrEmpty(role) ? "?" : role)}) " +
                    $"tried {operation} without {permission}");
            }
            catch (System.Exception ex)
            {
                // Auditing must never suppress the denial itself.
                ProGlassAutomation.Services.Logging.AppLogService.Warning("[OperationGuard] Audit write for permission denial failed: " + ex.Message);
            }

            throw new PermissionDeniedException(permission, operation, role);
        }

        /// <summary>
        /// Throws when the active user lacks EVERY one of the supplied
        /// permissions. Use this for actions that historically were
        /// gated by a legacy permission but are now also covered by a
        /// newer, more granular permission — so existing role configs
        /// keep working AND new configs get the precise gate.
        /// Throws with the first permission the user lacks; the audit
        /// entry names every permission checked.
        /// </summary>
        public static void DemandAny(IReadOnlyList<string> permissions, string operation)
        {
            if (!Enabled) return;
            if (permissions == null || permissions.Count == 0)
                throw new ArgumentException("DemandAny requires at least one permission.", nameof(permissions));
            foreach (var p in permissions)
            {
                if (PermissionService.Can(p)) return;
            }
            // No permission held. Audit + throw with the first one
            // (so the user sees a precise message naming what was
            // expected) but log every permission checked for forensics.
            string role = AuditService.CurrentUser?.Role ?? "";
            string required = string.Join(" or ", permissions);
            try
            {
                AuditService.Log(
                    AuditSections.Users,
                    "Permission denied (service layer)",
                    $"{AuditService.CurrentUsername} " +
                    $"({(string.IsNullOrEmpty(role) ? "?" : role)}) " +
                    $"tried {operation} without any of: {required}");
            }
            catch { /* auditing must never suppress the denial */ }
            throw new PermissionDeniedException(permissions[0], operation, role);
        }

        /// <summary>
        /// Non-throwing check for callers that want to branch rather
        /// than fail, e.g. deciding whether to offer an action.
        /// </summary>
        public static bool IsAllowed(string permission) =>
            !Enabled || PermissionService.Can(permission);
    }
}
