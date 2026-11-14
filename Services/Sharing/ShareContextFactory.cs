// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/ShareContextFactory.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — per-document context builders (Stage 3)
//
// ONE reviewed mapping per entity type. Modules call a single method —
// they never hand-fill contact fields, so a renamed column breaks here
// (loudly, in one place) instead of silently in fifteen view-models.
//
// Field provenance (all verified against the models in this repo):
//   PI  entity: ProformaId/CompanyName/CustomerName/IssueDate/totals.
//       PI  snapshot: Company.* (rep + mobile + email, ValidUntil),
//       Client.* (attention + contact phone + salesman + prepared-by),
//       PreparedBy.* (name + dept).
//   JO  entity: JobOrderId/CustomerName/GrandTotalVatInclusive/
//       DeliveryDate/CreatedAt. (Customer contact comes from the
//       MasterData client lookup.)
//   DN  entity: DeliveryNoteId/CustomerName/ReceiverName/DeliveryDate.
//       (Contacts via MasterData client lookup by customer name.)
//   TI  entity: InvoiceNumber/CustomerName/CompanyName/GrandTotal/
//       IssueDate. TI snapshot is NESTED: Customer.* (phone + email +
//       contact person), Company.* (email), Header.* (dates),
//       PreparedBy.* (name + dept).
//   Statement / OrderReport: Number + Title + FilterCustomer/
//       FilterCompany/FilterSalesman + CreatedAt.
//   Lists & reports: title-led contexts; recipients resolve from the
//   module-supplied customer/company names when the screen has them.
//
// Money: books always stay in AED (AppConfig.Localization comment) —
// Currency is "AED" wherever an Amount is set. DocumentDate feeds the
// {Date} template variable (PI shows ValidUntil per the built-in text).
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Models.JobOrder.SavedJobOrder;
using ProGlassAutomation.Models.OrderReport;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Models.Statement;
using ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice;
using System;

namespace ProGlassAutomation.Services.Sharing
{
    public static class ShareContextFactory
    {
        /// <summary>Books currency — document totals are always AED.</summary>
        public const string BooksCurrency = "AED";

        public static ShareContext FromProformaInvoice(
            SavedProformaInvoice entity,
            ProformaInvoiceSnapshot? snapshot = null)
        {
            var ctx = new ShareContext
            {
                DocumentTypeKey = "PI",
                DocumentTypeDisplay = "Proforma Invoice",
                DocumentNumber = entity?.ProformaId ?? "",
                CustomerName = entity?.CustomerName ?? "",
                CompanyName = entity?.CompanyName ?? "",
                SourceModule = "ProformaInvoice"
            };

            if (entity != null)
            {
                if (entity.IssueDate != default)
                    ctx.DocumentDate = entity.IssueDate;
                if (entity.GrandTotalVatInclusive > 0)
                {
                    ctx.Amount = entity.GrandTotalVatInclusive;
                    ctx.Currency = BooksCurrency;
                }
            }

            if (snapshot != null)
            {
                ctx.AttentionName =
                    snapshot.Client?.AttentionExecutiveName ?? "";
                ctx.CustomerPhone =
                    snapshot.Client?.ClientContactPhone ?? "";
                ctx.SalesmanName =
                    snapshot.Client?.SalesRepresentative ?? "";
                ctx.CompanyRepresentative =
                    snapshot.Company?.Representative ?? "";
                ctx.CompanyRepMobile =
                    snapshot.Company?.RepresentativeMobile ?? "";
                ctx.CompanyEmail =
                    snapshot.Company?.CompanyEmail ?? "";
                ctx.PreparedByName =
                    snapshot.PreparedBy?.PreparedByName ?? "";
                ctx.PreparedByRole =
                    snapshot.PreparedBy?.PreparedByDept ?? "";

                // The PI template reads "Valid until {Date}".
                if (snapshot.Company != null
                    && snapshot.Company.ValidUntil != default)
                    ctx.DocumentDate = snapshot.Company.ValidUntil;
            }

            return ctx;
        }

        public static ShareContext FromJobOrder(SavedJobOrder entity)
        {
            var ctx = new ShareContext
            {
                DocumentTypeKey = "JO",
                DocumentTypeDisplay = "Job Order",
                DocumentNumber = entity?.JobOrderId ?? "",
                CustomerName = entity?.CustomerName ?? "",
                SourceModule = "JobOrder"
            };

            if (entity != null)
            {
                if (entity.DeliveryDate.HasValue)
                    ctx.DocumentDate = entity.DeliveryDate.Value;
                else if (entity.CreatedAt != default)
                    ctx.DocumentDate = entity.CreatedAt;
                if (entity.GrandTotalVatInclusive > 0)
                {
                    ctx.Amount = entity.GrandTotalVatInclusive;
                    ctx.Currency = BooksCurrency;
                }
            }

            return ctx;
        }

