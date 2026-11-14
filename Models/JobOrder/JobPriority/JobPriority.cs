namespace ProGlassAutomation.Models.JobOrder.Common.JobPriority
{
    /// <summary>
    /// Production priority of a Job Order.
    /// Drives factory floor scheduling and dashboard sorting.
    ///
    /// Use JobPriorityMeta for display label + brush key mapping.
    /// </summary>
    public enum JobPriority
    {
        Low = 0,
        Medium = 1,
        High = 2
    }
}