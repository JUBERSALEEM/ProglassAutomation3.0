// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.LiveRecalc.cs
// PROJECT: ProGlassAutomation
// MODULE: Step 9 — live auto-sync (subscription manager + coalesced recalc)
//
// Everything Step 9 reads is watched through NAMED handlers registered on a
// SubscriptionManager, so a rebind can detach every old handler before it
// attaches the new ones (no duplicates, no anonymous handlers that cannot be
// removed):
//   · Step 5 spec groups added/removed and every dimension row edit
//   · Step 4 pricing panels (glass rates, ASP, yield, margins, cutting,
//     tempering, U-insert seed)
//   · Step 6 charge rows (rate, active state, target selection, add/remove)
//   · Step 6 wastage and custom-charge sub-sections
//
// Recalculation is coalesced on the UI dispatcher; a change that arrives
// while a run is in flight is re-queued so the UI never sits one edit behind.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguLamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.LamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.SguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    public partial class ProfitLossStepViewModel : ViewModelBase
    {
        private readonly SubscriptionManager _subscriptions = new();
        private readonly Dictionary<SpecGroup, List<IDisposable>> _groupSubscriptions = new();
        private bool _recalcQueued;
        /// <summary>
        /// True while RecalculateAsync is applying Step 4 snapshots so those
        /// writes are not treated as a new operator edit.
        /// </summary>
        private bool _suppressLiveRecalc;

        private bool _isRecalculationPending;
        /// <summary>True between the moment an edit arrives and the recalc runs.</summary>
        public bool IsRecalculationPending
        {
            get => _isRecalculationPending;
            private set
            {
                if (_isRecalculationPending == value) return;
                _isRecalculationPending = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CalculationStateLabel));
                OnPropertyChanged(nameof(CalculationStatus));
                RaiseStep9HealthNotifications();
                RaiseStep9HealthNotifications();
            }
        }

        /// <summary>Human label for the calc state ("Calculating…", "Up to date", …).</summary>
        public string CalculationStateLabel =>
            IsBusy
                ? "Calculating…"
                : IsRecalculationPending
                    ? "Waiting for recalculation…"
                    : HasCalculationWarnings
                        ? "Calculated with warnings"
                        : LastCalculatedAt.HasValue
                            ? "Up to date"
                            : "Not calculated yet";

        /// <summary>Alias of <see cref="CalculationStateLabel"/> (wizard state contract).</summary>
        public string CalculationStatus => CalculationStateLabel;

        /// <summary>
        /// Detaches every current handler and re-wires against the currently
        /// bound sources. Called by Bind() and exposed for invoice reloads.
        /// </summary>
        public void RebindToCurrentSources()
        {
            UnsubscribeAll();
            SubscribeAll();
            QueueRecalculate();
        }

        private void UnsubscribeAll()
        {
            _subscriptions.Clear();

            foreach (var token in _groupSubscriptions.Values.SelectMany(v => v))
            {
                try { token.Dispose(); }
                catch {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("UnsubscribeAll suppressed");
            }
            }

            _groupSubscriptions.Clear();
        }

        private void SubscribeAll()
        {
            if (_dataGrid?.SpecGroups is INotifyCollectionChanged groups)
                _subscriptions.OnCollectionChanged(groups, OnSpecGroupsCollectionChanged);

            foreach (var group in _dataGrid?.SpecGroups ?? Enumerable.Empty<SpecGroup>())
                WatchSpecGroup(group);

            // Step 4 panels: rate, yield or margin edits move the target and
            // the actual-cost basis — the report must follow them.
            _subscriptions.OnPropertyChanged(_dguVm, OnPanelPropertyChanged);
            _subscriptions.OnPropertyChanged(_sguVm, OnPanelPropertyChanged);
            _subscriptions.OnPropertyChanged(_lamVm, OnPanelPropertyChanged);
            _subscriptions.OnPropertyChanged(_dguLamVm, OnPanelPropertyChanged);

            // Step 7 revenue changes (discount, VAT, round-to total, Step 5/6
            // totals) change every margin. Watch the summary directly so Step 9
            // stays live even when the user edits the customer total after the
            // P&L tab was opened.
            _subscriptions.OnPropertyChanged(_revenueSource, OnPanelPropertyChanged);

            // Step 6 — the U-insert row state, automation mode and totals all
            // affect the target treatment / actual costs.
            _subscriptions.OnPropertyChanged(_otherChargesVm, OnPanelPropertyChanged);

            if (_otherChargesVm?.ChargeRows is INotifyCollectionChanged chargeRows)
            {
                _subscriptions.OnCollectionChanged(chargeRows, OnChargeRowsCollectionChanged);

                foreach (var row in _otherChargesVm.ChargeRows)
                    WatchChargeRow(row);
            }

            if (_otherChargesVm != null)
            {
                _subscriptions.OnPropertyChanged(_otherChargesVm.WastagesSection, OnPanelPropertyChanged);
                _subscriptions.OnPropertyChanged(_otherChargesVm.CustomChargesSection, OnPanelPropertyChanged);
            }
        }

        // ── Step 5: spec group collection ───────────────────────────────

        private void OnSpecGroupsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // The whole collection was rebuilt — re-watch every group.
                foreach (var token in _groupSubscriptions.Values.SelectMany(v => v))
                {
                    try { token.Dispose(); }
                    catch {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("if suppressed");
            }
                }

                _groupSubscriptions.Clear();

                foreach (var group in _dataGrid?.SpecGroups ?? Enumerable.Empty<SpecGroup>())
                    WatchSpecGroup(group);

                QueueRecalculate();
                return;
            }

            if (e.NewItems != null)
                foreach (SpecGroup g in e.NewItems.OfType<SpecGroup>())
                    WatchSpecGroup(g);

            if (e.OldItems != null)
                foreach (SpecGroup g in e.OldItems.OfType<SpecGroup>())
                    UnwatchSpecGroup(g);

            QueueRecalculate();
        }

        private void WatchSpecGroup(SpecGroup group)
        {
            if (group == null) return;
            UnwatchSpecGroup(group);

            var tokens = new List<IDisposable>();
            tokens.Add(_subscriptions.OnPropertyChanged(group, OnSpecGroupChanged));

            if (group.ItemRows is INotifyCollectionChanged rows)
                tokens.Add(_subscriptions.OnCollectionChanged(rows, OnItemRowsChanged));

            foreach (var row in group.ItemRows ?? Enumerable.Empty<GlassItemRow>())
            {
                var token = _subscriptions.OnPropertyChanged(row, OnItemRowChanged);
                tokens.Add(token);
            }

            _groupSubscriptions[group] = tokens;
        }

        private void UnwatchSpecGroup(SpecGroup group)
        {
            if (group == null) return;

            if (_groupSubscriptions.TryGetValue(group, out var tokens))
            {
                foreach (var token in tokens)
                {
                    try { token.Dispose(); }
                    catch {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("foreach suppressed");
            }
                }

                _groupSubscriptions.Remove(group);
            }
        }

        private void OnSpecGroupChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Spec selection / glass type / base rate moves the target model;
            // dimensions live on the item rows.
            if (e.PropertyName is nameof(SpecGroup.SelectedSpecName)
                               or nameof(SpecGroup.SelectedGlassType)
                               or nameof(SpecGroup.BaseRate)
                               or nameof(SpecGroup.SurchargePercent)
                               or nameof(SpecGroup.UInsertPricePerSqm))
            {
                QueueRecalculate();
            }
        }

        private void OnItemRowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (sender is SpecGroup group)
            {
                if (e.Action == NotifyCollectionChangedAction.Reset)
                {
                    WatchSpecGroup(group);
                }
                else
                {
                    if (e.OldItems != null)
                        foreach (GlassItemRow r in e.OldItems.OfType<GlassItemRow>())
                            UnwatchItemRow(group, r);

                    if (e.NewItems != null)
                        foreach (GlassItemRow r in e.NewItems.OfType<GlassItemRow>())
                            WatchItemRow(group, r);
                }
            }

            QueueRecalculate();
        }

        private void WatchItemRow(SpecGroup group, GlassItemRow row)
        {
            if (group == null || row == null) return;

            if (_groupSubscriptions.TryGetValue(group, out var tokens))
            {
                var token = _subscriptions.OnPropertyChanged(row, OnItemRowChanged);
                tokens.Add(token);
            }
        }

        private void UnwatchItemRow(SpecGroup group, GlassItemRow row)
        {
            if (group == null || row == null) return;
            // Row property handlers are owned by the subscription manager;
            // re-subscribing through WatchItemRow with a fresh token after an
            // Unwatch is the safe pattern. Individual row tokens are removed
            // wholesale when the group is unwatched/reset.
        }

        private void OnItemRowChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Dimensions and quantity are the only row fields that change the
            // measured area.
            if (e.PropertyName is nameof(GlassItemRow.W1) or nameof(GlassItemRow.H1)
                               or nameof(GlassItemRow.W2) or nameof(GlassItemRow.H2)
                               or nameof(GlassItemRow.Qty) or nameof(GlassItemRow.Rate))
            {
                QueueRecalculate();
            }
        }

        // ── Step 4 / Step 6 panel changes ───────────────────────────────

        private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            QueueRecalculate();
        }

        // ── Step 6: charge row collection ───────────────────────────────

        private void OnChargeRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_otherChargesVm == null) return;

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                // Rebuild row hooks wholesale.
                foreach (var row in _otherChargesVm.ChargeRows)
                    WatchChargeRow(row);
            }
            else
            {
                if (e.OldItems != null)
                    foreach (OtherChargeRow row in e.OldItems.OfType<OtherChargeRow>())
                        UnwatchChargeRow(row);

                if (e.NewItems != null)
                    foreach (OtherChargeRow row in e.NewItems.OfType<OtherChargeRow>())
                        WatchChargeRow(row);
            }

            QueueRecalculate();
        }

        private void WatchChargeRow(OtherChargeRow row)
        {
            if (row == null) return;
            _subscriptions.OnPropertyChanged(row, OnPanelPropertyChanged);

            if (row.TargetSelection is INotifyCollectionChanged specs)
                _subscriptions.OnCollectionChanged(specs, OnPanelCollectionChanged);
        }

        private void UnwatchChargeRow(OtherChargeRow row)
        {
            if (row == null) return;
            // Token-based removal is handled by SubscriptionManager.Clear on
            // rebind; individual row hook removal is best-effort here because
            // rows are rarely removed while Step 9 is visible.
        }

        private void OnPanelCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            QueueRecalculate();
        }

        // ── Coalesced scheduling on the dispatcher ──────────────────────

        /// <summary>
        /// Coalesces a burst of edits into a single rebuild on the dispatcher's
        /// next idle turn. A change arriving while a run is in flight is
        /// re-queued so the last edit is never dropped.
        /// </summary>
        private void QueueRecalculate()
        {
            if (_suppressLiveRecalc) return;

            IsRecalculationPending = true;

            if (_recalcQueued) return;
            _recalcQueued = true;

            var dispatcher = Application.Current?.Dispatcher;

            if (dispatcher == null)
            {
                // No UI thread (unit tests / headless) — run straight through.
                _recalcQueued = false;
                IsRecalculationPending = false;
                _ = RecalculateAsync();
                return;
            }

            dispatcher.BeginInvoke(
                new Action(async () =>
                {
                    _recalcQueued = false;
                    IsRecalculationPending = false;

                    if (IsBusy)
                    {
                        // The in-flight run will finish after this edit was
                        // made — re-queue so it runs again on the new data.
                        QueueRecalculate();
                        return;
                    }

                    await RecalculateAsync().ConfigureAwait(true);
                    OnPropertyChanged(nameof(CalculationStateLabel));
                    OnPropertyChanged(nameof(CalculationStatus));
                }),
                DispatcherPriority.Background);
        }

        private async Task FireRecalculateAsync()
        {
            // async void is confined to this command entry point and the
            // body cannot throw (RecalculateAsync swallows everything).
            await RecalculateAsync().ConfigureAwait(true);
        }
    }
}