        public static ShareContext FromDeliveryNote(
            SavedDeliveryNote entity,
            DeliveryNoteSnapshot? snapshot = null)
        {
            var ctx = new ShareContext
            {
                DocumentTypeKey = "DN",
                DocumentTypeDisplay = "Delivery Note",
                DocumentNumber = entity?.DeliveryNoteId ?? "",
                CustomerName = entity?.CustomerName ?? "",
                SourceModule = "DeliveryNote"
            };

            if (entity != null)
            {
                if (entity.DeliveryDate != default)
                    ctx.DocumentDate = entity.DeliveryDate;
                string receiver = (entity.ReceiverName ?? "").Trim();
                if (receiver.Length == 0 && snapshot?.Header != null)
                    receiver = (snapshot.Header.ReceiverName ?? "").Trim();
                ctx.AttentionName = receiver;
            }

            return ctx;
        }

        public static ShareContext FromTaxInvoice(
            SavedTaxInvoice entity,
            TaxInvoiceSnapshot? snapshot = null)
        {
            var ctx = new ShareContext
            {
                DocumentTypeKey = "TI",
                DocumentTypeDisplay = "Tax Invoice",
                DocumentNumber = entity?.InvoiceNumber ?? "",
                CustomerName = entity?.CustomerName ?? "",
                CompanyName = entity?.CompanyName ?? "",
                SourceModule = "TaxInvoice"
            };

            if (entity != null)
            {
                if (entity.IssueDate != default)
                    ctx.DocumentDate = entity.IssueDate;
                if (entity.GrandTotal > 0)
                {
                    ctx.Amount = entity.GrandTotal;
                    ctx.Currency = BooksCurrency;
                }
            }

            if (snapshot != null)
            {
                ctx.AttentionName =
                    snapshot.Customer?.ContactPerson ?? "";
                ctx.CustomerPhone =
                    snapshot.Customer?.CustomerPhone ?? "";
                ctx.CustomerEmail =
                    snapshot.Customer?.CustomerEmail ?? "";
                ctx.CompanyEmail =
                    snapshot.Company?.CompanyEmail ?? "";
                ctx.PreparedByName =
                    snapshot.PreparedBy?.Name ?? "";
                ctx.PreparedByRole =
                    !string.IsNullOrWhiteSpace(snapshot.PreparedBy?.Department)
                        ? snapshot.PreparedBy.Department
                        : snapshot.PreparedBy?.Title ?? "";

                if (snapshot.Header != null
                    && snapshot.Header.IssueDate != default)
                    ctx.DocumentDate = snapshot.Header.IssueDate;
            }

            return ctx;
        }

        public static ShareContext FromStatement(SavedStatement row)
        {
            string number = row?.StatementNumber ?? "";
            var ctx = new ShareContext
            {
                DocumentTypeKey = "Statement",
                DocumentTypeDisplay = "Statement",
                DocumentNumber = number,
                Title = string.IsNullOrWhiteSpace(row?.Title)
                    ? $"Statement {number}".Trim()
                    : row.Title.Trim(),
                CustomerName = row?.FilterCustomer ?? "",
                CompanyName = row?.FilterCompany ?? "",
                SalesmanName = row?.FilterSalesman ?? "",
                SourceModule = "Statement"
            };

            if (row != null && row.CreatedAt != default)
                ctx.DocumentDate = row.CreatedAt;

            return ctx;
        }

        public static ShareContext FromOrderReport(SavedOrderReport row)
        {
            string number = row?.ReportNumber ?? "";
            var ctx = new ShareContext
            {
                DocumentTypeKey = "OrderReport",
                DocumentTypeDisplay = "Order Report",
                DocumentNumber = number,
                Title = string.IsNullOrWhiteSpace(row?.Title)
                    ? $"Order Report {number}".Trim()
                    : row.Title.Trim(),
                CustomerName = row?.FilterCustomer ?? "",
                CompanyName = row?.FilterCompany ?? "",
                SalesmanName = row?.FilterSalesman ?? "",
                SourceModule = "OrderReport"
            };

            if (row != null && row.CreatedAt != default)
                ctx.DocumentDate = row.CreatedAt;

            return ctx;
        }

        /// <summary>
        /// Title-led context for list/report/wizard-analysis screens. The
        /// module passes its screen's customer/company filter when it has
        /// one so the composer can still resolve recipients.
        /// </summary>
        public static ShareContext FromListReport(
            string documentTypeKey,
            string title,
            string sourceModule,
            string? customerName = null,
            string? companyName = null)
        {
            string cleanTitle = (title ?? "").Trim();
            return new ShareContext
            {
                DocumentTypeKey = documentTypeKey ?? "",
                DocumentTypeDisplay = cleanTitle,
                Title = cleanTitle,
                CustomerName = customerName ?? "",
                CompanyName = companyName ?? "",
                SourceModule = sourceModule ?? ""
            };
        }
    }
}
