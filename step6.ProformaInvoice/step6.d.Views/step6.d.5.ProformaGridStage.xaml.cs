using System;
using System.Windows;
using System.Windows.Controls;
using ProGlassAutomation.ViewModels.Proforma;

namespace ProGlassAutomation.Proforma;

public partial class ProformaGridStage : UserControl
{
    public ProformaGridStage() => InitializeComponent();

    private void OnCustomPolishQuantityChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProformaWorkspaceViewModel viewModel)
            Dispatcher.BeginInvoke(new Action(viewModel.Reapply));
    }

    private void OnPolishSubTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.AddedItems.Count == 0
            || DataContext is not ProformaWorkspaceViewModel viewModel) return;

        Dispatcher.BeginInvoke(new Action(viewModel.Reapply));
    }

    private void OnCustomPolishQuantityChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || DataContext is not ProformaWorkspaceViewModel viewModel) return;
        Dispatcher.BeginInvoke(new Action(viewModel.Reapply));
    }
}
