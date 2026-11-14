// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step6OtherCharges/MetricDiscoveryHubViewModel.cs
// ═══════════════════════════════════════════════════════════════════════
// CHAT 20 — RESET BUG FIX
//
//   ROOT CAUSE OF "OTHER CHARGES RESETTING":
//     Previous Refresh() called PerSpecMetrics.Clear() then Add() × N.
//     Each Clear() fired CollectionChanged(Reset), triggering
//     OtherChargesViewModel.SyncTargetSelectionsFromHub() to run
//     against an EMPTY collection — which destroyed all user
//     spec-checkbox selections.
//     Every keystroke in Step 5 wiped Step 6 selections.
//
//   FIX:
//     Refresh() now updates PerSpecMetrics IN-PLACE.
//     Existing SpecGroupMetric objects are mutated to reflect new values.
//     Only truly new spec groups trigger CollectionChanged(Add).
//     Only truly removed spec groups trigger CollectionChanged(Remove).
//     Metric-value updates fire NO CollectionChanged event.
//
//   RESULT:
//     User's spec-target selections in Step 6 survive Step 5 edits.
//     Sync only runs when spec group set genuinely changes.
//
//   PRESERVED:
//     All global aggregate properties (TotalLm1, TotalSqm1, etc.)
//     PrimaryGlassRefs collection
//     BuildSpecDisplayName helper
//     All existing event subscription patterns
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.Services.ProformaInvoice;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges
{
    /// <summary>
    /// Read-only stats widget at the top of Step 6.
    /// Live-aggregates key metrics from the DataGrid (Step 5).
    ///
    /// CHAT 20 FIX: PerSpecMetrics is now updated IN-PLACE — no more
    /// Clear+Rebuild churn that was destroying user spec selections
    /// downstream in OtherChargesViewModel.
    /// </summary>
    public class MetricDiscoveryHubViewModel : ViewModelBase
    {
        private readonly ObservableCollection<SpecGroup> _specGroups;

        // ── Global aggregates (unchanged) ───────────────────────────────

        private decimal _totalLm1;
        public decimal TotalLm1
        {
            get => _totalLm1;
            private set { if (_totalLm1 == value) return; _totalLm1 = value; OnPropertyChanged(); }
        }

        private decimal _totalLm2;
        public decimal TotalLm2
        {
            get => _totalLm2;
            private set { if (_totalLm2 == value) return; _totalLm2 = value; OnPropertyChanged(); }
        }

        private decimal _combinedLm;
        public decimal CombinedLm
        {
            get => _combinedLm;
            private set { if (_combinedLm == value) return; _combinedLm = value; OnPropertyChanged(); }
        }

        private int _totalPieces;
        public int TotalPieces
        {
            get => _totalPieces;
            private set { if (_totalPieces == value) return; _totalPieces = value; OnPropertyChanged(); }
        }

        private int _doubleGlassPieces;
        public int DoubleGlassPieces
        {
            get => _doubleGlassPieces;
            private set { if (_doubleGlassPieces == value) return; _doubleGlassPieces = value; OnPropertyChanged(); }
        }

        private decimal _totalSqm1;
        public decimal TotalSqm1
        {
            get => _totalSqm1;
            private set { if (_totalSqm1 == value) return; _totalSqm1 = value; OnPropertyChanged(); }
        }

        private decimal _totalSqm2;
        public decimal TotalSqm2
        {
            get => _totalSqm2;
            private set { if (_totalSqm2 == value) return; _totalSqm2 = value; OnPropertyChanged(); }
        }

        private decimal _combinedSqm;
        public decimal CombinedSqm
        {
            get => _combinedSqm;
            private set { if (_combinedSqm == value) return; _combinedSqm = value; OnPropertyChanged(); }
        }

        private decimal _dguCavityArea;
        public decimal DguCavityArea
        {
            get => _dguCavityArea;
            private set { if (_dguCavityArea == value) return; _dguCavityArea = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> PrimaryGlassRefs { get; } = new();

        // ── Per-spec metrics ────────────────────────────────────────────
        //
        // CHAT 20: Updated in-place by Refresh() instead of cleared+rebuilt.
        // CollectionChanged fires ONLY for genuine add/remove operations.
        // Metric value updates on existing entries do NOT fire CollectionChanged.
        //
        // Consumers (OtherChargesViewModel) should:
        //   - Subscribe to CollectionChanged for spec add/remove events
        //   - Read values fresh from AvailableSpecs whenever they need them
        //     (no need to react to individual metric changes)
        //
        public ObservableCollection<SpecGroupMetric> PerSpecMetrics { get; } = new();

        // ── Constructor ────────────────────────────────────────────────────

        public MetricDiscoveryHubViewModel(ObservableCollection<SpecGroup> specGroups)
        {
            _specGroups = specGroups
                ?? throw new System.ArgumentNullException(nameof(specGroups));

            _specGroups.CollectionChanged += OnSpecGroupsChanged;
            foreach (var g in _specGroups)
                g.PropertyChanged += OnSpecGroupPropertyChanged;

            Refresh();
        }

        // ── Change tracking ────────────────────────────────────────────────

        private void OnSpecGroupsChanged(
            object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (SpecGroup g in e.OldItems)
                    g.PropertyChanged -= OnSpecGroupPropertyChanged;

            if (e.NewItems != null)
                foreach (SpecGroup g in e.NewItems)
                    g.PropertyChanged += OnSpecGroupPropertyChanged;

            Refresh();
        }

        private void OnSpecGroupPropertyChanged(
            object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SpecGroup.FooterTotalQty)
                or nameof(SpecGroup.FooterTotalSqm)
                or nameof(SpecGroup.FooterTotalSqm2)
                or nameof(SpecGroup.FooterTotalLm1)
                or nameof(SpecGroup.FooterTotalLm2)
                or nameof(SpecGroup.SelectedGlassType)
                or nameof(SpecGroup.SelectedSpecName)
                or nameof(SpecGroup.OuterGlassColor)
                or nameof(SpecGroup.InnerGlassColor)
                or nameof(SpecGroup.GroupName))
            {
                Refresh();
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  AGGREGATION — IN-PLACE UPDATE
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Recomputes all metrics from the current spec groups.
        ///
        /// CHAT 20 FIX: PerSpecMetrics is updated IN-PLACE:
        ///   • Existing entries are matched by SpecGroupNumber and
        ///     have their properties updated (no CollectionChanged fired)
        ///   • New spec groups are appended (CollectionChanged.Add fires)
        ///   • Deleted spec groups are removed (CollectionChanged.Remove fires)
        ///
        /// This preserves downstream consumer state — specifically the
        /// user's spec-checkbox selections in OtherChargesViewModel that
        /// were previously being destroyed by Clear+Rebuild churn.
        /// </summary>
        public void Refresh()
        {
            decimal lm1 = 0m, lm2 = 0m, sqm1 = 0m, sqm2 = 0m, dguArea = 0m;
            int qty = 0, doubleQty = 0;

            PrimaryGlassRefs.Clear();

            // Track which SpecGroupNumbers exist in the current source
            // so we can remove PerSpecMetrics entries that no longer exist.
            var currentGroupNumbers = new HashSet<int>();

            foreach (var g in _specGroups)
            {
                // A spec-name build marker is more reliable than a stale
                // Step 5 dropdown value during synchronization/reopen. Use
                // the same resolver as Step 6 automation so the metric strip,
                // source picker and charge suggestions cannot disagree.
                string effectiveGlassType =
                    OtherChargeAutomationRules.ResolveEffectiveGlassType(
                        g.SelectedGlassType, g.SelectedSpecName);
                bool isDoubleGlass = effectiveGlassType is "DGU" or "LAM" or "DGU+LAM";
                int paneFactor = effectiveGlassType == "DGU+LAM"
                    ? 3
                    : isDoubleGlass ? 2 : 1;

                currentGroupNumbers.Add(g.GroupNumber);

                // ── Per-spec metric — IN-PLACE UPDATE ──────────────────
                // Find existing metric entry for this spec group.
                // If exists → update its properties (no CollectionChanged fires)
                // If missing → create and add (CollectionChanged.Add fires)
                var existing = FindMetricByGroupNumber(g.GroupNumber);
                if (existing != null)
                {
                    // Update in place — property setters may or may not
                    // fire PropertyChanged (depends on SpecGroupMetric impl).
                    // Consumers should read values fresh when needed.
                    existing.SpecDisplayName = BuildSpecDisplayName(g);
                    existing.GlassType = effectiveGlassType;
                    existing.IsDoubleGlass = isDoubleGlass;
                    existing.PaneFactor = paneFactor;
                    existing.OuterGlassColor = g.OuterGlassColor ?? "";
                    existing.InnerGlassColor = g.InnerGlassColor ?? "";
                    existing.IsMatchedSameGlass = LaminateGlassMatcher.IsMatchedSameGlass(
                        g.SelectedSpecName, effectiveGlassType,
                        g.OuterGlassColor, g.InnerGlassColor);
                    existing.Lm1 = System.Math.Round(g.FooterTotalLm1, 2);
                    existing.Lm2 = System.Math.Round(g.FooterTotalLm2, 2);
                    existing.Sqm1 = System.Math.Round(g.FooterTotalSqm, 2);
                    existing.Sqm2 = System.Math.Round(g.FooterTotalSqm2, 2);
                    existing.Qty = g.FooterTotalQty;
                }
                else
                {
                    // New spec group — create and append.
                    // This is the ONLY path that fires CollectionChanged.Add.
                    var specMetric = new SpecGroupMetric
                    {
                        SpecGroupNumber = g.GroupNumber,
                        SpecDisplayName = BuildSpecDisplayName(g),
                        GlassType = effectiveGlassType,
                        IsDoubleGlass = isDoubleGlass,
                        PaneFactor = paneFactor,
                        OuterGlassColor = g.OuterGlassColor ?? "",
                        InnerGlassColor = g.InnerGlassColor ?? "",
                        IsMatchedSameGlass = LaminateGlassMatcher.IsMatchedSameGlass(
                            g.SelectedSpecName, effectiveGlassType,
                            g.OuterGlassColor, g.InnerGlassColor),
                        Lm1 = System.Math.Round(g.FooterTotalLm1, 2),
                        Lm2 = System.Math.Round(g.FooterTotalLm2, 2),
                        Sqm1 = System.Math.Round(g.FooterTotalSqm, 2),
                        Sqm2 = System.Math.Round(g.FooterTotalSqm2, 2),
                        Qty = g.FooterTotalQty
                    };
                    PerSpecMetrics.Add(specMetric);
                }

                // ── Global aggregation (unchanged) ─────────────────────
                lm1 += g.FooterTotalLm1;
                lm2 += g.FooterTotalLm2;
                sqm1 += g.FooterTotalSqm;
                sqm2 += g.FooterTotalSqm2;
                qty += g.FooterTotalQty;

                if (isDoubleGlass)
                {
                    doubleQty += g.FooterTotalQty * paneFactor;
                    dguArea += g.FooterTotalSqm;
                }
                else
                {
                    doubleQty += g.FooterTotalQty;
                }

                if (!string.IsNullOrWhiteSpace(g.SelectedSpecName))
                {
                    if (!PrimaryGlassRefs.Contains(g.SelectedSpecName))
                        PrimaryGlassRefs.Add(g.SelectedSpecName);
                }
            }

            // ── Remove metrics for spec groups that no longer exist ────
            // Iterate from end to avoid index shifts during removal.
            // This fires CollectionChanged.Remove for each actually-removed item.
            for (int i = PerSpecMetrics.Count - 1; i >= 0; i--)
            {
                if (!currentGroupNumbers.Contains(PerSpecMetrics[i].SpecGroupNumber))
                {
                    PerSpecMetrics.RemoveAt(i);
                }
            }

            // ── Global aggregate property assignments ──────────────────
            TotalLm1 = System.Math.Round(lm1, 2);
            TotalLm2 = System.Math.Round(lm2, 2);
            CombinedLm = System.Math.Round(lm1 + lm2, 2);
            TotalSqm1 = System.Math.Round(sqm1, 2);
            TotalSqm2 = System.Math.Round(sqm2, 2);
            CombinedSqm = System.Math.Round(sqm1 + sqm2, 2);
            DguCavityArea = System.Math.Round(dguArea, 2);
            TotalPieces = qty;
            DoubleGlassPieces = doubleQty;
        }

        /// <summary>
        /// Linear search for a metric entry by SpecGroupNumber.
        /// Collection is typically 1-10 items — linear scan is optimal.
        /// </summary>
        private SpecGroupMetric FindMetricByGroupNumber(int groupNumber)
        {
            foreach (var metric in PerSpecMetrics)
            {
                if (metric.SpecGroupNumber == groupNumber)
                    return metric;
            }
            return null;
        }

        /// <summary>
        /// Builds the display name for a spec group's dropdown header.
        /// Format: "SPEC 1 — 6mm Clear Tempered" or "SPEC 1 — (no spec selected)"
        /// </summary>
        private static string BuildSpecDisplayName(SpecGroup g)
        {
            string specName = string.IsNullOrWhiteSpace(g.SelectedSpecName)
                ? g.GroupName ?? "(unnamed)"
                : g.SelectedSpecName;
            return $"SPEC {g.GroupNumber} — {specName}";
        }
    }
}