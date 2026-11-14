namespace ProGlassAutomation.Models.DeliveryNote.Common.DeliveryType
{
    /// <summary>
    /// Classifies each Delivery Note:
    /// Original — normal first/partial delivery
    /// Dummy    — auto-generated from balance qty (Draft until confirmed)
    /// Rework   — re-delivery of reworked items from a rework JO
    /// Return   — tracking returned/rejected items
    ///
    /// Stored as STRING in DB — never cast to/from int.
    /// Use DeliveryTypeValues constants for comparisons.
    /// </summary>
    public enum DeliveryType
    {
        Original,
        Dummy,
        Rework,
        Return
    }

    /// <summary>
    /// String constants for type comparisons.
    /// Use these instead of magic strings throughout the codebase.
    /// Matches enum values exactly.
    /// </summary>
    public static class DeliveryTypeValues
    {
        public const string Original = "Original";
        public const string Dummy = "Dummy";
        public const string Rework = "Rework";
        public const string Return = "Return";
    }
}