// ═══════════════════════════════════════════════════════════════════════
// FILE: step4.CompanyProfile/step4.b.4.CompanyProfileRepository.cs
// MODULE: step4 — persistence for the single company profile.
//
// The save is the interesting part, so it is spelled out here:
//
//   1. Authorise. The operator comes from AuthenticationContext — the signed-in
//      user, set only by a verified password (step3.b.7). No session, no write;
//      not an active Administrator, no write. The refusal is produced by the
//      repository, not by a disabled button.
//   2. Validate. The same rules the editor runs (step4.a.2), so a caller cannot
//      skip them.
//   3. Import a newly picked logo FIRST. A copy lands in the app data folder
//      under a new name (step4.a.3). If this fails, nothing has been written.
//   4. Write the row inside a transaction: insert when it does not exist,
//      update the same row (Id = 1) when it does. The table's CHECK constraint
//      makes a second profile impossible even if this method is bypassed.
//   5. Only after the commit, delete the logo the new one replaced. A failed
//      save therefore never leaves the profile pointing at a missing file, and
//      never destroys the previous logo.
//
// Everything a document needs is read through the same object
// (<see cref="ICompanyProfileReader"/>); nothing is duplicated elsewhere.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Company;
using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Inquiry;
using ProGlassAutomation.Services.Users;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Company
{
    /// <summary>EF Core implementation of <see cref="ICompanyProfileRepository"/>.</summary>
    public sealed class CompanyProfileRepository : ICompanyProfileRepository
    {
        private readonly Func<CompanyProfileDbContext> _createContext;
        private bool _initialized;

        public CompanyProfileRepository() : this(null) { }

        /// <summary>
        /// Test seam, exactly as step2/step3 use: production callers use the
        /// parameterless constructor (real inquiries.db); the verification
        /// executable points the same code at an isolated file through
        /// AppDataRoot.TestFolderOverride.
        /// </summary>
        internal CompanyProfileRepository(Func<CompanyProfileDbContext>? contextFactory)
        {
            _createContext = contextFactory ?? (() => new CompanyProfileDbContext());
        }

        private CompanyProfileDbContext CreateContext() => _createContext();

        // ── Initialization ───────────────────────────────────────────────

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (_initialized) return;

            // The ERP file is owned by the Inquiry initializer; it applies the one
            // version chain, which now includes 1016 (this table).
            await new InquiryRepository().InitializeAsync(ct).ConfigureAwait(false);

            if (!await IsSchemaReadyAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException(
                    "The company profile table (migration " + CompanyProfileSchema.MigrationVersion
                    + ") is missing after initialization. Schema upgrade, if any, reported: "
                    + (string.IsNullOrWhiteSpace(SchemaMigrationRunner.FailureSummary)
                        ? "no failure recorded."
                        : SchemaMigrationRunner.FailureSummary));

            _initialized = true;
        }

        public async Task<bool> IsSchemaReadyAsync(CancellationToken ct = default)
        {
            try
            {
                await using var db = CreateContext();
                await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
                var connection = (SqliteConnection)db.Database.GetDbConnection();

                using (var table = connection.CreateCommand())
                {
                    table.CommandText =
                        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
                    table.Parameters.AddWithValue("@name", CompanyProfileSchema.TableName);
                    long found = Convert.ToInt64(table.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
                    if (found == 0) return false;
                }

                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $@"PRAGMA table_info(""{CompanyProfileSchema.TableName}"");";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                        if (!reader.IsDBNull(1)) columns.Add(reader.GetString(1));
                }

                foreach (string required in CompanyProfileSchema.RequiredColumns)
                    if (!columns.Contains(required)) return false;
                return true;
            }
            catch (Exception)
            {
                // A readiness probe never throws: an unreadable file is "not ready".
                return false;
            }
        }

        // ── Reads ────────────────────────────────────────────────────────

        public async Task<CompanyProfile?> GetAsync(CancellationToken ct = default)
        {
            await using var db = CreateContext();
            return await db.CompanyProfiles.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == CompanyProfile.SingleId, ct)
                .ConfigureAwait(false);
        }

        public async Task<CompanyIdentitySnapshot?> GetIdentityAsync(CancellationToken ct = default)
        {
            CompanyProfile? profile = await GetAsync(ct).ConfigureAwait(false);
            return profile?.ToSnapshot();
        }

        public string? ResolveLogoPath(string? logoFileName) => CompanyLogoStore.ResolvePath(logoFileName);

        public async Task<string> ResolveUserNameAsync(int? userId, CancellationToken ct = default)
        {
            if (userId is not int id || id <= 0) return "";

            try
            {
                await using var db = CreateContext();
                await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
                var connection = (SqliteConnection)db.Database.GetDbConnection();

                using var command = connection.CreateCommand();
                command.CommandText = @"SELECT ""FullName"" FROM ""Users"" WHERE ""Id"" = @id;";
                command.Parameters.AddWithValue("@id", id);
                object? value = command.ExecuteScalar();
                return value is string name ? name : "";
            }
            catch (Exception)
            {
                // Attribution is a caption, not a gate: an unreadable directory
                // must not stop the profile from loading.
                return "";
            }
        }

        // ── Write ────────────────────────────────────────────────────────

        public async Task<CompanyProfileSaveResult> SaveAsync(
            CompanyProfileDraft draft, CancellationToken ct = default)
        {
            if (draft == null) return CompanyProfileSaveResult.Fail("No company details were supplied.");

            // 1 — Authorise.
            User? actor = AuthenticationContext.Current;
            if (actor == null)
                return CompanyProfileSaveResult.Fail(
                    "Sign in before changing the company profile. Nothing was saved.");
            if (!actor.IsActive || !UserCatalog.IsAdministrator(actor.Role))
                return CompanyProfileSaveResult.Fail(
                    "Only an Administrator can change the company profile. Ask an administrator, "
                    + "or sign in with an administrator account. Nothing was saved.");

            // 2 — Validate, with the same rules the editor ran.
            IReadOnlyDictionary<string, string> errors = CompanyProfileRules.Validate(draft);
            if (errors.Count > 0)
                return CompanyProfileSaveResult.Fail(CompanyProfileRules.Describe(errors), errors);

            CompanyProfileDraft normalized = CompanyProfileRules.Normalize(draft);

            string? existingLogoName = normalized.LogoFileName;
            if (existingLogoName.Length > 0 && !CompanyLogoStore.IsSafeFileName(existingLogoName))
                return CompanyProfileSaveResult.Fail(
                    "The stored logo name is not one this application writes. Remove the logo and choose "
                    + "the image again.");

            // 3 — Import a newly picked image before anything is written.
            string importedLogoName = "";
            if (normalized.PendingLogoSourcePath.Length > 0)
            {
                CompanyLogoImportResult imported = CompanyLogoStore.Import(normalized.PendingLogoSourcePath);
                if (!imported.Succeeded)
                    return CompanyProfileSaveResult.Fail(imported.Message);
                importedLogoName = imported.FileName;
            }

            string finalLogoName = importedLogoName.Length > 0 ? importedLogoName : existingLogoName;
            string? previousLogoName = null;
            bool committed = false;

            try
            {
                await using var db = CreateContext();
                await using var transaction = await db.Database
                    .BeginTransactionAsync(ct).ConfigureAwait(false);

                // TRACKED row: this path writes. A no-tracking row would accept
                // every assignment and save nothing (the step3 round-27 defect).
                CompanyProfile? stored = await db.CompanyProfiles
                    .FirstOrDefaultAsync(x => x.Id == CompanyProfile.SingleId, ct)
                    .ConfigureAwait(false);

                DateTime now = DateTime.UtcNow;

                if (stored == null)
                {
                    stored = new CompanyProfile
                    {
                        Id = CompanyProfile.SingleId,
                        CreatedAt = now,
                        CreatedByUserId = actor.Id
                    };
                    db.CompanyProfiles.Add(stored);
                }
                else
                {
                    previousLogoName = stored.LogoFileName;
                }

                stored.CompanyName = normalized.CompanyName;
                stored.Trn = normalized.Trn;
                stored.Address = normalized.Address;
                stored.Telephone = normalized.Telephone;
                stored.Website = normalized.Website;
                stored.RepresentativeName = normalized.RepresentativeName;
                stored.RepresentativeMobile = normalized.RepresentativeMobile;
                stored.Email = normalized.Email;
                stored.BankName = normalized.BankName;
                stored.AccountNumber = normalized.AccountNumber;
                stored.LogoFileName = finalLogoName;
                stored.LogoLayout = finalLogoName.Length == 0
                    ? CompanyLogoLayout.None
                    : normalized.LogoLayout;
                stored.LogoSizeMm = normalized.LogoSizeMm;
                stored.UpdatedAt = now;
                stored.UpdatedByUserId = actor.Id;

                int written = await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                if (written < 1)
                    throw new InvalidOperationException(
                        "The company profile was accepted and stored nothing.");

                await transaction.CommitAsync(ct).ConfigureAwait(false);
                committed = true;

                // 5 — Only now is the replaced asset removed.
                if (previousLogoName != null
                    && previousLogoName.Length > 0
                    && !string.Equals(previousLogoName, finalLogoName, StringComparison.Ordinal))
                {
                    CompanyLogoStore.Delete(previousLogoName);
                }

                return CompanyProfileSaveResult.Ok(
                    stored.Clone(),
                    "The company profile was saved."
                    + (finalLogoName.Length == 0 ? " No logo is stored." : " The logo is stored with the profile."));
            }
            catch (Exception error)
            {
                // Nothing was committed: drop the imported copy so a failed save
                // leaves no orphan, and leave the previous logo untouched.
                if (!committed && importedLogoName.Length > 0) CompanyLogoStore.Delete(importedLogoName);

                return CompanyProfileSaveResult.Fail(
                    "The company profile could not be saved (" + error.GetType().Name + "): "
                    + error.Message + " Nothing was changed.");
            }
        }
    }
}
