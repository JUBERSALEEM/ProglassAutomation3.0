using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProGlassAutomation.Services.Inquiry;
using ProGlassAutomation.ViewModels.Inquiry;

namespace ProGlassAutomation.Inquiry;

/// <summary>
/// The Inquiry module's workspace: owns the single list view-model and
/// switches list ↔ detail in place. Created once per shell; the local
/// SQLite database (schema migrations included) is initialized here on
/// first open, before the register's first query runs.
/// </summary>
public partial class InquiryWorkspaceView : UserControl
{
    private readonly InquiryRepository _repository;
    private readonly InquiryListViewModel _list;

    public InquiryWorkspaceView()
    {
        _repository = new InquiryRepository();
        // One-time, fast: ensure schema + migrations before any query.
        // A failure here must not take the shell down — the register's
        // first load reports it on its error surface.
        try
        {
            _repository.InitializeAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Surface on the list page instead of crashing the window.
        }

        _list = new InquiryListViewModel(_repository);
        _list.EditRequested = RunEditorAsync;
        _list.OpenAttachmentsFolderRequested = row =>
        {
            var result = FileOpener.TryOpenFolder(InquiryAttachmentService.StorageFolder(row.Id));
            if (!result.IsSuccess)
                MessageBox.Show(Window.GetWindow(this), result.Error, "Attachments", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        _list.ExportPathRequested = () =>
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export filtered inquiries",
                Filter = "CSV file (*.csv)|*.csv",
                FileName = "inquiries.csv"
            };
            return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
        };
        _list.ImportPathRequested = () =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import inquiries from CSV",
                Filter = "CSV file (*.csv)|*.csv"
            };
            return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
        };
        _list.ConfirmImportRequested = count =>
            MessageBox.Show(Window.GetWindow(this),
                $"Import {count} inquiries from the file? Existing inquiry numbers keep their records; duplicates get new numbers.",
                "Import inquiries?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;
        _list.ConfirmDeleteInquiryRequested = row =>
            MessageBox.Show(Window.GetWindow(this),
                $"Delete inquiry {row.InquiryNumber} ({row.CustomerName})?\nAttachments and line items are removed too. This cannot be undone.",
                "Delete inquiry?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;

        InitializeComponent();
        ListView.DataContext = _list;
    }

    /// <summary>
    /// Opens the detail editor for <paramref name="source"/> (null for a
    /// new inquiry) and blocks until it closes. Returns true when the
    /// editor saved, so the register can reset to page 1 and reload.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> RunEditorAsync(Models.Inquiry.InquiryRecord? source)
    {
        var editor = new InquiryEditorViewModel(
            _repository,
            new InquiryWorkflowService(_repository),
            new InquiryAttachmentService(_repository),
            source);

        var closed = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        editor.CloseRequested += saved => closed.TrySetResult(saved);

        EditorView.DataContext = editor;
        EditorView.Visibility = Visibility.Visible;
        ListView.Visibility = Visibility.Collapsed;
        EditorView.Focus();

        bool saved = await closed.Task;

        EditorView.DataContext = null;
        EditorView.Visibility = Visibility.Collapsed;
        ListView.Visibility = Visibility.Visible;
        editor.CancelLoaders();
        return saved;
    }
}
