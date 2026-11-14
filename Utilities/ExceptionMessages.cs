// ═══════════════════════════════════════════════════════════════════════
// FILE: Utilities/ExceptionMessages.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — diagnostics text
//
// PURPOSE:
//   EF Core wraps provider errors as "An error occurred while saving the
//   entity changes. See the inner exception for details." — a message that
//   tells the user nothing. Every user-facing catch in the Inquiry module
//   (and any new module) flattens the whole inner chain into one readable
//   line, so the REAL cause (e.g. "NOT NULL constraint failed:
//   InquiryAttachments.Xyz") is what the user sees and reports.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Utilities
{
    /// <summary>Flattens exception chains into one diagnostic string.</summary>
    public static class ExceptionMessages
    {
        /// <summary>
        /// "Outer message → inner message → innermost message", de-duplicated,
        /// single line. Never throws; never returns empty for a real exception.
        /// </summary>
        public static string Deep(Exception? exception)
        {
            if (exception == null) return "";
            var parts = new List<string>();
            for (Exception? e = exception; e != null; e = e.InnerException)
            {
                string message = (e.Message ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
                if (message.Length == 0) continue;
                if (!parts.Contains(message, StringComparer.Ordinal))
                    parts.Add(message);
            }
            return string.Join(" → ", parts);
        }
    }
}
