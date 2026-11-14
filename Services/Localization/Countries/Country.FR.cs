// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.FR.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryFR
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "FR",
            Name = "France",
            DialCode = "+33",
            Flag = "🇫🇷",
            CurrencyCode = "EUR",
            SuggestedLanguage = "fr-FR",
        };
    }
}
