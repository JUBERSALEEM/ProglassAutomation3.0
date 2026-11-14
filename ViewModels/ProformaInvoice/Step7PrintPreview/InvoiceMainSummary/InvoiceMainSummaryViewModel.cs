using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.Step7PrintPreview.InvoiceMainSummary;
using ProGlassAutomation.Services.ProformaInvoice;
using ProGlassAutomation.Services.Reporting;
using ProGlassAutomation.ViewModels;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.InvoiceMainSummary
{
    /// <summary>
    /// Financial summary card at the top of Step 7 (Print Preview).
    /// Aggregates glass materials + other charges, applies discount,
    /// applies UAE VAT (default 5%), produces Grand Total (VAT inclusive).
    ///
    /// UAE VAT COMPLIANCE (Federal Decree-Law No. 8 of 2017):
    ///   1. Discount is subtracted from taxable value BEFORE VAT calc.
    ///   2. VAT applies to (net - discount), not to gross.
    ///   3. Standard rate is 5% for taxable supplies.
    ///   4. Zero-rated / exempt supplies set IsVatApplicable = false.
    ///
    /// BUG-56 [MEDIUM]: Discount capping consolidated in Recalculate.
    ///                  Setter for DiscountValue only clamps at zero;
    ///                  Recalculate() enforces the net-value cap.
    /// BUG-57 [HIGH]: Recalculate now guarded by _suspendRecalculation.
    ///                Restore-from-snapshot path suspends recalcs so
    ///                setting 4 values fires ONE recalc, not 4.
    /// BUG-58 [MEDIUM]: UAE VAT law reference inline in Recalculate.
    /// BUG-59 [LOW]: Percentage discount capped at 100% in setter;
    ///               fixed-amount discount uncapped in setter but
    ///               capped at net in Recalculate.
    /// </summary>
    public class InvoiceMainSummaryViewModel : ViewModelBase
    {
        private readonly DataGridViewModel _dataGridVM;
        private readonly OtherChargesViewModel _otherChargesVM;

        // BUG-57: nested guard for batched restore / type conversion
        private int _batchDepth;
        private bool _suspendRecalculation => _batchDepth > 0;

        public string Title { get; } = "INVOICE MAIN SUMMARY";
        public string Subtitle { get; } = "Final financial breakdown — glass materials + other charges + discount + VAT.";

        // ── Inputs (from other steps — read-only here) ────────────────────

        private decimal _glassMaterialsSubtotal;
        public decimal GlassMaterialsSubtotal
        {
            get => _glassMaterialsSubtotal;
            private set { if (_glassMaterialsSubtotal == value) return; _glassMaterialsSubtotal = value; OnPropertyChanged(); Recalculate(); }
        }

        private decimal _otherChargesSubtotal;
        public decimal OtherChargesSubtotal
        {
            get => _otherChargesSubtotal;
            private set { if (_otherChargesSubtotal == value) return; _otherChargesSubtotal = value; OnPropertyChanged(); Recalculate(); }
        }

        // ── Discount ──────────────────────────────────────────────────────

        public List<DiscountType> DiscountTypeOptions { get; } = new()
        {
            DiscountType.FixedAmount,
            DiscountType.Percentage
        };

        private DiscountType _selectedDiscountType = DiscountType.FixedAmount;
        public DiscountType SelectedDiscountType
        {
            get => _selectedDiscountType;
            set
            {
                if (_selectedDiscountType == value) return;
                decimal previousAed = DiscountAmount;
                var previous = _selectedDiscountType;
                _selectedDiscountType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DiscountUnitLabel));
                OnPropertyChanged(nameof(IsAedDiscount));
                OnPropertyChanged(nameof(IsPercentageDiscount));

                BeginBatchUpdate();
                if (value == DiscountType.FixedAmount && previous == DiscountType.Percentage)
                    DiscountValue = previousAed;
                else if (value == DiscountType.Percentage && previous == DiscountType.FixedAmount)
                {
                    decimal net = GlassMaterialsSubtotal + OtherChargesSubtotal;
                    DiscountValue = net > 0m
                        ? Math.Round(previousAed / net * 100m, 2)
                        : 0m;
                }
                EndBatchUpdate();
            }
        }

        /// <summary>True when the clerk is typing an AED amount (the default).</summary>
        public bool IsAedDiscount
        {
            get => _selectedDiscountType == DiscountType.FixedAmount;
            set { if (value) SelectedDiscountType = DiscountType.FixedAmount; }
        }

        public bool IsPercentageDiscount
        {
            get => _selectedDiscountType == DiscountType.Percentage;
            set { if (value) SelectedDiscountType = DiscountType.Percentage; }
        }

        /// <summary>
        /// Discount value — interpreted as % if Percentage, or AED if FixedAmount.
        /// BUG-59: Percentage capped at 100 in setter. Fixed-amount is not
        /// capped here (uncapped input allowed), but Recalculate() enforces
        /// the "cannot exceed net" rule to prevent negative subtotals.
        /// </summary>
        private decimal _discountValue;
        public decimal DiscountValue
        {
            get => _discountValue;
            set
            {
                // Lower bound: never negative
                var clamped = value < 0 ? 0 : value;

                // BUG-59: Upper bound for percentage discount = 100
                if (_selectedDiscountType == DiscountType.Percentage && clamped > 100m)
                    clamped = 100m;

                if (_discountValue == clamped) return;
                _discountValue = clamped;
                OnPropertyChanged();
                Recalculate();
            }
        }

        public string DiscountUnitLabel =>
            _selectedDiscountType == DiscountType.Percentage ? "%" : "AED";

        // ── Round grand total to a customer figure ────────────────────────

        private decimal _roundToGrandTotal;
        /// <summary>
        /// Target VAT-inclusive total (e.g. 15500.00). APPLY writes the
        /// matching AED discount onto net subtotal.
        /// </summary>
        public decimal RoundToGrandTotal
        {
            get => _roundToGrandTotal;
            set
            {
                var clamped = value < 0m ? 0m : MoneyRounding.Round(value);
                if (_roundToGrandTotal == clamped) return;
                _roundToGrandTotal = clamped;
                OnPropertyChanged();
                ApplyRoundToCommand?.RaiseCanExecuteChanged();
            }
        }

        private string _roundToHint = "Type the even total the customer wants, then APPLY.";
        public string RoundToHint
        {
            get => _roundToHint;
            private set { if (_roundToHint == value) return; _roundToHint = value; OnPropertyChanged(); }
        }

        public RelayCommand ApplyRoundToCommand { get; }

        // ── VAT ────────────────────────────────────────────────────────────

        private bool _isVatApplicable = true;
        public bool IsVatApplicable
        {
            get => _isVatApplicable;
            set { if (_isVatApplicable == value) return; _isVatApplicable = value; OnPropertyChanged(); Recalculate(); }
        }

        private decimal _vatPercent = 5m;
        public decimal VatPercent
        {
            get => _vatPercent;
            set
            {
                // Industrial guard: VAT is a percentage, not a free-form
                // multiplier. The cross-step validator also blocks stale
                // snapshots outside this range; the setter prevents new bad
                // values from being generated by the UI.
                var clamped = value < 0 ? 0 : value > 100m ? 100m : value;
                if (_vatPercent == clamped) return;
                _vatPercent = clamped;
                OnPropertyChanged();
                Recalculate();
            }
        }

        // ── Computed values (read-only) ───────────────────────────────────

        private decimal _netSubtotal;
        public decimal NetSubtotal
        {
            get => _netSubtotal;
            private set { if (_netSubtotal == value) return; _netSubtotal = value; OnPropertyChanged(); }
        }

        private decimal _discountAmount;
        public decimal DiscountAmount
        {
            get => _discountAmount;
            private set { if (_discountAmount == value) return; _discountAmount = value; OnPropertyChanged(); }
        }

        private decimal _subtotalBeforeVat;
        public decimal SubtotalBeforeVat
        {
            get => _subtotalBeforeVat;
            private set { if (_subtotalBeforeVat == value) return; _subtotalBeforeVat = value; OnPropertyChanged(); }
        }

        private decimal _vatAmount;
        public decimal VatAmount
        {
            get => _vatAmount;
            private set { if (_vatAmount == value) return; _vatAmount = value; OnPropertyChanged(); }
        }

        private decimal _grandTotalVatInclusive;
        public decimal GrandTotalVatInclusive
        {
            get => _grandTotalVatInclusive;
            private set { if (_grandTotalVatInclusive == value) return; _grandTotalVatInclusive = value; OnPropertyChanged(); }
        }

        // ── Constructor ───────────────────────────────────────────────────

        public InvoiceMainSummaryViewModel(
            DataGridViewModel dataGridVM,
            OtherChargesViewModel otherChargesVM)
        {
            _dataGridVM = dataGridVM ?? throw new ArgumentNullException(nameof(dataGridVM));
            _otherChargesVM = otherChargesVM ?? throw new ArgumentNullException(nameof(otherChargesVM));

            _dataGridVM.SummaryViewModel.PropertyChanged += OnDataGridSummaryChanged;
            _otherChargesVM.PropertyChanged += OnOtherChargesChanged;

            ApplyRoundToCommand = new RelayCommand(
                _ => ApplyRoundTo(),
                _ => !_suspendRecalculation && RoundToGrandTotal > 0m);

            SyncFromDataGrid();
            SyncFromOtherCharges();
        }

        /// <summary>
        /// Sets an AED discount on net subtotal so Grand Total equals
        /// <see cref="RoundToGrandTotal"/>. Cannot raise the total — only
        /// discount down.
        /// </summary>
        public void ApplyRoundTo()
        {
            decimal net = MoneyRounding.Round(GlassMaterialsSubtotal + OtherChargesSubtotal);
            decimal target = MoneyRounding.Round(RoundToGrandTotal);
            if (net <= 0m || target <= 0m)
            {
                RoundToHint = "Enter a target grand total in AED first.";
                return;
            }

            decimal maxGrand = InvoiceRoundTo.GrandFromDiscount(
                net, 0m, VatPercent, IsVatApplicable);
            if (target > maxGrand)
            {
                RoundToHint =
                    $"Cannot round up. Undiscounted total is AED {maxGrand:N2}.";
                return;
            }

            decimal discount = InvoiceRoundTo.DiscountOnNet(
                net, VatPercent, IsVatApplicable, target);

            BeginBatchUpdate();
            _selectedDiscountType = DiscountType.FixedAmount;
            _discountValue = discount;
            OnPropertyChanged(nameof(SelectedDiscountType));
            OnPropertyChanged(nameof(DiscountUnitLabel));
            OnPropertyChanged(nameof(IsAedDiscount));
            OnPropertyChanged(nameof(IsPercentageDiscount));
            OnPropertyChanged(nameof(DiscountValue));
            EndBatchUpdate();

            RoundToHint =
                $"Discount AED {DiscountAmount:N2} on net subtotal. Grand total AED {GrandTotalVatInclusive:N2}.";
        }

        // ══════════════════════════════════════════════════════════════════
        //  BATCH LOAD API (BUG-57)
        //
        //  Called by ProformaInvoiceMapper.RestoreWizardFromSnapshot to set
        //  multiple properties in one atomic operation without firing 4
        //  Recalculate() calls in a row.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// BUG-57: Suspends Recalculate() while restoring from snapshot.
        /// Call BeginBatchUpdate, set all properties, then EndBatchUpdate.
        /// </summary>
        public void BeginBatchUpdate()
        {
            _batchDepth++;
        }

        /// <summary>
        /// BUG-57: Fires ONE Recalculate() after the outermost batch ends.
        /// </summary>
        public void EndBatchUpdate()
        {
            if (_batchDepth > 0) _batchDepth--;
            if (_batchDepth == 0) Recalculate();
        }

        // ── Change tracking ───────────────────────────────────────────────

        private void OnDataGridSummaryChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_dataGridVM.SummaryViewModel.GrandTotalValue))
                SyncFromDataGrid();
        }

        private void OnOtherChargesChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OtherChargesViewModel.TotalOtherCharges))
                SyncFromOtherCharges();
        }

        private void SyncFromDataGrid()
        {
            GlassMaterialsSubtotal = _dataGridVM.SummaryViewModel.GrandTotalValue;
        }

        private void SyncFromOtherCharges()
        {
            OtherChargesSubtotal = _otherChargesVM.TotalOtherCharges;
        }

        // ══════════════════════════════════════════════════════════════════
        //  CORE CALCULATION (BUG-56 consolidated, BUG-57 suspendable,
        //                    BUG-58 UAE VAT law compliance)
        //
        //  UAE Federal Decree-Law No. 8 of 2017 on VAT:
        //    Article 34: Value of supply excludes discounts.
        //    Article 3:  Standard rate 5% on taxable supplies.
        //
        //  Waterfall:
        //    NetSubtotal        = GlassMaterialsSubtotal + OtherChargesSubtotal
        //    DiscountAmount     = NetSubtotal * (DiscountValue/100)  OR  DiscountValue
        //                         (capped so subtotal never goes negative)
        //    SubtotalBeforeVat  = NetSubtotal - DiscountAmount
        //    VatAmount          = SubtotalBeforeVat * (VatPercent/100)  OR  0 if VAT disabled
        //    GrandTotal         = SubtotalBeforeVat + VatAmount
        // ══════════════════════════════════════════════════════════════════

        private void Recalculate()
        {
            // BUG-57: skip recalc during batched restore
            if (_suspendRecalculation) return;

            decimal net = GlassMaterialsSubtotal + OtherChargesSubtotal;

            // Compute raw discount from user input
            decimal discount = SelectedDiscountType switch
            {
                DiscountType.Percentage => MoneyRounding.Round(net * (DiscountValue / 100m)),
                DiscountType.FixedAmount => DiscountValue,
                _ => 0m
            };

            // BUG-56: SINGLE place where discount is capped at net value.
            // Prevents negative subtotals. Required for UAE VAT compliance —
            // Article 34 of Federal Decree-Law No. 8 of 2017 states that
            // discount amount cannot exceed the taxable value of the supply.
            if (discount > net) discount = net;
            if (discount < 0) discount = 0;

            decimal beforeVat = net - discount;

            // BUG-58: VAT applied to (net - discount), NOT gross.
            // This matches UAE FTA guidance for tax invoices.
            decimal vat = IsVatApplicable
                ? MoneyRounding.Round(beforeVat * (VatPercent / 100m))
                : 0m;

            decimal grand = beforeVat + vat;

            NetSubtotal = MoneyRounding.Round(net);
            DiscountAmount = MoneyRounding.Round(discount);
            SubtotalBeforeVat = MoneyRounding.Round(beforeVat);
            VatAmount = vat;
            GrandTotalVatInclusive = MoneyRounding.Round(grand);
        }
    }
}