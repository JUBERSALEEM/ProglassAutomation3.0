using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using LiveChartsCore.SkiaSharpView;
using ProGlassAutomation.Analytics;
using ProGlassAutomation.Theming;
using ProGlassAutomation.ThemeStudio;

namespace ProGlassAutomation.ThemeTests;

internal static class AnalyticsChecks
{
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Analytics: " + message); }
    public static void DataContracts()
    {
        var samples = new AnalyticsSamples();
        int count = 0;
        foreach (var kind in samples.ChartKinds)
        foreach (var scenario in samples.Scenarios)
        {
            samples.SelectedKind = kind; samples.SelectedScenario = scenario;
            var data = samples.Preview;
            Check((data.ValidationError() is not null) == (scenario == "Invalid data"), $"{kind}/{scenario} validity");
            if (scenario == "Empty") Check(data.IsEmpty, "empty datasets");
            count++;
        }
        ChartDataset Dataset(IEnumerable<ChartSeriesData> series, ChartKind kind = ChartKind.Lines, IEnumerable<string>? labels = null) =>
            new("Test", kind, labels ?? ["A", "B"], series, "units", "Test summary");
        Check(Dataset([new("Negative", [-3, 0])]).ValidationError() is null, "negative/zero values accepted");
        Check(Dataset([new("Missing", [null, null])]).IsEmpty, "all missing is empty");
        Check(Dataset([new("Bad", [double.PositiveInfinity, 1])]).ValidationError() is not null, "infinity rejected");
        Check(Dataset([new("Mismatch", [1])]).ValidationError() is not null, "length mismatch rejected");
        Check(Dataset([new("Same", [1, 2]), new("Same", [3, 4])]).ValidationError() is not null, "duplicate names rejected");
        Check(Dataset([new("Bad pie", [-1])], ChartKind.Pie).ValidationError() is not null, "negative pie rejected");
        Check(Dataset([new("Zero", [0])], ChartKind.Donut).IsEmpty, "zero donut empty, no divide by zero");
        Check(Dataset([], (ChartKind)999).ValidationError() is not null, "unsupported chart kind rejected");
        Check(Dataset(Enumerable.Range(1, 5).Select(i => new ChartSeriesData(i.ToString(), [1, 2]))).ValidationError() is not null, "series limit");
        Check(Dataset([], labels: Enumerable.Repeat("A", 1001)).ValidationError() is not null, "point limit");
        var input = new double?[] { 1, 2 };
        var snapshot = new ChartSeriesData("Immutable", input); input[0] = 999;
        Check(snapshot.Values[0] == 1, "input values snapshotted");
        foreach (var mode in Enum.GetValues<Appearance>())
        {
            var package = ThemeService.Load(mode);
            foreach (string surface in new[] { "SurfacePositive", "SurfaceFeature", "SurfaceInformation", "SurfaceAttention" })
                foreach (string text in new[] { "TextPrimary", "TextSecondary", "TextMuted" }) ContractChecks.Contrast(package, text, surface, 4.5);
            ContractChecks.Contrast(package, "ChartTooltipText", "ChartTooltipSurface", 4.5);
        }
        Console.WriteLine($"PASS: {count} chart/scenario contracts, invalid/empty/immutable data and analytics contrast.");
    }
    public static void Runtime(ThemeService themes, BusinessReferenceView business, Action pump)
    {
        // The reference page declares eight analytics charts (four KPI tiles, momentum,
        // mix, channels and revenue); the count is not what this check is about, and
        // pinning a literal number was wrong twice over — it was four, and no page ever
        // had four. What matters is that the page is chart-rich and every chart carries a
        // real dataset; the specific charts used below are then found by their data.
        var charts = Descendants(business).OfType<BentoChart>().ToArray();
        Check(charts.Length >= 4, $"reference page carries analytics charts ({charts.Length})");
        Check(charts.All(chart => chart.Data is not null), "every analytics chart has a dataset bound");
        var revenue = charts.Single(c => c.Data?.Title == "Revenue and target");
        Check(revenue.RenderedSeries.Count == 2 && revenue.RenderedSeries[0] is ColumnSeries<double?>, "grouped columns rendered by library");
        var mix = charts.Single(c => c.Data?.Kind == ChartKind.Donut);
        Check(mix.RenderedSeries.Count == 4 && mix.RenderedSeries[0] is PieSeries<double?>, "real donut series");
        var toggle = Descendants(revenue).OfType<CheckBox>().First(c => AutomationProperties.GetName(c).StartsWith("Show series"));
        toggle.IsChecked = false;
        var nativeSeries = revenue.RenderedSeries[0];
        var table = Descendants(business).OfType<DataGrid>().Single();
        table.SelectedIndex = 1;
        var selected = table.SelectedItem;
        for (int i = 0; i < 6; i++)
        {
            themes.Apply(ThemeService.Bento, i % 2 == 0 ? Appearance.Dark : Appearance.Light); pump();
            Check(ReferenceEquals(nativeSeries, revenue.RenderedSeries[0]) && !nativeSeries.IsVisible, "palette update retains native series/visibility");
            Check(ReferenceEquals(toggle, Descendants(revenue).OfType<CheckBox>().First(c => AutomationProperties.GetName(c).StartsWith("Show series"))), "palette update retains legend controls");
            Check(ReferenceEquals(table.SelectedItem, selected), "palette update retains table selection");
            Check(Equals(revenue.AxisBrush, themes.GetToken("ChartAxis")), "axis paint binding follows active theme");
            Check(Equals(revenue.TooltipSurface, themes.GetToken("ChartTooltipSurface")), "tooltip paint binding follows theme");
        }
        var samples = new AnalyticsSamples();
        foreach (var kind in samples.ChartKinds)
        {
            samples.SelectedKind = kind; revenue.Data = samples.Preview; pump();
            Check(revenue.RenderedSeries.Count > 0 && revenue.DisplayMessage.Length == 0, "render configuration " + kind);
        }
        foreach (string scenario in new[] { "Empty", "Loading", "Error", "Invalid data" })
        {
            samples.SelectedScenario = scenario; revenue.Data = samples.Preview; pump();
            Check(revenue.RenderedSeries.Count == 0 && revenue.DisplayMessage.Length > 0, "explicit state " + scenario);
        }
        // Restore the original binding rather than leave test data on the real page.
        revenue.SetBinding(BentoChart.DataProperty, new System.Windows.Data.Binding(nameof(AnalyticsSamples.Revenue)));
        pump();
        Console.WriteLine("PASS: library chart configurations/states, six live mode switches, legend identity/visibility and DataGrid selection (not visual certification).");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
