// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/CompositionKey.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — canonical glass-composition key (Phase 2)
//
// The glass composition string is the merge key for cutting groups, rack
// routing, stock-usage tallies and costing rows. It used to be compared as
// raw free text (trim + case-insensitive): the same physical glass written
// differently across two jobs — "6mm clear ft" vs "6mm clear ft glass",
// extra spaces, casing — split ONE glass into TWO cutting groups, each
// losing its dedicated Step 4 rack and falling back to "other specs'
// racks". Sheet counts jumped and the result read like a foreign job's
// specs had been imported.
//
// CompositionKey.Normalize is the single canonicalisation applied at every
// point where a composition ENTERS the module (job import, Step 5 live run,
// manual cut list, session restore, reference job) plus one defensive
// normalisation at group-build time for user-typed rows.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Services.Optimization
{
    /// <summary>
    /// Canonical key for a glass composition label.
    /// </summary>
    public static class CompositionKey
    {
        /// <summary>
        /// Normalises a composition label: trim, collapse internal
        /// whitespace, lowercase, and drop a trailing standalone " glass"
        /// so "6mm clear ft glass" and "6mm clear ft" are the same key.
        /// Empty/whitespace input yields the empty string (callers decide
        /// whether that means "unlabelled").
        /// </summary>
        public static string Normalize(string? composition)
        {
            if (string.IsNullOrWhiteSpace(composition)) return string.Empty;
            string s = composition.Trim().ToLowerInvariant();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            if (s.EndsWith(" glass", StringComparison.Ordinal))
                s = s.Substring(0, s.Length - " glass".Length).TrimEnd();
            return s;
        }
    }
}
