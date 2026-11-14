// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.RU.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryRU
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "RU",
            Name = "Russia",
            DialCode = "+7",
            Flag = "🇷🇺",
            CurrencyCode = "RUB",
            SuggestedLanguage = "ru-RU",
        };
    }
}
