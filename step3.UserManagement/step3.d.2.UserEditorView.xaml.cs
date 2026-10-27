using System.Windows;
using System.Windows.Controls;
using ProGlassAutomation.ViewModels.Users;

namespace ProGlassAutomation.Users;

/// <summary>
/// User editor pane. Presentation only: the DataContext is a
/// UserEditorViewModel supplied by the workspace, and no business logic lives
/// here. The three handlers exist because a WPF PasswordBox has no bindable
/// Password property; each copies the masked value straight into the view model.
/// </summary>
public partial class UserEditorView : UserControl
{
    public UserEditorView()
    {
        InitializeComponent();

        // A reopened editor must never show the previous person's typed password.
        DataContextChanged += (_, _) =>
        {
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            NewPasswordConfirmBox.Clear();
        };
    }

    private UserEditorViewModel? ViewModel => DataContext as UserEditorViewModel;

    private void OnCurrentPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.CurrentPassword = CurrentPasswordBox.Password;
    }

    private void OnNewPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.Password = NewPasswordBox.Password;
    }

    private void OnNewPasswordConfirmChanged(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null) ViewModel.PasswordConfirm = NewPasswordConfirmBox.Password;
    }
}
