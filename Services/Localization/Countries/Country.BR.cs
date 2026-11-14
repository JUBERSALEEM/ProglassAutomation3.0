// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/Country.BR.cs
// One short file per country: dial code, flag, currency, suggested
// language. Add a country = add one file + one line in CountryCatalog.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Localization.Countries
{
    internal static class CountryBR
    {
        public static CountryInfo Info { get; } = new()
        {
            Iso = "BR",
            Name = "Brazil",
            DialCode = "+55",
            Flag = "🇧🇷",
            CurrencyCode = "BRL",
            SuggestedLanguage = "pt-BR",
        };
    }
}
