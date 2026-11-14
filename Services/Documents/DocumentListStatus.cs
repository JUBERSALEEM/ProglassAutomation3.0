using System;

namespace ProGlassAutomation.Services.Documents
{
    /// <summary>
    /// Industrial list DisplayStatus labels shared by PI / JO / DN lists.
    /// Quantity rules live in <see cref="DocumentProgressSnapshot"/>; this type
    /// only maps progress + stored status onto the operator-facing pill.
    /// </summary>
    public static class DocumentListStatus
    {
        public const string PiCreated = "PI created";
        public const string PiSent = "PI sent";
        public const string PiConfirmed = "PI confirmed";
        public const string FullJobCreated = "Full job created";
        public const string PartiallyDelivered = "Partially delivered";
        public const string FullyDelivered = "Fully delivered";
        public const string PartiallyInvoiced = "Partially invoiced";
        public const string FullyInvoiced = "Fully invoiced";
        public const string Cancelled = "Cancelled";

        public const string NotDelivered = "Not delivered";

        public const string DnCreated = "DN created";

        public static bool IsIssuedInvoice(string? status) =>
            string.Equals(status, "Issued", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Paid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "PartiallyPaid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Overdue", StringComparison.OrdinalIgnoreCase);

        public static bool IsCancelledInvoice(string? status) =>
            string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Void", StringComparison.OrdinalIgnoreCase);

        public static bool IsDraftInvoice(string? status) =>
            string.IsNullOrWhiteSpace(status) ||
            string.Equals(status, "Draft", StringComparison.OrdinalIgnoreCase);

        public static bool IsCancelledProforma(string? status) =>
            string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase);

        public static string ForJobOrder(int ordered, int delivered)
        {
            if (ordered > 0 && delivered >= ordered)
                return FullyDelivered;
            if (delivered > 0)
                return PartiallyDelivered;
            return NotDelivered;
        }

        public static string BrushKeyForJobOrder(string displayStatus) =>
            displayStatus switch
            {
                FullyDelivered => "SuccessBrush",
                PartiallyDelivered => "WarningBrush",
                _ => "NeutralBrush"
            };

        /// <summary>
        /// PI list overlay. Draft tax invoices never count as invoiced.
        /// While generated TIs exist but are not all Issued, the PI stays on
        /// its delivery / job / created state (typically Fully delivered).
        /// </summary>
        public static (string Status, string BrushKey) ForProforma(
            string? storedStatus,
            bool hasConfirmationAttachment,
            DocumentProgress? progress)
        {
            var p = progress ?? DocumentProgress.Empty;

            if (IsCancelledProforma(storedStatus))
                return (Cancelled, "DangerBrush");

            bool allGeneratedIssued = p.IssuedTiCount > 0 && p.DraftTiCount == 0;
            bool qtyCovered = p.Ordered > 0
                ? p.IssuedQty >= p.Ordered
                : p.IssuedQty > 0 || p.IssuedTiCount > 0;

            if (allGeneratedIssued && qtyCovered)
                return (FullyInvoiced, "SuccessBrush");

            if (allGeneratedIssued && p.IssuedQty > 0)
                return (PartiallyInvoiced, "AccentBrush");

            if (p.ConfirmedDelivered > 0)
            {
                if (p.Ordered > 0 && p.ConfirmedDelivered >= p.Ordered)
                    return (FullyDelivered, "SuccessBrush");
                return (PartiallyDelivered, "WarningBrush");
            }

            if (p.HasJobOrder)
                return (FullJobCreated, "InfoBrush");

            if (string.Equals(storedStatus, "Approved", StringComparison.OrdinalIgnoreCase)
                && hasConfirmationAttachment)
                return (PiConfirmed, "AccentBrush");

            if (string.Equals(storedStatus, "Sent", StringComparison.OrdinalIgnoreCase))
                return (PiSent, "AccentDarkBrush");

            return (PiCreated, "NeutralBrush");
        }

        /// <summary>
        /// DN list is grouped by job order. The group pill is invoicing state,
        /// not Completed / In Progress.
        /// </summary>
        public static (string Status, string TintKey, string ForegroundKey) ForDeliveryFamily(
            DocumentProgress? progress)
        {
            var p = progress ?? DocumentProgress.Empty;

            bool qtyComplete = p.Ordered > 0 && p.ConfirmedDelivered >= p.Ordered;
            bool allCovered = p.ConfirmedDnCount > 0
                && p.ConfirmedDnsCoveredByIssued >= p.ConfirmedDnCount;
            bool allGeneratedIssued = p.IssuedTiCount > 0 && p.DraftTiCount == 0;

            if (allGeneratedIssued && allCovered && qtyComplete)
                return (FullyInvoiced, "TintGreenBrush", "SuccessBrush");

            if (p.IssuedTiCount > 0)
                return (PartiallyInvoiced, "TintAmberBrush", "WarningBrush");

            return (DnCreated, "TintIndigoBrush", "AccentBrush");
        }
    }
}
