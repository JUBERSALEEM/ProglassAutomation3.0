// ═══════════════════════════════════════════════════════════════════════
// FILE: Views/ProformaInvoice/Step9ProfitLoss/ServiceProfitabilityView.xaml.cs
// PROJECT: ProGlassAutomation
// MODULE: Service Profitability Analysis View
// ═══════════════════════════════════════════════════════════════════════

using System.Windows.Controls;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;

namespace ProGlassAutomation.Views.ProformaInvoice.Step9ProfitLoss
{
    /// <summary>
    /// Interaction logic for ServiceProfitabilityView.xaml
    /// </summary>
    public partial class ServiceProfitabilityView : UserControl
    {
        public ServiceProfitabilityView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Sets the DataContext to the provided ViewModel.
        /// </summary>
        public void SetViewModel(ServiceProfitabilityViewModel viewModel)
        {
            DataContext = viewModel;
        }
    }
}
