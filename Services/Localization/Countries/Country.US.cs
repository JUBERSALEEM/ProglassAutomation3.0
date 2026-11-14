// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.US.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryUS
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "US",
            Name = "United States",
            DialCode = "+1",
            Flag = "🇺🇸",
            CurrencyCode = "USD",
            SuggestedLanguage = "en-US",
        };
    }
}
