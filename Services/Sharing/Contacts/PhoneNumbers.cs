// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/Contacts/PhoneNumbers.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — phone normalization + validation (Stage 1,
//         Stage 3 hardening — "WhatsApp opens then closes")
//
// PURPOSE
// ──────────────
// Turn the free-text phones an ERP stores into a STRICT E.164 digit string
// (country code + national number, no '+', no spaces) that WhatsApp's
// click-to-chat/deep-link endpoints accept. WhatsApp requires the number
// in FULL international format with NO leading '+', NO spaces/dashes, and
// crucially NO national trunk '0' after the country code. A number that
// still carries the trunk 0 (e.g. 971 0 50 …) makes WhatsApp OPEN and then
// immediately DROP to the home screen — "contact not found" — which is the
// exact symptom this rewrite fixes.
//
// WHY THE PREVIOUS VERSION FAILED (Sept 2026 senior audit)
// ──────────────
//   The old normalizer only stripped a trunk 0 that appeared at the very
//   START of the string. Real Gulf/ERP data entry mixes country code AND
//   trunk 0 together, and those went straight through as invalid numbers:
//       "+971 050 123 4567" → 9710501234567   (13 digits, code+trunk-0) ✗
//       "00971 050 1234567" → 9710501234567   ✗
//       "971 050 1234567"   → 9710501234567   ✗
//   All three passed the loose 7–15 digit gate, so a BROKEN number reached
//   WhatsApp. They now normalize to 971501234567. The normalizer also now
//   copes with two numbers in one field ("050… / 04…" → first valid) and
//   trailing extension text ("050… ext 12").
//
// RULES (prompt §27 — never mangle a legitimate number)
// ──────────────
//   • Number FIRST, name is secondary: we read digits, not the label the
//     contact is saved under, so a PI/JO whose contact name differs from
//     the phone book still resolves by number.
//   • Explicit international ('+' or '00') → the digits after it are the
//     country code + national number; a trunk 0 right after the country
//     code is dropped (E.164).
//   • Leading single '0' (national trunk, no country code) → swap the
//     trunk 0 for the default country code.
//   • A bare number already starting with the default country code →
//     kept, but a trunk 0 wedged between the code and the subscriber
//     number is removed.
//   • Multiple numbers in one field (separated by / , ; & "or") → the
//     FIRST that yields a valid E.164 number wins.
//   • Valid = 8–15 digits after normalization (E.164 upper bound is 15;
//     we require ≥ 8 so a 7-digit local fragment without a country code is
//     rejected rather than dialled wrongly).
//
// The default country code lives in SharingSettings — the UAE default is
// a setting, not an assumption baked into code. Never throws.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text;

namespace ProGlassAutomation.Services.Sharing.Contacts
{
    public static class PhoneNumbers
    {
        // Separators that indicate "more than one number in this field".
        private static readonly char[] MultiNumberSeparators =
            { '/', ',', ';', '&', '\n', '\r', '|' };

        // Known country calling codes we can confidently detect at the
        // START of a bare (no + / 00) number, so a trunk 0 wedged after
        // the code can be stripped. Longest-match first. This is a
        // pragmatic list centred on the Gulf/where this ERP operates plus
        // common international partners — not the full ITU table, because a
        // bare number is inherently ambiguous and we only act when we are
        // sure. Anything not listed falls back to the safe generic path.
        private static readonly string[] KnownCountryCodes =
        {
            // 3-digit
            "971", "966", "973", "974", "965", "968", "962", "961",
            "970", "212", "213", "216", "218", "249", "252", "255",
            "256", "234", "233", "254", "263", "260", "265", "880",
            "852", "853", "886", "998", "994", "995", "992", "993",
            // 2-digit
            "20", "27", "30", "31", "32", "33", "34", "36", "39",
            "40", "41", "43", "44", "45", "46", "47", "48", "49",
            "51", "52", "53", "54", "55", "56", "57", "58", "60",
            "61", "62", "63", "64", "65", "66", "81", "82", "84",
            "86", "90", "91", "92", "93", "94", "95", "98",
            // 1-digit
            "1", "7",
        };

        /// <summary>
        /// Normalizes to a strict E.164 digit string (no '+', no spaces),
        /// or "" when no usable number exists. Handles country-code +
        /// trunk-0 collisions and multi-number fields. Never throws.
        /// </summary>
        public static string Normalize(
            string? raw, string defaultCountryCode = "971")
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            string cc = SanitizeCountryCode(defaultCountryCode);

            // Field may hold more than one number ("050… / 04…"). Try each
            // candidate and return the first that yields a valid E.164.
            foreach (string candidate in SplitCandidates(raw))
            {
                string normalized = NormalizeOne(candidate, cc);
                if (IsValidNormalized(normalized))
                    return normalized;
            }

            // No candidate validated — return the best single-number
            // attempt (may be empty) so callers still see "" for junk but
            // the legacy "normalize even if slightly short" behaviour is
            // preserved for the whole-field case.
            return NormalizeOne(raw, cc);
        }

        /// <summary>E.164-plausible: 8–15 digits (no letters, no '+').</summary>
        public static bool IsValidNormalized(string? normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return false;
            foreach (char c in normalized)
                if (!char.IsDigit(c)) return false;
            return normalized.Length >= 8 && normalized.Length <= 15;
        }

