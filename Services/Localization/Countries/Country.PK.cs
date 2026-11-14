// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.PK.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryPK
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "PK",
            Name = "Pakistan",
            DialCode = "+92",
            Flag = "🇵🇰",
            CurrencyCode = "PKR",
            SuggestedLanguage = "ur-PK",
        };
    }
}
