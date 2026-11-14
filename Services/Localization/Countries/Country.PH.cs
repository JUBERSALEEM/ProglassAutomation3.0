// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.PH.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryPH
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "PH",
            Name = "Philippines",
            DialCode = "+63",
            Flag = "🇵🇭",
            CurrencyCode = "PHP",
            SuggestedLanguage = "en-US",
        };
    }
}
