// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.SA.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountrySA
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "SA",
            Name = "Saudi Arabia",
            DialCode = "+966",
            Flag = "🇸🇦",
            CurrencyCode = "SAR",
            SuggestedLanguage = "ar-AE",
        };
    }
}
