// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.AU.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryAU
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "AU",
            Name = "Australia",
            DialCode = "+61",
            Flag = "🇦🇺",
            CurrencyCode = "AUD",
            SuggestedLanguage = "en-US",
        };
    }
}
