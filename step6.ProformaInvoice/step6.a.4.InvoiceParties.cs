// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.4.InvoiceParties.cs
// MODULE: step6 — the header stage's party block (ADV steps 1–3, merged).
//
// THE RULE THIS FILE EXISTS TO ENFORCE
//   The company, the client, the project and the person preparing the
//   document already have owners: step4 Company Profile, step2 Client
//   Management, step5 Project Records and step3 User Management. This module
//   does not retype them, does not keep a second copy as the source of truth,
//   and never asks the operator for the same detail twice.
//
//   What IS stored with the invoice is a SNAPSHOT of what the document says,
//   because a quotation must print what it said when it was issued even after
//   a client is renamed or a salesman leaves. The snapshot is refreshed from
//   the owning module while the invoice is a Draft (see InvoicePartyReader);
//   once issued it is frozen. Ids are kept beside the names so reporting can
//   link, and so a name is never the identity.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>The document's own letterhead, copied from the Company Profile.</summary>
    public sealed class CompanyIdentity
    {
        public string CompanyName { get; set; } = "";
        public string Trn { get; set; } = "";
        public string Address { get; set; } = "";
        public string Telephone { get; set; } = "";
        public string Website { get; set; } = "";
        public string Email { get; set; } = "";
        public string RepresentativeName { get; set; } = "";
        public string RepresentativeMobile { get; set; } = "";
        public string BankName { get; set; } = "";
        public string AccountNumber { get; set; } = "";
        public string LogoFileName { get; set; } = "";
        /// <summary>None / LeftPortrait / TopPanel, as stored by step4.</summary>
        public string LogoLayout { get; set; } = "None";
        public double LogoSizeMm { get; set; }

        /// <summary>True when there is a letterhead to print at all.</summary>
        [JsonIgnore]
        public bool IsEmpty => CompanyName.Trim().Length == 0;

        public CompanyIdentity Clone() => (CompanyIdentity)MemberwiseClone();
    }

    /// <summary>The client the quotation is addressed to, copied from step2.</summary>
    public sealed class ClientIdentity
    {
        /// <summary>Stable id into Clients; 0 for a one-off customer.</summary>
        public int ClientId { get; set; }
        public string ClientName { get; set; } = "";
        public string AttentionName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public string Trn { get; set; } = "";
        public string BillingAddress { get; set; } = "";

        /// <summary>The client's representative (salesman) at the time of issue.</summary>
        public int? SalesRepresentativeUserId { get; set; }
        public string SalesRepresentativeName { get; set; } = "";

        [JsonIgnore]
        public bool IsEmpty => ClientName.Trim().Length == 0;

        /// <summary>A one-off customer must still be named; it needs no client row.</summary>
        [JsonIgnore]
        public bool HasCustomer => ClientName.Trim().Length > 0;

        public ClientIdentity Clone() => (ClientIdentity)MemberwiseClone();
    }

    /// <summary>The project/site the quotation is for, copied from step5.</summary>
    public sealed class ProjectIdentity
    {
        public int ProjectId { get; set; }
        public string ProjectNumber { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string Location { get; set; } = "";
        public string ContractReference { get; set; } = "";
        public string LpoNumber { get; set; } = "";
        public string Status { get; set; } = "";

        [JsonIgnore]
        public bool IsEmpty => ProjectId == 0 && ProjectName.Trim().Length == 0;

        /// <summary>One line naming the project without empty separators.</summary>
        [JsonIgnore]
        public string DisplayText
        {
            get
            {
                var parts = new List<string>(3);
                if (ProjectNumber.Trim().Length > 0) parts.Add(ProjectNumber.Trim());
                if (ProjectName.Trim().Length > 0) parts.Add(ProjectName.Trim());
                if (Location.Trim().Length > 0) parts.Add(Location.Trim());
                return string.Join(" · ", parts);
            }
        }

        public ProjectIdentity Clone() => (ProjectIdentity)MemberwiseClone();
    }

    /// <summary>
    /// The person who prepared the document. The id is the identity; the name
    /// and role are the SNAPSHOT the document prints. Attribution never relies
    /// on the workstation login (G1-06), and a rename cannot rewrite history.
    /// </summary>
    public sealed class PreparedByIdentity
    {
        public int? UserId { get; set; }
        public string FullName { get; set; } = "";
        public string Role { get; set; } = "";
        public string Department { get; set; } = "";

        [JsonIgnore]
        public bool IsEmpty => UserId is null && FullName.Trim().Length == 0;

        [JsonIgnore]
        public string DisplayText
        {
            get
            {
                if (FullName.Trim().Length == 0) return "";
                return Role.Trim().Length == 0 ? FullName.Trim() : $"{FullName.Trim()} · {Role.Trim()}";
            }
        }

        public PreparedByIdentity Clone() => (PreparedByIdentity)MemberwiseClone();
    }

    /// <summary>The optional source inquiry, kept as a link and a printed reference.</summary>
    public sealed class InquiryLink
    {
        public int? InquiryId { get; set; }
        public string InquiryNumber { get; set; } = "";
        public string InquiryReference { get; set; } = "";

        [JsonIgnore]
        public bool IsLinked => InquiryId is > 0;

        public InquiryLink Clone() => (InquiryLink)MemberwiseClone();
    }

    /// <summary>Everything the header stage owns, in one serializable block.</summary>
    public sealed class InvoiceParties
    {
        public CompanyIdentity Company { get; set; } = new();
        public ClientIdentity Client { get; set; } = new();
        public ProjectIdentity Project { get; set; } = new();
        public PreparedByIdentity PreparedBy { get; set; } = new();
        public InquiryLink Inquiry { get; set; } = new();

        /// <summary>
        /// A timestamp of the last time the master-data snapshot was refreshed,
        /// so the document can say how old its copy is. UTC.
        /// </summary>
        public DateTime PartiesRefreshedAtUtc { get; set; }

        /// <summary>
        /// Which master records could not be read at refresh time. Empty is the
        /// normal state; a missing client record for a one-off customer is not
        /// an error and is therefore not listed.
        /// </summary>
        public List<string> RefreshWarnings { get; set; } = new();

        public InvoiceParties Clone()
        {
            var copy = (InvoiceParties)MemberwiseClone();
            copy.Company = CopyCompany(Company);
            copy.Client = CopyClient(Client);
            copy.Project = CopyProject(Project);
            copy.PreparedBy = CopyPreparedBy(PreparedBy);
            copy.Inquiry = CopyInquiry(Inquiry);
            copy.RefreshWarnings = new List<string>(RefreshWarnings);
            return copy;
        }

        // Copies are made through these helpers rather than by calling a property's
        // Clone() directly: a property named Client or Project reads, to a reader
        // (and to a static analyser), like the type of the same name, and the
        // header must never be mistaken for a reference to another module's model.
        private static CompanyIdentity CopyCompany(CompanyIdentity value) => value.Clone();
        private static ClientIdentity CopyClient(ClientIdentity value) => value.Clone();
        private static ProjectIdentity CopyProject(ProjectIdentity value) => value.Clone();
        private static PreparedByIdentity CopyPreparedBy(PreparedByIdentity value) => value.Clone();
        private static InquiryLink CopyInquiry(InquiryLink value) => value.Clone();

        /// <summary>
        /// The header is usable when the letterhead and the customer exist.
        /// Project and salesman are optional: not every job has a project record
        /// and a one-off customer has no representative.
        /// </summary>
        [JsonIgnore]
        public bool IsComplete => !Company.IsEmpty && HasCustomerName(Client);

        private static bool HasCustomerName(ClientIdentity value) => value.HasCustomer;
    }
}
