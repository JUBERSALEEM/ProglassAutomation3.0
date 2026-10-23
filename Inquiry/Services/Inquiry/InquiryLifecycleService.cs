// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryLifecycleService.cs
// MODULE: Inquiry — lifecycle hydration implementation (V3).
//
// V3 strategy
//   The rebuilt app has not yet ported the PI / JO / DN / TI / Statement
//   modules, so there are no sibling databases to walk. The snapshot is
//   hydrated from the denormalised downstream link fields that live on
//   the Inquiries row itself:
//
//     • ProformaInvoiceId / ProformaNumber — written by
//       SetProformaLinkAsync once the Proforma module persists a PI.
//     • JobOrderNumber — written when the JO module releases a job from
//       that PI.
//
//   Those two stages therefore render exactly as they will in the full
//   build; DN / TI / STMT stages report NotStarted (zero counts, empty
//   numbers) until their modules land, and the snapshot contract
//   (InquiryLifecycleSnapshot) does not need to change for them.
//
// The cache semantics are unchanged from the legacy build: 60 s TTL,
// keyed by the sorted ID batch, pruned at 64 batches, invalidated on
// writes. Re-sorting / re-filtering the register stays instant.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Inquiry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Inquiry
{
    public sealed class InquiryLifecycleService : IInquiryLifecycleService
    {
        /// <summary>
        /// 60 s TTL: long enough to absorb re-sort / re-filter / page
        /// navigation, short enough that a downstream edit becomes
        /// visible within a minute even without an explicit Invalidate().
        /// </summary>
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

        private sealed class CacheEntry
        {
            public DateTime StoredAt { get; init; } = DateTime.UtcNow;
            public IReadOnlyDictionary<int, InquiryLifecycleSnapshot> Snapshots { get; init; }
                = new Dictionary<int, InquiryLifecycleSnapshot>();
            public bool IsFresh => DateTime.UtcNow - StoredAt < CacheTtl;
        }

        // Guarded by lock(_lock). Bounded by page-size * active users;
        // we keep at most 64 batches (~3,200 inquiry IDs) before pruning.
        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly object _lock = new();

        public async Task<IReadOnlyDictionary<int, InquiryLifecycleSnapshot>> GetLifecycleAsync(
            IReadOnlyCollection<int> inquiryIds, CancellationToken ct = default)
        {
            if (inquiryIds == null || inquiryIds.Count == 0)
                return new Dictionary<int, InquiryLifecycleSnapshot>();

            string key = string.Join(",", inquiryIds.OrderBy(x => x));
            CacheEntry? hit = null;
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry) && entry.IsFresh)
                    hit = entry;
            }
            if (hit != null) return hit.Snapshots;

            // One small AsNoTracking SELECT over the Inquiries row's
            // denormalised link fields. The PI module will write these
            // through SetProformaLinkAsync; until then every stage
            // reports NotStarted and the snapshot renders "—".
            var rows = new List<(int Id, string Status, string ProformaNumber, string JobOrderNumber)>();
            await using (var db = new InquiryDbContext())
            {
                rows = await db.Inquiries.AsNoTracking()
                    .Where(x => inquiryIds.Contains(x.Id))
                    .Select(x => new ValueTuple<int, string, string, string>(
                        x.Id, x.Status, x.ProformaNumber, x.JobOrderNumber))
                    .ToListAsync(ct).ConfigureAwait(false);
            }

            var result = new Dictionary<int, InquiryLifecycleSnapshot>(inquiryIds.Count);
            foreach (int id in inquiryIds)
                result[id] = InquiryLifecycleSnapshot.Empty(id);
            foreach (var (id, status, piNumber, joNumber) in rows)
                result[id] = BuildSnapshot(id, status, piNumber, joNumber);

            // ── Cache + prune ───────────────────────────────────────────
            lock (_lock)
            {
                _cache[key] = new CacheEntry { Snapshots = result };
                if (_cache.Count > 64)
                {
                    // Drop expired entries first; if still over budget,
                    // drop the oldest.
                    var expired = _cache.Where(kv => !kv.Value.IsFresh)
                        .Select(kv => kv.Key).ToList();
                    foreach (string k in expired) _cache.Remove(k);
                    if (_cache.Count > 64)
                    {
                        var oldest = _cache.OrderBy(kv => kv.Value.StoredAt)
                            .Take(_cache.Count - 64).Select(kv => kv.Key).ToList();
                        foreach (string k in oldest) _cache.Remove(k);
                    }
                }
            }
            return result;
        }

        public void InvalidateAll()
        {
            lock (_lock) _cache.Clear();
        }

        public void Invalidate(int inquiryId)
        {
            lock (_lock)
            {
                var stale = _cache.Where(kv => kv.Value.Snapshots.ContainsKey(inquiryId))
                    .Select(kv => kv.Key).ToList();
                foreach (string k in stale) _cache.Remove(k);
            }
        }

        private static InquiryLifecycleSnapshot BuildSnapshot(
            int inquiryId, string status, string proformaNumber, string jobOrderNumber)
        {
            bool hasPi = !string.IsNullOrWhiteSpace(proformaNumber);
            bool hasJo = !string.IsNullOrWhiteSpace(jobOrderNumber);
            // V3: a linked PI/JO number exists in the register but the
            // downstream modules (and therefore the real document row
            // IDs) are not ported yet. Row IDs stay 0 so a future
            // "Open document" handler cannot navigate to a wrong row;
            // the stage is Active ("in flight") until the PI/JO module
            // can report its own status vocabulary.
            int furthest = hasJo ? 2 : hasPi ? 1 : 0;
            return new InquiryLifecycleSnapshot
            {
                InquiryId = inquiryId,
                InquiryStatus = status,
                ProformaRowId = 0,
                ProformaNumber = hasPi ? proformaNumber : null,
                ProformaStatus = hasPi ? "Active" : null,
                JobOrderRowId = 0,
                JobOrderNumber = hasJo ? jobOrderNumber : null,
                JobOrderStatus = hasJo ? "Active" : null,
                FurthestStageReached = furthest
            };
        }
    }
}
