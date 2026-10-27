using System.Windows.Controls;

namespace ProGlassAutomation.Users
{
    /// <summary>
    /// Startup first-run gate. Presentation only: the DataContext is a
    /// FirstRunViewModel supplied by the shell, and the write goes through
    /// IFirstRunService.
    /// </summary>
    public partial class FirstRunSetupView : UserControl
    {
        public FirstRunSetupView()
        {
            InitializeComponent();

            // The gate can be shown again (sign-out while the directory is still
            // empty is impossible, but the Users page offers the same step), so it
            // must never come back holding a password somebody typed earlier.
            IsVisibleChanged += (_, _) =>
            {
                if (!IsVisible) return;
                PasswordEntryBox.Clear();
                PasswordConfirmBox.Clear();
                if (DataContext is ViewModels.Users.FirstRunViewModel vm)
                {
                    vm.Password = "";
                    vm.PasswordConfirm = "";
                }
            };
        }

        private ViewModels.Users.FirstRunViewModel? ViewModel =>
            DataContext as ViewModels.Users.FirstRunViewModel;

        private void OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.Password = PasswordEntryBox.Password;
        }

        private void OnPasswordConfirmChanged(object sender, System.Windows.RoutedEventArgs e)
        {
            if (ViewModel != null) ViewModel.PasswordConfirm = PasswordConfirmBox.Password;
        }

        /// <summary>
        /// Puts the caret in the one field the operator must fill, so the gate is
        /// usable from the keyboard without hunting for the first control.
        /// </summary>
        internal void FocusFirstField()
        {
            // The card has two states: a new row (name first) and an existing
            // administrator's first password (the name fields are hidden, so
            // focusing one would silently do nothing).
            if (ViewModel is { ShowIdentityFields: true })
            {
                FullNameBox.Focus();
                FullNameBox.CaretIndex = FullNameBox.Text?.Length ?? 0;
                return;
            }

            PasswordEntryBox.Focus();
        }
    }
}
