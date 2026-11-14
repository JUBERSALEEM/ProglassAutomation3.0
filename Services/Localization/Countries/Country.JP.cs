// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.JP.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryJP
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "JP",
            Name = "Japan",
            DialCode = "+81",
            Flag = "🇯🇵",
            CurrencyCode = "JPY",
            SuggestedLanguage = "ja-JP",
        };
    }
}
