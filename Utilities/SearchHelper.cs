// ═══════════════════════════════════════════════════════════════════════
// FILE: Utilities/SearchHelper.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — shared search/filter helpers
//
// PURPOSE: Single source of truth for case-insensitive string matching
// used across God Views, reports, and list filtering. Previously
// duplicated as private static methods in 3+ ViewModels.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Utilities
{
    /// <summary>
    /// Shared search/filter utilities for case-insensitive string matching.
    /// Null-safe — returns false when the source text is null.
    /// </summary>
    public static class SearchHelper
    {
        /// <summary>
        /// Case-insensitive substring check. Null-safe.
        /// Returns false if either argument is null or empty.
        /// </summary>
        public static bool ContainsIgnoreCase(string? text, string? filter)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(filter))
                return false;
            return text.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Checks if any of the given fields contain the filter string.
        /// Null-safe — skips null fields.
        /// </summary>
        public static bool MatchesAny(string? filter, params string?[] fields)
        {
            if (string.IsNullOrEmpty(filter))
                return true; // empty filter matches everything
            foreach (var field in fields)
            {
                if (!string.IsNullOrEmpty(field) &&
                    field.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
