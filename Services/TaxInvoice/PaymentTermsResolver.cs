// ═══════════════════════════════════════════════════════════════════════
//  Services/TaxInvoice/PaymentTermsResolver.cs
//
//  Single source of truth for mapping payment-terms labels to due-day
//  counts. Previously a private switch on TaxInvoiceDetailViewModel.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.TaxInvoice
{
    /// <summary>
    /// Maps payment-terms display labels ("Immediate", "Net 7", "Net 30", …)
    /// to the number of days used to compute the invoice due date. Unknown or
    /// "Custom" terms fall back to the default 30 days.
    /// </summary>
    public static class PaymentTermsResolver
    {
        public static int ResolveDays(string terms) => terms switch
        {
            "Immediate" => 0,
            "Net 7" => 7,
            "Net 15" => 15,
            "Net 30" => 30,
            "Net 45" => 45,
            "Net 60" => 60,
            "Net 90" => 90,
            _ => 30
        };
    }
}
