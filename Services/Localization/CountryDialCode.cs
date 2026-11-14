// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/CountryDialCode.cs
// PROJECT: ProGlassAutomation
// MODULE: Global locale — country dial codes for phone inputs
//
// THIN FACADE. Data now lives in one short file per country:
//     Services/Localization/Countries/Country.XX.cs
// aggregated by CountryCatalog. This class keeps the historic API
// (All / Find / FindByDial / DefaultFor) so existing callers and the
// CountryPhoneControl need no changes.
// ═══════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using ProGlassAutomation.Services.Localization.Countries;

namespace ProGlassAutomation.Services.Localization
{
    /// <summary>One selectable country (defined in Countries/Country.XX.cs).</summary>
    public sealed class CountryInfo
    {
        public string Iso { get; init; } = "";
        public string Name { get; init; } = "";
        public string DialCode { get; init; } = "";
        public string Flag { get; init; } = "";

        /// <summary>ISO 4217 currency most used in this country.</summary>
        public string CurrencyCode { get; init; } = "AED";

        /// <summary>BCP-47 language to suggest for this country
        /// (IN → hi-IN, CN → zh-CN, AE → ar-AE …). English stays primary;
        /// this is only the second option.</summary>
        public string SuggestedLanguage { get; init; } = "en-US";

        /// <summary>Picker text ("🇦🇪 United Arab Emirates +971").</summary>
        public string DisplayName => $"{Flag} {Name} {DialCode}";

        /// <summary>Never leak the raw type name into a picker if a
        /// binding ever fails — always show the friendly label.</summary>
        public override string ToString() => DisplayName;
    }

    /// <summary>Historic facade over the per-country catalog.</summary>
    public static class CountryDialCode
    {
        public static IReadOnlyList<CountryInfo> All => CountryCatalog.All;

        public static CountryInfo? Find(string? iso) => CountryCatalog.Find(iso);

        public static CountryInfo? FindByDial(string? dial) =>
            CountryCatalog.FindByDial(dial);

        public static string DefaultFor(string? iso) =>
            CountryCatalog.DefaultFor(iso);
    }
}
