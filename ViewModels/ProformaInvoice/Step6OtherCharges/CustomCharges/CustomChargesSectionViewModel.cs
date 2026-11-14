    using ProGlassAutomation.Commands;
    using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges;
    using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges.CustomCharges;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Windows.Input;

    namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges.CustomCharges
    {
        public class CustomChargesSectionViewModel : ViewModelBase
        {
            public ObservableCollection<CustomChargeRow> AddedCharges { get; } = new();
            public List<OtherChargeUnit> UnitOptions { get; } = Enum.GetValues(typeof(OtherChargeUnit)).Cast<OtherChargeUnit>().ToList();

            // ── Input Form Properties ──────────────────────────────────────────

            private string _inputName = "";
            public string InputName
            {
                get => _inputName;
                set
                {
                    _inputName = value;
                    OnPropertyChanged();
                    if (AddChargeCommand is RelayCommand add)
                        add.RaiseCanExecuteChanged();
                }
            }

            private decimal _inputQty = 1;
            public decimal InputQty { get => _inputQty; set { _inputQty = value; OnPropertyChanged(); } }

            private OtherChargeUnit _inputUnit = OtherChargeUnit.Qty;
            public OtherChargeUnit InputUnit { get => _inputUnit; set { _inputUnit = value; OnPropertyChanged(); } }

            private decimal _inputRate = 0;
            public decimal InputRate { get => _inputRate; set { _inputRate = value; OnPropertyChanged(); } }

            // ── Totals ─────────────────────────────────────────────────────────

            private decimal _totalCustomChargesValue;
            public decimal TotalCustomChargesValue
            {
                get => _totalCustomChargesValue;
                private set { if (_totalCustomChargesValue == value) return; _totalCustomChargesValue = value; OnPropertyChanged(); }
            }

            public ICommand AddChargeCommand { get; }
            public ICommand RemoveChargeCommand { get; }

            public CustomChargesSectionViewModel()
            {
                AddChargeCommand = new RelayCommand(_ => AddNewCharge(), _ => !string.IsNullOrWhiteSpace(InputName));
                RemoveChargeCommand = new RelayCommand(p =>
                {
                    if (p is CustomChargeRow r)
                    {
                        DetachCharge(r);
                        AddedCharges.Remove(r);
                        RecalculateTotal();
                    }
                });
            }

            private void AddNewCharge()
            {
                var row = new CustomChargeRow
                {
                    ChargeName = InputName,
                    Qty = InputQty,
                    Unit = InputUnit,
                    Rate = InputRate
                };

                AttachCharge(row);
                AddedCharges.Add(row);

                // Reset form
                InputName = "";
                InputQty = 1;
                InputRate = 0;

                RecalculateTotal();
            }

            /// <summary>
            /// Hooks a charge created outside AddNewCharge (snapshot restore)
            /// so toggling ON / editing rate still updates the section total.
            /// </summary>
            public void AttachCharge(CustomChargeRow row)
            {
                if (row == null) return;
                row.PropertyChanged -= OnRestoredChargeChanged;
                row.PropertyChanged += OnRestoredChargeChanged;
            }

            public void DetachCharge(CustomChargeRow row)
            {
                if (row == null) return;
                row.PropertyChanged -= OnRestoredChargeChanged;
            }

            public void ClearCharges()
            {
                foreach (var row in AddedCharges.ToList())
                    DetachCharge(row);

                AddedCharges.Clear();
                RecalculateTotal();
            }

            private void OnRestoredChargeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(CustomChargeRow.ResultAmount)
                    || e.PropertyName == nameof(CustomChargeRow.IsActive))
                    RecalculateTotal();
            }

            public void RefreshTotal() => RecalculateTotal();

            private void RecalculateTotal()
            {
                TotalCustomChargesValue = AddedCharges.Where(c => c.IsActive).Sum(c => c.ResultAmount);
            }
        }
    }