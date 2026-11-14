// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.GB.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryGB
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "GB",
            Name = "United Kingdom",
            DialCode = "+44",
            Flag = "🇬🇧",
            CurrencyCode = "GBP",
            SuggestedLanguage = "en-US",
        };
    }
}
