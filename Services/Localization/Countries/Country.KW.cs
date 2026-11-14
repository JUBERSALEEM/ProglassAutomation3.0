// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.KW.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryKW
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "KW",
            Name = "Kuwait",
            DialCode = "+965",
            Flag = "🇰🇼",
            CurrencyCode = "KWD",
            SuggestedLanguage = "ar-AE",
        };
    }
}
