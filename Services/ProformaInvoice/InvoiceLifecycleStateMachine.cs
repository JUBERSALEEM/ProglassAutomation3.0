// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/InvoiceLifecycleStateMachine.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — Lifecycle state machine
//
// THE PROBLEM THIS SOLVES
//   Invoice status was a free-text string ("Draft", "Pending", etc.)
//   with no enforcement of valid transitions. A bug could set an
//   invoice to "Sent" without going through approval, or approve a
//   cancelled invoice. This state machine makes invalid transitions
//   impossible at the code level.
//
// CONTRACT
//   • Pure function — no side effects, no WPF, no repositories.
//   • CanTransition() returns true/false for any (from, to) pair.
//   • GetValidTransitions() returns the list of allowed next states.
//   • Never throws — an invalid transition is a result, not an exception.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    /// <summary>
    /// The lifecycle states a Proforma Invoice can be in.
    /// </summary>
    public enum InvoiceLifecycleState
    {
        /// <summary>Being created, not yet submitted.</summary>
        Draft = 0,

        /// <summary>Submitted for manager approval.</summary>
        PendingApproval = 1,

        /// <summary>Approved by manager, ready to send.</summary>
        Approved = 2,

        /// <summary>Sent to customer.</summary>
        Sent = 3,

        /// <summary>Customer confirmed / converted to Job Order.</summary>
        Confirmed = 4,

        /// <summary>Cancelled (any state can transition here).</summary>
        Cancelled = 5,

        /// <summary>Rejected by manager (returns to Draft).</summary>
        Rejected = 6
    }

    /// <summary>
    /// Describes a valid state transition with a human-readable label.
    /// </summary>
    public sealed class StateTransition
    {
        public InvoiceLifecycleState From { get; init; }
        public InvoiceLifecycleState To { get; init; }
        public string Description { get; init; } = "";
        public bool RequiresApproval { get; init; }
    }

    /// <summary>
    /// Enforces valid lifecycle transitions for Proforma Invoices.
    /// </summary>
    public static class InvoiceLifecycleStateMachine
    {
        // ── Valid transitions ──────────────────────────────────────
        private static readonly List<StateTransition> Transitions = new()
        {
            // Draft → PendingApproval (submit for review)
            new StateTransition
            {
                From = InvoiceLifecycleState.Draft,
                To = InvoiceLifecycleState.PendingApproval,
                Description = "Submit for approval",
                RequiresApproval = false
            },

            // PendingApproval → Approved (manager approves)
            new StateTransition
            {
                From = InvoiceLifecycleState.PendingApproval,
                To = InvoiceLifecycleState.Approved,
                Description = "Approve invoice",
                RequiresApproval = true
            },

            // PendingApproval → Rejected (manager rejects)
            new StateTransition
            {
                From = InvoiceLifecycleState.PendingApproval,
                To = InvoiceLifecycleState.Rejected,
                Description = "Reject invoice",
                RequiresApproval = true
            },

            // PendingApproval → Draft (return to draft)
            new StateTransition
            {
                From = InvoiceLifecycleState.PendingApproval,
                To = InvoiceLifecycleState.Draft,
                Description = "Return to draft",
                RequiresApproval = false
            },

            // Rejected → Draft (revise and resubmit)
            new StateTransition
            {
                From = InvoiceLifecycleState.Rejected,
                To = InvoiceLifecycleState.Draft,
                Description = "Revise and return to draft",
                RequiresApproval = false
            },

            // Approved → Sent (send to customer)
            new StateTransition
            {
                From = InvoiceLifecycleState.Approved,
                To = InvoiceLifecycleState.Sent,
                Description = "Send to customer",
                RequiresApproval = false
            },

            // Sent → Confirmed (customer confirms)
            new StateTransition
            {
                From = InvoiceLifecycleState.Sent,
                To = InvoiceLifecycleState.Confirmed,
                Description = "Customer confirmed",
                RequiresApproval = false
            },

            // Cancellation from any active state
            new StateTransition
            {
                From = InvoiceLifecycleState.Draft,
                To = InvoiceLifecycleState.Cancelled,
                Description = "Cancel invoice",
                RequiresApproval = false
            },
            new StateTransition
            {
                From = InvoiceLifecycleState.PendingApproval,
                To = InvoiceLifecycleState.Cancelled,
                Description = "Cancel invoice",
                RequiresApproval = false
            },
            new StateTransition
            {
                From = InvoiceLifecycleState.Rejected,
                To = InvoiceLifecycleState.Cancelled,
                Description = "Cancel invoice",
                RequiresApproval = false
            },
            new StateTransition
            {
                From = InvoiceLifecycleState.Approved,
                To = InvoiceLifecycleState.Cancelled,
                Description = "Cancel approved invoice",
                RequiresApproval = true
            }
        };

        /// <summary>
        /// Checks if a transition from one state to another is valid.
        /// </summary>
        public static bool CanTransition(
            InvoiceLifecycleState from,
            InvoiceLifecycleState to)
        {
            return Transitions.Any(t => t.From == from && t.To == to);
        }

        /// <summary>
        /// Checks if a transition is valid using string state names.
        /// Returns false for unrecognized state strings.
        /// </summary>
        public static bool CanTransition(string? fromState, string? toState)
        {
            if (!TryParseState(fromState, out var from)) return false;
            if (!TryParseState(toState, out var to)) return false;
            return CanTransition(from, to);
        }

        /// <summary>
        /// Returns the list of valid next states from the current state.
        /// </summary>
        public static IReadOnlyList<InvoiceLifecycleState> GetValidTransitions(
            InvoiceLifecycleState current)
        {
            return Transitions
                .Where(t => t.From == current)
                .Select(t => t.To)
                .ToList();
        }

        /// <summary>
        /// Returns the list of valid transitions with descriptions.
        /// </summary>
        public static IReadOnlyList<StateTransition> GetValidTransitionsWithDescriptions(
            InvoiceLifecycleState current)
        {
            return Transitions
                .Where(t => t.From == current)
                .ToList();
        }

        /// <summary>
        /// Gets the human-readable description for a specific transition.
        /// Returns null if the transition is invalid.
        /// </summary>
        public static string? GetTransitionDescription(
            InvoiceLifecycleState from,
            InvoiceLifecycleState to)
        {
            return Transitions
                .FirstOrDefault(t => t.From == from && t.To == to)
                ?.Description;
        }

        /// <summary>
        /// Checks if a transition requires manager approval.
        /// </summary>
        public static bool RequiresApproval(
            InvoiceLifecycleState from,
            InvoiceLifecycleState to)
        {
            return Transitions
                .FirstOrDefault(t => t.From == from && t.To == to)
                ?.RequiresApproval ?? false;
        }

        /// <summary>
        /// Checks if a state is a terminal state (no further transitions).
        /// </summary>
        public static bool IsTerminalState(InvoiceLifecycleState state)
        {
            return state == InvoiceLifecycleState.Cancelled
                || state == InvoiceLifecycleState.Confirmed;
        }

        /// <summary>
        /// Checks if a state allows editing (Draft, Rejected).
        /// </summary>
        public static bool IsEditable(InvoiceLifecycleState state)
        {
            return state == InvoiceLifecycleState.Draft
                || state == InvoiceLifecycleState.Rejected;
        }

        /// <summary>
        /// Parses a state string (case-insensitive) into an enum value.
        /// Also handles legacy status strings like "Pending" → PendingApproval.
        /// </summary>
        public static bool TryParseState(
            string? stateString,
            out InvoiceLifecycleState state)
        {
            state = InvoiceLifecycleState.Draft;
            if (string.IsNullOrWhiteSpace(stateString))
                return false;

            string normalized = stateString.Trim();

            // Direct enum parse
            if (Enum.TryParse<InvoiceLifecycleState>(normalized, true, out state))
                return true;

            // Legacy mappings
            if (normalized.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            {
                state = InvoiceLifecycleState.PendingApproval;
                return true;
            }

            if (normalized.Equals("Active", StringComparison.OrdinalIgnoreCase))
            {
                state = InvoiceLifecycleState.Sent;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Converts a legacy status string to the new enum format.
        /// </summary>
        public static string NormalizeStatus(string? legacyStatus)
        {
            if (TryParseState(legacyStatus, out var state))
                return state.ToString();

            return InvoiceLifecycleState.Draft.ToString();
        }
    }
}
