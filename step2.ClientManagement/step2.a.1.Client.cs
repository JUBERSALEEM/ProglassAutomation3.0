// ═══════════════════════════════════════════════════════════════════════
// FILE: step2.ClientManagement/step2.a.1.Client.cs
// MODULE: step2 — Client Management, domain model.
//
// One row of the glass company's customer directory. Persisted in the
// existing inquiries.db (step2.b), created by migration 1011.
//
// Deliberate design decisions (owner-approved 2026-10-27):
//   • Id is the only identifier other modules may reference. The name is
//     data, never a key.
//   • The representative is a STABLE USER REFERENCE, added by migration 1014
//     once User Management existed: RepresentativeUserId is a nullable id into
//     Users, never a typed name. It is nullable because the client rows that
//     already exist have no representative, and a client may genuinely have
//     none yet. A free-text rep name is the ADV mistake this module avoids.
//   • ClientName is the company (the entity we invoice); AttentionName is the
//     person at that company to address. Both are stored, and neither is
//     derived from the other.
//   • IsActive replaces deletion: inactive clients stay readable for
//     historical display and reporting.
//   • Trn is TEXT: a tax registration number is an identifier, not a number,
//     and leading zeros must survive.
//   • This module holds no Inquiry relationship. Since step5 (2026-10-28) an
//     inquiry may carry a nullable ClientId, but that column lives on
//     Inquiries and step5 owns it: nothing here writes it, and this entity
//     gains no navigation property. Inquiries keep their own snapshot text
//     columns untouched, which is why a client rename never rewrites history.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.Clients
{
    /// <summary>One client (customer) of the glass company.</summary>
    [Table("Clients")]
    public sealed class Client
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Client name as entered (display casing retained). Required.</summary>
        [Required, MaxLength(ClientRules.ClientNameMaxLength)]
        public string ClientName { get; set; } = "";

        /// <summary>
        /// The person at the client to address ("Attention: ..." on documents or
        /// correspondence). Optional. Distinct from <see cref="ClientName"/>,
        /// which is the company.
        /// </summary>
        [MaxLength(ClientRules.AttentionNameMaxLength)]
        public string AttentionName { get; set; } = "";

        /// <summary>Contact phone as entered. Optional.</summary>
        [MaxLength(ClientRules.PhoneMaxLength)]
        public string Phone { get; set; } = "";

        /// <summary>Contact e-mail as entered. Optional.</summary>
        [MaxLength(ClientRules.EmailMaxLength)]
        public string Email { get; set; } = "";

        /// <summary>UAE tax registration number as text (may be blank). Optional.</summary>
        [MaxLength(ClientRules.TrnMaxLength)]
        public string Trn { get; set; } = "";

        /// <summary>Billing / correspondence address. Optional.</summary>
        [MaxLength(ClientRules.AddressMaxLength)]
        public string Address { get; set; } = "";

        /// <summary>Free notes about the client. Optional.</summary>
        public string Notes { get; set; } = "";

        /// <summary>
        /// The salesman who owns this client, as a stable reference into the
        /// Users directory (migration 1014). Null means "no representative yet",
        /// which is the state every pre-1014 client row is in.
        /// </summary>
        public int? RepresentativeUserId { get; set; }

        /// <summary>
        /// Inactive clients are retained for history and reporting. Nothing in
        /// this module physically deletes a client row.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Set by the repository on insert (UTC).</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>Set by the repository on every update (UTC).</summary>
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Editable client values coming from the editor. Kept separate from the
    /// persisted entity so validation and duplicate detection can run on a
    /// draft without a database round-trip or a half-built entity.
    /// </summary>
    public sealed class ClientDraft
    {
        public string ClientName { get; set; } = "";
        public string AttentionName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public string Trn { get; set; } = "";
        public string Address { get; set; } = "";
        public string Notes { get; set; } = "";
        public bool IsActive { get; set; } = true;

        /// <summary>Stable user id of the client's representative; null for none.</summary>
        public int? RepresentativeUserId { get; set; }

        /// <summary>Builds a draft from an existing client (edit mode).</summary>
        public static ClientDraft FromClient(Client client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            return new ClientDraft
            {
                ClientName = client.ClientName,
                AttentionName = client.AttentionName,
                Phone = client.Phone,
                Email = client.Email,
                Trn = client.Trn,
                Address = client.Address,
                Notes = client.Notes,
                IsActive = client.IsActive,
                RepresentativeUserId = client.RepresentativeUserId
            };
        }
    }
}
