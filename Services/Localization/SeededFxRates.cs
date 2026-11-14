// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/SeededFxRates.cs
// PROJECT: ProGlassAutomation
// MODULE: Global currency (2026-09-01)
//
// Approximate seed rates (1 AED → X). ONLY used before the first
// successful internet fetch; the settings page labels the source so
// nobody mistakes a seed for a live quote. A fresh online fetch replaces
// every value in this table.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Localization
{
    public static class SeededFxRates
    {
        public static readonly IReadOnlyDictionary<string, decimal> Table =
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["AED"] = 1m,
                ["USD"] = 0.2723m,
                ["EUR"] = 0.2508m,
                ["GBP"] = 0.2131m,
                ["INR"] = 22.72m,   // ≈ 1 AED → ₹22.7 (2026-09 seed)
                ["CNY"] = 1.9600m,
                ["JPY"] = 41.20m,
                ["KRW"] = 370.0m,
                ["SAR"] = 1.0214m,
                ["QAR"] = 0.9913m,
                ["KWD"] = 0.0836m,
                ["BHD"] = 0.1027m,
                ["OMR"] = 0.1048m,
                ["EGP"] = 13.20m,
                ["TRY"] = 9.25m,
                ["RUB"] = 24.60m,
                ["BRL"] = 1.49m,
                ["PKR"] = 75.60m,
                ["BDT"] = 32.30m,
                ["IDR"] = 4360m,
                ["MYR"] = 1.190m,
                ["PHP"] = 15.35m,
                ["THB"] = 9.43m,
                ["VND"] = 6920m,
                ["LKR"] = 82.0m,
                ["NPR"] = 36.40m,
                ["AUD"] = 0.416m,
                ["CAD"] = 0.372m,
                ["CHF"] = 0.2340m,  // ≈ 1 AED → CHF 0.234 (2026-09 seed)
                ["SGD"] = 0.3640m,  // ≈ 1 AED → S$ 0.364 (2026-09 seed)
                ["ZAR"] = 4.850m,   // ≈ 1 AED → R 4.85 (2026-09 seed)
            };
    }
}
