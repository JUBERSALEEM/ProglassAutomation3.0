// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryRepository.cs
// MODULE: Inquiry — persistence, numbering, concurrency, lifecycle cache.
//
// Ported from the legacy build. Behaviour kept unchanged:
//   • numbering: INQ-<year>-<D4 sequence>, regenerated on UNIQUE
//     collision (fresh DbContext per attempt, up to 20 attempts with
//     jittered delay);
//   • optimistic concurrency: RowVersion token checked in-repository,
//     ConcurrencyConflictException on stale writes;
//   • transient SQLITE_BUSY / SQLITE_LOCKED retried with exponential
//     back-off (SqliteResilience.SaveWithRetryAsync);
//   • cascade deletes + downstream-document delete guard.
//
// V3 scope: the legacy licensing gate (ReadOnlyGate) and role
// permissions (OperationGuard) are not ported — V3 starts without the
// licensing module. Validation (InquiryValidationRules) is untouched.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Reporting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Inquiry
{
    public sealed class InquiryRepository : IInquiryRepository
    {
        private readonly IInquiryLifecycleService _lifecycle;

        public InquiryRepository() : this(new InquiryLifecycleService()) { }

        public InquiryRepository(IInquiryLifecycleService lifecycle)
        {
            _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        }

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            await db.Database.EnsureCreatedAsync(ct).ConfigureAwait(false);
            SchemaMigrationRunner.Upgrade(InquiryDbContext.DatabaseFilePath,
                AppSchemaMigrations.Inquiries, "Inquiry");
        }

        public async Task<PagedResult<InquiryListProjection>> QueryAsync(
            InquiryQuery query, CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            var source = ApplyFilters(db.Inquiries.AsNoTracking(), query, includeStatus: true);
            source = query.Sort switch
            {
                "Updated oldest" => source.OrderBy(x => x.UpdatedAt),
                "Received oldest" => source.OrderBy(x => x.ReceivedAt),
                "Received newest" => source.OrderByDescending(x => x.ReceivedAt),
                "Inquiry number" => source.OrderBy(x => x.InquiryNumber),
                "Inquiry number descending" => source.OrderByDescending(x => x.InquiryNumber),
                "Value highest" => source.OrderByDescending(x => x.InquiryValue),
                _ => source.OrderByDescending(x => x.UpdatedAt)
            };
            var page = await GlobalQueryEngine.Instance.ExecutePagedQueryAsync(source,
                x => new InquiryListProjection
                {
                    Id = x.Id, InquiryNumber = x.InquiryNumber, InquiryReference = x.InquiryReference,
                    CustomerName = x.CustomerName, CustomerLocation = x.CustomerLocation,
                    ProjectName = x.ProjectName, ProjectDescription = x.ProjectDescription,
                    SalesmanName = x.SalesmanName, PreparedByName = x.PreparedByName,
                    PreparedByRole = x.PreparedByRole, Status = x.Status, Priority = x.Priority,
                    InquiryValue = x.InquiryValue, Notes = x.Notes,
                    ReceivedAt = x.ReceivedAt, UpdatedAt = x.UpdatedAt, UpdatedBy = x.UpdatedBy,
                    AttachmentCount = x.Attachments.Count, ProformaInvoiceId = x.ProformaInvoiceId,
                    ProformaNumber = x.ProformaNumber, JobOrderNumber = x.JobOrderNumber,
                    RowVersion = x.RowVersion
                }, query.Page, Math.Clamp(query.PageSize, 10, 200), ct).ConfigureAwait(false);
            var ids = page.Items.Select(x => x.Id).ToList();
            if (ids.Count > 0)
            {
                var lifecycle = await _lifecycle.GetLifecycleAsync(ids, ct).ConfigureAwait(false);
                foreach (var row in page.Items)
                {
                    row.Lifecycle = lifecycle.TryGetValue(row.Id, out var snap)
                        ? snap
                        : InquiryLifecycleSnapshot.Empty(row.Id);
                }
            }
            if (ids.Count > 0)
            {
                var metadata = await db.Attachments.AsNoTracking()
                    .Where(x => ids.Contains(x.InquiryId))
                    .Select(x => new { x.InquiryId, x.Extension })
                    .ToListAsync(ct).ConfigureAwait(false);
                foreach (var row in page.Items)
                {
                    var types = metadata.Where(x => x.InquiryId == row.Id)
                        .Select(x => (x.Extension ?? "").TrimStart('.').ToUpperInvariant())
                        .Where(x => x.Length > 0).Distinct().Take(3).ToList();
                    int extra = Math.Max(0, row.AttachmentCount - types.Count);
                    row.AttachmentBadgeText = string.Join(" ", types)
                        + (extra > 0 ? $" +{extra}" : "");
                }
            }
            return page;
        }

        public async Task<InquiryStatistics> GetStatisticsAsync(InquiryQuery query, CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            var source = ApplyFilters(db.Inquiries.AsNoTracking(), query, includeStatus: false);
            var rows = await source.Select(x => new { x.Status, x.InquiryValue })
                .ToListAsync(ct).ConfigureAwait(false);
            int Count(string status) => rows.Count(x => x.Status == status);
            return new InquiryStatistics
            {
                All = rows.Count,
                Received = Count(InquiryStatuses.Received),
                Prepared = Count(InquiryStatuses.Prepared),
                Sent = Count(InquiryStatuses.Sent),
                Confirmed = Count(InquiryStatuses.Confirmed),
                Hold = Count(InquiryStatuses.Hold),
                Cancelled = Count(InquiryStatuses.Cancelled),
                OpenPipelineValue = rows.Where(x => x.Status != InquiryStatuses.Cancelled)
                    .Sum(x => x.InquiryValue)
            };
        }

        public async Task<InquiryRecord?> GetDetailAsync(int id, CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            // Include LineItems — the BOQ travels with the record so the
            // editor edits it and ConvertToPi can import it.
            return await db.Inquiries.AsNoTracking()
                .Include(x => x.LineItems)
                .SingleOrDefaultAsync(x => x.Id == id, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Replace-all persistence for an inquiry's BOQ lines.
        /// Replace-all (rather than a diff-merge) because the editor owns
        /// the full list in-memory; deleting a row is a UI action that
        /// must persist as a deletion too. Single context + one commit:
        /// a half-saved BOQ (old rows partly gone) is impossible.
        /// Writes a communication entry so the inquiry's history shows
        /// who reshaped the scope of supply and when.
        /// </summary>
        public async Task<int> SaveLineItemsAsync(
            int inquiryId,
            IReadOnlyList<InquiryLineItem> items,
            int expectedRowVersion,
            string updatedBy,
            CancellationToken ct = default)
        {
            if (inquiryId <= 0) throw new ArgumentException(
                "Line items belong to a saved inquiry.", nameof(inquiryId));
            items ??= Array.Empty<InquiryLineItem>();
            var validationErrors = InquiryValidationRules.ValidateLineItems(items);
            if (validationErrors.Count > 0)
                throw new InvalidOperationException(
                    "Inquiry scope-of-supply is not safe to save: " +
                    string.Join(" ", validationErrors.Take(5)));

            await using var db = new InquiryDbContext();
            var stored = await db.Inquiries
                .SingleOrDefaultAsync(x => x.Id == inquiryId, ct)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "Inquiry no longer exists. It may have been removed by another user — refresh the register.");
            if (stored.RowVersion != expectedRowVersion)
                throw new ConcurrencyConflictException(
                    "Inquiry", stored.InquiryNumber, expectedRowVersion, stored.RowVersion);

            // ORDER: wipe + insert. If the insert throws, the transaction
            // rolls the wipe back with it — the caller retains the UI list
            // and SaveLineItemsAsync may be retried safely.
            db.LineItems.RemoveRange(
                db.LineItems.Where(x => x.InquiryId == inquiryId));

            var now = DateTime.Now;
            int sort = 0;
            foreach (var item in items)
            {
                db.LineItems.Add(new InquiryLineItem
                {
                    InquiryId = inquiryId,
                    SortOrder = ++sort,
                    GlassType = (item.GlassType ?? "").Trim(),
                    SpecName = (item.SpecName ?? "").Trim(),
                    OuterGlassColor = (item.OuterGlassColor ?? "").Trim(),
                    InnerGlassColor = (item.InnerGlassColor ?? "").Trim(),
                    WidthMm = item.WidthMm,
                    HeightMm = item.HeightMm,
                    Qty = item.Qty,
                    RatePerSqm = item.RatePerSqm,
                    Notes = item.Notes ?? "",
                    CreatedAt = now
                });
            }

            stored.UpdatedAt = now;
            stored.UpdatedBy = updatedBy ?? "";
            stored.RowVersion++;
            db.Communications.Add(new InquiryCommunication
            {
                InquiryId = inquiryId,
                EventType = "BOQ",
                Title = $"Scope of supply saved ({sort} item{(sort == 1 ? "" : "s")})",
                Description = "Scope of supply (BOQ) updated from the inquiry editor.",
                OccurredAt = now,
                ActorName = updatedBy ?? ""
            });

            try
            {
                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                int actual = await GetCurrentRowVersionAsync(db, inquiryId, ct).ConfigureAwait(false);
                throw new ConcurrencyConflictException(
                    "Inquiry", stored.InquiryNumber, expectedRowVersion, actual);
            }
            _lifecycle.Invalidate(inquiryId);
            return stored.RowVersion;
        }

        public async Task<InquiryRecord> CreateAsync(InquiryRecord inquiry, CancellationToken ct = default)
        {
            if (inquiry == null) throw new ArgumentNullException(nameof(inquiry));

            // A fresh DbContext per attempt: when SaveChangesAsync throws
            // a UNIQUE violation, only the tracked entity is detached —
            // the connection's transaction state and the change tracker's
            // shadow-property cache are left in a partial state, so the
            // next SaveChanges on the same context could throw a
            // confusing "attempted to update or insert entity with the
            // same key". We allocate a fresh DbContext per attempt and
            // let the previous context dispose cleanly.
            //
            // Retry semantics:
            //   • UNIQUE violation → regenerate InquiryNumber, retry
            //     (up to 20 attempts; 8 concurrent creates need up to 8
            //     sequential attempts, so 20 with jittered delay);
            //   • SQLITE_BUSY / SQLITE_LOCKED →
            //     SqliteResilience.SaveWithRetryAsync handles those
            //     inside a single attempt (exponential back-off);
            //   • any other error → surface immediately.
            const int maxAttempts = 20;
            InquiryRecord? saved = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                await using var db = new InquiryDbContext();

                if (string.IsNullOrWhiteSpace(inquiry.InquiryNumber) || attempt > 1)
                    inquiry.InquiryNumber = await NextInquiryNumberAsync(db, ct).ConfigureAwait(false);

                var now = DateTime.UtcNow;
                inquiry.Status = InquiryStatuses.Normalize(inquiry.Status);
                inquiry.CreatedAt = now;
                inquiry.UpdatedAt = now;
                if (inquiry.ReceivedAt == default) inquiry.ReceivedAt = now;

                db.Inquiries.Add(inquiry);
                try
                {
                    await SqliteResilience.SaveWithRetryAsync(
                        token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                    saved = inquiry;
                    break;
                }
                catch (Exception ex) when (attempt < maxAttempts && SqliteErrorClassifier.IsUnique(ex))
                {
                    // Fresh context on the next iteration re-seeds the
                    // sequence number and re-attempts.
                    try { db.Entry(inquiry).State = EntityState.Detached; } catch { /* detach is best-effort */ }
                    // Small jittered delay to reduce immediate re-collision
                    // under 8-way concurrency.
                    try { await Task.Delay(10 * attempt + Random.Shared.Next(0, 20), ct).ConfigureAwait(false); }
                    catch { /* cancellation on dispose is acceptable */ }
                }
            }

            if (saved == null)
            {
                // Exhausted retries — surface a clean domain exception
                // so the UI layer can show a friendly message instead
                // of the SQLite error text.
                throw new InvalidOperationException(
                    "Could not allocate a unique inquiry number after " + maxAttempts +
                    " attempts. The sequence table may be under heavy contention — try again in a moment.");
            }

            _lifecycle.Invalidate(saved.Id);
            return saved;
        }

        /// <summary>Maximum existing sequence for the current year prefix, plus one.</summary>
        private static async Task<string> NextInquiryNumberAsync(InquiryDbContext db, CancellationToken ct)
        {
            string prefix = $"INQ-{DateTime.Now:yyyy}-";
            var numbers = await db.Inquiries.AsNoTracking()
                .Where(x => x.InquiryNumber.StartsWith(prefix))
                .Select(x => x.InquiryNumber).ToListAsync(ct).ConfigureAwait(false);
            int highest = numbers.Select(x => ParseSequenceSuffix(x, prefix))
                .DefaultIfEmpty(0).Max();
            return prefix + (highest + 1).ToString("D4");
        }

        /// <summary>Pure helper (unit-tested): "INQ-2026-0007" + prefix → 7, junk → 0.</summary>
        public static int ParseSequenceSuffix(string? number, string prefix)
        {
            if (string.IsNullOrEmpty(number) || number.Length <= prefix.Length)
                return 0;
            return int.TryParse(number.Substring(prefix.Length), out int value) ? value : 0;
        }

        public async Task<InquiryRecord> UpdateAsync(
            InquiryRecord inquiry, int expectedRowVersion, CancellationToken ct = default)
        {
            if (inquiry == null || inquiry.Id <= 0) throw new ArgumentException("A saved Inquiry is required.", nameof(inquiry));
            await using var db = new InquiryDbContext();
            var stored = await db.Inquiries.SingleOrDefaultAsync(x => x.Id == inquiry.Id, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Inquiry no longer exists. It may have been removed by another user — refresh the register.");
            if (stored.RowVersion != expectedRowVersion)
                throw new ConcurrencyConflictException(
                    "Inquiry", stored.InquiryNumber, expectedRowVersion, stored.RowVersion);
            stored.InquiryReference = inquiry.InquiryReference;
            stored.CustomerId = inquiry.CustomerId; stored.CustomerName = inquiry.CustomerName;
            stored.CustomerLocation = inquiry.CustomerLocation; stored.ProjectId = inquiry.ProjectId;
            stored.ProjectName = inquiry.ProjectName; stored.ProjectDescription = inquiry.ProjectDescription;
            stored.SalesmanId = inquiry.SalesmanId; stored.SalesmanName = inquiry.SalesmanName;
            stored.PreparedByUserId = inquiry.PreparedByUserId; stored.PreparedByName = inquiry.PreparedByName;
            stored.PreparedByRole = inquiry.PreparedByRole; stored.Priority = inquiry.Priority;
            stored.InquiryValue = inquiry.InquiryValue; stored.Notes = inquiry.Notes;
            // ReceivedAt is an editable field — persist it. (Never write
            // default; a stale binding must not reset the date to 0001-01-01.)
            if (inquiry.ReceivedAt != default) stored.ReceivedAt = inquiry.ReceivedAt;
            stored.UpdatedAt = DateTime.Now; stored.UpdatedBy = inquiry.UpdatedBy;
            stored.RowVersion++;
            try
            {
                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                int actual = await GetCurrentRowVersionAsync(db, inquiry.Id, ct).ConfigureAwait(false);
                throw new ConcurrencyConflictException(
                    "Inquiry", stored.InquiryNumber, expectedRowVersion, actual);
            }
            _lifecycle.Invalidate(stored.Id);
            return stored;
        }

        public async Task<bool> ChangeStatusAsync(
            int id, string status, string actor, int expectedRowVersion,
            CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            var row = await db.Inquiries.SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
            if (row == null) return false;
            if (row.RowVersion != expectedRowVersion)
                throw new ConcurrencyConflictException(
                    "Inquiry", row.InquiryNumber, expectedRowVersion, row.RowVersion);
            string next = InquiryStatuses.Normalize(status);
            row.Status = next; row.UpdatedAt = DateTime.Now; row.UpdatedBy = actor ?? "";
            if (next == InquiryStatuses.Prepared)
            {
                row.PreparedAt ??= row.UpdatedAt;
                // Stamp the preparer the first time the record is
                // prepared — the register's "Prepared By" column was
                // silently empty when records were created from import.
                if (string.IsNullOrWhiteSpace(row.PreparedByName))
                    row.PreparedByName = actor ?? "";
            }
            if (next == InquiryStatuses.Sent) row.SentAt ??= row.UpdatedAt;
            if (next == InquiryStatuses.Confirmed) row.ConfirmedAt ??= row.UpdatedAt;
            row.RowVersion++;
            db.Communications.Add(new InquiryCommunication
            {
                InquiryId = id, EventType = next, Title = $"Inquiry {next}",
                Description = $"Status changed to {next}.", OccurredAt = row.UpdatedAt,
                ActorName = actor ?? ""
            });
            try
            {
                await SqliteResilience.SaveWithRetryAsync(
                    token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                int actual = await GetCurrentRowVersionAsync(db, id, ct).ConfigureAwait(false);
                throw new ConcurrencyConflictException(
                    "Inquiry", row.InquiryNumber, expectedRowVersion, actual);
            }
            _lifecycle.Invalidate(id);
            return true;
        }

        public async Task<IReadOnlyList<InquiryAttachment>> GetAttachmentsAsync(int inquiryId, CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            return await db.Attachments.AsNoTracking().Where(x => x.InquiryId == inquiryId)
                .OrderByDescending(x => x.UploadedAt).ToListAsync(ct).ConfigureAwait(false);
        }

        public async Task<InquiryAttachment> AddAttachmentAsync(InquiryAttachment attachment, CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            db.Attachments.Add(attachment);
            await SqliteResilience.SaveWithRetryAsync(token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
            return attachment;
        }

        public async Task<bool> DeleteAttachmentAsync(long attachmentId, CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            var row = await db.Attachments.SingleOrDefaultAsync(x => x.Id == attachmentId, ct).ConfigureAwait(false);
            if (row == null) return false;
            db.Attachments.Remove(row);
            await SqliteResilience.SaveWithRetryAsync(token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
            return true;
        }

        public async Task<IReadOnlyList<InquiryCommunication>> GetCommunicationsAsync(int inquiryId, CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            return await db.Communications.AsNoTracking().Where(x => x.InquiryId == inquiryId)
                .OrderByDescending(x => x.OccurredAt).ToListAsync(ct).ConfigureAwait(false);
        }

        public async Task<DeleteInquiryResult> DeleteAsync(int id, CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            var row = await db.Inquiries
                .Include(x => x.Attachments)
                .Include(x => x.Communications)
                .SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
            if (row == null) return DeleteInquiryResult.NotFound();

            // Downstream-document guard: an inquiry that's already
            // spawned a PI / JO / DN / TI / Statement is part of the
            // audit trail and must be cancelled, not erased. Deleting it
            // would orphan the downstream rows and break the
            // "FurthestStageReached" history. We surface the blocking
            // document numbers so the caller can show a precise message.
            var blocking = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.ProformaNumber)) blocking.Add($"PI {row.ProformaNumber}");
            if (!string.IsNullOrWhiteSpace(row.JobOrderNumber)) blocking.Add($"JO {row.JobOrderNumber}");
            if (row.ProformaInvoiceId.HasValue)
            {
                // Re-check for actual downstream documents even when the
                // denormalised fields are empty (e.g. legacy rows from
                // before the link fields were populated). The lifecycle
                // service has the cached picture.
                var snap = (await _lifecycle.GetLifecycleAsync(new[] { id }, ct).ConfigureAwait(false))
                    .TryGetValue(id, out var s) ? s : null;
                if (snap != null)
                {
                    if (snap.HasProforma && !blocking.Any(x => x.StartsWith("PI ")))
                        blocking.Add($"PI {snap.ProformaNumber}");
                    if (snap.HasJobOrder && !blocking.Any(x => x.StartsWith("JO ")))
                        blocking.Add($"JO {snap.JobOrderNumber}");
                    if (snap.HasDeliveryNote && !blocking.Any(x => x.StartsWith("DN ")))
                        blocking.Add($"DN {snap.LatestDeliveryNoteNumber}");
                    if (snap.HasTaxInvoice && !blocking.Any(x => x.StartsWith("TI ")))
                        blocking.Add($"TI {snap.LatestTaxInvoiceNumber}");
                    if (snap.HasStatement && !blocking.Any(x => x.StartsWith("STMT ")))
                        blocking.Add($"STMT {snap.LatestStatementNumber}");
                }
            }
            if (blocking.Count > 0) return DeleteInquiryResult.Blocked(blocking);

            // Delete the children first. With foreign_keys=ON on every
            // connection, the FK constraints declared in
            // InquiryDbContext.OnModelCreating are enforced by SQLite —
            // leaving orphan children would throw
            // SQLITE_CONSTRAINT_FOREIGNKEY. EF Core honours the
            // OnDelete(Cascade) rule too, but an explicit delete is
            // faster (one round-trip) and behaves identically either way.
            if (row.Communications.Count > 0) db.Communications.RemoveRange(row.Communications);
            if (row.Attachments.Count > 0) db.Attachments.RemoveRange(row.Attachments);
            db.Inquiries.Remove(row);
            await SqliteResilience.SaveWithRetryAsync(token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
            _lifecycle.Invalidate(id);
            return DeleteInquiryResult.Deleted();
        }

        public async Task<InquiryRecord> DuplicateAsync(int sourceId, CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            // Load LineItems too — a clone carries the scope of supply
            // (it IS the inquiry, unlike attachments which belong to the
            // original request thread).
            var source = await db.Inquiries.AsNoTracking()
                .Include(x => x.LineItems)
                .SingleOrDefaultAsync(x => x.Id == sourceId, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Inquiry {sourceId} no longer exists. It may have been removed — refresh the register.");

            var now = DateTime.Now;
            var clone = new InquiryRecord
            {
                // New identity: fresh number, new Id (EF assigns on save).
                InquiryNumber = await NextInquiryNumberAsync(db, ct).ConfigureAwait(false),
                // The reference is preserved so the clone carries a
                // breadcrumb back to the source.
                InquiryReference = string.IsNullOrWhiteSpace(source.InquiryReference)
                    ? $"Clone of {source.InquiryNumber}"
                    : source.InquiryReference + " · cloned",
                CustomerId = source.CustomerId,
                CustomerName = source.CustomerName,
                CustomerLocation = source.CustomerLocation,
                ProjectId = source.ProjectId,
                ProjectName = source.ProjectName,
                ProjectDescription = source.ProjectDescription,
                SalesmanId = source.SalesmanId,
                SalesmanName = source.SalesmanName,
                PreparedByUserId = source.PreparedByUserId,
                PreparedByName = source.PreparedByName,
                PreparedByRole = source.PreparedByRole,
                Status = InquiryStatuses.Received, // clones always start as a new intake
                Priority = source.Priority,
                InquiryValue = source.InquiryValue,
                Notes = source.Notes,
                // ReceivedAt is "now" (when the clone was raised).
                // PreparedAt / SentAt / ConfirmedAt are dropped — the new
                // inquiry has no downstream history yet.
                ReceivedAt = now,
                PreparedAt = null,
                SentAt = null,
                ConfirmedAt = null,
                CreatedAt = now,
                UpdatedAt = now,
                // UpdatedBy stays empty so the audit trail starts fresh.
                UpdatedBy = "",
                // Critical: drop the downstream link fields. A clone
                // must not inherit the source's ProformaInvoiceId /
                // ProformaNumber / JobOrderNumber, otherwise the new
                // draft would show as "already converted to PI" and
                // block a fresh conversion flow.
                ProformaInvoiceId = null,
                ProformaNumber = "",
                JobOrderNumber = "",
                // Clone the scope of supply. Fresh Ids/SortOrder;
                // InquiryId is wired by the navigation on save.
                LineItems = source.LineItems
                    .OrderBy(x => x.SortOrder)
                    .Select(x => new InquiryLineItem
                    {
                        SortOrder = x.SortOrder,
                        GlassType = x.GlassType,
                        SpecName = x.SpecName,
                        OuterGlassColor = x.OuterGlassColor,
                        InnerGlassColor = x.InnerGlassColor,
                        WidthMm = x.WidthMm,
                        HeightMm = x.HeightMm,
                        Qty = x.Qty,
                        RatePerSqm = x.RatePerSqm,
                        Notes = x.Notes,
                        CreatedAt = now
                    })
                    .ToList()
                // Attachments and Communications stay empty — the
                // user's explicit choice for a new request.
            };
            db.Inquiries.Add(clone);
            await SqliteResilience.SaveWithRetryAsync(token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
            _lifecycle.Invalidate(clone.Id);
            return clone;
        }

        public void InvalidateLifecycle(int inquiryId) => _lifecycle.Invalidate(inquiryId);

        // ══════════════════════════════════════════════════════════════
        //  PI back-link write (ported from the legacy build)
        //
        //  The single write-side for Inquiries.ProformaInvoiceId and
        //  Inquiries.ProformaNumber. Called from the Proforma
        //  repository after each Insert / Update / Delete of a PI that
        //  has an InquiryId (link set / refreshed / cleared), so the
        //  inquiry list can show "Converted → PI xxx" without a second
        //  query and the Convert-to-PI button disables once a PI
        //  already exists.
        //
        //  DELIBERATE CHOICES
        //  ──────────────────
        //  • Best-effort — returns false on any exception. The PI
        //    operation must not roll back just because the
        //    denormalised cache write failed.
        //  • Idempotent — a no-op update when the target values already
        //    match returns true without touching the row.
        // ══════════════════════════════════════════════════════════════

        public async Task<bool> SetProformaLinkAsync(
            int inquiryId, int? proformaInvoiceId, string proformaNumber,
            CancellationToken ct = default)
        {
            try
            {
                if (inquiryId <= 0) return false;
                proformaNumber ??= string.Empty;
                await using var db = new InquiryDbContext();
                var row = await db.Inquiries
                    .FirstOrDefaultAsync(x => x.Id == inquiryId, ct)
                    .ConfigureAwait(false);
                if (row == null) return false;

                bool changed =
                    row.ProformaInvoiceId != proformaInvoiceId ||
                    !string.Equals(row.ProformaNumber ?? "", proformaNumber,
                        StringComparison.Ordinal);
                if (!changed)
                {
                    // Still invalidate the cache so a stale downstream
                    // snapshot cannot survive across a PI edit that
                    // happened to leave the denormalised fields alone.
                    _lifecycle.Invalidate(inquiryId);
                    return true;
                }

                row.ProformaInvoiceId = proformaInvoiceId;
                row.ProformaNumber = proformaNumber;
                row.UpdatedAt = DateTime.Now;
                row.RowVersion++;
                try
                {
                    await SqliteResilience.SaveWithRetryAsync(
                        token => db.SaveChangesAsync(token), ct).ConfigureAwait(false);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // This best-effort cross-document back-link must
                    // never overwrite a concurrent user edit. The
                    // concurrency token rejects the stale write; the PI
                    // operation can retry sync.
                    return false;
                }
                _lifecycle.Invalidate(inquiryId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ── V3 additions: filter-source lookups ─────────────────────────

        public async Task<IReadOnlyList<string>> GetDistinctSalesmenAsync(CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            return await db.Inquiries.AsNoTracking()
                .Where(x => x.SalesmanName != null && x.SalesmanName != "")
                .Select(x => x.SalesmanName)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<string>> GetDistinctPreparedByAsync(CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            return await db.Inquiries.AsNoTracking()
                .Where(x => x.PreparedByName != null && x.PreparedByName != "")
                .Select(x => x.PreparedByName)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<string>> GetDistinctCustomersAsync(CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            return await db.Inquiries.AsNoTracking()
                .Where(x => x.CustomerName != null && x.CustomerName != "")
                .Select(x => x.CustomerName)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(ct).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<(string Customer, string Location)>> GetCustomerLocationsAsync(CancellationToken ct = default)
        {
            await using var db = new InquiryDbContext();
            var rows = await db.Inquiries.AsNoTracking()
                .Where(x => x.CustomerName != null && x.CustomerName != ""
                    && x.CustomerLocation != null && x.CustomerLocation != "")
                .Select(x => new { x.CustomerName, x.CustomerLocation })
                .Distinct()
                .ToListAsync(ct).ConfigureAwait(false);
            return rows.Select(x => (x.CustomerName, x.CustomerLocation)).ToList();
        }

        private static async Task<int> GetCurrentRowVersionAsync(
            InquiryDbContext db, int inquiryId, CancellationToken ct)
        {
            return await db.Inquiries.AsNoTracking()
                .Where(x => x.Id == inquiryId)
                .Select(x => (int?)x.RowVersion)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? -1;
        }

        private static IQueryable<InquiryRecord> ApplyFilters(
            IQueryable<InquiryRecord> source, InquiryQuery query, bool includeStatus)
        {
            string search = (query.Search ?? "").Trim();
            if (search.Length > 0)
                source = source.Where(x => x.InquiryNumber.Contains(search)
                    || x.CustomerName.Contains(search) || x.ProjectName.Contains(search)
                    || x.SalesmanName.Contains(search) || x.PreparedByName.Contains(search));
            if (includeStatus && !string.IsNullOrWhiteSpace(query.Status)
                && !query.Status.StartsWith("All", StringComparison.OrdinalIgnoreCase))
                source = source.Where(x => x.Status == query.Status);
            if (query.SalesmanId.HasValue) source = source.Where(x => x.SalesmanId == query.SalesmanId);
            else if (!string.IsNullOrWhiteSpace(query.SalesmanName)
                && !query.SalesmanName.StartsWith("All", StringComparison.OrdinalIgnoreCase))
                source = source.Where(x => x.SalesmanName == query.SalesmanName);
            if (query.PreparedByUserId.HasValue) source = source.Where(x => x.PreparedByUserId == query.PreparedByUserId);
            else if (!string.IsNullOrWhiteSpace(query.PreparedByName)
                && !query.PreparedByName.StartsWith("All", StringComparison.OrdinalIgnoreCase))
                source = source.Where(x => x.PreparedByName == query.PreparedByName);
            if (query.From.HasValue) source = source.Where(x => x.ReceivedAt >= query.From.Value);
            if (query.ToExclusive.HasValue) source = source.Where(x => x.ReceivedAt < query.ToExclusive.Value);
            if (query.HasAttachmentsOnly) source = source.Where(x => x.Attachments.Any());
            if (!string.IsNullOrWhiteSpace(query.Priority)
                && !query.Priority.StartsWith("All", StringComparison.OrdinalIgnoreCase))
                source = source.Where(x => x.Priority == query.Priority);
            return source;
        }
    }
}
