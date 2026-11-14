// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step5DataGrid/GlassItemRow.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — Step 5 data grid
//
// GlassItemRow.cs extracted from the large DataGridViewModel file (Phase 11/12
// God-VM split) — behaviour unchanged, relocated for organisation only.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Behaviors;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.Specifications;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid.GrandTotalSummary;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid
{
    public class GlassItemRow : ViewModelBase
    {
        // ── Serial number ──────────────────────────────────────────────────

        private int _serialNumber;
        public int SerialNumber
        {
            get => _serialNumber;
            set
            {
                if (_serialNumber == value) return;
                _serialNumber = value;
                OnPropertyChanged();
            }
        }

        // ── GlassRef ───────────────────────────────────────────────────────

        private string _glassRef = "";
        public string GlassRef
        {
            get => _glassRef;
            set
            {
                if (_glassRef == value) return;
                _glassRef = value;
                OnPropertyChanged();
            }
        }

        // ── Dimensions ────────────────────────────────────────────────────

        private int _w1;
        public int W1
        {
            get => _w1;
            set
            {
                if (_w1 == value) return;
                _w1 = value;
                OnPropertyChanged();
                RecalculateAll();
            }
        }

        private int _h1;
        public int H1
        {
            get => _h1;
            set
            {
                if (_h1 == value) return;
                _h1 = value;
                OnPropertyChanged();
                RecalculateAll();
            }
        }

        private int _w2;
        public int W2
        {
            get => _w2;
            set
            {
                if (_w2 == value) return;
                _w2 = value;
                OnPropertyChanged();
                RecalculateAll();
            }
        }

        private int _h2;
        public int H2
        {
            get => _h2;
            set
            {
                if (_h2 == value) return;
                _h2 = value;
                OnPropertyChanged();
                RecalculateAll();
            }
        }

        private int _qty = 1;
        public int Qty
        {
            get => _qty;
            set
            {
                if (_qty == value) return;
                _qty = value;
                OnPropertyChanged();
                RecalculateAll();
            }
        }

        // ── Calculated outputs ────────────────────────────────────────────

        private decimal _sqm1;
        public decimal Sqm1
        {
            get => _sqm1;
            private set
            {
                if (_sqm1 == value) return;
                _sqm1 = value;
                OnPropertyChanged();
            }
        }

        private decimal _totalSqm;
        public decimal TotalSqm
        {
            get => _totalSqm;
            private set
            {
                if (_totalSqm == value) return;
                _totalSqm = value;
                OnPropertyChanged();
            }
        }

        private decimal _sqm2;
        public decimal Sqm2
        {
            get => _sqm2;
            private set
            {
                if (_sqm2 == value) return;
                _sqm2 = value;
                OnPropertyChanged();
            }
        }

        private decimal _totalSqm2;
        public decimal TotalSqm2
        {
            get => _totalSqm2;
            private set
            {
                if (_totalSqm2 == value) return;
                _totalSqm2 = value;
                OnPropertyChanged();
            }
        }

        private decimal _lm1;
        public decimal Lm1
        {
            get => _lm1;
            private set
            {
                if (_lm1 == value) return;
                _lm1 = value;
                OnPropertyChanged();
            }
        }

        private decimal _lm2;
        public decimal Lm2
        {
            get => _lm2;
            private set
            {
                if (_lm2 == value) return;
                _lm2 = value;
                OnPropertyChanged();
            }
        }

        // ── Rate and Value ────────────────────────────────────────────────

        private decimal _rate;
        public decimal Rate
        {
            get => _rate;
            set
            {
                if (_rate == value) return;
                _rate = value;
                _value = MoneyRounding.Round(_sqm1 * _qty * _rate);
                OnPropertyChanged();
                OnPropertyChanged(nameof(Value));
            }
        }

        private decimal _value;
        public decimal Value
        {
            get => _value;
            private set
            {
                if (_value == value) return;
                _value = value;
                OnPropertyChanged();
            }
        }

        // ── Perimeter factor ──────────────────────────────────────────────

        private int _perimeterFactor = 2;
        public int PerimeterFactor
        {
            get => _perimeterFactor;
            set
            {
                if (_perimeterFactor == value) return;
                _perimeterFactor = value;
                OnPropertyChanged();
                RecalculateAll();
            }
        }

        // ── Flags ─────────────────────────────────────────────────────────

        internal bool SuppressRateAutoAdjust { get; set; } = false;

        // ── optimisation read-back ─────────────────────────────────────────
        // Stamped AFTER a run by OptimizationViewModel.StampCutListResults,
        // which joins each row's pane sizes to that run's per-size results.
        // Purely additive and display-only: no area, pricing or footer maths
        // reads these, and Step 5's own grid never binds them, so a proforma
        // invoice is unaffected by their existence.

        private int _qtyUsed;
        /// <summary>Pieces of this row's sizes the engine actually placed.</summary>
        public int QtyUsed
        {
            get => _qtyUsed;
            set
            {
                if (_qtyUsed == value) return;
                _qtyUsed = value;
                OnPropertyChanged();
            }
        }

        private decimal _optPercent;
        /// <summary>
        /// Area-weighted utilization of the cutting groups this row's panes
        /// landed in — glass m² over stock m² across both panes, which is the
        /// row's true yield rather than either pane's on its own.
        /// </summary>
        public decimal OptPercent
        {
            get => _optPercent;
            set
            {
                if (_optPercent == value) return;
                _optPercent = value;
                OnPropertyChanged();
            }
        }

        private bool _hasOptimizationResult;
        /// <summary>
        /// True once a run has stamped this row. Drives the difference between
        /// "0 placed" — a real answer, the size did not fit — and "—", no run
        /// yet. Conflating those two would read as a failure before anything
        /// was ever optimised.
        /// </summary>
        public bool HasOptimizationResult
        {
            get => _hasOptimizationResult;
            set
            {
                if (_hasOptimizationResult == value) return;
                _hasOptimizationResult = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Drops a stale read-back, e.g. when the list is rebuilt.</summary>
        public void ClearOptimizationStamp()
        {
            QtyUsed = 0;
            OptPercent = 0m;
            HasOptimizationResult = false;
        }

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(_glassRef) &&
            _w1 == 0 && _h1 == 0 && _w2 == 0 && _h2 == 0;

        // ── Highlight state (transient — for Find visual feedback) ─────

        private bool _isHighlighted;
        /// <summary>
        /// Transient visual highlight — set true briefly by SpecGroup.HighlightRow
        /// when Find locates this row. Cleared automatically after ~2 seconds.
        /// Bound to row background via DataTrigger for yellow flash.
        /// </summary>
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                if (_isHighlighted == value) return;
                _isHighlighted = value;
                OnPropertyChanged();
            }
        }

        // ── Selection state (for multi-row Ctrl+D fill down) ──────────

        private bool _isSelected;
        /// <summary>
        /// True when this row is part of the current selection.
        /// Set by DataGridNavigationBehavior via SpecGroup.SetRowSelection.
        /// Bound to row background via DataTrigger for blue selection highlight.
        /// Cleared by clicking on a TextBox, clicking empty area, or pressing Escape.
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        // ── Calculations ──────────────────────────────────────────────────

        public void RecalculateAll()
        {
            decimal newSqm1 = _w1 > 0 && _h1 > 0
                ? SqmCalculator.CalculatePanel(_w1, _h1) : 0m;
            decimal newTotalSqm = Math.Round(newSqm1 * _qty, 3);

            decimal newSqm2 = _w2 > 0 && _h2 > 0
                ? SqmCalculator.CalculatePanel(_w2, _h2) : 0m;
            decimal newTotalSqm2 = Math.Round(newSqm2 * _qty, 3);

            decimal newLm1 = _w1 > 0 && _h1 > 0
                ? Math.Round(
                    ((decimal)_w1 + _h1) / 1000m * _perimeterFactor * _qty, 3)
                : 0m;
            decimal newLm2 = _w2 > 0 && _h2 > 0
                ? Math.Round(
                    ((decimal)_w2 + _h2) / 1000m * _perimeterFactor * _qty, 3)
                : 0m;

            decimal newValue = MoneyRounding.Round(newSqm1 * _qty * _rate);

            _sqm1 = newSqm1;
            _totalSqm = newTotalSqm;
            _sqm2 = newSqm2;
            _totalSqm2 = newTotalSqm2;
            _lm1 = newLm1;
            _lm2 = newLm2;
            _value = newValue;

            OnPropertyChanged(nameof(Sqm1));
            OnPropertyChanged(nameof(TotalSqm));
            OnPropertyChanged(nameof(Sqm2));
            OnPropertyChanged(nameof(TotalSqm2));
            OnPropertyChanged(nameof(Lm1));
            OnPropertyChanged(nameof(Lm2));
            OnPropertyChanged(nameof(Value));
        }

        public void SetAllSilent(
            string glassRef,
            int w1, int h1,
            int w2, int h2,
            int qty,
            int perimeterFactor,
            decimal rate)
        {
            _glassRef = glassRef;
            _w1 = w1;
            _h1 = h1;
            _w2 = w2;
            _h2 = h2;
            _qty = qty <= 0 ? 1 : qty;
            _perimeterFactor = perimeterFactor;
            _rate = rate;

            _sqm1 = _w1 > 0 && _h1 > 0
                ? SqmCalculator.CalculatePanel(_w1, _h1) : 0m;
            _totalSqm = Math.Round(_sqm1 * _qty, 3);

            _sqm2 = _w2 > 0 && _h2 > 0
                ? SqmCalculator.CalculatePanel(_w2, _h2) : 0m;
            _totalSqm2 = Math.Round(_sqm2 * _qty, 3);

            _lm1 = _w1 > 0 && _h1 > 0
                ? Math.Round(
                    ((decimal)_w1 + _h1) / 1000m * _perimeterFactor * _qty, 3)
                : 0m;
            _lm2 = _w2 > 0 && _h2 > 0
                ? Math.Round(
                    ((decimal)_w2 + _h2) / 1000m * _perimeterFactor * _qty, 3)
                : 0m;

            _value = MoneyRounding.Round(_sqm1 * _qty * _rate);
        }

        public void ApplyColumnValues(
            Dictionary<int, string> columnValues,
            int perimeterFactor,
            decimal surchargeRate,
            decimal baseRate)
        {
            foreach (var kv in columnValues)
            {
                switch (kv.Key)
                {
                    case GridColumnIndex.GlassRef:
                        _glassRef = kv.Value ?? "";
                        break;

                    case GridColumnIndex.W1:
                        if (int.TryParse(TextParsing.CleanNum(kv.Value), out int w1))
                            _w1 = w1;
                        break;

                    case GridColumnIndex.H1:
                        if (int.TryParse(TextParsing.CleanNum(kv.Value), out int h1))
                            _h1 = h1;
                        break;

                    case GridColumnIndex.W2:
                        if (int.TryParse(TextParsing.CleanNum(kv.Value), out int w2))
                            _w2 = w2;
                        break;

                    case GridColumnIndex.H2:
                        if (int.TryParse(TextParsing.CleanNum(kv.Value), out int h2))
                            _h2 = h2;
                        break;

                    case GridColumnIndex.Qty:
                        if (TextParsing.TryParsePieceQty(kv.Value, out int qty))
                            _qty = qty;
                        break;

                    case GridColumnIndex.Rate:
                        if (decimal.TryParse(
                                TextParsing.CleanNum(kv.Value),
                                System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture,
                                out decimal rate)
                            && rate > 0)
                        {
                            _rate = rate;
                        }
                        break;
                }
            }

            _perimeterFactor = perimeterFactor;

            _sqm1 = _w1 > 0 && _h1 > 0
                ? SqmCalculator.CalculatePanel(_w1, _h1) : 0m;
            _totalSqm = Math.Round(_sqm1 * _qty, 3);

            _sqm2 = _w2 > 0 && _h2 > 0
                ? SqmCalculator.CalculatePanel(_w2, _h2) : 0m;
            _totalSqm2 = Math.Round(_sqm2 * _qty, 3);

            _lm1 = _w1 > 0 && _h1 > 0
                ? Math.Round(
                    ((decimal)_w1 + _h1) / 1000m * _perimeterFactor * _qty, 3)
                : 0m;
            _lm2 = _w2 > 0 && _h2 > 0
                ? Math.Round(
                    ((decimal)_w2 + _h2) / 1000m * _perimeterFactor * _qty, 3)
                : 0m;

            if (!SuppressRateAutoAdjust &&
                !columnValues.ContainsKey(GridColumnIndex.Rate))
            {
                _rate = _sqm1 >= 4m ? surchargeRate : baseRate;
            }

            _value = MoneyRounding.Round(_sqm1 * _qty * _rate);
        }

        public void FireAllChanged()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                FireAllChangedCore();
            else
                dispatcher.InvokeAsync(
                    FireAllChangedCore, DispatcherPriority.Background);
        }

        private void FireAllChangedCore()
        {
            OnPropertyChanged(nameof(SerialNumber));
            OnPropertyChanged(nameof(GlassRef));
            OnPropertyChanged(nameof(W1));
            OnPropertyChanged(nameof(H1));
            OnPropertyChanged(nameof(W2));
            OnPropertyChanged(nameof(H2));
            OnPropertyChanged(nameof(Qty));
            OnPropertyChanged(nameof(Sqm1));
            OnPropertyChanged(nameof(TotalSqm));
            OnPropertyChanged(nameof(Sqm2));
            OnPropertyChanged(nameof(TotalSqm2));
            OnPropertyChanged(nameof(Lm1));
            OnPropertyChanged(nameof(Lm2));
            OnPropertyChanged(nameof(Rate));
            OnPropertyChanged(nameof(Value));
        }

    }
}
