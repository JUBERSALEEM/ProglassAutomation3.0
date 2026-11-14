namespace ProGlassAutomation.Models.ProformaInvoice.Step7PrintPreview.InvoiceMainSummary
{
    /// <summary>
    /// How discount is applied to the invoice subtotal.
    /// </summary>
    public enum DiscountType
    {
        /// <summary>Percentage of subtotal (e.g. 5% off).</summary>
        Percentage,

        /// <summary>Flat AED amount off (e.g. AED 500 off).</summary>
        FixedAmount
    }
}