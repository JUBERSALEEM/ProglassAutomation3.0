namespace ProGlassAutomation.Models.DeliveryNote.Common.DeliveryNoteStatus
{
    /// <summary>
    /// Status flow for Delivery Notes:
    /// Dispatching → Dispatched → Signed → Received
    /// (Draft kept as a legacy alias of Dispatching.)
    ///
    /// Stored as STRING in DB — never cast to/from int.
    /// Use DeliveryNoteStatusValues constants for comparisons.
    /// </summary>
    public enum DeliveryNoteStatus
    {
        Draft,
        Dispatched,
        Delivered,
        Signed,
        Received
    }

    /// <summary>
    /// String constants for status comparisons.
    /// Use these instead of magic strings throughout the codebase.
    /// Matches enum values exactly.
    /// </summary>
    public static class DeliveryNoteStatusValues
    {
        public const string Draft = "Draft";
        public const string Dispatching = "Dispatching";
        public const string Dispatched = "Dispatched";
        public const string Delivered = "Delivered";
        public const string Signed = "Signed";
        public const string Received = "Received";

        /// <summary>
        /// Case- and whitespace-insensitive status comparison. Prefer the
        /// <c>Is*</c> helpers below over calling this directly for a status
        /// that has more than one stored spelling.
        /// </summary>
        public static bool Is(string? stored, string expected) =>
            string.Equals(stored?.Trim(), expected, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when the note has not left the building yet.
        ///
        /// <para><b>Why this matches two spellings.</b> <c>Draft</c> and
        /// <c>Dispatching</c> are the same state, and both are live in the
        /// database: <see cref="DeliveryNoteStatus"/> has no
        /// <c>Dispatching</c> member, but
        /// <c>Services/DeliveryNote/Mapper/DeliveryNoteMapper.cs</c> writes
        /// <c>Status = "Dispatching"</c> in three places, and
        /// <c>DeliveryNoteStatusMeta</c> maps both keys to the same display
        /// label. <c>Services/DeliveryNote/DeliveryNoteWorkflow.cs</c> already
        /// treats them as equivalent.</para>
        ///
        /// <para><c>DashboardMetricsService</c> previously counted
        /// <c>Preparing</c> as <c>Status == "Draft"</c> only, so every
        /// mapper-written note was in <b>neither</b> Preparing nor Dispatched
        /// — the two dashboard figures did not sum to the total.</para>
        /// </summary>
        public static bool IsPreparing(string? stored) =>
            Is(stored, Draft) || Is(stored, Dispatching);

        public static bool IsDispatched(string? stored) => Is(stored, Dispatched);
        public static bool IsDelivered(string? stored) => Is(stored, Delivered);
        public static bool IsSigned(string? stored) => Is(stored, Signed);
        public static bool IsReceived(string? stored) => Is(stored, Received);

        /// <summary>
        /// True once the note has left the building, in any later state. A
        /// note that has since been Delivered/Signed/Received still counts —
        /// counting only "Dispatched" under-reports.
        /// </summary>
        public static bool HasLeftTheBuilding(string? stored) =>
            IsDispatched(stored) || IsDelivered(stored) ||
            IsSigned(stored) || IsReceived(stored);

        /// <summary>
        /// Canonical label for display and grouping. Collapses the
        /// Draft/Dispatching split so a breakdown cannot show one state
        /// twice, matching <c>DeliveryNoteStatusMeta</c>, which already
        /// renders both keys as "Dispatching".
        /// </summary>
        public static string Canonical(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return "Unknown";
            return IsPreparing(stored) ? Dispatching : stored.Trim();
        }
    }
}