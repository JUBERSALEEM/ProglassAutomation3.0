// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.AE.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryAE
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "AE",
            Name = "United Arab Emirates",
            DialCode = "+971",
            Flag = "🇦🇪",
            CurrencyCode = "AED",
            SuggestedLanguage = "ar-AE",
        };
    }
}
