// ═══════════════════════════════════════════════════════════════════════
// FILE: Views/ProformaInvoice/Step9ProfitLoss/SheetProfitabilityView.xaml.cs
// PROJECT: ProGlassAutomation
// MODULE: Sheet Profitability Analysis View
// ═══════════════════════════════════════════════════════════════════════

using System.Windows.Controls;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;

namespace ProGlassAutomation.Views.ProformaInvoice.Step9ProfitLoss
{
    /// <summary>
    /// Interaction logic for SheetProfitabilityView.xaml
    /// </summary>
    public partial class SheetProfitabilityView : UserControl
    {
        public SheetProfitabilityView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Sets the DataContext to the provided ViewModel.
        /// </summary>
        public void SetViewModel(SheetProfitabilityViewModel viewModel)
        {
            DataContext = viewModel;
        }
    }
}
