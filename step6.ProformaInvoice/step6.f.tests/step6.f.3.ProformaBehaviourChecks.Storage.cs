// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.f.tests/step6.f.3.ProformaBehaviourChecks.Storage.cs
// MODULE: step6 — behaviour of the stored document.
//
// NOT EXECUTED BY THE AUTHOR (no .NET SDK on the authoring machine). Run with
// the rest of the suite: cd step6.ProformaInvoice/step6.f.tests; dotnet run
//
// Every check runs against a disposable database in the system temp folder:
// AppDataRoot.TestFolderOverride points the repositories there, and the
// production file is never opened.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using System.IO;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Proforma;
using ProGlassAutomation.Services.Users;

namespace ProGlassAutomation.Step6Tests
{
    internal static partial class Program
    {
        // ── Stored body ─────────────────────────────────────────────────
        private static void StoredBody()
        {
            var draft = new ProformaInvoiceDraft();
            draft.EnsureStructure();
            draft.Parties.Client.ClientName = "Al Noor Glass LLC";
            draft.Notes = "Stored once";
            draft.AddPricingSection(GlassBuilds.Dgu);
            string json = ProformaInvoiceJson.Write(draft);
            Check("the body is stored with its format version",
                json.Contains("\"FormatVersion\":1", StringComparison.Ordinal), json[..Math.Min(120, json.Length)]);
            Check("computed totals are not stored",
                !json.Contains("\"GrandTotal\"", StringComparison.Ordinal)
                && !json.Contains("\"TotalSqm\"", StringComparison.Ordinal));
            Check("status is stored by name, not by number",
                json.Contains("\"Status\":\"Draft\"", StringComparison.Ordinal));

            ProformaInvoiceJsonResult reread = ProformaInvoiceJson.Read(json);
            Check("a stored body reads back", reread.Succeeded, reread.Message);
            Check("the client survives the round trip",
                reread.Draft!.Parties.Client.ClientName == "Al Noor Glass LLC");
            Check("the sections survive the round trip",
                reread.Draft.PricingSections.Count == 2);
            Check("an empty body is reported, not invented",
                !ProformaInvoiceJson.Read("").Succeeded);
            Check("a corrupt body is reported, not invented",
                !ProformaInvoiceJson.Read("{ not json").Succeeded);
            Check("a newer format is refused with an explanation",
                ProformaInvoiceJson.Read("{\"FormatVersion\":99}").Message.Contains(
                    "newer version", StringComparison.OrdinalIgnoreCase));
        }

        // ── Repository ──────────────────────────────────────────────────
        private static async Task RepositoryAsync()
        {
            AuthenticationContext.SignIn(Administrator());
            var repository = new ProformaInvoiceRepository();
            await repository.InitializeAsync();
            Check("migration 1018 created the invoice tables", await repository.IsSchemaReadyAsync());

            ProformaInvoiceDraft draft = SavableDraft();
            ProformaInvoiceSaveResult first = await repository.SaveAsync(draft, 0);
            Check("a complete draft saves", first.Succeeded, first.Message);
            Check("the number is allocated on first save",
                first.NumberWasAllocated && draft.ProformaNumber.StartsWith("PI-", StringComparison.Ordinal),
                draft.ProformaNumber);
            Check("the stored author is the signed-in user",
                draft.CreatedByUserId == 1 && draft.CreatedByName == "Juber Saleem",
                draft.CreatedByName);

            int id = draft.Id;
            int rowVersion = draft.RowVersion;
            string number = draft.ProformaNumber;

            ProformaInvoiceDraft? reloaded = await repository.GetAsync(id);
            Check("the document reloads", reloaded != null);
            Check("the number did not change on reload", reloaded!.ProformaNumber == number);
            Check("the grid survived the round trip", reloaded.Groups.Count == 1
                && reloaded.Groups[0].Rows[0].W1 == 1000);
            Check("the cost card survived the round trip",
                reloaded.Groups[0].CostCard.Panes.Count == 1);
            Check("a second save keeps the number",
                (await repository.SaveAsync(reloaded, rowVersion)).Succeeded
                && reloaded.ProformaNumber == number);

            var rows = await repository.QueryAsync(new ProformaInvoiceQuery());
            Check("the register lists the document", rows.Count == 1 && rows[0].Id == id);

            ProformaInvoiceStatusResult sent = await repository.ChangeStatusAsync(
                id, ProformaInvoiceStatus.Sent, reloaded.RowVersion);
            Check("a draft can be sent", sent.Succeeded, sent.Message);
            ProformaInvoiceDraft? afterSend = await repository.GetAsync(id);
            Check("an issued document refuses a body edit",
                !(await repository.SaveAsync(afterSend!, afterSend!.RowVersion)).Succeeded);
            Check("an issued document cannot go back to draft",
                !(await repository.ChangeStatusAsync(id, ProformaInvoiceStatus.Draft,
                    afterSend.RowVersion)).Succeeded);

            ProformaInvoiceSaveResult copy = await repository.DuplicateAsync(id);
            Check("a revision is a new draft with a new number",
                copy.Succeeded && copy.Invoice != null
                && copy.Invoice.Id != id
                && copy.Invoice.ProformaNumber != number
                && copy.Invoice.Status == ProformaInvoiceStatus.Draft,
                copy.Message);

            ProformaInvoiceStatusResult cancelled = await repository.ChangeStatusAsync(
                copy.Invoice!.Id, ProformaInvoiceStatus.Cancelled, copy.Invoice.RowVersion);
            Check("a draft can be cancelled", cancelled.Succeeded, cancelled.Message);
            Check("no status leaves cancelled",
                !(await repository.ChangeStatusAsync(copy.Invoice.Id,
                    ProformaInvoiceStatus.Sent, copy.Invoice.RowVersion + 1)).Succeeded);

            string next = await repository.SuggestNumberAsync();
            Check("the next number continues the sequence, no reuse",
                next != number && next != copy.Invoice.ProformaNumber,
                next + " vs " + number + " / " + copy.Invoice.ProformaNumber);

            Check("the document number is unique in the database", UniqueIndexHolds());

            Check("an anonymous session cannot save",
                await anonymousSaveRefused());
        }

