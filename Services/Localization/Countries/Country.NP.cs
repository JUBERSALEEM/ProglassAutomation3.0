// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.NP.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryNP
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "NP",
            Name = "Nepal",
            DialCode = "+977",
            Flag = "🇳🇵",
            CurrencyCode = "NPR",
            SuggestedLanguage = "en-US",
        };
    }
}
