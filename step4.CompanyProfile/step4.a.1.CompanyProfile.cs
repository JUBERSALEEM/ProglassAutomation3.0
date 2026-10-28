// ═══════════════════════════════════════════════════════════════════════
// FILE: step4.CompanyProfile/step4.a.1.CompanyProfile.cs
// MODULE: step4 — Company Profile (the business's own identity).
//
// Why this module exists
//   Every future document the ERP prints — proforma invoice, job order,
//   delivery note, tax invoice — carries the same identity: the company's
//   name, TRN, address, contact details, bank details and logo. Until now
//   that identity had nowhere to live, so each screen would have had to
//   invent its own copy of it.
//
// Boundaries (docs/11, docs/12)
//   • A company profile is NOT a client. Client Management owns customers;
//     this owns the business that runs this installation.
//   • There is exactly ONE profile per installation. ADV stored a company
//     directory for a multi-company installer; V3.0 has one company per
//     database, and the single-row invariant is enforced by the database
//     itself (step4.b.1) rather than by hiding a "New" button.
//   • Nothing here is a transaction. No numbering, no VAT defaults, no
//     currency, no document rendering: those belong to step6 and later.
//
// The logo reference is a FILE NAME, not a path
//   The asset lives in the application-data folder (step4.a.3) and the row
//   stores only its name. An absolute path would break the moment the data
//   folder moved, was backed up to another machine, or the profile was
//   restored beside a different installation.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Models.Company
{
    /// <summary>
    /// How the logo is composed into a document header.
    /// </summary>
    /// <remarks>
    /// Two compositions, taken from the ADV reference and reduced to what V3.0
    /// documents need:
    ///   <see cref="LeftPortrait"/> — a portrait/compact logo to the LEFT of the
    ///   company identity, on the same line as the name and contact details.
    ///   <see cref="TopPanel"/> — a wide masthead: the logo and the identity share
    ///   the full width of the header band, which is what a wordmark or a
    ///   letterhead strip needs.
    /// Both preserve the image's aspect ratio; neither stretches it.
    /// </remarks>
    public enum CompanyLogoLayout
    {
        /// <summary>No logo: a text-only letterhead.</summary>
        None = 0,

        /// <summary>Logo to the left of the identity (portrait documents).</summary>
        LeftPortrait = 1,

        /// <summary>Logo inside a full-width top masthead (landscape documents).</summary>
        TopPanel = 2
    }

    /// <summary>Wording for a logo layout, in one place.</summary>
    public static class CompanyLogoLayouts
    {
        public static string Label(CompanyLogoLayout layout) => layout switch
        {
            CompanyLogoLayout.LeftPortrait => "Left of the identity (portrait)",
            CompanyLogoLayout.TopPanel => "Top masthead (full width)",
            _ => "No logo (text only)"
        };

        /// <summary>Longer explanation used beside the choice.</summary>
        public static string Description(CompanyLogoLayout layout) => layout switch
        {
            CompanyLogoLayout.LeftPortrait =>
                "The logo sits to the left of the company name and contact details. "
                + "Best for a stacked or square mark.",
            CompanyLogoLayout.TopPanel =>
                "The logo and the company details share the full width of the header band. "
                + "Best for a wide wordmark or a letterhead strip.",
            _ => "The letterhead is text only. The logo is kept on file and can be shown again."
        };

        /// <summary>True when the value is one the module knows about.</summary>
        public static bool IsDefined(int value) =>
            value is (int)CompanyLogoLayout.None
                  or (int)CompanyLogoLayout.LeftPortrait
                  or (int)CompanyLogoLayout.TopPanel;

        public static bool IsDefined(CompanyLogoLayout layout) => IsDefined((int)layout);
    }

    /// <summary>
    /// The company's stored profile — one row, always <see cref="SingleId"/>.
    /// </summary>
    public sealed class CompanyProfile
    {
        /// <summary>
        /// The one and only profile id. The table's CHECK constraint allows no
        /// other value, so "create a second company" is not a validation rule
        /// that a caller could forget — it is a constraint the write fails on.
        /// </summary>
        public const int SingleId = 1;

        public int Id { get; set; } = SingleId;

        // ── Business identity ────────────────────────────────────────────

        /// <summary>Registered company name. Required.</summary>
        public string CompanyName { get; set; } = "";

        /// <summary>UAE Tax Registration Number: 15 digits when present.</summary>
        public string Trn { get; set; } = "";

        /// <summary>Full business address, as it should print on a document.</summary>
        public string Address { get; set; } = "";

        /// <summary>Official telephone. Optional; used on printed material.</summary>
        public string Telephone { get; set; } = "";

        /// <summary>Website. Optional; printed under the address.</summary>
        public string Website { get; set; } = "";

        // ── Representative and contact ───────────────────────────────────

        /// <summary>Authorised representative / contact person.</summary>
        public string RepresentativeName { get; set; } = "";

        /// <summary>Representative's mobile number.</summary>
        public string RepresentativeMobile { get; set; } = "";

        /// <summary>Company e-mail address, as printed.</summary>
        public string Email { get; set; } = "";

        // ── Banking ──────────────────────────────────────────────────────

        /// <summary>Bank name. Separate from the account, so neither is a blob.</summary>
        public string BankName { get; set; } = "";

        /// <summary>Account number and/or IBAN.</summary>
        public string AccountNumber { get; set; } = "";

        // ── Logo ─────────────────────────────────────────────────────────

        /// <summary>
        /// The stored asset's FILE NAME inside the company-logo folder, or ""
        /// when there is no logo. Never an absolute path — see step4.a.3.
        /// </summary>
        public string LogoFileName { get; set; } = "";

        public CompanyLogoLayout LogoLayout { get; set; } = CompanyLogoLayout.None;

        /// <summary>
        /// The logo's long edge in millimetres on a document. Clamped to
        /// <see cref="CompanyProfileRules.MinLogoSizeMm"/>–<see cref="CompanyProfileRules.MaxLogoSizeMm"/>
        /// by the rules layer and again at render time. One dimension only, on
        /// purpose: the other one follows the image's own aspect ratio, so a
        /// stored value can never distort a logo.
        /// </summary>
        public double LogoSizeMm { get; set; } = CompanyProfileRules.DefaultLogoSizeMm;

        /// <summary>True when a logo file name is recorded.</summary>
        public bool HasLogo => LogoFileName.Length > 0;

        // ── Audit ────────────────────────────────────────────────────────

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        /// <summary>The user who first saved the profile; null before the first save.</summary>
        public int? CreatedByUserId { get; set; }

        /// <summary>The user who saved it last.</summary>
        public int? UpdatedByUserId { get; set; }

        /// <summary>A deep copy, so a rejected save cannot mutate the loaded state.</summary>
        public CompanyProfile Clone() => new()
        {
            Id = Id,
            CompanyName = CompanyName,
            Trn = Trn,
            Address = Address,
            Telephone = Telephone,
            Website = Website,
            RepresentativeName = RepresentativeName,
            RepresentativeMobile = RepresentativeMobile,
            Email = Email,
            BankName = BankName,
            AccountNumber = AccountNumber,
            LogoFileName = LogoFileName,
            LogoLayout = LogoLayout,
            LogoSizeMm = LogoSizeMm,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            CreatedByUserId = CreatedByUserId,
            UpdatedByUserId = UpdatedByUserId
        };

        /// <summary>
        /// The identity a document needs, detached from persistence.
        /// </summary>
        /// <remarks>
        /// Future modules read this instead of copying the company name, TRN,
        /// bank details or logo name into their own tables. It is a snapshot of
        /// the CURRENT profile: when a document needs the identity as it stood
        /// at issue time, that historical copy is the document's own business
        /// (recorded as future work in docs/12, not implemented here).
        /// </remarks>
        public CompanyIdentitySnapshot ToSnapshot() => new(
            CompanyName, Trn, Address, Telephone, Website,
            RepresentativeName, RepresentativeMobile, Email,
            BankName, AccountNumber, LogoFileName, LogoLayout,
            CompanyProfileRules.ClampLogoSize(LogoSizeMm));
    }

    /// <summary>Immutable company identity for a document to print.</summary>
    public sealed record CompanyIdentitySnapshot(
        string CompanyName,
        string Trn,
        string Address,
        string Telephone,
        string Website,
        string RepresentativeName,
        string RepresentativeMobile,
        string Email,
        string BankName,
        string AccountNumber,
        string LogoFileName,
        CompanyLogoLayout LogoLayout,
        double LogoSizeMm)
    {
        /// <summary>True when there is an identity to print at all.</summary>
        public bool IsEmpty => CompanyName.Length == 0;

        public bool HasLogo => LogoFileName.Length > 0;
    }

    /// <summary>
    /// What the editor submits. Separate from <see cref="CompanyProfile"/> so a
    /// caller cannot hand over an id or an audit stamp, and so the logo can be
    /// described as "unchanged", "cleared" or "a new file to import".
    /// </summary>
    public sealed class CompanyProfileDraft
    {
        public string CompanyName { get; set; } = "";
        public string Trn { get; set; } = "";
        public string Address { get; set; } = "";
        public string Telephone { get; set; } = "";
        public string Website { get; set; } = "";
        public string RepresentativeName { get; set; } = "";
        public string RepresentativeMobile { get; set; } = "";
        public string Email { get; set; } = "";
        public string BankName { get; set; } = "";
        public string AccountNumber { get; set; } = "";

        /// <summary>
        /// The logo that should be stored after this save: "" clears it, an
        /// existing asset name keeps it, and a new file is supplied through
        /// <see cref="PendingLogoSourcePath"/>.
        /// </summary>
        public string LogoFileName { get; set; } = "";

        /// <summary>
        /// A file the operator has chosen and that is not yet an asset. When
        /// set, the repository imports it, stores the new name and only then
        /// removes the asset it replaced.
        /// </summary>
        public string PendingLogoSourcePath { get; set; } = "";

        public CompanyLogoLayout LogoLayout { get; set; } = CompanyLogoLayout.None;

        public double LogoSizeMm { get; set; } = CompanyProfileRules.DefaultLogoSizeMm;
    }

    /// <summary>Outcome of a save, with per-field messages when it is refused.</summary>
    public sealed class CompanyProfileSaveResult
    {
        public bool Succeeded { get; init; }

        public CompanyProfile? Profile { get; init; }

        /// <summary>Operator-facing summary; empty when there is nothing to say.</summary>
        public string Message { get; init; } = "";

        /// <summary>Field name → message, for the fields the operator must fix.</summary>
        public System.Collections.Generic.IReadOnlyDictionary<string, string> FieldErrors { get; init; }
            = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);

        public static CompanyProfileSaveResult Ok(CompanyProfile profile, string message) =>
            new() { Succeeded = true, Profile = profile, Message = message };

        public static CompanyProfileSaveResult Fail(
            string message,
            System.Collections.Generic.IReadOnlyDictionary<string, string>? fieldErrors = null) =>
            new()
            {
                Succeeded = false,
                Message = message,
                FieldErrors = fieldErrors
                    ?? new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
            };
    }
}
