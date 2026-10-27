// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.a.3.UserRules.cs
// MODULE: step3 — User normalization and field validation.
//
// Pure functions only: no EF, no WPF, no I/O — the same discipline as
// step2.a.2.ClientRules, and for the same reason: these rules must be
// testable without the WPF runtime (step3.f).
//
// Normalization policy
//   • Full name is trimmed and internal whitespace runs collapsed, so
//     "  Ahmed   Al  Balushi " and "Ahmed Al Balushi" store identically.
//   • Username is trimmed but its internal characters are left alone; it is
//     an identifier the operator must be able to type back exactly.
//   • The username comparison key is uppercase (invariant) — the unique index
//     is built on that key, so "Ahmed" and "ahmed" cannot both exist.
//   • Department and role resolve to catalogue codes (step3.a.2). A value that
//     matches no shipped entry is kept verbatim when it looks like a code, so
//     an administrator-added catalogue row is usable without a code change.
//
// Validation policy
//   • Full name is required.
//   • Username is required, must be one "word" (letters, digits, dot, dash,
//     underscore, backslash or "@"), and must be unique — uniqueness is
//     enforced by the repository/index, not here; the editor asks first.
//   • Department and role must name a known catalogue value.
//   • There is nothing to validate about a password because no password is
//     stored anywhere in this module (see step3.a.1.User).
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProGlassAutomation.Models.Users
{
    /// <summary>Normalization and field validation rules for the user directory.</summary>
    public static class UserRules
    {
        public const int FullNameMaxLength = 120;
        public const int UsernameMaxLength = 64;

        // ── Normalization ────────────────────────────────────────────────

        /// <summary>Trims and collapses internal whitespace runs to one space.</summary>
        public static string NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var builder = new StringBuilder(value.Length);
            bool pendingSpace = false;
            foreach (char character in value)
            {
                if (char.IsWhiteSpace(character))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }
                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }
                builder.Append(character);
            }
            return builder.ToString();
        }

        /// <summary>Stores the username trimmed, with its casing preserved.</summary>
        public static string NormalizeUsername(string? value) => (value ?? "").Trim();

        /// <summary>
        /// Unique comparison key for a username: trimmed, Unicode Form KC,
        /// invariant uppercase. Stored in Users.NormalizedUsername and backed by
        /// a unique index, so "A.Salam" and "a.salam" are the same person.
        /// </summary>
        public static string UsernameKey(string? value)
        {
            string trimmed = NormalizeUsername(value);
            if (trimmed.Length == 0) return "";
            return trimmed.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        }

        /// <summary>Department code for a draft: catalogue code, or the text verbatim.</summary>
        public static string NormalizeDepartment(string? value) =>
            UserCatalog.CoerceCode(UserCatalog.Departments, value, UserCatalog.DefaultDepartment);

        /// <summary>Role code for a draft: catalogue code, or the text verbatim.</summary>
        public static string NormalizeRole(string? value) =>
            UserCatalog.CoerceCode(UserCatalog.Roles, value, UserCatalog.DefaultRole);

        // ── Field validation (null = valid) ──────────────────────────────

        /// <summary>Full name is required and length-bounded.</summary>
        public static string? ValidateFullName(string? value)
        {
            string name = NormalizeText(value);
            if (name.Length == 0)
                return "Full name is required.";
            if (name.Length > FullNameMaxLength)
                return $"Full name must be {FullNameMaxLength} characters or fewer.";
            return null;
        }

        /// <summary>
        /// Username is required, length-bounded and restricted to the characters
        /// a Windows account name may contain. It is the identity match key, so
        /// it must be typed exactly as the workstation reports it.
        /// </summary>
        public static string? ValidateUsername(string? value)
        {
            string text = NormalizeUsername(value);
            if (text.Length == 0)
                return "Username is required.";
            if (text.Length > UsernameMaxLength)
                return $"Username must be {UsernameMaxLength} characters or fewer.";
            foreach (char character in text)
            {
                if (char.IsLetterOrDigit(character)) continue;
                if (character == '.' || character == '_' || character == '-' ||
                    character == '\\' || character == '@')
                    continue;
                return "Username may contain letters, digits, dot, dash, underscore, backslash and @ only.";
            }
            return null;
        }

        /// <summary>Department must be one of the shipped catalogue values.</summary>
        public static string? ValidateDepartment(string? value)
        {
            string code = (value ?? "").Trim();
            if (code.Length == 0) return "Department is required.";
            if (code.Length > UserCatalog.ValueMaxLength)
                return $"Department must be {UserCatalog.ValueMaxLength} characters or fewer.";
            if (!UserCatalog.IsKnownDepartment(code) &&
                UserCatalog.DepartmentCode(code).Length == 0)
                return "Choose a department from the list.";
            return null;
        }

        /// <summary>Role must be one of the shipped catalogue values.</summary>
        public static string? ValidateRole(string? value)
        {
            string code = (value ?? "").Trim();
            if (code.Length == 0) return "Role is required.";
            if (code.Length > UserCatalog.ValueMaxLength)
                return $"Role must be {UserCatalog.ValueMaxLength} characters or fewer.";
            if (!UserCatalog.IsKnownRole(code) && UserCatalog.RoleCode(code).Length == 0)
                return "Choose a role from the list.";
            return null;
        }

        // ── Whole draft ──────────────────────────────────────────────────

        /// <summary>
        /// Validates every field of a draft. Returns field name → message for
        /// each invalid field; an empty dictionary means the draft may be saved.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Validate(UserDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);

            AddIfPresent(errors, nameof(UserDraft.FullName), ValidateFullName(draft.FullName));
            AddIfPresent(errors, nameof(UserDraft.Username), ValidateUsername(draft.Username));
            AddIfPresent(errors, nameof(UserDraft.Department), ValidateDepartment(draft.Department));
            AddIfPresent(errors, nameof(UserDraft.Role), ValidateRole(draft.Role));

            return errors;
        }

        /// <summary>
        /// Returns a normalized copy of the draft: display text trimmed and
        /// collapsed, department and role reduced to catalogue codes. This is the
        /// value the repository persists.
        /// </summary>
        public static UserDraft Normalize(UserDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            return new UserDraft
            {
                Id = draft.Id,
                FullName = NormalizeText(draft.FullName),
                Username = NormalizeUsername(draft.Username),
                Department = NormalizeDepartment(draft.Department),
                Role = NormalizeRole(draft.Role),
                IsActive = draft.IsActive,
                OriginalRole = draft.OriginalRole,
                OriginalIsActive = draft.OriginalIsActive
            };
        }

        /// <summary>
        /// One-line summary of blocking problems, for the message bar.
        /// </summary>
        public static string Describe(IReadOnlyDictionary<string, string> errors)
        {
            if (errors == null || errors.Count == 0) return "";
            var messages = new List<string>(errors.Values);
            return string.Join(" ", messages);
        }

        /// <summary>Local-time wording for a stored UTC timestamp.</summary>
        public static string FormatTimestamp(DateTime utc)
        {
            if (utc == default) return "";
            return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        private static void AddIfPresent(
            IDictionary<string, string> errors, string field, string? message)
        {
            if (!string.IsNullOrEmpty(message)) errors[field] = message!;
        }
    }
}
