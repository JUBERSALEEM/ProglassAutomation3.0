using System.Windows.Controls;

namespace ProGlassAutomation.Clients;

/// <summary>
/// Client editor pane. Purely presentational: the DataContext is a
/// ClientEditorViewModel supplied by the workspace, and no logic lives here.
/// </summary>
public partial class ClientEditorView : UserControl
{
    public ClientEditorView()
    {
        InitializeComponent();
    }
}
