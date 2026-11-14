// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.ID.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryID
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "ID",
            Name = "Indonesia",
            DialCode = "+62",
            Flag = "🇮🇩",
            CurrencyCode = "IDR",
            SuggestedLanguage = "id-ID",
        };
    }
}
