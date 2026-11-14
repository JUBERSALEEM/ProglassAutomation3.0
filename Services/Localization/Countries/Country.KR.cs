// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.KR.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryKR
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "KR",
            Name = "South Korea",
            DialCode = "+82",
            Flag = "🇰🇷",
            CurrencyCode = "KRW",
            SuggestedLanguage = "ko-KR",
        };
    }
}
