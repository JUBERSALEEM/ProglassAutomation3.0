using ProGlassAutomation.ViewModels.ProformaInvoice.SavedInvoicesList;
using System.Windows;
using ProGlassAutomation.Views.Shared.Dialogs;

namespace ProGlassAutomation.Views.ProformaInvoice.SavedInvoicesList
{
    /// <summary>
    /// Modal gate shown when a Proforma Invoice is approved. The user must
    /// confirm HOW the customer confirmed the quote:
    ///   • "Customer signed the Proforma Invoice" → approval proceeds without
    ///     needing an LPO number.
    ///   • "Customer provided an LPO / Purchase Order" → an LPO number is
    ///     required; it is saved back to the invoice (Step 3) so invoices and
    ///     the statement generator always have it.
    ///
    /// Returns true from ShowDialog() only when the user actually confirmed
    /// approval (Approved == true). Cancel / close returns false and leaves
    /// the invoice status unchanged.
    ///
    /// 2026-09-02 (Phase 3.7): mode state, validation and the approve flow
    /// moved to InvoiceApprovalViewModel (WorkflowProgressDialog pattern —
    /// RequestClose wired to DialogResult; radios bound via
    /// IndexEqualsConverter; LPO-box focus-on-check via
    /// FocusOnCheckedBehavior; Enter handled by IsDefault). This file only
    /// wires the VM.
    /// </summary>
    public partial class InvoiceApprovalDialog : ThemedDialogWindow
    {
        private readonly InvoiceApprovalViewModel _vm;

        /// <summary>True when the user confirmed the approval.</summary>
        public bool Approved => _vm.Approved;

        /// <summary>True when "Customer signed the Proforma Invoice" was chosen.</summary>
        public bool IsSignedPi => _vm.IsSignedPi;

        /// <summary>The LPO / PO number the user entered (empty if Signed PI).</summary>
        public string LpoNumber => _vm.LpoNumber;

        public InvoiceApprovalDialog(int attachmentCount = 0)
        {
            InitializeComponent();

            _vm = new InvoiceApprovalViewModel(attachmentCount);
            _vm.RequestClose = result =>
            {
                DialogResult = result;
                Close();
            };
            DataContext = _vm;
        }
    }
}
