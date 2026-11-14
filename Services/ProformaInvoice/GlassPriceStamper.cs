// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/GlassPriceStamper.cs
// PROJECT: ProGlassAutomation
// PHASE 3 — profit-basis staleness signal.
//
// WHY A VERSION STAMP AND NOT A RE-COSTER
//   Step 9 is the single engine that turns a wizard into money. Building a
//   second engine inside reporting would drift from it the first time the
//   pricing logic changes — the classic shadow-calculator bug. What a
//   report can do HONESTLY is answer a narrower question: "did the price
//   basis move after this P&L was computed?" The Sheet Store stamps
//   LastUpdated on every write, so the whole basis fits into one cheap
//   signature:
//
//       stamp = "{max(GlassSheets.LastUpdated).Ticks}:{sheet count}"
//
//   Any sheet add / edit / delete after the invoice's P&L was computed
//   flips it. An empty stamp (failure, or a snapshot saved before this
//   feature existed) is reported as UNKNOWN — never silently treated as
//   fresh, never fabricated as stale.
//
//   Recovery path is also deliberately engine-only: open the invoice,
//   Step 9 recalculates live against the current store, save. The report
//   flags; the wizard re-costs. One engine, one truth.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Database;
using System;
using System.Linq;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    /// <summary>Version signature of the sheet-store price basis.</summary>
    public static class GlassPriceStamper
    {
        /// <summary>
        /// Current basis signature, or "" when it cannot be read (never
        /// throws — a signal must never block a save or a report).
        /// </summary>
        public static string Current()
        {
            try
            {
                using var db = new AppDbContext();
                int count = db.GlassSheets.Count();
                DateTime? max = db.GlassSheets
                    .Max(s => (DateTime?)s.LastUpdated);
                return $"{(max?.Ticks ?? 0L)}:{count}";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// True only when BOTH stamps are readable AND differ. An unknown
        /// snapshot stamp (pre-versioning save) is handled by the caller
        /// as its own state, because "we don't know" is not "it changed".
        /// New callers should prefer <see cref="Classify"/> and keep this
        /// for the simple boolean question only.
        /// </summary>
        public static bool HasMoved(string? savedStamp, string? currentStamp)
            => Classify(savedStamp, currentStamp) == BasisState.Moved;

        /// <summary>Basis verdict for a saved P&L stamp.</summary>
        public enum BasisState
        {
            /// <summary>Saved stamp matches the live basis.</summary>
            Current,
            /// <summary>The sheet store changed AFTER the P&L was pinned.</summary>
            Moved,
            /// <summary>No stamp saved (pre-versioning) — never silently fresh.</summary>
            Unconfirmed,
            /// <summary>The live basis could not be read — report nothing.</summary>
            BasisUnreadable
        }

        /// <summary>
        /// THE single classification home for basis staleness. Pure and
        /// deterministic — the same inputs must always give the same
        /// verdict, and an unreadable LIVE basis always degrades the
        /// answer to "we don't know", never to a false alarm.
        /// </summary>
        public static BasisState Classify(string? savedStamp, string? currentStamp)
        {
            if (string.IsNullOrWhiteSpace(currentStamp)) return BasisState.BasisUnreadable;
            if (string.IsNullOrWhiteSpace(savedStamp)) return BasisState.Unconfirmed;
            return string.Equals(savedStamp, currentStamp, StringComparison.Ordinal)
                ? BasisState.Current
                : BasisState.Moved;
        }
    }
}
