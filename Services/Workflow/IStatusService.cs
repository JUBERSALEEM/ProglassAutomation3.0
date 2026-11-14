// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Workflow/IStatusService.cs
// PROJECT: ProGlassAutomation
// MODULE: Workflow enforcement (Phase 4, audit item 4.1)
//
// Every USER-INITIATED status change must flow through IStatusService so
// that:
//   1. The transition table in StatusTransitions is the SINGLE authority
//      on which changes are legal (no more direct .Status = writes that
//      bypass validation).
//   2. Every applied transition is AUDITED (in-memory ring + log line),
//      giving a traceable workflow history.
//
// Internal operations that merely COPY state (snapshot replay, mapper
// sync, sample data, report filters) are NOT transitions and keep writing
// entity.Status directly — that is by design.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Workflow
{
    /// <summary>One audited status transition.</summary>
    public sealed record StatusAuditEntry(
        StatusTransitions.Kind Kind,
        string From,
        string To,
        string Actor,
        string? Note,
        DateTime Utc);

    /// <summary>
    /// Workflow authority facade. Validates against
    /// <see cref="StatusTransitions"/> and records every applied
    /// transition. The service never mutates entities itself — callers
    /// apply the returned status (e.g. <c>entity.Status = next</c>) after
    /// a successful <see cref="Transition"/>.
    /// </summary>
    public interface IStatusService
    {
        /// <summary>Validate a transition against the authority table
        /// (pure; nothing is recorded).</summary>
        TransitionResult Validate(StatusTransitions.Kind kind,
            string? current, string? next);

        /// <summary>
        /// Validate and, when legal, record an audited transition.
        /// Returns the same result as <see cref="Validate"/>; on
        /// <c>IsValid</c> the transition is appended to <see cref="Recent"/>
        /// and logged. Nothing is recorded for illegal transitions.
        /// </summary>
        TransitionResult Transition(StatusTransitions.Kind kind,
            string? current, string next,
            string? actor = null, string? note = null);

        /// <summary>Recent audited transitions (in-memory ring, newest
        /// first). Bounded — not a substitute for a DB audit table.</summary>
        IReadOnlyList<StatusAuditEntry> Recent { get; }

        /// <summary>Raised after every recorded transition (UI can refresh
        /// status badges / audit panels).</summary>
        event Action<StatusAuditEntry>? Transitioned;
    }
}
