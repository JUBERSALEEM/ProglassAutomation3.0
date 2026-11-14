using System;
using System.Windows.Controls;
using ProGlassAutomation.ViewModels.Proforma;

namespace ProGlassAutomation.Proforma;

public partial class ProformaGridStage : UserControl
{
    public ProformaGridStage() => InitializeComponent();

    private void OnPolishSubTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.AddedItems.Count == 0
            || DataContext is not ProformaWorkspaceViewModel viewModel) return;

        Dispatcher.BeginInvoke(new Action(viewModel.Reapply));
    }
}
