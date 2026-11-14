// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/SpecSheetRegistry.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — Step 4 → Step 5 sheet handoff (PHASE G)
//
// THE PROBLEM THIS SOLVES
//   Step 4 lets the user choose the stock sheet a spec will be cut from
//   (e.g. 3210 × 2250). Step 5 then loads that spec BY NAME out of
//   specifications.db, which only stores name + price — the sheet choice
//   was lost. Step 9 therefore re-guessed the sheet from Sheet Store and
//   silently fell back to a 2440 × 1830 default, so the profit figure
//   could be costed against a sheet the user never picked.
//   With 3210×2250 (7.2225 SQM) vs 2440×1830 (4.4652 SQM) that is a ~38%
//   error in glass cost — invisible, and in the wrong direction.
//
// WHY A REGISTRY AND NOT A SCHEMA CHANGE
//   specifications.db is a portable, shareable spec library (its own
//   header says so). Sheet choice is a per-INVOICE decision, not a
//   property of the spec itself: the same "6mm Clear FT Glass" spec may
//   be cut from a jumbo sheet on one job and an offcut size on another.
//   So the live choice rides in this in-memory registry, and the
//   PERSISTED copy lives in the invoice snapshot (SpecGroupSnapshot),
//   which is where per-invoice decisions belong.
//
// CONTRACT
//   • Session-scoped, in-memory, thread-safe. Never throws.
//   • Keyed on the normalized spec name, so Step 5 can look up whatever
//     the user just applied in Step 4 without the two steps referencing
//     each other.
//   • A miss is normal (spec applied in an earlier session) — callers
//     fall back and say so, they do not fail.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    /// <summary>The stock sheets chosen for one spec in Step 4.</summary>
    public sealed class SpecSheetSelection
    {
        public int OuterWidthMm { get; init; }
        public int OuterHeightMm { get; init; }
        public int InnerWidthMm { get; init; }
        public int InnerHeightMm { get; init; }
        public int ThirdWidthMm { get; init; }
        public int ThirdHeightMm { get; init; }
        public decimal OuterPricePerSqm { get; init; }
        public decimal InnerPricePerSqm { get; init; }
        public decimal ThirdPricePerSqm { get; init; }

        /// <summary>
        /// Optional Step 4 seeded U-Insert rate for DGU builds. Lives in the
        /// same per-spec handoff as the sheet selection because Step 5 / Step 6
        /// need it later in the same session, but it is still an invoice-side
        /// choice rather than part of the portable spec library.
        /// </summary>
        public decimal UInsertPricePerSqm { get; init; }

        public bool HasOuter => OuterWidthMm > 0 && OuterHeightMm > 0;
        public bool HasInner => InnerWidthMm > 0 && InnerHeightMm > 0;
    }

    /// <summary>
    /// Session-scoped map of spec name → chosen stock sheets, written by
    /// Step 4 APPLY and read by Step 5 when a spec is selected.
    /// </summary>
    public static class SpecSheetRegistry
    {
        private static readonly object Gate = new();

        private static readonly Dictionary<string, SpecSheetSelection> Map =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Trim + lowercase, matching SheetStorePricingService.Norm.</summary>
        private static string Norm(string? value) =>
            (value ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>
        /// Records the sheets chosen for a spec. Called from Step 4 APPLY.
        /// A later apply for the same spec name overwrites the earlier one,
        /// which is correct: the most recent choice is the live one.
        /// </summary>
        public static void Record(string specName, SpecSheetSelection selection)
        {
            if (string.IsNullOrWhiteSpace(specName) || selection == null) return;

            lock (Gate) Map[Norm(specName)] = selection;
        }

        /// <summary>
        /// Convenience overload taking raw "W × H" display strings as they
        /// appear in the Step 4 combo boxes.
        /// </summary>
        public static void Record(
            string specName, string? outerDisplay, string? innerDisplay)
        {
            var (ow, oh) = ParseSize(outerDisplay);
            var (iw, ih) = ParseSize(innerDisplay);

            if (ow <= 0 && iw <= 0) return;   // nothing worth recording

            Record(specName, new SpecSheetSelection
            {
                OuterWidthMm = ow,
                OuterHeightMm = oh,
                InnerWidthMm = iw,
                InnerHeightMm = ih
            });
        }

        /// <summary>
        /// Looks up the sheets chosen for a spec. Returns null when the
        /// spec was applied in an earlier session — callers fall back.
        /// </summary>
        public static SpecSheetSelection? TryGet(string? specName)
        {
            if (string.IsNullOrWhiteSpace(specName)) return null;

            lock (Gate)
                return Map.TryGetValue(Norm(specName), out var found) ? found : null;
        }

        /// <summary>Drops everything (new invoice / test isolation).</summary>
        public static void Clear()
        {
            lock (Gate) Map.Clear();
        }

        /// <summary>
        /// Parses "3210 × 2250" (or "3210 x 2250", "3210*2250") into
        /// millimetres. Returns (0, 0) for anything unrecognised — a typed
        /// custom size that is not W×H is simply not a stock sheet.
        /// </summary>
        public static (int Width, int Height) ParseSize(string? display)
        {
            if (string.IsNullOrWhiteSpace(display)) return (0, 0);

            var separators = new[] { '×', 'x', 'X', '*' };
            int idx = display.IndexOfAny(separators);
            if (idx <= 0) return (0, 0);

            string left = new string(display[..idx]
                .Where(char.IsDigit).ToArray());
            string right = new string(display[(idx + 1)..]
                .Where(char.IsDigit).ToArray());

            if (!int.TryParse(left, out int w)) w = 0;
            if (!int.TryParse(right, out int h)) h = 0;

            return (w > 0 && h > 0) ? (w, h) : (0, 0);
        }
    }
}
