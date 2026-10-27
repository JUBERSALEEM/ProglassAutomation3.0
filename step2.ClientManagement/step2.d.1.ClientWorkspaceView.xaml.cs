using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProGlassAutomation.ViewModels.Clients;

namespace ProGlassAutomation.Clients;

/// <summary>
/// step2 Client Management workspace: owns the single workspace view model and
/// switches register ↔ editor in place (same page pattern as the Inquiry
/// module, whose workspace this mirrors).
///
/// Code-behind is limited to composition and navigation: creating the view
/// model, starting the one-time database initialization when the tab is first
/// shown, and opening the selected row on double-click or Enter. Every rule,
/// query and write lives in step2.a/step2.b/step2.c.
///
/// A database failure must never take the shell down: initialization reports
/// the failure on the workspace error surface instead of throwing here.
/// </summary>
public partial class ClientWorkspaceView : UserControl
{
    private readonly ClientWorkspaceViewModel _viewModel;

    public ClientWorkspaceView()
    {
        InitializeComponent();
        _viewModel = new ClientWorkspaceViewModel();
        DataContext = _viewModel;
        Loaded += OnWorkspaceLoaded;
    }

    private async void OnWorkspaceLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnWorkspaceLoaded;
        try
        {
            // EnsureInitializedAsync catches its own failures and surfaces them
            // through ErrorMessage, so nothing is hidden and nothing crashes.
            await _viewModel.EnsureInitializedAsync();
        }
        catch (Exception error)
        {
            // Defensive: even an unexpected failure must not escape an async
            // void handler and terminate the application.
            MessageBox.Show(
                error.ToString(),
                "The client directory could not be opened",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedRow();
        e.Handled = true;
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        OpenSelectedRow();
        e.Handled = true;
    }

    private void OpenSelectedRow()
    {
        if (_viewModel.SelectedRow == null) return;
        if (_viewModel.EditSelectedCommand.CanExecute(null))
            _viewModel.EditSelectedCommand.Execute(null);
    }
}
