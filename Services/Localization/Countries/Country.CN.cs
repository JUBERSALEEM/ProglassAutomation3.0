// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.CN.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryCN
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "CN",
            Name = "China",
            DialCode = "+86",
            Flag = "🇨🇳",
            CurrencyCode = "CNY",
            SuggestedLanguage = "zh-CN",
        };
    }
}
