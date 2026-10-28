using System.Windows;
using System.Windows.Media;
using ProGlassAutomation.Theming;

namespace ProGlassAutomation.Customization;

/// <summary>
/// Shared accent overlay. A plate is one hue with many named values (50–800).
/// Role hex overrides let the user keep an exact value. Not a material engine.
/// </summary>
public sealed record ColorStop(string Name, string Hex);

public sealed record ColorPlateDefinition(
    string Name,
    IReadOnlyList<ColorStop> Stops,
    IReadOnlyDictionary<string, string> LightRoles,
    IReadOnlyDictionary<string, string> DarkRoles);

public sealed record ColorPlateCard(
    string Name,
    IReadOnlyList<ColorStop> Stops,
    IReadOnlyList<Brush> Swatches,
    Brush Sample,
    string Summary);

public sealed record ColorPlateState(
    string Plate = "Ocean",
    Dictionary<string, string>? RoleHex = null,
    string CardTint = "#C4B0D4",
    int CardSaturation = 0,
    string CardTintEnd = "#EDE4F5",
    string CardGradient = "Solid")
{
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Plate) || Plate.Length > 40)
            return "Color plate name is required.";
        if (!ColorPlateService.Exists(Plate))
            return "Unknown color plate.";
        foreach (var pair in RoleHex ?? [])
        {
            if (!ColorPlateService.Roles.Contains(pair.Key))
                return "Unknown color role.";
            if (ColorPlateService.ParseHex(pair.Value) is null)
                return "Color values must be #RRGGBB.";
        }
        if (ColorPlateService.ParseHex(CardTint) is null)
            return "Card tint must be #RRGGBB.";
        if (ColorPlateService.ParseHex(CardTintEnd) is null)
            return "Card gradient end must be #RRGGBB.";
        if (CardSaturation is < 0 or > 100)
            return "Card saturation must be 0–100.";
        if (!ColorPlateService.CardGradients.Contains(CardGradient))
            return "Unknown card gradient.";
        return null;
    }
}

public static class ColorPlateService
{
    public static readonly IReadOnlyList<string> Roles =
    [
        "AccentPrimary", "AccentHover", "AccentPressed", "AccentSoft", "AccentSecondary",
        "FocusIndicator", "ChartSeries1", "ChartSeries2", "ChartSeries3", "ChartSeries4"
    ];

    public static readonly IReadOnlyList<string> CardRoles =
    [
        "SurfacePrimary", "SurfaceSecondary", "SurfaceElevated"
    ];

    public static readonly IReadOnlyList<string> CardGradients =
    [
        "Solid", "Left to right", "Right to left", "Top to bottom", "Bottom to top", "Diagonal down", "Diagonal up"
    ];

    public static IReadOnlyList<ColorPlateDefinition> BuiltIn { get; } =
    [
        Plate("Ocean",
            ("50", "#E8F0FE"), ("100", "#C5D7F7"), ("200", "#8EB0EE"),
            ("400", "#4D82D6"), ("600", "#2F5AA8"), ("800", "#1E3C72")),
        Plate("Forest",
            ("50", "#E6F4EC"), ("100", "#C4E4D2"), ("200", "#86C8A4"),
            ("400", "#3D9A6A"), ("600", "#1F6B4A"), ("800", "#144632")),
        Plate("Sunset",
            ("50", "#FDECE8"), ("100", "#F8C9BE"), ("200", "#F08A72"),
            ("400", "#E05A3A"), ("600", "#C43C1E"), ("800", "#8A2410")),
        Plate("Slate",
            ("50", "#EEF1F5"), ("100", "#D5DCE6"), ("200", "#A9B4C4"),
            ("400", "#6B7789"), ("600", "#445060"), ("800", "#1B2430")),
        Plate("Violet",
            ("50", "#F3EEFA"), ("100", "#DDD0F2"), ("200", "#B89EE3"),
            ("400", "#8B63C7"), ("600", "#6B3FA0"), ("800", "#452868")),
        Plate("Sand",
            ("50", "#F8F1E8"), ("100", "#EAD9C3"), ("200", "#D4B48A"),
            ("400", "#C4954A"), ("600", "#A67C3A"), ("800", "#6E4F22")),
        new ColorPlateDefinition(
            "Theme",
            [new ColorStop("theme", "#888888")],
            new Dictionary<string, string>(),
            new Dictionary<string, string>()),
    ];

