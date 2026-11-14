// FILE: step6.ProformaInvoice/step6.a.17.ProformaTermsDefaults.cs
// Legacy Step 7 default UAE glass-industry terms, adapted to V3's editable
// single-field TermsText contract. The operator can edit the complete text
// per invoice; it is stored in the invoice snapshot and printed verbatim.

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>Creates the default customer-facing terms for a new invoice.</summary>
    public static class ProformaTermsDefaults
    {
        public static string BuildDefaultText(int validityDays = 7)
        {
            int days = Math.Clamp(validityDays, 1, 365);
            var lines = new List<string>
            {
                "1. PAYMENT TERMS",
                "Payment is required as per the following schedule:",
                "   • 50% advance upon PO confirmation",
                "   • 50% balance before dispatch from factory",
                "   • All payments in AED via bank transfer or cheque",
                "",
                "2. DELIVERY",
                "Delivery within 3-4 weeks from PO confirmation and receipt of advance payment. Delivery timeline is subject to material availability and factory workload.",
                "",
                "3. WARRANTY",
                "12 months against manufacturing defects only. Glass breakage, mishandling, or damage after installation is not covered under warranty.",
                "",
                "4. VALIDITY",
                "This quotation is valid for " + days + " days from issue date.",
                "",
                "5. TAXES",
                "Prices are inclusive of 5% UAE VAT unless otherwise specified. TRN registration details are as stated in the invoice header.",
                "",
                "6. INSTALLATION",
                "Installation is NOT included in this quotation unless specified separately. Site work, scaffolding, and permits are the client's responsibility."
            };
            return string.Join(Environment.NewLine, lines);
        }
    }
}
