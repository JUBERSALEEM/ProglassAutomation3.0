using ProGlassAutomation.ViewModels.ProformaInvoice.SavedInvoicesList;
using System;
using System.Windows;

namespace ProGlassAutomation.Views.ProformaInvoice.SavedInvoicesList
{
    public partial class SavedInvoicesListDialog : Window
    {
        /// <summary>The Id of the invoice user picked (0 if user cancelled).</summary>
        public int SelectedInvoiceId { get; private set; }

        public SavedInvoicesListDialog()
        {
            InitializeComponent();

            var vm = new SavedInvoicesListViewModel
            {
                OnInvoiceLoadRequested = OnInvoicePicked
            };

            EmbeddedListView.DataContext = vm;
        }

        protected override void OnClosed(EventArgs e)
        {
            // This picker owns a separate list VM; detach live document subscriptions.
            if (EmbeddedListView.DataContext is IDisposable disposable)
                disposable.Dispose();
            base.OnClosed(e);
        }

        private void OnInvoicePicked(int invoiceId)
        {
            SelectedInvoiceId = invoiceId;
            DialogResult = true;
            Close();
        }

        }
}