    public static bool IsThemePlate(string? name) =>
        string.Equals(name, "Theme", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<ColorPlateCard> Cards { get; } = BuiltIn.Select(CardOf).ToArray();

    public static bool Exists(string name) =>
        BuiltIn.Any(plate => string.Equals(plate.Name, name, StringComparison.OrdinalIgnoreCase));

    public static ColorPlateDefinition Find(string name) =>
        BuiltIn.FirstOrDefault(plate => string.Equals(plate.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? BuiltIn[0];

    public static Color? ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        string value = hex.Trim();
        if (value[0] != '#') value = "#" + value;
        try
        {
            object converted = ColorConverter.ConvertFromString(value)
                ?? throw new FormatException();
            return (Color)converted;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return null;
        }
    }

    public static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static void Apply(ResourceDictionary package, Appearance mode, ColorPlateState state, string? theme = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(state);
        if (state.Validate() is not null) throw new ArgumentException(state.Validate());
        if (IsThemePlate(state.Plate))
        {
            if (!string.IsNullOrWhiteSpace(theme))
                RestoreRoles(package, theme, mode);
            ApplyCardTint(package, mode, state, theme);
            return;
        }
        var plate = Find(state.Plate);
        var roles = mode == Appearance.Dark ? plate.DarkRoles : plate.LightRoles;
        var stops = plate.Stops.ToDictionary(stop => stop.Name, stop => stop.Hex, StringComparer.OrdinalIgnoreCase);
        Color? appliedAccent = null;
        foreach (string role in Roles)
        {
            string hex;
            if (state.RoleHex is not null && state.RoleHex.TryGetValue(role, out string? over) && ParseHex(over) is not null)
                hex = over.StartsWith('#') ? over : "#" + over;
            else if (roles.TryGetValue(role, out string? stop) && stops.TryGetValue(stop, out string? mapped))
                hex = mapped;
            else
                continue;
            if (ParseHex(hex) is not Color color) continue;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Set(package, role, brush);
            if (string.Equals(role, "AccentPrimary", StringComparison.Ordinal))
                appliedAccent = color;
        }
        if (appliedAccent is Color accent)
            ApplyTextOnAccent(package, accent);
        ApplyCardTint(package, mode, state, theme);
    }

    public static (Point Start, Point End)? GradientPoints(string? name) => name switch
    {
        "Left to right" => (new Point(0, 0.5), new Point(1, 0.5)),
        "Right to left" => (new Point(1, 0.5), new Point(0, 0.5)),
        "Top to bottom" => (new Point(0.5, 0), new Point(0.5, 1)),
        "Bottom to top" => (new Point(0.5, 1), new Point(0.5, 0)),
        "Diagonal down" => (new Point(0, 0), new Point(1, 1)),
        "Diagonal up" => (new Point(0, 1), new Point(1, 0)),
        _ => null
    };

    public static IReadOnlyList<ColorStop> StopsOf(string plate) => Find(plate).Stops;

    public static string ResolvedHex(ColorPlateState state, Appearance mode, string role)
    {
        if (state.RoleHex is not null && state.RoleHex.TryGetValue(role, out string? over) && ParseHex(over) is Color)
            return over.StartsWith('#') ? over : "#" + over;
        var plate = Find(state.Plate);
        var roles = mode == Appearance.Dark ? plate.DarkRoles : plate.LightRoles;
        var stops = plate.Stops.ToDictionary(stop => stop.Name, stop => stop.Hex, StringComparer.OrdinalIgnoreCase);
        if (roles.TryGetValue(role, out string? stop) && stops.TryGetValue(stop, out string? hex))
            return hex;
        return "#000000";
    }

    private static ColorPlateDefinition Plate(string name, params (string Stop, string Hex)[] stops)
    {
        var list = stops.Select(item => new ColorStop(item.Stop, item.Hex)).ToArray();
        return new ColorPlateDefinition(
            name,
            list,
            new Dictionary<string, string>
            {
                ["AccentPrimary"] = "600",
                ["AccentHover"] = "800",
                ["AccentPressed"] = "800",
                ["AccentSoft"] = "100",
                ["AccentSecondary"] = "400",
                ["FocusIndicator"] = "600",
                ["ChartSeries1"] = "600",
                ["ChartSeries2"] = "400",
                ["ChartSeries3"] = "800",
                ["ChartSeries4"] = "200",
            },
            new Dictionary<string, string>
            {
                ["AccentPrimary"] = "400",
                ["AccentHover"] = "200",
                ["AccentPressed"] = "200",
                ["AccentSoft"] = "800",
                ["AccentSecondary"] = "200",
                ["FocusIndicator"] = "400",
                ["ChartSeries1"] = "400",
                ["ChartSeries2"] = "200",
                ["ChartSeries3"] = "50",
                ["ChartSeries4"] = "600",
            });
    }

    private static void RestoreRoles(ResourceDictionary package, string theme, Appearance mode)
    {
        var fresh = ThemeService.ReadOnlyBase(theme, mode);
        foreach (string role in Roles.Append("TextOnAccent"))
        {
            if (fresh[role] is not SolidColorBrush source) continue;
            var brush = new SolidColorBrush(source.Color);
            brush.Freeze();
            Set(package, role, brush);
        }
    }

    private static void ApplyTextOnAccent(ResourceDictionary package, Color accent)
    {
        // Choose whichever neutral foreground has the stronger contrast with
        // the active accent so selection text remains readable for any plate.
        double Contrast(Color foreground, Color background)
        {
            static double Linear(byte channel)
            {
                double value = channel / 255d;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            double luminance = 0.2126 * Linear(background.R)
                + 0.7152 * Linear(background.G) + 0.0722 * Linear(background.B);
            double foregroundLuminance = 0.2126 * Linear(foreground.R)
                + 0.7152 * Linear(foreground.G) + 0.0722 * Linear(foreground.B);
            return (Math.Max(luminance, foregroundLuminance) + 0.05)
                / (Math.Min(luminance, foregroundLuminance) + 0.05);
        }

        Color foreground = Contrast(Colors.White, accent) >= Contrast(Colors.Black, accent)
            ? Colors.White
            : Colors.Black;
        var brush = new SolidColorBrush(foreground);
        brush.Freeze();
        Set(package, "TextOnAccent", brush);
    }

    private static ColorPlateCard CardOf(ColorPlateDefinition plate)
    {
        if (IsThemePlate(plate.Name))
        {
            var identity = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            identity.Freeze();
            return new ColorPlateCard(plate.Name, plate.Stops, [identity], identity, "Active theme accent. No overlay.");
        }
        var swatches = new List<Brush>(plate.Stops.Count);
        foreach (var stop in plate.Stops)
        {
            var brush = new SolidColorBrush(ParseHex(stop.Hex) ?? Color.FromRgb(0x44, 0x50, 0x60));
            brush.Freeze();
            swatches.Add(brush);
        }
        string sampleHex = plate.Stops.FirstOrDefault(stop => stop.Name == "600")?.Hex ?? plate.Stops[0].Hex;
        var sample = new SolidColorBrush(ParseHex(sampleHex) ?? Color.FromRgb(0x2F, 0x5A, 0xA8));
        sample.Freeze();
        return new ColorPlateCard(
            plate.Name,
            plate.Stops,
            swatches,
            sample,
            string.Join("  ·  ", plate.Stops.Select(stop => stop.Name + " " + stop.Hex)));
    }

    private static void ApplyCardTint(ResourceDictionary package, Appearance mode, ColorPlateState state, string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme)) return;
        var fresh = ThemeService.ReadOnlyBase(theme, mode);
        Color? tint = ParseHex(state.CardTint);
        Color? tintEnd = ParseHex(state.CardTintEnd);
        double amount = Math.Clamp(state.CardSaturation, 0, 100) / 100d;
        var points = amount > 0 ? GradientPoints(state.CardGradient) : null;
        foreach (string role in CardRoles)
        {
            if (fresh[role] is not SolidColorBrush source) continue;
            Color start = amount <= 0 || tint is null ? source.Color : MixRgb(source.Color, tint.Value, amount);
            if (points is null)
            {
                var solid = new SolidColorBrush(start);
                solid.Freeze();
                Set(package, role, solid);
                continue;
            }
            Color end = amount <= 0 || tintEnd is null ? source.Color : MixRgb(source.Color, tintEnd.Value, amount);
            var brush = new LinearGradientBrush
            {
                StartPoint = points.Value.Start,
                EndPoint = points.Value.End,
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };
            brush.GradientStops.Add(new GradientStop(start, 0));
            brush.GradientStops.Add(new GradientStop(end, 1));
            brush.Freeze();
            Set(package, role, brush);
        }
    }

    private static Color MixRgb(Color original, Color tint, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        if (amount >= 1) return tint;
        if (amount <= 0) return original;
        byte Channel(byte from, byte to) => (byte)Math.Round(from + (to - from) * amount);
        return Color.FromRgb(Channel(original.R, tint.R), Channel(original.G, tint.G), Channel(original.B, tint.B));
    }

    private static void Set(ResourceDictionary package, string key, object value)
    {
        if (Replace(package, key, value)) return;
        package[key] = value;
    }

    private static bool Replace(ResourceDictionary dictionary, string key, object value)
    {
        foreach (object existing in dictionary.Keys)
        {
            if (!Equals(existing, key)) continue;
            dictionary[key] = value;
            return true;
        }
        foreach (ResourceDictionary child in dictionary.MergedDictionaries)
            if (Replace(child, key, value)) return true;
        return false;
    }
}
