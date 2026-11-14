namespace ProGlassAutomation.Services.Export.Core
{
    /// <summary>
    /// Shared column-family colours for industrial glass documents.
    /// PDF, Excel and on-screen exports use the same families so a
    /// factory reader can scan W1/H1 (pane 1), W2/H2 (pane 2 / shape),
    /// quantities, area and weight at a glance.
    /// </summary>
    public static class ExportColumnPalette
    {
        public const string Identity = "#1E293B";
        public const string Pane1 = "#1D4ED8";
        public const string Pane2 = "#C2410C";
        public const string Qty = "#047857";
        public const string Area = "#6D28D9";
        public const string Weight = "#0F766E";
        public const string Money = "#0F4C3A";

        public const string Pane1Fill = "#DBEAFE";
        public const string Pane2Fill = "#FFEDD5";
        public const string QtyFill = "#ECFDF5";
        public const string AreaFill = "#EDE9FE";
        public const string WeightFill = "#CCFBF1";

        public static string NoHash(string hex) =>
            string.IsNullOrEmpty(hex) ? hex
            : hex[0] == '#' ? hex.Substring(1) : hex;
    }
}
