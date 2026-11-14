// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.VN.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryVN
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "VN",
            Name = "Vietnam",
            DialCode = "+84",
            Flag = "🇻🇳",
            CurrencyCode = "VND",
            SuggestedLanguage = "en-US",
        };
    }
}
