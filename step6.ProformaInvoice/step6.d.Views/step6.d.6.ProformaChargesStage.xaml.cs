// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.d.Views/step6.d.6.ProformaChargesStage.xaml.cs
// MODULE: step6 — the one thing a charge toggle has to tell the engine.
//
// A checkbox can write IsActive on its own, but "the operator switched this off"
// is a different fact from "the catalogue suggested it": the row records the
// manual decision, and the invoice is recomputed so the totals follow at once.
// Keeping that here means the pricing engine keeps one rule, not two.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Windows;
using System.Windows.Controls;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.ViewModels.Proforma;

namespace ProGlassAutomation.Proforma;

/// <summary>Stage 4: catalogue, wastage and custom charges.</summary>
public partial class ProformaChargesStage : UserControl
{
    public ProformaChargesStage() => InitializeComponent();

    private void OnWastageCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit
            || DataContext is not ProformaWorkspaceViewModel viewModel) return;

        // The binding commits after CellEditEnding. Queue recalculation so the
        // new sheet quantity/rate is included in the P&L immediately.
        Dispatcher.BeginInvoke(new Action(viewModel.Reapply));
    }

    private void OnChargeToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || DataContext is not ProformaWorkspaceViewModel viewModel) return;

        switch (box.DataContext)
        {
            // The workspace decides, not the view: it records that the operator,
            // not the catalogue, made this choice, and recomputes the sheet.
            case PredefinedChargeRow predefined:
                viewModel.ApplyChargeToggle(predefined);
                break;
            case WastageChargeRow wastage:
                viewModel.ApplyWastageToggle(wastage);
                break;
        }

        viewModel.Reapply();
    }
}
