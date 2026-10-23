// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/IInquiryLifecycleService.cs
// MODULE: Inquiry — lifecycle hydration contract.
//
// Single responsibility: given a batch of inquiry IDs, return a
// per-inquiry snapshot of the downstream commercial chain
// (PI / JO / DN / TI / STMT).
//
// Implementations must:
//   - never throw on missing data (return NotStarted for that stage);
//   - be safe to call from the register's UI thread (async only);
//   - support a 60-second in-memory cache keyed by the input ID set so
//     re-sorting / re-filtering the register is instant;
//   - invalidate the cache when the caller signals a write happened
//     (e.g. PI created, DN dispatched, TI paid).
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Inquiry
{
    public interface IInquiryLifecycleService
    {
        /// <summary>
        /// Returns one <see cref="InquiryLifecycleSnapshot"/> per
        /// requested inquiry ID. IDs that have no downstream documents
        /// get an <see cref="InquiryLifecycleSnapshot.Empty"/> entry.
        /// </summary>
        Task<IReadOnlyDictionary<int, InquiryLifecycleSnapshot>> GetLifecycleAsync(
            IReadOnlyCollection<int> inquiryIds,
            CancellationToken ct = default);

        /// <summary>
        /// Drop the cache. Call after any write that could have changed
        /// downstream state (PI created, JO released, DN dispatched,
        /// TI issued, statement generated, etc.).
        /// </summary>
        void InvalidateAll();

        /// <summary>
        /// Drop the cache for one inquiry. Call after edits to that row.
        /// </summary>
        void Invalidate(int inquiryId);
    }
}
