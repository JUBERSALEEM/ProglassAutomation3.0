using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Models.ProformaInvoice.Common;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Licensing;         // ReadOnlyGate
using ProGlassAutomation.Services.ProformaInvoice;   // InquiryLinkSync
using ProGlassAutomation.Services.ProformaInvoice.Attachments;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Common;

namespace ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository
{
    /// <summary>
    /// EF Core implementation of IProformaInvoiceRepository.
    ///
    /// BUG-07: Return types nullable (matches interface contract).
    /// BUG-08: SuggestNextIdAsync batch-loads all matching IDs (no N+1).
    /// BUG-09: Fallback ID uses Guid.NewGuid() — no timestamp collision.
    /// BUG-10: UpdateAsync no longer opens second context in insert path.
    /// BUG-11: Regex tightened — non-greedy prefix, explicit numeric group.
    /// BUG-12: GetAllAsync accepts CancellationToken.
    /// </summary>
    public class ProformaInvoiceRepository : IProformaInvoiceRepository
    {
        // ══════════════════════════════════════════════════════════════
        //  SAVED-INVOICE CHANGE SIGNAL
        //
        //  Job Profitability reads saved invoices. It reloaded only when the
        //  module was entered, so saving an invoice while that screen was
        //  already open left it showing figures for a job that had since
        //  changed, with no indication anything was stale.
        //
        //  The signal is raised HERE rather than at each call site because
        //  invoices are written from several places (the wizard's print
        //  preview, the saved-invoices list, draft auto-save). Hooking the
        //  repository means a future write path is covered without anyone
        //  remembering to add a notification.
        //
        //  Static and parameterless on purpose: listeners re-query rather
        //  than trusting a payload, so a missed or duplicated event costs a
        //  redundant read, never a wrong number on screen.
        // ══════════════════════════════════════════════════════════════
        public static event Action? SavedInvoicesChanged;

        private static void RaiseChanged()
        {
            // A listener that throws must not roll back a completed save.
            try { SavedInvoicesChanged?.Invoke(); } catch (Exception ex) { AppLogService.Warning($"Suppressed exception: {ex.Message}"); }
            DocumentLifecycle.Raise();
        }

        // BUG-11: named groups, non-greedy prefix, anchored at end
        private static readonly Regex _idPattern = new(
            @"^(?<prefix>.*?)(?<num>\d+)$",
            RegexOptions.Compiled);

        public async Task InitializeAsync()
        {
            using var db = new ProformaInvoicesDbContext();
            await db.Database.EnsureCreatedAsync();

            // Additive schema upgrades for an existing proformainvoices.db.
            SchemaMigrationRunner.Upgrade(
                ProformaInvoicesDbContext.DatabaseFilePath,
                AppSchemaMigrations.ProformaInvoices,
                "Proforma Invoice");
        }

        public async Task<bool> ExistsByProformaIdAsync(string proformaId)
        {
            if (string.IsNullOrWhiteSpace(proformaId)) return false;

            using var db = new ProformaInvoicesDbContext();
            return await db.ProformaInvoices
                .AnyAsync(x => x.ProformaId == proformaId).ConfigureAwait(false);
        }

