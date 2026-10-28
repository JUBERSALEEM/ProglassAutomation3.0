using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProGlassAutomation.Theming;
using ProGlassAutomation.ThemeClaymorphism;
using ProGlassAutomation.ThemeLiquidGlass;
using ProGlassAutomation.ThemeNeumorphism;
using ProGlassAutomation.Diagnostics;

namespace ProGlassAutomation.Customization;

public static class CustomizationStore
{
    public const int MaxBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProGlassAutomation", "bento-customization.json");

    public static string? LoadWarning { get; private set; }

    public static SavedCustomization Load()
    {
        LoadWarning = null;
        try
        {
            if (!File.Exists(FilePath)) return Defaults();
            return ReadFile(FilePath);
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or InvalidOperationException)
        {
            LoadWarning = "Saved customization could not be loaded. Defaults applied. " + ex.Message;
            return Defaults();
        }
    }

    public static SavedCustomization ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Theme file not found.", path);
        if (info.Length > MaxBytes) throw new ArgumentException("Theme files must be at most 64 KB.");
        var json = File.ReadAllText(path);
        if (LooksLikeMarkup(json))
            throw new ArgumentException("Theme files must be JSON customization data, not XAML or markup.");
        if (!HasVersion2(json))
            throw new ArgumentException("Unsupported or empty ProGlass theme file.");
        var loaded = JsonSerializer.Deserialize<SavedCustomization>(json, Options);
        if (loaded is null || loaded.Version != 2) throw new ArgumentException("Unsupported or empty ProGlass theme file.");
        if (loaded.Preferences is null || loaded.Preferences.Validate() is not null) throw new ArgumentException("Theme file contains invalid Bento preferences.");
        if (loaded.Clay?.Validate() is not null) throw new ArgumentException("Theme file contains invalid Clay preferences.");
        if (loaded.Neumorph?.Validate() is not null) throw new ArgumentException("Theme file contains invalid Neumorph preferences.");
        if (loaded.Liquid?.Validate() is not null) throw new ArgumentException("Theme file contains invalid Liquid Glass preferences.");
        if (!Enum.IsDefined(loaded.Appearance)) throw new ArgumentException("Theme file appearance is not supported.");
        if (string.IsNullOrWhiteSpace(loaded.Theme) || !ThemeService.InstalledThemes.Contains(loaded.Theme))
            loaded = loaded with { Theme = ThemeCatalog.BentoName };
        loaded = loaded with
        {
            Clay = loaded.Clay ?? new ClayCustomizationState(),
            Geometry = loaded.Geometry ?? new SharedGeometryState(),
            Neumorph = loaded.Neumorph ?? new NeumorphCustomizationState(),
            Font = loaded.Font ?? new FontCustomizationState(),
            Colors = loaded.Colors ?? new ColorPlateState(),
            Liquid = loaded.Liquid ?? new LiquidCustomizationState(),
            Icons = loaded.Icons ?? new IconCustomizationState()
        };
        if (loaded.Geometry.Validate() is not null) throw new ArgumentException("Theme file contains invalid card rounding.");
        if (loaded.Font?.Validate() is not null) throw new ArgumentException("Theme file contains invalid font preferences.");
        if (loaded.Colors?.Validate() is not null) throw new ArgumentException("Theme file contains invalid color plate preferences.");
        if (loaded.Icons?.Validate() is not null) throw new ArgumentException("Theme file contains invalid icon preferences.");
        if (loaded.Pages?.Validate() is not null) throw new ArgumentException("Theme file contains invalid page size preferences.");
        foreach (var preset in loaded.CustomPresets ?? [])
            if (string.IsNullOrWhiteSpace(preset.Name) || preset.State.Validate() is not null)
                throw new ArgumentException("Theme file contains an invalid Bento preset.");
        foreach (var preset in loaded.ClayPresets ?? [])
            if (string.IsNullOrWhiteSpace(preset.Name) || preset.State.Validate() is not null)
                throw new ArgumentException("Theme file contains an invalid Clay preset.");
        foreach (var preset in loaded.NeumorphPresets ?? [])
            if (string.IsNullOrWhiteSpace(preset.Name) || preset.State.Validate() is not null)
                throw new ArgumentException("Theme file contains an invalid Neumorph preset.");
        foreach (var preset in loaded.LiquidPresets ?? [])
            if (string.IsNullOrWhiteSpace(preset.Name) || preset.State.Validate() is not null)
                throw new ArgumentException("Theme file contains an invalid Liquid Glass preset.");
        return loaded;
    }

    public static void Save(SavedCustomization state) => WriteFile(state, FilePath);

    public static void WriteFile(SavedCustomization state, string path)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (state.Preferences.Validate() is not null) throw new ArgumentException("Cannot persist invalid preferences.");
        if (state.Clay?.Validate() is not null) throw new ArgumentException("Cannot persist invalid clay preferences.");
        if (state.Neumorph?.Validate() is not null) throw new ArgumentException("Cannot persist invalid neumorph preferences.");
        if ((state.Geometry ?? new SharedGeometryState()).Validate() is not null) throw new ArgumentException("Cannot persist invalid card rounding.");
        if ((state.Font ?? new FontCustomizationState()).Validate() is not null) throw new ArgumentException("Cannot persist invalid font preferences.");
        if ((state.Colors ?? new ColorPlateState()).Validate() is not null) throw new ArgumentException("Cannot persist invalid color plate preferences.");
        if ((state.Icons ?? new IconCustomizationState()).Validate() is not null) throw new ArgumentException("Cannot persist invalid icon preferences.");
        if ((state.Pages ?? new PageCustomizationState()).Validate() is not null) throw new ArgumentException("Cannot persist invalid page size preferences.");
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(dir);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(state with
            {
                Version = 2,
                Geometry = state.Geometry ?? new SharedGeometryState(),
                Font = state.Font ?? new FontCustomizationState(),
                Colors = state.Colors ?? new ColorPlateState(),
                Icons = state.Icons ?? new IconCustomizationState()
            }, Options));
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static bool LooksLikeMarkup(string text)
    {
        var start = text.AsSpan().TrimStart();
        return start.Length > 0 && start[0] == '<';
    }

    /// <summary>
    /// Record constructor defaults set Version=2 and a valid Preferences instance, so <c>{}</c>
    /// deserializes as a complete package. Import must see an explicit v2 Version in the JSON.
    /// WriteFile always emits Version; existing v2 saves are unchanged.
    /// </summary>
    private static bool HasVersion2(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!string.Equals(property.Name, "Version", StringComparison.OrdinalIgnoreCase)) continue;
            return property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out int version) && version == 2;
        }
        return false;
    }

    public static SavedCustomization Defaults() => new();

    public static void ClearCorrupt()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file stays, so the same fallback warning will appear on every
            // launch. That is worth recording once per attempt, otherwise the
            // owner sees a repeated message and no reason for it.
            AppLog.Error("theme.customization.clear-corrupt", ex,
                "the unreadable customisation file could not be removed and will be re-read next launch");
        }
    }
}
