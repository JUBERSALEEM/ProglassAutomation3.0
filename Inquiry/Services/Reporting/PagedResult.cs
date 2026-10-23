// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Reporting/PagedResult.cs
// MODULE: Reporting — one page of a database-side paged query.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Reporting
{
    /// <summary>
    /// A single page of results from a database-side paginated query:
    /// the page window plus the totals the UI needs for paging controls.
    /// </summary>
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
        public bool HasNext => Page < TotalPages;
        public bool HasPrevious => Page > 1;
    }
}
