// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.b.7.ProformaInvoiceRepository.cs
// MODULE: step6 — the one write path, in the order it happens.
//
//   1. Authorise: the signed-in user, resolved here (step6.b.6).
//   2. Refuse a body change on a document that has left Draft. An issued
//      quotation is evidence; a revision is a NEW document with its own number.
//   3. Normalize and validate with the same rules the workspace uses.
//   4. Recalculate, so the stored columns and the stored body agree.
//   5. Allocate the number on the FIRST save only, from the highest existing
//      sequence for the year (never a count), then write inside a transaction.
//   6. Link the source inquiry AFTER the commit, through the Inquiry module's
//      own method. A link failure is reported as a warning, never as a lost
//      invoice, and never by writing another module's table from here.
//
// A concurrent writer can lose the race for a number; the unique index refuses
// the duplicate and the loop allocates the next one. Five attempts, then the
// failure is stated instead of being swallowed.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Diagnostics;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Inquiry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>EF Core implementation of the invoice repository.</summary>
    public sealed partial class ProformaInvoiceRepository : IProformaInvoiceRepository
    {
        private readonly Func<ProformaInvoiceDbContext> _createContext;
        private bool _initialized;

        public ProformaInvoiceRepository() : this(null) { }

        /// <summary>
        /// Test seam, as step2–step5 use: production callers use the
        /// parameterless constructor; the verification executable points the same
        /// code at an isolated file through AppDataRoot.TestFolderOverride.
        /// </summary>
        internal ProformaInvoiceRepository(Func<ProformaInvoiceDbContext>? contextFactory)
        {
            _createContext = contextFactory ?? (() => new ProformaInvoiceDbContext());
        }

        private ProformaInvoiceDbContext CreateContext() => _createContext();

        // ── Initialization ───────────────────────────────────────────────

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (_initialized) return;

            // The ERP file and its one version chain are owned by the Inquiry
            // initializer; 1018 (this module) is on that chain.
            await new InquiryRepository().InitializeAsync(ct).ConfigureAwait(false);

            if (!await IsSchemaReadyAsync(ct).ConfigureAwait(false))
                throw new InvalidOperationException(
                    "The proforma invoice tables (migration " +
                    ProformaInvoiceSchema.MigrationVersion +
                    ") are missing after initialization. Schema upgrade, if any, reported: " +
                    (string.IsNullOrWhiteSpace(SchemaMigrationRunner.FailureSummary)
                        ? "no failure recorded."
                        : SchemaMigrationRunner.FailureSummary));

            _initialized = true;
        }

        public async Task<bool> IsSchemaReadyAsync(CancellationToken ct = default)
        {
            try
            {
                await using var db = CreateContext();
                await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
                var connection = (SqliteConnection)db.Database.GetDbConnection();

                foreach (string table in new[]
                         { ProformaInvoiceSchema.TableName, ProformaInvoiceSchema.AttachmentTableName })
                {
                    using (var probe = connection.CreateCommand())
                    {
                        probe.CommandText =
                            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name;";
                        probe.Parameters.AddWithValue("@name", table);
                        long found = Convert.ToInt64(probe.ExecuteScalar() ?? 0L, CultureInfo.InvariantCulture);
                        if (found == 0) return false;
                    }
                }

                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA table_info(\"" + ProformaInvoiceSchema.TableName + "\");";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                        if (!reader.IsDBNull(1)) columns.Add(reader.GetString(1));
                }

                return ProformaInvoiceSchema.RequiredColumns.All(columns.Contains);
            }
            catch
            {
                // A readiness probe never throws: an unreadable file is "not ready".
                return false;
            }
        }

        // ── Save ─────────────────────────────────────────────────────────

        public async Task<ProformaInvoiceSaveResult> SaveAsync(
            ProformaInvoiceDraft draft, int expectedRowVersion, CancellationToken ct = default)
        {
            if (draft == null) return ProformaInvoiceSaveResult.Fail("No invoice was supplied.");

            InvoiceActor? actor = InvoiceAccess.Current(out string refusal);
            if (actor == null) return ProformaInvoiceSaveResult.Fail(refusal);

            await InitializeAsync(ct).ConfigureAwait(false);

            ProformaInvoiceRules.Normalize(draft);
            draft.Recalculate();
            InvoiceValidationResult validation = ProformaInvoiceRules.Validate(draft);
            if (!validation.IsValid)
                return ProformaInvoiceSaveResult.Fail(
                    string.Join(" ", validation.Errors), validation.Warnings);

            bool isNew = draft.Id <= 0;
            bool numberAllocated = false;

            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    await using var db = CreateContext();
                    await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

                    ProformaInvoiceRecord? record = null;
                    if (!isNew)
                    {
                        record = await db.Invoices
                            .FirstOrDefaultAsync(row => row.Id == draft.Id, ct)
                            .ConfigureAwait(false);
                        if (record == null)
                            return ProformaInvoiceSaveResult.Fail(
                                "This invoice no longer exists. Reload the register.");

                        if (record.RowVersion != expectedRowVersion)
                            return ProformaInvoiceSaveResult.Fail(
                                "This invoice was changed by someone else after you opened it. "
                                + "Reload it before saving, so neither version is lost.");

                        if (!InvoiceLifecycle.CanEditBody(record.Status))
                            return ProformaInvoiceSaveResult.Fail(
                                "A " + record.Status + " invoice cannot be edited. Its number is a record "
                                + "of what the customer was told; create a revision instead.");

                        draft.Status = ProformaInvoiceStatus.Normalize(record.Status) is { Length: > 0 } stored
                            ? stored
                            : ProformaInvoiceStatus.Draft;
                        draft.ProformaNumber = record.ProformaNumber;
                        draft.CreatedAtUtc = record.CreatedAtUtc;
                        draft.CreatedByUserId = record.CreatedByUserId;
                        draft.CreatedByName = record.CreatedByName;
                    }

                    // Allocate the number from the same connection that will
                    // write it, so the read and the write see one database.
                    if (string.IsNullOrWhiteSpace(draft.ProformaNumber))
                    {
                        draft.ProformaNumber = ProformaInvoiceSchema.NextNumber(
                            (SqliteConnection)db.Database.GetDbConnection(), DateTime.Today.Year);
                        numberAllocated = true;
                    }

                    DateTime now = DateTime.UtcNow;
                    if (record == null)
                    {
                        record = new ProformaInvoiceRecord
                        {
                            CreatedAtUtc = now,
                            CreatedByUserId = actor.UserId,
                            CreatedByName = actor.Name,
                            RowVersion = 0
                        };
                        db.Invoices.Add(record);
                    }

                    FillRecord(record, draft, actor, now);

                    await using var transaction = await db.Database
                        .BeginTransactionAsync(ct).ConfigureAwait(false);
                    await SqliteResilience.SaveWithRetryAsync(
                        token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);

                    draft.Id = record.Id;
                    draft.RowVersion = record.RowVersion;
                    draft.UpdatedAtUtc = record.UpdatedAtUtc;
                    draft.UpdatedByUserId = record.UpdatedByUserId;
                    draft.UpdatedByName = record.UpdatedByName;
                    draft.CreatedAtUtc = record.CreatedAtUtc;
                    draft.CreatedByUserId = record.CreatedByUserId;
                    draft.CreatedByName = record.CreatedByName;

                    var warnings = new List<string>(validation.Warnings);
                    if (draft.Parties.Inquiry.IsLinked)
                        await LinkInquiryAsync(draft, warnings, ct).ConfigureAwait(false);

                    return ProformaInvoiceSaveResult.Ok(draft, warnings, numberAllocated);
                }
                catch (DbUpdateException error) when (
                    SqliteErrorClassifier.IsUnique(error) && attempt < 4)
                {
                    // A race between two writers for the same number: take the
                    // next free one and try again.
                    draft.ProformaNumber = "";
                    numberAllocated = false;
                    _ = error;
                }
                catch (Exception error)
                {
                    return ProformaInvoiceSaveResult.Fail(
                        DescribeWriteFailure("The proforma invoice could not be saved", error),
                        validation.Warnings);
                }
            }

            return ProformaInvoiceSaveResult.Fail(
                "Could not allocate a free invoice number after five attempts. "
                + "Another window is saving invoices at the same time; try again.");
        }

        /// <summary>Copies the validated draft onto the row and its projections.</summary>
        private static void FillRecord(
            ProformaInvoiceRecord record, ProformaInvoiceDraft draft,
            InvoiceActor actor, DateTime now)
        {
            record.ProformaNumber = draft.ProformaNumber;
            record.Status = ProformaInvoiceStatus.Normalize(draft.Status);
            record.IssueDate = draft.IssueDate.Date;
            record.ValidUntil = draft.ValidUntil.Date;
            record.SnapshotJson = ProformaInvoiceJson.Write(draft);
            record.Notes = draft.Notes;

            record.ClientId = draft.Parties.Client.ClientId > 0
                ? draft.Parties.Client.ClientId : null;
            record.ClientName = draft.Parties.Client.ClientName;
            record.ProjectId = draft.Parties.Project.ProjectId > 0
                ? draft.Parties.Project.ProjectId : null;
            record.ProjectName = draft.Parties.Project.ProjectName;
            record.InquiryId = draft.Parties.Inquiry.InquiryId;
            record.InquiryNumber = draft.Parties.Inquiry.InquiryNumber;
            record.PreparedByUserId = draft.Parties.PreparedBy.UserId;
            record.PreparedByName = draft.Parties.PreparedBy.FullName;
            record.SalesRepresentativeUserId = draft.Parties.Client.SalesRepresentativeUserId;
            record.SalesRepresentativeName = draft.Parties.Client.SalesRepresentativeName;

            record.NetSubtotal = draft.Summary.NetSubtotal;
            record.OtherChargesSubtotal = draft.Summary.OtherChargesSubtotal;
            record.DiscountAmount = draft.Summary.DiscountAmount;
            record.VatAmount = draft.Summary.VatAmount;
            record.GrandTotal = draft.Summary.GrandTotal;
            record.TotalCost = draft.ProfitLoss?.TotalCost ?? 0m;
            record.Profit = draft.ProfitLoss?.Profit ?? 0m;

            record.UpdatedAtUtc = now;
            record.UpdatedByUserId = actor.UserId;
            record.UpdatedByName = actor.Name;
            record.RowVersion += 1;
        }

        /// <summary>
        /// Writes the PI reference onto the source inquiry through the Inquiry
        /// module's own method. Never writes the inquiry table from here, and a
        /// failure is a warning on a saved invoice — not a reason to lose it.
        /// </summary>
        private static async Task LinkInquiryAsync(
            ProformaInvoiceDraft draft, List<string> warnings, CancellationToken ct)
        {
            if (draft.Parties.Inquiry.InquiryId is not int inquiryId || inquiryId <= 0) return;
            try
            {
                bool linked = await new InquiryRepository()
                    .SetProformaLinkAsync(inquiryId, draft.Id, draft.ProformaNumber, ct)
                    .ConfigureAwait(false);
                if (!linked)
                    warnings.Add("The invoice was saved, but the source inquiry could not be marked "
                        + "as quoted. Open the inquiry and link it again.");
            }
            catch (Exception error)
            {
                AppLog.Error("step6.invoice.inquiry-link", error, "invoice " + draft.ProformaNumber);
                warnings.Add("The invoice was saved, but the source inquiry could not be updated: "
                    + error.GetBaseException().Message);
            }
        }

        /// <summary>Operator-facing wording for a persistence failure.</summary>
        internal static string DescribeWriteFailure(string prefix, Exception error)
        {
            string message = error.GetBaseException().Message;

            // A duplicate number is ordinary feedback (a concurrent save), not an
            // application fault, so it is not written to the error log.
            if (SqliteErrorClassifier.IsUnique(error))
                return "Another invoice already uses that number. Save again to take the next free one.";

            AppLog.Error("step6.invoice.write-failure", error, prefix);
            if (SqliteErrorClassifier.IsTransient(error))
                return prefix + ": the database is busy. Close other ProGlass windows and try again.";
            if (message.Contains("CHECK", StringComparison.OrdinalIgnoreCase))
                return prefix + ": the status is not one this database accepts.";
            return prefix + ". " + message;
        }
    }
}
