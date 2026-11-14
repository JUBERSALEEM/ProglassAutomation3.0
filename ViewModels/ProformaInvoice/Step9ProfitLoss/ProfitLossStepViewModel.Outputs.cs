// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.Outputs.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 3.4 — partial split. This part owns the ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.Outputs members.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.ProformaInvoice;
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
        //  OUTPUTS
        // ══════════════════════════════════════════════════════════════

        /// <summary>Costed breakdown, rebuilt by every recalculation.</summary>
        public ObservableCollection<ProfitLossRow> Rows { get; } = new();

        /// <summary>True when <see cref="Rows"/> is empty (drives the empty state).</summary>
        public bool HasRows => Rows.Count > 0;

        /// <summary>
        /// The same cost rows, grouped by specification, each with its own
        /// cost per SQM and rate check.
        ///
        /// This is what the report shows. The flat <see cref="Rows"/> list is
        /// kept because the snapshot, the printable invoice and the saved
        /// history all read from it, and changing that shape would rewrite
        /// stored financial records.
        /// </summary>
        public ObservableCollection<SpecCostGroup> SpecGroupsView { get; } = new();

        /// <summary>True once at least one specification has been costed.</summary>
        public bool HasSpecGroups => SpecGroupsView.Count > 0;

    }
}
