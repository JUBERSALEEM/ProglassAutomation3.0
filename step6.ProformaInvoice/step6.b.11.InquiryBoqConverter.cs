// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.b.11.InquiryBoqConverter.cs
// MODULE: step6 — importing an inquiry's scope of supply into the grid.
//
// The DECISION half (which rows import, in what order, with which glass type)
// is pure and can be checked without a database. The materialisation half asks
// the Inquiry module for the inquiry through its own repository and adds groups
// to the draft; it never reads the inquiry table.
//
// Contract, pinned by the checks:
//   • a row imports only when it names a specification, has a width, a height
//     and a quantity — a partial row deliberately stays on the inquiry;
//   • order is the inquiry's own SortOrder, so the grid mirrors the BOQ;
//   • the glass type is normalized onto SGU/DGU/LAM/DGU+LAM, with DGU+LAM
//     matched first so the compound build is never downgraded to DGU;
//   • a quoted rate (a positive one) becomes the group's rate; an unquoted row
//     leaves pricing to the estimator, rather than forcing a zero price;
//   • an inquiry that already has a live invoice is refused, so one inquiry
//     cannot quietly produce two quotations.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Inquiry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>One import decision for a BOQ line.</summary>
    public sealed class BoqImportRow
    {
        public string GlassType { get; init; } = "";
        public string SpecName { get; init; } = "";
        public string OuterGlassColor { get; init; } = "";
        public string InnerGlassColor { get; init; } = "";
        public int WidthMm { get; init; }
        public int HeightMm { get; init; }
        public int Qty { get; init; }
        public decimal RatePerSqm { get; init; }
        public bool HasQuotedRate { get; init; }
    }

    /// <summary>Outcome of an import, with what was skipped and why.</summary>
    public sealed class BoqImportResult
    {
        public bool Succeeded { get; init; }
        public int GroupsAdded { get; init; }
        public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

        public static BoqImportResult Fail(string message) =>
            new() { Succeeded = false, Messages = new[] { message } };
    }

    /// <summary>Turns inquiry BOQ lines into grid groups.</summary>
    public static class InquiryBoqConverter
    {
        /// <summary>Pure decisions: importable rows, in the inquiry's order.</summary>
        public static IReadOnlyList<BoqImportRow> ToImportRows(
            IReadOnlyList<InquiryLineItem>? items)
        {
            if (items == null || items.Count == 0) return Array.Empty<BoqImportRow>();

            return items
                .Where(item => item != null && item.IsImportable)
                .OrderBy(item => item.SortOrder)
                .Select(item => new
                {
                    Item = item,
                    GlassType = GlassBuilds.Normalize(item.GlassType) is { Length: > 0 } normalized
                        ? normalized
                        : OtherChargeAutomation.EffectiveGlassType(item.GlassType, item.SpecName)
                })
                // A complete row whose build cannot be identified is refused: the
                // grid must not inherit whatever type happened to be selected.
                .Where(entry => entry.GlassType.Length > 0)
                .Select(entry => new BoqImportRow
                {
                    GlassType = entry.GlassType,
                    SpecName = (entry.Item.SpecName ?? "").Trim(),
                    OuterGlassColor = (entry.Item.OuterGlassColor ?? "").Trim(),
                    InnerGlassColor = (entry.Item.InnerGlassColor ?? "").Trim(),
                    WidthMm = entry.Item.WidthMm,
                    HeightMm = entry.Item.HeightMm,
                    Qty = entry.Item.Qty,
                    RatePerSqm = entry.Item.RatePerSqm,
                    HasQuotedRate = entry.Item.RatePerSqm > 0m
                })
                .ToList();
        }

        /// <summary>
        /// Adds the inquiry's importable rows to a draft as priced groups. Each
        /// row becomes one group so its dimensions, colours and quantity stay
        /// exactly as the customer asked; a quoted rate becomes the group rate.
        /// </summary>
        public static async Task<BoqImportResult> ImportAsync(
            ProformaInvoiceDraft draft,
            int inquiryId,
            Func<InquiryRepository>? inquiryFactory = null,
            CancellationToken ct = default)
        {
            if (draft == null) return BoqImportResult.Fail("No invoice was supplied.");
            if (!draft.IsEditable)
                return BoqImportResult.Fail(
                    "A " + draft.Status + " invoice cannot import scope; revise it into a draft first.");
            if (inquiryId <= 0) return BoqImportResult.Fail("No inquiry was selected.");

            InquiryRecord? inquiry;
            try
            {
                var repository = (inquiryFactory ?? (() => new InquiryRepository()))();
                inquiry = await repository.GetDetailAsync(inquiryId, ct).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                return BoqImportResult.Fail(
                    "The inquiry could not be read: " + error.GetBaseException().Message);
            }

            if (inquiry == null)
                return BoqImportResult.Fail("The inquiry no longer exists.");
            if (inquiry.ProformaInvoiceId is int existingId && existingId > 0)
                return BoqImportResult.Fail(
                    "This inquiry is already quoted as " +
                    (string.IsNullOrWhiteSpace(inquiry.ProformaNumber)
                        ? "another proforma invoice"
                        : inquiry.ProformaNumber) + ". Revision is a deliberate act, not an import.");

            IReadOnlyList<BoqImportRow> rows = ToImportRows(inquiry.LineItems);
            var messages = new List<string>();
            if (rows.Count == 0)
            {
                int total = inquiry.LineItems?.Count ?? 0;
                return new BoqImportResult
                {
                    Succeeded = false,
                    Messages = new[]
                    {
                        total == 0
                            ? "The inquiry has no scope of supply to import."
                            : "None of the inquiry's " + total + " scope row(s) is complete enough to import. "
                              + "A row needs a specification, a width, a height and a quantity."
                    }
                };
            }

            draft.EnsureStructure();
            foreach (BoqImportRow row in rows)
            {
                int groupNumber = draft.Groups.Count == 0
                    ? 1 : draft.Groups.Max(group => group.GroupNumber) + 1;

                var group = new SpecGroup
                {
                    GroupNumber = groupNumber,
                    GlassType = row.GlassType,
                    SpecName = row.SpecName,
                    OuterGlassColor = row.OuterGlassColor,
                    InnerGlassColor = row.InnerGlassColor,
                    BaseRatePerSqm = row.HasQuotedRate ? row.RatePerSqm : 0m,
                    ImportedRatePerSqm = row.RatePerSqm,
                    HasManualSpecOverride = true,
                    HasManualGlassTypeOverride = true,
                    HasManualRateOverride = row.HasQuotedRate
                };
                group.Rows.Add(new SpecRow
                {
                    GlassRef = "W1",
                    W1 = row.WidthMm,
                    H1 = row.HeightMm,
                    Qty = row.Qty
                });
                draft.Groups.Add(group);
            }

            draft.Parties.Inquiry = new InquiryReference
            {
                InquiryId = inquiry.Id,
                InquiryNumber = inquiry.InquiryNumber,
                InquiryReference = inquiry.InquiryReference
            };
            draft.Recalculate();

            int skipped = (inquiry.LineItems?.Count ?? 0) - rows.Count;
            messages.Add(rows.Count + " scope line(s) imported from " + inquiry.InquiryNumber + ".");
            if (skipped > 0)
                messages.Add(skipped + " incomplete line(s) stayed on the inquiry.");
            if (rows.Any(row => !row.HasQuotedRate))
                messages.Add("Rows without a quoted rate keep the grid's own pricing — set their rate "
                    + "or apply a pricing section.");

            return new BoqImportResult
            {
                Succeeded = true,
                GroupsAdded = rows.Count,
                Messages = messages
            };
        }
    }
}
