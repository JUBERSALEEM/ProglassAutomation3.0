// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.Binding.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 3.4 — partial split. This part owns the ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.Binding members.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.ProformaInvoice;
using ProGlassAutomation.Services.Reporting;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguLamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.LamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.SguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Text.RegularExpressions; // Match still used while production rows read spacer/pvb groups
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    public partial class ProfitLossStepViewModel : ViewModelBase
    {
        // ══════════════════════════════════════════════════════════════
        //  BINDING TO THE REST OF THE WIZARD
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Wires this step to its data sources. Called once by the wizard
        /// shell after all step VMs are constructed. Any argument may be
        /// null; the step then simply produces an empty report.
        /// </summary>
        public void Bind(
            DataGridViewModel? dataGrid,
            DguParametersViewModel? dguVm,
            SguParametersViewModel? sguVm,
            Func<decimal>? grossProvider,
            LamParametersViewModel? lamVm = null,
            DguLamParametersViewModel? dguLamVm = null,
            OtherChargesViewModel? otherChargesVm = null,
            Func<decimal>? netRevenueProvider = null,
            INotifyPropertyChanged? revenueSource = null,
            Func<IReadOnlyList<Step4PricingSnapshot>>? step4SnapshotProvider = null,
            Func<ReportDocumentContext>? reportDocumentContextProvider = null)
        {
            _dataGrid = dataGrid;
            _dguVm = dguVm;
            _sguVm = sguVm;
            _lamVm = lamVm;
            _dguLamVm = dguLamVm;
            _otherChargesVm = otherChargesVm;
            _grossProvider = grossProvider;
            _netRevenueProvider = netRevenueProvider;
            _revenueSource = revenueSource;
            _step4SnapshotProvider = step4SnapshotProvider;
            _reportDocumentContextProvider = reportDocumentContextProvider;

            // Full subscribe/unsubscribe cycle: Bind() can be re-invoked for a
            // freshly loaded invoice, and every old handler must be detached
            // first (see RebindToCurrentSources in ProfitLossStepViewModel.LiveRecalc.cs).
            RebindToCurrentSources();
        }

        /// <summary>
        /// Projects the current Step 1–3 PI identity at export time. The
        /// shell supplies a delegate rather than a cached snapshot, so edits
        /// made immediately before exporting are reflected in every Step 9
        /// artifact.
        /// </summary>
        internal ReportDocumentContext GetReportDocumentContext() =>
            _reportDocumentContextProvider?.Invoke() ?? new ReportDocumentContext();

    }
}
