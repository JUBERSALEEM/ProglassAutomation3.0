    // ═══════════════════════════════════════════════════════════════════════
    // FILE: Models/ProformaInvoice/Step6OtherCharges/SpecTargetSelection.cs
    // ═══════════════════════════════════════════════════════════════════════
    // CHAT 17 — NEW FILE
    //
    //   Two-part selector: WHICH SPECS (checkboxes) + WHAT METRIC (radio).
    //   Owned by OtherChargeRow. Computes live ComputedValue by summing the
    //   chosen metric across all checked specs.
    //
    //   Filters metric options by row Unit type.
    //   Emits PropertyChanged on ComputedValue / SummaryText when anything
    //   changes (spec check toggle, metric selection, or underlying metrics).
    // ═══════════════════════════════════════════════════════════════════════

    using ProGlassAutomation.ViewModels;
    using System.Collections.ObjectModel;
    using System.Collections.Specialized;
    using System.ComponentModel;
    using System.Linq;

    namespace ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges
    {
        /// <summary>
        /// Two-part spec-target selector for one OtherChargeRow.
        /// Combines multi-select spec checkboxes with a single metric choice.
        /// </summary>
        public class SpecTargetSelection : ViewModelBase
        {
            /// <summary>Unit of the parent row — filters which metrics are shown.</summary>
            public OtherChargeUnit Unit { get; }

            /// <summary>Part A: which specs are ticked.</summary>
            public ObservableCollection<SpecCheckOption> AvailableSpecs { get; } = new();

            /// <summary>Part B: which metric to sum.</summary>
            public ObservableCollection<MetricOption> AvailableMetrics { get; } = new();

            private MetricOption? _selectedMetric;
            public MetricOption? SelectedMetric
            {
                get => _selectedMetric;
                set
                {
                    if (_selectedMetric == value) return;
                    _selectedMetric = value;
                    OnPropertyChanged();
                    RaiseComputed();
                }
            }

            // ── Live computed value ─────────────────────────────────────────

            public decimal ComputedValue
            {
                get
                {
                    if (_selectedMetric == null) return 0m;
                    decimal sum = 0m;
                    foreach (var spec in AvailableSpecs)
                    {
                        if (spec.IsChecked)
                            sum += spec.GetMetricValue(_selectedMetric.Key);
                    }
                    return sum;
                }
            }

            // ── Summary text for toggle button ──────────────────────────────

            public string SummaryText
            {
                get
                {
                    var checkedSpecs = AvailableSpecs.Where(s => s.IsChecked).ToList();
                    if (checkedSpecs.Count == 0)
                        return "Select specs and metric...";

                    string specList = "Spec " + string.Join(", ",
                        checkedSpecs.Select(s => s.SpecGroupNumber));

                    string metricLabel = _selectedMetric?.ShortLabel ?? "—";

                    string valueText = Unit switch
                    {
                        OtherChargeUnit.Sqm => $"{ComputedValue:0.##} sqm",
                        OtherChargeUnit.Lm => $"{ComputedValue:0.##} lm",
                        OtherChargeUnit.Qty => $"{(int)ComputedValue} pcs",
                        _ => ComputedValue.ToString("0.##")
                    };

                    return $"{specList} · {metricLabel} · {valueText}";
                }
            }

            // ── Constructor ─────────────────────────────────────────────────

            public SpecTargetSelection(OtherChargeUnit unit)
            {
                Unit = unit;

                // Populate metric options filtered by unit
                foreach (var metric in MetricOption.ForUnit(unit))
                    AvailableMetrics.Add(metric);

                AvailableSpecs.CollectionChanged += OnSpecsCollectionChanged;
            }

            // ── Spec collection change tracking ─────────────────────────────

            private void OnSpecsCollectionChanged(
                object? sender, NotifyCollectionChangedEventArgs e)
            {
                if (e.OldItems != null)
                    foreach (SpecCheckOption s in e.OldItems)
                        s.PropertyChanged -= OnSpecCheckChanged;

                if (e.NewItems != null)
                    foreach (SpecCheckOption s in e.NewItems)
                        s.PropertyChanged += OnSpecCheckChanged;

                RaiseComputed();
            }

            private void OnSpecCheckChanged(
                object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(SpecCheckOption.IsChecked))
                    RaiseComputed();
            }

            /// <summary>
            /// Notifies both ComputedValue and SummaryText.
            /// Called externally by OtherChargesViewModel when underlying
            /// per-spec metrics are refreshed.
            /// </summary>
            public void RaiseComputed()
            {
                OnPropertyChanged(nameof(ComputedValue));
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }