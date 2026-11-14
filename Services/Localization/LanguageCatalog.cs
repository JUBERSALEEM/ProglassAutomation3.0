// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/LanguageCatalog.cs
// PROJECT: ProGlassAutomation
// MODULE: Global language & currency (2026-09-01)
//
// The supported-language table for the system-wide language switch.
//   • en-US is ALWAYS the primary/default language, in any country.
//   • The location hint from GeoService (country IP / OS region) picks a
//     SUGGESTED language (e.g. India → hi-IN, China → zh-CN) — the user
//     is offered it, never forced.
//   • Language and currency are independent: the user can run the app in
//     English while displaying INR, or Chinese with AED, etc.
//   • FlowDirection comes from the language (Arabic/Urdu/Hebrew → RTL) so
//     the whole shell mirrors when the user picks an RTL language.
// ═══════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Text;

namespace ProGlassAutomation.Services.Localization
{
    /// <summary>One supported language with its locale metadata.</summary>
    public sealed class LanguageInfo
    {
        /// <summary>BCP-47 code, e.g. "en-US", "hi-IN". File name of the
        /// resource dictionary Themes/Languages/{Code}.xaml.</summary>
        public string Code { get; init; } = "";

        /// <summary>English name shown in the picker (self-describing).</summary>
        public string EnglishName { get; init; } = "";

        /// <summary>Name in its own language (picker).</summary>
        public string NativeName { get; init; } = "";

        /// <summary>Right-to-left layout (Arabic script languages).</summary>
        public bool IsRtl { get; init; }

        /// <summary>ISO country whose region is most associated with this
        /// language — used for the "suggest" ranking.</summary>
        public string PrimaryRegionIso { get; init; } = "";

        /// <summary>Currency most used in that region.</summary>
        public string DefaultCurrency { get; init; } = "AED";

        /// <summary>Default international dial code for phone inputs
        /// (+971, +91, ...).</summary>
        public string DialCode { get; init; } = "+971";

        /// <summary>Picker text ("English — English", "Hindi — हिन्दी").</summary>
        public string DisplayName => LanguageCatalog.Display(this);

        /// <summary>Never leak the raw type name into a picker if a
        /// binding ever fails — always show the friendly label.</summary>
        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Static catalog of the 15 shipped languages. en-US is always first
    /// (primary). The rest are ordered by population reach.
    /// </summary>
    public static class LanguageCatalog
    {
        public static LanguageInfo Primary => All[0];

        public static readonly IReadOnlyList<LanguageInfo> All =
        [
            new() { Code = "en-US", EnglishName = "English",      NativeName = "English",      IsRtl = false, PrimaryRegionIso = "US", DefaultCurrency = "USD", DialCode = "+1"   },
            new() { Code = "ar-AE", EnglishName = "Arabic",       NativeName = "العربية",       IsRtl = true,  PrimaryRegionIso = "AE", DefaultCurrency = "AED", DialCode = "+971" },
            new() { Code = "zh-CN", EnglishName = "Chinese",      NativeName = "中文",          IsRtl = false, PrimaryRegionIso = "CN", DefaultCurrency = "CNY", DialCode = "+86"  },
            new() { Code = "hi-IN", EnglishName = "Hindi",        NativeName = "हिन्दी",          IsRtl = false, PrimaryRegionIso = "IN", DefaultCurrency = "INR", DialCode = "+91"  },
            new() { Code = "es-ES", EnglishName = "Spanish",      NativeName = "Español",       IsRtl = false, PrimaryRegionIso = "ES", DefaultCurrency = "EUR", DialCode = "+34"  },
            new() { Code = "fr-FR", EnglishName = "French",       NativeName = "Français",      IsRtl = false, PrimaryRegionIso = "FR", DefaultCurrency = "EUR", DialCode = "+33"  },
            new() { Code = "de-DE", EnglishName = "German",       NativeName = "Deutsch",       IsRtl = false, PrimaryRegionIso = "DE", DefaultCurrency = "EUR", DialCode = "+49"  },
            new() { Code = "ru-RU", EnglishName = "Russian",      NativeName = "Русский",       IsRtl = false, PrimaryRegionIso = "RU", DefaultCurrency = "RUB", DialCode = "+7"   },
            new() { Code = "pt-BR", EnglishName = "Portuguese",   NativeName = "Português",     IsRtl = false, PrimaryRegionIso = "BR", DefaultCurrency = "BRL", DialCode = "+55"  },
            new() { Code = "tr-TR", EnglishName = "Turkish",      NativeName = "Türkçe",        IsRtl = false, PrimaryRegionIso = "TR", DefaultCurrency = "TRY", DialCode = "+90"  },
            new() { Code = "ur-PK", EnglishName = "Urdu",         NativeName = "اردو",           IsRtl = true,  PrimaryRegionIso = "PK", DefaultCurrency = "PKR", DialCode = "+92"  },
            new() { Code = "bn-BD", EnglishName = "Bengali",      NativeName = "বাংলা",           IsRtl = false, PrimaryRegionIso = "BD", DefaultCurrency = "BDT", DialCode = "+880" },
            new() { Code = "id-ID", EnglishName = "Indonesian",   NativeName = "Bahasa Indonesia", IsRtl = false, PrimaryRegionIso = "ID", DefaultCurrency = "IDR", DialCode = "+62" },
            new() { Code = "ja-JP", EnglishName = "Japanese",     NativeName = "日本語",         IsRtl = false, PrimaryRegionIso = "JP", DefaultCurrency = "JPY", DialCode = "+81"  },
            new() { Code = "ko-KR", EnglishName = "Korean",       NativeName = "한국어",          IsRtl = false, PrimaryRegionIso = "KR", DefaultCurrency = "KRW", DialCode = "+82"  },
        ];

        public static LanguageInfo? Find(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            foreach (var lang in All)
                if (string.Equals(lang.Code, code, System.StringComparison.OrdinalIgnoreCase))
                    return lang;
            return null;
        }

        /// <summary>
        /// Suggests a language for a country: exact region match first,
        /// then script/region families (e.g. "IN" → hi-IN, "PK" → ur-PK,
        /// "BD" → bn-BD, "CN" → zh-CN, "AE" → ar-AE), else English.
        /// </summary>
        public static LanguageInfo Suggest(string? countryIso)
        {
            string iso = (countryIso ?? "").Trim().ToUpperInvariant();
            if (iso.Length == 0) return Primary;

            // 1) Per-country data (Countries/Country.XX.cs) — IN → hi-IN,
            //    CN → zh-CN, AE → ar-AE, CA → fr-FR …
            var country = Countries.CountryCatalog.Find(iso);
            if (country != null && Find(country.SuggestedLanguage) != null)
                return Find(country.SuggestedLanguage)!;

            foreach (var lang in All)
                if (lang.PrimaryRegionIso == iso)
                    return lang;

            // Script/region families
            var map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                // CN, TW, HK, SG -> Chinese ; IN -> Hindi ; PK -> Urdu ;
                // BD -> Bengali ; ID -> Indonesian ; JP -> Japanese ;
                // KR -> Korean ; AE/SA/EG/QA/KW/BH/OM/MA/DZ/TN/LY/IL/JO/LB -> Arabic
                ["CN"] = "zh-CN", ["TW"] = "zh-CN", ["HK"] = "zh-CN", ["SG"] = "zh-CN",
                ["IN"] = "hi-IN", ["PK"] = "ur-PK", ["BD"] = "bn-BD", ["ID"] = "id-ID",
                ["JP"] = "ja-JP", ["KR"] = "ko-KR",
                ["AE"] = "ar-AE", ["SA"] = "ar-AE", ["QA"] = "ar-AE", ["KW"] = "ar-AE",
                ["BH"] = "ar-AE", ["OM"] = "ar-AE", ["EG"] = "ar-AE", ["MA"] = "ar-AE",
            };
            if (map.TryGetValue(iso, out var code) && Find(code) != null)
                return Find(code)!;

            // European region families
            if (iso is "ES" or "MX" or "AR" or "CO" or "CL" or "PE") return Find("es-ES")!;
            if (iso is "FR" or "BE" or "CH" or "CA" or "SN") return Find("fr-FR")!;
            if (iso is "DE" or "AT") return Find("de-DE")!;
            if (iso is "RU" or "UA" or "KZ") return Find("ru-RU")!;
            if (iso is "BR" or "PT") return Find("pt-BR")!;
            if (iso is "TR" or "CY") return Find("tr-TR")!;

            return Primary;
        }

        /// <summary>Human-readable "English (en-US)" picker text with the
        /// BCP-47 code so the code is always visible in the picker.</summary>
        public static string Display(LanguageInfo lang) =>
            string.Equals(lang.EnglishName, lang.NativeName, System.StringComparison.Ordinal)
                ? $"{lang.EnglishName} ({lang.Code})"
                : $"{lang.EnglishName} — {lang.NativeName} ({lang.Code})";

        /// <summary>FlowDirection string for the shell root.</summary>
        public static string FlowDirection(bool isRtl) => isRtl ? "RightToLeft" : "LeftToRight";
    }
}
