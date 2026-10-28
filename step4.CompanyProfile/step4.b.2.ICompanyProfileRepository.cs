// ═══════════════════════════════════════════════════════════════════════
// FILE: step4.CompanyProfile/step4.b.2.ICompanyProfileRepository.cs
// MODULE: step4 — the contract other modules are allowed to depend on.
//
// Two interfaces, on purpose
//   • <see cref="ICompanyProfileReader"/> is the READ path. A future document
//     module depends on this and nothing else: it can ask for the company
//     identity, and it cannot accidentally write it.
//   • <see cref="ICompanyProfileRepository"/> adds the write. Only the Company
//     Profile screen (and the verification executable) needs that one.
//
// Nobody copies the company name, TRN, logo or bank details into another
// table. That is the whole point of this module: the identity has ONE owner.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Company;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Company
{
    /// <summary>Read path for the company identity.</summary>
    public interface ICompanyProfileReader
    {
        /// <summary>
        /// The stored profile, or null when nothing has been saved yet.
        /// </summary>
        /// <remarks>
        /// Null is a real state: a fresh installation has no company profile
        /// until somebody fills one in. A document module must handle it rather
        /// than print a blank letterhead.
        /// </remarks>
        Task<CompanyProfile?> GetAsync(CancellationToken ct = default);

        /// <summary>The identity a document needs, or null when none is stored.</summary>
        Task<CompanyIdentitySnapshot?> GetIdentityAsync(CancellationToken ct = default);

        /// <summary>
        /// Absolute path of the stored logo image, or null when there is none or
        /// the file no longer exists. Never throws: a deleted logo is "no logo".
        /// </summary>
        string? ResolveLogoPath(string? logoFileName);
    }

    /// <summary>Company profile storage, including the one write path.</summary>
    public interface ICompanyProfileRepository : ICompanyProfileReader
    {
        /// <summary>
        /// Ensures the ERP file and the CompanyProfile table (migration 1016)
        /// exist. Safe to call more than once.
        /// </summary>
        Task InitializeAsync(CancellationToken ct = default);

        /// <summary>True when the table and every column this module needs exist.</summary>
        Task<bool> IsSchemaReadyAsync(CancellationToken ct = default);

        /// <summary>
        /// Saves the profile: inserts the single row the first time, updates the
        /// same row afterwards.
        /// </summary>
        /// <remarks>
        /// Refused unless the signed-in user is an active Administrator. Hiding the
        /// Save button is not the check — the write itself resolves the operator
        /// from <c>AuthenticationContext</c> and refuses, exactly as the step3
        /// writes do.
        ///
        /// A newly picked logo is copied into the app data folder BEFORE the row is
        /// written, and the logo it replaces is deleted only AFTER the commit. If
        /// anything fails, the previous profile is left exactly as it was.
        /// </remarks>
        Task<CompanyProfileSaveResult> SaveAsync(
            CompanyProfileDraft draft, CancellationToken ct = default);

        /// <summary>
        /// Display name for a stored attribution id, or "" when it cannot be
        /// resolved. Names are resolved at read time, never copied into the row.
        /// </summary>
        Task<string> ResolveUserNameAsync(int? userId, CancellationToken ct = default);
    }
}
