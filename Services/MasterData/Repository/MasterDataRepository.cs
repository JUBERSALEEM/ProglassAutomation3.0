// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/MasterData/Repository/MasterDataRepository.cs
// ═══════════════════════════════════════════════════════════════════════
// CHAT 15 — PROJECT REFERENCE FIELD ADDED
//   3 precise changes:
//     1. SaveOrUpdateProjectAsync update-branch: persist ProjectReference
//     2. AutoSaveFromInvoiceAsync signature: add projectReference param
//     3. AutoSaveFromInvoiceAsync project block: pass ProjectReference
//   All other code, comments, methods preserved byte-for-byte.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.MasterData;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.MasterData.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Database;

using ProGlassAutomation.Services.Licensing;         // ReadOnlyGate
namespace ProGlassAutomation.Services.MasterData.Repository
{
    /// <summary>
    /// EF Core implementation of IMasterDataRepository.
    /// Each method creates a fresh DbContext (matches app-wide pattern).
    ///
    /// Upsert pattern:
    ///   1. Query for existing by uniqueness key (case-insensitive)
    ///   2. If found → update all fields + increment UsageCount + set LastUsedAt
    ///   3. If not found → insert with UsageCount=1 + set CreatedAt + LastUsedAt
    ///
    /// List pattern:
    ///   All GetAll methods return ordered by UsageCount DESC then
    ///   LastUsedAt DESC. Most-frequently-used records appear first
    ///   in card lists and dropdowns.
    ///
    /// Search pattern:
    ///   Case-insensitive CONTAINS using EF.Functions.Like for SQLite.
    ///   Returns max 20 results (prevents UI overload on large datasets).
    ///
    /// Auto-save:
    ///   AutoSaveFromInvoiceAsync() is the bulk entry point called by
    ///   ProformaInvoiceMapper after every invoice save. Skips entities
    ///   with empty primary name fields — no garbage rows in master data.
    /// </summary>
    public class MasterDataRepository : IMasterDataRepository
    {
        private const int SEARCH_LIMIT = 20;

        // PHASE 4 additive seam: tests inject an isolated-database factory
        // to exercise SaveOrUpdateCompanyAsync semantics end-to-end (latest
        // -wins vs preserve-on-blank, rename-collision, stale-Id) without
        // touching the real masterdata.db. Production callers use the
        // parameterless ctor — behaviour unchanged.
        private readonly Func<MasterDataDbContext> _createContext;

        public MasterDataRepository() : this(null) { }

        internal MasterDataRepository(Func<MasterDataDbContext>? contextFactory)
        {
            _createContext = contextFactory ?? (() => new MasterDataDbContext());
        }

        private MasterDataDbContext CreateContext() => _createContext();

        public async Task InitializeAsync()
        {
            using var ctx = new MasterDataDbContext();
            await ctx.Database.EnsureCreatedAsync();

            // Additive schema upgrades for an existing masterdata.db
            // (future versioned migrations; empty today = no-op).
            SchemaMigrationRunner.Upgrade(
                MasterDataDbContext.DatabaseFilePath,
                AppSchemaMigrations.MasterData,
                "Master Data");

            // Schema evolution for databases created before v2 company
            // profile fields existed. EnsureCreated never ALTERs an
            // existing file, so add missing columns explicitly.
            // (Legacy inline migrator — to be folded into AppSchemaMigrations
            // in a later verified phase.)
            await EvolveCompanyColumnsAsync(ctx);
        }

