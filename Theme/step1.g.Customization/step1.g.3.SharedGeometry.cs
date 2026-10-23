using System.Windows;
using ProGlassAutomation.ThemeBento;
using ProGlassAutomation.ThemeClaymorphism;
using ProGlassAutomation.ThemeLiquidGlass;
using ProGlassAutomation.ThemeNeumorphism;
using ProGlassAutomation.Theming;

namespace ProGlassAutomation.Customization;

/// <summary>
/// Studio-owned card geometry written into the 102-token contract.
/// Future modules inherit rounding by binding CardCornerRadius / ControlCornerRadius.
/// This is not a material engine and does not run on themes that own their radius.
/// Font and color-plate overlays always run after the active engine so every theme
/// keeps its own material while sharing type and accent roles.
/// </summary>
public enum CardRounding { Crisp, Soft, Rounded, Pill }

public sealed record SharedGeometryState(CardRounding Rounding = CardRounding.Rounded)
{
    public string? Validate() => Enum.IsDefined(Rounding) ? null : "Unsupported card rounding.";
}

public static class SharedGeometry
{
    public static double CardRadius(CardRounding rounding) => rounding switch
    {
        CardRounding.Crisp => 6d,
        CardRounding.Rounded => 18d,
        CardRounding.Pill => 26d,
        _ => 12d
    };

    public static CardRounding FromShape(BentoShape shape) => shape switch
    {
        BentoShape.Crisp => CardRounding.Crisp,
        BentoShape.Rounded => CardRounding.Rounded,
        BentoShape.Pill => CardRounding.Pill,
        _ => CardRounding.Soft
    };

    public static BentoShape ToShape(CardRounding rounding) => rounding switch
    {
        CardRounding.Crisp => BentoShape.Crisp,
        CardRounding.Rounded => BentoShape.Rounded,
        CardRounding.Pill => BentoShape.Pill,
        _ => BentoShape.Soft
    };

    public static void Apply(ResourceDictionary package, SharedGeometryState state)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(state);
        if (state.Validate() is not null) throw new ArgumentException(state.Validate());
        double card = CardRadius(state.Rounding);
        package["CardCornerRadius"] = new CornerRadius(card);
        package["ControlCornerRadius"] = new CornerRadius(card / 2);
        package["ChipCornerRadius"] = new CornerRadius(Math.Max(4d, Math.Round(card / 3d, MidpointRounding.AwayFromZero)));
        package["ChoiceCornerRadius"] = new CornerRadius(Math.Max(6d, Math.Round(card * 0.75, MidpointRounding.AwayFromZero)));
    }

    /// <summary>Keep copy off the left rim of every Card, regardless of theme chrome.</summary>
    public static void InsetCardCopy(ResourceDictionary package)
    {
        if (package["PaddingCard"] is not Thickness card) return;
        package["PaddingCard"] = new Thickness(card.Left + 8, card.Top, card.Right, card.Bottom);
    }

    public static Action<ResourceDictionary, Appearance> Compose(
        string theme,
        BentoCustomizationState bento,
        ClayCustomizationState clay,
        SharedGeometryState geometry,
        NeumorphCustomizationState? neumorph = null,
        FontCustomizationState? font = null,
        ColorPlateState? colors = null,
        LiquidCustomizationState? liquid = null,
        PageCustomizationState? pages = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(theme);
        ArgumentNullException.ThrowIfNull(bento);
        ArgumentNullException.ThrowIfNull(clay);
        ArgumentNullException.ThrowIfNull(geometry);
        var neu = neumorph ?? new NeumorphCustomizationState();
        var type = font ?? new FontCustomizationState();
        var plate = colors ?? new ColorPlateState();
        var glass = liquid ?? new LiquidCustomizationState();
        return (package, mode) =>
        {
            if (theme == ThemeService.Bento)
                BentoCustomization.ApplyExtended(package, mode, bento);
            else if (theme == ThemeService.Clay)
                ClayCustomization.Apply(package, mode, clay);
            else if (theme == ThemeService.Neumorphism)
                NeumorphCustomization.Apply(package, mode, neu);
            else if (theme == ThemeService.LiquidGlass)
                LiquidCustomization.Apply(package, mode, glass);
            FontService.Apply(package, type);
            ColorPlateService.Apply(package, mode, plate, theme);
            // Null keeps the theme's own rhythm (the default); a choice pins
            // the four compact layouts above the engine.
            if (pages is not null) PageSizeService.Apply(package, pages);
            InsetCardCopy(package);
            if (ThemeCatalog.Find(theme).Geometry != ThemeGeometry.SharedTokens)
                return;
            Apply(package, geometry);
            if (theme == ThemeService.Neumorphism)
            {
                package["ControlCornerRadius"] = new CornerRadius(12);
                package["ChipCornerRadius"] = new CornerRadius(12);
            }
            if (theme == ThemeService.Bento && bento.LayoutPreset == BentoLayoutPreset.CompactDense)
            {
                double card = Math.Max(6d, CardRadius(geometry.Rounding) - 2d);
                package["CardCornerRadius"] = new CornerRadius(card);
                package["ControlCornerRadius"] = new CornerRadius(card / 2);
            }
        };
    }
}
