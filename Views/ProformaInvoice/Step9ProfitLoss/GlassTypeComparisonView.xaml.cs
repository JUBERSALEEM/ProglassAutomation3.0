// ═══════════════════════════════════════════════════════════════════════
// FILE: Views/ProformaInvoice/Step9ProfitLoss/GlassTypeComparisonView.xaml.cs
// PROJECT: ProGlassAutomation
// MODULE: Glass Type Comparison Analysis View
// ═══════════════════════════════════════════════════════════════════════

using System.Windows.Controls;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;

namespace ProGlassAutomation.Views.ProformaInvoice.Step9ProfitLoss
{
    /// <summary>
    /// Interaction logic for GlassTypeComparisonView.xaml
    /// </summary>
    public partial class GlassTypeComparisonView : UserControl
    {
        public GlassTypeComparisonView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Sets the DataContext to the provided ViewModel.
        /// </summary>
        public void SetViewModel(GlassTypeComparisonViewModel viewModel)
        {
            DataContext = viewModel;
        }
    }
}
