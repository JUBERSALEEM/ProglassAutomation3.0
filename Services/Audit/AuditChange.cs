// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Audit/AuditChange.cs
// PROJECT: ProGlassAutomation
// MODULE: Core — before/after change records (GitHub-style diffs)
//
// Audit entries can carry a structured change set so the UI can show
// exactly what changed — not just "record updated".
// ═══════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Text.Json;

namespace ProGlassAutomation.Services.Audit
{
    public enum ChangeKind
    {
        Unchanged,
        Added,
        Removed,
        Modified
    }

    /// <summary>One field-level change.</summary>
    public class AuditChange
    {
        public string Field { get; set; } = "";
        public string Before { get; set; } = "";
        public string After { get; set; } = "";
        public string Kind { get; set; } = nameof(ChangeKind.Modified);
    }

    /// <summary>Serialization helpers for change sets.</summary>
    public static class AuditChangeSerializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        public static string ToJson(List<AuditChange> changes)
        {
            if (changes == null || changes.Count == 0) return "";
            try
            {
                return JsonSerializer.Serialize(changes, Options);
            }
            catch
            {
                return "";
            }
        }

        public static List<AuditChange> FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<AuditChange>();

            try
            {
                return JsonSerializer.Deserialize<List<AuditChange>>(
                    json, Options) ?? new List<AuditChange>();
            }
            catch
            {
                return new List<AuditChange>();
            }
        }

        /// <summary>Convenience builder for a modified field.</summary>
        public static AuditChange Modified(
            string field, string before, string after) => new()
            {
                Field = field,
                Before = before,
                After = after,
                Kind = nameof(ChangeKind.Modified)
            };
    }
}
