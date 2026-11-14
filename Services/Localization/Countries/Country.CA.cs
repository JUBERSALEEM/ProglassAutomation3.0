// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.CA.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryCA
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "CA",
            Name = "Canada",
            DialCode = "+1",
            Flag = "🇨🇦",
            CurrencyCode = "CAD",
            SuggestedLanguage = "fr-FR",
        };
    }
}
