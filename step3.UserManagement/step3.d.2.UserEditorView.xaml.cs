using System.Windows.Controls;

namespace ProGlassAutomation.Users;

/// <summary>
/// User editor pane. Purely presentational: the DataContext is a
/// UserEditorViewModel supplied by the workspace, and no logic lives here.
/// </summary>
public partial class UserEditorView : UserControl
{
    public UserEditorView()
    {
        InitializeComponent();
    }
}
