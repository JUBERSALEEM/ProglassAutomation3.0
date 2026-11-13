// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.d.Views/step6.d.6.ProformaChargesStage.xaml.cs
// MODULE: step6 — the one thing a charge toggle has to tell the engine.
//
// A checkbox can write IsActive on its own, but "the operator switched this off"
// is a different fact from "the catalogue suggested it": the row records the
// manual decision, and the invoice is recomputed so the totals follow at once.
// Keeping that here means the pricing engine keeps one rule, not two.
// ═══════════════════════════════════════════════════════════════════════

using System.Windows;
using System.Windows.Controls;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.ViewModels.Proforma;

namespace ProGlassAutomation.Proforma;

/// <summary>Stage 4: catalogue, wastage and custom charges.</summary>
public partial class ProformaChargesStage : UserControl
{
    public ProformaChargesStage() => InitializeComponent();

    private void OnChargeToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox box || DataContext is not ProformaWorkspaceViewModel viewModel) return;

        switch (box.DataContext)
        {
            case PredefinedChargeRow predefined:
                // Records that the operator, not the catalogue, decided this row.
                predefined.SetManualActive(predefined.IsActive);
                break;
            case WastageChargeRow wastage:
                wastage.Recalculate();
                break;
        }

        viewModel.Reapply();
    }
}
