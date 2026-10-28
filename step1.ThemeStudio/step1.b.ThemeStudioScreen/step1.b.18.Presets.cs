using System.Collections.Generic;
using ProGlassAutomation.Customization;
using ProGlassAutomation.ThemeBento;
using ProGlassAutomation.ThemeClaymorphism;
using ProGlassAutomation.ThemeLiquidGlass;
using ProGlassAutomation.ThemeNeumorphism;

namespace ProGlassAutomation.ThemeStudio;

/// <summary>
/// The preset half of the studio view model: every family's catalogue, the picker
/// that applies a pick, and the commands that save, delete and rename a preset.
/// Split out of step1.b.17 when round 36 added the pickers — a grandfathered file
/// may be split, never grown (step1.d.19).
/// </summary>
public sealed partial class ThemeStudioViewModel
{
    /// <summary>
    /// A preset pick must apply even when the picker already shows that name. An
    /// appearance switch or a slider edit leaves the live state away from the preset
    /// while the picker keeps its name, and an equality guard then made the pick do
    /// nothing — the owner's report that Night Berry would not select. Nothing is
    /// applied only when the name is the same and the state it names is still live.
    /// </summary>
    private static bool PresetPickNeedsApply(string name, string shownName, object live, object? presetState) =>
        name != shownName || (presetState is not null && !Equals(live, presetState));

