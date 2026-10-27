// ═══════════════════════════════════════════════════════════════════════
// FILE: step2.ClientManagement/step2.b.4.ClientRepository.cs
// MODULE: step2 — client directory persistence.
//
// Behaviour
//   • InitializeAsync first asks the established ERP initializer to bring the
//     shared inquiries.db to the current schema (fresh-file creation by
//     EnsureCreated + the single AppSchemaMigrations chain, which now
//     includes migration 1011 → Clients). Creating the client table through
//     that chain keeps the backup-before-migrate, single-transaction,
//     verification and quick_check safeguards in force. No second chain, no
//     second EnsureCreated owner, no direct file manipulation.
//   • Every write normalizes through ClientRules, then stamps timestamps in
//     UTC. Writes go through SqliteResilience.SaveWithRetryAsync so a
//     transient SQLITE_BUSY is retried rather than surfaced.
//   • Failures return a ClientSaveResult with an operator-facing message; the
//     UI never sees a raw exception for an ordinary save failure.
//   • Nothing here deletes a client or touches the Inquiry tables.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Clients;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Inquiry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Clients
{
    /// <summary>EF Core implementation of <see cref="IClientRepository"/>.</summary>
    public sealed class ClientRepository : IClientRepository
    {
        private readonly Func<ClientDbContext> _createContext;
        private bool _initialized;

        public ClientRepository() : this(null) { }

        /// <summary>
        /// Test seam: an alternate context factory. Production callers use the
        /// parameterless constructor (self-configuring context on the real
        /// inquiries.db); the verification executable can point the same code
        /// at an isolated file via AppDataRoot.TestFolderOverride.
        /// </summary>
        internal ClientRepository(Func<ClientDbContext>? contextFactory)
        {
            _createContext = contextFactory ?? (() => new ClientDbContext());
        }

        private ClientDbContext CreateContext() => _createContext();

        // ── Initialization ───────────────────────────────────────────────

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (_initialized) return;

            // The ERP file is owned by the Inquiry initializer: it creates a
            // fresh file and applies the single version chain (which includes
            // migration 1011 → Clients). Running it here means Clients can
            // never be created before the base tables exist, which would make
            // EF's "database is empty" check skip the Inquiry schema.
            await new InquiryRepository().InitializeAsync(ct).ConfigureAwait(false);

            if (!await IsSchemaReadyAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException(
                    "The client directory table (migration 1011) is missing after initialization. " +
                    "Schema upgrade, if any, reported: " +
                    (string.IsNullOrWhiteSpace(SchemaMigrationRunner.FailureSummary)
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

                using (var tableCommand = connection.CreateCommand())
                {
                    tableCommand.CommandText =
                        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Clients';";
                    long tables = Convert.ToInt64(
                        tableCommand.ExecuteScalar() ?? 0L,
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (tables == 0) return false;
                }

                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var columnCommand = connection.CreateCommand())
                {
                    columnCommand.CommandText = "PRAGMA table_info(\"Clients\");";
                    using var reader = columnCommand.ExecuteReader();
                    while (reader.Read())
                    {
                        if (!reader.IsDBNull(1)) columns.Add(reader.GetString(1));
                    }
                }

                foreach (string required in ClientSchema.RequiredColumns)
                    if (!columns.Contains(required)) return false;
                return true;
            }
            catch (Exception)
            {
                // A readiness probe never throws: an unreadable/absent file is
                // simply "not ready", and the caller reports it.
                return false;
            }
        }

        // ── Reads ────────────────────────────────────────────────────────

        public async Task<IReadOnlyList<ClientListRow>> QueryAsync(
            ClientQuery query, CancellationToken ct = default)
        {
            query ??= new ClientQuery();
            await using var db = CreateContext();
            IQueryable<Client> source = db.Clients.AsNoTracking();

            if (!query.IncludeInactive)
                source = source.Where(x => x.IsActive);

            string term = ClientRules.NormalizeText(query.Search);
            if (term.Length > 0)
            {
                // LIKE (not Contains) so matching is case-insensitive for ASCII
                // the way operators expect; the escape character keeps a typed
                // % or _ literal instead of a wildcard.
                string pattern = "%" +
                    term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                source = source.Where(x =>
                    EF.Functions.Like(x.ClientName, pattern, "\\")
                    || EF.Functions.Like(x.ContactPerson, pattern, "\\")
                    || EF.Functions.Like(x.Phone, pattern, "\\")
                    || EF.Functions.Like(x.Email, pattern, "\\")
                    || EF.Functions.Like(x.Trn, pattern, "\\")
                    || EF.Functions.Like(x.Address, pattern, "\\"));
            }

            int limit = query.MaxRows <= 0 ? 5000 : query.MaxRows;

            return await source
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.ClientName)
                .Take(limit)
                .Select(x => new ClientListRow
                {
                    Id = x.Id,
                    ClientName = x.ClientName,
                    ContactPerson = x.ContactPerson,
                    Phone = x.Phone,
                    Email = x.Email,
                    Trn = x.Trn,
                    IsActive = x.IsActive,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        public async Task<Client?> GetAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0) return null;
            await using var db = CreateContext();
            return await db.Clients.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<ClientDuplicateMatch>> FindDuplicatesAsync(
            ClientDraft draft, int? excludeClientId = null, CancellationToken ct = default)
        {
            if (draft == null) return Array.Empty<ClientDuplicateMatch>();

            await using var db = CreateContext();
            // Comparison keys are computed in memory (Unicode normalization is
            // not expressible in SQLite), so the whole directory is read as a
            // small projection. The directory is a local master list, capped
            // for safety; it is never a high-volume ledger.
            var candidates = await db.Clients.AsNoTracking()
                .Take(20000)
                .Select(x => new ClientDuplicateCandidate
                {
                    Id = x.Id,
                    ClientName = x.ClientName,
                    Phone = x.Phone,
                    Email = x.Email,
                    Trn = x.Trn,
                    IsActive = x.IsActive
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            return ClientDuplicatePolicy.Find(candidates, draft, excludeClientId);
        }

        // ── Writes ───────────────────────────────────────────────────────

        public async Task<ClientSaveResult> CreateAsync(
            ClientDraft draft, CancellationToken ct = default)
        {
            if (draft == null) return ClientSaveResult.Fail("No client data was supplied.");

            var errors = ClientRules.Validate(draft);
            if (errors.Count > 0)
                return ClientSaveResult.Fail(string.Join(" ", errors.Values));

            ClientDraft normalized = ClientRules.Normalize(draft);
            var now = DateTime.UtcNow;

            var client = new Client
            {
                ClientName = normalized.ClientName,
                ContactPerson = normalized.ContactPerson,
                Phone = normalized.Phone,
                Email = normalized.Email,
                Trn = normalized.Trn,
                Address = normalized.Address,
                Notes = normalized.Notes,
                IsActive = normalized.IsActive,
                CreatedAt = now,
                UpdatedAt = now
            };

            try
            {
                await using var db = CreateContext();
                db.Clients.Add(client);
                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                return ClientSaveResult.Ok(client);
            }
            catch (Exception ex)
            {
                return ClientSaveResult.Fail(DescribeWriteFailure("The client could not be saved", ex));
            }
        }

        public async Task<ClientSaveResult> UpdateAsync(
            int id, ClientDraft draft, CancellationToken ct = default)
        {
            if (id <= 0) return ClientSaveResult.Fail("No client was selected.");
            if (draft == null) return ClientSaveResult.Fail("No client data was supplied.");

            var errors = ClientRules.Validate(draft);
            if (errors.Count > 0)
                return ClientSaveResult.Fail(string.Join(" ", errors.Values));

            ClientDraft normalized = ClientRules.Normalize(draft);

            try
            {
                await using var db = CreateContext();
                Client? client = await db.Clients
                    .FirstOrDefaultAsync(x => x.Id == id, ct)
                    .ConfigureAwait(false);
                if (client == null)
                    return ClientSaveResult.Fail(
                        "That client no longer exists in the directory. Refresh the list and try again.");

                client.ClientName = normalized.ClientName;
                client.ContactPerson = normalized.ContactPerson;
                client.Phone = normalized.Phone;
                client.Email = normalized.Email;
                client.Trn = normalized.Trn;
                client.Address = normalized.Address;
                client.Notes = normalized.Notes;
                client.IsActive = normalized.IsActive;
                client.UpdatedAt = DateTime.UtcNow;

                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                return ClientSaveResult.Ok(client);
            }
            catch (Exception ex)
            {
                return ClientSaveResult.Fail(DescribeWriteFailure("The client could not be updated", ex));
            }
        }

        public async Task<ClientSaveResult> SetActiveAsync(
            int id, bool isActive, CancellationToken ct = default)
        {
            if (id <= 0) return ClientSaveResult.Fail("No client was selected.");

            try
            {
                await using var db = CreateContext();
                Client? client = await db.Clients
                    .FirstOrDefaultAsync(x => x.Id == id, ct)
                    .ConfigureAwait(false);
                if (client == null)
                    return ClientSaveResult.Fail(
                        "That client no longer exists in the directory. Refresh the list and try again.");

                if (client.IsActive == isActive) return ClientSaveResult.Ok(client);

                client.IsActive = isActive;
                client.UpdatedAt = DateTime.UtcNow;

                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                return ClientSaveResult.Ok(client);
            }
            catch (Exception ex)
            {
                string action = isActive ? "re-activated" : "deactivated";
                return ClientSaveResult.Fail(DescribeWriteFailure($"The client could not be {action}", ex));
            }
        }

        /// <summary>
        /// Turns a persistence exception into an operator-facing sentence. The
        /// exception type is named so a support conversation has something to
        /// go on, without exposing a stack trace in the UI.
        /// </summary>
        private static string DescribeWriteFailure(string action, Exception exception)
        {
            if (exception == null) return action + ".";
            if (exception is SqliteException sqlite && SqliteErrorClassifier.IsTransient(exception))
                return action + ": the database is busy. Close other ProGlass windows and try again.";
            if (SqliteErrorClassifier.IsUnique(exception))
                return action + ": a conflicting record already exists.";
            return action + " (" + exception.GetType().Name + "): " + exception.Message;
        }
    }
}