        private static async Task<bool> anonymousSaveRefused()
        {
            AuthenticationContext.SignOut();
            var repository = new ProformaInvoiceRepository();
            ProformaInvoiceSaveResult result = await repository.SaveAsync(SavableDraft(), 0);
            return !result.Succeeded
                && result.Message.Contains("Sign in", StringComparison.OrdinalIgnoreCase);
        }

        private static bool UniqueIndexHolds()
        {
            try
            {
                using var connection = new SqliteConnection(
                    SqliteResilience.BuildConnectionString(ProformaInvoiceDbContext.DatabaseFilePath));
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO ProformaInvoices (ProformaNumber, Status, IssueDate, ValidUntil, " +
                    "ClientName, ProjectName, InquiryNumber, PreparedByName, SalesRepresentativeName, " +
                    "NetSubtotal, OtherChargesSubtotal, DiscountAmount, VatAmount, GrandTotal, " +
                    "TotalCost, Profit, SnapshotJson, Notes, RowVersion, CreatedAtUtc, UpdatedAtUtc) " +
                    "SELECT ProformaNumber, 'Draft', IssueDate, ValidUntil, ClientName, ProjectName, " +
                    "InquiryNumber, PreparedByName, SalesRepresentativeName, NetSubtotal, " +
                    "OtherChargesSubtotal, DiscountAmount, VatAmount, GrandTotal, TotalCost, Profit, " +
                    "SnapshotJson, Notes, 1, CreatedAtUtc, UpdatedAtUtc FROM ProformaInvoices LIMIT 1;";
                command.ExecuteNonQuery();
                return false;   // the duplicate was accepted: the unique index is gone
            }
            catch (SqliteException)
            {
                return true;
            }
        }

        // ── Attachments ─────────────────────────────────────────────────
        private static async Task AttachmentsAsync()
        {
            AuthenticationContext.SignIn(Administrator());
            var repository = new ProformaInvoiceRepository();
            await repository.InitializeAsync();
            ProformaInvoiceDraft draft = SavableDraft();
            Check("the invoice for attachments saved", (await repository.SaveAsync(draft, 0)).Succeeded);

            string source = Path.Combine(Path.GetTempPath(),
                "proglass-step6-" + Guid.NewGuid().ToString("N")[..8] + ".pdf");
            File.WriteAllText(source, "evidence");

            ProformaAttachmentAddResult added = await repository.AddAttachmentAsync(
                draft.Id, source, "Document", "signed quotation");
            Check("an attachment is added", added.Succeeded, added.Message);
            string? path = added.Attachment == null
                ? null : repository.ResolveAttachmentPath(draft.Id, added.Attachment.StoredFileName);
            Check("the stored file exists where the row says", path != null && File.Exists(path!));
            Check("the stored name is not the source name",
                added.Attachment!.StoredFileName != Path.GetFileName(source));
            Check("the original name is kept for display", added.Attachment.FileName == Path.GetFileName(source));
            Check("a missing file is refused",
                !(await repository.AddAttachmentAsync(draft.Id, source + ".missing", "Document", "")).Succeeded);
            Check("a path that escapes the folder is refused",
                repository.ResolveAttachmentPath(draft.Id, "..\\..\\inquiries.db") == null);

            Check("an attachment can be removed", await repository.RemoveAttachmentAsync(added.Attachment.Id));
            Check("the file is gone after the row commits", path == null || !File.Exists(path!));
            Check("no attachment rows remain",
                (await repository.GetAttachmentsAsync(draft.Id)).Count == 0);
        }

        private static async Task WithTempFolder(string name, Func<Task> check)
        {
            string folder = Path.Combine(
                Path.GetTempPath(), "proglass-step6-" + name + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(folder);
            AppDataRoot.TestFolderOverride = folder;
            try
            {
                await check();
            }
            finally
            {
                AuthenticationContext.SignOut();
                AppDataRoot.TestFolderOverride = null;
                SqliteConnection.ClearAllPools();
                try { Directory.Delete(folder, recursive: true); } catch { /* best effort */ }
            }
        }

    }
}
