using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProGlassAutomation.ViewModels.Users;

namespace ProGlassAutomation.Users;

/// <summary>
/// step3 User Management workspace: owns the single workspace view model and
/// switches register ↔ editor in place (the same page pattern as step2 Client
/// Management).
///
/// Code-behind is limited to composition and navigation: creating the view
/// model, starting the one-time database initialization when the tab is first
/// shown, and opening the selected row on double-click or Enter. Every rule,
/// query and write lives in step3.a/step3.b/step3.c.
///
/// A database failure must never take the shell down: initialization reports
/// the failure on the workspace error surface instead of throwing here.
/// </summary>
public partial class UserWorkspaceView : UserControl
{
    private readonly UserWorkspaceViewModel _viewModel;

    public UserWorkspaceView()
    {
        InitializeComponent();
        _viewModel = new UserWorkspaceViewModel();
        DataContext = _viewModel;
        Loaded += OnWorkspaceLoaded;
        IsVisibleChanged += OnWorkspaceVisibilityChanged;
    }

    /// <summary>
    /// Coming back to the page re-reads the directory. The first-run gate may have
    /// created the first administrator while this page was hidden, and a stale
    /// "not registered" state on return would look exactly like the lock-out the
    /// gate was built to remove.
    /// </summary>
    private async void OnWorkspaceVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!IsVisible || !IsLoaded) return;
        try
        {
            await _viewModel.RefreshAsync();
        }
        catch (Exception error)
        {
            // The view model surfaces its own failures; never rethrow from an
            // async void handler.
            System.Diagnostics.Debug.WriteLine("User workspace refresh failed: " + error);
        }
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
                "The user directory could not be opened",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedRow();
        e.Handled = true;
    }

    /// <summary>
    /// Opens the command bar's overflow menu on a left click.
    ///
    /// A Button.ContextMenu only opens on right-click by its own wiring, so the
    /// menu a user can see needs one line of code-behind to show it. The menu
    /// contents themselves are a themed ContextMenu with MenuItem children, both
    /// of which every package styles.
    /// </summary>

    /// <summary>Same one-liner for the per-row menu inside the register.</summary>
    private void OnRowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
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
