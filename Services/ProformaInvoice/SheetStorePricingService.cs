// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/SheetStorePricingService.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice ↔ Sheet Store pricing bridge (PHASE A)
//
// PURPOSE
//   One read-only lookup surface that lets the Step 4 pricing panels and
//   the Step 9 Profit & Loss engine ask the Sheet Store inventory:
//
//     "which colors do we stock?"                → GetColorsAsync()
//     "which thicknesses for that color?"        → GetThicknessesAsync(color)
//     "which sheet sizes for color + thickness?" → GetSizesAsync(color, thk)
//     "what does that cost / sell for per SQM?"  → TryGetPriceAsync(color, thk)
//
// CONTRACT (deliberate, do not relax)
//   • STATIC        — no DI wiring needed, callable from any VM.
//   • READ-ONLY     — never writes, never mutates stock. Uses the existing
//                     SheetStoreRepository.GetAllAsync() only, so it stays
//                     correct if the Sheet Store schema evolves.
//   • NEVER THROWS  — every public method is fully guarded. A missing DB,
//                     a locked file, an empty table: all degrade to an
//                     empty list / null. Pricing help is a convenience,
//                     it must never break the invoice wizard.
//   • MATCHING      — Norm() (trim + lowercase) on both sides so
//                     " clear ", "Clear" and "CLEAR" are one color.
//                     ParseMm("6mm") → 6 so UI strings map to DB integers.
//
// NOTE ON CACHING: intentionally none. Sheet Store rows change while the
// wizard is open (buy / use stock) and a full GetAllAsync() on a local
// SQLite table is cheap. Correctness beats micro-optimisation here.
//
// ONE EXCEPTION (2026-09-17): TryResolveStockSheet / IsStockedSize are SYNC,
// because the Step 5 optimization run builds its rack list on the UI thread and
// must not block on the database. They read a small snapshot the caller primes
// (PrimeSyncSnapshot) and self-refresh in the background; every async lookup
// above still reads the store fresh, so nothing that prices a quote can ever see
// a stale number. The snapshot is a CROSS-CHECK, not an authority: it only fills
// in a sheet size Step 4 did not supply.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.SheetStore;
using ProGlassAutomation.Services.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    /// <summary>
    /// Read-only pricing/size lookups over the Sheet Store inventory,
    /// shaped for the Proforma Invoice wizard. Static, additive, and
    /// guaranteed non-throwing (see file header contract).
    /// </summary>
    public static class SheetStorePricingService
    {
        // ═══════════════════════════════════════════════════════════════
        //  HELPERS — normalisation
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Canonical form used for every comparison: trimmed + lowercase.
        /// Null-safe (null → empty string).
        /// </summary>
        public static string Norm(string? value) =>
            (value ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>
        /// Parses a UI thickness string into millimetres.
        /// "6mm" → 6 · " 12 MM " → 12 · "10" → 10 · junk → 0.
        /// Never throws.
        /// </summary>
        public static int ParseMm(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            var digits = new System.Text.StringBuilder(4);
            foreach (var ch in text)
            {
                if (char.IsDigit(ch)) digits.Append(ch);
                else if (digits.Length > 0) break;   // stop at first break after digits
            }

            return digits.Length > 0 && int.TryParse(digits.ToString(), out int mm)
                ? mm
                : 0;
        }

        // ═══════════════════════════════════════════════════════════════
        //  CORE LOAD — the single point that can fail, fully swallowed
        // ═══════════════════════════════════════════════════════════════

        private static async Task<List<GlassSheet>> LoadAllAsync()
        {
            try
            {
                var repo = new ProGlassAutomation.Services.SheetStore.SheetStoreRepository();
                var all = await repo.GetAllAsync().ConfigureAwait(false);
                return all ?? new List<GlassSheet>();
            }
            catch (Exception ex)
            {
                // Sheet Store unavailable → the wizard simply gets no hints.
                AppLogService.Debug(
                    $"[SheetStorePricingService] Sheet Store read failed: {ex.Message}");
                return new List<GlassSheet>();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PUBLIC LOOKUPS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Full store snapshot for linkage consumers (Step 6 auto-use).
        /// Same never-throw path as every other lookup. Empty on failure.
        /// </summary>
        public static Task<List<GlassSheet>> GetAllSheetsAsync() =>
            LoadAllAsync();

        /// <summary>
        /// UI-thread marshaller for ObservableCollection mutations. The
        /// wizard fires store loads via SafeFireAndForget, so continuations
        /// run on the thread pool — WPF CollectionView throws when the
        /// SOURCE collection changes off-dispatcher, therefore every
        /// list swap must land on the UI thread. (Cross-check hardening,
        /// 2026-09-10: also covers the stock-ledger refresh path.)
        /// </summary>
        internal static void PostToUi(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) action();
            else dispatcher.BeginInvoke(action);
        }

        /// <summary>
        /// REPLACES the target combo list with exactly the Sheet Store
        /// colours (2026-09-10: built-in defaults removed by request —
        /// the dropdown must offer only what inventory actually stocks).
        /// Single shared mechanism for SGU / DGU / LAM / DGU+LAM.
        /// Never throws; leaves the list empty when the store is empty.
        /// Marshalled to the UI thread (see <see cref="PostToUi"/>).
        /// </summary>
        public static async Task LoadColorsIntoAsync(
            ObservableCollection<string> target)
        {
            if (target == null) return;
            var colors = await GetColorsAsync().ConfigureAwait(true);
            PostToUi(() =>
            {
                target.Clear();
                foreach (var color in colors) target.Add(color);
            });
        }

        /// <summary>
        /// Distinct glass colors present in the Sheet Store, in the
        /// original casing of the first row that used them, alphabetical.
        /// Empty list when the store is empty or unreadable.
        /// </summary>
        public static async Task<List<string>> GetColorsAsync()
        {
            var sheets = await LoadAllAsync().ConfigureAwait(false);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>();

            foreach (var s in sheets)
            {
                var color = (s?.Color ?? string.Empty).Trim();
                if (color.Length == 0) continue;
                if (!seen.Add(Norm(color))) continue;
                result.Add(color);
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>
        /// Distinct thicknesses (mm) stocked for a given color, ascending.
        /// Blank/unknown color → empty list.
        /// </summary>
        public static async Task<List<int>> GetThicknessesAsync(string? color)
        {
            var key = Norm(color);
            if (key.Length == 0) return new List<int>();

            var sheets = await LoadAllAsync().ConfigureAwait(false);

            return sheets
                .Where(s => s != null && Norm(s.Color) == key && s.ThicknessMm > 0)
                .Select(s => s.ThicknessMm)
                .Distinct()
                .OrderBy(t => t)
                .ToList();
        }

        /// <summary>
        /// Sheet sizes stocked for a color + thickness combination.
        /// Display is the human label ("3210 × 2250"), Sqm the area of one
        /// sheet. Duplicates (same W×H from several suppliers) collapse to
        /// one entry. Largest sheet first — the usual cutting preference.
        /// </summary>
        public static async Task<List<(int W, int H, string Display, decimal Sqm)>>
            GetSizesAsync(string? color, int thicknessMm)
        {
            var result = new List<(int W, int H, string Display, decimal Sqm)>();

            var key = Norm(color);
            if (key.Length == 0 || thicknessMm <= 0) return result;

            var sheets = await LoadAllAsync().ConfigureAwait(false);

            var seen = new HashSet<(int, int)>();

            foreach (var s in sheets
                         .Where(s => s != null
                                     && Norm(s.Color) == key
                                     && s.ThicknessMm == thicknessMm
                                     && s.WidthMm > 0
                                     && s.HeightMm > 0)
                         .OrderByDescending(s => s.WidthMm * (long)s.HeightMm))
            {
                if (!seen.Add((s.WidthMm, s.HeightMm))) continue;

                result.Add((
                    s.WidthMm,
                    s.HeightMm,
                    $"{s.WidthMm} × {s.HeightMm}",
                    Utilities.SqmCalculator.CalculateSheetArea(s.WidthMm, s.HeightMm)));
            }

            return result;
        }

        /// <summary>
        /// Best available price for a color + thickness.
        ///
        /// Selection rule: the cheapest positive purchase price wins (that
        /// is the sheet a buyer would actually use for costing). Rows with
        /// no purchase price are only used as a last resort so a partially
        /// filled inventory still returns a sell price.
        ///
        /// Returns null when nothing matches — callers treat null as
        /// "not in Sheet Store, type it manually".
        /// </summary>
        public static async Task<(decimal PurchasePerSqm, decimal SellPerSqm, string Supplier)?>
            TryGetPriceAsync(string? color, int thicknessMm)
        {
            var key = Norm(color);
            if (key.Length == 0 || thicknessMm <= 0) return null;

            var sheets = await LoadAllAsync().ConfigureAwait(false);

            var candidates = sheets
                .Where(s => s != null
                            && Norm(s.Color) == key
                            && s.ThicknessMm == thicknessMm)
                .ToList();

            if (candidates.Count == 0) return null;

            var priced = candidates
                .Where(s => s.PurchasePricePerSqm > 0m)
                .OrderBy(s => s.PurchasePricePerSqm)
                .FirstOrDefault();

            var pick = priced ?? candidates[0];

            return (pick.PurchasePricePerSqm,
                    pick.SellPricePerSqm,
                    pick.Supplier ?? string.Empty);
        }

        /// <summary>
        /// Strict price for one selected stock sheet: exact colour + exact
        /// thickness + exact width/height (orientation-insensitive). Returns
        /// null when that physical sheet row is not present. This is the
        /// safe lookup for Step 4/Step 9 costing because a selected
        /// 2500 × 1875 pane must never be priced from a 3210 × 2250 row.
        /// </summary>
        public static async Task<(decimal PurchasePerSqm, decimal SellPerSqm, string Supplier)?>
            TryGetExactPriceForSizeAsync(string? color, int thicknessMm, int widthMm, int heightMm)
        {
            var key = Norm(color);
            if (key.Length == 0 || thicknessMm <= 0 || widthMm <= 0 || heightMm <= 0)
                return null;

            var sheets = await LoadAllAsync().ConfigureAwait(false);

            var exact = sheets
                .Where(s => s != null
                            && Norm(s.Color) == key
                            && s.ThicknessMm == thicknessMm
                            && ((s.WidthMm == widthMm && s.HeightMm == heightMm)
                             || (s.WidthMm == heightMm && s.HeightMm == widthMm)))
                .OrderBy(s => s.PurchasePricePerSqm > 0m ? 0 : 1)
                .ThenBy(s => s.PurchasePricePerSqm)
                .FirstOrDefault();

            return exact == null
                ? null
                : (exact.PurchasePricePerSqm,
                   exact.SellPricePerSqm,
                   exact.Supplier ?? string.Empty);
        }

        /// <summary>
        /// Price for an EXACT stock sheet: colour + thickness + width +
        /// height.
        ///
        /// Why this exists: the same colour and thickness is often stocked
        /// in more than one size at different rates (a 3210x2250 jumbo and
        /// a 2440x1830 standard are not the same money). Step 4 lets the
        /// user pick the size, so the base price must follow THAT sheet.
        /// The colour+thickness overload cannot express this — it returns
        /// the cheapest row regardless of size.
        ///
        /// Falls back to the cheapest positive purchase price for the
        /// colour+thickness when the exact size is not stocked, so a typed
        /// custom size still yields a sensible rate. Returns null when
        /// nothing matches at all.
        /// </summary>
        public static async Task<(decimal PurchasePerSqm, decimal SellPerSqm, string Supplier)?>
            TryGetPriceForSizeAsync(string? color, int thicknessMm, int widthMm, int heightMm)
        {
            var key = Norm(color);
            if (key.Length == 0 || thicknessMm <= 0) return null;

            var sheets = await LoadAllAsync().ConfigureAwait(false);

            var sameGlass = sheets
                .Where(s => s != null
                            && Norm(s.Color) == key
                            && s.ThicknessMm == thicknessMm)
                .ToList();

            if (sameGlass.Count == 0) return null;

            if (widthMm > 0 && heightMm > 0)
            {
                // Match the size in either orientation — a 3210x2250 sheet
                // and a 2250x3210 entry are the same physical stock.
                var exact = sameGlass
                    .Where(s => (s.WidthMm == widthMm && s.HeightMm == heightMm)
                             || (s.WidthMm == heightMm && s.HeightMm == widthMm))
                    .OrderBy(s => s.PurchasePricePerSqm > 0m ? 0 : 1)
                    .ThenBy(s => s.PurchasePricePerSqm)
                    .FirstOrDefault();

                if (exact != null)
                {
                    return (exact.PurchasePricePerSqm,
                            exact.SellPricePerSqm,
                            exact.Supplier ?? string.Empty);
                }
            }

            var priced = sameGlass
                .Where(s => s.PurchasePricePerSqm > 0m)
                .OrderBy(s => s.PurchasePricePerSqm)
                .FirstOrDefault();

            var pick = priced ?? sameGlass[0];

            return (pick.PurchasePricePerSqm,
                    pick.SellPricePerSqm,
                    pick.Supplier ?? string.Empty);
        }

        /// <summary>
        /// Convenience overload accepting the UI thickness string
        /// ("6mm"). Delegates to <see cref="TryGetPriceAsync(string, int)"/>.
        /// </summary>
        public static Task<(decimal PurchasePerSqm, decimal SellPerSqm, string Supplier)?>
            TryGetPriceAsync(string? color, string? thicknessText) =>
            TryGetPriceAsync(color, ParseMm(thicknessText));

        /// <summary>
        /// Convenience overload accepting the UI thickness string ("6mm").
        /// </summary>
        public static Task<List<(int W, int H, string Display, decimal Sqm)>>
            GetSizesAsync(string? color, string? thicknessText) =>
            GetSizesAsync(color, ParseMm(thicknessText));

        // ═══════════════════════════════════════════
        //  SYNC CROSS-CHECK — for callers that cannot await (2026-09-17)
        // ═══════════════════════════════════════════
        private static readonly object _snapshotGate = new object();
        private static List<GlassSheet>? _snapshot;
        private static DateTime _snapshotAtUtc;
        /// <summary>How long a snapshot is trusted before it is refreshed in the
        /// background. Short, because the Sheet Store changes while a quote is
        /// open; the stale copy still answers the current call so nobody
        /// waits.</summary>
        private const int SnapshotTrustSeconds = 60;

        /// <summary>
        /// Warms the snapshot the sync lookups read. Fire-and-forget and
        /// non-throwing: call it when a screen opens, not when a button is
        /// clicked, so the first click already has inventory to cross-check.
        /// </summary>
        public static void PrimeSyncSnapshot()
        {
            _ = RefreshSyncSnapshotAsync();
        }

        /// <summary>Re-reads the store into the sync snapshot. Never throws.</summary>
        public static async Task RefreshSyncSnapshotAsync()
        {
            var all = await LoadAllAsync().ConfigureAwait(false);
            lock (_snapshotGate)
            {
                _snapshot = all;
                _snapshotAtUtc = DateTime.UtcNow;
            }
        }

        private static bool TryGetSnapshot(out List<GlassSheet> snapshot)
        {
            snapshot = new List<GlassSheet>();
            try
            {
                bool stale;
                lock (_snapshotGate)
                {
                    // Local copy first: it keeps the null check and the use under
                    // one flow analysis, so the non-nullable out parameter never
                    // sees the nullable field (CS8601 under warnings-as-errors).
                    var current = _snapshot;
                    if (current == null)
                    {
                        // Nothing primed yet: ask for it now so the NEXT call has
                        // data and report "unknown" for this one. Guessing a sheet
                        // size would be worse than admitting we do not have one.
                        PrimeSyncSnapshot();
                        return false;
                    }
                    snapshot = current;
                    stale = (DateTime.UtcNow - _snapshotAtUtc).TotalSeconds > SnapshotTrustSeconds;
                }
                if (stale) PrimeSyncSnapshot();
                return true;
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[SheetStorePricingService] snapshot read failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Largest sheet the store stocks for a colour, preferring an exact
        /// thickness match and falling back to any thickness of that colour.
        /// Sync and non-blocking (see the header note): false means "the store
        /// cannot say", never "the store is empty".
        /// SENIOR FIX 2026-09-20: previously returned false when snapshot not
        /// primed, causing first optimization run to have zero stocks for
        /// DGU+LAM and any spec with zero Step4 sheet. Now attempts direct DB
        /// load synchronously as fallback, so first run still gets a rack.
        /// </summary>
        public static bool TryResolveStockSheet(
            string? color, int thicknessMm, out int widthMm, out int heightMm)
        {
            widthMm = 0;
            heightMm = 0;
            var key = Norm(color);
            if (key.Length == 0) return false;
            List<GlassSheet> snapshot;
            if (!TryGetSnapshot(out snapshot))
            {
                try
                {
                    var repo = new ProGlassAutomation.Services.SheetStore.SheetStoreRepository();
                    var task = repo.GetAllAsync();
                    if (!task.Wait(2000)) return false;
                    snapshot = task.Result ?? new List<GlassSheet>();
                    // Warm cache for next calls
                    lock (_snapshotGate)
                    {
                        _snapshot = snapshot;
                        _snapshotAtUtc = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    AppLogService.Debug($"[SheetStorePricingService] fallback load failed: {ex.Message}");
                    return false;
                }
            }
            try
            {
                return TryPickStockSheet(
                    snapshot, color, thicknessMm, requirePositiveBalance: false,
                    out widthMm, out heightMm, out _);
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[SheetStorePricingService] resolve failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// True when the store stocks this exact colour + size (thickness ignored
        /// when 0). Used to CROSS-CHECK a size Step 4 supplied: the answer never
        /// overrides Step 4, it only lets the caller tell the operator that the
        /// rack on screen is not one inventory holds. False also covers "the
        /// store cannot say right now", so callers must treat it as a note, never
        /// as an error.
        /// SENIOR FIX 2026-09-20: fallback to direct DB load when snapshot cold,
        /// so cross-check note does not lie on first run.
        /// </summary>
        public static bool IsStockedSize(string? color, int thicknessMm, int widthMm, int heightMm)
        {
            var key = Norm(color);
            if (key.Length == 0 || widthMm <= 0 || heightMm <= 0) return false;
            List<GlassSheet> snapshot;
            if (!TryGetSnapshot(out snapshot))
            {
                try
                {
                    var repo = new ProGlassAutomation.Services.SheetStore.SheetStoreRepository();
                    var task = repo.GetAllAsync();
                    if (!task.Wait(1500)) return false;
                    snapshot = task.Result ?? new List<GlassSheet>();
                }
                catch { return false; }
            }
            try
            {
                if (thicknessMm <= 0) return false;
                return snapshot.Any(s => s != null
                    && Norm(s.Color) == key
                    && s.ThicknessMm == thicknessMm
                    && ((s.WidthMm == widthMm && s.HeightMm == heightMm)
                        || (s.WidthMm == heightMm && s.HeightMm == widthMm)));
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[SheetStorePricingService] stocked-size check failed: {ex.Message}");
                return false;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  PHASE 10 — the Sheet Store BALANCE, which until now never reached
        //  the optimization engine at all.
        //
        //  Cross-check of every consumer of GlassSheet inventory:
        //
        //    ProductionOptimizationBridge   .Where(g => g.Balance > 0),
        //                                   MaxSheets = gs.Balance      ✓
        //    StockSelectionService          .Where(g => g.Balance > 0),
        //                                   less reserved and allocated ✓
        //    SheetStorePricingService       no Balance filter, and
        //      .TryResolveStockSheet        Balance discarded — only
        //                                   WidthMm/HeightMm returned   ✗
        //
        //  The Optimization module was the ONE consumer that ignored stock.
        //  It resolved a rack SIZE from inventory, then AddStep5Rack built the
        //  row without an Available value, so StockEntryVm's default of 99999999
        //  applied and the engine planned against INFINITE glass — including
        //  glass the store reports as Balance 0, because nothing filtered on it.
        //  Sheet counts, utilization and cost for that glass flowed on to Step 6
        //  and Step 9.
        //
        //  GlassSheet.Balance is `Stock - Used`, real inventory arithmetic, so 0
        //  genuinely means none left.
        //
        //  These methods are declared AFTER the two original lookups on purpose.
        //  Step5ProgressAndViewResultTests slices this file from the first
        //  IndexOf of each lookup's declaration text, so an overload placed ABOVE
        //  the original would silently retarget that assertion at different code.
        //  The declaration strings are spelled out here in prose rather than as
        //  literals for the same reason: a comment containing the exact marker
        //  would become a match candidate if the file were ever reordered.
        //
        //  CONTRACT, same as the two lookups above: sync, cache-first with a
        //  BOUNDED database fallback, never throws, and false means "the store
        //  cannot say" — never "the store is empty". Callers must therefore
        //  leave a balance alone on false rather than writing 0, because 0 in
        //  StockSize.MaxSheets is the UnlimitedSheets sentinel and would mean
        //  infinite glass.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Snapshot reader shared by the balance lookups. Cache first; on a cold
        /// or stale cache, one bounded synchronous load. The bound is the point:
        /// <c>RunFromStep5</c> builds racks on the UI thread, so an unbounded wait
        /// would freeze the window, while no fallback at all was what made a first
        /// run come back with zero racks (the 2026-09-20 senior fix).
        /// </summary>
        private static bool TryGetSnapshotWithBoundedFallback(
            out List<GlassSheet> snapshot, int timeoutMs)
        {
            if (TryGetSnapshot(out snapshot)) return true;

            snapshot = new List<GlassSheet>();
            try
            {
                var repo = new ProGlassAutomation.Services.SheetStore.SheetStoreRepository();
                var task = repo.GetAllAsync();
                if (!task.Wait(timeoutMs)) return false;

                var loaded = task.Result;
                if (loaded == null) return false;
                snapshot = loaded;

                lock (_snapshotGate)
                {
                    _snapshot = loaded;
                    _snapshotAtUtc = DateTime.UtcNow;
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[SheetStorePricingService] bounded snapshot load failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sums the Sheet Store balance for a colour + size, matching either
        /// orientation. Returns false when the store cannot answer or holds no
        /// such row, so the caller can leave the rack's availability untouched
        /// instead of guessing.
        /// </summary>
        public static bool TryGetStockBalance(
            string? color, int thicknessMm, int widthMm, int heightMm, out int balance)
        {
            balance = 0;
            var key = Norm(color);
            if (key.Length == 0 || widthMm <= 0 || heightMm <= 0) return false;

            if (!TryGetSnapshotWithBoundedFallback(out var snapshot, 1500)) return false;

            try
            {
                var matches = snapshot
                    .Where(s => s != null
                                && Norm(s.Color) == key
                                && (thicknessMm <= 0 || s.ThicknessMm == thicknessMm)
                                && ((s.WidthMm == widthMm && s.HeightMm == heightMm)
                                    || (s.WidthMm == heightMm && s.HeightMm == widthMm)))
                    .ToList();

                if (matches.Count == 0) return false;

                // Summed in long and clamped, matching DistinctBySize: several
                // inventory rows at one size must not be able to overflow into a
                // negative balance, which the packer would read as exhausted.
                long total = 0;
                foreach (var m in matches) total += m.Balance;
                balance = total >= int.MaxValue ? int.MaxValue : (int)total;
                return true;
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[SheetStorePricingService] balance lookup failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Balance for a size when — and ONLY when — inventory can attribute it to
        /// one kind of glass without being told the colour.
        ///
        /// PHASE 11. The JOBS-tab Step 4 import builds racks from
        /// <c>Step4Sheets</c>, whose tuple carries no colour, so it cannot call
        /// <see cref="TryGetStockBalance"/>. Looking a balance up on size alone
        /// would merge a Clear and a Low-E rack at the same dimensions — the exact
        /// double-count <c>RefreshStockUsage</c>'s header comment exists to prevent
        /// — and hand a run a number belonging to glass it is not cutting. A wrong
        /// balance is worse than no balance, because it refuses groups that do have
        /// stock.
        ///
        /// So this answers only the cases that are certain. It matches on size and
        /// thickness and collects the distinct colours inventory holds there:
        /// exactly one colour means the rack cannot be anything else, and the
        /// balance is safe to apply. Zero or two-plus colours means the caller
        /// cannot be told, and it gets false — leaving the rack on its visible
        /// "effectively unlimited" default for the operator to correct.
        /// </summary>
        public static bool TryGetUnambiguousStockBalance(
            int thicknessMm, int widthMm, int heightMm, out int balance)
        {
            balance = 0;
            if (thicknessMm <= 0 || widthMm <= 0 || heightMm <= 0) return false;

            if (!TryGetSnapshotWithBoundedFallback(out var snapshot, 1500)) return false;

            try
            {
                var matches = snapshot
                    .Where(s => s != null
                                && s.WidthMm > 0 && s.HeightMm > 0
                                && s.ThicknessMm == thicknessMm
                                && ((s.WidthMm == widthMm && s.HeightMm == heightMm)
                                    || (s.WidthMm == heightMm && s.HeightMm == widthMm)))
                    .ToList();

                if (matches.Count == 0) return false;

                // More than one colour at this size and thickness: attribution
                // would be a guess, and a guessed balance is the unsafe answer.
                var colours = matches.Select(m => Norm(m.Color)).Distinct().ToList();
                if (colours.Count != 1) return false;

                long total = 0;
                foreach (var m in matches) total += m.Balance;
                balance = total >= int.MaxValue ? int.MaxValue : (int)total;
                return true;
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[SheetStorePricingService] unambiguous balance lookup failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Balance-aware sibling of <see cref="TryResolveStockSheet(string, int, out int, out int)"/>:
        /// the largest sheet the store stocks for a colour that is ACTUALLY IN
        /// STOCK, plus how many are on hand.
        ///
        /// The 4-argument overload is deliberately left exactly as it was. It
        /// answers a different question — "does inventory hold this size at all?",
        /// used only to write a cross-check NOTE — and it is pinned by
        /// Step5ProgressAndViewResultTests. This one decides which rack a run
        /// cuts against, so a rack with nothing on it must lose to a smaller rack
        /// that has glass. When every rack of that colour is at zero the answer is
        /// false, and the caller's existing honest refusal runs.
        /// </summary>
        public static bool TryResolveStockSheet(
            string? color, int thicknessMm, out int widthMm, out int heightMm, out int balance)
        {
            widthMm = 0;
            heightMm = 0;
            balance = 0;

            var key = Norm(color);
            if (key.Length == 0) return false;

            if (!TryGetSnapshotWithBoundedFallback(out var snapshot, 2000)) return false;

            try
            {
                return TryPickStockSheet(
                    snapshot, color, thicknessMm, requirePositiveBalance: true,
                    out widthMm, out heightMm, out balance);
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[SheetStorePricingService] balance-aware resolve failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Material-aware stock pick: exact colour + exact thickness. Never
        /// falls back to another thickness, another colour, or the globally
        /// largest sheet. Thickness 0 is a missing identity, not a wildcard.
        /// </summary>
        public static bool TryPickStockSheet(
            IReadOnlyList<GlassSheet>? snapshot,
            string? color,
            int thicknessMm,
            bool requirePositiveBalance,
            out int widthMm,
            out int heightMm,
            out int balance)
        {
            widthMm = 0;
            heightMm = 0;
            balance = 0;

            var key = Norm(color);
            if (key.Length == 0 || thicknessMm <= 0) return false;
            if (snapshot == null || snapshot.Count == 0) return false;

            try
            {
                var candidates = snapshot
                    .Where(s => s != null
                                && Norm(s.Color) == key
                                && s.ThicknessMm == thicknessMm
                                && s.WidthMm > 0
                                && s.HeightMm > 0
                                && (!requirePositiveBalance || s.Balance > 0))
                    .OrderByDescending(s => (long)s.WidthMm * s.HeightMm)
                    .ToList();

                if (candidates.Count == 0) return false;

                var pick = candidates[0];
                long total = 0;
                foreach (var c in candidates)
                    if (c.WidthMm == pick.WidthMm && c.HeightMm == pick.HeightMm)
                        total += c.Balance;

                widthMm = pick.WidthMm;
                heightMm = pick.HeightMm;
                balance = total >= int.MaxValue ? int.MaxValue : (int)total;
                return true;
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[SheetStorePricingService] pick failed: {ex.Message}");
                return false;
            }
        }
    }
}
