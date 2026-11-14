// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.IN.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryIN
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "IN",
            Name = "India",
            DialCode = "+91",
            Flag = "🇮🇳",
            CurrencyCode = "INR",
            SuggestedLanguage = "hi-IN",
        };
    }
}
