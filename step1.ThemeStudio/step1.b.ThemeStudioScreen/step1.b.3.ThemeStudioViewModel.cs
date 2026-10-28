using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Markup;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using ProGlassAutomation.Theming;
using ProGlassAutomation.ThemeBento;
using ProGlassAutomation.ThemeClaymorphism;
using ProGlassAutomation.Customization;

namespace ProGlassAutomation.ThemeStudio;

public sealed partial class ThemeStudioViewModel : INotifyPropertyChanged, IDataErrorInfo, IDisposable
{
    private readonly ThemeService themes;
    private string sampleName = "Bento review";
    private string tokenFilter = "";
    private string feedback = "Ready to explore. Changes here are preview-only.";
    private bool showHelpers = true;
    private int progress = 50;
    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? DialogRequested;
    public ObservableCollection<TokenRow> Tokens { get; } = [];
    public ICollectionView TokenView { get; }
    public IReadOnlyList<string> InstalledThemes => ThemeService.InstalledThemes;
    public string SelectedTheme
    {
        get => themes.ActiveTheme;
        set { if (value is not null) SelectAppearance(value, themes.ActiveAppearance); }
    }
    public string AppearanceLabel => themes.ActiveAppearance.ToString();
    public bool IsLight { get => themes.ActiveAppearance == Appearance.Light; set { if (value) SelectAppearance(SelectedTheme, Appearance.Light); } }
    public bool IsDark { get => themes.ActiveAppearance == Appearance.Dark; set { if (value) SelectAppearance(SelectedTheme, Appearance.Dark); } }
    public int TokenCount => Tokens.Count;
    public string ThemeError { get; private set; } = "";
    public string SampleName
    {
        get => sampleName;
        set { sampleName = value; Changed(nameof(SampleName)); Changed(nameof(NameError)); CommandManager.InvalidateRequerySuggested(); }
    }
    public string NameError => sampleName.Trim().Length < 3 ? "Enter at least 3 characters to apply the preview." : "";
    public bool ShowHelpers { get => showHelpers; set { showHelpers = value; Changed(nameof(ShowHelpers)); } }
    public string Feedback { get => feedback; internal set { feedback = value; Changed(nameof(Feedback)); } }
    public int Progress { get => progress; private set { progress = value; Changed(nameof(Progress)); } }
    public string TokenFilter { get => tokenFilter; set { tokenFilter = value; TokenView.Refresh(); Changed(nameof(TokenFilter)); } }
    public ICommand QuickApplyPreview { get; }
    public ICommand ResetPreview { get; }
    public ICommand AdvanceProgress { get; }
    public ICommand OpenDialog { get; }
    public string Error => NameError;
    public string this[string columnName] => columnName == nameof(SampleName) ? NameError : "";

    public ThemeStudioViewModel(ThemeService themes)
    {
        this.themes = themes;
        TokenView = CollectionViewSource.GetDefaultView(Tokens);
        TokenView.Filter = value => value is TokenRow row && row.Name.Contains(tokenFilter, StringComparison.OrdinalIgnoreCase);
        QuickApplyPreview = new DelegateCommand(() => Feedback = $"Preview applied: {SampleName.Trim()} · {AppearanceLabel}", () => NameError.Length == 0);
        ResetPreview = new DelegateCommand(() => { SampleName = "Bento review"; ShowHelpers = true; Progress = 50; Feedback = "Preview reset. Appearance unchanged."; });
        AdvanceProgress = new DelegateCommand(() => Progress = Progress >= 100 ? 0 : Progress + 25);
        OpenDialog = new DelegateCommand(() => DialogRequested?.Invoke(this, EventArgs.Empty));
        themes.PropertyChanged += ThemeChanged;
        InitializeCustomization();
        InitializeReferenceSamples();
        RefreshTokens();
    }

