namespace ProGlassAutomation.Models.JobOrder.Common.JobOrderStatus
{
    /// <summary>
    /// String constants and comparisons for Job Order status.
    ///
    /// <para>
    /// Follows the DeliveryNote module's pattern (<c>DeliveryNoteStatusValues</c>).
    /// Job Order had an enum but no canonical values class, so every consumer
    /// invented its own literals — which is how the bug this class exists to
    /// prevent got shipped.
    /// </para>
    ///
    /// <para><b>The split.</b> <see cref="JobOrderStatus"/> is an enum whose
    /// member is <c>InProduction</c>, but <c>SavedJobOrder.Status</c> is a
    /// <c>string</c>. A job written through the enum stores <c>"InProduction"</c>
    /// with no space; older rows store <c>"In Production"</c>. Both forms are
    /// live in the same table.</para>
    ///
    /// <para>Consumers that compared against only one spelling silently
    /// under-counted. <c>DashboardMetricsService</c> matched
    /// <c>"In Production"</c> and therefore missed every enum-written job, so
    /// the Overview dashboard disagreed with the Job Order dashboard about how
    /// many jobs were in production. <c>ReportingDataService</c> matched both
    /// and documented the split locally. This class is the single place that
    /// knows about it.</para>
    ///
    /// <para><b>Rule:</b> never compare a job status with <c>==</c> or a bare
    /// <c>string.Equals</c>. Use <see cref="Is"/> or one of the <c>Is*</c>
    /// helpers. A new status comparison written any other way will drift.</para>
    /// </summary>
    public static class JobOrderStatusValues
    {
        public const string Draft = "Draft";
        public const string Released = "Released";
        public const string Completed = "Completed";
        public const string Delivered = "Delivered";

        /// <summary>
        /// The form <see cref="JobOrderStatus.InProduction"/> serialises to.
        /// This is what new records hold.
        /// </summary>
        public const string InProduction = "InProduction";

        /// <summary>
        /// The legacy spaced form. Still present on older rows.
        /// </summary>
        public const string InProductionLegacy = "In Production";

        /// <summary>
        /// The label shown to users, and the value every breakdown groups by.
        /// </summary>
        public const string InProductionDisplay = "In Production";

        /// <summary>
        /// Case- and whitespace-insensitive status comparison.
        /// </summary>
        public static bool Is(string? stored, string expected) =>
            string.Equals(stored?.Trim(), expected, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when the job is in production, accepting BOTH stored
        /// spellings. Use this instead of <see cref="Is"/> for this status.
        /// </summary>
        public static bool IsInProduction(string? stored) =>
            Is(stored, InProduction) || Is(stored, InProductionLegacy);

        public static bool IsCompleted(string? stored) => Is(stored, Completed);

        public static bool IsDelivered(string? stored) => Is(stored, Delivered);

        /// <summary>
        /// Production is finished: the job is completed or already on site.
        /// A delivered job is never "overdue" even if its date has passed.
        /// </summary>
        public static bool IsFinished(string? stored) =>
            IsCompleted(stored) || IsDelivered(stored);

        /// <summary>
        /// Canonical label for display and grouping. Collapses the spelling
        /// split so a status breakdown cannot show the same state twice under
        /// two names, and so a chart cannot split one number into two bars.
        /// </summary>
        public static string Canonical(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return "Unknown";
            return IsInProduction(stored) ? InProductionDisplay : stored.Trim();
        }
    }
}
