// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/Contacts/ContactResolver.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — contact resolution (Stage 1)
//
// FROM = the ProGlass user: logged-in AppUser first (name + role +
// contact fields), falling back to the document's prepared-by snapshot
// (older docs, or a share opened by someone other than the preparer).
//
// TO = resolved from ERP data, never typed, except through the explicit
// manual fallback. Default preference order:
//     Customer Attention → Customer → Company Representative → Salesman
// The composer pre-selects the first candidate that can satisfy the
// chosen provider (phone for WhatsApp, email for Email) and always offers
// "Change contact".
//
// LIVE LOOKUPS, NO DUPLICATION. When the context carries only a customer
// NAME (reports), the resolver fills phone/attention from MasterData
// (FindClientByNameAsync); the company representative is likewise filled
// from SavedCompany (FindCompanyByNameAsync). Nothing is copied into a
// sharing database — there is none, by design.
//
// CONTACT REALITY (verified against this codebase):
//   • SavedClient: AttentionExecutiveName + ClientContactPhone (no email).
//   • SavedCompany: Representative + RepresentativeMobile + CompanyEmail.
//   • SavedSalesman: NAME ONLY — no phone/email columns exist, so the
//     salesman is offered name-only and the operator supplies a number via
//     the composer's manual contact when needed. Nothing is fabricated.
//   • Direct channels win: TI snapshots carry CustomerPhone/CustomerEmail.
//
// Never throws: resolution failure yields fewer candidates, never a crash.
// The composer shows "no contact on file" with the manual fallback when
// the ERP genuinely knows nobody for the document.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Audit;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.MasterData.Repository;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Sharing.Contacts
{
    /// <summary>FROM identity: the ProGlass user behind the share.</summary>
    public sealed class ShareSender
    {
        public string Name { get; }
        public string Role { get; }
        public string Mobile { get; }
        public string Email { get; }

        /// <summary>True when from the live user, false when from snapshot.</summary>
        public bool IsLiveUser { get; }

        public ShareSender(
            string name, string role = "", string mobile = "",
            string email = "", bool isLiveUser = false)
        {
            Name = (name ?? "").Trim();
            Role = (role ?? "").Trim();
            Mobile = (mobile ?? "").Trim();
            Email = (email ?? "").Trim();
            IsLiveUser = isLiveUser;
        }

        /// <summary>"Juber Saleem · Estimator" (role omitted when blank).</summary>
        public string DisplayLine =>
            string.IsNullOrWhiteSpace(Role) ? Name : $"{Name} · {Role}";
    }

    public interface IContactResolver
    {
        /// <summary>FROM for this share (live user → prepared-by fallback).</summary>
        ShareSender ResolveSender(ShareContext context);

        /// <summary>
        /// TO candidates in preference order, gaps filled from MasterData.
        /// </summary>
        Task<List<ShareRecipient>> ResolveRecipientsAsync(ShareContext context);
    }

    public sealed class ContactResolver : IContactResolver
    {
        private readonly IMasterDataRepository? _masterData;

        /// <summary>
        /// Production uses the app-wide MasterData repository; tests (and
        /// future callers) may inject a fake. Additive overload — the
        /// parameterless construction used by the composer is unchanged.
        /// </summary>
        public ContactResolver(IMasterDataRepository? masterData = null)
        {
            _masterData = masterData;
        }

        private IMasterDataRepository? MasterData
        {
            get
            {
                if (_masterData != null) return _masterData;
                try { return App.MasterData; }
                catch { return null; }
            }
        }

        public ShareSender ResolveSender(ShareContext context)
        {
            try
            {
                AppUser? live = AuditService.CurrentUser;
                if (live != null)
                {
                    string name = !string.IsNullOrWhiteSpace(live.DisplayName)
                        ? live.DisplayName.Trim()
                        : (live.Username ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return new ShareSender(
                            name, live.Role ?? "",
                            live.Mobile ?? "", live.Email ?? "",
                            isLiveUser: true);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Sharing.Resolver] live user read failed: {ex.Message}");
            }

            // Fallback: whoever prepared the document (snapshot survives
            // user switches, deletions and older databases).
            return new ShareSender(
                string.IsNullOrWhiteSpace(context.PreparedByName)
                    ? "ProGlass" : context.PreparedByName,
                context.PreparedByRole,
                context.PreparedByMobile,
                context.PreparedByEmail,
                isLiveUser: false);
        }

        public async Task<List<ShareRecipient>> ResolveRecipientsAsync(
            ShareContext context)
        {
            var result = new List<ShareRecipient>();
            try
            {
                string customer = (context.CustomerName ?? "").Trim();
                string attention = (context.AttentionName ?? "").Trim();
                string phone = (context.CustomerPhone ?? "").Trim();
                string email = (context.CustomerEmail ?? "").Trim();

                // Gap-fill from MasterData when the context only names the
                // customer (reports) — a live read, never a copy.
                if (!string.IsNullOrWhiteSpace(customer)
                    && (string.IsNullOrWhiteSpace(attention)
                        || string.IsNullOrWhiteSpace(phone)))
                {
                    try
                    {
                        var repo = MasterData;
                        var client = repo == null ? null
                            : await repo
                                .FindClientByNameAsync(customer)
                                .ConfigureAwait(false);
                        if (client != null)
                        {
                            if (string.IsNullOrWhiteSpace(attention))
                                attention = (client.AttentionExecutiveName ?? "").Trim();
                            if (string.IsNullOrWhiteSpace(phone))
                                phone = (client.ClientContactPhone ?? "").Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogService.Debug(
                            $"[Sharing.Resolver] client lookup failed: {ex.Message}");
                    }
                }

                // 1. Customer attention (name + customer channels). Skipped
                // when it would duplicate the customer row exactly.
                if (!string.IsNullOrWhiteSpace(attention)
                    && !string.IsNullOrWhiteSpace(customer)
                    && !string.Equals(attention, customer,
                        StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new ShareRecipient(
                        ShareRecipientKind.CustomerAttention,
                        attention, customer, phone, email));
                }
                // 2. Customer primary.
                if (!string.IsNullOrWhiteSpace(customer))
                {
                    result.Add(new ShareRecipient(
                        ShareRecipientKind.Customer,
                        customer, customer, phone, email));
                }

                // 3. Own-company representative (internal forwarding).
                // Direct context channels win; MasterData fills the gaps.
                string company = (context.CompanyName ?? "").Trim();
                string rep = (context.CompanyRepresentative ?? "").Trim();
                string repPhone = (context.CompanyRepMobile ?? "").Trim();
                string repEmail = (context.CompanyEmail ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(company)
                    && (string.IsNullOrWhiteSpace(rep)
                        || string.IsNullOrWhiteSpace(repPhone)
                        || string.IsNullOrWhiteSpace(repEmail)))
                {
                    try
                    {
                        var repo = MasterData;
                        var saved = repo == null ? null
                            : await repo
                                .FindCompanyByNameAsync(company)
                                .ConfigureAwait(false);
                        if (saved != null)
                        {
                            if (string.IsNullOrWhiteSpace(rep))
                                rep = (saved.Representative ?? "").Trim();
                            if (string.IsNullOrWhiteSpace(repPhone))
                                repPhone = (saved.RepresentativeMobile ?? "").Trim();
                            if (string.IsNullOrWhiteSpace(repEmail))
                                repEmail = (saved.CompanyEmail ?? "").Trim();
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogService.Debug(
                            $"[Sharing.Resolver] company lookup failed: {ex.Message}");
                    }
                }
                if (!string.IsNullOrWhiteSpace(rep))
                {
                    result.Add(new ShareRecipient(
                        ShareRecipientKind.CompanyRepresentative,
                        rep, company, repPhone, repEmail));
                }

                // 4. Salesman on the document (name only — the ERP stores no
                // salesman phone; offered for completeness, skipped by
                // phone/email providers unless the operator supplies one).
                string salesman = (context.SalesmanName ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(salesman))
                {
                    result.Add(new ShareRecipient(
                        ShareRecipientKind.Salesman, salesman, customer));
                }
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Sharing.Resolver] resolve failed: {ex.Message}");
            }

            return result;
        }
    }
}
