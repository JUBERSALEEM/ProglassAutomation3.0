// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.b.10.InvoicePartyReader.cs
// MODULE: step6 — stage 1's only way to fill the header.
//
// This is the merged ADV steps 1–3: one call reads the company identity from
// the Company Profile, the client and its representative from Client
// Management, the project from Project Records, the preparing user from User
// Management and, optionally, the source inquiry.
//
// IT DEPENDS ON CONTRACTS, NOT ON TABLES
//   G1-03/G1-04 found modules opening another module's DbContext. This class
//   therefore takes ICompanyProfileReader, IClientRepository,
//   IProjectRepository and IUserRepository, and can read nothing else. The
//   Inquiry module predates a contract interface; the one method it exposes is
//   called on its repository rather than on its table.
//
// A missing master record is a WARNING with a usable message, never a throw:
// a quotation is not blocked because a salesman left the company, and a
// one-off customer is a legitimate row with no client id at all.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Clients;
using ProGlassAutomation.Models.Company;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Models.Projects;
using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Clients;
using ProGlassAutomation.Services.Company;
using ProGlassAutomation.Services.Inquiry;
using ProGlassAutomation.Services.Projects;
using ProGlassAutomation.Services.Users;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>The parties a header can be composed from, plus what was missing.</summary>
    public sealed class InvoicePartyResult
    {
        public InvoiceParties Parties { get; init; } = new();
        public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
        public bool CompanyFound { get; init; }
        public bool ClientFound { get; init; }
    }

    /// <summary>Composes stage 1 from the modules that own each record.</summary>
    public sealed class InvoicePartyReader
    {
        private readonly ICompanyProfileReader _company;
        private readonly IClientRepository _clients;
        private readonly IProjectRepository _projects;
        private readonly IUserRepository _users;
        private readonly Func<InquiryRepository> _inquiries;

        public InvoicePartyReader()
            : this(null, null, null, null, null) { }

        /// <summary>Every dependency is injectable, so a check can use fakes.</summary>
        public InvoicePartyReader(
            ICompanyProfileReader? company = null,
            IClientRepository? clients = null,
            IProjectRepository? projects = null,
            IUserRepository? users = null,
            Func<InquiryRepository>? inquiries = null)
        {
            _company = company ?? new CompanyProfileRepository();
            _clients = clients ?? new ClientRepository();
            _projects = projects ?? new ProjectRepository();
            _users = users ?? new UserRepository();
            _inquiries = inquiries ?? (() => new InquiryRepository());
        }

        /// <summary>
        /// Reads the chosen records into one header. The ids are the caller's
        /// choice; the printed values are always read fresh from their owners.
        /// </summary>
        public async Task<InvoicePartyResult> ComposeAsync(
            int? clientId,
            int? projectId,
            int? preparedByUserId,
            int? inquiryId,
            CancellationToken ct = default)
        {
            var parties = new InvoiceParties();
            var warnings = new List<string>();
            bool companyFound = false, clientFound = false;

            // ── Company letterhead ──────────────────────────────────────
            try
            {
                CompanyIdentitySnapshot? identity = await _company
                    .GetIdentityAsync(ct).ConfigureAwait(false);
                if (identity == null || identity.IsEmpty)
                {
                    warnings.Add("No company profile is saved. Fill it in on the Company page before "
                        + "issuing a quotation.");
                }
                else
                {
                    companyFound = true;
                    parties.Company = new CompanyIdentity
                    {
                        CompanyName = identity.CompanyName,
                        Trn = identity.Trn,
                        Address = identity.Address,
                        Telephone = identity.Telephone,
                        Website = identity.Website,
                        Email = identity.Email,
                        RepresentativeName = identity.RepresentativeName,
                        RepresentativeMobile = identity.RepresentativeMobile,
                        BankName = identity.BankName,
                        AccountNumber = identity.AccountNumber,
                        LogoFileName = identity.LogoFileName,
                        LogoLayout = identity.LogoLayout.ToString(),
                        LogoSizeMm = identity.LogoSizeMm
                    };
                }
            }
            catch (Exception error)
            {
                warnings.Add("The company profile could not be read: " + error.GetBaseException().Message);
            }

            // ── Client and its representative ───────────────────────────
            if (clientId is int id && id > 0)
            {
                try
                {
                    Client? client = await _clients.GetAsync(id, ct).ConfigureAwait(false);
                    if (client == null)
                    {
                        warnings.Add("The selected client no longer exists. Choose another client, or "
                            + "type the customer on the document.");
                    }
                    else
                    {
                        clientFound = true;
                        parties.Client = new ClientIdentity
                        {
                            ClientId = client.Id,
                            ClientName = client.ClientName,
                            AttentionName = client.AttentionName,
                            Phone = client.Phone,
                            Email = client.Email,
                            Trn = client.Trn,
                            BillingAddress = client.Address,
                            SalesRepresentativeUserId = client.RepresentativeUserId
                        };
                        if (!client.IsActive)
                            warnings.Add("This client is inactive. The quotation can still be prepared, "
                                + "but confirm the account before issuing it.");
                        if (client.RepresentativeUserId is int representativeId)
                            parties.Client.SalesRepresentativeName =
                                await ResolveUserNameAsync(representativeId, ct).ConfigureAwait(false);
                    }
                }
                catch (Exception error)
                {
                    warnings.Add("The client record could not be read: " + error.GetBaseException().Message);
                }
            }

            // ── Project ─────────────────────────────────────────────────
            if (projectId is int projectKey && projectKey > 0)
            {
                try
                {
                    Project? project = await _projects.GetAsync(projectKey, ct).ConfigureAwait(false);
                    if (project == null)
                    {
                        warnings.Add("The selected project no longer exists. The quotation will carry no "
                            + "project reference.");
                    }
                    else
                    {
                        parties.Project = new ProjectIdentity
                        {
                            ProjectId = project.Id,
                            ProjectNumber = project.ProjectNumber,
                            ProjectName = project.ProjectName,
                            Location = project.Location,
                            ContractReference = project.Description,
                            Status = project.Status
                        };
                        if (!string.IsNullOrWhiteSpace(project.ClientName) &&
                            clientFound &&
                            !string.Equals(project.ClientName.Trim(), parties.Client.ClientName.Trim(),
                                StringComparison.OrdinalIgnoreCase))
                            warnings.Add("The project belongs to " + project.ClientName.Trim() +
                                ", but the quotation is addressed to " + parties.Client.ClientName.Trim() +
                                ". Check both before issuing.");
                        if (project.IsClosed)
                            warnings.Add("This project is closed. A new quotation for it is unusual; "
                                + "confirm the job is being reopened.");
                    }
                }
                catch (Exception error)
                {
                    warnings.Add("The project record could not be read: " + error.GetBaseException().Message);
                }
            }

            // ── Prepared by ─────────────────────────────────────────────
            int? preparerId = preparedByUserId
                ?? AuthenticationContext.UserId;
            if (preparerId is int userId && userId > 0)
            {
                try
                {
                    User? user = await _users.GetAsync(userId, ct).ConfigureAwait(false);
                    if (user == null)
                    {
                        warnings.Add("The preparing user no longer exists.");
                    }
                    else
                    {
                        parties.PreparedBy = new PreparedByIdentity
                        {
                            UserId = user.Id,
                            FullName = user.FullName,
                            Role = UserCatalog.RoleDisplayName(user.Role),
                            Department = UserCatalog.DepartmentDisplayName(user.Department)
                        };
                        if (!user.IsActive)
                            warnings.Add("The preparing user is inactive. The document still records them "
                                + "as its author.");
                    }
                }
                catch (Exception error)
                {
                    warnings.Add("The user record could not be read: " + error.GetBaseException().Message);
                }
            }

            // ── Source inquiry ──────────────────────────────────────────
            if (inquiryId is int linkedInquiryId && linkedInquiryId > 0)
            {
                try
                {
                    var inquiry = await _inquiries().GetDetailAsync(linkedInquiryId, ct)
                        .ConfigureAwait(false);
                    if (inquiry == null)
                    {
                        warnings.Add("The source inquiry no longer exists.");
                    }
                    else
                    {
                        parties.Inquiry = new InquiryLink
                        {
                            InquiryId = inquiry.Id,
                            InquiryNumber = inquiry.InquiryNumber,
                            InquiryReference = inquiry.InquiryReference
                        };
                        if (!string.IsNullOrWhiteSpace(inquiry.CustomerName) &&
                            !string.IsNullOrWhiteSpace(parties.Client.ClientName) &&
                            !string.Equals(inquiry.CustomerName.Trim(),
                                parties.Client.ClientName.Trim(), StringComparison.OrdinalIgnoreCase))
                            warnings.Add("The inquiry was addressed to " + inquiry.CustomerName.Trim() +
                                ", which differs from the selected client.");
                    }
                }
                catch (Exception error)
                {
                    warnings.Add("The inquiry could not be read: " + error.GetBaseException().Message);
                }
            }

            parties.PartiesRefreshedAtUtc = DateTime.UtcNow;
            parties.RefreshWarnings = warnings;
            return new InvoicePartyResult
            {
                Parties = parties,
                Warnings = warnings,
                CompanyFound = companyFound,
                ClientFound = clientFound
            };
        }

        /// <summary>
        /// Refreshes an existing draft's parties from their owners, keeping the
        /// chosen ids. Refused for an issued document: what the customer was
        /// sent must not change because a client was renamed afterwards.
        /// </summary>
        public async Task<InvoicePartyResult> RefreshAsync(
            ProformaInvoiceDraft draft, CancellationToken ct = default)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (!draft.IsEditable)
                throw new InvalidOperationException(
                    "A " + draft.Status + " invoice keeps the parties it was issued with.");

            int? clientId = draft.Parties.Client.ClientId > 0 ? draft.Parties.Client.ClientId : null;
            int? projectId = draft.Parties.Project.ProjectId > 0 ? draft.Parties.Project.ProjectId : null;
            int? preparedBy = draft.Parties.PreparedBy.UserId;
            int? inquiryId = draft.Parties.Inquiry.InquiryId;

            InvoicePartyResult refreshed = await ComposeAsync(
                clientId, projectId, preparedBy, inquiryId, ct).ConfigureAwait(false);
            draft.Parties = refreshed.Parties;
            return refreshed;
        }

        private async Task<string> ResolveUserNameAsync(int userId, CancellationToken ct)
        {
            try
            {
                User? user = await _users.GetAsync(userId, ct).ConfigureAwait(false);
                return user?.FullName ?? "";
            }
            catch
            {
                // A name is display only; an unreadable directory must not fail
                // the header. The stored id remains the identity.
                return "";
            }
        }
    }
}