        /// <summary>Revision chain of one PI: the original (ProformaId ==
        /// root, usually RevisionNumber 0) plus every "-R{n}" revision.
        /// Legacy originals have a blank RootProformaId — the root id itself
        /// is matched so they still resolve. 2026-09-11.</summary>
        public async Task<List<SavedProformaInvoice>> GetRevisionChainAsync(
            string rootProformaId)
        {
            var root = (rootProformaId ?? "").Trim();
            if (root.Length == 0) return new List<SavedProformaInvoice>();

            using var db = new ProformaInvoicesDbContext();
            return await db.ProformaInvoices
                .Where(x => x.RootProformaId == root || x.ProformaId == root)
                .OrderBy(x => x.RevisionNumber).ThenBy(x => x.Id)
                .ToListAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Suggests the next available ProformaId by auto-incrementing the trailing number.
        ///
        /// BUG-08: Was doing up to 10,000 DB queries. Now does ONE query
        /// to load all IDs with matching prefix, then finds next unused
        /// in memory. Massive perf improvement.
        ///
        /// BUG-09: Fallback uses Guid.NewGuid() instead of timestamp —
        /// no risk of two invoices created same second colliding.
        ///
        /// BUG-11: Regex tightened using named groups + non-greedy prefix.
        ///
        /// Examples:
        ///   "PI-2026-001" → "PI-2026-002" (or 003, 004... if those also exist)
        ///   "INVOICE-42"  → "INVOICE-43"
        ///   "ABC"         → "ABC-001"
        /// </summary>
        public async Task<string> SuggestNextIdAsync(string currentId)
        {
            if (string.IsNullOrWhiteSpace(currentId))
                currentId = "PI-000";

            var match = _idPattern.Match(currentId);

            string prefix;
            int currentNumber;
            int padWidth;

            if (match.Success)
            {
                prefix = match.Groups["prefix"].Value;
                var numText = match.Groups["num"].Value;

                // AUDIT V-1 (Phase 13 — input validation).
                // _idPattern uses \d+ which is UNBOUNDED, so a document
                // id such as "PI-99999999999" (11 digits, easily typed or
                // pasted, and legal in the ProformaId column) produced an
                // OverflowException here. int.Parse throws, SuggestNextIdAsync
                // has no try/catch, and the caller is an async command whose
                // exception was swallowed by RelayCommand — so the user saw
                // "nothing happens" and could not create an invoice at all.
                //
                // TryParse degrades instead: an out-of-range or malformed
                // run of digits restarts numbering from that prefix rather
                // than crashing. The duplicate-avoidance loop below then
                // finds the next free id, so correctness is preserved.
                if (!int.TryParse(numText, out currentNumber))
                    currentNumber = 0;

                padWidth = numText.Length;
            }
            else
            {
                prefix = currentId + "-";
                currentNumber = 0;
                padWidth = 3;
            }

            using var db = new ProformaInvoicesDbContext();

            // BUG-08: single query — load all IDs starting with prefix
            var existingIds = await db.ProformaInvoices
                .Where(x => x.ProformaId.StartsWith(prefix))
                .Select(x => x.ProformaId)
                .ToListAsync().ConfigureAwait(false);

            var existingSet = new HashSet<string>(
                existingIds, StringComparer.OrdinalIgnoreCase);

            // Find next unused number by simple in-memory loop
            for (int i = 1; i <= 100_000; i++)
            {
                var candidate =
                    $"{prefix}{(currentNumber + i).ToString().PadLeft(padWidth, '0')}";
                if (!existingSet.Contains(candidate))
                    return candidate;
            }

            // BUG-09: GUID fallback — never collides
            return $"{prefix}{Guid.NewGuid():N}".Substring(0, Math.Min(50,
                prefix.Length + 32));
        }

        public async Task<int> InsertAsync(SavedProformaInvoice invoice)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.ProformaCreate, "create a proforma invoice");

            // Licensing (Aug 2026 refactor): read-only mode blocks new
            // writes so an expired trial / grace period cannot silently
            // accumulate more data. The banner in the shell tells the
            // operator why, and this throw surfaces the same message
            // to the ViewModel dialog.
            ReadOnlyGate.Demand("create a proforma invoice");

            if (invoice == null)
                throw new ArgumentNullException(nameof(invoice));

            bool idReallocated = false;
            if (ProformaInvoiceStatus.IsUnallocatedSeed(invoice.ProformaId))
            {
                invoice.ProformaId = await SuggestNextIdAsync(
                    $"PI-{DateTime.Now.Year}-000").ConfigureAwait(false);
                idReallocated = true;
            }

