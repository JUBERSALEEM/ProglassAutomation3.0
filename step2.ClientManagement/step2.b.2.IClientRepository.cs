// ═══════════════════════════════════════════════════════════════════════
// FILE: step2.ClientManagement/step2.b.2.IClientRepository.cs
// MODULE: step2 — client persistence contract.
//
// Read path returns projections (no tracking) so the register stays cheap.
// Write path always normalizes through ClientRules before saving, and never
// deletes a row: deactivation is the only lifecycle transition.
//
// There is no Inquiry dependency here. The representative reference is a
// nullable id into the Users directory (migration 1014); it is read and written
// as an id, and the name is resolved for display only, so a rename never
// rewrites a client row.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Clients;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Clients
{
    /// <summary>Register filter for the client list.</summary>
    public sealed class ClientQuery
    {
        /// <summary>Free text matched against name, contact, phone, e-mail, TRN and address.</summary>
        public string Search { get; init; } = "";

        /// <summary>When true, inactive clients are included (never the default).</summary>
        public bool IncludeInactive { get; init; }

        /// <summary>Safety cap; the directory is a local master list, not a ledger.</summary>
        public int MaxRows { get; init; } = 5000;

        /// <summary>
        /// Restricts the register to the clients one person represents.
        /// </summary>
        /// <remarks>
        /// Set by the workspace for a signed-in non-administrator (owner
        /// instruction: "user should be able to read the data related to them
        /// only"). Null — the administrator's view — reads every client. A client
        /// with no representative is therefore visible to administrators only,
        /// which is the honest reading of "related to them".
        /// </remarks>
        public int? ScopeToRepresentativeUserId { get; init; }
    }

    /// <summary>Register row projection.</summary>
    public sealed class ClientListRow
    {
        public int Id { get; init; }
        public string ClientName { get; init; } = "";
        public string AttentionName { get; init; } = "";
        public string Phone { get; init; } = "";
        public string Email { get; init; } = "";
        public string Trn { get; init; } = "";
        public bool IsActive { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }

        /// <summary>Stable id of the client's representative, or null for none.</summary>
        public int? RepresentativeUserId { get; init; }

        /// <summary>
        /// The representative's name as it reads today, resolved through the
        /// user directory for display. Empty when there is no representative or
        /// the referenced row is gone; the stored id is never rewritten because
        /// a person was renamed.
        /// </summary>
        public string RepresentativeName { get; set; } = "";

        /// <summary>Register wording for the representative column.</summary>
        public string RepresentativeText =>
            RepresentativeUserId is null
                ? "—"
                : (string.IsNullOrWhiteSpace(RepresentativeName) ? "(missing user)" : RepresentativeName);

        /// <summary>StatusBadge tag vocabulary: Success for active, neutral otherwise.</summary>
        public string StatusTag => IsActive ? "Success" : "";

        public string StatusText => IsActive ? "Active" : "Inactive";

        /// <summary>
        /// Label for the row's lifecycle action. The register shows it in the row
        /// menu, so the wording lives with the projection instead of a view-model
        /// property that would only ever describe the selected row.
        /// </summary>
        public string ToggleLabel => IsActive ? "Deactivate client" : "Re-activate client";

        /// <summary>
        /// One line naming the person to address plus their reachable details,
        /// without empty separators.
        /// </summary>
        public string AttentionSummary
        {
            get
            {
                var parts = new List<string>(3);
                if (!string.IsNullOrWhiteSpace(AttentionName)) parts.Add(AttentionName);
                if (!string.IsNullOrWhiteSpace(Phone)) parts.Add(Phone);
                if (!string.IsNullOrWhiteSpace(Email)) parts.Add(Email);
                return string.Join("  ·  ", parts);
            }
        }

        public string UpdatedText => UpdatedAt == default
            ? "—"
            : UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        public string CreatedText => CreatedAt == default
            ? "—"
            : CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }

    /// <summary>Outcome of a create/update/deactivate call.</summary>
    public sealed class ClientSaveResult
    {
        public bool Succeeded { get; init; }
        public Client? Client { get; init; }
        /// <summary>Operator-facing message; empty on success.</summary>
        public string Message { get; init; } = "";

        public static ClientSaveResult Ok(Client client) =>
            new() { Succeeded = true, Client = client };

        public static ClientSaveResult Fail(string message) =>
            new() { Succeeded = false, Message = message };
    }

    /// <summary>One selectable representative: a stable user id and its name.</summary>
    public sealed class ClientRepresentative
    {
        public int Id { get; init; }
        public string DisplayName { get; init; } = "";

        public override string ToString() => DisplayName;
    }

    /// <summary>Client directory persistence.</summary>
    public interface IClientRepository
    {
        /// <summary>
        /// Ensures the ERP database file exists at the current schema and that
        /// the Clients table (migration 1011) is present. Safe to call more
        /// than once.
        /// </summary>
        Task InitializeAsync(CancellationToken ct = default);

        /// <summary>Register rows, active-status-first then by name.</summary>
        Task<IReadOnlyList<ClientListRow>> QueryAsync(ClientQuery query, CancellationToken ct = default);

        /// <summary>One client by stable id, or null.</summary>
        Task<Client?> GetAsync(int id, CancellationToken ct = default);

        /// <summary>Inserts a new client from a validated draft.</summary>
        Task<ClientSaveResult> CreateAsync(ClientDraft draft, CancellationToken ct = default);

        /// <summary>Updates an existing client. Never changes the id.</summary>
        Task<ClientSaveResult> UpdateAsync(int id, ClientDraft draft, CancellationToken ct = default);

        /// <summary>Activates or deactivates a client. Never deletes a row.</summary>
        Task<ClientSaveResult> SetActiveAsync(int id, bool isActive, CancellationToken ct = default);

        /// <summary>
        /// Existing clients that resemble the draft (name, phone, e-mail, TRN).
        /// Informational only: the caller shows a warning and may still save.
        /// </summary>
        Task<IReadOnlyList<ClientDuplicateMatch>> FindDuplicatesAsync(
            ClientDraft draft, int? excludeClientId = null, CancellationToken ct = default);

        /// <summary>
        /// Active users a client may be assigned to, named for the picker. The
        /// list is the salesman role from the shared user catalogue, ordered by
        /// name; a client may also have no representative at all.
        /// </summary>
        Task<IReadOnlyList<ClientRepresentative>> GetRepresentativesAsync(CancellationToken ct = default);

        /// <summary>True when the Clients table is present and shaped as expected.</summary>
        Task<bool> IsSchemaReadyAsync(CancellationToken ct = default);
    }
}
