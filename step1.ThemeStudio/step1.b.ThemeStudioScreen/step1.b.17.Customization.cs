using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ProGlassAutomation.Customization;
using ProGlassAutomation.ThemeBento;
using ProGlassAutomation.ThemeClaymorphism;
using ProGlassAutomation.ThemeLiquidGlass;
using ProGlassAutomation.ThemeNeumorphism;
using ProGlassAutomation.Theming;

namespace ProGlassAutomation.ThemeStudio;

public sealed partial class ThemeStudioViewModel
{
    private BentoPreferences preferences = new();
    private BentoCustomizationState extended = new();
    private ClayCustomizationState clay = new();
    private NeumorphCustomizationState neumorph = new();
    private LiquidCustomizationState liquid = new();
    private SharedGeometryState geometry = new();
    private FontCustomizationState font = new();
    private IconCustomizationState icons = new();
    // Null = theme default (each theme keeps its own rhythm); non-null = a pinned page size.
    private PageCustomizationState? pages = null;
    private ColorPlateState colors = new();
    private string selectedColorRole = ColorPlateService.Roles[0];
    private string colorRoleHexDraft = "";
    private SavedCustomization lastSaved = CustomizationStore.Load();
    private ThemeEditSession session = null!;
    private BentoCustomizationState draft => extended;
    // Inquiry (index 2) is the first business page and the app's default landing page.
    private int selectedWorkspace = 2;
    private bool isMenuOpen = true;
    private string customizationCategory = "Appearance";

