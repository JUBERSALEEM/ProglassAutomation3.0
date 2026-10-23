using System.Windows;
using System.Windows.Media;

namespace ProGlassAutomation.Customization;

public enum IconKind
{
    Home, Theme, Texts, Colors, About, Marks,
    Appearance, Layout, Charts, Work, Settings, Author, Engineering, Components,
    Inquiry
}

public enum IconMarkStyle
{
    FluentOutline,
    FluentFilled,
    MdlFilled,
    LucideThin,
    MaterialRound,
    Duotone,
    SoftPlate,
    Contrast,
    Compact,
    LineHeavy
}

public sealed record IconCustomizationState(IconMarkStyle Style = IconMarkStyle.FluentOutline)
{
    public string? Validate() => Enum.IsDefined(Style) ? null : "Unsupported icon style.";
}

public sealed record IconStyleCard(IconMarkStyle Style, string Name, string Caption);

/// <summary>
/// Shared icon overlay. Marks are native Path geometry, not PNG and not a tenth theme.
/// Pages pick a mark by Kind, by heading Title, or by Category.
/// </summary>
public static class IconService
{
    public static IconCustomizationState Active { get; private set; } = new();
    public static event EventHandler? Changed;

    public static readonly IReadOnlyList<IconStyleCard> Cards =
    [
        new(IconMarkStyle.FluentOutline, "Fluent outline", "Windows 11 line marks. Default."),
        new(IconMarkStyle.FluentFilled, "Fluent filled", "Solid glyphs, stronger at a glance."),
        new(IconMarkStyle.MdlFilled, "MDL2 filled", "Squarer Windows 10 blocks."),
        new(IconMarkStyle.LucideThin, "Lucide thin", "Editorial 1px stroke."),
        new(IconMarkStyle.MaterialRound, "Material round", "Filled marks on a soft plate."),
        new(IconMarkStyle.Duotone, "Duotone", "Tinted fill plus a crisp outline."),
        new(IconMarkStyle.SoftPlate, "Soft plate", "Outline sitting in a circular chip."),
        new(IconMarkStyle.Contrast, "Contrast", "Larger solid marks."),
        new(IconMarkStyle.Compact, "Compact", "Smaller outline for dense nav."),
        new(IconMarkStyle.LineHeavy, "Line heavy", "Bold stroke, still outline.")
    ];

    public static void Apply(IconCustomizationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Validate() is not null) throw new ArgumentException(state.Validate());
        Active = state;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static IconKind Resolve(string? name, string? category = null, IconKind fallback = IconKind.Home)
    {
        IconKind fromName = Match(name);
        if (fromName != IconKind.Home || LooksLikeHome(name)) return fromName;
        IconKind fromCategory = Match(category);
        if (fromCategory != IconKind.Home || LooksLikeHome(category)) return fromCategory;
        return fallback;
    }

    private static bool LooksLikeHome(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string key = Normalize(text);
        return key is "home" or "business" or "focus";
    }

    private static IconKind Match(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return IconKind.Home;
        string key = Normalize(text);
        if (Contains(key, "icon", "mark")) return IconKind.Marks;
        if (Contains(key, "text", "type", "font", "typography")) return IconKind.Texts;
        if (Contains(key, "color", "palette", "wash", "accent")) return IconKind.Colors;
        if (Contains(key, "about", "info", "credit")) return IconKind.About;
        if (Contains(key, "author", "person", "people", "juber")) return IconKind.Author;
        if (Contains(key, "engineer", "notes", "wrench")) return IconKind.Engineering;
        if (Contains(key, "chart", "analytic", "revenue", "mix", "channel", "capacity", "conversion", "retention")) return IconKind.Charts;
        if (Contains(key, "layout", "preset", "density", "spacing")) return IconKind.Layout;
        if (Contains(key, "appearance", "canvas", "surface", "light", "dark", "pulse")) return IconKind.Appearance;
        if (Contains(key, "component", "control", "review")) return IconKind.Components;
        if (Contains(key, "setting", "customize", "design", "save", "reset", "import", "export")) return IconKind.Settings;
        if (Contains(key, "work", "queue", "activity", "recent", "job", "token", "surface")) return IconKind.Work;
        if (Contains(key, "theme", "studio", "engine", "bento", "clay", "fluent", "neu", "liquid", "glass", "spatial", "swiss", "holo", "presentation")) return IconKind.Theme;
        if (Contains(key, "home", "business", "focus")) return IconKind.Home;
        return IconKind.Home;
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.ToLowerInvariant().Split(new[] { ' ', '/', '-', '_', ',', '.', '·' }, StringSplitOptions.RemoveEmptyEntries));

