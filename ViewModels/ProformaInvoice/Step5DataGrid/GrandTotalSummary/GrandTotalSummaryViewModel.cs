using ProGlassAutomation.Models.ProformaInvoice.Step5DataGrid.GrandTotalSummary;
using ProGlassAutomation.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid.GrandTotalSummary
{
    /// <summary>
    /// Displays a read-only per-spec-group rollup with a grand total footer.
    /// Auto-refreshes whenever spec groups change or any row inside them changes.
    /// </summary>
    public class GrandTotalSummaryViewModel : ViewModelBase
    {
        private readonly ObservableCollection<SpecGroup> _specGroups;

        public ObservableCollection<SpecGroupSummaryRow> SummaryRows { get; } = new();

        // ── Grand totals (across all spec groups) ─────────────────────────

        private int _totalSpecGroups;
        public int TotalSpecGroups
        {
            get => _totalSpecGroups;
            private set { if (_totalSpecGroups == value) return; _totalSpecGroups = value; OnPropertyChanged(); }
        }

        private int _grandTotalQty;
        public int GrandTotalQty
        {
            get => _grandTotalQty;
            private set { if (_grandTotalQty == value) return; _grandTotalQty = value; OnPropertyChanged(); }
        }

        private decimal _grandTotalSqm;
        public decimal GrandTotalSqm
        {
            get => _grandTotalSqm;
            private set { if (_grandTotalSqm == value) return; _grandTotalSqm = value; OnPropertyChanged(); }
        }

        private decimal _grandTotalLm;
        public decimal GrandTotalLm
        {
            get => _grandTotalLm;
            private set { if (_grandTotalLm == value) return; _grandTotalLm = value; OnPropertyChanged(); }
        }

        private decimal _grandTotalValue;
        public decimal GrandTotalValue
        {
            get => _grandTotalValue;
            private set { if (_grandTotalValue == value) return; _grandTotalValue = value; OnPropertyChanged(); }
        }

        // ── Constructor ───────────────────────────────────────────────────

        public GrandTotalSummaryViewModel(ObservableCollection<SpecGroup> specGroups)
        {
            _specGroups = specGroups ?? throw new ArgumentNullException(nameof(specGroups));

            // Subscribe to top-level collection changes
            _specGroups.CollectionChanged += OnSpecGroupsCollectionChanged;

            // Subscribe to existing groups' property changes
            foreach (var group in _specGroups)
                group.PropertyChanged += OnSpecGroupChanged;

            Refresh();
        }

        // ── Change tracking ───────────────────────────────────────────────

        private void OnSpecGroupsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (SpecGroup g in e.OldItems)
                    g.PropertyChanged -= OnSpecGroupChanged;

            if (e.NewItems != null)
                foreach (SpecGroup g in e.NewItems)
                    g.PropertyChanged += OnSpecGroupChanged;

            Refresh();
        }

        private void OnSpecGroupChanged(object sender, PropertyChangedEventArgs e)
        {
            // Only recompute when a total or label-affecting property changes
            if (e.PropertyName is nameof(SpecGroup.FooterTotalQty)
                or nameof(SpecGroup.FooterTotalSqm)
                or nameof(SpecGroup.FooterTotalLm1)
                or nameof(SpecGroup.FooterTotalValue)
                or nameof(SpecGroup.GroupNumber)
                or nameof(SpecGroup.SelectedGlassType)
                or nameof(SpecGroup.SelectedSpecName))
            {
                Refresh();
            }
        }

        // ── Refresh logic ─────────────────────────────────────────────────

        /// <summary>
        /// Rebuilds SummaryRows from the current spec-groups snapshot
        /// and recomputes the grand totals.
        /// </summary>
        public void Refresh()
        {
            SummaryRows.Clear();

            int grandQty = 0;
            decimal grandSqm = 0m, grandLm = 0m, grandVal = 0m;

            foreach (var group in _specGroups)
            {
                var row = new SpecGroupSummaryRow
                {
                    GroupNumber = group.GroupNumber,
                    DisplayLabel = BuildDisplayLabel(group),
                    TotalQty = group.FooterTotalQty,
                    TotalSqm = group.FooterTotalSqm,
                    TotalLm = group.FooterTotalLm1,
                    TotalValue = group.FooterTotalValue
                };

                SummaryRows.Add(row);

                grandQty += row.TotalQty;
                grandSqm += row.TotalSqm;
                grandLm += row.TotalLm;
                grandVal += row.TotalValue;
            }

            TotalSpecGroups = _specGroups.Count;
            GrandTotalQty = grandQty;
            GrandTotalSqm = Math.Round(grandSqm, 3);
            GrandTotalLm = Math.Round(grandLm, 3);
            GrandTotalValue = MoneyRounding.Round(grandVal);
        }

        /// <summary>
        /// Format: "DGU · 6mm HD Grey FT"
        /// Falls back gracefully when data is missing.
        /// </summary>
        private static string BuildDisplayLabel(SpecGroup group)
        {
            string glassType = string.IsNullOrWhiteSpace(group.SelectedGlassType) ? "—" : group.SelectedGlassType;
            string specName = string.IsNullOrWhiteSpace(group.SelectedSpecName) ? $"Spec {group.GroupNumber}" : group.SelectedSpecName;
            return $"{glassType} · {specName}";
        }
    }
}