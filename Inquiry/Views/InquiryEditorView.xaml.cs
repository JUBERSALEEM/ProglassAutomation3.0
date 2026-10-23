using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProGlassAutomation.Services.Inquiry;
using ProGlassAutomation.ViewModels.Inquiry;

namespace ProGlassAutomation.Inquiry;

/// <summary>
/// Inquiry detail / editor page. Code-behind hosts the dialog callbacks
/// the VM stays MessageBox-free for; every business decision is made in
/// InquiryEditorViewModel.
/// </summary>
public partial class InquiryEditorView : UserControl
{
    public InquiryEditorView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not InquiryEditorViewModel vm) return;

        vm.SelectFilesRequested = () =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Attach files to this inquiry",
                Multiselect = true,
                Filter = "All allowed formats|*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.dwg;*.dxf;*.xlsx;*.xls;*.csv"
                        + "|PDF documents (*.pdf)|*.pdf"
                        + "|Images (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp"
                        + "|CAD drawings (*.dwg;*.dxf)|*.dwg;*.dxf"
                        + "|Spreadsheets (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv"
            };
            return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileNames : null;
        };

        vm.ConfirmDiscardRequested = () =>
            MessageBox.Show(Window.GetWindow(this),
                "You have unsaved changes. Discard them?",
                "Discard changes?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;

        vm.ConfirmDeleteRequested = item =>
            MessageBox.Show(Window.GetWindow(this),
                $"Delete '{item.FileName}' from this inquiry?",
                "Delete attachment?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;

        vm.OpenAttachmentsFolderRequested = () =>
        {
            if (vm.Record.Id <= 0) return;
            var result = FileOpener.TryOpenFolder(InquiryAttachmentService.StorageFolder(vm.Record.Id));
            if (!result.IsSuccess)
                MessageBox.Show(Window.GetWindow(this), result.Error, "Attachments", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
    }
}
