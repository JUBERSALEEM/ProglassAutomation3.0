namespace ProGlassAutomation.ViewModels.ProformaInvoice.SavedInvoicesList
{
    internal enum JobConversionKind
    {
        Created,
        ReleasedExistingDraft,
        AlreadyPresent,
        Failed,
        Unavailable
    }

    internal readonly record struct JobConversionResult(
        JobConversionKind Kind,
        string? JobOrderId,
        string? Detail);

    internal enum NextActionKind
    {
        None,
        MarkSent,
        Confirm,
        EnsureJob,
        Revision,
        ReopenDraft,
        Open,
        OpenJob
    }

    /// <summary>
    /// One stage chip on the saved-invoice register. The key is a real
    /// status-filter value, not a new query.
    /// </summary>
    public sealed class ProformaStageChip
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public string Tip { get; init; } = "";
        public bool IsSelected { get; init; }
    }
}