            // Senior audit fix (Aug 2026): concurrent SuggestNextIdAsync
            // callers could both see "PI-2026-042" as free, both call
            // InsertAsync, and the second one would surface a raw
            // DbUpdateException (unique-index violation on ProformaId)
            // that RelayCommand swallowed silently. We now retry with
            // the next free id — capped at 5 attempts so a truly stuck
            // system still surfaces an error instead of looping.
            for (int attempt = 0; attempt < 5; attempt++)
            {
                await using var db = new ProformaInvoicesDbContext();

                bool exists = await db.ProformaInvoices
                    .AnyAsync(x => x.ProformaId == invoice.ProformaId)
                    .ConfigureAwait(false);
                if (exists)
                {
                    // Bump to the next free id and retry.
                    invoice.ProformaId = await SuggestNextIdAsync(
                        invoice.ProformaId).ConfigureAwait(false);
                    idReallocated = true;
                    continue;
                }

                invoice.CreatedAt = DateTime.Now;
                invoice.UpdatedAt = DateTime.Now;

                // Seed / unique-retry may have changed the column id after
                // the wizard snapshot was serialized. Keep JSON identity
                // aligned so restore / JO / attachments see one ProformaId.
                if (idReallocated)
                    SyncSnapshotProformaId(invoice);

                db.ProformaInvoices.Add(invoice);
                try
                {
                    await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync()).ConfigureAwait(false);
                }
                catch (DbUpdateException ex)
                    when (IsUniqueViolation(ex) && attempt < 4)
                {
                    // Race with another writer between the AnyAsync check
                    // and SaveChanges. Detach, bump id, try again.
                    db.Entry(invoice).State = EntityState.Detached;
                    invoice.ProformaId = await SuggestNextIdAsync(
                        invoice.ProformaId).ConfigureAwait(false);
                    invoice.Id = 0;
                    idReallocated = true;
                    continue;
                }

                RaiseChanged();

                // Senior audit fix (Aug 2026) — write the PI back-link
                // onto the source Inquiry so the InquiryList and the
                // ConvertToPi guard see the freshly-created PI. No-op
                // when the PI has no InquiryId. Fire-and-forget safe:
                // failures are logged and never fail the insert.
                await InquiryLinkSync.AfterInsertAsync(invoice)
                    .ConfigureAwait(false);

                return invoice.Id;
            }

