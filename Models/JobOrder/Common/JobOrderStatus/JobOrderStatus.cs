namespace ProGlassAutomation.Models.JobOrder.Common.JobOrderStatus
{
    /// <summary>
    /// Lifecycle status of a Job Order.
    ///
    /// Status flow:
    ///     Draft         → newly converted from PI, not yet released to factory
    ///     Released      → sent to factory floor, production not started
    ///     InProduction  → cutting/processing in progress
    ///     Completed     → all production stages finished, ready for dispatch
    ///     Delivered     → delivered to site, closed
    ///
    /// Use JobOrderStatusMeta for display label + theme brush key mapping.
    /// </summary>
    public enum JobOrderStatus
    {
        Draft = 0,
        Released = 1,
        InProduction = 2,
        Completed = 3,
        Delivered = 4
    }
}