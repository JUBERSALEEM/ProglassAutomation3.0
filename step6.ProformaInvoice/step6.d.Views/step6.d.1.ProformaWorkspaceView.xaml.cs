// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.d.Views/step6.d.1.ProformaWorkspaceView.xaml.cs
// MODULE: step6 — composition for the proforma invoice page.
//
// Code-behind is composition and nothing else: it creates the one view model
// this page owns and asks it for its first read when the tab is first shown.
// Every rule, query and total lives in step6.a–step6.c.
//
// A database failure must never take the shell down, so initialization reports
// through the page's own error surface instead of throwing here.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Windows.Controls;
using ProGlassAutomation.ViewModels.Proforma;

namespace ProGlassAutomation.Proforma;

/// <summary>The Proforma Invoice page: register and editor.</summary>
public partial class ProformaWorkspaceView : UserControl
{
    private readonly ProformaWorkspaceViewModel _viewModel;
    private bool _started;

    public ProformaWorkspaceView()
    {
        InitializeComponent();
        _viewModel = new ProformaWorkspaceViewModel();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // The tab is created once; the first paint is the only time the page
        // prepares its tables and reads the register.
        if (_started) return;
        _started = true;
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception error)
        {
            _viewModel.Report("The invoice register could not be prepared: "
                + error.GetBaseException().Message);
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e) => _viewModel.Dispose();
}
