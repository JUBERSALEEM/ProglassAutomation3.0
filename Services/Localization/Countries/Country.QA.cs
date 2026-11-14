// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.QA.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryQA
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "QA",
            Name = "Qatar",
            DialCode = "+974",
            Flag = "🇶🇦",
            CurrencyCode = "QAR",
            SuggestedLanguage = "ar-AE",
        };
    }
}
