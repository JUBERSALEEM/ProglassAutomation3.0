// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.d.1.ProjectWorkspaceView.xaml.cs
// MODULE: step5 — composition for the project workspace.
//
// Code-behind is limited to composition and to telling the view model which row
// was clicked: creating the one view model this workspace owns, starting the
// one-time schema initialization when the tab is first shown, and routing a
// register click to SelectProjectCommand. Every rule, query and write lives in
// step5.a/step5.b/step5.c.
//
// A database failure must never take the shell down: initialization reports the
// failure on the workspace error surface instead of throwing here.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProGlassAutomation.Services.Projects;
using ProGlassAutomation.ViewModels.Projects;

namespace ProGlassAutomation.Projects
{
    /// <summary>The Project Records workspace.</summary>
    public partial class ProjectWorkspaceView : UserControl
    {
        private readonly ProjectWorkspaceViewModel _viewModel;

        public ProjectWorkspaceView()
        {
            InitializeComponent();
            _viewModel = new ProjectWorkspaceViewModel();
            DataContext = _viewModel;
            Loaded += OnWorkspaceLoaded;
        }

        /// <summary>
        /// A click on a register row: open that project's inquiries, or close them
        /// if it is the row already open.
        ///
        /// ADV's shape, ported: `Views/SheetStore/SheetStoreView.xaml:439` puts
        /// `ClickToCommandBehavior.Command = SelectSheetCommand` on the row, and
        /// `SheetStoreViewModel.Part1.cs:338` toggles
        /// `SelectedSheet = ReferenceEquals(SelectedSheet, sheet) ? null : sheet`.
        /// The command owns the rule; this handler only says which row was hit.
        ///
        /// The click is always consumed: the panel is driven by the view model,
        /// and letting the grid also run its own selection would re-select the
        /// row we just closed and reopen the panel immediately.
        /// </summary>
        private void OnRegisterRowClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGridRow row) return;
            if (row.Item is not ProjectListRow project) return;

            var toggle = _viewModel.SelectProjectCommand;
            if (toggle != null && toggle.CanExecute(project)) toggle.Execute(project);
            e.Handled = true;
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
                    "The project register could not be opened",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
