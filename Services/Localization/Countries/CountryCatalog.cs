// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/Countries/CountryCatalog.cs
// Aggregates one short file per country (Country.XX.cs) into the single
// ordered list used by phone inputs, geo suggestion and currency lookup.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Localization.Countries
{
    public static class CountryCatalog
    {
        /// <summary>Ordered by importance for the phone picker (UAE,
        /// India, China first). Order matters for shared dial codes:
        /// +1 resolves to United States.</summary>
        public static readonly IReadOnlyList<CountryInfo> All =
        [
            CountryAE.Info,
            CountryIN.Info,
            CountryCN.Info,
            CountryUS.Info,
            CountryGB.Info,
            CountrySA.Info,
            CountryQA.Info,
            CountryKW.Info,
            CountryBH.Info,
            CountryOM.Info,
            CountryEG.Info,
            CountryPK.Info,
            CountryBD.Info,
            CountryID.Info,
            CountryJP.Info,
            CountryKR.Info,
            CountryES.Info,
            CountryFR.Info,
            CountryDE.Info,
            CountryIT.Info,
            CountryTR.Info,
            CountryRU.Info,
            CountryBR.Info,
            CountryMX.Info,
            CountryZA.Info,
            CountryAU.Info,
            CountryCA.Info,
            CountryNG.Info,
            CountryMY.Info,
            CountryPH.Info,
            CountryTH.Info,
            CountryVN.Info,
            CountryLK.Info,
            CountryNP.Info,
        ];

        public static CountryInfo? Find(string? iso)
        {
            if (string.IsNullOrWhiteSpace(iso)) return null;
            string key = iso.Trim().ToUpperInvariant();
            foreach (var c in All)
                if (c.Iso == key) return c;
            return null;
        }

        public static CountryInfo? FindByDial(string? dial)
        {
            string d = (dial ?? "").Trim();
            if (d.Length == 0) return null;
            foreach (var c in All)
                if (c.DialCode == d) return c;
            return null;
        }

        /// <summary>Dial code for a country ("" when unknown).</summary>
        public static string DefaultFor(string? iso) =>
            Find(iso)?.DialCode ?? "";
    }
}
