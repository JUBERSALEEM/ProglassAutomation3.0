// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/MasterData/Repository/IMasterDataRepository.cs
// ═══════════════════════════════════════════════════════════════════════
// CHAT 15 — PROJECT REFERENCE PARAMETER ADDED
//   ONLY change: AutoSaveFromInvoiceAsync gains projectReference param.
//   All original XML documentation preserved exactly.
//   No method signatures changed except the auto-save one.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.MasterData;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.MasterData.Repository
{
    /// <summary>
    /// Repository contract for the Master Data database (masterdata.db).
    ///
    /// Unified interface for all 4 master data entities:
    ///   Companies, Clients, Projects, Salesmen
    ///
    /// Why one interface instead of 4 separate?
    ///   - All 4 entities are tightly coupled (saved from same invoice)
    ///   - All share the same upsert + search + list pattern
    ///   - One DB context → one repository → less file sprawl
    ///   - Easier to test and wire in App.xaml.cs
    ///
    /// Upsert pattern:
    ///   SaveOrUpdate methods match by uniqueness key (case-insensitive).
    ///   If match found → update fields + increment UsageCount + set LastUsedAt.
    ///   If no match → insert new record with UsageCount=1.
    ///
    /// List pattern:
    ///   GetAll methods return ordered by UsageCount DESC then LastUsedAt DESC.
    ///   Most-used records appear first in card lists and dropdowns.
    ///
    /// Search pattern:
    ///   Search methods accept a query string and match against Name
    ///   fields using case-insensitive LIKE. Used for ComboBox
    ///   autocomplete and card filtering.
    ///
    /// Auto-save flow:
    ///   ProformaInvoiceMapper.BuildEntityFromWizard() calls
    ///   AutoSaveFromInvoiceAsync() after building the entity.
    ///   This extracts company/client/project/salesman from the
    ///   wizard VMs and upserts them into masterdata.db.
    ///   No UI interaction required — completely invisible to user.
    /// </summary>
    public interface IMasterDataRepository
    {
        /// <summary>Ensure database file + all 4 tables exist.</summary>
        Task InitializeAsync();

        // ══════════════════════════════════════════════════════════════════
        //  COMPANIES
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Upsert a company profile. Lookup: primary key first (when
        /// <paramref name="company"/>.Id &gt; 0), then natural key
        /// CompanyName + CompanyTrn (case-insensitive).
        /// Updates existing or inserts new.
        /// <paramref name="preserveUnsetProfileFields"/>: when true (invoice
        /// auto-save), blank profile fields must not erase stored values and
        /// IsDefault is left untouched; when false (Company Center editor),
        /// latest data wins for all fields including IsDefault.
        /// Throws <see cref="InvalidOperationException"/> when a rename would
        /// collide with a name+TRN already owned by another company.
        /// Returns the saved/updated entity.
        /// </summary>
        Task<SavedCompany> SaveOrUpdateCompanyAsync(
            SavedCompany company, bool preserveUnsetProfileFields = false);

        /// <summary>
        /// Returns all companies ordered by UsageCount DESC, LastUsedAt DESC.
        /// Used for Step 1 card list.
        /// </summary>
        Task<List<SavedCompany>> GetAllCompaniesAsync();

        /// <summary>
        /// Search companies by name (case-insensitive LIKE).
        /// Used for autocomplete / filtering.
        /// </summary>
        Task<List<SavedCompany>> SearchCompaniesAsync(string query);

        /// <summary>Delete a company profile by ID.</summary>
        Task DeleteCompanyAsync(int id);

        /// <summary>
        /// Check if a company with the same name already exists (case-insensitive).
        /// Returns the existing company if found, null otherwise.
        /// Used for duplicate detection before save to warn users.
        /// </summary>
        Task<SavedCompany?> FindCompanyByNameAsync(string companyName);

        // ══════════════════════════════════════════════════════════════════
        //  CLIENTS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Upsert a client profile. Matches by CustomerName + CustomerTrn
        /// (case-insensitive). Updates existing or inserts new.
        /// </summary>
        Task<SavedClient> SaveOrUpdateClientAsync(SavedClient client);

        /// <summary>
        /// Returns all clients ordered by UsageCount DESC, LastUsedAt DESC.
        /// Used for Step 2 (future) card list.
        /// </summary>
        Task<List<SavedClient>> GetAllClientsAsync();

        /// <summary>
        /// Search clients by name (case-insensitive LIKE).
        /// </summary>
        Task<List<SavedClient>> SearchClientsAsync(string query);

        /// <summary>Delete a client profile by ID.</summary>
        Task DeleteClientAsync(int id);

        /// <summary>
        /// Check if a client with the same name already exists (case-insensitive).
        /// Returns the existing client if found, null otherwise.
        /// Used for duplicate detection before save to warn users.
        /// </summary>
        Task<SavedClient?> FindClientByNameAsync(string customerName);

        // ══════════════════════════════════════════════════════════════════
        //  PROJECTS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Upsert a project profile. Matches by ProjectIdentifierName +
        /// CustomerName (case-insensitive). Updates existing or inserts new.
        /// </summary>
        Task<SavedProject> SaveOrUpdateProjectAsync(SavedProject project);

        /// <summary>
        /// Returns all projects ordered by UsageCount DESC, LastUsedAt DESC.
        /// Used for Step 3 (future) card list.
        /// </summary>
        Task<List<SavedProject>> GetAllProjectsAsync();

        /// <summary>
        /// Search projects by name (case-insensitive LIKE).
        /// </summary>
        Task<List<SavedProject>> SearchProjectsAsync(string query);

        /// <summary>
        /// Returns all projects for a specific customer.
        /// Used when user selects a client card — shows only their projects.
        /// </summary>
        Task<List<SavedProject>> GetProjectsByCustomerAsync(string customerName);

        /// <summary>Delete a project profile by ID.</summary>
        Task DeleteProjectAsync(int id);

        /// <summary>
        /// Check if a project with the same name already exists for a customer (case-insensitive).
        /// Returns the existing project if found, null otherwise.
        /// Used for duplicate detection before save to warn users.
        /// </summary>
        Task<SavedProject?> FindProjectByNameAsync(string projectName, string customerName);

        // ══════════════════════════════════════════════════════════════════
        //  SALESMEN
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Upsert a salesman. Matches by Name (case-insensitive).
        /// Updates existing or inserts new.
        /// </summary>
        Task<SavedSalesman> SaveOrUpdateSalesmanAsync(SavedSalesman salesman);

        /// <summary>
        /// Returns all salesmen ordered by UsageCount DESC, LastUsedAt DESC.
        /// Used for salesman ComboBox dropdown.
        /// </summary>
        Task<List<SavedSalesman>> GetAllSalesmenAsync();

        /// <summary>
        /// Search salesmen by name (case-insensitive LIKE).
        /// Used for ComboBox autocomplete.
        /// </summary>
        Task<List<SavedSalesman>> SearchSalesmenAsync(string query);

        /// <summary>Delete a salesman by ID.</summary>
        Task DeleteSalesmanAsync(int id);

        // ══════════════════════════════════════════════════════════════════
        //  BULK AUTO-SAVE (called from ProformaInvoiceMapper)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Convenience method — extracts company, client, project, and
        /// salesman from raw field values and upserts all 4 in one call.
        /// Skips any entity where the primary name field is empty.
        ///
        /// Called by ProformaInvoiceMapper.BuildEntityFromWizard() after
        /// building the invoice entity. Completely invisible to the user.
        ///
        /// Parameters mirror the wizard VM field values directly:
        ///   companyName, companyTrn, companyLocation
        ///   customerName, customerTrn, customerRef, salesRep,
        ///   preparedBy, billingAddress, attentionName, contactPhone
        ///   projectName, contractNo, projectReference, siteDelivery, lpoNumber
        ///
        /// CHAT 15: projectReference parameter added.
        ///   Descriptive project reference (e.g. "HOUSE FOR IMAD HABIB")
        ///   separate from projectContractNo (short code, e.g. "428 IMAD HABIB").
        /// AUDIT-FIX F-2: optional company profile parameters added
        ///   (logo / representative / mobile / email / account / bank) so
        ///   values typed on the invoice header also enrich the saved
        ///   company record. Auto-save always uses preserve-on-blank
        ///   semantics — it can never erase an existing profile value.
        /// </summary>
        Task AutoSaveFromInvoiceAsync(
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
            string projectReference,        // CHAT 15: NEW
            string siteDeliveryLocation,
            string lpoNumber,

            // Company profile fields (AUDIT-FIX F-2, optional).
            // Passed straight from the Step 1 header: non-blank values
            // enrich the saved company; blanks never erase stored values.
            string? companyLogoPath = null,
            string? companyRepresentative = null,
            string? companyRepresentativeMobile = null,
            string? companyEmail = null,
            string? companyAccountNumber = null,
            string? companyBankName = null,
            CompanyLogoLayout companyLogoLayout = CompanyLogoLayout.None,
            double companyLogoWidthMm = 0,
            double companyLogoHeightMm = 0);
    }
}