// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step4PricingEstimator/SguParameters/
//         SguParametersViewModel.SheetStore.cs
// PROJECT: ProGlassAutomation
// MODULE: Step 4 — SGU parameters ↔ Sheet Store adapter
//
// ADDITIVE PARTIAL. SguParametersViewModel.cs is untouched except for the
// setter hook (OnGlassChanged) and the ObservableCollection option lists.
//
// WHAT THIS PROVIDES
//   • Sheet WIDTH and HEIGHT as two separate numeric fields, auto-filled
//     from Sheet Store as soon as a colour and thickness are chosen.
//   • A size list, so a colour/thickness stocked in more than one size
//     (3210×2250 vs 3660×2440) can be switched.
//   • BasePricePerSqm auto-filled from the Sheet Store SELLING price of
//     the exact sheet selected.
//   • SheetStoreHint — one skippable warning line.
//
// The user may overwrite width, height and base price by hand; auto-fill
// only writes when the glass selection or the sheet size changes.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.ProformaInvoice;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.SguParameters
{
    public partial class SguParametersViewModel
    {
        /// <summary>Shown when the colour/thickness pair is not stocked.</summary>
        public const string NotInStoreHint =
            "This glass is not in Sheet Store — colour, thickness, sheet size and a store price must resolve before optimization can use it.";

        private bool _isAutoFilling;
        private bool _suspendSheetStoreRefresh;
        private int _sheetStoreEpoch;

        // ═══════════════════════════════════════════════════════════════
        //  SHEET SIZE — WIDTH / HEIGHT
        // ═══════════════════════════════════════════════════════════════

        private int _sheetWidthMm;
        /// <summary>
        /// Stock sheet width in mm. Auto-filled from Sheet Store for the
        /// chosen colour + thickness; editable for a custom sheet.
        /// </summary>
        public int SheetWidthMm
        {
            get => _sheetWidthMm;
            set
            {
                if (_sheetWidthMm == value) return;
                _sheetWidthMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SheetDisplay));
                if (!_isAutoFilling && !_suspendSheetStoreRefresh)
                    FireAndForget(RepriceAsync);
            }
        }

        private int _sheetHeightMm;
        /// <summary>Stock sheet height in mm. See <see cref="SheetWidthMm"/>.</summary>
        public int SheetHeightMm
        {
            get => _sheetHeightMm;
            set
            {
                if (_sheetHeightMm == value) return;
                _sheetHeightMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SheetDisplay));
                if (!_isAutoFilling && !_suspendSheetStoreRefresh)
                    FireAndForget(RepriceAsync);
            }
        }

        /// <summary>"3210 × 2250", or empty when no sheet is set.</summary>
        public string SheetDisplay =>
            (SheetWidthMm > 0 && SheetHeightMm > 0)
                ? $"{SheetWidthMm} × {SheetHeightMm}"
                : "";

        // ═══════════════════════════════════════════════════════════════
        //  AVAILABLE SIZES
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Sizes stocked for the selected colour + thickness.</summary>
        public ObservableCollection<string> SheetSizeOptions { get; } = new();

        private string _selectedSheetSize = "";
        /// <summary>
        /// Picking a size writes <see cref="SheetWidthMm"/> and
        /// <see cref="SheetHeightMm"/>, which in turn reprices.
        /// </summary>
        public string SelectedSheetSize
        {
            get => _selectedSheetSize;
            set
            {
                if (_selectedSheetSize == value) return;
                _selectedSheetSize = value ?? "";
                OnPropertyChanged();

                // Manual sheet choice wins over any in-flight colour/thickness
                // refresh. Without this, a delayed store refresh can reset the
                // user's chosen sheet back to the largest/default size.
                _sheetStoreEpoch++;
                var (w, h) = SpecSheetRegistry.ParseSize(_selectedSheetSize);
                if (w <= 0 || h <= 0) return;

                _isAutoFilling = true;
                SheetWidthMm = w;
                SheetHeightMm = h;
                _isAutoFilling = false;

                FireAndForget(RepriceAsync);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HINT
        // ═══════════════════════════════════════════════════════════════

        private string _sheetStoreHint = "";
        /// <summary>Skippable guidance line; empty when the glass resolved.</summary>
        public string SheetStoreHint
        {
            get => _sheetStoreHint;
            private set
            {
                if (_sheetStoreHint == value) return;
                _sheetStoreHint = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSheetStoreHint));
            }
        }

        /// <summary>True when <see cref="SheetStoreHint"/> has content.</summary>
        public bool HasSheetStoreHint => !string.IsNullOrWhiteSpace(SheetStoreHint);

        private bool _panePriceResolvedFromStore;
        /// <summary>
        /// True only after an exact Sheet Store lookup returned a positive
        /// selling or purchase price. The constructor seed (46) is not this.
        /// </summary>
        public bool PanePriceResolvedFromStore => _panePriceResolvedFromStore;

        internal void SetPanePriceResolvedFromStore(bool resolved) =>
            _panePriceResolvedFromStore = resolved;

        // ═══════════════════════════════════════════════════════════════
        //  LOAD / REFRESH
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Refills <c>ColorOptions</c> from the Sheet Store ONLY (no
        /// built-in defaults since 2026-09-10) and refreshes sizes and
        /// price. Idempotent.
        /// </summary>
        public async Task LoadSheetStoreAsync()
        {
            try
            {
                await SheetStorePricingService
                    .LoadColorsIntoAsync(ColorOptions)
                    .ConfigureAwait(true);

                await RefreshFromStoreAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLogService.Error(
                    "[SguParametersViewModel] Sheet-store load failed", ex);
            }
        }

        /// <summary>Setter hook — thickness/colour changed.</summary>
        private void OnGlassChanged()
        {
            if (_suspendSheetStoreRefresh) return;
            // Invalidate any refresh that was already in flight for the
            // PREVIOUS colour/thickness. Without this bump a slow store read
            // for the old selection can finish after the new one and rewrite
            // the size list / sheet W-H / base price back to the stale
            // values — the "Step 4 not syncing with Sheet Store" report.
            _sheetStoreEpoch++;
            FireAndForget(RefreshFromStoreAsync);
        }

        // Thin wrapper over the shared AsyncUtility.SafeFireAndForget so the
        // per-file log prefix stays here while the try/catch + logging logic
        // lives in one place. Pricing hints must never surface as a crash
        // dialog, but a failure must still be recorded in Release.
        internal void RunWithoutSheetStoreRefresh(Action work)
        {
            _sheetStoreEpoch++;
            _suspendSheetStoreRefresh = true;
            try { work?.Invoke(); }
            finally { _suspendSheetStoreRefresh = false; }
        }

        private static void FireAndForget(Func<Task> work)
            => Utilities.AsyncUtility.SafeFireAndForget(
                work, "[SguParametersViewModel] sheet-store refresh failed");

        /// <summary>
        /// Colour or thickness changed: reload the size list, snap W/H to
        /// the largest stocked sheet, and reprice.
        /// </summary>
        private async Task RefreshFromStoreAsync()
        {
            int epoch = _sheetStoreEpoch;
            int mm = SheetStorePricingService.ParseMm(SelectedThickness);

            var sizes = await SheetStorePricingService
                .GetSizesAsync(GlassColor, mm).ConfigureAwait(true);

            if (epoch != _sheetStoreEpoch || _suspendSheetStoreRefresh) return;

            SheetStorePricingService.PostToUi(() =>
            {
                SheetSizeOptions.Clear();
                foreach (var sz in sizes) SheetSizeOptions.Add(sz.Display);
            });

            _isAutoFilling = true;
            if (sizes.Count > 0)
            {
                // Largest-area first — the jumbo sheet is the sensible
                // default to quote against.
                SheetWidthMm = sizes[0].W;
                SheetHeightMm = sizes[0].H;
                _selectedSheetSize = sizes[0].Display;
                OnPropertyChanged(nameof(SelectedSheetSize));
            }
            _isAutoFilling = false;

            await RepriceAsync(epoch).ConfigureAwait(true);
        }

        /// <summary>
        /// Writes BasePricePerSqm from the Sheet Store SELLING price of the
        /// exact sheet (colour + thickness + W + H) currently selected.
        /// </summary>
        private Task RepriceAsync() => RepriceAsync(-1);

        private async Task RepriceAsync(int expectedEpoch)
        {
            int epoch = expectedEpoch >= 0 ? expectedEpoch : _sheetStoreEpoch;
            int mm = SheetStorePricingService.ParseMm(SelectedThickness);

            var price = await SheetStorePricingService
                .TryGetExactPriceForSizeAsync(GlassColor, mm, SheetWidthMm, SheetHeightMm)
                .ConfigureAwait(true);

            if (epoch != _sheetStoreEpoch || _suspendSheetStoreRefresh) return;

            decimal? resolved = Step4MaterialIntegrity.ResolveStorePrice(price);
            _panePriceResolvedFromStore = resolved.HasValue;

            if (resolved.HasValue)
            {
                BasePricePerSqm = resolved.Value;
                SheetStoreHint = "";
            }
            else
            {
                // PHASE A: a miss is invalid. Do not write 0 and do not
                // treat the leftover constructor default (46) as a store price.
                SheetStoreHint = Step4MaterialIntegrity.DescribeMiss(
                    "SGU", GlassColor, mm, SheetWidthMm, SheetHeightMm);
            }
        }
    }
}
