// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Audit/AuditSections.cs
// PROJECT: ProGlassAutomation
// MODULE: Audit / Activity Logging
//
// Canonical section names used across the audit system. Always use these
// constants (never free strings) so the Activity view can group/filter
// reliably and typos cannot split one section into two.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Audit
{
    /// <summary>Section names for audit entries.</summary>
    public static class AuditSections
    {
        public const string App          = "Application";
        public const string Users        = "Users";
        public const string Dashboard    = "Dashboard";
        public const string SheetStore   = "Sheet Store";
        public const string Proforma     = "Proforma Invoice";
        public const string JobOrder     = "Job Order";
        public const string DeliveryNote = "Delivery Note";
        public const string TaxInvoice   = "Tax Invoice";
        public const string CreditNote   = "Credit Note";
        public const string Statement    = "Statement";
        public const string Reports      = "Reports";
        public const string Settings     = "Settings";
        public const string Ai           = "AI Assistant";
        public const string Updates      = "Updates";
        // PHASE 5 (industrial traceability): events raised by the master
        // data repository (company profile enrichments) and the inquiry
        // module (conversion into the PI wizard) had no home — the entries
        // existed neither as vocabulary nor as trail. Additive constants;
        // no existing section string changed.
        public const string MasterData   = "Master Data";
        public const string Inquiry      = "Inquiry";
        // Ledger ground truth: the specification reconcile service writes
        // here when it heals counts or purges orphan usage rows. Additive;
        // no existing section string changed.
        public const string Specifications = "Specifications";
        // Sharing Center: share initiated / external app opened /
        // completed / cancelled / failed. Additive; no existing section
        // string changed.
        public const string Sharing = "Sharing";
    }
}
