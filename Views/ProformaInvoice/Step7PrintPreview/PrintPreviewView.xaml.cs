using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProGlassAutomation.Views.ProformaInvoice.Step7PrintPreview
{
    public partial class PrintPreviewView : UserControl
    {
        public PrintPreviewView()
        {
            InitializeComponent();
        }

        // Same contract as Sheet Store: left click toggles, outside click closes.
        private static bool IsDescendantOf(DependencyObject element, DependencyObject root)
            => ProGlassAutomation.Utilities.UiTreeHelper.IsDescendantOf(element, root);
    }
}
