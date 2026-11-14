// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.BH.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryBH
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "BH",
            Name = "Bahrain",
            DialCode = "+973",
            Flag = "🇧🇭",
            CurrencyCode = "BHD",
            SuggestedLanguage = "ar-AE",
        };
    }
}
