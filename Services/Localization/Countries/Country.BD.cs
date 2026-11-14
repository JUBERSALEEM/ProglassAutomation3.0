// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.BD.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryBD
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "BD",
            Name = "Bangladesh",
            DialCode = "+880",
            Flag = "🇧🇩",
            CurrencyCode = "BDT",
            SuggestedLanguage = "bn-BD",
        };
    }
}
