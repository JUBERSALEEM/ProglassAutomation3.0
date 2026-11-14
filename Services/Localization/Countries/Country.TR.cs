// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.TR.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryTR
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "TR",
            Name = "Turkey",
            DialCode = "+90",
            Flag = "🇹🇷",
            CurrencyCode = "TRY",
            SuggestedLanguage = "tr-TR",
        };
    }
}
