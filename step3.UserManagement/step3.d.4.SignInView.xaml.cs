using System.Windows;
using System.Windows.Controls;
using ProGlassAutomation.ViewModels.Users;

namespace ProGlassAutomation.Users
{
    /// <summary>
    /// step3.d.4 — the sign-in gate's view.
    /// </summary>
    /// <remarks>
    /// Presentation only. The three handlers exist because a WPF PasswordBox does
    /// not expose a bindable Password property, and adding an attached "bind my
    /// password" helper would widen the theme/XAML surface for no gain. Each
    /// handler copies the masked value straight into the view model, which is the
    /// only place that reads it.
    /// </remarks>
    public partial class SignInView : UserControl
    {
        public SignInView()
        {
            InitializeComponent();

            // A gate is shown again after "Sign out", so it must never come back
            // with the previous person's username or a half-typed password.
            IsVisibleChanged += OnVisibilityChanged;
        }

        private SignInViewModel? ViewModel => DataContext as SignInViewModel;

        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsVisible) return;

            PasswordEntryBox.Clear();
            NewPasswordEntryBox.Clear();
            NewPasswordConfirmEntryBox.Clear();
            if (ViewModel != null)
            {
                ViewModel.Password = "";
                ViewModel.NewPassword = "";
                ViewModel.NewPasswordConfirm = "";
            }

            UsernameBox.Focus();
            UsernameBox.CaretIndex = UsernameBox.Text?.Length ?? 0;
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.Password = PasswordEntryBox.Password;
        }

        private void OnNewPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.NewPassword = NewPasswordEntryBox.Password;
        }

        private void OnNewPasswordConfirmChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.NewPasswordConfirm = NewPasswordConfirmEntryBox.Password;
        }

        /// <summary>Puts the caret in the first field once the gate is shown.</summary>
        internal void FocusFirstField()
        {
            UsernameBox.Focus();
            UsernameBox.CaretIndex = UsernameBox.Text?.Length ?? 0;
        }
    }
}
