using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using ProGlassAutomation.Services.Optimization;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel
    {
        // The source UI is retained; this adapter protects the current feed contract.
        private bool _sessionInputsActive;
        private const string UnlabelledGlass = "(unlabelled glass)";
        public bool AllowCrossCompositionStock => false;
        private readonly HashSet<StockEntryVm> _observedStocks = new();

        private void InitializeInputLifetime()
        {
            Pieces.CollectionChanged += OnPiecesChanged;
            Stocks.CollectionChanged += OnStocksChanged;
            PropertyChanged += OnSettingsChanged;
        }

        private void DisposeInputLifetime()
        {
            Pieces.CollectionChanged -= OnPiecesChanged;
            Stocks.CollectionChanged -= OnStocksChanged;
            PropertyChanged -= OnSettingsChanged;
            foreach (var stock in _observedStocks) stock.PropertyChanged -= OnStockEdited;
            _observedStocks.Clear();
        }

        private void OnPiecesChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateInputs();

        private void OnStocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var stock in _observedStocks) stock.PropertyChanged -= OnStockEdited;
                _observedStocks.Clear();
            }
            if (e.OldItems != null)
                foreach (StockEntryVm stock in e.OldItems)
                {
                    stock.PropertyChanged -= OnStockEdited;
                    _observedStocks.Remove(stock);
                }
            if (e.NewItems != null)
                foreach (StockEntryVm stock in e.NewItems)
                    if (_observedStocks.Add(stock)) stock.PropertyChanged += OnStockEdited;
            OnPropertyChanged(nameof(HasStocks));
            InvalidateInputs();
        }

        private void OnStockEdited(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(StockEntryVm.WidthText) or nameof(StockEntryVm.HeightText)
                or nameof(StockEntryVm.AvailableText) or nameof(StockEntryVm.Composition)
                or nameof(StockEntryVm.PricePerSqm) or nameof(StockEntryVm.IsUsedInRun)) InvalidateInputs();
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ThicknessText) or nameof(LeftMarginText) or nameof(RightMarginText)
                or nameof(TopMarginText) or nameof(BottomMarginText) or nameof(KerfText)
                or nameof(MinBreakoutText) or nameof(RotationEnabled) or nameof(SelectedEffortLevel)
                or nameof(SelectedStageStrategy) or nameof(SelectedPlatePattern)
                or nameof(UseOffcutReuse)
                or nameof(SelectedCutComplexity) or nameof(MinTrimWidthText))
                InvalidateInputs();
        }

        private void InvalidateInputs()
        {
            // A concurrent programmatic edit cancels as well; disabling WPF tabs alone
            // is not enough to stop a headless host or a late async picker callback.
            if (IsRunning) CancelRun();
            if (!HasResult) return;
            HasResult = false;
            OptimizationResultFeed.Invalidate("optimization inputs changed — run again");
        }
    }
}
