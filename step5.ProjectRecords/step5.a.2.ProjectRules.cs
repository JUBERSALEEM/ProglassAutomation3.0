// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.a.2.ProjectRules.cs
// MODULE: step5 — project normalization, validation and numbering.
//
// Pure functions only: no EF, no WPF, no I/O, no clock. That is what lets the
// rules be checked without a Windows runtime, and it is why the number
// allocator takes the numbers that already exist instead of reading the
// database itself.
//
// Numbering policy
//   • The number is PRJ- plus a zero-padded sequence (PRJ-0001).
//   • The next number is one above the highest existing sequence, not one above
//     the count: closing a project must never cause its number to be reused.
//   • A hand-typed number is accepted only if it matches the pattern. The
//     database's UNIQUE index is the final authority; this is the first check,
//     not the only one.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ProGlassAutomation.Models.Projects
{
    /// <summary>Normalization, validation and numbering for project records.</summary>
    public static class ProjectRules
    {
        public const int NumberMaxLength = 32;
        public const int NameMaxLength = 200;
        public const int LocationMaxLength = 200;
        public const int DescriptionMaxLength = 1000;
        public const int StatusMaxLength = 20;
        public const int ActorMaxLength = 200;

        /// <summary>The prefix every generated number carries.</summary>
        public const string NumberPrefix = "PRJ-";

        /// <summary>Digits in a generated sequence (PRJ-0001).</summary>
        public const int NumberDigits = 4;

        /// <summary>PRJ-0001 … PRJ-9999, and longer once the sequence passes it.</summary>
        private static readonly Regex NumberPattern =
            new(@"^PRJ-\d{" + NumberDigits + @",}$", RegexOptions.CultureInvariant);

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
                if (pendingSpace) builder.Append(' ');
                pendingSpace = false;
                builder.Append(character);
            }
            return builder.ToString();
        }

        /// <summary>
        /// A comparison key for duplicate detection: Unicode Form KC and invariant
        /// uppercase, so "Al Noor" and "AL NOOR" are recognised as similar. Used
        /// for WARNINGS only — nothing is ever merged or matched automatically.
        /// </summary>
        public static string ComparisonKey(string? value) =>
            NormalizeText(value).Normalize(NormalizationForm.FormKC).ToUpperInvariant();

        public static ProjectDraft Normalize(ProjectDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            return new ProjectDraft
            {
                ProjectNumber = NormalizeText(draft.ProjectNumber).ToUpperInvariant(),
                ProjectName = NormalizeText(draft.ProjectName),
                ClientId = draft.ClientId,
                ClientName = NormalizeText(draft.ClientName),
                Location = NormalizeText(draft.Location),
                Description = (draft.Description ?? "").Trim(),
                Status = ProjectStatus.Normalize(draft.Status)
            };
        }

        // ── Validation ───────────────────────────────────────────────────

        /// <summary>
        /// Field errors, keyed by field name. Empty means valid. The number is
        /// optional on a draft: a blank number means "allocate one on save", which
        /// is what the editor does for a new project.
        /// </summary>
        public static Dictionary<string, string> Validate(ProjectDraft draft)
        {
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            if (draft == null)
            {
                errors["Project"] = "No project data was supplied.";
                return errors;
            }

            string name = NormalizeText(draft.ProjectName);
            if (name.Length == 0)
                errors["ProjectName"] = "A project name is required.";
            else if (name.Length > NameMaxLength)
                errors["ProjectName"] = $"The project name cannot be longer than {NameMaxLength} characters.";

            string number = NormalizeText(draft.ProjectNumber);
            if (number.Length > 0 && !IsValidNumber(number))
                errors["ProjectNumber"] =
                    $"A project number looks like {ExampleNumber} (letters PRJ, a hyphen, at least "
                    + $"{NumberDigits} digits).";

            // A project with no client is legal: one-off customers exist, and the
            // register shows "No client recorded" rather than an empty cell.

            if (NormalizeText(draft.Location).Length > LocationMaxLength)
                errors["Location"] = $"The location cannot be longer than {LocationMaxLength} characters.";

            if ((draft.Description ?? "").Trim().Length > DescriptionMaxLength)
                errors["Description"] = $"The description cannot be longer than {DescriptionMaxLength} characters.";

            if (!ProjectStatus.IsDefined(draft.Status))
                errors["Status"] = $"The status must be {ProjectStatus.Active} or {ProjectStatus.Closed}.";

            return errors;
        }

        /// <summary>True when a number is in the generated shape.</summary>
        public static bool IsValidNumber(string? number) =>
            !string.IsNullOrWhiteSpace(number) && NumberPattern.IsMatch(number.Trim().ToUpperInvariant());

        /// <summary>A number in the documented shape, for messages.</summary>
        public static string ExampleNumber => NumberPrefix + new string('0', NumberDigits - 1) + "1";

        // ── Numbering ────────────────────────────────────────────────────

        /// <summary>
        /// The next free number given every number that already exists. One above
        /// the highest sequence — never one above the count, or closing a project
        /// would hand its number to the next one created.
        /// </summary>
        public static string NextNumber(IEnumerable<string>? existingNumbers)
        {
            long highest = 0;
            foreach (string existing in existingNumbers ?? Enumerable.Empty<string>())
            {
                if (!TrySequence(existing, out long sequence)) continue;
                if (sequence > highest) highest = sequence;
            }
            return FormatNumber(highest + 1);
        }

        /// <summary>Formats a sequence as PRJ-0001 (and PRJ-10000 past the padding).</summary>
        public static string FormatNumber(long sequence)
        {
            if (sequence < 1) sequence = 1;
            return NumberPrefix + sequence.ToString(
                "D" + NumberDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        /// <summary>Reads the sequence out of a number, ignoring anything malformed.</summary>
        public static bool TrySequence(string? number, out long sequence)
        {
            sequence = 0;
            if (string.IsNullOrWhiteSpace(number)) return false;
            string trimmed = number.Trim().ToUpperInvariant();
            if (!trimmed.StartsWith(NumberPrefix, StringComparison.Ordinal)) return false;
            string digits = trimmed.Substring(NumberPrefix.Length);
            if (digits.Length == 0 || !digits.All(char.IsDigit)) return false;
            return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out sequence);
        }
    }
}
