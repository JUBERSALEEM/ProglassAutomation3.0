// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/CurrencyCatalog.cs
// PROJECT: ProGlassAutomation
// MODULE: Global currency (2026-09-01)
//
// Static metadata: symbol, decimal digits and the region → currency map.
// Values are DISPLAY metadata only — live rates live in CurrencyService.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Localization
{
    public sealed class CurrencyInfo
    {
        public string Code { get; init; } = "";      // ISO 4217
        public string Symbol { get; init; } = "";
        public string EnglishName { get; init; } = "";
        public int Decimals { get; init; } = 2;
        public bool SymbolFirst { get; init; } = true;

        /// <summary>Picker text ("INR — Indian Rupee (₹)").</summary>
        public string DisplayName => $"{Code} — {EnglishName} ({Symbol})";

        /// <summary>Never leak the raw type name into a picker if a
        /// binding ever fails — always show the friendly label.</summary>
        public override string ToString() => DisplayName;
    }

    public static class CurrencyCatalog
    {
        public static readonly IReadOnlyList<CurrencyInfo> All =
        [
            new() { Code = "AED", Symbol = "AED", EnglishName = "UAE Dirham",              Decimals = 2 },
            new() { Code = "USD", Symbol = "$",   EnglishName = "US Dollar",                Decimals = 2 },
            new() { Code = "EUR", Symbol = "€",   EnglishName = "Euro",                    Decimals = 2 },
            new() { Code = "GBP", Symbol = "£",   EnglishName = "British Pound",           Decimals = 2 },
            new() { Code = "INR", Symbol = "₹",   EnglishName = "Indian Rupee",            Decimals = 2 },
            new() { Code = "CNY", Symbol = "¥",   EnglishName = "Chinese Yuan",            Decimals = 2 },
            new() { Code = "JPY", Symbol = "¥",   EnglishName = "Japanese Yen",            Decimals = 0 },
            new() { Code = "KRW", Symbol = "₩",   EnglishName = "South Korean Won",        Decimals = 0 },
            new() { Code = "SAR", Symbol = "﷼",   EnglishName = "Saudi Riyal",             Decimals = 2 },
            new() { Code = "QAR", Symbol = "﷼",   EnglishName = "Qatari Riyal",            Decimals = 2 },
            new() { Code = "KWD", Symbol = "د.ك", EnglishName = "Kuwaiti Dinar",           Decimals = 3 },
            new() { Code = "BHD", Symbol = "د.ب", EnglishName = "Bahraini Dinar",          Decimals = 3 },
            new() { Code = "OMR", Symbol = "ر.ع", EnglishName = "Omani Rial",              Decimals = 3 },
            new() { Code = "EGP", Symbol = "E£",  EnglishName = "Egyptian Pound",          Decimals = 2 },
            new() { Code = "TRY", Symbol = "₺",   EnglishName = "Turkish Lira",            Decimals = 2 },
            new() { Code = "RUB", Symbol = "₽",   EnglishName = "Russian Ruble",           Decimals = 2 },
            new() { Code = "BRL", Symbol = "R$",  EnglishName = "Brazilian Real",          Decimals = 2 },
            new() { Code = "PKR", Symbol = "₨",   EnglishName = "Pakistani Rupee",         Decimals = 2 },
            new() { Code = "BDT", Symbol = "৳",   EnglishName = "Bangladeshi Taka",        Decimals = 2 },
            new() { Code = "IDR", Symbol = "Rp",  EnglishName = "Indonesian Rupiah",       Decimals = 0 },
            new() { Code = "MYR", Symbol = "RM",  EnglishName = "Malaysian Ringgit",       Decimals = 2 },
            new() { Code = "PHP", Symbol = "₱",   EnglishName = "Philippine Peso",         Decimals = 2 },
            new() { Code = "THB", Symbol = "฿",   EnglishName = "Thai Baht",               Decimals = 2 },
            new() { Code = "VND", Symbol = "₫",   EnglishName = "Vietnamese Dong",         Decimals = 0 },
            new() { Code = "LKR", Symbol = "Rs",  EnglishName = "Sri Lankan Rupee",        Decimals = 2 },
            new() { Code = "NPR", Symbol = "Rs",  EnglishName = "Nepalese Rupee",          Decimals = 2 },
            new() { Code = "AUD", Symbol = "A$",  EnglishName = "Australian Dollar",       Decimals = 2 },
            new() { Code = "CAD", Symbol = "C$",  EnglishName = "Canadian Dollar",         Decimals = 2 },
            new() { Code = "CHF", Symbol = "CHF", EnglishName = "Swiss Franc",             Decimals = 2 },
            new() { Code = "SGD", Symbol = "S$",  EnglishName = "Singapore Dollar",        Decimals = 2 },
            new() { Code = "ZAR", Symbol = "R",   EnglishName = "South African Rand",      Decimals = 2 },
        ];

        public static CurrencyInfo? Find(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            foreach (var c in All)
                if (string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase))
                    return c;
            return null;
        }

        /// <summary>Currency typically used by a country (for the geo
        /// suggestion + phone defaults). Unknown → AED (books base).</summary>
        public static string CurrencyForRegion(string? countryIso)
        {
            if (string.IsNullOrWhiteSpace(countryIso)) return "AED";

            // 1) Per-country data (Countries/Country.XX.cs).
            var country = Countries.CountryCatalog.Find(countryIso);
            if (country != null) return country.CurrencyCode;

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AE"] = "AED", ["SA"] = "SAR", ["QA"] = "QAR", ["KW"] = "KWD",
                ["BH"] = "BHD", ["OM"] = "OMR", ["EG"] = "EGP",
                ["IN"] = "INR", ["PK"] = "PKR", ["BD"] = "BDT", ["LK"] = "LKR",
                ["NP"] = "NPR", ["CN"] = "CNY", ["JP"] = "JPY", ["KR"] = "KRW",
                ["US"] = "USD", ["GB"] = "GBP", ["EU"] = "EUR",
                ["DE"] = "EUR", ["FR"] = "EUR", ["ES"] = "EUR", ["IT"] = "EUR",
                ["TR"] = "TRY", ["RU"] = "RUB", ["BR"] = "BRL",
                ["ID"] = "IDR", ["MY"] = "MYR", ["PH"] = "PHP", ["TH"] = "THB",
                ["VN"] = "VND", ["AU"] = "AUD", ["CA"] = "CAD",
            };
            return map.TryGetValue(countryIso.Trim().ToUpperInvariant(), out var code)
                ? code
                : "AED";
        }
    }
}
