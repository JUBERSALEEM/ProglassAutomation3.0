// ═══════════════════════════════════════════════════════════════════════
// FILE: Views/ProformaInvoice/Step9ProfitLoss/ProfitLossStepView.xaml.cs
// PROJECT: ProGlassAutomation
// MODULE: Wizard Step 9 — Profit & Loss (PHASE B)
//
// Pure MVVM: no event handlers, no logic. Every action on this screen is a
// command on ProfitLossStepViewModel; the code-behind exists only to run
// InitializeComponent().
// ═══════════════════════════════════════════════════════════════════════

using System.Windows.Controls;

namespace ProGlassAutomation.Views.ProformaInvoice.Step9ProfitLoss
{
    /// <summary>
    /// Step 9 view — costs the invoice against Sheet Store and shows the
    /// profit / loss breakdown. DataContext is
    /// <c>ProfitLossStepViewModel</c>, supplied by the wizard shell.
    /// </summary>
    public partial class ProfitLossStepView : UserControl
    {
        public ProfitLossStepView()
        {
            InitializeComponent();
        }
    }
}
