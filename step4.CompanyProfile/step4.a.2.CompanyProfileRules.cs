// ═══════════════════════════════════════════════════════════════════════
// FILE: step4.CompanyProfile/step4.a.2.CompanyProfileRules.cs
// MODULE: step4 — what the company profile accepts, in one place.
//
// The same rules run in three places and must never diverge: the editor (so a
// field says what is wrong as it is typed), the repository (so a caller cannot
// bypass the editor), and the verification executable.
//
// Rules, and why each is what it is
//   • Company name is required. That is the only mandatory field: a business
//     that has not filled in a bank account yet is still a business, and
//     inventing a "you must have an email" law would block first use.
//   • TRN is optional but, when present, must be 15 digits — the UAE Federal
//     Tax Authority length. The Client module already normalises TRN to digits
//     (step2.a.2); this follows the same convention and adds the length the
//     FTA actually uses, because a company's own TRN prints on every invoice.
//   • Contact fields are validated for SHAPE, not for existence: an e-mail that
//     is present must look like one, a mobile that is present must have enough
//     digits to be dialable.
//   • Everything is length-bounded so a paste cannot overflow a column that the
//     document renderer will later print.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Company;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProGlassAutomation.Models.Company
{
    /// <summary>Validation and normalisation for the company profile.</summary>
    public static class CompanyProfileRules
    {
        public const int CompanyNameMaxLength = 200;
        public const int TrnLength = 15;
        public const int AddressMaxLength = 300;
        public const int TelephoneMaxLength = 40;
        public const int WebsiteMaxLength = 200;
        public const int RepresentativeNameMaxLength = 120;
        public const int MobileMaxLength = 30;
        public const int EmailMaxLength = 120;
        public const int BankNameMaxLength = 120;
        public const int AccountNumberMaxLength = 80;

        /// <summary>Digits a mobile number must contain to be dialable.</summary>
        public const int MobileMinDigits = 7;

        /// <summary>Default printed logo size: the long edge, in millimetres.</summary>
        public const double DefaultLogoSizeMm = 24;

        /// <summary>Smallest printed logo size the renderer accepts.</summary>
        public const double MinLogoSizeMm = 8;

        /// <summary>Largest printed logo size the renderer accepts.</summary>
        public const double MaxLogoSizeMm = 80;

        // ── Normalisation ────────────────────────────────────────────────

        /// <summary>Trims and collapses internal whitespace runs to one space.</summary>
        public static string NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var builder = new StringBuilder(value.Length);
            bool space = false;
            foreach (char character in value.Trim())
            {
                if (char.IsWhiteSpace(character))
                {
                    space = true;
                    continue;
                }

                if (space) builder.Append(' ');
                space = false;
                builder.Append(character);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Multi-line text: each line is trimmed and blank lines are DROPPED, so an
        /// address pasted with ragged indentation or stray empty lines prints
        /// evenly.
        ///
        /// Dropped, not collapsed (round 34.5). This used to keep one empty line
        /// between blocks, while the letterhead renderer
        /// (<c>CompanyProfileLogoViewModel.AddressLines</c>) already skipped every
        /// blank — so the operator saved four lines and the page printed three, and
        /// the stored value disagreed with the document it feeds. docs/12 states
        /// the rule plainly: "lines preserved, blank lines dropped".
        /// </summary>
        public static string NormalizeAddress(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var lines = new List<string>();
            foreach (string raw in value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length > 0) lines.Add(line);
            }

            return string.Join(Environment.NewLine, lines);
        }

        public static string DigitsOnly(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
                if (character >= '0' && character <= '9') builder.Append(character);
            return builder.ToString();
        }

        /// <summary>TRN as stored: digits only.</summary>
        public static string NormalizeTrn(string? value) => DigitsOnly(value);

        /// <summary>Clamps a printed logo size into the range the renderer uses.</summary>
        public static double ClampLogoSize(double millimetres) =>
            double.IsNaN(millimetres) || millimetres <= 0
                ? DefaultLogoSizeMm
                : Math.Clamp(millimetres, MinLogoSizeMm, MaxLogoSizeMm);

        // ── Field validation (null = valid) ──────────────────────────────

        public static string? ValidateCompanyName(string? value)
        {
            string name = NormalizeText(value);
            if (name.Length == 0) return "Company name is required.";
            if (name.Length > CompanyNameMaxLength)
                return $"Company name must be {CompanyNameMaxLength} characters or fewer.";
            return null;
        }

        /// <summary>
        /// TRN is optional; when present it must be the 15 digits the FTA issues.
        /// Separators (spaces, dashes) are tolerated on input and removed.
        /// </summary>
        public static string? ValidateTrn(string? value)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0) return null;

            string digits = DigitsOnly(text);
            if (digits.Length != text.Replace(" ", "").Replace("-", "").Length)
                return "TRN must contain digits only.";
            if (digits.Length != TrnLength)
                return $"TRN must be {TrnLength} digits (the UAE Tax Registration Number length).";
            return null;
        }

        public static string? ValidateAddress(string? value)
        {
            string text = NormalizeAddress(value);
            if (text.Length > AddressMaxLength)
                return $"Address must be {AddressMaxLength} characters or fewer.";
            return null;
        }

        public static string? ValidateTelephone(string? value)
        {
            string text = NormalizeText(value);
            if (text.Length == 0) return null;
            if (text.Length > TelephoneMaxLength)
                return $"Telephone must be {TelephoneMaxLength} characters or fewer.";
            if (DigitsOnly(text).Length < MobileMinDigits)
                return "Telephone needs at least 7 digits to be dialable.";
            return null;
        }

        public static string? ValidateWebsite(string? value)
        {
            string text = NormalizeText(value);
            if (text.Length == 0) return null;
            if (text.Length > WebsiteMaxLength)
                return $"Website must be {WebsiteMaxLength} characters or fewer.";
            if (text.Contains(' ')) return "Website must not contain spaces.";
            if (text.Contains('@')) return "Website looks like an e-mail address; use the e-mail field.";
            return null;
        }

        public static string? ValidateRepresentativeName(string? value)
        {
            string text = NormalizeText(value);
            if (text.Length > RepresentativeNameMaxLength)
                return $"Representative must be {RepresentativeNameMaxLength} characters or fewer.";
            return null;
        }

        public static string? ValidateMobile(string? value)
        {
            string text = NormalizeText(value);
            if (text.Length == 0) return null;
            if (text.Length > MobileMaxLength)
                return $"Mobile must be {MobileMaxLength} characters or fewer.";
            if (DigitsOnly(text).Length < MobileMinDigits)
                return "Mobile needs at least 7 digits to be dialable.";
            return null;
        }

        /// <summary>
        /// E-mail is optional; when present it must have the shape a document can
        /// print: one @, something on both sides, and a dot in the domain.
        /// </summary>
        public static string? ValidateEmail(string? value)
        {
            string text = NormalizeText(value);
            if (text.Length == 0) return null;
            if (text.Length > EmailMaxLength)
                return $"E-mail must be {EmailMaxLength} characters or fewer.";

            int at = text.IndexOf('@');
            if (at <= 0 || at != text.LastIndexOf('@') || at == text.Length - 1)
                return "E-mail needs one @ with a name before it and a domain after it.";

            string domain = text[(at + 1)..];
            if (!domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.'))
                return "E-mail domain needs a dot, for example company.ae.";
            if (text.Contains(' ')) return "E-mail must not contain spaces.";
            return null;
        }

        public static string? ValidateBankName(string? value)
        {
            string text = NormalizeText(value);
            if (text.Length > BankNameMaxLength)
                return $"Bank name must be {BankNameMaxLength} characters or fewer.";
            return null;
        }

        /// <summary>
        /// Account number / IBAN is optional. An IBAN, once the spaces are
        /// removed, is letters and digits only; a plain account number is
        /// usually digits. Anything else is a paste mistake worth catching.
        /// </summary>
        public static string? ValidateAccountNumber(string? value)
        {
            string text = NormalizeText(value);
            if (text.Length == 0) return null;
            if (text.Length > AccountNumberMaxLength)
                return $"Account number must be {AccountNumberMaxLength} characters or fewer.";

            string compact = text.Replace(" ", "");
            foreach (char character in compact)
            {
                if (char.IsLetterOrDigit(character)) continue;
                return "Account number / IBAN may contain letters, digits and spaces only.";
            }

            return null;
        }

        /// <summary>Logo layout must be one the module defines.</summary>
        public static string? ValidateLogoLayout(CompanyLogoLayout layout) =>
            CompanyLogoLayouts.IsDefined(layout)
                ? null
                : "Choose how the logo is shown on documents.";

        /// <summary>Logo size must sit inside the printed range.</summary>
        public static string? ValidateLogoSize(double millimetres) =>
            double.IsNaN(millimetres) || millimetres < MinLogoSizeMm || millimetres > MaxLogoSizeMm
                ? $"Logo size must be between {MinLogoSizeMm:0} and {MaxLogoSizeMm:0} mm."
                : null;

        // ── Whole draft ──────────────────────────────────────────────────

        /// <summary>
        /// Every field message, keyed by the draft property the editor binds.
        /// An empty dictionary means the draft may be saved.
        /// </summary>
        public static IReadOnlyDictionary<string, string> Validate(CompanyProfileDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));

            var errors = new Dictionary<string, string>(StringComparer.Ordinal);
            Add(errors, nameof(CompanyProfileDraft.CompanyName), ValidateCompanyName(draft.CompanyName));
            Add(errors, nameof(CompanyProfileDraft.Trn), ValidateTrn(draft.Trn));
            Add(errors, nameof(CompanyProfileDraft.Address), ValidateAddress(draft.Address));
            Add(errors, nameof(CompanyProfileDraft.Telephone), ValidateTelephone(draft.Telephone));
            Add(errors, nameof(CompanyProfileDraft.Website), ValidateWebsite(draft.Website));
            Add(errors, nameof(CompanyProfileDraft.RepresentativeName),
                ValidateRepresentativeName(draft.RepresentativeName));
            Add(errors, nameof(CompanyProfileDraft.RepresentativeMobile), ValidateMobile(draft.RepresentativeMobile));
            Add(errors, nameof(CompanyProfileDraft.Email), ValidateEmail(draft.Email));
            Add(errors, nameof(CompanyProfileDraft.BankName), ValidateBankName(draft.BankName));
            Add(errors, nameof(CompanyProfileDraft.AccountNumber), ValidateAccountNumber(draft.AccountNumber));
            Add(errors, nameof(CompanyProfileDraft.LogoLayout), ValidateLogoLayout(draft.LogoLayout));
            Add(errors, nameof(CompanyProfileDraft.LogoSizeMm), ValidateLogoSize(draft.LogoSizeMm));

            // A placement without a logo would print an empty panel, so it is
            // refused and the message says which field to change.
            //
            // The mirror case is deliberately ALLOWED: a stored logo with the
            // "No logo" placement means "keep the image on file, print text only".
            // That is a real choice a business makes (a plain letterhead with the
            // artwork ready for the day it is wanted), so it is not an error. The
            // asset stays in the app-data folder and switching the placement back
            // brings it straight back into the header.
            bool hasLogo = draft.LogoFileName.Length > 0 || draft.PendingLogoSourcePath.Length > 0;
            if (!hasLogo && draft.LogoLayout != CompanyLogoLayout.None)
                errors[nameof(CompanyProfileDraft.LogoLayout)] =
                    "Choose a logo before choosing how it is shown, or set the placement to \"No logo\".";

            return errors;
        }

        /// <summary>The draft the editor starts from when nothing is stored yet.</summary>
        public static CompanyProfileDraft EmptyDraft() => new()
        {
            LogoLayout = CompanyLogoLayout.None,
            LogoSizeMm = DefaultLogoSizeMm
        };

        /// <summary>A draft carrying an existing profile, for editing.</summary>
        public static CompanyProfileDraft ToDraft(CompanyProfile profile) => new()
        {
            CompanyName = profile.CompanyName,
            Trn = profile.Trn,
            Address = profile.Address,
            Telephone = profile.Telephone,
            Website = profile.Website,
            RepresentativeName = profile.RepresentativeName,
            RepresentativeMobile = profile.RepresentativeMobile,
            Email = profile.Email,
            BankName = profile.BankName,
            AccountNumber = profile.AccountNumber,
            LogoFileName = profile.LogoFileName,
            LogoLayout = profile.LogoLayout,
            LogoSizeMm = ClampLogoSize(profile.LogoSizeMm)
        };

        /// <summary>The normalised draft the repository persists.</summary>
        public static CompanyProfileDraft Normalize(CompanyProfileDraft draft) => new()
        {
            CompanyName = NormalizeText(draft.CompanyName),
            Trn = NormalizeTrn(draft.Trn),
            Address = NormalizeAddress(draft.Address),
            Telephone = NormalizeText(draft.Telephone),
            Website = NormalizeText(draft.Website),
            RepresentativeName = NormalizeText(draft.RepresentativeName),
            RepresentativeMobile = NormalizeText(draft.RepresentativeMobile),
            Email = NormalizeText(draft.Email),
            BankName = NormalizeText(draft.BankName),
            AccountNumber = NormalizeText(draft.AccountNumber),
            LogoFileName = (draft.LogoFileName ?? "").Trim(),
            PendingLogoSourcePath = (draft.PendingLogoSourcePath ?? "").Trim(),
            LogoLayout = draft.LogoLayout,
            LogoSizeMm = ClampLogoSize(draft.LogoSizeMm)
        };

        /// <summary>
        /// One sentence covering every field message, for the summary surface
        /// above the form. Field messages stay beside their own field.
        /// </summary>
        public static string Describe(IReadOnlyDictionary<string, string> errors)
        {
            if (errors == null || errors.Count == 0) return "";
            var parts = new List<string>(errors.Count);
            foreach (var pair in errors) parts.Add(pair.Value);
            return parts.Count == 1
                ? parts[0]
                : string.Format(CultureInfo.InvariantCulture, "Fix {0} fields: ", parts.Count)
                  + string.Join(" ", parts);
        }

        private static void Add(Dictionary<string, string> errors, string field, string? message)
        {
            if (message != null) errors[field] = message;
        }
    }
}
