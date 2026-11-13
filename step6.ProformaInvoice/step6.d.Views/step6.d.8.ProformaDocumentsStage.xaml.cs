// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.d.Views/step6.d.8.ProformaDocumentsStage.xaml.cs
// MODULE: step6 — the only view code in the module: choosing a file.
//
// A file picker is the one thing a view has to do itself, so it is done here and
// nowhere else. The path is handed to the view model, which does the copying,
// the size check and the message the operator reads afterwards.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProGlassAutomation.ViewModels.Proforma;

namespace ProGlassAutomation.Proforma;

/// <summary>Stage 6: the documents attached to the invoice.</summary>
public partial class ProformaDocumentsStage : UserControl
{
    public ProformaDocumentsStage() => InitializeComponent();

    private async void OnAttachFile(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProformaWorkspaceViewModel viewModel) return;

        OpenFileDialog dialog = new()
        {
            Title = "Attach a document to this invoice",
            CheckFileExists = true,
            Multiselect = false,
            Filter = "Documents (*.pdf;*.png;*.jpg;*.jpeg;*.xlsx;*.docx)|*.pdf;*.png;*.jpg;*.jpeg;*.xlsx;*.docx"
                + "|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            await viewModel.AttachFileAsync(dialog.FileName);
        }
        catch (Exception error)
        {
            viewModel.Report("The file could not be attached: " + error.GetBaseException().Message);
        }
    }
}
