// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.OM.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryOM
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "OM",
            Name = "Oman",
            DialCode = "+968",
            Flag = "🇴🇲",
            CurrencyCode = "OMR",
            SuggestedLanguage = "ar-AE",
        };
    }
}
