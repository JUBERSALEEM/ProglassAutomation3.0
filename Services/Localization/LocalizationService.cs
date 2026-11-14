// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/LocalizationService.cs
// PROJECT: ProGlassAutomation
// MODULE: Global language & currency (2026-09-01)
//
// THE SWITCH ENGINE
//   Each language ships as Themes/Languages/{Code}.xaml — a plain
//   ResourceDictionary of <sys:String x:Key="Shell.Title">…</sys:String>.
//   The service merges the ACTIVE dictionary into the application scope
//   (same pattern the theme engine uses for brushes), so every XAML text
//   bound with {DynamicResource Shell.Title} retranslates LIVE the moment
//   the user picks a language — no restart, no view reload.
//
//   • en-US is ALWAYS the fallback: a key missing from the active
//     dictionary (or from the active file entirely) resolves from
//     en-US.xaml, so partial translations never leave a blank label.
//   • RTL languages (Arabic, Urdu) flip the shell FlowDirection via the
//     published "AppFlowDirection" string resource and
//     Window.FlowDirection on the root window.
//   • Language is INDEPENDENT of currency (English UI + INR amounts,
//     Chinese UI + AED amounts, …).
//
//   UI threads only: ApplyLanguage manipulates Application resources.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.AI.AIConfig;
using ProGlassAutomation.Services.Logging;
using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ProGlassAutomation.Services.Localization
{
    public sealed class LocalizationService : INotifyPropertyChanged
    {
        public static LocalizationService Instance { get; } = new();

        private ResourceDictionary? _active;
        private ResourceDictionary? _english;
        private LanguageInfo _current = LanguageCatalog.Primary;
        private bool _userPicked;

        /// <summary>Raised after a language switch completes.</summary>
        public event EventHandler? LanguageChanged;

        private LocalizationService() { }

        public LanguageInfo CurrentLanguage
        {
            get => _current;
            private set
            {
                if (_current.Code == value.Code) return;
                _current = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentLanguageName));
                OnPropertyChanged(nameof(CurrentCulture));
            }
        }

        public string CurrentLanguageName =>
            LanguageCatalog.Display(CurrentLanguage);

        public CultureInfo CurrentCulture =>
            CultureInfo.GetCultureInfo(CurrentLanguage.Code);

        public bool IsRtl => CurrentLanguage.IsRtl;

        /// <summary>App-level config was loaded — apply or suggest.</summary>
        public void Initialize(LocalizationSettings? settings)
        {
            settings ??= new LocalizationSettings();

            string requested = settings.Language ?? LanguageCatalog.Primary.Code;
            if (LanguageCatalog.Find(requested) == null)
                requested = LanguageCatalog.Primary.Code;

            // ENGLISH IS THE LAUNCH DEFAULT (2026-09-02).
            //
            // This block used to SILENTLY switch the whole app to the
            // region's language before the first frame -- a user in the UAE
            // opened the app and found it in Arabic without ever being
            // asked, and had to hunt through Settings to get back.
            //
            // The suggestion is now an OFFER, not an override: startup
            // always honours the saved/primary language, and
            // GetPendingRegionalSuggestion() lets the shell ask once, after
            // the UI is up. A saved choice (settings.Language) is used as-is
            // and never second-guessed.
            ApplyLanguage(requested, persist: false);
        }

        /// <summary>Switch the whole app to a shipped language.</summary>
        public void ApplyLanguage(string code, bool persist = true)
        {
            var lang = LanguageCatalog.Find(code) ?? LanguageCatalog.Primary;
            if (lang.Code == _current.Code && _active != null) return;

            if (persist) _userPicked = true;

            try
            {
                var resources = Application.Current?.Resources;
                if (resources == null) return;

                // App.xaml pre-merges the primary language (design-time
                // resolution of {DynamicResource lang.key}), so ALWAYS look
                // for an already-merged copy first — never stack a
                // duplicate dictionary of the same source.
                var next = FindMergedBySource(resources, lang.Code) ?? LoadDictionary(lang.Code);
                if (next == null) return;

                // Replace the previously-merged language dictionary —
                // except when it IS the English fallback (that one stays).
                if (_active != null && !ReferenceEquals(_active, next)
                    && !ReferenceEquals(_active, _english)
                    && resources.MergedDictionaries.Contains(_active))
                    resources.MergedDictionaries.Remove(_active);

                // English always stays merged as the fallback (last = wins,
                // so the ACTIVE language must be merged AFTER English).
                if (_english == null)
                {
                    _english = FindMergedBySource(resources, LanguageCatalog.Primary.Code)
                               ?? LoadDictionary(LanguageCatalog.Primary.Code);
                    if (_english != null && !resources.MergedDictionaries.Contains(_english))
                        resources.MergedDictionaries.Add(_english);
                }
                // Move (or add) the active dictionary to the TOP of the
                // merge order — last merged wins on key collisions.
                if (resources.MergedDictionaries.Contains(next))
                    resources.MergedDictionaries.Remove(next);
                resources.MergedDictionaries.Add(next);
                _active = next;

                CurrentLanguage = lang;
                ApplyFlowDirection(resources, lang.IsRtl);

                OnLanguageChanged();

                if (persist && App.Config != null)
                {
                    App.Config.Localization.Language = lang.Code;
                    ConfigService.Save(App.Config);
                }
            }
            catch (Exception ex)
            {
                AppLogService.Error($"Language switch to {code} failed: {ex.Message}", ex,
                    new LogContext { Module = "Localization", Operation = "ApplyLanguage" });
            }
        }

        /// <summary>Translate a key using the active dictionary, falling
        /// back to English. Throws nothing — returns the key itself when
        /// unknown so a missing translation is visible in QA.</summary>
        public string T(string key, string? languageCode = null)
        {
            if (string.IsNullOrWhiteSpace(key)) return "";

            ResourceDictionary? dict = languageCode == null
                ? _active
                : LoadDictionary(languageCode);
            if (dict != null && dict.Contains(key) && dict[key] is string s && s.Length > 0)
                return s;

            if (_english != null && _english.Contains(key) && _english[key] is string es)
                return es;

            return key;
        }

        /// <summary>Indexer for {Binding [Key], Source=…} patterns.</summary>
        public string this[string key] => T(key);

        /// <summary>
        /// Applies the regional language suggestion (India → हिन्दी,
        /// China → 中文, UAE → العربية) when:
        ///   • the user never picked a language manually, AND
        ///   • the auto-suggest setting is on.
        /// Called when GeoService resolves the country (startup cache,
        /// IP lookup or OS region). English remains the primary default —
        /// this only upgrades the SECOND option once, then the user owns
        /// the choice.
        /// </summary>
        public void ApplyRegionalSuggestion(string countryIso)
        {
            // NO LONGER SWITCHES THE LANGUAGE (2026-09-02).
            //
            // This fired when GeoService resolved the country -- typically a
            // second or two AFTER the window was already on screen -- and
            // called ApplyLanguage(persist: true). The entire UI would flip
            // to Arabic/Hindi/Chinese mid-session, with the change written
            // to config, without the user ever being asked.
            //
            // Together with the startup block in Initialize(), that was two
            // separate silent overrides. English is now the launch default
            // and the regional language is OFFERED once, by the shell, via
            // GetPendingRegionalSuggestion().
            //
            // All this does now is record the detected country so the offer
            // can be made. Never switches, never persists a language.
            if (string.IsNullOrWhiteSpace(countryIso)) return;

            try
            {
                var settings = App.Config?.Localization;
                if (settings == null) return;

                if (string.Equals(settings.LastKnownCountryIso, countryIso,
                        StringComparison.OrdinalIgnoreCase))
                    return;

                settings.LastKnownCountryIso = countryIso;
                ConfigService.Save(App.Config!);
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Debug(
                    $"[Localization] country record failed: {ex.Message}");
            }
        }

        /// <summary>True while the current language is the automatically
        /// suggested one (user has not overridden it).</summary>
        public bool IsUserPicked => _userPicked;

        /// <summary>
        /// Returns the language to OFFER the user on first launch, or null
        /// when no prompt should be shown.
        ///
        /// Null (stay silent) when any of these hold:
        ///   • the prompt has already been shown once — asked and answered,
        ///     regardless of which way, so it is never asked again;
        ///   • regional suggestion is switched off in settings;
        ///   • the user already picked a language this session;
        ///   • the saved language is not the primary one, i.e. a previous
        ///     choice is already in force;
        ///   • no country is known yet, or the region maps to English.
        ///
        /// This only READS state. The caller decides whether to prompt, and
        /// is responsible for setting LanguagePromptShown so the question
        /// is not repeated.
        /// </summary>
        public LanguageInfo? GetPendingRegionalSuggestion(
            LocalizationSettings? settings)
        {
            if (settings == null) return null;
            if (settings.LanguagePromptShown) return null;
            if (!settings.AutoSuggestByRegion) return null;
            if (_userPicked) return null;

            // A saved non-English language means the user is already set up.
            var saved = LanguageCatalog.Find(settings.Language ?? "");
            if (saved != null && saved.Code != LanguageCatalog.Primary.Code)
                return null;

            if (string.IsNullOrWhiteSpace(settings.LastKnownCountryIso))
                return null;

            // Suggest() is non-nullable: it falls back to Primary when the
            // country is unknown, so the Primary check below is the real
            // "nothing to offer" test.
            var suggested = LanguageCatalog.Suggest(settings.LastKnownCountryIso);
            if (suggested.Code == LanguageCatalog.Primary.Code) return null;

            return suggested;
        }

        /// <summary>Find an already-merged language dictionary by its source
        /// path (App.xaml pre-merges the primary language so design surfaces
        /// resolve language keys). Returns null when not merged yet.</summary>
        private static ResourceDictionary? FindMergedBySource(ResourceDictionary resources, string code)
        {
            var tail = $"Themes/Languages/{code}.xaml";
            foreach (var dict in resources.MergedDictionaries)
            {
                var src = dict.Source?.OriginalString;
                if (src != null && src.Replace('\\', '/')
                        .EndsWith(tail, StringComparison.OrdinalIgnoreCase))
                    return dict;
            }
            return null;
        }

        private static ResourceDictionary? LoadDictionary(string code)
        {
            var lang = LanguageCatalog.Find(code) ?? LanguageCatalog.Primary;
            try
            {
                var dict = new ResourceDictionary
                {
                    Source = new Uri($"Themes/Languages/{lang.Code}.xaml",
                        UriKind.Relative)
                };
                return dict;
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    $"Language resource dict {code} failed to load: {ex.Message}",
                    new LogContext { Module = "Localization", Operation = "LoadDictionary" });
                return null;
            }
        }

        private static void ApplyFlowDirection(ResourceDictionary resources, bool isRtl)
        {
            // Publish the direction token so views can bind to it where a
            // FlowDirection inheritance is not enough (e.g. Icon columns).
            resources["AppFlowDirection"] = isRtl ? "RightToLeft" : "LeftToRight";

            if (Application.Current?.MainWindow is Window root)
                root.FlowDirection = isRtl
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight;
        }

        private void OnLanguageChanged()
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
