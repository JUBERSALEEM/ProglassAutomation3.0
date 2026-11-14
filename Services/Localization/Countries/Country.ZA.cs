// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.ZA.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryZA
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "ZA",
            Name = "South Africa",
            DialCode = "+27",
            Flag = "🇿🇦",
            CurrencyCode = "ZAR",
            SuggestedLanguage = "en-US",
        };
    }
}
