// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.d.1.ProjectWorkspaceView.xaml.cs
// MODULE: step5 — composition for the project workspace.
//
// Code-behind is limited to composition: creating the one view model this
// workspace owns and starting the one-time schema initialization when the tab is
// first shown. Every rule, query and write lives in step5.a/step5.b/step5.c.
//
// A database failure must never take the shell down: initialization reports the
// failure on the workspace error surface instead of throwing here.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Windows;
using System.Windows.Controls;
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
