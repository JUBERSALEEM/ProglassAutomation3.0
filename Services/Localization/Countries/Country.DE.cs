// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.DE.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryDE
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "DE",
            Name = "Germany",
            DialCode = "+49",
            Flag = "🇩🇪",
            CurrencyCode = "EUR",
            SuggestedLanguage = "de-DE",
        };
    }
}