        private static async Task EvolveCompanyColumnsAsync(
            MasterDataDbContext ctx)
        {
            var existing = new System.Collections.Generic.HashSet<string>(
                System.StringComparer.OrdinalIgnoreCase);

            using (var cmd = ctx.Database.GetDbConnection().CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(Companies);";
                await ctx.Database.OpenConnectionAsync();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    existing.Add(reader.GetString(1));
            }

            // (column, DDL type + default) — order matters only for
            // readability; each ALTER is independent.
            var columns = new (string Name, string Ddl)[]
            {
                ("LogoPath", "TEXT NOT NULL DEFAULT ''"),
                ("Representative", "TEXT NOT NULL DEFAULT ''"),
                ("RepresentativeMobile", "TEXT NOT NULL DEFAULT ''"),
                ("CompanyEmail", "TEXT NOT NULL DEFAULT ''"),
                ("AccountNumber", "TEXT NOT NULL DEFAULT ''"),
                ("BankName", "TEXT NOT NULL DEFAULT ''"),
                ("IsDefault", "INTEGER NOT NULL DEFAULT 0"),
                ("LogoLayout", "INTEGER NOT NULL DEFAULT 0"),
                ("LogoWidthMm", "REAL NOT NULL DEFAULT 22"),
                ("LogoHeightMm", "REAL NOT NULL DEFAULT 26"),
            };

            foreach (var (name, ddl) in columns)
            {
                if (existing.Contains(name)) continue;

                using var cmd = ctx.Database.GetDbConnection().CreateCommand();
                cmd.CommandText = $"ALTER TABLE Companies ADD COLUMN {name} {ddl};";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  COMPANIES
        // ══════════════════════════════════════════════════════════════════

        public async Task<SavedCompany> SaveOrUpdateCompanyAsync(
            SavedCompany company, bool preserveUnsetProfileFields = false)
        {
            ReadOnlyGate.Demand("save a company");
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.SettingsManage, "save a company");

            if (company == null)
                throw new ArgumentNullException(nameof(company));

            // PHASE 4: factory seam (tests inject an isolated database;
            // production resolves to the self-configuring context).
            using var ctx = CreateContext();

            // AUDIT-FIX F-1: lookup order. (1) primary key when the caller
            // knows it — Company Center edits carry the row Id and may have
            // RENAMED the company; a name-based lookup would miss the row and
            // INSERT a duplicate instead of updating it. (2) natural key
            // Name+TRN otherwise (invoice auto-save / suggestion dedup).
            SavedCompany? existing = null;
            if (company.Id > 0)
                existing = await ctx.Companies
                    .FirstOrDefaultAsync(c => c.Id == company.Id);
            existing ??= await ctx.Companies.FirstOrDefaultAsync(c =>
                c.CompanyName.ToLower() == company.CompanyName.ToLower()
                && c.CompanyTrn.ToLower() == company.CompanyTrn.ToLower());

            var now = DateTime.Now;

            if (existing != null)
            {
                // AUDIT-FIX F-1b: renaming onto a name+TRN pair that another
                // row already owns would create a hidden duplicate company.
                // Refuse instead of silently creating it. Only checked when
                // the identity key actually changes, so saves against
                // legacy-duplicated data keep working.
                bool keyChanged =
                    !string.Equals(existing.CompanyName, company.CompanyName,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existing.CompanyTrn, company.CompanyTrn,
                        StringComparison.OrdinalIgnoreCase);
                if (keyChanged)
                {
                    bool clash = await ctx.Companies.AnyAsync(c =>
                        c.Id != existing.Id
                        && c.CompanyName.ToLower() == company.CompanyName.ToLower()
                        && c.CompanyTrn.ToLower() == company.CompanyTrn.ToLower());
                    if (clash)
                        throw new InvalidOperationException(
                            $"Cannot rename \"{existing.CompanyName}\": another saved company " +
                            $"already uses the name + TRN \"{company.CompanyName}\" / " +
                            $"\"{company.CompanyTrn}\".");
                }

                void SetIfProvided(Action<string> set, string? value)
                {
                    if (!string.IsNullOrWhiteSpace(value)) set(value.Trim());
                }

                existing.CompanyName = company.CompanyName?.Trim()
                    ?? existing.CompanyName;
                existing.CompanyTrn = company.CompanyTrn?.Trim()
                    ?? existing.CompanyTrn;

                // PHASE 5 — traceability: profile fields are captured
                // BEFORE the branch applies, so every save yields an exact
                // field-level diff (the Activity log renders it GitHub-
                // style). Auto-enrichment and explicit edits are logged as
                // DISTINCT actions — the path the data took is part of the
                // trail, not guesswork.
                string beforeLocation = existing.CompanyLocation;
                string beforeLogo = existing.LogoPath;
                var beforeLogoLayout = existing.LogoLayout;
                double beforeLogoW = existing.LogoWidthMm;
                double beforeLogoH = existing.LogoHeightMm;
                string beforeRep = existing.Representative;
                string beforeRepMobile = existing.RepresentativeMobile;
                string beforeEmail = existing.CompanyEmail;
                string beforeAccount = existing.AccountNumber;
                string beforeBank = existing.BankName;
                string beforeName = existing.CompanyName;
                string beforeTrn = existing.CompanyTrn;

                if (preserveUnsetProfileFields)
                {
                    // Invoice auto-save path: a blank header field must never
                    // erase a stored profile value; a non-blank field
                    // enriches the record. IsDefault is deliberately untouched
                    // — defaults are managed explicitly in Company Center.
                    SetIfProvided(v => existing.CompanyLocation = v,
                        company.CompanyLocation);
                    SetIfProvided(v => existing.LogoPath = v, company.LogoPath);
                    SetIfProvided(v => existing.Representative = v,
                        company.Representative);
                    SetIfProvided(v => existing.RepresentativeMobile = v,
                        company.RepresentativeMobile);
                    SetIfProvided(v => existing.CompanyEmail = v,
                        company.CompanyEmail);
                    SetIfProvided(v => existing.AccountNumber = v,
                        company.AccountNumber);
                    SetIfProvided(v => existing.BankName = v,
                        company.BankName);

                    // Branding: the invoice path only carries the logo path.
                    // Layout / size come from Company Center, so a blank invoice
                    // value must never reset them (None → 0 → untouched here).
                    if (company.LogoLayout != CompanyLogoLayout.None)
                        existing.LogoLayout = company.LogoLayout;
                    if (company.LogoWidthMm > 0)
                        existing.LogoWidthMm = company.LogoWidthMm;
                    if (company.LogoHeightMm > 0)
                        existing.LogoHeightMm = company.LogoHeightMm;
                }
                else
                {
                    // AUDIT-FIX F-1c: explicit editor (Company Center) —
                    // latest data wins for ALL fields; blank = clear.
                    // The previous branch copied ONLY CompanyLocation, which
                    // silently discarded every LogoPath / Representative /
                    // RepresentativeMobile / CompanyEmail / AccountNumber /
                    // BankName edit, and also meant the IsDefault toggle (and
                    // the single-default clearing loop) never persisted:
                    // multiple rows could look default forever.
                    existing.CompanyLocation =
                        company.CompanyLocation?.Trim() ?? "";
                    existing.LogoPath = company.LogoPath ?? "";
                    existing.Representative =
                        company.Representative?.Trim() ?? "";
                    existing.RepresentativeMobile =
                        company.RepresentativeMobile?.Trim() ?? "";
                    existing.CompanyEmail = company.CompanyEmail?.Trim() ?? "";
                    existing.AccountNumber =
                        company.AccountNumber?.Trim() ?? "";
                    existing.BankName = company.BankName?.Trim() ?? "";

                    // Editor (Company Center): latest data wins for branding.
                    // Width/height fall back to the industrial defaults when
                    // the operator leaves them blank so a broken 0mm box can
                    // never reach the renderer.
                    existing.LogoLayout = company.LogoLayout;
                    existing.LogoWidthMm =
                        company.LogoWidthMm > 0 ? company.LogoWidthMm : 22;
                    existing.LogoHeightMm =
                        company.LogoHeightMm > 0 ? company.LogoHeightMm : 26;
                    existing.IsDefault = company.IsDefault;
                }

                existing.UsageCount += 1;
                existing.LastUsedAt = now;

                if (!preserveUnsetProfileFields && existing.IsDefault)
                {
                    foreach (var other in ctx.Companies.Where(c => c.Id != existing.Id && c.IsDefault))
                        other.IsDefault = false;
                }

                await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());

                // PHASE 5: persistent field-diff trail (fire-and-forget,
                // never throws — auditing must never block a save).
                LogCompanyChanges(
                    existing, preserveUnsetProfileFields,
                    beforeName, beforeTrn, beforeLocation, beforeLogo,
                    beforeLogoLayout, beforeLogoW, beforeLogoH,
                    beforeRep, beforeRepMobile, beforeEmail,
                    beforeAccount, beforeBank);

                return existing;
            }

            // Insert new — clear any caller-supplied key so SQLite assigns
            // the rowid (a stale Id from a deleted row must not be reused).
            company.Id = 0;
            company.UsageCount = 1;
            company.CreatedAt = now;
            company.LastUsedAt = now;

            if (!preserveUnsetProfileFields && company.IsDefault)
            {
                foreach (var other in ctx.Companies.Where(c => c.IsDefault))
                    other.IsDefault = false;
            }

            ctx.Companies.Add(company);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());

