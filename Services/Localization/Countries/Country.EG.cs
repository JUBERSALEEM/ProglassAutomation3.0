// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.EG.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryEG
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "EG",
            Name = "Egypt",
            DialCode = "+20",
            Flag = "🇪🇬",
            CurrencyCode = "EGP",
            SuggestedLanguage = "ar-AE",
        };
    }
}