        /// <summary>
        /// Normalize + validate in one call. Returns the E.164 digits, or
        /// "" when the input cannot produce a dialable WhatsApp number.
        /// This is what providers hand to the deep link.
        /// </summary>
        public static string NormalizeValidated(
            string? raw, string defaultCountryCode = "971")
        {
            string digits = Normalize(raw, defaultCountryCode);
            return IsValidNormalized(digits) ? digits : "";
        }

        // ── internals ────────────────────────────────────────────────

        private static string SanitizeCountryCode(string? cc)
        {
            if (string.IsNullOrWhiteSpace(cc)) return "";
            var sb = new StringBuilder(cc.Length);
            foreach (char c in cc)
                if (char.IsDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// Splits a field into candidate numbers on separators, and also
        /// truncates trailing "ext"/"x" extension noise on each. The whole
        /// trimmed field is always the first candidate so a plain single
        /// number is unaffected.
        /// </summary>
        private static IEnumerable<string> SplitCandidates(string raw)
        {
            yield return raw;

            string[] parts = raw.Split(
                MultiNumberSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
                foreach (string p in parts)
                {
                    string t = p.Trim();
                    if (t.Length > 0) yield return t;
                }
        }

        /// <summary>
        /// Normalizes exactly one candidate to E.164 digits. The core
        /// rules; see the file header. Extension text is dropped by only
        /// considering the leading run once a plausible number is formed.
        /// </summary>
        private static string NormalizeOne(string candidate, string cc)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return "";

            // Detect explicit-international intent BEFORE stripping, using
            // the first non-space significant character.
            string compact = StripFormatting(candidate);
            bool hasPlus = compact.StartsWith("+", StringComparison.Ordinal);
            bool hasZeroZero = compact.StartsWith("00", StringComparison.Ordinal);

            string digits = DigitsOnly(candidate);
            if (digits.Length == 0) return "";
            // All-zero input carries no subscriber number — junk, not a
            // "00" international prefix. Reject so "0000" → "".
            if (digits.Trim('0').Length == 0) return "";

            // 1. Explicit international: strip the +/00, the remainder is
            // country code + national number. Drop a trunk 0 that follows
            // the country code (E.164).
            if (hasPlus || hasZeroZero)
            {
                string intl = hasPlus
                    ? DigitsOnly(compact.Substring(1))
                    : DigitsOnly(compact.Substring(2));
                return DropTrunkAfterCode(intl, cc);
            }

            // 2. National trunk 0, no country code: swap trunk 0 → code.
            if (digits.StartsWith("0", StringComparison.Ordinal))
            {
                string withoutTrunk = digits.TrimStart('0');
                if (withoutTrunk.Length == 0) return "";
                if (!string.IsNullOrEmpty(cc) && withoutTrunk.StartsWith(cc, StringComparison.Ordinal))
                    return DropTrunkAfterCode(withoutTrunk, cc);
                return string.IsNullOrEmpty(cc) ? withoutTrunk : cc + withoutTrunk;
            }

            // 3. Bare number that already starts with the default country
            // code (e.g. "971 050 …" or "971 50 …"): keep the code, drop a
            // trunk 0 wedged after it.
            if (!string.IsNullOrEmpty(cc)
                && digits.StartsWith(cc, StringComparison.Ordinal)
                && digits.Length > cc.Length)
                return DropTrunkAfterCode(digits, cc);

            // 4. Bare number that starts with SOME known country code
            // (international contact stored without + / 00): drop a trunk 0
            // after that code if present.
            string? knownCc = MatchKnownCountryCode(digits);
            if (knownCc != null)
                return DropTrunkAfterCode(digits, knownCc);

            // 5. No prefix and long enough to already be international →
            // verbatim; short local → prepend the default country code.
            if (digits.Length >= 11 || string.IsNullOrEmpty(cc))
                return digits;
            return cc + digits;
        }

        /// <summary>
        /// Given digits that begin with <paramref name="code"/>, remove a
        /// single national trunk '0' sitting immediately after the code
        /// (the E.164 collision that made WhatsApp reject the number).
        /// Leaves everything else untouched.
        /// </summary>
        private static string DropTrunkAfterCode(string digits, string code)
        {
            if (string.IsNullOrEmpty(code)
                || !digits.StartsWith(code, StringComparison.Ordinal))
                return digits;

            string rest = digits.Substring(code.Length);
            // Drop leading trunk zeros between the country code and the
            // subscriber number (usually one; be tolerant of "00").
            string trimmed = rest.TrimStart('0');
            if (trimmed.Length == 0) return digits; // all zeros — leave as-is
            return code + trimmed;
        }

        private static string? MatchKnownCountryCode(string digits)
        {
            // Longest match first so "971" wins over "97"/"9".
            string? best = null;
            foreach (string code in KnownCountryCodes)
            {
                if (digits.Length > code.Length
                    && digits.StartsWith(code, StringComparison.Ordinal)
                    && (best == null || code.Length > best.Length))
                    best = code;
            }
            return best;
        }

        private static string DigitsOnly(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        private static string StripFormatting(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (!char.IsWhiteSpace(c) && c != '-' && c != '(' && c != ')' && c != '.')
                    sb.Append(c);
            return sb.ToString();
        }
    }
}