            // PHASE 5: creation is part of the trail (fire-and-forget).
            AuditService.Log(
                AuditSections.MasterData,
                "Company created",
                $"Company \"{company.CompanyName}\" added to master data (TRN {company.CompanyTrn}).",
                "Company",
                company.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return company;
        }

        // ── PHASE 5: persistent change-set trail for company profiles ────
        //  The Activity log renders these GitHub-style. Only fields whose
        //  value ACTUALLY changed are recorded; once a profile is complete,
        //  routine invoice saves emit nothing (no audit noise).
        private static void LogCompanyChanges(
            SavedCompany after,
            bool preserveMode,
            string beforeName, string beforeTrn,
            string beforeLocation, string beforeLogo,
            CompanyLogoLayout beforeLogoLayout,
            double beforeLogoW, double beforeLogoH,
            string beforeRep, string beforeRepMobile,
            string beforeEmail, string beforeAccount, string beforeBank)
        {
            static string Trunc(string s) =>
                s.Length > 200 ? s.Substring(0, 200) : s;

            static void Add(List<AuditChange> c, string field,
                string before, string afterValue)
            {
                if (!string.Equals(before, afterValue, StringComparison.Ordinal))
                    c.Add(new AuditChange
                    {
                        Field = field,
                        Before = Trunc(before ?? ""),
                        After = Trunc(afterValue ?? "")
                    });
            }

            var changes = new List<AuditChange>();
            Add(changes, "Name", beforeName, after.CompanyName);
            Add(changes, "TRN", beforeTrn, after.CompanyTrn);
            Add(changes, "Location", beforeLocation, after.CompanyLocation);
            Add(changes, "Logo", beforeLogo, after.LogoPath);
            Add(changes, "Logo layout",
                beforeLogoLayout.ToString(), after.LogoLayout.ToString());
            Add(changes, "Logo width (mm)",
                beforeLogoW.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                after.LogoWidthMm.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            Add(changes, "Logo height (mm)",
                beforeLogoH.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
                after.LogoHeightMm.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            Add(changes, "Representative", beforeRep, after.Representative);
            Add(changes, "Rep mobile", beforeRepMobile, after.RepresentativeMobile);
            Add(changes, "Email", beforeEmail, after.CompanyEmail);
            Add(changes, "Account no.", beforeAccount, after.AccountNumber);
            Add(changes, "Bank", beforeBank, after.BankName);

            if (changes.Count == 0) return;

            AuditService.LogWithChanges(
                AuditSections.MasterData,
                preserveMode
                    ? "Company profile auto-enriched (invoice save)"
                    : "Company profile updated",
                $"Company \"{after.CompanyName}\" — {changes.Count} field(s) changed.",
                "Company",
                after.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                changes);
        }

        public async Task<List<SavedCompany>> GetAllCompaniesAsync()
        {
            using var ctx = new MasterDataDbContext();
            return await ctx.Companies.AsNoTracking()
                .OrderByDescending(c => c.UsageCount)
                .ThenByDescending(c => c.LastUsedAt)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<SavedCompany>> SearchCompaniesAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<SavedCompany>();

            using var ctx = new MasterDataDbContext();
            var q = query.Trim().ToLower();

            return await ctx.Companies
                .Where(c => c.CompanyName.ToLower().Contains(q))
                .OrderByDescending(c => c.UsageCount)
                .Take(SEARCH_LIMIT)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task DeleteCompanyAsync(int id)
        {
            ReadOnlyGate.Demand("delete a company");
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.SettingsManage, "delete a company");

            using var ctx = new MasterDataDbContext();
            var entity = await ctx.Companies.FindAsync(id).ConfigureAwait(false);
            if (entity == null) return;

            ctx.Companies.Remove(entity);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
        }

        public async Task<SavedCompany?> FindCompanyByNameAsync(string companyName)
        {
            if (string.IsNullOrWhiteSpace(companyName))
                return null;

            using var ctx = new MasterDataDbContext();
            var name = companyName.Trim().ToLower();

            return await ctx.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CompanyName.ToLower() == name)
                .ConfigureAwait(false);
        }

        // ══════════════════════════════════════════════════════════════════
        //  CLIENTS
        // ══════════════════════════════════════════════════════════════════

        public async Task<SavedClient> SaveOrUpdateClientAsync(SavedClient client)
        {
            ReadOnlyGate.Demand("save a client");
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.ProformaEdit, "save a client");

            if (client == null)
                throw new ArgumentNullException(nameof(client));

            using var ctx = new MasterDataDbContext();

            var existing = await ctx.Clients.FirstOrDefaultAsync(c =>
                c.CustomerName.ToLower() == client.CustomerName.ToLower()
                && c.CustomerTrn.ToLower() == client.CustomerTrn.ToLower());

            var now = DateTime.Now;

            if (existing != null)
            {
                // Update — latest data wins for all fields
                existing.CustomerReferenceCode = client.CustomerReferenceCode;
                existing.SalesRepresentative = client.SalesRepresentative;
                existing.DocumentPreparedBy = client.DocumentPreparedBy;
                existing.BillingAddress = client.BillingAddress;
                existing.AttentionExecutiveName = client.AttentionExecutiveName;
                existing.ClientContactPhone = client.ClientContactPhone;
                existing.UsageCount += 1;
                existing.LastUsedAt = now;

                await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
                return existing;
            }

            // Insert new
            client.UsageCount = 1;
            client.CreatedAt = now;
            client.LastUsedAt = now;

            ctx.Clients.Add(client);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
            return client;
        }

