// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.ES.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryES
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "ES",
            Name = "Spain",
            DialCode = "+34",
            Flag = "🇪🇸",
            CurrencyCode = "EUR",
            SuggestedLanguage = "es-ES",
        };
    }
}
