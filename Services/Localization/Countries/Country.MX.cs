// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.MX.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryMX
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "MX",
            Name = "Mexico",
            DialCode = "+52",
            Flag = "🇲🇽",
            CurrencyCode = "MXN",
            SuggestedLanguage = "es-ES",
        };
    }
}