        public async Task<List<SavedClient>> GetAllClientsAsync()
        {
            using var ctx = new MasterDataDbContext();
            return await ctx.Clients.AsNoTracking()
                .OrderByDescending(c => c.UsageCount)
                .ThenByDescending(c => c.LastUsedAt)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<SavedClient>> SearchClientsAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<SavedClient>();

            using var ctx = new MasterDataDbContext();
            var q = query.Trim().ToLower();

            return await ctx.Clients
                .Where(c => c.CustomerName.ToLower().Contains(q))
                .OrderByDescending(c => c.UsageCount)
                .Take(SEARCH_LIMIT)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task DeleteClientAsync(int id)
        {
            ReadOnlyGate.Demand("delete a client");
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.SettingsManage, "delete a client");

            using var ctx = new MasterDataDbContext();
            var entity = await ctx.Clients.FindAsync(id).ConfigureAwait(false);
            if (entity == null) return;

            ctx.Clients.Remove(entity);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
        }

        public async Task<SavedClient?> FindClientByNameAsync(string customerName)
        {
            if (string.IsNullOrWhiteSpace(customerName))
                return null;

            using var ctx = new MasterDataDbContext();
            var name = customerName.Trim().ToLower();

            return await ctx.Clients
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CustomerName.ToLower() == name)
                .ConfigureAwait(false);
        }