    private static bool Contains(string key, params string[] tokens)
    {
        foreach (string token in tokens)
            if (key.Contains(token, StringComparison.Ordinal)) return true;
        return false;
    }

    public static IconRecipe Recipe(IconMarkStyle style) => style switch
    {
        IconMarkStyle.FluentFilled => new(true, 0, false, false, 1),
        IconMarkStyle.MdlFilled => new(true, 0, false, false, 1),
        IconMarkStyle.LucideThin => new(false, 1.15, false, false, 1),
        IconMarkStyle.MaterialRound => new(true, 0, false, true, 0.86),
        IconMarkStyle.Duotone => new(true, 1.45, true, false, 1),
        IconMarkStyle.SoftPlate => new(false, 1.5, false, true, 0.78),
        IconMarkStyle.Contrast => new(true, 0, false, false, 1.12),
        IconMarkStyle.Compact => new(false, 1.4, false, false, 0.78),
        IconMarkStyle.LineHeavy => new(false, 2.45, false, false, 0.96),
        _ => new(false, 1.6, false, false, 1)
    };

    public static Geometry Mark(IconKind kind, IconMarkStyle style)
    {
        bool filled = Recipe(style).Filled;
        bool mdl = style == IconMarkStyle.MdlFilled;
        string data = (kind, filled, mdl) switch
        {
            (IconKind.Home, false, _) => "M 3,11 L 12,3.2 L 21,11 V 20.6 H 14.7 V 13.5 H 9.3 V 20.6 H 3 Z",
            (IconKind.Home, true, true) => "M 2.5,11 L 12,2.8 L 21.5,11 V 21 H 13.8 V 13.2 H 10.2 V 21 H 2.5 Z",
            (IconKind.Home, true, _) => "M 3,11 L 12,3.2 L 21,11 V 20.6 H 14.7 V 13.5 H 9.3 V 20.6 H 3 Z",
            (IconKind.Theme, false, _) => "M 4,7 H 20 M 8,7 A 2,2 0 1 1 7.99,7 M 4,12 H 20 M 15,12 A 2,2 0 1 1 14.99,12 M 4,17 H 20 M 11,17 A 2,2 0 1 1 10.99,17",
            (IconKind.Theme, true, _) => "M 3.5,5.7 H 20.5 V 8.3 H 3.5 Z M 3.5,10.7 H 20.5 V 13.3 H 3.5 Z M 3.5,15.7 H 20.5 V 18.3 H 3.5 Z",
            (IconKind.Texts, false, _) => "M 6,19 L 12,5 L 18,19 M 8.5,13.5 H 15.5",
            (IconKind.Texts, true, _) => "M 12,4.6 L 18.4,19.2 H 15.5 L 13.7,14.4 H 10.3 L 8.5,19.2 H 5.6 Z M 10.7,12.6 H 13.3 L 12,9 Z",
            (IconKind.Colors, _, _) => "M 12,3.2 C 12,3.2 5.2,11 5.2,15.2 A 6.8,6.8 0 0 0 18.8,15.2 C 18.8,11 12,3.2 12,3.2 Z",
            (IconKind.About, _, _) => "M 12,3.2 A 8.8,8.8 0 1 1 11.99,3.2 Z M 12,10.2 V 16.6 M 12,7 V 8.2",
            (IconKind.Marks, false, _) => "M 4,4 H 10 V 10 H 4 Z M 14,4 H 20 V 10 H 14 Z M 4,14 H 10 V 20 H 4 Z M 14,14 H 20 V 20 H 14 Z",
            (IconKind.Marks, true, _) => "M 4,4 H 10.4 V 10.4 H 4 Z M 13.6,4 H 20 V 10.4 H 13.6 Z M 4,13.6 H 10.4 V 20 H 4 Z M 13.6,13.6 H 20 V 20 H 13.6 Z",
            (IconKind.Appearance, _, _) => "M 12,4 V 6.2 M 12,17.8 V 20 M 4,12 H 6.2 M 17.8,12 H 20 M 6.4,6.4 L 7.9,7.9 M 16.1,16.1 L 17.6,17.6 M 6.4,17.6 L 7.9,16.1 M 16.1,7.9 L 17.6,6.4 M 12,8.2 A 3.8,3.8 0 1 1 11.99,8.2 Z",
            (IconKind.Layout, _, _) => "M 3.5,4 H 20.5 V 20 H 3.5 Z M 3.5,10.2 H 20.5 M 10.2,4 V 20 M 16.8,4 V 20",
            (IconKind.Charts, false, _) => "M 5,18 V 11 H 8.2 V 18 Z M 10.4,18 V 6 H 13.6 V 18 Z M 15.8,18 V 13 H 19 V 18 Z M 3.5,20 H 20.5",
            (IconKind.Charts, true, _) => "M 5,18 V 11 H 8.2 V 18 Z M 10.4,18 V 6 H 13.6 V 18 Z M 15.8,18 V 13 H 19 V 18 Z",
            (IconKind.Work, false, _) => "M 8,4 H 10 C 10,2.8 14,2.8 14,4 H 16 V 6 H 8 Z M 7,6 H 17 V 20 H 7 Z",
            (IconKind.Work, true, _) => "M 8,4 H 16 V 6 H 8 Z M 7,6 H 17 V 20 H 7 Z",
            (IconKind.Settings, _, _) => "M 12,8.2 A 3.8,3.8 0 1 1 11.99,8.2 Z M 11,3 H 13 L 13.7,6.2 L 16.6,5.2 L 18.4,8.2 L 15.8,10 L 18.4,11.8 L 16.6,14.8 L 13.7,13.8 L 13,21 H 11 L 10.3,13.8 L 7.4,14.8 L 5.6,11.8 L 8.2,10 L 5.6,8.2 L 7.4,5.2 L 10.3,6.2 Z",
            (IconKind.Author, _, _) => "M 12,4.2 A 3.6,3.6 0 1 1 11.99,4.2 Z M 6.2,20 C 6.2,15.4 8.8,13.2 12,13.2 C 15.2,13.2 17.8,15.4 17.8,20 Z",
            (IconKind.Engineering, false, _) => "M 8,7.5 L 4,12 L 8,16.5 M 16,7.5 L 20,12 L 16,16.5 M 13.4,5.5 L 10.6,18.5",
            (IconKind.Engineering, true, _) => "M 8,7.5 L 4,12 L 8,16.5 L 5.6,12 Z M 16,7.5 L 20,12 L 16,16.5 L 18.4,12 Z",
            (IconKind.Components, false, _) => "M 12,3.2 L 20,7.2 L 12,11.2 L 4,7.2 Z M 4,12 L 12,16 L 20,12 M 4,16.2 L 12,20.2 L 20,16.2",
            (IconKind.Components, true, _) => "M 12,3.2 L 20,7.2 L 12,11.2 L 4,7.2 Z",
            (IconKind.Inquiry, false, _) => "M 8,4.4 H 10 C 10,3.2 14,3.2 14,4.4 H 16 A 1.4,1.4 0 0 1 17.4,5.8 V 19 A 1.4,1.4 0 0 1 16,20.4 H 8 A 1.4,1.4 0 0 1 6.6,19 V 5.8 A 1.4,1.4 0 0 1 8,4.4 Z M 9.4,12.6 L 11.2,14.4 L 15,10.4",
            (IconKind.Inquiry, true, _) => "M 7.6,4.4 H 10 C 10,3 14,3 14,4.4 H 16.4 A 1.6,1.6 0 0 1 18,6 V 18.8 A 1.6,1.6 0 0 1 16.4,20.4 H 7.6 A 1.6,1.6 0 0 1 6,18.8 V 6 A 1.6,1.6 0 0 1 7.6,4.4 Z M 9.2,12.7 L 11.1,14.6 L 14.8,10.6 L 13.5,9.4 L 11.1,12.3 L 10.5,11.5 Z",
            _ => "M 4,4 H 10.4 V 10.4 H 4 Z M 13.6,4 H 20 V 10.4 H 13.6 Z M 4,13.6 H 10.4 V 20 H 4 Z M 13.6,13.6 H 20 V 20 H 13.6 Z"
        };
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }
}

public readonly record struct IconRecipe(bool Filled, double Stroke, bool Duotone, bool Plate, double Scale);