            throw new InvalidOperationException(
                $"Could not allocate a free ProformaId after 5 retries " +
                $"starting from '{invoice.ProformaId}'. Another concurrent " +
                $"writer is racing this insert.");
        }

        /// <summary>
        /// Detect a UNIQUE / PRIMARY-KEY violation on the SqliteException
        /// wrapped inside a DbUpdateException. Delegates to the shared
        /// <see cref="SqliteErrorClassifier.IsUnique"/> which uses the
        /// SqliteExtendedErrorCode (2067 = UNIQUE, 1555 = PRIMARYKEY)
        /// instead of matching the error-message string — that string
        /// changed between Microsoft.Data.Sqlite 6.x and 8.x and would
        /// silently miss a real collision on a future upgrade.
        /// </summary>
        private static bool IsUniqueViolation(DbUpdateException ex) =>
            ProGlassAutomation.Services.Database.SqliteErrorClassifier.IsUnique(ex);

        /// <summary>
        /// When InsertAsync reallocates ProformaId, rewrite Company.ProformaId
        /// inside FullSnapshotJson so load/restore matches the column.
        /// Never throws — a snapshot miss must not fail the insert.
        /// </summary>
        private static void SyncSnapshotProformaId(SavedProformaInvoice invoice)
        {
            if (invoice == null || string.IsNullOrWhiteSpace(invoice.ProformaId))
                return;
            if (string.IsNullOrWhiteSpace(invoice.FullSnapshotJson))
                return;
            try
            {
                var snap = ProformaInvoiceMapper.DeserializeSnapshot(invoice);
                if (snap?.Company == null) return;
                if (string.Equals(snap.Company.ProformaId, invoice.ProformaId,
                        StringComparison.Ordinal))
                    return;
                snap.Company.ProformaId = invoice.ProformaId;
                invoice.FullSnapshotJson = ProformaInvoiceMapper.SerializeSnapshot(snap);
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    "Could not sync snapshot ProformaId after reallocation: " +
                    ex.Message);
            }
        }

        /// <summary>
        /// Updates an existing invoice or creates a new one if missing.
        ///
        /// BUG-10: do not insert on the same open SQLite context as the
        /// lookup. Missing-row path disposes then calls InsertAsync so
        /// unique-id retry is shared with create.
        /// </summary>
        public async Task UpdateAsync(SavedProformaInvoice invoice)
            => await UpdateAsync(invoice, expectedRowVersion: null).ConfigureAwait(false);

        /// <summary>
        /// Updates a proforma invoice (or inserts if missing). When
        /// <paramref name="expectedRowVersion"/> is supplied and the stored
        /// RowVersion has moved on, a
        /// <see cref="ProGlassAutomation.Services.Database.ConcurrencyConflictException"/>
        /// is thrown so a concurrent save isn't silently overwritten. Null keeps
        /// legacy behaviour; the counter still increments on update.
        /// </summary>
        public async Task UpdateAsync(SavedProformaInvoice invoice, int? expectedRowVersion)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.ProformaEdit, "update a proforma invoice");

            ReadOnlyGate.Demand("update a proforma invoice");

            if (invoice == null)
                throw new ArgumentNullException(nameof(invoice));

            using var db = new ProformaInvoicesDbContext();

            var existing = await db.ProformaInvoices
                .FirstOrDefaultAsync(x => x.ProformaId == invoice.ProformaId).ConfigureAwait(false);

            int? previousInquiryId = existing?.InquiryId;

            // ERP hardening (Phase 5): optimistic-concurrency check.
            if (expectedRowVersion.HasValue
                && existing != null
                && existing.RowVersion != expectedRowVersion.Value)
            {
                throw new ProGlassAutomation.Services.Database.ConcurrencyConflictException(
                    "Proforma Invoice", invoice.ProformaId,
                    expectedRowVersion.Value, existing.RowVersion);
            }

            if (existing == null)
            {
                // Missing-row update is a create: reuse InsertAsync so
                // unique-id retry applies. Dispose this read context first
                // (BUG-10: two live SQLite writers lock).
                db.Dispose();
                await InsertAsync(invoice).ConfigureAwait(false);
                return;
            }

            // Copy fields onto tracked entity
            existing.InquiryId = invoice.InquiryId;
            existing.CompanyName = invoice.CompanyName;
            existing.CustomerName = invoice.CustomerName;
            existing.ProjectName = invoice.ProjectName;
            // PHASE 5: statuses enter the store through the single
            // normalization home (case/space variants canonicalize; an
            // unknown value is passed through as-typed, never re-mapped).
            existing.Status = ProformaInvoiceStatus.Normalize(invoice.Status);
            existing.IssueDate = invoice.IssueDate;
            existing.ValidUntil = invoice.ValidUntil;
            existing.GlassMaterialsSubtotal = invoice.GlassMaterialsSubtotal;
            existing.OtherChargesSubtotal = invoice.OtherChargesSubtotal;
            existing.DiscountAmount = invoice.DiscountAmount;
            existing.VatAmount = invoice.VatAmount;
            existing.GrandTotalVatInclusive = invoice.GrandTotalVatInclusive;
            existing.FullSnapshotJson = invoice.FullSnapshotJson;
            existing.RowVersion = existing.RowVersion + 1;   // Phase 5: concurrency counter
            existing.UpdatedAt = DateTime.Now;

            await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());
            RaiseChanged();

            // Senior audit fix — the update may have re-pointed the PI at
            // a different inquiry (rare: only via a snapshot-JSON edit
            // that changed Company.InquiryId). In that case the OLD
            // inquiry's link must be cleared and the NEW one written.
            if (previousInquiryId.HasValue &&
                previousInquiryId != existing.InquiryId)
            {
                try
                {
                    if (App.Inquiries != null)
                    {
                        await App.Inquiries.SetProformaLinkAsync(
                            previousInquiryId.Value,
                            proformaInvoiceId: null,
                            proformaNumber: string.Empty)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    AppLogService.Warning(
                        "Could not clear previous inquiry link on PI update: " + ex.Message,
                        new LogContext
                        {
                            Module = "Proforma",
                            Class = nameof(ProformaInvoiceRepository),
                            Operation = "UpdateAsync/PreviousInquiryClear"
                        });
                }
            }

            await InquiryLinkSync.AfterUpdateAsync(existing)
                .ConfigureAwait(false);
        }

        public async Task<SavedProformaInvoice?> GetByIdAsync(int id)
        {
            using var db = new ProformaInvoicesDbContext();
            return await db.ProformaInvoices.FindAsync(id).ConfigureAwait(false);
        }

        public async Task<SavedProformaInvoice?> GetByProformaIdAsync(
            string proformaId)
        {
            if (string.IsNullOrWhiteSpace(proformaId)) return null;

            using var db = new ProformaInvoicesDbContext();
            return await db.ProformaInvoices
                .FirstOrDefaultAsync(x => x.ProformaId == proformaId).ConfigureAwait(false);
        }

        public async Task<List<SavedProformaInvoice>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            using var db = new ProformaInvoicesDbContext();
            return await db.ProformaInvoices
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<SavedProformaInvoice>> GetByStatusAsync(
            string status,
            CancellationToken cancellationToken = default)
        {
            using var db = new ProformaInvoicesDbContext();
            return await db.ProformaInvoices
                .AsNoTracking()
                .Where(x => x.Status == status)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// PERF-9: list-page projection — see the interface contract.
        /// Never touches FullSnapshotJson; the Saved Invoices list only
        /// needs indexed scalars, and this keeps opening that page at a
        /// constant cost per row instead of deserializing whole snapshots.
        /// </summary>
        public async Task<List<ProformaListRow>> GetListRowsAsync(
            CancellationToken cancellationToken = default)
        {
            using var db = new ProformaInvoicesDbContext();
            return await db.ProformaInvoices
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new ProformaListRow
                {
                    Id = x.Id,
                    ProformaId = x.ProformaId ?? "",
                    CompanyName = x.CompanyName ?? "",
                    CustomerName = x.CustomerName ?? "",
                    ProjectName = x.ProjectName ?? "",
                    Status = x.Status ?? "Draft",
                    GrandTotalVatInclusive = x.GrandTotalVatInclusive,
                    IssueDate = x.IssueDate,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt,
                    IsVoid = x.IsVoid
                })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// PERF-2: see IProformaInvoiceRepository for why this exists
        /// alongside GetAllAsync rather than replacing it.
        /// </summary>
        public async Task<List<ProformaAnalyticsRow>> GetAnalyticsRowsAsync(
            CancellationToken cancellationToken = default)
        {
            using var db = new ProformaInvoicesDbContext();

            // AsNoTracking: these rows are read-only analytics input, so
            // there is no reason to build change-tracking entries for them.
            // The Select is what keeps FullSnapshotJson off the wire.
            return await db.ProformaInvoices
                .AsNoTracking()
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new ProformaAnalyticsRow
                {
                    Status                 = x.Status,
                    CustomerName           = x.CustomerName,
                    GrandTotalVatInclusive = x.GrandTotalVatInclusive,
                    CreatedAt              = x.CreatedAt,
                    IssueDate              = x.IssueDate
                })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<ProformaSearchRow>> SearchAsync(
            string term, int take)
        {
            using var db = new ProformaInvoicesDbContext();

            string q = term?.Trim() ?? "";
            if (string.IsNullOrEmpty(q) || take <= 0)
                return new List<ProformaSearchRow>();

            // Escape LIKE wildcards so DB-side search matches the previous
            // in-memory IndexOf(OrdinalIgnoreCase) semantics for literal terms.
            string like = q.Replace("%", "[%]").Replace("_", "[_]");
            string pattern = $"%{like}%";

            return await db.ProformaInvoices
                .AsNoTracking()
                .Where(p =>
                    EF.Functions.Like(p.ProformaId ?? "", pattern) ||
                    EF.Functions.Like(p.CustomerName ?? "", pattern) ||
                    EF.Functions.Like(p.ProjectName ?? "", pattern) ||
                    EF.Functions.Like(p.CompanyName ?? "", pattern))
                .OrderByDescending(p => p.CreatedAt)
                .Take(take)
                .Select(p => new ProformaSearchRow
                {
                    ProformaId = p.ProformaId,
                    CustomerName = p.CustomerName,
                    ProjectName = p.ProjectName,
                    CompanyName = p.CompanyName,
                    Status = p.Status
                })
                .ToListAsync().ConfigureAwait(false);
        }

        public async Task DeleteAsync(int id)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.ProformaDelete, "delete a proforma invoice");

            ReadOnlyGate.Demand("delete a proforma invoice");

            using var db = new ProformaInvoicesDbContext();
            var entity = await db.ProformaInvoices.FindAsync(id).ConfigureAwait(false);
            if (entity == null) return;

            // Capture identifiers BEFORE removing so we can synchronise
            // the Inquiry back-link and attachments after the row is gone.
            string? proformaId = entity.ProformaId;
            var deletedSnapshot = new SavedProformaInvoice
            {
                Id = entity.Id,
                ProformaId = entity.ProformaId,
                InquiryId = entity.InquiryId
            };

            db.ProformaInvoices.Remove(entity);
            await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());
            RaiseChanged();

            // Senior audit fix — clear the PI back-link on the source
            // inquiry so ConvertToPi becomes available again and the
            // list projection stops showing a stale PI number.
            await InquiryLinkSync.AfterDeleteAsync(deletedSnapshot)
                .ConfigureAwait(false);

            // Remove the invoice's Step 8 attachments (files + manifest)
            // so no orphaned files remain and a re-created invoice with
            // the same Proforma ID cannot resurrect stale attachments.
            await ClearAttachmentsQuietlyAsync(proformaId);
        }

        /// <summary>
        /// ERP lifecycle hardening (Phase 1): soft-close (void) instead of a
        /// physical delete. The row — and therefore the Proforma number,
        /// snapshot and audit trail — is kept; Status moves to the void
        /// target and the void audit columns are stamped. Attachments and
        /// the Inquiry back-link are intentionally NOT torn down (unlike
        /// DeleteAsync), because the voided document still exists.
        /// </summary>
        public async Task VoidAsync(int id, string reason)
        {
            OperationGuard.Demand(
                AppPermissions.ProformaDelete, "void a proforma invoice");
            ReadOnlyGate.Demand("void a proforma invoice");

            using var db = new ProformaInvoicesDbContext();
            var entity = await db.ProformaInvoices.FindAsync(id).ConfigureAwait(false);
            if (entity == null) return;
            if (entity.IsVoid) return;   // idempotent — already voided

            entity.IsVoid = true;
            entity.VoidedAtUtc = DateTime.UtcNow;
            entity.VoidedBy = AuditService.CurrentUsername;
            entity.VoidReason = string.IsNullOrWhiteSpace(reason) ? "(no reason given)" : reason.Trim();
            entity.Status = Services.Workflow.DocumentLifecyclePolicy.VoidTargetStatus(
                Services.Workflow.StatusTransitions.Kind.ProformaInvoice);
            entity.UpdatedAt = DateTime.Now;

            await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());
            RaiseChanged();
        }

        /// <summary>
        /// Internal lifecycle write used by ProformaLifecycleSync when a
        /// Job Order / Delivery Note / Tax Invoice is removed and nothing
        /// downstream remains. Deliberately does NOT demand ProformaEdit:
        /// the authorization boundary is the originating downstream delete,
        /// which the invoking repository already enforced (see the
        /// interface contract). Without this, the Production role could
        /// delete a Job Order but the resulting PI cancellation would
        /// throw PermissionDeniedException inside the sync and never be
        /// persisted — leaving the PI stuck in its old status.
        /// </summary>
        public async Task<bool> MarkCancelledAsync(string proformaId)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.ProformaDelete, "cancel a proforma invoice");

            if (string.IsNullOrWhiteSpace(proformaId)) return false;

            using var db = new ProformaInvoicesDbContext();
            var entity = await db.ProformaInvoices
                .FirstOrDefaultAsync(x => x.ProformaId == proformaId).ConfigureAwait(false);
            if (entity == null) return false;

            // Lifecycle-forced write (downstream delete cascade, see
            // ProformaLifecycleSync): this is a dedicated cancellation write
            // with its own authorization semantics, so it deliberately
            // bypasses the user-transition table (the PI is being cancelled
            // because a dependent document was deleted, not by user action).
            entity.Status = ProformaInvoiceStatus.Rejected;
            entity.UpdatedAt = DateTime.Now;

            // Keep the snapshot's status in sync so Step 1 / print preview
            // reflect the cancellation too.
            var snap = ProformaInvoiceMapper.DeserializeSnapshot(entity);
            if (snap.Company != null)
                snap.Company.ProformaStatus = "Rejected";
            entity.FullSnapshotJson = ProformaInvoiceMapper.SerializeSnapshot(snap);

            await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());
            RaiseChanged();
            return true;
        }

        /// <summary>
        /// Best-effort cleanup of a deleted PI's Step 8 attachments.
        /// The PI row is already gone — a failure here is logged, never
        /// thrown back to the caller.
        /// </summary>
        private static async Task ClearAttachmentsQuietlyAsync(string? proformaId)
        {
            if (string.IsNullOrWhiteSpace(proformaId)) return;

            try
            {
                await new ProformaAttachmentService().ClearAllAsync(proformaId);
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    $"Could not clear attachments for deleted PI " +
                    $"{proformaId}: {ex.Message}",
                    new LogContext
                    {
                        Module = "Proforma",
                        Class = nameof(ProformaInvoiceRepository),
                        Operation = "Delete"
                    });
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PHASE 2 — Result Pattern with CorrelationId
        //  New explicit-error APIs for billing pipeline
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Phase 2: Insert with explicit Result and CorrelationId.
        /// Returns Result with ErrorId for UI dialog and CorrelationId for log tracing.
        /// </summary>
        public async Task<Result<int>> InsertWithResultAsync(SavedProformaInvoice invoice, string? correlationId = null)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.ProformaEdit, "create a proforma invoice (Result variant)");

            var corrId = correlationId ?? CorrelationContext.NewIdFor("ProformaInvoice", invoice?.ProformaId);
            using var scope = CorrelationContext.BeginDocumentScope("ProformaInvoice", invoice?.ProformaId ?? "", corrId);

            try
            {
                var id = await InsertAsync(invoice!);
                AppLogService.Info(
                    $"PI inserted: {invoice!.ProformaId} (Id={id})",
                    new LogContext
                    {
                        Module = "Proforma",
                        Class = nameof(ProformaInvoiceRepository),
                        Operation = "Insert",
                        DocumentId = invoice.ProformaId,
                        DocumentType = "ProformaInvoice",
                        CorrelationId = corrId
                    });
                return Result<int>.Ok(id, corrId);
            }
            catch (Exception ex)
            {
                var errorId = AppLogService.Error(
                    $"Failed to insert PI {invoice?.ProformaId}: {ex.Message}",
                    ex,
                    new LogContext
                    {
                        Module = "Proforma",
                        Class = nameof(ProformaInvoiceRepository),
                        Operation = "Insert",
                        DocumentId = invoice?.ProformaId ?? "",
                        DocumentType = "ProformaInvoice",
                        CorrelationId = corrId
                    });
                return Result<int>.Fail($"Could not save invoice {invoice?.ProformaId}: {ex.Message}", ex, corrId, errorId);
            }
        }

        /// <summary>
        /// Phase 2: Update with Result and CorrelationId.
        /// </summary>
        public async Task<Result> UpdateWithResultAsync(SavedProformaInvoice invoice, string? correlationId = null)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.ProformaEdit, "update a proforma invoice (Result variant)");

            var corrId = correlationId ?? CorrelationContext.NewIdFor("ProformaInvoice", invoice?.ProformaId);
            using var scope = CorrelationContext.BeginDocumentScope("ProformaInvoice", invoice?.ProformaId ?? "", corrId);

            try
            {
                await UpdateAsync(invoice!);
                AppLogService.Info(
                    $"PI updated: {invoice!.ProformaId}",
                    new LogContext
                    {
                        Module = "Proforma",
                        Class = nameof(ProformaInvoiceRepository),
                        Operation = "Update",
                        DocumentId = invoice.ProformaId,
                        DocumentType = "ProformaInvoice",
                        CorrelationId = corrId
                    });
                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errorId = AppLogService.Error(
                    $"Failed to update PI {invoice?.ProformaId}: {ex.Message}",
                    ex,
                    new LogContext
                    {
                        Module = "Proforma",
                        Class = nameof(ProformaInvoiceRepository),
                        Operation = "Update",
                        DocumentId = invoice?.ProformaId ?? "",
                        DocumentType = "ProformaInvoice",
                        CorrelationId = corrId
                    });
                return Result.Fail($"Could not update invoice {invoice?.ProformaId}: {ex.Message}", ex, corrId, errorId);
            }
        }

        /// <summary>
        /// Phase 2: Delete with Result and CorrelationId.
        /// </summary>
        public async Task<Result> DeleteWithResultAsync(int id, string? proformaId = null, string? correlationId = null)
        {
            // A-1: service-layer authorization. The UI also checks,
            // but this is the boundary that cannot be bypassed.
            OperationGuard.Demand(
                AppPermissions.ProformaDelete, "delete a proforma invoice (Result variant)");

            var corrId = correlationId ?? CorrelationContext.NewIdFor("ProformaInvoice", proformaId ?? id.ToString());
            using var scope = CorrelationContext.BeginDocumentScope("ProformaInvoice", proformaId ?? id.ToString(), corrId);

            try
            {
                await DeleteAsync(id);
                AppLogService.Info(
                    $"PI deleted: Id={id}, ProformaId={proformaId}",
                    new LogContext
                    {
                        Module = "Proforma",
                        Class = nameof(ProformaInvoiceRepository),
                        Operation = "Delete",
                        DocumentId = proformaId ?? id.ToString(),
                        DocumentType = "ProformaInvoice",
                        CorrelationId = corrId
                    });
                return Result.Ok(corrId);
            }
            catch (Exception ex)
            {
                var errorId = AppLogService.Error(
                    $"Failed to delete PI Id={id}: {ex.Message}",
                    ex,
                    new LogContext
                    {
                        Module = "Proforma",
                        Class = nameof(ProformaInvoiceRepository),
                        Operation = "Delete",
                        DocumentId = proformaId ?? id.ToString(),
                        DocumentType = "ProformaInvoice",
                        CorrelationId = corrId
                    });
                return Result.Fail($"Could not delete invoice: {ex.Message}", ex, corrId, errorId);
            }
        }
    }
}
