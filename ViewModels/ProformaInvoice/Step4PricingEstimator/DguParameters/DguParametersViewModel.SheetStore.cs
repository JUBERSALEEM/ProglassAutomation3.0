// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step4PricingEstimator/DguParameters/
//         DguParametersViewModel.SheetStore.cs
// PROJECT: ProGlassAutomation
// MODULE: Step 4 — DGU parameters ↔ Sheet Store adapter
//
// ADDITIVE PARTIAL. The original DguParametersViewModel.cs is untouched
// except for the setter hooks (OnOuterGlassChanged / OnInnerGlassChanged)
// and the ObservableCollection option lists.
//
// WHAT THIS PROVIDES
//   • Outer/Inner sheet WIDTH and HEIGHT as two separate numeric fields
//     per side, auto-filled from Sheet Store the moment a colour and a
//     thickness are chosen.
//   • Size choice list per side, so a colour/thickness stocked in more
//     than one size (3210×2250 vs 3660×2440) can be switched.
//   • OuterBasePrice / InnerBasePrice auto-filled from the Sheet Store
//     SELLING price of the exact sheet selected.
//   • SheetStoreHint — one skippable warning line.
//
// DESIGN RULES
//   • The user can still overwrite W, H and base price by hand. Auto-fill
//     never fights a manual edit: it only writes when the glass selection
//     or the sheet size actually changes.
//   • Refresh is fire-and-forget (setters cannot await) and each pass is
//     wrapped so a Sheet Store failure can never surface as a crash.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.ProformaInvoice;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguParameters
{
    public partial class DguParametersViewModel
    {
        /// <summary>Shown when a colour/thickness pair is not stocked.</summary>
        public const string NotInStoreHint =
            "This glass is not in Sheet Store — colour, thickness, sheet size and a store price must resolve before optimization can use it.";

        // Guards: auto-fill must not re-enter through its own setters.
        private bool _isAutoFillingOuter;
        private bool _isAutoFillingInner;
        private bool _suspendSheetStoreRefresh;
        private int _sheetStoreEpoch;

        // ═══════════════════════════════════════════════════════════════
        //  SHEET SIZE — WIDTH / HEIGHT PER SIDE
        // ═══════════════════════════════════════════════════════════════

        private int _outerSheetWidthMm;
        /// <summary>
        /// OUTER stock sheet width in mm. Auto-filled from Sheet Store for
        /// the chosen outer colour + thickness; editable for a custom sheet.
        /// </summary>
        public int OuterSheetWidthMm
        {
            get => _outerSheetWidthMm;
            set
            {
                if (_outerSheetWidthMm == value) return;
                _outerSheetWidthMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OuterSheetDisplay));
                if (!_isAutoFillingOuter && !_suspendSheetStoreRefresh)
                    FireAndForget(RepriceOuterAsync);
            }
        }

        private int _outerSheetHeightMm;
        /// <summary>OUTER stock sheet height in mm. See <see cref="OuterSheetWidthMm"/>.</summary>
        public int OuterSheetHeightMm
        {
            get => _outerSheetHeightMm;
            set
            {
                if (_outerSheetHeightMm == value) return;
                _outerSheetHeightMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(OuterSheetDisplay));
                if (!_isAutoFillingOuter && !_suspendSheetStoreRefresh)
                    FireAndForget(RepriceOuterAsync);
            }
        }

        private int _innerSheetWidthMm;
        /// <summary>INNER stock sheet width in mm.</summary>
        public int InnerSheetWidthMm
        {
            get => _innerSheetWidthMm;
            set
            {
                if (_innerSheetWidthMm == value) return;
                _innerSheetWidthMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InnerSheetDisplay));
                if (!_isAutoFillingInner && !_suspendSheetStoreRefresh)
                    FireAndForget(RepriceInnerAsync);
            }
        }

        private int _innerSheetHeightMm;
        /// <summary>INNER stock sheet height in mm.</summary>
        public int InnerSheetHeightMm
        {
            get => _innerSheetHeightMm;
            set
            {
                if (_innerSheetHeightMm == value) return;
                _innerSheetHeightMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InnerSheetDisplay));
                if (!_isAutoFillingInner && !_suspendSheetStoreRefresh)
                    FireAndForget(RepriceInnerAsync);
            }
        }

        /// <summary>"3210 × 2250", or empty when no sheet is set.</summary>
        public string OuterSheetDisplay =>
            (OuterSheetWidthMm > 0 && OuterSheetHeightMm > 0)
                ? $"{OuterSheetWidthMm} × {OuterSheetHeightMm}"
                : "";

        /// <summary>"3210 × 2250", or empty when no sheet is set.</summary>
        public string InnerSheetDisplay =>
            (InnerSheetWidthMm > 0 && InnerSheetHeightMm > 0)
                ? $"{InnerSheetWidthMm} × {InnerSheetHeightMm}"
                : "";

        // ═══════════════════════════════════════════════════════════════
        //  AVAILABLE SIZES (a colour/thickness may be stocked in several)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Sizes stocked for the OUTER colour + thickness.</summary>
        public ObservableCollection<string> OuterSheetSizeOptions { get; } = new();

        /// <summary>Sizes stocked for the INNER colour + thickness.</summary>
        public ObservableCollection<string> InnerSheetSizeOptions { get; } = new();

        private string _selectedOuterSheetSize = "";
        /// <summary>
        /// Picking a size here writes <see cref="OuterSheetWidthMm"/> and
        /// <see cref="OuterSheetHeightMm"/>, which in turn reprices.
        /// </summary>
        public string SelectedOuterSheetSize
        {
            get => _selectedOuterSheetSize;
            set
            {
                if (_selectedOuterSheetSize == value) return;
                _selectedOuterSheetSize = value ?? "";
                OnPropertyChanged();
                ApplySizeText(_selectedOuterSheetSize, isInner: false);
            }
        }

        private string _selectedInnerSheetSize = "";
        /// <summary>See <see cref="SelectedOuterSheetSize"/>.</summary>
        public string SelectedInnerSheetSize
        {
            get => _selectedInnerSheetSize;
            set
            {
                if (_selectedInnerSheetSize == value) return;
                _selectedInnerSheetSize = value ?? "";
                OnPropertyChanged();
                ApplySizeText(_selectedInnerSheetSize, isInner: true);
            }
        }

        private void ApplySizeText(string display, bool isInner)
        {
            // Manual sheet choice wins over any in-flight colour/thickness
            // refresh. Without this, a delayed store refresh can reset the
            // user's 2500 × 1875 pick back to the largest/default size.
            _sheetStoreEpoch++;
            var (w, h) = SpecSheetRegistry.ParseSize(display);
            if (w <= 0 || h <= 0) return;

            if (isInner)
            {
                _isAutoFillingInner = true;
                InnerSheetWidthMm = w;
                InnerSheetHeightMm = h;
                _isAutoFillingInner = false;
                FireAndForget(RepriceInnerAsync);
            }
            else
            {
                _isAutoFillingOuter = true;
                OuterSheetWidthMm = w;
                OuterSheetHeightMm = h;
                _isAutoFillingOuter = false;
                FireAndForget(RepriceOuterAsync);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HINT
        // ═══════════════════════════════════════════════════════════════

        private string _sheetStoreHint = "";
        /// <summary>Skippable guidance line; empty when both panes resolved.</summary>
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

        private bool _outerInStore;
        private bool _innerInStore;
        private string _outerMissReason = "";
        private string _innerMissReason = "";

        public bool OuterPriceResolvedFromStore => _outerInStore;
        public bool InnerPriceResolvedFromStore => _innerInStore;

        internal void SetPanePriceResolvedFromStore(bool outer, bool inner)
        {
            _outerInStore = outer;
            _innerInStore = inner;
            UpdateHint();
        }

        // ═══════════════════════════════════════════════════════════════
        //  LOAD / REFRESH
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Refills <c>ColorOptions</c> from the Sheet Store ONLY (no
        /// built-in defaults since 2026-09-10) and refreshes both sides.
        /// Idempotent.
        /// </summary>
        public async Task LoadSheetStoreAsync()
        {
            try
            {
                await SheetStorePricingService
                    .LoadColorsIntoAsync(ColorOptions)
                    .ConfigureAwait(true);

                await RefreshOuterFromStoreAsync().ConfigureAwait(true);
                await RefreshInnerFromStoreAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLogService.Error(
                    "[DguParametersViewModel] Sheet-store load failed", ex);
            }
        }

        /// <summary>Setter hook — outer thickness/colour changed.</summary>
        private void OnOuterGlassChanged()
        {
            if (_suspendSheetStoreRefresh) return;
            // Invalidate any in-flight refresh for the previous outer
            // selection so a slow store read cannot rewrite the size list /
            // W-H / price with stale values (shared epoch covers both sides;
            // a discarded inner refresh is harmless — its glass is unchanged).
            _sheetStoreEpoch++;
            FireAndForget(RefreshOuterFromStoreAsync);
        }

        /// <summary>Setter hook — inner thickness/colour changed.</summary>
        private void OnInnerGlassChanged()
        {
            if (_suspendSheetStoreRefresh) return;
            _sheetStoreEpoch++;
            FireAndForget(RefreshInnerFromStoreAsync);
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
                work, "[DguParametersViewModel] sheet-store refresh failed");

        /// <summary>
        /// Colour or thickness changed for the outer pane: reload the size
        /// list, snap W/H to the largest stocked sheet, and reprice.
        /// </summary>
        private async Task RefreshOuterFromStoreAsync()
        {
            int epoch = _sheetStoreEpoch;
            int mm = SheetStorePricingService.ParseMm(OuterThickness);

            var sizes = await SheetStorePricingService
                .GetSizesAsync(OuterGlassColor, mm).ConfigureAwait(true);

            if (epoch != _sheetStoreEpoch || _suspendSheetStoreRefresh) return;

            ReplaceSizes(OuterSheetSizeOptions, sizes.Select(s => s.Display));

            _isAutoFillingOuter = true;
            if (sizes.Count > 0)
            {
                // GetSizesAsync returns largest-area first — the jumbo sheet
                // is the sensible default to quote against.
                OuterSheetWidthMm = sizes[0].W;
                OuterSheetHeightMm = sizes[0].H;
                _selectedOuterSheetSize = sizes[0].Display;
                OnPropertyChanged(nameof(SelectedOuterSheetSize));
            }
            _isAutoFillingOuter = false;

            await RepriceOuterAsync(epoch).ConfigureAwait(true);
        }

        /// <summary>Inner equivalent of <see cref="RefreshOuterFromStoreAsync"/>.</summary>
        private async Task RefreshInnerFromStoreAsync()
        {
            int epoch = _sheetStoreEpoch;
            int mm = SheetStorePricingService.ParseMm(InnerThickness);

            var sizes = await SheetStorePricingService
                .GetSizesAsync(InnerGlassColor, mm).ConfigureAwait(true);

            if (epoch != _sheetStoreEpoch || _suspendSheetStoreRefresh) return;

            ReplaceSizes(InnerSheetSizeOptions, sizes.Select(s => s.Display));

            _isAutoFillingInner = true;
            if (sizes.Count > 0)
            {
                InnerSheetWidthMm = sizes[0].W;
                InnerSheetHeightMm = sizes[0].H;
                _selectedInnerSheetSize = sizes[0].Display;
                OnPropertyChanged(nameof(SelectedInnerSheetSize));
            }
            _isAutoFillingInner = false;

            await RepriceInnerAsync(epoch).ConfigureAwait(true);
        }

        /// <summary>
        /// Writes OuterBasePrice from the Sheet Store SELLING price of the
        /// exact sheet (colour + thickness + W + H) currently selected.
        /// </summary>
        private Task RepriceOuterAsync() => RepriceOuterAsync(-1);

        private async Task RepriceOuterAsync(int expectedEpoch)
        {
            int epoch = expectedEpoch >= 0 ? expectedEpoch : _sheetStoreEpoch;
            int mm = SheetStorePricingService.ParseMm(OuterThickness);

            var price = await SheetStorePricingService
                .TryGetExactPriceForSizeAsync(
                    OuterGlassColor, mm, OuterSheetWidthMm, OuterSheetHeightMm)
                .ConfigureAwait(true);

            if (epoch != _sheetStoreEpoch || _suspendSheetStoreRefresh) return;

            decimal? resolved = Step4MaterialIntegrity.ResolveStorePrice(price);
            _outerInStore = resolved.HasValue;

            if (resolved.HasValue)
            {
                OuterBasePrice = resolved.Value;
                _outerMissReason = "";
            }
            else
            {
                // PHASE A: a miss is invalid. Do not write 0 (that silently
                // became a valid-looking rate) and do not keep a prior pane
                // price as if the lookup succeeded.
                _outerMissReason = Step4MaterialIntegrity.DescribeMiss(
                    "DGU outer", OuterGlassColor, mm, OuterSheetWidthMm, OuterSheetHeightMm);
            }

            UpdateHint();
        }

        /// <summary>Inner equivalent of <see cref="RepriceOuterAsync"/>.</summary>
        private Task RepriceInnerAsync() => RepriceInnerAsync(-1);

        private async Task RepriceInnerAsync(int expectedEpoch)
        {
            int epoch = expectedEpoch >= 0 ? expectedEpoch : _sheetStoreEpoch;
            int mm = SheetStorePricingService.ParseMm(InnerThickness);

            var price = await SheetStorePricingService
                .TryGetExactPriceForSizeAsync(
                    InnerGlassColor, mm, InnerSheetWidthMm, InnerSheetHeightMm)
                .ConfigureAwait(true);

            if (epoch != _sheetStoreEpoch || _suspendSheetStoreRefresh) return;

            decimal? resolved = Step4MaterialIntegrity.ResolveStorePrice(price);
            _innerInStore = resolved.HasValue;

            if (resolved.HasValue)
            {
                InnerBasePrice = resolved.Value;
                _innerMissReason = "";
            }
            else
            {
                _innerMissReason = Step4MaterialIntegrity.DescribeMiss(
                    "DGU inner", InnerGlassColor, mm, InnerSheetWidthMm, InnerSheetHeightMm);
            }

            UpdateHint();
        }

        private static void ReplaceSizes(
            ObservableCollection<string> target,
            System.Collections.Generic.IEnumerable<string> values)
        {
            // Marshalled: store refreshes run on thread-pool continuations
            // and CollectionView rejects off-dispatcher source changes.
            SheetStorePricingService.PostToUi(() =>
            {
                target.Clear();
                foreach (var v in values) target.Add(v);
            });
        }

        private void UpdateHint()
        {
            if (_outerInStore && _innerInStore)
            {
                SheetStoreHint = "";
                return;
            }

            var parts = new System.Collections.Generic.List<string>(2);
            if (!_outerInStore && !string.IsNullOrWhiteSpace(_outerMissReason))
                parts.Add(_outerMissReason);
            if (!_innerInStore && !string.IsNullOrWhiteSpace(_innerMissReason))
                parts.Add(_innerMissReason);
            SheetStoreHint = parts.Count > 0 ? string.Join(" ", parts) : NotInStoreHint;
        }
    }
}
