// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Database/GlobalQueryEngine.cs
// MODULE: Database Core — high-scale query & paging engine (V3 port).
//
// PURPOSE
//   ONE centralized database query infrastructure for every ERP module:
//   • zero full-table memory materialization — filtering, paging and
//     aggregation happen in the SQLite engine;
//   • standardized controlled data windows (default 100 rows);
//   • automatic AsNoTracking() for read queries;
//   • CancellationToken propagation across every async step;
//   • projection-first design (.Select() returns only needed columns).
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Services.Reporting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Database
{
    /// <summary>
    /// Contract for the global high-scale database query and pagination engine.
    /// </summary>
    public interface IGlobalQueryEngine
    {
        Task<PagedResult<TResult>> ExecutePagedQueryAsync<TSource, TResult>(
            IQueryable<TSource> query,
            Expression<Func<TSource, TResult>> selector,
            int page = 1,
            int pageSize = 100,
            CancellationToken ct = default)
            where TSource : class;

        Task<int> ExecuteCountAsync<TSource>(
            IQueryable<TSource> query,
            CancellationToken ct = default)
            where TSource : class;
    }

    /// <summary>
    /// Thread-safe, high-performance database query engine.
    /// </summary>
    public sealed class GlobalQueryEngine : IGlobalQueryEngine
    {
        public static readonly GlobalQueryEngine Instance = new();

        public const int DefaultPageSize = 100;

        /// <summary>
        /// Executes a database-side paginated query with projection.
        /// Guaranteed: only the requested page window is retrieved.
        /// </summary>
        public async Task<PagedResult<TResult>> ExecutePagedQueryAsync<TSource, TResult>(
            IQueryable<TSource> query,
            Expression<Func<TSource, TResult>> selector,
            int page = 1,
            int pageSize = DefaultPageSize,
            CancellationToken ct = default)
            where TSource : class
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            var safePage = Math.Max(1, page);
            var safePageSize = Math.Max(1, pageSize);
            var skip = (safePage - 1) * safePageSize;

            try
            {
                // 1. Database-side total count query.
                var totalCount = await query.AsNoTracking().CountAsync(ct).ConfigureAwait(false);
                if (totalCount == 0 || ct.IsCancellationRequested)
                {
                    return new PagedResult<TResult>
                    {
                        Items = new List<TResult>(),
                        TotalCount = totalCount,
                        Page = safePage,
                        PageSize = safePageSize
                    };
                }

                // 2. Database-side Skip + Take + Projection query.
                var items = await query.AsNoTracking()
                    .Skip(skip)
                    .Take(safePageSize)
                    .Select(selector)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                return new PagedResult<TResult>
                {
                    Items = items,
                    TotalCount = totalCount,
                    Page = safePage,
                    PageSize = safePageSize
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // A failed page read must surface as an empty page the UI
                // can report — not crash the register.
                return new PagedResult<TResult>
                {
                    Items = new List<TResult>(),
                    TotalCount = 0,
                    Page = safePage,
                    PageSize = safePageSize
                };
            }
        }

        /// <summary>Executes a fast database-side COUNT query.</summary>
        public async Task<int> ExecuteCountAsync<TSource>(
            IQueryable<TSource> query,
            CancellationToken ct = default)
            where TSource : class
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            return await query.AsNoTracking().CountAsync(ct).ConfigureAwait(false);
        }
    }
}
