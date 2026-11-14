// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.b.9.ProformaInvoiceRepository.Lifecycle.cs
// MODULE: step6 — status changes and revisions.
//
// A status change goes through the transition table in step6.a.1 and nowhere
// else. There is no method that sets a status directly, so "approve a
// cancelled quotation" is not a rule somebody must remember — it is a call
// that returns a refusal.
//
// The stored BODY is updated with the new status in the same transaction as
// the column, so the register and the document cannot disagree about what the
// customer was told.
//
// A revision is a NEW document with its own number, a Draft body copied from
// the source, no attachment files (the originals belong to the source) and no
// inquiry link (one inquiry must not appear to have two live quotations).
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Database;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Proforma
{
    public sealed partial class ProformaInvoiceRepository
    {
        public async Task<ProformaInvoiceStatusResult> ChangeStatusAsync(
            int id, string targetStatus, int expectedRowVersion, CancellationToken ct = default)
        {
            if (id <= 0) return ProformaInvoiceStatusResult.Fail("No invoice was selected.");

            string target = ProformaInvoiceStatus.Normalize(targetStatus);
            if (!ProformaInvoiceStatus.IsDefined(target))
                return ProformaInvoiceStatusResult.Fail(
                    "\"" + targetStatus + "\" is not a known invoice status.");

            InvoiceActor? actor = InvoiceAccess.Current(out string refusal);
            if (actor == null) return ProformaInvoiceStatusResult.Fail(refusal);

            await InitializeAsync(ct).ConfigureAwait(false);

            try
            {
                await using var db = CreateContext();
                await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

                ProformaInvoiceRecord? record = await db.Invoices
                    .FirstOrDefaultAsync(row => row.Id == id, ct).ConfigureAwait(false);
                if (record == null)
                    return ProformaInvoiceStatusResult.Fail("This invoice no longer exists.");

                if (record.RowVersion != expectedRowVersion)
                    return ProformaInvoiceStatusResult.Fail(
                        "This invoice was changed by someone else. Reload it before changing its status.");

                string current = ProformaInvoiceStatus.Normalize(record.Status);
                if (!InvoiceLifecycle.CanTransition(current, target))
                    return ProformaInvoiceStatusResult.Fail(
                        "A " + current + " invoice cannot become " + target + ". " +
                        (InvoiceLifecycle.IsTerminal(current)
                            ? "A cancelled invoice is final; create a revision if the job is quoted again."
                            : "Choose one of: " + string.Join(", ",
                                System.Linq.Enumerable.Select(
                                    InvoiceLifecycle.From(current), transition => transition.To)) + "."));

                // Keep the body's status aligned with the column in one write.
                ProformaInvoiceJsonResult body = ProformaInvoiceJson.Read(record.SnapshotJson);
                if (!body.Succeeded || body.Draft == null)
                    return ProformaInvoiceStatusResult.Fail(
                        "The stored document body is unreadable, so its status cannot be changed safely. " +
                        body.Message);

                body.Draft.Status = target;
                record.SnapshotJson = ProformaInvoiceJson.Write(body.Draft);
                record.Status = target;
                record.UpdatedAtUtc = DateTime.UtcNow;
                record.UpdatedByUserId = actor.UserId;
                record.UpdatedByName = actor.Name;
                record.RowVersion += 1;

                await using var transaction = await db.Database
                    .BeginTransactionAsync(ct).ConfigureAwait(false);
                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);

                return ProformaInvoiceStatusResult.Ok(target);
            }
            catch (Exception error)
            {
                return ProformaInvoiceStatusResult.Fail(
                    DescribeWriteFailure("The invoice status could not be changed", error));
            }
        }

        public async Task<ProformaInvoiceSaveResult> DuplicateAsync(
            int sourceId, CancellationToken ct = default)
        {
            if (sourceId <= 0) return ProformaInvoiceSaveResult.Fail("No invoice was selected.");

            ProformaInvoiceDraft? source = await GetAsync(sourceId, ct).ConfigureAwait(false);
            if (source == null)
                return ProformaInvoiceSaveResult.Fail(
                    "The invoice could not be read, so it cannot be copied.");

            ProformaInvoiceDraft copy = source.Clone();
            copy.Id = 0;
            copy.ProformaNumber = "";
            copy.Status = ProformaInvoiceStatus.Draft;
            copy.RowVersion = 0;
            copy.IssueDate = DateTime.Today;
            copy.ValidUntil = DateTime.Today.AddDays(7);
            copy.CreatedAtUtc = default;
            copy.UpdatedAtUtc = default;
            copy.CreatedByUserId = null;
            copy.CreatedByName = "";
            copy.UpdatedByUserId = null;
            copy.UpdatedByName = "";
            copy.Attachments = new System.Collections.Generic.List<ProformaAttachment>();

            // One inquiry must not look quoted twice by two live documents.
            copy.Parties.Inquiry = new InquiryLink();
            copy.Parties.PartiesRefreshedAtUtc = DateTime.UtcNow;

            string note = "Copied from " + source.ProformaNumber + ".";
            copy.Notes = copy.Notes.Length == 0 ? note : copy.Notes + " " + note;

            return await SaveAsync(copy, 0, ct).ConfigureAwait(false);
        }
    }
}
