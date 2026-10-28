// ═══════════════════════════════════════════════════════════════════════
// FILE: step2.ClientManagement/step2.a.2.ClientRules.cs
// MODULE: step2 — Client normalization and field validation.
//
// Pure functions only: no EF, no WPF, no I/O. This is what makes the
// business rules directly testable without the WPF runtime (see step2.f).
//
// Normalization policy
//   • display values are trimmed and internal whitespace runs collapsed, so
//     "  Al  Noor   Glass " and "Al Noor Glass" store identically;
//   • comparison keys additionally apply Unicode Form KC and invariant
//     uppercase (adapted from the ADV identity-normalization concept) so
//     duplicate detection does not depend on casing or composed accents;
//   • phone/TRN keys keep digits only, so "+971 50 123 4567" and
//     "00971501234567" are recognised as similar for WARNING purposes only.
//
// Validation policy
//   • Client name is the only required field.
//   • Email is checked for shape only when supplied.
//   • TRN must be digits when supplied. Its length is not forced to 15: the
//     column is optional and a partly-known TRN must not block a client.
//   • Nothing here rejects a client because it resembles another client; that
//     is a warning surfaced by ClientDuplicatePolicy, never a silent merge.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProGlassAutomation.Models.Clients
{
    /// <summary>Normalization and validation rules for the client directory.</summary>
    public static class ClientRules
    {
        // Field lengths are the single source used by the entity attributes,
        // the DDL and the editor's MaxLength.
        public const int ClientNameMaxLength = 200;
        public const int AttentionNameMaxLength = 200;
        public const int PhoneMaxLength = 50;
        public const int EmailMaxLength = 200;
        public const int TrnMaxLength = 20;
        public const int AddressMaxLength = 500;

        /// <summary>Minimum digits for a phone number that looks usable.</summary>
        public const int PhoneMinDigits = 6;

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

        /// <summary>
        /// Comparison key: normalized text, Unicode Form KC, invariant
        /// uppercase. Used for duplicate detection only — never stored as the
        /// client name.
        /// </summary>
        public static string ComparisonKey(string? value)
        {
            string normalized = NormalizeText(value);
            if (normalized.Length == 0) return "";
            return normalized.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        }

        /// <summary>Digits only, for phone similarity warnings.</summary>
        public static string DigitsOnly(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
                if (character >= '0' && character <= '9') builder.Append(character);
            return builder.ToString();
        }

        /// <summary>Stores the phone as typed (trimmed), not a reformatted value.</summary>
        public static string NormalizePhone(string? value) => NormalizeText(value);

        /// <summary>Comparison key for phone numbers: digits only.</summary>
        public static string PhoneKey(string? value) => DigitsOnly(value);

        /// <summary>Stores the e-mail trimmed, with casing preserved.</summary>
        public static string NormalizeEmail(string? value) => NormalizeText(value);

        /// <summary>Comparison key for e-mail addresses.</summary>
        public static string EmailKey(string? value) => ComparisonKey(value);

        /// <summary>Stores the TRN as digits only: it is an identifier, not prose.</summary>
        public static string NormalizeTrn(string? value) => DigitsOnly(value);

        /// <summary>Comparison key for TRNs.</summary>
        public static string TrnKey(string? value) => DigitsOnly(value);

        // ── Field validation (null = valid) ──────────────────────────────

        /// <summary>Client name is required and length-bounded.</summary>
        public static string? ValidateClientName(string? value)
        {
            string name = NormalizeText(value);
            if (name.Length == 0)
                return "Client name is required.";
            if (name.Length > ClientNameMaxLength)
                return $"Client name must be {ClientNameMaxLength} characters or fewer.";
            return null;
        }

        /// <summary>
        /// Attention name is optional; only length is bounded. It is the person at
        /// the client to address, so it is never required and never a duplicate key.
        /// </summary>
        public static string? ValidateAttentionName(string? value)
        {
            string text = NormalizeText(value);
            if (text.Length > AttentionNameMaxLength)
                return $"Attention name must be {AttentionNameMaxLength} characters or fewer.";
            return null;
        }

        /// <summary>
        /// Phone is optional. When supplied it must contain at least
        /// <see cref="PhoneMinDigits"/> digits and stay within the column limit.
        /// </summary>
        public static string? ValidatePhone(string? value)
        {
            string text = NormalizePhone(value);
            if (text.Length == 0) return null;
            if (text.Length > PhoneMaxLength)
                return $"Phone must be {PhoneMaxLength} characters or fewer.";
            string digits = DigitsOnly(text);
            if (digits.Length < PhoneMinDigits)
                return $"Phone must contain at least {PhoneMinDigits} digits.";
            return null;
        }

        /// <summary>E-mail is optional; shape is checked only when supplied.</summary>
        public static string? ValidateEmail(string? value)
        {
            string text = NormalizeEmail(value);
            if (text.Length == 0) return null;
            if (text.Length > EmailMaxLength)
                return $"E-mail must be {EmailMaxLength} characters or fewer.";
            int at = text.IndexOf('@');
            if (at <= 0 || at != text.LastIndexOf('@') || at == text.Length - 1)
                return "E-mail must contain one @ with a name before it.";
            string domain = text.Substring(at + 1);
            if (domain.Length == 0 || domain.IndexOf('.') <= 0 || domain.EndsWith(".", StringComparison.Ordinal))
                return "E-mail domain must contain a dot, for example company.ae.";
            foreach (char character in text)
                if (char.IsWhiteSpace(character))
                    return "E-mail must not contain spaces.";
            return null;
        }

        /// <summary>TRN is optional; when supplied it must be digits only.</summary>
        public static string? ValidateTrn(string? value)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) return null;

            // A TRN arrives written with separators — pasted from a certificate, an
            // invoice or an e-mail — and this module is built for exactly that:
            // NormalizeTrn stores it as digits only and TrnKey compares digits, so
            // "100-123-456-700-003" and "100123456700003" are the same registration.
            // Refusing the separators here contradicted both, made that stripping
            // path unreachable from any user input, and differed from the Company
            // Profile TRN the same operator types (round 34.7).
            string digits = DigitsOnly(text);
            // A non-blank TRN of nothing but separators ("- -") would otherwise
            // normalize to an empty string and be silently accepted as blank.
            if (digits.Length == 0)
                return "TRN must contain digits only.";
            if (digits.Length != text.Replace(" ", "").Replace("-", "").Length)
                return "TRN must contain digits only.";

            // The column stores the digits, so the bound belongs on the stored
            // value: counting separators against it would refuse a valid TRN for
            // being typed the way the certificate prints it.
            if (digits.Length > TrnMaxLength)
                return $"TRN must be {TrnMaxLength} digits or fewer.";
            return null;
        }

        /// <summary>Address is optional; only length is bounded.</summary>
        public static string? ValidateAddress(string? value)
        {
            string text = NormalizeText(value);
            if (text.Length > AddressMaxLength)
                return $"Address must be {AddressMaxLength} characters or fewer.";
            return null;
        }

        // ── Whole draft ──────────────────────────────────────────────────

        /// <summary>
        /// Validates every field of a draft. Returns field name → message for
        /// each invalid field; an empty dictionary means the draft may be saved.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Validate(ClientDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);

            AddIfPresent(errors, nameof(ClientDraft.ClientName), ValidateClientName(draft.ClientName));
            AddIfPresent(errors, nameof(ClientDraft.AttentionName), ValidateAttentionName(draft.AttentionName));
            AddIfPresent(errors, nameof(ClientDraft.Phone), ValidatePhone(draft.Phone));
            AddIfPresent(errors, nameof(ClientDraft.Email), ValidateEmail(draft.Email));
            AddIfPresent(errors, nameof(ClientDraft.Trn), ValidateTrn(draft.Trn));
            AddIfPresent(errors, nameof(ClientDraft.Address), ValidateAddress(draft.Address));

            // A representative is a reference, not free text: the only invalid
            // shape is a non-null id that cannot name a row (zero or negative).
            // Existence in Users is checked by the repository, which owns the
            // database, and null is always valid ("no representative yet").
            if (draft.RepresentativeUserId is int id && id <= 0)
                errors[nameof(ClientDraft.RepresentativeUserId)] =
                    "Choose a representative from the list.";

            return errors;
        }

        /// <summary>
        /// Returns a normalized copy of the draft: display values trimmed and
        /// whitespace-collapsed, TRN reduced to digits. This is the value the
        /// repository persists.
        /// </summary>
        public static ClientDraft Normalize(ClientDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            return new ClientDraft
            {
                ClientName = NormalizeText(draft.ClientName),
                AttentionName = NormalizeText(draft.AttentionName),
                Phone = NormalizePhone(draft.Phone),
                Email = NormalizeEmail(draft.Email),
                Trn = NormalizeTrn(draft.Trn),
                Address = NormalizeText(draft.Address),
                Notes = NormalizeText(draft.Notes),
                IsActive = draft.IsActive,
                RepresentativeUserId = draft.RepresentativeUserId is int id && id > 0 ? id : null
            };
        }

        private static void AddIfPresent(
            IDictionary<string, string> errors, string field, string? message)
        {
            if (!string.IsNullOrEmpty(message)) errors[field] = message!;
        }
    }
}