    private string selectedComponentId = "";
    private string presetNameInput = "";
    private string? lastSavedSnapshot;
    private string? lastSavedClaySnapshot;
    private string? lastSavedNeumorphSnapshot;
    private string? lastSavedLiquidSnapshot;
    public ProGlassAutomation.Customization.ChartCustomizationViewModel ChartEditor { get; } = new();
    public IReadOnlyList<BentoAccent> AccentChoices { get; } = Enum.GetValues<BentoAccent>();
    public IReadOnlyList<BentoDensity> DensityChoices { get; } = Enum.GetValues<BentoDensity>();
    public IReadOnlyList<BentoShape> ShapeChoices { get; } = Enum.GetValues<BentoShape>();
    public IReadOnlyList<CardRounding> RoundingChoices { get; } = Enum.GetValues<CardRounding>();
    public IReadOnlyList<BentoType> TypeChoices { get; } = Enum.GetValues<BentoType>();
    public IReadOnlyList<BentoCanvas> CanvasChoices { get; } = Enum.GetValues<BentoCanvas>();
    public IReadOnlyList<BentoSurfaceStyle> SurfaceChoices { get; } = Enum.GetValues<BentoSurfaceStyle>();
    public IReadOnlyList<BentoTextContrast> TextContrastChoices { get; } = Enum.GetValues<BentoTextContrast>();
    public IReadOnlyList<BentoBorderIntensity> BorderChoices { get; } = Enum.GetValues<BentoBorderIntensity>();
    public IReadOnlyList<BentoShadowIntensity> ShadowChoices { get; } = Enum.GetValues<BentoShadowIntensity>();
    public IReadOnlyList<BentoSpacing> SpacingChoices { get; } = Enum.GetValues<BentoSpacing>();
    public IReadOnlyList<BentoCardPadding> PaddingChoices { get; } = Enum.GetValues<BentoCardPadding>();
    public IReadOnlyList<BentoFontWeight> WeightChoices { get; } = Enum.GetValues<BentoFontWeight>();
    public IReadOnlyList<string> AvailableCategories => CustomizationCategories.All;
    public IReadOnlyList<BentoLayoutPreset> LayoutPresetChoices { get; } = Enum.GetValues<BentoLayoutPreset>();
    public IReadOnlyList<ClayAccent> ClayAccentChoices { get; } = Enum.GetValues<ClayAccent>();
    public IReadOnlyList<ClayVolume> ClayVolumeChoices { get; } = Enum.GetValues<ClayVolume>();
    public IReadOnlyList<ClayPlumpness> ClayPlumpnessChoices { get; } = Enum.GetValues<ClayPlumpness>();
    public IReadOnlyList<ClayDensity> ClayDensityChoices { get; } = Enum.GetValues<ClayDensity>();
    public IReadOnlyList<ClayCanvas> ClayCanvasChoices { get; } = Enum.GetValues<ClayCanvas>();
    public IReadOnlyList<ClayType> ClayTypeChoices { get; } = Enum.GetValues<ClayType>();
    public IReadOnlyList<ClaySpacing> ClaySpacingChoices { get; } = Enum.GetValues<ClaySpacing>();
    public IReadOnlyList<ClayTextContrast> ClayTextContrastChoices { get; } = Enum.GetValues<ClayTextContrast>();
    public IReadOnlyList<ClaySheen> ClaySheenChoices { get; } = Enum.GetValues<ClaySheen>();
    public IReadOnlyList<ClayWeight> ClayWeightChoices { get; } = Enum.GetValues<ClayWeight>();
    public IReadOnlyList<ClayLighting> ClayLightingChoices { get; } = Enum.GetValues<ClayLighting>();
    public IReadOnlyList<ClayRelief> ClayReliefChoices { get; } = Enum.GetValues<ClayRelief>();
    public IReadOnlyList<ClayEffects> ClayEffectsChoices { get; } = Enum.GetValues<ClayEffects>();
    private bool overlayBusy;
    private IReadOnlyList<string> fontFamilyChoices = FontService.AvailableFamilies();
    public IReadOnlyList<string> FontFamilyChoices => fontFamilyChoices;
    public IReadOnlyList<IconStyleCard> IconStyleCards => IconService.Cards;
    public IconMarkStyle SelectedIconStyle
    {
        get => icons.Style;
        set { if (value != icons.Style) UpdateIcons(icons with { Style = value }); }
    }
    public string SelectedIconCaption =>
        IconService.Cards.FirstOrDefault(card => card.Style == icons.Style)?.Caption ?? "";
    public IReadOnlyList<PageStyleCard> PageSizeCards => PageSizeService.Cards;
    public PageSize? SelectedPageSize
    {
        get => pages?.Size;
        set
        {
            if (value is null || pages?.Size == value) return;
            UpdatePages(new PageCustomizationState(value.Value));
        }
    }
    public string SelectedPageCaption
    {
        get
        {
            var current = pages;
            return current is null
                ? "Theme default — each theme keeps its own spacing rhythm. Pick a size to pin one layout across all pages."
                : PageSizeService.Cards.FirstOrDefault(card => card.Size == current.Size)?.Caption ?? "";
        }
    }
    public IReadOnlyList<string> ColorPlateChoices => ColorPlateService.BuiltIn.Select(plate => plate.Name).ToList();
    public IReadOnlyList<ColorPlateCard> ColorPlateCards => ColorPlateService.Cards;
    public IReadOnlyList<string> ColorRoleChoices => ColorPlateService.Roles;
    public string SelectedFontFamily
    {
        get => font.Family;
        set
        {
            if (overlayBusy || string.IsNullOrWhiteSpace(value) || value == font.Family) return;
            UpdateFont(font with { Family = value });
        }
    }
    public bool HeadingBold { get => font.HeadingBold; set => UpdateFont(font with { HeadingBold = value }); }
    public bool BodyItalic { get => font.BodyItalic; set => UpdateFont(font with { BodyItalic = value }); }
    public bool BodyUnderline { get => font.BodyUnderline; set => UpdateFont(font with { BodyUnderline = value }); }
    public string SelectedColorPlate
    {
        get => colors.Plate;
        set { if (!string.IsNullOrWhiteSpace(value)) UpdateColors(colors with { Plate = value }); }
    }
    public string SelectedColorRole
    {
        get => selectedColorRole;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == selectedColorRole) return;
            selectedColorRole = value;
            colorRoleHexDraft = ColorPlateService.ResolvedHex(colors, themes.ActiveAppearance, value);
            Changed(nameof(SelectedColorRole));
            Changed(nameof(ColorRoleHex));
        }
    }
    public string ColorRoleHex
    {
        get => colorRoleHexDraft;
        set
        {
            if (overlayBusy || string.Equals(value, colorRoleHexDraft, StringComparison.OrdinalIgnoreCase)) return;
            colorRoleHexDraft = value ?? "";
            Changed(nameof(ColorRoleHex));
            if (ColorPlateService.ParseHex(colorRoleHexDraft) is not Color parsed) return;
            string hex = ColorPlateService.ToHex(parsed);
            string plateHex = ColorPlateService.ResolvedHex(colors with { RoleHex = null }, themes.ActiveAppearance, selectedColorRole);
            var nextRoles = colors.RoleHex is null ? new Dictionary<string, string>() : new Dictionary<string, string>(colors.RoleHex);
            if (string.Equals(hex, plateHex, StringComparison.OrdinalIgnoreCase))
                nextRoles.Remove(selectedColorRole);
            else
                nextRoles[selectedColorRole] = hex;
            UpdateColors(colors with { RoleHex = nextRoles.Count == 0 ? null : nextRoles });
        }
    }
    public string ColorStopSummary =>
        string.Join("  ·  ", ColorPlateService.StopsOf(colors.Plate).Select(stop => stop.Name + " " + stop.Hex));
    public string ColorRoleSummary =>
        string.Join("  ·  ", ColorPlateService.Roles.Select(role => role + " " + ColorPlateService.ResolvedHex(colors, themes.ActiveAppearance, role)));
    public IReadOnlyList<string> CardGradientChoices => ColorPlateService.CardGradients;
    public IReadOnlyList<ThemeIdentity> AboutThemes => ThemeCatalog.Sequence;
    public int CardSaturation
    {
        get => colors.CardSaturation;
        set
        {
            int next = Math.Clamp(value, 0, 100);
            if (next == colors.CardSaturation) return;
            UpdateColors(colors with { CardSaturation = next });
        }
    }
    public string SelectedCardGradient
    {
        get => colors.CardGradient;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == colors.CardGradient) return;
            UpdateColors(colors with { CardGradient = value });
        }
    }
    public string CardTintHex
    {
        get => colors.CardTint;
        set
        {
            if (overlayBusy || string.Equals(value, colors.CardTint, StringComparison.OrdinalIgnoreCase)) return;
            if (ColorPlateService.ParseHex(value) is not Color parsed) return;
            UpdateColors(colors with { CardTint = ColorPlateService.ToHex(parsed) });
        }
    }
    public string CardTintEndHex
    {
        get => colors.CardTintEnd;
        set
        {
            if (overlayBusy || string.Equals(value, colors.CardTintEnd, StringComparison.OrdinalIgnoreCase)) return;
            if (ColorPlateService.ParseHex(value) is not Color parsed) return;
            UpdateColors(colors with { CardTintEnd = ColorPlateService.ToHex(parsed) });
        }
    }
    public string CardTintSummary =>
        colors.CardSaturation <= 0
            ? "0 keeps theme card surfaces. 100 is the coded start colour. Status plates are never washed."
            : colors.CardSaturation + "% toward " + colors.CardTint
                + (colors.CardGradient == "Solid" ? "." : " → " + colors.CardTintEnd + " (" + colors.CardGradient + ").");
    public IReadOnlyList<NeumorphDepth> NeumorphDepthChoices { get; } = Enum.GetValues<NeumorphDepth>();
    public IReadOnlyList<NeumorphBlur> NeumorphBlurChoices { get; } = Enum.GetValues<NeumorphBlur>();
    public IReadOnlyList<NeumorphIntensity> NeumorphIntensityChoices { get; } = Enum.GetValues<NeumorphIntensity>();
    public IReadOnlyList<NeumorphNamedPreset> NeumorphBuiltInPresets => NeumorphCustomization.BuiltIn;
    public IReadOnlyList<NeumorphNamedPreset> NeumorphCustomPresets => lastSaved.NeumorphPresets ?? [];
    public IReadOnlyList<string> NeumorphPresetNames => NeumorphCustomization.BuiltIn.Select(p => p.Name).Concat((lastSaved.NeumorphPresets ?? []).Select(p => p.Name)).ToList();
    private string selectedNeumorphPresetName = NeumorphCustomization.BuiltIn[0].Name;
    public string SelectedNeumorphPresetName
    {
        get => selectedNeumorphPresetName;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == selectedNeumorphPresetName) return;
            selectedNeumorphPresetName = value;
            Changed(nameof(SelectedNeumorphPresetName));
            ApplyPreset.Execute(value);
        }
    }
    public NeumorphDepth SelectedNeumorphDepth { get => neumorph.Depth; set => UpdateNeumorph(neumorph with { Depth = value }); }
    public NeumorphBlur SelectedNeumorphBlur { get => neumorph.Blur; set => UpdateNeumorph(neumorph with { Blur = value }); }
    public NeumorphIntensity SelectedNeumorphIntensity { get => neumorph.Intensity; set => UpdateNeumorph(neumorph with { Intensity = value }); }
    public IReadOnlyList<LiquidFrost> LiquidFrostChoices { get; } = Enum.GetValues<LiquidFrost>();
    public IReadOnlyList<LiquidSpecular> LiquidSpecularChoices { get; } = Enum.GetValues<LiquidSpecular>();
    public IReadOnlyList<LiquidTint> LiquidTintChoices { get; } = Enum.GetValues<LiquidTint>();
    public IReadOnlyList<LiquidNamedPreset> LiquidBuiltInPresets => LiquidCustomization.BuiltIn;
    public IReadOnlyList<LiquidNamedPreset> LiquidCustomPresets => lastSaved.LiquidPresets ?? [];
    public IReadOnlyList<string> LiquidPresetNames => LiquidCustomization.BuiltIn.Select(p => p.Name).Concat((lastSaved.LiquidPresets ?? []).Select(p => p.Name)).ToList();
    private string selectedLiquidPresetName = LiquidCustomization.BuiltIn[0].Name;
    public string SelectedLiquidPresetName
    {
        get => selectedLiquidPresetName;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == selectedLiquidPresetName) return;
            selectedLiquidPresetName = value;
            Changed(nameof(SelectedLiquidPresetName));
            ApplyPreset.Execute(value);
        }
    }
    public LiquidFrost SelectedLiquidFrost { get => liquid.Frost; set => UpdateLiquid(liquid with { Frost = value }); }
    public LiquidSpecular SelectedLiquidSpecular { get => liquid.Specular; set => UpdateLiquid(liquid with { Specular = value }); }
    public LiquidTint SelectedLiquidTint { get => liquid.Tint; set => UpdateLiquid(liquid with { Tint = value }); }
    public IReadOnlyList<ClayNamedPreset> ClayBuiltInPresets => ClayCustomization.BuiltIn;
    public IReadOnlyList<ClayNamedPreset> ClayCustomPresets => lastSaved.ClayPresets ?? [];
    public IReadOnlyList<string> ClayPresetNames => ClayCustomization.BuiltIn.Select(p => p.Name).Concat((lastSaved.ClayPresets ?? []).Select(p => p.Name)).ToList();
    private string selectedClayPresetName = ClayCustomization.BuiltIn[0].Name;
    public string SelectedClayPresetName
    {
        get => selectedClayPresetName;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == selectedClayPresetName) return;
            selectedClayPresetName = value;
            Changed(nameof(SelectedClayPresetName));
            ApplyPreset.Execute(value);
        }
    }
    public ClayAccent SelectedClayAccent { get => clay.Accent; set => UpdateClay(clay with { Accent = value }); }
    public ClayVolume SelectedClayVolume { get => clay.Volume; set => UpdateClay(clay with { Volume = value }); }
    public ClayPlumpness SelectedClayPlumpness { get => clay.Plumpness; set => UpdateClay(clay with { Plumpness = value }); }
    public ClayDensity SelectedClayDensity { get => clay.Density; set => UpdateClay(clay with { Density = value }); }
    public ClayCanvas SelectedClayCanvas { get => clay.Canvas; set => UpdateClay(clay with { Canvas = value }); }
    public ClayType SelectedClayTypography { get => clay.Typography; set => UpdateClay(clay with { Typography = value }); }
    public ClaySpacing SelectedClaySpacing { get => clay.Spacing; set => UpdateClay(clay with { Spacing = value }); }
    public ClayTextContrast SelectedClayTextContrast { get => clay.TextContrast; set => UpdateClay(clay with { TextContrast = value }); }
    public ClaySheen SelectedClaySheen { get => clay.Sheen; set => UpdateClay(clay with { Sheen = value }); }
    public ClayWeight SelectedClayWeight { get => clay.Weight; set => UpdateClay(clay with { Weight = value }); }
    public ClayLighting SelectedClayLighting { get => clay.Lighting; set => UpdateClay(clay with { Lighting = value }); }
    public ClayRelief SelectedClayRelief { get => clay.Relief; set => UpdateClay(clay with { Relief = value }); }
    public ClayEffects SelectedClayEffects { get => clay.Effects; set => UpdateClay(clay with { Effects = value }); }
    public IReadOnlyList<NamedPreset> BuiltInPresets => PresetCatalog.BuiltIn;
    public IReadOnlyList<NamedPreset> CustomPresets => lastSaved.CustomPresets ?? [];
    public BentoAccent SelectedAccent { get => extended.Accent; set => UpdateExtended(extended with { Accent = value }); }
    public BentoDensity SelectedDensity { get => extended.Density; set => UpdateExtended(extended with { Density = value }); }
    public BentoShape SelectedShape
    {
        get => extended.Shape;
        set
        {
            if (!CanCustomizeBento || value == extended.Shape) return;
            geometry = geometry with { Rounding = SharedGeometry.FromShape(value) };
            UpdateExtended(extended with { Shape = value });
            Changed(nameof(SelectedRounding));
        }
    }
    public CardRounding SelectedRounding
    {
        get => geometry.Rounding;
        set
        {
            if (!CanCustomizeGeometry || value == geometry.Rounding) return;
            geometry = geometry with { Rounding = value };
            if (CanCustomizeBento)
                UpdateExtended(extended with { Shape = SharedGeometry.ToShape(value) });
            else
                ApplySharedOnly();
            Changed(nameof(SelectedRounding));
            Changed(nameof(DesignSummary));
            Changed(nameof(IsDirty));
        }
    }
    public BentoType SelectedTypography { get => extended.Typography; set => UpdateExtended(extended with { Typography = value }); }
    public BentoCanvas SelectedCanvas { get => extended.Canvas; set => UpdateExtended(extended with { Canvas = value }); }
    public BentoSurfaceStyle SelectedSurface { get => extended.SurfaceStyle; set => UpdateExtended(extended with { SurfaceStyle = value }); }
    public BentoTextContrast SelectedTextContrast { get => extended.TextContrast; set => UpdateExtended(extended with { TextContrast = value }); }
    public BentoBorderIntensity SelectedBorder { get => extended.BorderIntensity; set => UpdateExtended(extended with { BorderIntensity = value }); }
    public BentoShadowIntensity SelectedShadow { get => extended.Shadow; set => UpdateExtended(extended with { Shadow = value }); }
    public BentoSpacing SelectedSpacing { get => extended.Spacing; set => UpdateExtended(extended with { Spacing = value }); }
    public BentoCardPadding SelectedPadding { get => extended.CardPadding; set => UpdateExtended(extended with { CardPadding = value }); }
    public BentoFontWeight SelectedWeight { get => extended.FontWeight; set => UpdateExtended(extended with { FontWeight = value }); }
    public BentoLayoutPreset SelectedLayoutPreset { get => extended.LayoutPreset; set => UpdateExtended(extended with { LayoutPreset = value }); }
    public bool HeroCompact { get => extended.HeroCompact; set => UpdateExtended(extended with { HeroCompact = value }); }
    public bool ShowKpiTrend { get => extended.ShowKpiTrend; set => UpdateExtended(extended with { ShowKpiTrend = value }); }
    public string CustomizationCategory { get => customizationCategory; set { customizationCategory = value; Changed(nameof(CustomizationCategory)); } }
    public string SelectedComponentId { get => selectedComponentId; set { selectedComponentId = value; Changed(nameof(SelectedComponentId)); Changed(nameof(ContextualTitle)); } }
    public string ContextualTitle => string.IsNullOrEmpty(selectedComponentId) ? "Select a card in the preview" : $"Editing: {selectedComponentId}";
    public string PresetNameInput { get => presetNameInput; set { presetNameInput = value; Changed(nameof(PresetNameInput)); } }
    public bool IsClay => SelectedTheme == ThemeService.Clay;
    public bool IsFluent => SelectedTheme == ThemeService.Fluent;
    public bool IsNeumorph => SelectedTheme == ThemeService.Neumorphism;
    public bool IsLiquid => SelectedTheme == ThemeService.LiquidGlass;
    public bool IsGlass => SelectedTheme == ThemeService.Glassmorphism;
    public bool IsSpatial => SelectedTheme == ThemeService.Spatial;
    public bool IsSwiss => SelectedTheme == ThemeService.Swiss;
    public bool IsHolographic => SelectedTheme == ThemeService.Holographic;
    public bool CanCustomizeBento => SelectedTheme == ThemeService.Bento;
    public bool CanCustomizeClay => IsClay;
    public bool CanCustomizeNeumorph => IsNeumorph;
    public bool CanCustomizeLiquid => IsLiquid;
    public bool CanCustomizeGeometry => ThemeCatalog.Find(SelectedTheme).Geometry == ThemeGeometry.SharedTokens;
    public bool CanCustomize => CanCustomizeBento || CanCustomizeClay || CanCustomizeNeumorph || CanCustomizeLiquid;
    public string DesignSummary => CanCustomizeClay
        ? $"{SelectedClayAccent} / {SelectedClayVolume} / {SelectedClayPlumpness} / {SelectedClayTypography}"
        : CanCustomizeBento ? $"{SelectedAccent} / {SelectedDensity} / {SelectedShape} / {SelectedTypography} · {SelectedLayoutPreset}"
        : IsFluent ? $"Fluent / {SelectedRounding}"
        : IsNeumorph ? $"Neumorphism / {SelectedRounding} / {SelectedNeumorphDepth} / {SelectedNeumorphBlur}"
        : IsLiquid ? $"Liquid Glass / {SelectedRounding} / {SelectedLiquidFrost} / {SelectedLiquidSpecular}"
        : IsGlass ? $"Glassmorphism / {SelectedRounding}"
        : IsSpatial ? $"Spatial / {SelectedRounding}"
        : IsSwiss ? $"Swiss / {SelectedRounding}"
        : IsHolographic ? $"Holographic / {SelectedRounding}"
        : "Theme-default settings";
    public bool IsDirty =>
        themes.ActiveTheme != lastSaved.Theme
        || themes.ActiveAppearance != lastSaved.Appearance
        || geometry != (lastSaved.Geometry ?? new SharedGeometryState())
        || (CanCustomizeClay && clay != (lastSaved.Clay ?? new ClayCustomizationState()))
        || (CanCustomizeNeumorph && neumorph != (lastSaved.Neumorph ?? new NeumorphCustomizationState()))
        || (CanCustomizeLiquid && liquid != (lastSaved.Liquid ?? new LiquidCustomizationState()))
        || (CanCustomizeBento && extended != lastSaved.Preferences)
        || font != (lastSaved.Font ?? new FontCustomizationState())
        || icons != (lastSaved.Icons ?? new IconCustomizationState())
        || pages != lastSaved.Pages
        || !SameColors(colors, lastSaved.Colors ?? new ColorPlateState());
    public int SelectedWorkspace
    {
        get => selectedWorkspace;
        set
        {
            if (value is >= 0 and <= 10)
            {
                selectedWorkspace = value;
                Changed(nameof(SelectedWorkspace));
                Changed(nameof(IsStudio));
                Changed(nameof(IsBusiness));
                Changed(nameof(IsInquiry));
                Changed(nameof(IsTexts));
                Changed(nameof(IsColors));
                Changed(nameof(IsAbout));
                Changed(nameof(IsIcons));
                Changed(nameof(IsPages));
                Changed(nameof(IsClients));
                Changed(nameof(IsUsers));
                Changed(nameof(IsCompany));
                Changed(nameof(WorkspaceTitle));
                Changed(nameof(WorkspaceSubtitle));
                Changed(nameof(WorkspaceIcon));
            }
        }
    }
    public bool IsStudio { get => selectedWorkspace == 0; set { if (value) SelectedWorkspace = 0; } }
    public bool IsBusiness { get => selectedWorkspace == 1; set { if (value) SelectedWorkspace = 1; } }
    public bool IsInquiry { get => selectedWorkspace == 2; set { if (value) SelectedWorkspace = 2; } }
    public bool IsTexts { get => selectedWorkspace == 3; set { if (value) SelectedWorkspace = 3; } }
    public bool IsColors { get => selectedWorkspace == 4; set { if (value) SelectedWorkspace = 4; } }
    public bool IsAbout { get => selectedWorkspace == 5; set { if (value) SelectedWorkspace = 5; } }
    public bool IsIcons { get => selectedWorkspace == 6; set { if (value) SelectedWorkspace = 6; } }
    public bool IsPages { get => selectedWorkspace == 7; set { if (value) SelectedWorkspace = 7; } }
    /// <summary>step2 Client Management workspace (index 8; appended so the
    /// existing workspace indexes 0–7 keep their meaning).</summary>
    public bool IsClients { get => selectedWorkspace == 8; set { if (value) SelectedWorkspace = 8; } }
    /// <summary>step3 User Management workspace (index 9; appended for the same
    /// reason — every earlier index keeps its meaning).</summary>
    public bool IsUsers { get => selectedWorkspace == 9; set { if (value) SelectedWorkspace = 9; } }
    /// <summary>step4 Company Profile workspace (index 10; appended for the same
    /// reason — every earlier index keeps its meaning).</summary>
    public bool IsCompany { get => selectedWorkspace == 10; set { if (value) SelectedWorkspace = 10; } }
    public IconKind WorkspaceIcon => selectedWorkspace switch
    {
        0 => IconKind.Theme,
        1 => IconKind.Home,
        2 => IconKind.Inquiry,
        3 => IconKind.Texts,
        4 => IconKind.Colors,
        5 => IconKind.About,
        6 => IconKind.Marks,
        7 => IconKind.Layout,
        // Clients moved from the person mark to the briefcase when step3 added
        // the user directory: a filled person silhouette now means "the people
        // who use this application", and a briefcase means "the companies we
        // work for". One icon per concept, no duplicates in the rail.
        8 => IconKind.Work,
        9 => IconKind.Author,
        // The company itself is an installation setting, not a record list, so
        // it carries the gear rather than a fourth people/business mark.
        10 => IconKind.Settings,
        _ => IconKind.Home
    };
    public string WorkspaceTitle => selectedWorkspace switch
    {
        0 => "Theme Studio",
        1 => "Home",
        2 => "Inquiries",
        3 => "Texts Studio",
        4 => "Colors Studio",
        5 => "About",
        6 => "Icons Studio",
        7 => "Pages Studio",
        8 => "Clients",
        9 => "Users",
        10 => "Company",
        _ => "Home"
    };
    public string WorkspaceSubtitle => selectedWorkspace switch
    {
        0 => "Settings · Theme engine",
        1 => "Home",
        2 => "Business · Inquiry register",
        3 => "Settings · Type overlay",
        4 => "Settings · Color plates",
        5 => "Settings · Product and credits",
        6 => "Settings · Navigation marks",
        7 => "Settings · Page sizing",
        8 => "Business · Client directory",
        9 => "Administration · Users, departments and roles",
        10 => "Settings · Company profile, contact details and logo",
        _ => "Home"
    };
    public bool IsMenuOpen { get => isMenuOpen; set { isMenuOpen = value; Changed(nameof(IsMenuOpen)); } }
    public string ShellClock => DateTime.Now.ToString("dd MMM yyyy / HH:mm", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
    public ICommand ToggleMenu { get; private set; } = null!;
    public ICommand ToggleAppearance { get; private set; } = null!;
    public ICommand ResetDesign { get; private set; } = null!;
    public ICommand ShowStudio { get; private set; } = null!;
    public ICommand ShowBusiness { get; private set; } = null!;
    public ICommand ShowInquiry { get; private set; } = null!;
    public ICommand ShowTexts { get; private set; } = null!;
    public ICommand ShowColors { get; private set; } = null!;
    public ICommand ShowAbout { get; private set; } = null!;
    public ICommand ShowIcons { get; private set; } = null!;
    public ICommand ResetIcons { get; private set; } = null!;
    public ICommand ShowPages { get; private set; } = null!;
    public ICommand ShowClients { get; private set; } = null!;
    public ICommand ShowUsers { get; private set; } = null!;
    public ICommand ShowCompany { get; private set; } = null!;
    public ICommand ResetPages { get; private set; } = null!;
    public ICommand SelectColorPlate { get; private set; } = null!;
    public ICommand ApplyPreview { get; private set; } = null!;
    public ICommand SaveCustomization { get; private set; } = null!;
    public ICommand DiscardChanges { get; private set; } = null!;
    public ICommand ResetSection { get; private set; } = null!;
    public ICommand ResetTheme { get; private set; } = null!;
    public ICommand PreviewDefault { get; private set; } = null!;
    public ICommand ApplyPreset { get; private set; } = null!;
    public ICommand SavePreset { get; private set; } = null!;
    public ICommand DeletePreset { get; private set; } = null!;
    public ICommand RenamePreset { get; private set; } = null!;
    public ICommand ImportCustomization { get; private set; } = null!;
    public ICommand ExportCustomization { get; private set; } = null!;
    public ICommand ImportFont { get; private set; } = null!;
    public ICommand ResetFont { get; private set; } = null!;
    public ICommand ResetColors { get; private set; } = null!;
    public ICommand ClearColorOverride { get; private set; } = null!;

    public string StoreWarning => CustomizationStore.LoadWarning ?? "";

    private void PersistSaved(SavedCustomization state)
    {
        session.Save(state);
        lastSaved = session.Baseline;
    }

    private void InitializeCustomization()
    {
        // Restore last saved; if none, defaults
        extended = lastSaved.Preferences ?? new BentoCustomizationState();
        clay = lastSaved.Clay ?? new ClayCustomizationState();
        neumorph = lastSaved.Neumorph ?? new NeumorphCustomizationState();
        liquid = lastSaved.Liquid ?? new LiquidCustomizationState();
        geometry = lastSaved.Geometry ?? new SharedGeometryState(SharedGeometry.FromShape(extended.Shape));
        font = lastSaved.Font ?? new FontCustomizationState();
        icons = lastSaved.Icons ?? new IconCustomizationState();
        pages = lastSaved.Pages;
        colors = lastSaved.Colors ?? new ColorPlateState();
        colorRoleHexDraft = ColorPlateService.ResolvedHex(colors, lastSaved.Appearance, selectedColorRole);
        if (lastSaved.Geometry is null) lastSaved = lastSaved with { Geometry = geometry };
        session = ThemeEditSession.Begin(lastSaved);
        if (!string.IsNullOrEmpty(CustomizationStore.LoadWarning))
            ThemeError = CustomizationStore.LoadWarning;
        preferences = extended.ToLegacy();
        var restored = ThemeService.InstalledThemes.Contains(lastSaved.Theme) ? lastSaved.Theme : ThemeService.Bento;
        try
        {
            themes.Apply(restored, lastSaved.Appearance, SharedGeometry.Compose(restored, extended, clay, geometry, neumorph, font, colors, liquid, pages));
            IconService.Apply(icons);
        }
        catch (Exception error) { ProGlassAutomation.Diagnostics.AppLog.Error("theme.customization.restore", error, "the saved customisation could not be applied; the Studio is showing defaults"); }
        ResetDesign = new DelegateCommand(() =>
        {
            geometry = new SharedGeometryState();
            if (IsClay) ApplyClayState(new ClayCustomizationState());
            else if (IsNeumorph) ApplyNeumorphState(new NeumorphCustomizationState());
            else if (IsLiquid) ApplyLiquidState(new LiquidCustomizationState());
            else if (CanCustomizeBento) ApplyState(new BentoCustomizationState());
            else ApplySharedOnly();
            Changed(nameof(SelectedRounding));
        }, () => CanCustomize || CanCustomizeGeometry);
        ToggleMenu = new DelegateCommand(() => IsMenuOpen = !IsMenuOpen);
        ToggleAppearance = new DelegateCommand(() => { if (IsLight) IsDark = true; else IsLight = true; });
        ShowStudio = new DelegateCommand(() => SelectedWorkspace = 0);
        ShowBusiness = new DelegateCommand(() => SelectedWorkspace = 1);
        ShowInquiry = new DelegateCommand(() => SelectedWorkspace = 2);
        ShowTexts = new DelegateCommand(() => SelectedWorkspace = 3);
        ShowColors = new DelegateCommand(() => SelectedWorkspace = 4);
        ShowAbout = new DelegateCommand(() => SelectedWorkspace = 5);
        ShowIcons = new DelegateCommand(() => SelectedWorkspace = 6);
        ShowPages = new DelegateCommand(() => SelectedWorkspace = 7);
        ShowClients = new DelegateCommand(() => SelectedWorkspace = 8);
        ShowUsers = new DelegateCommand(() => SelectedWorkspace = 9);
        ShowCompany = new DelegateCommand(() => SelectedWorkspace = 10);
        SelectColorPlate = new DelegateCommand<string>(name => { if (!string.IsNullOrWhiteSpace(name)) SelectedColorPlate = name; });
        ApplyPreview = new DelegateCommand(() => { try { themes.Apply(SelectedTheme, themes.ActiveAppearance, LiveConfigure()); ThemeError=""; Feedback="Preview applied — unsaved changes present."; } catch(Exception ex){ ThemeError=ex.Message; } Changed(nameof(ThemeError)); }, () => CanCustomize || CanCustomizeGeometry);
        SaveCustomization = new DelegateCommand(() =>
        {
            if (extended.Validate() is not null || (clay ?? new ClayCustomizationState()).Validate() is not null || (neumorph ?? new NeumorphCustomizationState()).Validate() is not null || (liquid ?? new LiquidCustomizationState()).Validate() is not null || font.Validate() is not null || colors.Validate() is not null || icons.Validate() is not null || pages?.Validate() is not null)
            {
                ThemeError = "Cannot save invalid customization.";
                Changed(nameof(ThemeError));
                return;
            }
            var candidate = CaptureLive();
            try
            {
                PersistSaved(candidate);
                ThemeError = "";
                Feedback = "Customization saved — will restore on next launch.";
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
            {
                ThemeError = "Save failed; previous saved package kept. " + ex.Message;
            }
            Changed(nameof(ThemeError));
            Changed(nameof(IsDirty));
        }, () => extended.Validate() is null && (clay ?? new ClayCustomizationState()).Validate() is null && (neumorph ?? new NeumorphCustomizationState()).Validate() is null && (liquid ?? new LiquidCustomizationState()).Validate() is null && font.Validate() is null && colors.Validate() is null && icons.Validate() is null && pages?.Validate() is null);
        DiscardChanges = new DelegateCommand(() =>
        {
            RestoreSaved(session.Revert());
            lastSaved = session.Baseline;
            Feedback = "Discarded unsaved changes.";
        }, () => IsDirty);
        ImportCustomization = new DelegateCommand(() =>
        {
            try
            {
                var dialog = new OpenFileDialog { Filter = "ProGlass theme (*.json)|*.json", Title = "Import customization" };
                if (dialog.ShowDialog() != true) return;
                ApplyImported(CustomizationStore.ReadFile(dialog.FileName));
                Feedback = "Imported into the live application — use Save to persist, Discard to revert.";
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or JsonException)
            {
                ThemeError = "Import rejected: " + ex.Message;
                Changed(nameof(ThemeError));
            }
        });
        ExportCustomization = new DelegateCommand(() =>
        {
            try
            {
                var dialog = new SaveFileDialog { Filter = "ProGlass theme (*.json)|*.json", Title = "Export customization", FileName = "proglass-theme.json" };
                if (dialog.ShowDialog() != true) return;
                CustomizationStore.WriteFile(CaptureLive(), dialog.FileName);
                Feedback = "Exported current live customization.";
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
            {
                ThemeError = "Export failed: " + ex.Message;
                Changed(nameof(ThemeError));
            }
        });
        ImportFont = new DelegateCommand(() =>
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Filter = "Font files (*.ttf;*.otf;*.ttc;*.otc)|*.ttf;*.otf;*.ttc;*.otc",
                    Title = "Import a TrueType or OpenType font"
                };
                if (dialog.ShowDialog() != true) return;
                string name = FontService.Import(dialog.FileName);
                RefreshFontFamilies(name);
                UpdateFont(font with { Family = name });
                Feedback = "Imported font: " + name;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or FileFormatException)
            {
                ThemeError = "Font import rejected: " + ex.Message;
                Changed(nameof(ThemeError));
            }
        });
        ResetFont = new DelegateCommand(() =>
        {
            UpdateFont(new FontCustomizationState());
            RefreshFontFamilies(font.Family);
            Feedback = "Texts restored to Segoe UI.";
        });
        ResetIcons = new DelegateCommand(() =>
        {
            UpdateIcons(new IconCustomizationState());
            Feedback = "Icons restored to Fluent outline.";
        });
        ResetPages = new DelegateCommand(() => UpdatePages(null));
        ResetColors = new DelegateCommand(() =>
        {
            UpdateColors(new ColorPlateState());
            Feedback = "Colors restored to the Ocean plate.";
        });
        ClearColorOverride = new DelegateCommand(() =>
        {
            if (colors.RoleHex is null || !colors.RoleHex.ContainsKey(selectedColorRole)) return;
            var next = new Dictionary<string, string>(colors.RoleHex);
            next.Remove(selectedColorRole);
            UpdateColors(colors with { RoleHex = next.Count == 0 ? null : next });
        });
        ResetSection = new DelegateCommand(() =>
        {
            if (IsClay)
            {
                var defaults = new ClayCustomizationState();
                var next = customizationCategory switch
                {
                    "Appearance" => clay with { Accent = defaults.Accent, Canvas = defaults.Canvas, Volume = defaults.Volume, Plumpness = defaults.Plumpness, TextContrast = defaults.TextContrast, Sheen = defaults.Sheen, Lighting = defaults.Lighting, Relief = defaults.Relief, Effects = defaults.Effects },
                    "Layout" => clay with { Density = defaults.Density, Spacing = defaults.Spacing },
                    "Typography" => clay with { Typography = defaults.Typography, Weight = defaults.Weight },
                    _ => defaults
                };
                ApplyClayState(next);
                Feedback = $"Reset {customizationCategory} to clay defaults.";
                return;
            }
            if (IsNeumorph)
            {
                ApplyNeumorphState(new NeumorphCustomizationState());
                Feedback = $"Reset {customizationCategory} to neumorph defaults.";
                return;
            }
            if (IsLiquid)
            {
                ApplyLiquidState(new LiquidCustomizationState());
                Feedback = $"Reset {customizationCategory} to liquid glass defaults.";
                return;
            }
            var bentoDefaults = new BentoCustomizationState();
            var bentoNext = customizationCategory switch
            {
                "Appearance" => extended with { Canvas = bentoDefaults.Canvas, SurfaceStyle = bentoDefaults.SurfaceStyle, TextContrast = bentoDefaults.TextContrast, BorderIntensity = bentoDefaults.BorderIntensity, Shadow = bentoDefaults.Shadow },
                "Layout" => extended with { LayoutPreset = bentoDefaults.LayoutPreset, Spacing = bentoDefaults.Spacing, CardPadding = bentoDefaults.CardPadding, HeroCompact = bentoDefaults.HeroCompact },
                "Typography" => extended with { Typography = bentoDefaults.Typography, FontWeight = bentoDefaults.FontWeight, Spacing = bentoDefaults.Spacing },
                _ => bentoDefaults
            };
            ApplyState(bentoNext);
            Feedback = $"Reset {customizationCategory} to defaults.";
        }, () => CanCustomize);
        ResetTheme = new DelegateCommand(() =>
        {
            geometry = new SharedGeometryState();
            Changed(nameof(SelectedRounding));
            if (IsClay)
            {
                ApplyClayState(new ClayCustomizationState());
                lastSaved = lastSaved with { Clay = new ClayCustomizationState(), Theme = ThemeService.Clay, Appearance = themes.ActiveAppearance, Geometry = geometry };
                PersistSaved(lastSaved);
                Feedback = "Clay restored to defaults.";
                return;
            }
            if (IsNeumorph)
            {
                ApplyNeumorphState(new NeumorphCustomizationState());
                lastSaved = lastSaved with { Neumorph = new NeumorphCustomizationState(), Theme = ThemeService.Neumorphism, Appearance = themes.ActiveAppearance, Geometry = geometry };
                PersistSaved(lastSaved);
                Feedback = "Neumorphism restored to defaults.";
                return;
            }
            if (IsLiquid)
            {
                ApplyLiquidState(new LiquidCustomizationState());
                lastSaved = lastSaved with { Liquid = new LiquidCustomizationState(), Theme = ThemeService.LiquidGlass, Appearance = themes.ActiveAppearance, Geometry = geometry };
                PersistSaved(lastSaved);
                Feedback = "Liquid Glass restored to defaults.";
                return;
            }
            ApplyState(new BentoCustomizationState());
            lastSaved = new SavedCustomization();
            PersistSaved(lastSaved);
            Feedback = "Theme restored to Bento defaults.";
        }, () => CanCustomize);
        PreviewDefault = new DelegateCommand(() =>
        {
            if (IsClay)
            {
                if (lastSavedClaySnapshot == null)
                {
                    lastSavedClaySnapshot = System.Text.Json.JsonSerializer.Serialize(clay);
                    ApplyClayState(new ClayCustomizationState());
                    Feedback = "Previewing clay defaults — toggle again to restore.";
                }
                else
                {
                    var prev = System.Text.Json.JsonSerializer.Deserialize<ClayCustomizationState>(lastSavedClaySnapshot);
                    lastSavedClaySnapshot = null;
                    if (prev != null) ApplyClayState(prev);
                    Feedback = "Restored your clay preview.";
                }
                return;
            }
            if (IsNeumorph)
            {
                if (lastSavedNeumorphSnapshot == null)
                {
                    lastSavedNeumorphSnapshot = System.Text.Json.JsonSerializer.Serialize(neumorph);
                    ApplyNeumorphState(new NeumorphCustomizationState());
                    Feedback = "Previewing neumorph defaults — toggle again to restore.";
                }
                else
                {
                    var prev = System.Text.Json.JsonSerializer.Deserialize<NeumorphCustomizationState>(lastSavedNeumorphSnapshot);
                    lastSavedNeumorphSnapshot = null;
                    if (prev != null) ApplyNeumorphState(prev);
                    Feedback = "Restored your neumorph preview.";
                }
                return;
            }
            if (IsLiquid)
            {
                if (lastSavedLiquidSnapshot == null)
                {
                    lastSavedLiquidSnapshot = System.Text.Json.JsonSerializer.Serialize(liquid);
                    ApplyLiquidState(new LiquidCustomizationState());
                    Feedback = "Previewing liquid glass defaults — toggle again to restore.";
                }
                else
                {
                    var prev = System.Text.Json.JsonSerializer.Deserialize<LiquidCustomizationState>(lastSavedLiquidSnapshot);
                    lastSavedLiquidSnapshot = null;
                    if (prev != null) ApplyLiquidState(prev);
                    Feedback = "Restored your liquid glass preview.";
                }
                return;
            }
            if (lastSavedSnapshot == null)
            {
                lastSavedSnapshot = System.Text.Json.JsonSerializer.Serialize(extended);
                ApplyState(new BentoCustomizationState());
                Feedback = "Previewing defaults — toggle again to restore.";
            }
            else
            {
                var prev = System.Text.Json.JsonSerializer.Deserialize<BentoCustomizationState>(lastSavedSnapshot);
                lastSavedSnapshot = null;
                if (prev != null) ApplyState(prev);
                Feedback = "Restored your preview.";
            }
        }, () => CanCustomize);
        ApplyPreset = new DelegateCommand<string>(name =>
        {
            if (IsClay)
            {
                var clayPreset = ClayCustomization.BuiltIn.Concat(lastSaved.ClayPresets ?? []).FirstOrDefault(p => p.Name == name);
                if (clayPreset == null) return;
                if (clayPreset.Appearance != themes.ActiveAppearance) SelectAppearance(ThemeService.Clay, clayPreset.Appearance);
                ApplyClayState(clayPreset.State);
                Feedback = $"Clay preset applied: {clayPreset.Name}";
                return;
            }
            if (IsNeumorph)
            {
                var neuPreset = NeumorphCustomization.BuiltIn.Concat(lastSaved.NeumorphPresets ?? []).FirstOrDefault(p => p.Name == name);
                if (neuPreset == null) return;
                if (neuPreset.Appearance != themes.ActiveAppearance) SelectAppearance(ThemeService.Neumorphism, neuPreset.Appearance);
                ApplyNeumorphState(neuPreset.State);
                Feedback = $"Neumorph preset applied: {neuPreset.Name}";
                return;
            }
            if (IsLiquid)
            {
                var glassPreset = LiquidCustomization.BuiltIn.Concat(lastSaved.LiquidPresets ?? []).FirstOrDefault(p => p.Name == name);
                if (glassPreset == null) return;
                if (glassPreset.Appearance != themes.ActiveAppearance) SelectAppearance(ThemeService.LiquidGlass, glassPreset.Appearance);
                ApplyLiquidState(glassPreset.State);
                Feedback = $"Liquid Glass preset applied: {glassPreset.Name}";
                return;
            }
            var preset = BuiltInPresets.Concat(CustomPresets).FirstOrDefault(p => p.Name == name);
            if (preset != null)
            {
                ApplyState(preset.State);
                if (preset.Appearance != themes.ActiveAppearance) SelectAppearance(ThemeService.Bento, preset.Appearance);
                Feedback = $"Preset applied: {preset.Name}";
            }
        });
        SavePreset = new DelegateCommand(() =>
        {
            var n = presetNameInput.Trim();
            if (string.IsNullOrEmpty(n))
            {
                ThemeError = "Enter unique name not conflicting with built-in presets.";
                Changed(nameof(ThemeError));
                return;
            }
            if (IsClay)
            {
                if (ClayCustomization.IsBuiltIn(n))
                {
                    ThemeError = "Enter unique name not conflicting with built-in presets.";
                    Changed(nameof(ThemeError));
                    return;
                }
                var clayList = (lastSaved.ClayPresets ?? []).ToList();
                clayList.RemoveAll(p => p.Name == n);
                clayList.Add(new ClayNamedPreset(n, clay, themes.ActiveAppearance));
                lastSaved = lastSaved with { ClayPresets = clayList, Clay = clay, Theme = themes.ActiveTheme, Appearance = themes.ActiveAppearance };
                PersistSaved(lastSaved);
                PresetNameInput = "";
                Feedback = $"Clay preset saved: {n}";
                Changed(nameof(ThemeError));
                Changed(nameof(ClayCustomPresets));
                Changed(nameof(ClayPresetNames));
                return;
            }
            if (IsNeumorph)
            {
                if (NeumorphCustomization.IsBuiltIn(n))
                {
                    ThemeError = "Enter unique name not conflicting with built-in presets.";
                    Changed(nameof(ThemeError));
                    return;
                }
                var neuList = (lastSaved.NeumorphPresets ?? []).ToList();
                neuList.RemoveAll(p => p.Name == n);
                neuList.Add(new NeumorphNamedPreset(n, neumorph, themes.ActiveAppearance));
                lastSaved = lastSaved with { NeumorphPresets = neuList, Neumorph = neumorph, Theme = themes.ActiveTheme, Appearance = themes.ActiveAppearance };
                PersistSaved(lastSaved);
                PresetNameInput = "";
                Feedback = $"Neumorph preset saved: {n}";
                Changed(nameof(ThemeError));
                Changed(nameof(NeumorphCustomPresets));
                Changed(nameof(NeumorphPresetNames));
                return;
            }
            if (IsLiquid)
            {
                if (LiquidCustomization.IsBuiltIn(n))
                {
                    ThemeError = "Enter unique name not conflicting with built-in presets.";
                    Changed(nameof(ThemeError));
                    return;
                }
                var glassList = (lastSaved.LiquidPresets ?? []).ToList();
                glassList.RemoveAll(p => p.Name == n);
                glassList.Add(new LiquidNamedPreset(n, liquid, themes.ActiveAppearance));
                lastSaved = lastSaved with { LiquidPresets = glassList, Liquid = liquid, Theme = themes.ActiveTheme, Appearance = themes.ActiveAppearance };
                PersistSaved(lastSaved);
                PresetNameInput = "";
                Feedback = $"Liquid Glass preset saved: {n}";
                Changed(nameof(ThemeError));
                Changed(nameof(LiquidCustomPresets));
                Changed(nameof(LiquidPresetNames));
                return;
            }
            if (PresetCatalog.IsBuiltIn(n))
            {
                ThemeError = "Enter unique name not conflicting with built-in presets.";
                Changed(nameof(ThemeError));
                return;
            }
            var list = (lastSaved.CustomPresets ?? []).ToList();
            list.RemoveAll(p => p.Name == n);
            list.Add(new NamedPreset(n, extended, themes.ActiveAppearance));
            lastSaved = lastSaved with { CustomPresets = list };
            PersistSaved(lastSaved);
            PresetNameInput = "";
            Feedback = $"Preset saved: {n}";
            Changed(nameof(ThemeError));
        }, () => !string.IsNullOrWhiteSpace(presetNameInput) && CanCustomize);
        DeletePreset = new DelegateCommand<string>(name =>
        {
            if (IsClay)
            {
                if (ClayCustomization.IsBuiltIn(name)) return;
                var clayList = (lastSaved.ClayPresets ?? []).ToList();
                clayList.RemoveAll(p => p.Name == name);
                lastSaved = lastSaved with { ClayPresets = clayList };
                PersistSaved(lastSaved);
                Feedback = $"Deleted clay preset: {name}";
                Changed(nameof(ClayCustomPresets));
                Changed(nameof(ClayPresetNames));
                return;
            }
            if (IsNeumorph)
            {
                if (NeumorphCustomization.IsBuiltIn(name)) return;
                var neuList = (lastSaved.NeumorphPresets ?? []).ToList();
                neuList.RemoveAll(p => p.Name == name);
                lastSaved = lastSaved with { NeumorphPresets = neuList };
                PersistSaved(lastSaved);
                Feedback = $"Deleted neumorph preset: {name}";
                Changed(nameof(NeumorphCustomPresets));
                Changed(nameof(NeumorphPresetNames));
                return;
            }
            if (IsLiquid)
            {
                if (LiquidCustomization.IsBuiltIn(name)) return;
                var glassList = (lastSaved.LiquidPresets ?? []).ToList();
                glassList.RemoveAll(p => p.Name == name);
                lastSaved = lastSaved with { LiquidPresets = glassList };
                PersistSaved(lastSaved);
                Feedback = $"Deleted liquid glass preset: {name}";
                Changed(nameof(LiquidCustomPresets));
                Changed(nameof(LiquidPresetNames));
                return;
            }
            if (PresetCatalog.IsBuiltIn(name)) return;
            var list = (lastSaved.CustomPresets ?? []).ToList();
            list.RemoveAll(p => p.Name == name);
            lastSaved = lastSaved with { CustomPresets = list };
            PersistSaved(lastSaved);
            Feedback = $"Deleted preset: {name}";
        });
        RenamePreset = new DelegateCommand<(string oldName, string newName)>(pair =>
        {
            var n = pair.newName.Trim();
            if (string.IsNullOrEmpty(n)) return;
            if (IsClay)
            {
                if (ClayCustomization.IsBuiltIn(n)) return;
                var clayList = (lastSaved.ClayPresets ?? []).ToList();
                var clayIdx = clayList.FindIndex(p => p.Name == pair.oldName);
                if (clayIdx >= 0)
                {
                    clayList[clayIdx] = clayList[clayIdx] with { Name = n };
                    lastSaved = lastSaved with { ClayPresets = clayList };
                    PersistSaved(lastSaved);
                    Feedback = $"Renamed to {n}";
                    Changed(nameof(ClayCustomPresets));
                    Changed(nameof(ClayPresetNames));
                }
                return;
            }
            if (IsNeumorph)
            {
                if (NeumorphCustomization.IsBuiltIn(n)) return;
                var neuList = (lastSaved.NeumorphPresets ?? []).ToList();
                var neuIdx = neuList.FindIndex(p => p.Name == pair.oldName);
                if (neuIdx >= 0)
                {
                    neuList[neuIdx] = neuList[neuIdx] with { Name = n };
                    lastSaved = lastSaved with { NeumorphPresets = neuList };
                    PersistSaved(lastSaved);
                    Feedback = $"Renamed to {n}";
                    Changed(nameof(NeumorphCustomPresets));
                    Changed(nameof(NeumorphPresetNames));
                }
                return;
            }
            if (IsLiquid)
            {
                if (LiquidCustomization.IsBuiltIn(n)) return;
                var glassList = (lastSaved.LiquidPresets ?? []).ToList();
                var glassIdx = glassList.FindIndex(p => p.Name == pair.oldName);
                if (glassIdx >= 0)
                {
                    glassList[glassIdx] = glassList[glassIdx] with { Name = n };
                    lastSaved = lastSaved with { LiquidPresets = glassList };
                    PersistSaved(lastSaved);
                    Feedback = $"Renamed to {n}";
                    Changed(nameof(LiquidCustomPresets));
                    Changed(nameof(LiquidPresetNames));
                }
                return;
            }
            if (PresetCatalog.IsBuiltIn(n)) return;
            var list = (lastSaved.CustomPresets ?? []).ToList();
            var idx = list.FindIndex(p => p.Name == pair.oldName);
            if (idx >= 0)
            {
                list[idx] = list[idx] with { Name = n };
                lastSaved = lastSaved with { CustomPresets = list };
                PersistSaved(lastSaved);
                Feedback = $"Renamed to {n}";
            }
        });
    }
    private Action<ResourceDictionary, Appearance> LiveConfigure() =>
        SharedGeometry.Compose(SelectedTheme, extended, clay, geometry, neumorph, font, colors, liquid, pages);
    private void Customize(BentoPreferences candidate)
    {
        if (!CanCustomizeBento || candidate == preferences) return;
        UpdateExtended(new BentoCustomizationState(candidate.Accent, candidate.Density, candidate.Shape, candidate.Typography, extended.Canvas, extended.SurfaceStyle, extended.TextContrast, extended.BorderIntensity, extended.Shadow, extended.Spacing, extended.CardPadding, extended.FontWeight, extended.LayoutPreset, extended.HeroCompact, extended.ShowKpiTrend, extended.ShowTableStatus));
    }
    private void UpdateExtended(BentoCustomizationState next)
    {
        if (!CanCustomizeBento || next == extended) return;
        ApplyState(next);
    }
    private void UpdateClay(ClayCustomizationState next)
    {
        if (!CanCustomizeClay || next == clay) return;
        ApplyClayState(next);
    }
    private void ApplyState(BentoCustomizationState next)
    {
        try
        {
            themes.Apply(ThemeService.Bento, themes.ActiveAppearance, SharedGeometry.Compose(ThemeService.Bento, next, clay, geometry, neumorph, font, colors, pages: pages));
            extended = next; preferences = next.ToLegacy(); ThemeError=""; Feedback="Live preview updated — use Save to persist.";
        }
        catch (ThemeNotificationException error) { extended = next; preferences = next.ToLegacy(); ThemeError = error.Message; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.IO.IOException or System.Windows.Markup.XamlParseException) { ThemeError=$"Design unchanged: {error.Message}"; }
        foreach (var n in new[] { nameof(SelectedAccent), nameof(SelectedDensity), nameof(SelectedShape), nameof(SelectedTypography), nameof(SelectedCanvas), nameof(SelectedSurface), nameof(SelectedTextContrast), nameof(SelectedBorder), nameof(SelectedShadow), nameof(SelectedSpacing), nameof(SelectedPadding), nameof(SelectedWeight), nameof(SelectedLayoutPreset), nameof(HeroCompact), nameof(ShowKpiTrend), nameof(DesignSummary), nameof(ThemeError), nameof(IsDirty) }) Changed(n);
    }
    private void ApplyClayState(ClayCustomizationState next)
    {
        try
        {
            themes.Apply(ThemeService.Clay, themes.ActiveAppearance, SharedGeometry.Compose(ThemeService.Clay, extended, next, geometry, neumorph, font, colors, pages: pages));
            clay = next; ThemeError=""; Feedback="Live clay preview updated — use Save to persist.";
        }
        catch (ThemeNotificationException error) { clay = next; ThemeError = error.Message; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.IO.IOException or System.Windows.Markup.XamlParseException) { ThemeError=$"Design unchanged: {error.Message}"; }
        foreach (var n in new[] { nameof(SelectedClayAccent), nameof(SelectedClayVolume), nameof(SelectedClayPlumpness), nameof(SelectedClayDensity), nameof(SelectedClayCanvas), nameof(SelectedClayTypography), nameof(SelectedClaySpacing), nameof(SelectedClayTextContrast), nameof(SelectedClaySheen), nameof(SelectedClayWeight), nameof(SelectedClayLighting), nameof(SelectedClayRelief), nameof(SelectedClayEffects), nameof(DesignSummary), nameof(ThemeError), nameof(IsDirty) }) Changed(n);
    }

    private void UpdateNeumorph(NeumorphCustomizationState next)
    {
        if (!CanCustomizeNeumorph || next == neumorph) return;
        ApplyNeumorphState(next);
    }
    private void ApplyNeumorphState(NeumorphCustomizationState next)
    {
        try
        {
            themes.Apply(ThemeService.Neumorphism, themes.ActiveAppearance, SharedGeometry.Compose(ThemeService.Neumorphism, extended, clay, geometry, next, font, colors, liquid, pages));
            neumorph = next; ThemeError = ""; Feedback = "Live neumorph preview updated — use Save to persist.";
        }
        catch (ThemeNotificationException error) { neumorph = next; ThemeError = error.Message; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or System.Windows.Markup.XamlParseException) { ThemeError = $"Design unchanged: {error.Message}"; }
        foreach (var n in new[] { nameof(SelectedNeumorphDepth), nameof(SelectedNeumorphBlur), nameof(SelectedNeumorphIntensity), nameof(DesignSummary), nameof(ThemeError), nameof(IsDirty) }) Changed(n);
    }

    private void UpdateLiquid(LiquidCustomizationState next)
    {
        if (!CanCustomizeLiquid || next == liquid) return;
        ApplyLiquidState(next);
    }
    private void ApplyLiquidState(LiquidCustomizationState next)
    {
        try
        {
            themes.Apply(ThemeService.LiquidGlass, themes.ActiveAppearance, SharedGeometry.Compose(ThemeService.LiquidGlass, extended, clay, geometry, neumorph, font, colors, next, pages));
            liquid = next; ThemeError = ""; Feedback = "Live liquid glass preview updated — use Save to persist.";
        }
        catch (ThemeNotificationException error) { liquid = next; ThemeError = error.Message; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or System.Windows.Markup.XamlParseException) { ThemeError = $"Design unchanged: {error.Message}"; }
        foreach (var n in new[] { nameof(SelectedLiquidFrost), nameof(SelectedLiquidSpecular), nameof(SelectedLiquidTint), nameof(DesignSummary), nameof(ThemeError), nameof(IsDirty) }) Changed(n);
    }

    private SavedCustomization CaptureLive() => new(
        2,
        themes.ActiveAppearance,
        extended,
        lastSaved.ChartOptions,
        lastSaved.ComponentStyles,
        lastSaved.CustomPresets,
        themes.ActiveTheme,
        clay,
        lastSaved.ClayPresets,
        geometry,
        neumorph,
        lastSaved.NeumorphPresets,
        font,
        colors with { RoleHex = colors.RoleHex is null ? null : new Dictionary<string, string>(colors.RoleHex) },
        liquid,
        lastSaved.LiquidPresets,
        icons,
        pages);

    private void ApplySharedOnly()
    {
        try
        {
            themes.Apply(SelectedTheme, themes.ActiveAppearance, LiveConfigure());
            ThemeError = "";
            Feedback = "Live card rounding updated — use Save to persist.";
        }
        catch (ThemeNotificationException error) { ThemeError = error.Message; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or System.Windows.Markup.XamlParseException)
        {
            ThemeError = $"Design unchanged: {error.Message}";
        }
        Changed(nameof(ThemeError));
        Changed(nameof(IsDirty));
        Changed(nameof(DesignSummary));
    }

    private void ApplyImported(SavedCustomization loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        AdoptPackage(loaded, persist: false);
    }

    private void RestoreSaved(SavedCustomization saved) => AdoptPackage(saved, persist: false);

    private void AdoptPackage(SavedCustomization package, bool persist)
    {
        var nextBento = package.Preferences ?? new BentoCustomizationState();
        var nextClay = package.Clay ?? new ClayCustomizationState();
        var nextNeumorph = package.Neumorph ?? new NeumorphCustomizationState();
        var nextLiquid = package.Liquid ?? new LiquidCustomizationState();
        var nextGeometry = package.Geometry ?? new SharedGeometryState(SharedGeometry.FromShape(nextBento.Shape));
        var nextFont = package.Font ?? new FontCustomizationState();
        var nextIcons = package.Icons ?? new IconCustomizationState();
        var nextPages = package.Pages;
        var nextColors = package.Colors ?? new ColorPlateState();
        var theme = ThemeService.InstalledThemes.Contains(package.Theme) ? package.Theme : ThemeService.Bento;
        var previousBento = extended;
        var previousClay = clay;
        var previousNeumorph = neumorph;
        var previousLiquid = liquid;
        var previousGeometry = geometry;
        var previousFont = font;
        var previousIcons = icons;
        var previousPages = pages;
        var previousColors = colors;
        try
        {
            extended = nextBento;
            clay = nextClay;
            neumorph = nextNeumorph;
            liquid = nextLiquid;
            geometry = nextGeometry;
            font = nextFont;
            icons = nextIcons;
            pages = nextPages;
            colors = nextColors;
            IconService.Apply(icons);
            colorRoleHexDraft = ColorPlateService.ResolvedHex(colors, package.Appearance, selectedColorRole);
            preferences = nextBento.ToLegacy();
            themes.Apply(theme, package.Appearance, SharedGeometry.Compose(theme, nextBento, nextClay, nextGeometry, nextNeumorph, nextFont, nextColors, nextLiquid, nextPages));
            ThemeError = "";
            if (persist)
            {
                PersistSaved(package);
            }
        }
        catch (ThemeNotificationException error)
        {
            ThemeError = error.Message;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or System.Windows.Markup.XamlParseException)
        {
            extended = previousBento;
            clay = previousClay;
            neumorph = previousNeumorph;
            liquid = previousLiquid;
            geometry = previousGeometry;
            font = previousFont;
            icons = previousIcons;
            pages = previousPages;
            colors = previousColors;
            IconService.Apply(icons);
            colorRoleHexDraft = ColorPlateService.ResolvedHex(colors, themes.ActiveAppearance, selectedColorRole);
            preferences = previousBento.ToLegacy();
            ThemeError = error.Message;
        }
        foreach (var n in new[] { nameof(SelectedAccent), nameof(SelectedDensity), nameof(SelectedShape), nameof(SelectedTypography), nameof(SelectedCanvas), nameof(SelectedSurface), nameof(SelectedTextContrast), nameof(SelectedBorder), nameof(SelectedShadow), nameof(SelectedSpacing), nameof(SelectedPadding), nameof(SelectedWeight), nameof(SelectedLayoutPreset), nameof(HeroCompact), nameof(ShowKpiTrend), nameof(SelectedRounding), nameof(SelectedClayAccent), nameof(SelectedClayVolume), nameof(SelectedClayPlumpness), nameof(SelectedClayDensity), nameof(SelectedClayCanvas), nameof(SelectedClayTypography), nameof(SelectedClaySpacing), nameof(SelectedClayTextContrast), nameof(SelectedClaySheen), nameof(SelectedClayWeight), nameof(SelectedClayLighting), nameof(SelectedClayRelief), nameof(SelectedClayEffects), nameof(SelectedNeumorphDepth), nameof(SelectedNeumorphBlur), nameof(SelectedNeumorphIntensity), nameof(SelectedLiquidFrost), nameof(SelectedLiquidSpecular), nameof(SelectedLiquidTint), nameof(SelectedFontFamily), nameof(HeadingBold), nameof(BodyItalic), nameof(BodyUnderline), nameof(SelectedIconStyle), nameof(SelectedIconCaption), nameof(SelectedPageSize), nameof(SelectedPageCaption), nameof(SelectedColorPlate), nameof(ColorStopSummary), nameof(ColorRoleSummary), nameof(ColorRoleHex), nameof(FontFamilyChoices), nameof(DesignSummary), nameof(ThemeError), nameof(IsDirty) }) Changed(n);
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateIcons(IconCustomizationState next)
    {
        if (overlayBusy || next == icons) return;
        overlayBusy = true;
        try
        {
            IconService.Apply(next);
            icons = next;
            ThemeError = "";
            Feedback = "Live icon overlay updated — use Save to persist.";
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            ThemeError = $"Design unchanged: {error.Message}";
        }
        finally { overlayBusy = false; }
        Changed(nameof(SelectedIconStyle));
        Changed(nameof(SelectedIconCaption));
        NotifyFontColor();
    }

    private void UpdateFont(FontCustomizationState next)
    {
        if (overlayBusy || next == font) return;
        overlayBusy = true;
        try
        {
            themes.Overlay((package, _) => FontService.Apply(package, next));
            font = next;
            ThemeError = "";
            Feedback = "Live type overlay updated — use Save to persist.";
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or System.Windows.Markup.XamlParseException or System.Runtime.InteropServices.COMException)
        {
            ThemeError = $"Design unchanged: {error.Message}";
        }
        finally { overlayBusy = false; }
        NotifyFontColor();
    }

    private void UpdatePages(PageCustomizationState? next)
    {
        if (overlayBusy || next == pages) return;
        overlayBusy = true;
        try
        {
            if (next is null)
            {
                // Returning to the theme default rebuilds the live package so the
                // engine's own sizing tokens come back (the overlay wrote in place).
                themes.Apply(themes.ActiveTheme, themes.ActiveAppearance, LiveConfigure());
            }
            else
            {
                // Same pass as Compose: page sizing, then the card-copy inset.
                themes.Overlay((package, _) =>
                {
                    PageSizeService.Apply(package, next);
                    SharedGeometry.InsetCardCopy(package);
                });
            }
            pages = next;
            ThemeError = "";
            Feedback = next is null
                ? "Pages restored to the theme default — use Save to persist."
                : "Live page sizing updated — use Save to persist.";
        }
        catch (ThemeNotificationException error) { ThemeError = error.Message; }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or System.Windows.Markup.XamlParseException or System.Runtime.InteropServices.COMException)
        {
            ThemeError = $"Design unchanged: {error.Message}";
        }
        finally { overlayBusy = false; }
        Changed(nameof(SelectedPageSize));
        Changed(nameof(SelectedPageCaption));
        Changed(nameof(ThemeError));
        Changed(nameof(DesignSummary));
        Changed(nameof(IsDirty));
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateColors(ColorPlateState next)
    {
        if (overlayBusy || SameColors(next, colors)) return;
        overlayBusy = true;
        try
        {
            themes.Overlay((package, mode) => ColorPlateService.Apply(package, mode, next, themes.ActiveTheme));
            colors = next;
            colorRoleHexDraft = ColorPlateService.ResolvedHex(colors, themes.ActiveAppearance, selectedColorRole);
            ThemeError = "";
            Feedback = "Live color overlay updated — use Save to persist.";
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or System.Windows.Markup.XamlParseException or System.Runtime.InteropServices.COMException)
        {
            ThemeError = $"Design unchanged: {error.Message}";
        }
        finally { overlayBusy = false; }
        NotifyFontColor();
    }

    private void RefreshFontFamilies(string? extra = null)
    {
        var names = FontService.AvailableFamilies().ToList();
        if (!string.IsNullOrWhiteSpace(extra) && !names.Contains(extra, StringComparer.OrdinalIgnoreCase))
            names.Add(extra);
        fontFamilyChoices = names;
        Changed(nameof(FontFamilyChoices));
    }

    private void NotifyFontColor()
    {
        foreach (var n in new[] { nameof(SelectedFontFamily), nameof(HeadingBold), nameof(BodyItalic), nameof(BodyUnderline), nameof(SelectedIconStyle), nameof(SelectedIconCaption), nameof(SelectedPageSize), nameof(SelectedPageCaption), nameof(SelectedColorPlate), nameof(ColorStopSummary), nameof(ColorRoleSummary), nameof(ColorRoleHex), nameof(CardSaturation), nameof(SelectedCardGradient), nameof(CardTintHex), nameof(CardTintEndHex), nameof(CardTintSummary), nameof(ThemeError), nameof(IsDirty) })
            Changed(n);
        CommandManager.InvalidateRequerySuggested();
    }

    private static bool SameColors(ColorPlateState left, ColorPlateState right)
    {
        if (!string.Equals(left.Plate, right.Plate, StringComparison.OrdinalIgnoreCase)) return false;
        var a = left.RoleHex ?? [];
        var b = right.RoleHex ?? [];
        if (a.Count != b.Count) return false;
        foreach (var pair in a)
            if (!b.TryGetValue(pair.Key, out string? hex) || !string.Equals(pair.Value, hex, StringComparison.OrdinalIgnoreCase))
                return false;
        if (!string.Equals(left.CardTint, right.CardTint, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(left.CardTintEnd, right.CardTintEnd, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(left.CardGradient, right.CardGradient, StringComparison.OrdinalIgnoreCase)) return false;
        return left.CardSaturation == right.CardSaturation;
    }
}
