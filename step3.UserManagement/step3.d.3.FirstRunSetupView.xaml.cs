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
        }

        /// <summary>
        /// Puts the caret in the one field the operator must fill, so the gate is
        /// usable from the keyboard without hunting for the first control.
        /// </summary>
        internal void FocusFirstField()
        {
            FullNameBox.Focus();
            FullNameBox.CaretIndex = FullNameBox.Text?.Length ?? 0;
        }
    }
}
