// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step4PricingEstimator/DguLamParameters/
//         DguLamParametersViewModel.SheetStore.cs
// PROJECT: ProGlassAutomation
// MODULE: Step 4 — DGU+LAM three-pane sheet sizes ↔ Sheet Store
//
// PHASE A. The hybrid has three physical glass panes. Each pane needs its
// own stock sheet W/H and a store-resolved price. A lookup miss is invalid
// — never a silent 0, never a leftover default.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.ProformaInvoice;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguLamParameters
{
    public partial class DguLamParametersViewModel
    {
        public const string NotInStoreHint =
            "This glass is not in Sheet Store — colour, thickness, sheet size and a store price must resolve before optimization can use it.";

        private bool _isAutoFillingOuter;
        private bool _isAutoFillingLamOuter;
        private bool _isAutoFillingLamInner;
        private bool _suspendSheetStoreRefresh;
        private int _sheetStoreEpoch;

        private int _outerSheetWidthMm;
        public int OuterSheetWidthMm
        {
            get => _outerSheetWidthMm;
            set
            {
                if (_outerSheetWidthMm == value) return;
                _outerSheetWidthMm = value;
                OnPropertyChanged();
                if (!_isAutoFillingOuter && !_suspendSheetStoreRefresh)
                    FireAndForget(RepriceOuterAsync);
            }
        }

        private int _outerSheetHeightMm;
        public int OuterSheetHeightMm
        {
            get => _outerSheetHeightMm;
            set
            {
                if (_outerSheetHeightMm == value) return;
                _outerSheetHeightMm = value;
                OnPropertyChanged();
                if (!_isAutoFillingOuter && !_suspendSheetStoreRefresh)
                    FireAndForget(RepriceOuterAsync);
            }
        }

        private int _lamOuterSheetWidthMm;
        public int LamOuterSheetWidthMm
        {
            get => _lamOuterSheetWidthMm;
            set
            {
                if (_lamOuterSheetWidthMm == value) return;
                _lamOuterSheetWidthMm = value;
                OnPropertyChanged();
                if (!_isAutoFillingLamOuter && !_suspendSheetStoreRefresh)
                    FireAndForget(RepriceLamOuterAsync);
            }
        }

        private int _lamOuterSheetHeightMm;
        public int LamOuterSheetHeightMm
        {
            get => _lamOuterSheetHeightMm;
            set
            {
                if (_lamOuterSheetHeightMm == value) return;
                _lamOuterSheetHeightMm = value;
                OnPropertyChanged();
                if (!_isAutoFillingLamOuter && !_suspendSheetStoreRefresh)
                    FireAndForget(RepriceLamOuterAsync);
            }
        }

        private int _lamInnerSheetWidthMm;
        public int LamInnerSheetWidthMm
        {
            get => _lamInnerSheetWidthMm;
            set
            {
                if (_lamInnerSheetWidthMm == value) return;
                _lamInnerSheetWidthMm = value;
                OnPropertyChanged();
                if (!_isAutoFillingLamInner && !_suspendSheetStoreRefresh)
                    FireAndForget(RepriceLamInnerAsync);
            }
        }

        private int _lamInnerSheetHeightMm;
        public int LamInnerSheetHeightMm
        {
            get => _lamInnerSheetHeightMm;
            set
            {
                if (_lamInnerSheetHeightMm == value) return;
                _lamInnerSheetHeightMm = value;
                OnPropertyChanged();
                if (!_isAutoFillingLamInner && !_suspendSheetStoreRefresh)
                    FireAndForget(RepriceLamInnerAsync);
            }
        }

        public ObservableCollection<string> OuterSheetSizeOptions { get; } = new();
        public ObservableCollection<string> LamOuterSheetSizeOptions { get; } = new();
        public ObservableCollection<string> LamInnerSheetSizeOptions { get; } = new();

        private string _sheetStoreHint = "";
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

        public bool HasSheetStoreHint => !string.IsNullOrWhiteSpace(SheetStoreHint);

        private bool _outerInStore;
        private bool _lamOuterInStore;
        private bool _lamInnerInStore;
        private string _outerMissReason = "";
        private string _lamOuterMissReason = "";
        private string _lamInnerMissReason = "";

        public bool OuterPriceResolvedFromStore => _outerInStore;
        public bool LamOuterPriceResolvedFromStore => _lamOuterInStore;
        public bool LamInnerPriceResolvedFromStore => _lamInnerInStore;

        internal void SetPanePriceResolvedFromStore(bool outer, bool lamOuter, bool lamInner)
        {
            _outerInStore = outer;
            _lamOuterInStore = lamOuter;
            _lamInnerInStore = lamInner;
            UpdateHint();
        }

        public async Task LoadSheetStoreAsync()
        {
            try
            {
                await SheetStorePricingService
                    .LoadColorsIntoAsync(ColorOptions)
                    .ConfigureAwait(true);

                await RefreshOuterFromStoreAsync().ConfigureAwait(true);
                await RefreshLamOuterFromStoreAsync().ConfigureAwait(true);
                await RefreshLamInnerFromStoreAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLogService.Error(
                    "[DguLamParametersViewModel] Sheet-store load failed", ex);
            }
        }

        private void OnOuterGlassChanged()
        {
            if (_suspendSheetStoreRefresh) return;
            _sheetStoreEpoch++;
            FireAndForget(RefreshOuterFromStoreAsync);
        }

        private void OnLamOuterGlassChanged()
        {
            if (_suspendSheetStoreRefresh) return;
            _sheetStoreEpoch++;
            FireAndForget(RefreshLamOuterFromStoreAsync);
        }

        private void OnLamInnerGlassChanged()
        {
            if (_suspendSheetStoreRefresh) return;
            _sheetStoreEpoch++;
            FireAndForget(RefreshLamInnerFromStoreAsync);
        }

        internal void RunWithoutSheetStoreRefresh(Action work)
        {
            _sheetStoreEpoch++;
            _suspendSheetStoreRefresh = true;
            try { work?.Invoke(); }
            finally { _suspendSheetStoreRefresh = false; }
        }

        private static void FireAndForget(Func<Task> work)
            => Utilities.AsyncUtility.SafeFireAndForget(
                work, "[DguLamParametersViewModel] sheet-store refresh failed");

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
                OuterSheetWidthMm = sizes[0].W;
                OuterSheetHeightMm = sizes[0].H;
            }
            _isAutoFillingOuter = false;
            await RepriceOuterAsync(epoch).ConfigureAwait(true);
        }

        private async Task RefreshLamOuterFromStoreAsync()
        {
            int epoch = _sheetStoreEpoch;
            int mm = SheetStorePricingService.ParseMm(LamOuterThickness);
            var sizes = await SheetStorePricingService
                .GetSizesAsync(LamOuterColor, mm).ConfigureAwait(true);
            if (epoch != _sheetStoreEpoch || _suspendSheetStoreRefresh) return;

            ReplaceSizes(LamOuterSheetSizeOptions, sizes.Select(s => s.Display));
            _isAutoFillingLamOuter = true;
            if (sizes.Count > 0)
            {
                LamOuterSheetWidthMm = sizes[0].W;
                LamOuterSheetHeightMm = sizes[0].H;
            }
            _isAutoFillingLamOuter = false;
            await RepriceLamOuterAsync(epoch).ConfigureAwait(true);
        }

        private async Task RefreshLamInnerFromStoreAsync()
        {
            int epoch = _sheetStoreEpoch;
            int mm = SheetStorePricingService.ParseMm(LamInnerThickness);
            var sizes = await SheetStorePricingService
                .GetSizesAsync(LamInnerColor, mm).ConfigureAwait(true);
            if (epoch != _sheetStoreEpoch || _suspendSheetStoreRefresh) return;

            ReplaceSizes(LamInnerSheetSizeOptions, sizes.Select(s => s.Display));
            _isAutoFillingLamInner = true;
            if (sizes.Count > 0)
            {
                LamInnerSheetWidthMm = sizes[0].W;
                LamInnerSheetHeightMm = sizes[0].H;
            }
            _isAutoFillingLamInner = false;
            await RepriceLamInnerAsync(epoch).ConfigureAwait(true);
        }

        private Task RepriceOuterAsync() => RepriceOuterAsync(-1);
        private Task RepriceLamOuterAsync() => RepriceLamOuterAsync(-1);
        private Task RepriceLamInnerAsync() => RepriceLamInnerAsync(-1);

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
                _outerMissReason = Step4MaterialIntegrity.DescribeMiss(
                    "DGU+LAM outer", OuterGlassColor, mm, OuterSheetWidthMm, OuterSheetHeightMm);
            }
            UpdateHint();
        }

        private async Task RepriceLamOuterAsync(int expectedEpoch)
        {
            int epoch = expectedEpoch >= 0 ? expectedEpoch : _sheetStoreEpoch;
            int mm = SheetStorePricingService.ParseMm(LamOuterThickness);
            var price = await SheetStorePricingService
                .TryGetExactPriceForSizeAsync(
                    LamOuterColor, mm, LamOuterSheetWidthMm, LamOuterSheetHeightMm)
                .ConfigureAwait(true);
            if (epoch != _sheetStoreEpoch || _suspendSheetStoreRefresh) return;

            decimal? resolved = Step4MaterialIntegrity.ResolveStorePrice(price);
            _lamOuterInStore = resolved.HasValue;
            if (resolved.HasValue)
            {
                LamOuterBasePrice = resolved.Value;
                _lamOuterMissReason = "";
            }
            else
            {
                _lamOuterMissReason = Step4MaterialIntegrity.DescribeMiss(
                    "DGU+LAM lam outer", LamOuterColor, mm, LamOuterSheetWidthMm, LamOuterSheetHeightMm);
            }
            UpdateHint();
        }

        private async Task RepriceLamInnerAsync(int expectedEpoch)
        {
            int epoch = expectedEpoch >= 0 ? expectedEpoch : _sheetStoreEpoch;
            int mm = SheetStorePricingService.ParseMm(LamInnerThickness);
            var price = await SheetStorePricingService
                .TryGetExactPriceForSizeAsync(
                    LamInnerColor, mm, LamInnerSheetWidthMm, LamInnerSheetHeightMm)
                .ConfigureAwait(true);
            if (epoch != _sheetStoreEpoch || _suspendSheetStoreRefresh) return;

            decimal? resolved = Step4MaterialIntegrity.ResolveStorePrice(price);
            _lamInnerInStore = resolved.HasValue;
            if (resolved.HasValue)
            {
                LamInnerBasePrice = resolved.Value;
                _lamInnerMissReason = "";
            }
            else
            {
                _lamInnerMissReason = Step4MaterialIntegrity.DescribeMiss(
                    "DGU+LAM lam inner", LamInnerColor, mm, LamInnerSheetWidthMm, LamInnerSheetHeightMm);
            }
            UpdateHint();
        }

        private static void ReplaceSizes(
            ObservableCollection<string> target,
            IEnumerable<string> values)
        {
            SheetStorePricingService.PostToUi(() =>
            {
                target.Clear();
                foreach (var v in values) target.Add(v);
            });
        }

        private void UpdateHint()
        {
            if (_outerInStore && _lamOuterInStore && _lamInnerInStore)
            {
                SheetStoreHint = "";
                return;
            }

            var parts = new List<string>(3);
            if (!_outerInStore && !string.IsNullOrWhiteSpace(_outerMissReason))
                parts.Add(_outerMissReason);
            if (!_lamOuterInStore && !string.IsNullOrWhiteSpace(_lamOuterMissReason))
                parts.Add(_lamOuterMissReason);
            if (!_lamInnerInStore && !string.IsNullOrWhiteSpace(_lamInnerMissReason))
                parts.Add(_lamInnerMissReason);
            SheetStoreHint = parts.Count > 0 ? string.Join(" ", parts) : NotInStoreHint;
        }
    }
}
