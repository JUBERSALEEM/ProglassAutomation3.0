// ═══════════════════════════════════════════════════════════════════════
// FILE: ActivityRowViewModel.cs
// PROJECT: ProGlassAutomation
// MODULE: Shared — professional audit/activity row
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Audit;
using ProGlassAutomation.Services.Audit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.ViewModels.Shared
{
    public class ActivityRowViewModel
    {
        public int Id { get; set; }
        public DateTime TimestampUtc { get; set; }
        public string TimeDisplay { get; set; } = "";
        public string UtcTimeDisplay { get; set; } = "";
        public string Username { get; set; } = "";
        public string UserDisplayName { get; set; } = "";
        public string UserRole { get; set; } = "";
        public string Action { get; set; } = "";
        public string Details { get; set; } = "";
        public string Section { get; set; } = "";
        public string EntityType { get; set; } = "";
        public string EntityId { get; set; } = "";
        public string SessionId { get; set; } = "";
        public string OperationId { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public string LocalTimeZone { get; set; } = "";
        public string CurrentViewerUsername { get; set; } = "";

        /// <summary>Raw before/after change set JSON (empty = no diff).</summary>
        public string ChangesJson { get; set; } = "";

        /// <summary>True when a GitHub-style diff can be shown.</summary>
        public bool HasChanges => !string.IsNullOrWhiteSpace(ChangesJson);

        /// <summary>The human WHY the user typed when performing this action.</summary>
        public string Reason { get; set; } = "";

        public bool HasReason => !string.IsNullOrWhiteSpace(Reason);
        public bool HasEntity => !string.IsNullOrWhiteSpace(EntityId);
        public bool IsOtherUser =>
            !string.IsNullOrWhiteSpace(CurrentViewerUsername)
            && !string.Equals(Username, CurrentViewerUsername, StringComparison.OrdinalIgnoreCase);

        public string OtherUserNotice => IsOtherUser
            ? "Updated by another user/session — verify before overwriting."
            : "Current user/session activity.";

        public bool HasTechnicalContext =>
            !string.IsNullOrWhiteSpace(SessionId)
            || !string.IsNullOrWhiteSpace(OperationId)
            || !string.IsNullOrWhiteSpace(MachineName)
            || !string.IsNullOrWhiteSpace(AppVersion);

        public string ActorDisplay
        {
            get
            {
                var name = string.IsNullOrWhiteSpace(UserDisplayName)
                    ? Username
                    : UserDisplayName;
                if (string.IsNullOrWhiteSpace(name)) name = "system";
                return string.IsNullOrWhiteSpace(UserRole)
                    ? name
                    : $"{name} ({UserRole})";
            }
        }

        public string UserLine => string.IsNullOrWhiteSpace(Username)
            ? ActorDisplay
            : $"{ActorDisplay} · login: {Username}";

        public string EntityLine => HasEntity
            ? $"{EntityType} {EntityId}".Trim()
            : "No record id captured";

        public int ChangeCount => AuditChangeSerializer.FromJson(ChangesJson).Count;

        public string ChangeSummary => ChangeCount == 0
            ? "No field-level before/after diff was attached to this legacy/simple event."
            : ChangeCount == 1
                ? "1 field changed"
                : $"{ChangeCount} fields changed";

        public string WhatSummary => string.IsNullOrWhiteSpace(Details)
            ? Action
            : $"{Action}: {Details}";

        public string WhySummary => string.IsNullOrWhiteSpace(Reason)
            ? "No reason note was supplied."
            : Reason;

        public string TechnicalLine
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(SessionId)) parts.Add($"Session {SessionId}");
                if (!string.IsNullOrWhiteSpace(OperationId)) parts.Add(OperationId);
                if (!string.IsNullOrWhiteSpace(MachineName)) parts.Add($"PC {MachineName}");
                if (!string.IsNullOrWhiteSpace(AppVersion)) parts.Add($"v{AppVersion}");
                if (!string.IsNullOrWhiteSpace(LocalTimeZone)) parts.Add(LocalTimeZone);
                return parts.Count == 0 ? "Technical context not captured on this old row" : string.Join(" · ", parts);
            }
        }

        /// <summary>
        /// Complete professional audit report text for copy/review: who,
        /// when, where, what, why, record, operation and before/after count.
        /// </summary>
        public string AuditReportText
        {
            get
            {
                var lines = new List<string>
                {
                    "AUDIT EVENT REPORT",
                    $"Audit Id: {Id}",
                    $"When Local: {TimeDisplay}",
                    $"When UTC: {UtcTimeDisplay}",
                    $"Who: {UserLine}",
                    $"Other user check: {OtherUserNotice}",
                    $"Module: {Section}",
                    $"Record: {EntityLine}",
                    $"What: {Action}",
                    $"Details: {(string.IsNullOrWhiteSpace(Details) ? "—" : Details)}",
                    $"Why: {WhySummary}",
                    $"Before/After: {ChangeSummary}",
                    $"Technical: {TechnicalLine}"
                };
                return string.Join(Environment.NewLine, lines);
            }
        }

        /// <summary>Full row tooltip: date/time · user · module · action · details.</summary>
        public string RowTooltip
        {
            get
            {
                var parts = new List<string>
                {
                    $"When:   {TimeDisplay}",
                    $"UTC:    {UtcTimeDisplay}",
                    $"Who:    {UserLine}",
                    $"Module: {Section}",
                    $"Record: {EntityLine}",
                    $"Action: {Action}",
                    $"Changes:{ChangeSummary}",
                    $"Trace:  {TechnicalLine}"
                };
                if (!string.IsNullOrWhiteSpace(Details))
                    parts.Add($"Details: {Details}");
                if (!string.IsNullOrWhiteSpace(Reason))
                    parts.Add($"Why:     {Reason}");
                return string.Join("\n", parts);
            }
        }
    }
}
