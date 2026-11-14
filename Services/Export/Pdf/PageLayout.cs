namespace ProGlassAutomation.Services.Export.Pdf
{
    /// <summary>
    /// Page setup for a PDF document.
    /// </summary>
    public class PageLayout
    {
        public PageSize Size { get; set; } = PageSize.A4;
        public PageOrientation Orientation { get; set; } = PageOrientation.Portrait;
        public float MarginMm { get; set; } = 15f;

        public static PageLayout A4Portrait => new();
        public static PageLayout A4Landscape => new() { Orientation = PageOrientation.Landscape };
    }

    public enum PageSize
    {
        A4,
        A3,
        Letter
    }

    public enum PageOrientation
    {
        Portrait,
        Landscape
    }
}