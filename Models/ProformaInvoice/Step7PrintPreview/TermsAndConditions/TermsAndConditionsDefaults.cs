using System.Collections.Generic;

namespace ProGlassAutomation.Models.ProformaInvoice.Step7PrintPreview.TermsAndConditions
{
    /// <summary>
    /// Factory for the default UAE glass industry Terms &amp; Conditions clauses.
    /// User can edit, remove, or add custom clauses per invoice.
    ///
    /// BUG-32: Uses TermClause.SetBodyFromDefaults() instead of the Body
    ///         property setter so the default text is not flagged as
    ///         "manually edited". This lets ValidityDays picker still
    ///         auto-update the Validity clause body until user types
    ///         into it themselves.
    /// </summary>
    public static class TermsAndConditionsDefaults
    {
        public static List<TermClause> BuildDefaults()
        {
            return new List<TermClause>
            {
                BuildClause(
                    type:  TermClauseType.PaymentTerms,
                    order: 1,
                    title: "Payment Terms",
                    body:  "Payment is required as per the following schedule:",
                    subLines: new[]
                    {
                        "50% advance upon PO confirmation",
                        "50% balance before dispatch from factory",
                        "All payments in AED via bank transfer or cheque"
                    }),

                BuildClause(
                    type:  TermClauseType.Delivery,
                    order: 2,
                    title: "Delivery",
                    body:  "Delivery within 3-4 weeks from PO confirmation and receipt of advance payment. "
                         + "Delivery timeline subject to material availability and factory workload."),

                BuildClause(
                    type:  TermClauseType.Warranty,
                    order: 3,
                    title: "Warranty",
                    body:  "12 months against manufacturing defects only. "
                         + "Glass breakage, mishandling, or damage after installation is not covered under warranty."),

                BuildClause(
                    type:         TermClauseType.Validity,
                    order:        4,
                    title:        "Validity",
                    body:         "This quotation is valid for 7 days from issue date.",
                    validityDays: 7),

                BuildClause(
                    type:  TermClauseType.Taxes,
                    order: 5,
                    title: "Taxes",
                    body:  "Prices are inclusive of 5% UAE VAT unless otherwise specified. "
                         + "TRN registration details as per invoice header."),

                BuildClause(
                    type:  TermClauseType.Installation,
                    order: 6,
                    title: "Installation",
                    body:  "Installation is NOT included in this quotation unless specified separately. "
                         + "Site work, scaffolding, and permits are the client's responsibility.")
            };
        }

        /// <summary>
        /// Builds a TermClause using SetBodyFromDefaults() so the Body is
        /// not flagged as manually edited. This lets the Validity clause
        /// body still auto-update when the ValidityDays picker changes.
        /// </summary>
        private static TermClause BuildClause(
            TermClauseType type,
            int order,
            string title,
            string body,
            string[]? subLines = null,
            int validityDays = 7)
        {
            var clause = new TermClause
            {
                Type = type,
                Order = order,
                Title = title,
                ValidityDays = validityDays
            };

            // BUG-32: use factory setter that does NOT mark as manually edited
            clause.SetBodyFromDefaults(body);

            if (subLines != null)
            {
                foreach (var line in subLines)
                    clause.SubLines.Add(line);
            }

            return clause;
        }
    }
}