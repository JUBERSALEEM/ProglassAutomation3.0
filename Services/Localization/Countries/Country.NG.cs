// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.NG.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryNG
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "NG",
            Name = "Nigeria",
            DialCode = "+234",
            Flag = "🇳🇬",
            CurrencyCode = "NGN",
            SuggestedLanguage = "en-US",
        };
    }
}
