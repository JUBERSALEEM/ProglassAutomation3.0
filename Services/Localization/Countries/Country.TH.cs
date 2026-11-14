// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.TH.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryTH
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "TH",
            Name = "Thailand",
            DialCode = "+66",
            Flag = "🇹🇭",
            CurrencyCode = "THB",
            SuggestedLanguage = "en-US",
        };
    }
}