        // ══════════════════════════════════════════════════════════════════
        //  PROJECTS
        // ══════════════════════════════════════════════════════════════════

        public async Task<SavedProject> SaveOrUpdateProjectAsync(SavedProject project)
        {
            ReadOnlyGate.Demand("save a project");
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.ProformaEdit, "save a project");

            if (project == null)
                throw new ArgumentNullException(nameof(project));

            using var ctx = new MasterDataDbContext();

            var existing = await ctx.Projects.FirstOrDefaultAsync(p =>
                p.ProjectIdentifierName.ToLower() == project.ProjectIdentifierName.ToLower()
                && p.CustomerName.ToLower() == project.CustomerName.ToLower());

            var now = DateTime.Now;

            if (existing != null)
            {
                // Update — latest data wins
                existing.ProjectContractNo = project.ProjectContractNo;
                existing.ProjectReference = project.ProjectReference;    // CHAT 15: NEW
                existing.SiteDeliveryLocation = project.SiteDeliveryLocation;
                existing.LpoNumber = project.LpoNumber;
                existing.UsageCount += 1;
                existing.LastUsedAt = now;

                await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
                return existing;
            }

            // Insert new
            project.UsageCount = 1;
            project.CreatedAt = now;
            project.LastUsedAt = now;

            ctx.Projects.Add(project);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
            return project;
        }

        public async Task<List<SavedProject>> GetAllProjectsAsync()
        {
            using var ctx = new MasterDataDbContext();
            return await ctx.Projects.AsNoTracking()
                .OrderByDescending(p => p.UsageCount)
                .ThenByDescending(p => p.LastUsedAt)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<SavedProject>> SearchProjectsAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<SavedProject>();

            using var ctx = new MasterDataDbContext();
            var q = query.Trim().ToLower();

            return await ctx.Projects
                .Where(p => p.ProjectIdentifierName.ToLower().Contains(q))
                .OrderByDescending(p => p.UsageCount)
                .Take(SEARCH_LIMIT)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<SavedProject>> GetProjectsByCustomerAsync(
            string customerName)
        {
            if (string.IsNullOrWhiteSpace(customerName))
                return new List<SavedProject>();

            using var ctx = new MasterDataDbContext();
            var name = customerName.Trim().ToLower();

            return await ctx.Projects
                .Where(p => p.CustomerName.ToLower() == name)
                .OrderByDescending(p => p.UsageCount)
                .ThenByDescending(p => p.LastUsedAt)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task DeleteProjectAsync(int id)
        {
            ReadOnlyGate.Demand("delete a project");
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.SettingsManage, "delete a project");

            using var ctx = new MasterDataDbContext();
            var entity = await ctx.Projects.FindAsync(id).ConfigureAwait(false);
            if (entity == null) return;

            ctx.Projects.Remove(entity);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
        }

        public async Task<SavedProject?> FindProjectByNameAsync(string projectName, string customerName)
        {
            if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(customerName))
                return null;

            using var ctx = new MasterDataDbContext();
            var projName = projectName.Trim().ToLower();
            var custName = customerName.Trim().ToLower();

            return await ctx.Projects
                .AsNoTracking()
                .FirstOrDefaultAsync(p => 
                    p.ProjectIdentifierName.ToLower() == projName 
                    && p.CustomerName.ToLower() == custName)
                .ConfigureAwait(false);
        }

        // ══════════════════════════════════════════════════════════════════
        //  SALESMEN
        // ══════════════════════════════════════════════════════════════════

        public async Task<SavedSalesman> SaveOrUpdateSalesmanAsync(
            SavedSalesman salesman)
        {
            ReadOnlyGate.Demand("save a salesman");
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.SettingsManage, "save a salesman");

            if (salesman == null)
                throw new ArgumentNullException(nameof(salesman));

            using var ctx = new MasterDataDbContext();

            var existing = await ctx.Salesmen.FirstOrDefaultAsync(s =>
                s.Name.ToLower() == salesman.Name.ToLower());

            var now = DateTime.Now;

            if (existing != null)
            {
                // Update — latest department wins
                if (!string.IsNullOrWhiteSpace(salesman.Department))
                    existing.Department = salesman.Department;

                existing.UsageCount += 1;
                existing.LastUsedAt = now;

                await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
                return existing;
            }

            // Insert new
            salesman.UsageCount = 1;
            salesman.CreatedAt = now;
            salesman.LastUsedAt = now;

            ctx.Salesmen.Add(salesman);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
            return salesman;
        }

        public async Task<List<SavedSalesman>> GetAllSalesmenAsync()
        {
            using var ctx = new MasterDataDbContext();
            return await ctx.Salesmen.AsNoTracking()
                .OrderByDescending(s => s.UsageCount)
                .ThenByDescending(s => s.LastUsedAt)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task<List<SavedSalesman>> SearchSalesmenAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<SavedSalesman>();

            using var ctx = new MasterDataDbContext();
            var q = query.Trim().ToLower();

            return await ctx.Salesmen
                .Where(s => s.Name.ToLower().Contains(q))
                .OrderByDescending(s => s.UsageCount)
                .Take(SEARCH_LIMIT)
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task DeleteSalesmanAsync(int id)
        {
            ReadOnlyGate.Demand("delete a salesman");
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.SettingsManage, "delete a salesman");

            using var ctx = new MasterDataDbContext();
            var entity = await ctx.Salesmen.FindAsync(id).ConfigureAwait(false);
            if (entity == null) return;

            ctx.Salesmen.Remove(entity);
            await SqliteResilience.SaveWithRetryAsync(_ => ctx.SaveChangesAsync());
        }

        // ══════════════════════════════════════════════════════════════════
        //  BULK AUTO-SAVE FROM INVOICE
        //
        //  Called by ProformaInvoiceMapper.BuildEntityFromWizard() after
        //  building the invoice entity. Extracts company/client/project/
        //  salesman from raw field values and upserts all 4.
        //
        //  Skips any entity where the primary name field is empty —
        //  prevents garbage rows in master data.
        //
        //  Each upsert is independent — if one fails (e.g. duplicate
        //  race condition), the others still succeed. Errors are
        //  swallowed with Debug.WriteLine because master data auto-save
        //  should NEVER block invoice save.
        //
        //  CHAT 15: projectReference parameter added between
        //           projectContractNo and siteDeliveryLocation.
        // ══════════════════════════════════════════════════════════════════

        public async Task AutoSaveFromInvoiceAsync(
            // Company fields
            string companyName,
            string companyTrn,
            string companyLocation,

            // Client fields
            string customerName,
            string customerTrn,
            string customerReferenceCode,
            string salesRepresentative,
            string documentPreparedBy,
            string billingAddress,
            string attentionExecutiveName,
            string clientContactPhone,

            // Project fields
            string projectIdentifierName,
            string projectContractNo,
            string projectReference,           // CHAT 15: NEW
            string siteDeliveryLocation,
            string lpoNumber,

            // Company profile fields (AUDIT-FIX F-2, optional)
            string? companyLogoPath = null,
            string? companyRepresentative = null,
            string? companyRepresentativeMobile = null,
            string? companyEmail = null,
            string? companyAccountNumber = null,
            string? companyBankName = null,
            CompanyLogoLayout companyLogoLayout = CompanyLogoLayout.None,
            double companyLogoWidthMm = 0,
            double companyLogoHeightMm = 0)
        {
            ReadOnlyGate.Demand("auto-save master data from an invoice");
            // ── Company ──────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(companyName))
            {
                try
                {
                    // AUDIT-FIX F-2: forward the Step 1 profile fields so a
                    // header edit enriches the saved company record. The
                    // preserve flag guarantees a blank invoice field never
                    // erases an existing profile value.
                    await SaveOrUpdateCompanyAsync(new SavedCompany
                    {
                        CompanyName = companyName.Trim(),
                        CompanyTrn = (companyTrn ?? "").Trim(),
                        CompanyLocation = (companyLocation ?? "").Trim(),
                        LogoPath = companyLogoPath ?? "",
                        Representative = (companyRepresentative ?? "").Trim(),
                        RepresentativeMobile =
                            (companyRepresentativeMobile ?? "").Trim(),
                        CompanyEmail = (companyEmail ?? "").Trim(),
                        AccountNumber = (companyAccountNumber ?? "").Trim(),
                        BankName = (companyBankName ?? "").Trim(),
                        LogoLayout = companyLogoLayout,
                        LogoWidthMm = companyLogoWidthMm,
                        LogoHeightMm = companyLogoHeightMm
                    }, preserveUnsetProfileFields: true);
                }
                catch (Exception ex)
                {
                    AppLogService.Warning(
                            "[MasterData] Company auto-save failed: " + ex.Message);
                }
            }

            // ── Client ───────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(customerName))
            {
                try
                {
                    await SaveOrUpdateClientAsync(new SavedClient
                    {
                        CustomerName = customerName.Trim(),
                        CustomerTrn = (customerTrn ?? "").Trim(),
                        CustomerReferenceCode = (customerReferenceCode ?? "").Trim(),
                        SalesRepresentative = (salesRepresentative ?? "").Trim(),
                        DocumentPreparedBy = (documentPreparedBy ?? "").Trim(),
                        BillingAddress = (billingAddress ?? "").Trim(),
                        AttentionExecutiveName = (attentionExecutiveName ?? "").Trim(),
                        ClientContactPhone = (clientContactPhone ?? "").Trim()
                    });
                }
                catch (Exception ex)
                {
                    AppLogService.Warning(
                            "[MasterData] Client auto-save failed: " + ex.Message);
                }
            }

            // ── Salesman (from client's SalesRepresentative field) ────────
            if (!string.IsNullOrWhiteSpace(salesRepresentative))
            {
                try
                {
                    await SaveOrUpdateSalesmanAsync(new SavedSalesman
                    {
                        Name = salesRepresentative.Trim()
                    });
                }
                catch (Exception ex)
                {
                    AppLogService.Warning(
                            "[MasterData] Salesman auto-save failed: " + ex.Message);
                }
            }

            // ── Project ──────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(projectIdentifierName))
            {
                try
                {
                    await SaveOrUpdateProjectAsync(new SavedProject
                    {
                        ProjectIdentifierName = projectIdentifierName.Trim(),
                        ProjectContractNo = (projectContractNo ?? "").Trim(),
                        ProjectReference = (projectReference ?? "").Trim(),   // CHAT 15: NEW
                        SiteDeliveryLocation = (siteDeliveryLocation ?? "").Trim(),
                        LpoNumber = (lpoNumber ?? "").Trim(),
                        CustomerName = (customerName ?? "").Trim()
                    });
                }
                catch (Exception ex)
                {
                    AppLogService.Warning(
                            "[MasterData] Project auto-save failed: " + ex.Message);
                }
            }
        }
    }
}