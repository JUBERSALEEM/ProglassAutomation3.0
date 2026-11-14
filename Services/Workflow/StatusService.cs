// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Workflow/StatusService.cs
// PROJECT: ProGlassAutomation
// MODULE: Workflow enforcement (Phase 4, audit item 4.1)
//
// Default IStatusService. Registered as a singleton in DI and exposed as
// App.Status (the codebase's provider-backed static pattern) so existing
// ViewModels can adopt it without constructor churn during migration.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.Workflow
{
    public sealed class StatusService : IStatusService
    {
        private const int RingCapacity = 200;
        private readonly ConcurrentQueue<StatusAuditEntry> _ring = new();

        public event Action<StatusAuditEntry>? Transitioned;

        /// <inheritdoc />
        public TransitionResult Validate(StatusTransitions.Kind kind,
            string? current, string? next)
            => StatusTransitions.Validate(kind, current, next);

        /// <inheritdoc />
        public TransitionResult Transition(StatusTransitions.Kind kind,
            string? current, string next,
            string? actor = null, string? note = null)
        {
            var result = StatusTransitions.Validate(kind, current, next);
            if (!result.IsValid)
                return result;                      // illegal — never recorded

            var entry = new StatusAuditEntry(
                kind,
                (current ?? "").Trim(),
                next.Trim(),
                string.IsNullOrWhiteSpace(actor) ? Environment.UserName : actor!,
                string.IsNullOrWhiteSpace(note) ? null : note,
                DateTime.UtcNow);

            _ring.Enqueue(entry);
            while (_ring.Count > RingCapacity && _ring.TryDequeue(out _)) { }

            AppLogService.Info(
                $"[{entry.Kind}] \"{entry.From}\" -> \"{entry.To}\" by {entry.Actor}"
                + (entry.Note is null ? "" : $" ({entry.Note})"),
                new LogContext { Module = "Workflow", Operation = "StatusTransition" });

            Transitioned?.Invoke(entry);
            return result;
        }

        /// <inheritdoc />
        public IReadOnlyList<StatusAuditEntry> Recent
            => _ring.Reverse().ToArray();
    }
}
