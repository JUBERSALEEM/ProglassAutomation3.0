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
//   • There is NO sales-representative column and no representative name
//     string. Representative assignment is deferred to the User Management
//     stage, which will add a stable nullable user reference by migration.
//     Adding a free-text rep name here is the ADV mistake this module avoids.
//   • ClientName is the company (the entity we invoice); AttentionName is the
//     person at that company to address. Both are stored, and neither is
//     derived from the other.
//   • IsActive replaces deletion: inactive clients stay readable for
//     historical display and reporting.
//   • Trn is TEXT: a tax registration number is an identifier, not a number,
//     and leading zeros must survive.
//   • No Inquiry relationship exists in this stage. Inquiries keep their
//     own snapshot columns untouched.
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
                IsActive = client.IsActive
            };
        }
    }
}
