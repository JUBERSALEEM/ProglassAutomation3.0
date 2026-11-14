// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.IT.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryIT
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "IT",
            Name = "Italy",
            DialCode = "+39",
            Flag = "🇮🇹",
            CurrencyCode = "EUR",
            SuggestedLanguage = "en-US",
        };
    }
}
