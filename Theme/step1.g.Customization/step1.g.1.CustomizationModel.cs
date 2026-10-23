using System.Text.Json.Serialization;
using ProGlassAutomation.Theming;
using ProGlassAutomation.ThemeBento;
using ProGlassAutomation.ThemeClaymorphism;
using ProGlassAutomation.ThemeLiquidGlass;
using ProGlassAutomation.ThemeNeumorphism;

namespace ProGlassAutomation.Customization;

public enum BentoCanvas { Neutral, Warm, Cool }
public enum BentoSurfaceStyle { Clean, Elevated, Contrast }
public enum BentoTextContrast { Standard, High }
public enum BentoBorderIntensity { Subtle, Strong, Hidden }
public enum BentoShadowIntensity { None, Subtle, Elevated }
public enum BentoSpacing { Compact, Comfortable, Spacious }
public enum BentoCardPadding { Compact, Comfortable, Spacious }
public enum BentoFontWeight { Regular, Medium, Semibold }
public enum BentoLayoutPreset { Balanced, Executive, AnalyticsFocus, ProductionOps, CompactDense }
public enum BentoPlateStyle { Default, Positive, Feature, Information, Attention }
public enum LegendPlacement { Auto, Top, Right, Bottom, Hidden }

/// <summary>Extended immutable preferences — every change produces visible resource/layout/chart updates.</summary>
public sealed record BentoCustomizationState(
    BentoAccent Accent = BentoAccent.Ocean,
    BentoDensity Density = BentoDensity.Compact,
    BentoShape Shape = BentoShape.Rounded,
    BentoType Typography = BentoType.Standard,
    BentoCanvas Canvas = BentoCanvas.Neutral,
    BentoSurfaceStyle SurfaceStyle = BentoSurfaceStyle.Clean,
    BentoTextContrast TextContrast = BentoTextContrast.Standard,
    BentoBorderIntensity BorderIntensity = BentoBorderIntensity.Subtle,
    BentoShadowIntensity Shadow = BentoShadowIntensity.Subtle,
    BentoSpacing Spacing = BentoSpacing.Comfortable,
    BentoCardPadding CardPadding = BentoCardPadding.Comfortable,
    BentoFontWeight FontWeight = BentoFontWeight.Regular,
    BentoLayoutPreset LayoutPreset = BentoLayoutPreset.Balanced,
    bool HeroCompact = false,
    bool ShowKpiTrend = true,
    bool ShowTableStatus = true
)
{
    public string? Validate()
    {
        if (!Enum.IsDefined(Accent) || !Enum.IsDefined(Density) || !Enum.IsDefined(Shape) || !Enum.IsDefined(Typography) ||
            !Enum.IsDefined(Canvas) || !Enum.IsDefined(SurfaceStyle) || !Enum.IsDefined(TextContrast) || !Enum.IsDefined(BorderIntensity) ||
            !Enum.IsDefined(Shadow) || !Enum.IsDefined(Spacing) || !Enum.IsDefined(CardPadding) || !Enum.IsDefined(FontWeight) || !Enum.IsDefined(LayoutPreset))
            return "Unsupported customization choice.";
        return null;
    }
    public BentoPreferences ToLegacy() => new(Accent, Density, Shape, Typography);
}

public sealed record ChartDisplayOptions(
    string TitleOverride = "",
    string SubtitleOverride = "",
    double Height = 205,
    LegendPlacement Legend = LegendPlacement.Auto,
    bool ShowGrid = true,
    bool ShowAxis = true,
    bool ShowValueLabels = false,
    bool ShowTooltip = true,
    double DonutHole = 56,
    double BarCorner = 6
);

public sealed record ComponentStyle(
    BentoPlateStyle Plate = BentoPlateStyle.Default,
    bool ShowIcon = true,
    bool ShowTrend = true,
    bool Compact = false
);

public sealed record SavedCustomization(
    int Version = 2,
    Appearance Appearance = Appearance.Light,
    BentoCustomizationState Preferences = null!,
    Dictionary<string, ChartDisplayOptions>? ChartOptions = null,
    Dictionary<string, ComponentStyle>? ComponentStyles = null,
    List<NamedPreset>? CustomPresets = null,
    string Theme = ThemeCatalog.BentoName,
    ClayCustomizationState? Clay = null,
    List<ClayNamedPreset>? ClayPresets = null,
    SharedGeometryState? Geometry = null,
    NeumorphCustomizationState? Neumorph = null,
    List<NeumorphNamedPreset>? NeumorphPresets = null,
    FontCustomizationState? Font = null,
    ColorPlateState? Colors = null,
    LiquidCustomizationState? Liquid = null,
    List<LiquidNamedPreset>? LiquidPresets = null,
    IconCustomizationState? Icons = null,
    PageCustomizationState? Pages = null
)
{
    public SavedCustomization() : this(2, Appearance.Light, new BentoCustomizationState()) { }
}

public sealed record NamedPreset(string Name, BentoCustomizationState State, Appearance Appearance, Dictionary<string, ChartDisplayOptions>? Charts = null);
public static class CustomizationCategories { public static IReadOnlyList<string> All => ["Appearance","Layout","Components","Charts"]; }