    public IReadOnlyList<NeumorphNamedPreset> NeumorphBuiltInPresets => NeumorphCustomization.BuiltIn;
    public IReadOnlyList<NeumorphNamedPreset> NeumorphCustomPresets => lastSaved.NeumorphPresets ?? [];
    public IReadOnlyList<string> NeumorphPresetNames => NeumorphCustomization.BuiltIn.Select(p => p.Name).Concat((lastSaved.NeumorphPresets ?? []).Select(p => p.Name)).ToList();
    private string selectedNeumorphPresetName = NeumorphCustomization.BuiltIn[0].Name;
    public string SelectedNeumorphPresetName
    {
        get => selectedNeumorphPresetName;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var wanted = NeumorphCustomization.BuiltIn.Concat(lastSaved.NeumorphPresets ?? []).FirstOrDefault(p => p.Name == value);
            bool apply = PresetPickNeedsApply(value, selectedNeumorphPresetName, neumorph, wanted?.State);
            selectedNeumorphPresetName = value;
            Changed(nameof(SelectedNeumorphPresetName));
            if (apply) ApplyPreset.Execute(value);
        }
    }
    public IReadOnlyList<LiquidNamedPreset> LiquidBuiltInPresets => LiquidCustomization.BuiltIn;
    public IReadOnlyList<LiquidNamedPreset> LiquidCustomPresets => lastSaved.LiquidPresets ?? [];
    public IReadOnlyList<string> LiquidPresetNames => LiquidCustomization.BuiltIn.Select(p => p.Name).Concat((lastSaved.LiquidPresets ?? []).Select(p => p.Name)).ToList();
    private string selectedLiquidPresetName = LiquidCustomization.BuiltIn[0].Name;
    public string SelectedLiquidPresetName
    {
        get => selectedLiquidPresetName;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var wanted = LiquidCustomization.BuiltIn.Concat(lastSaved.LiquidPresets ?? []).FirstOrDefault(p => p.Name == value);
            bool apply = PresetPickNeedsApply(value, selectedLiquidPresetName, liquid, wanted?.State);
            selectedLiquidPresetName = value;
            Changed(nameof(SelectedLiquidPresetName));
            if (apply) ApplyPreset.Execute(value);
        }
    }
    public IReadOnlyList<ClayNamedPreset> ClayBuiltInPresets => ClayCustomization.BuiltIn;
    public IReadOnlyList<ClayNamedPreset> ClayCustomPresets => lastSaved.ClayPresets ?? [];
    public IReadOnlyList<string> ClayPresetNames => ClayCustomization.BuiltIn.Select(p => p.Name).Concat((lastSaved.ClayPresets ?? []).Select(p => p.Name)).ToList();
    private string selectedClayPresetName = ClayCustomization.BuiltIn[0].Name;
    public string SelectedClayPresetName
    {
        get => selectedClayPresetName;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var wanted = ClayCustomization.BuiltIn.Concat(lastSaved.ClayPresets ?? []).FirstOrDefault(p => p.Name == value);
            bool apply = PresetPickNeedsApply(value, selectedClayPresetName, clay, wanted?.State);
            selectedClayPresetName = value;
            Changed(nameof(SelectedClayPresetName));
            if (apply) ApplyPreset.Execute(value);
        }
    }
    public IReadOnlyList<NamedPreset> BuiltInPresets => PresetCatalog.BuiltIn;
    private string selectedPresetName = PresetCatalog.BuiltIn[0].Name;
    /// <summary>Bound by both Bento preset lists; Bento had no picker at all, so its
    /// built-in and saved presets could only ever be displayed.</summary>
    public string SelectedPresetName
    {
        get => selectedPresetName;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var wanted = BuiltInPresets.Concat(CustomPresets).FirstOrDefault(p => p.Name == value);
            bool apply = PresetPickNeedsApply(value, selectedPresetName, extended, wanted?.State);
            selectedPresetName = value;
            Changed(nameof(SelectedPresetName));
            if (apply) ApplyPreset.Execute(value);
        }
    }
    public IReadOnlyList<NamedPreset> CustomPresets => lastSaved.CustomPresets ?? [];
    private string presetNameInput = "";
    public string PresetNameInput { get => presetNameInput; set { presetNameInput = value; Changed(nameof(PresetNameInput)); } }

    /// <summary>
    /// Created from InitializeCustomization, where the other commands are created.
    /// </summary>
    private void CreatePresetCommands()
    {
        ApplyPreset = new DelegateCommand<string>(name =>
        {
            // Round 36: a preset that also changes mode used to rebuild the whole
            // package twice — once for the appearance switch and once for the state.
            // The state now goes into place first, so the single appearance switch
            // that follows composes it: one rebuild, not two. A switch that fails
            // leaves the previous state in place, so the two never disagree.
            if (IsClay)
            {
                var clayPreset = ClayCustomization.BuiltIn.Concat(lastSaved.ClayPresets ?? []).FirstOrDefault(p => p.Name == name);
                if (clayPreset == null) return;
                var previous = clay;
                if (clayPreset.Appearance != themes.ActiveAppearance)
                {
                    clay = clayPreset.State;
                    SelectAppearance(ThemeService.Clay, clayPreset.Appearance);
                    if (themes.ActiveTheme == ThemeService.Clay && themes.ActiveAppearance == clayPreset.Appearance) PublishClayState();
                    else clay = previous;
                }
                else ApplyClayState(clayPreset.State);
                Feedback = $"Clay preset applied: {clayPreset.Name}";
                return;
            }
            if (IsNeumorph)
            {
                var neuPreset = NeumorphCustomization.BuiltIn.Concat(lastSaved.NeumorphPresets ?? []).FirstOrDefault(p => p.Name == name);
                if (neuPreset == null) return;
                var previous = neumorph;
                if (neuPreset.Appearance != themes.ActiveAppearance)
                {
                    neumorph = neuPreset.State;
                    SelectAppearance(ThemeService.Neumorphism, neuPreset.Appearance);
                    if (themes.ActiveTheme == ThemeService.Neumorphism && themes.ActiveAppearance == neuPreset.Appearance) PublishNeumorphState();
                    else neumorph = previous;
                }
                else ApplyNeumorphState(neuPreset.State);
                Feedback = $"Neumorph preset applied: {neuPreset.Name}";
                return;
            }
            if (IsLiquid)
            {
                var glassPreset = LiquidCustomization.BuiltIn.Concat(lastSaved.LiquidPresets ?? []).FirstOrDefault(p => p.Name == name);
                if (glassPreset == null) return;
                var previous = liquid;
                if (glassPreset.Appearance != themes.ActiveAppearance)
                {
                    liquid = glassPreset.State;
                    SelectAppearance(ThemeService.LiquidGlass, glassPreset.Appearance);
                    if (themes.ActiveTheme == ThemeService.LiquidGlass && themes.ActiveAppearance == glassPreset.Appearance) PublishLiquidState();
                    else liquid = previous;
                }
                else ApplyLiquidState(glassPreset.State);
                Feedback = $"Liquid Glass preset applied: {glassPreset.Name}";
                return;
            }
            var preset = BuiltInPresets.Concat(CustomPresets).FirstOrDefault(p => p.Name == name);
            if (preset != null)
            {
                var previous = extended;
                if (preset.Appearance != themes.ActiveAppearance)
                {
                    extended = preset.State;
                    SelectAppearance(ThemeService.Bento, preset.Appearance);
                    if (themes.ActiveTheme == ThemeService.Bento && themes.ActiveAppearance == preset.Appearance) { preferences = preset.State.ToLegacy(); PublishBentoState(); }
                    else extended = previous;
                }
                else ApplyState(preset.State);
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
}
