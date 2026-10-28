// ═══════════════════════════════════════════════════════════════════════
// FILE: Diagnostics/AppLogRedaction.cs
// MODULE: application foundation — the part of the diagnostic log that
//         protects the owner's business data.
//
// A diagnostic log is written to be read by a human at support time, so it must
// carry the technical failure in full. It must NOT carry the secrets or the
// customer data that happened to be in scope when the failure occurred.
//
// What is removed, and why each one:
//   • passwords, hashes, salts and tokens — never useful for diagnosis, and a
//     credential in a text file is a credential leak;
//   • IBAN / account numbers and anything else that is 12+ digits in a row —
//     bank details are business data (step4 stores them), and the database's
//     own error messages quote values;
//   • e-mail addresses — a support file should not become a customer list.
//
// The rule is deliberately pattern-based and blunt: an over-redacted log entry
// still diagnoses the failure, an under-redacted one cannot be taken back.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Text.RegularExpressions;

namespace ProGlassAutomation.Diagnostics
{
    /// <summary>Removes credentials and business data from a log entry.</summary>
    public static class AppLogRedaction
    {
        /// <summary>What the removed value is replaced with.</summary>
        public const string Mask = "***";

        /// <summary>
        /// Field names that must never carry their value into a log file.
        /// Matched case-insensitively before the '=' or ':' that follows them.
        /// </summary>
        private static readonly string[] SecretFieldNames =
        {
            "password", "passwd", "pwd", "newpassword", "temporarypassword",
            "passwordhash", "passwordsalt", "hash", "salt", "token", "secret",
            "apikey", "api_key", "credential", "connectionstring"
        };

        /// <summary>`name=value` or `name: value` for a secret field name.</summary>
        private static readonly Regex SecretAssignment = new(
            @"(?<name>\b(?:" + string.Join("|", SecretFieldNames) + @")\b\s*[:=]\s*)(?<value>[^\s,;""'\]\)]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>A value that looks like a logical field assignment.</summary>
        private static readonly Regex QuotedSecret = new(
            @"(?<name>\b(?:" + string.Join("|", SecretFieldNames) + @")\b[^\r\n:=]{0,40}[:=]\s*)(?<value>""[^""]*""|'[^']*')",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// A run of 12 or more digits joined by dashes or nothing — an account
        /// number or an IBAN typed without spaces. Spaces are deliberately NOT
        /// allowed here: "2026-10-28 21:14:07" is a timestamp, not an account.
        /// </summary>
        private static readonly Regex LongNumber = new(
            @"\b\d[\d\-]{11,}\d\b", RegexOptions.Compiled);

        /// <summary>
        /// The spaced IBAN shape: three or more groups of exactly four digits.
        /// A date ("2026 10 28") has 2-digit groups and cannot match.
        /// </summary>
        private static readonly Regex GroupedNumber = new(
            @"\b\d{4}(?: \d{4}){2,}\b", RegexOptions.Compiled);

        /// <summary>An e-mail address.</summary>
        private static readonly Regex Email = new(
            @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled);

        /// <summary>
        /// Returns <paramref name="text"/> with credentials, long account numbers
        /// and e-mail addresses replaced by <see cref="Mask"/>. Never throws: a
        /// redaction fault must not lose the entry it was protecting.
        /// </summary>
        public static string Scrub(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";

            try
            {
                string result = QuotedSecret.Replace(text, "${name}" + Mask);
                result = SecretAssignment.Replace(result, "${name}" + Mask);
                result = LongNumber.Replace(result, Mask);
                result = GroupedNumber.Replace(result, Mask);
                result = Email.Replace(result, Mask);
                return result;
            }
            catch (Exception)
            {
                // A pattern engine failure is not worth losing the entry over, and
                // the safe direction is the one that shows nothing at all.
                return "[entry withheld: the redaction step failed]";
            }
        }
    }
}
