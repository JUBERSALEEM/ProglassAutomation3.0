using ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.PrintableInvoice;
using System.Windows;
using System.Windows.Controls;

namespace ProGlassAutomation.Views.ProformaInvoice.Step7PrintPreview.PrintableInvoice
{
    public partial class PrintableInvoiceView : UserControl
    {
        public PrintableInvoiceView()
        {
            InitializeComponent();

            // Wire the PrintablePaper element to the VM so Print command can use it
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is PrintableInvoiceViewModel vm)
            {
                vm.PrintTarget = PrintablePaper;
            }
        }
    }
}