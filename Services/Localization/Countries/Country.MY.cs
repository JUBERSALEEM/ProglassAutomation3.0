// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.MY.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryMY
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "MY",
            Name = "Malaysia",
            DialCode = "+60",
            Flag = "🇲🇾",
            CurrencyCode = "MYR",
            SuggestedLanguage = "en-US",
        };
    }
}
