// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.LK.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryLK
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "LK",
            Name = "Sri Lanka",
            DialCode = "+94",
            Flag = "🇱🇰",
            CurrencyCode = "LKR",
            SuggestedLanguage = "en-US",
        };
    }
}