    private void SelectAppearance(string theme, Appearance appearance)
    {
        var current = extended;
        try
        {
            // Pages must ride along here too: an appearance/theme switch rebuilds
            // the whole package, so a pinned page size would drop without it.
            themes.Apply(theme, appearance, SharedGeometry.Compose(theme, current, clay, geometry, neumorph, font, colors, liquid, pages));
            ThemeError = "";
            lastSaved = lastSaved with { Theme = theme, Appearance = appearance, Geometry = geometry };
            try { CustomizationStore.Save(lastSaved); } catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { }
        }
        catch (ThemeNotificationException error) { ThemeError = error.Message; }
        catch (Exception error) when (error is IOException or XamlParseException or InvalidOperationException or ArgumentException)
        {
            ThemeError = $"Appearance unchanged: {error.Message}";
            foreach (string name in new[] { nameof(IsLight), nameof(IsDark), nameof(SelectedTheme) }) Changed(name);
        }
        Changed(nameof(ThemeError));
    }

    private void ThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        // ThemeService.Apply raises ActiveAppearance first and ActiveTheme after it,
        // for one package. Everything package-bound is refreshed once, here. The old
        // handler did all of it for both names, so every switch rebuilt the 164-row
        // token list twice and republished ~45 properties twice for the same package.
        if (e.PropertyName == nameof(ThemeService.ActiveAppearance))
        {
            RefreshTokens();
            colorRoleHexDraft = ColorPlateService.ResolvedHex(colors, themes.ActiveAppearance, selectedColorRole);
            foreach (string property in new[] { nameof(IsLight), nameof(IsDark), nameof(AppearanceLabel), nameof(SelectedTheme), nameof(IsClay), nameof(IsFluent), nameof(IsNeumorph), nameof(IsLiquid), nameof(IsGlass), nameof(IsSpatial), nameof(IsSwiss), nameof(IsHolographic), nameof(CanCustomizeBento), nameof(CanCustomizeClay), nameof(CanCustomizeNeumorph), nameof(CanCustomizeLiquid), nameof(CanCustomizeGeometry), nameof(CanCustomize), nameof(DesignSummary), nameof(SelectedRounding), nameof(SelectedFontFamily), nameof(HeadingBold), nameof(BodyItalic), nameof(BodyUnderline), nameof(SelectedIconStyle), nameof(SelectedIconCaption), nameof(SelectedColorPlate), nameof(ColorStopSummary), nameof(ColorRoleSummary), nameof(ColorRoleHex), nameof(FontFamilyChoices), nameof(IsDirty) }) Changed(property);
            CommandManager.InvalidateRequerySuggested();
            return;
        }
        if (e.PropertyName != nameof(ThemeService.ActiveTheme)) return;
        // The package was handled by the appearance notification above; a theme
        // change only has to republish what names the theme itself.
        foreach (string property in new[] { nameof(SelectedTheme), nameof(AppearanceLabel), nameof(IsClay), nameof(IsFluent), nameof(IsNeumorph), nameof(IsLiquid), nameof(IsGlass), nameof(IsSpatial), nameof(IsSwiss), nameof(IsHolographic), nameof(CanCustomizeBento), nameof(CanCustomizeClay), nameof(CanCustomizeNeumorph), nameof(CanCustomizeLiquid), nameof(CanCustomizeGeometry), nameof(CanCustomize), nameof(DesignSummary), nameof(ThemeError), nameof(IsDirty) }) Changed(property);
    }

    private void RefreshTokens()
    {
        // The contract key set never changes, only the values. Rebuilding the rows
        // on every switch threw away and re-created every container in the token
        // list, so rows are replaced in place, and only when the value changed.
        var keys = ThemeContract.Required.Keys.OrderBy(key => key).ToList();
        if (Tokens.Count != keys.Count)
        {
            Tokens.Clear();
            foreach (string key in keys) Tokens.Add(TokenRow.From(key, themes.GetToken(key)));
            Changed(nameof(TokenCount));
            return;
        }
        for (int index = 0; index < keys.Count; index++)
        {
            if (Tokens[index].Name == keys[index]) continue;
            Tokens.Clear();
            foreach (string key in keys) Tokens.Add(TokenRow.From(key, themes.GetToken(key)));
            Changed(nameof(TokenCount));
            return;
        }
        for (int index = 0; index < keys.Count; index++)
        {
            var row = TokenRow.From(keys[index], themes.GetToken(keys[index]));
            if (Tokens[index] != row) Tokens[index] = row;
        }
        Changed(nameof(TokenCount));
    }

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void Dispose() => themes.PropertyChanged -= ThemeChanged;
}
