// ═══════════════════════════════════════════════════════════════════════
// FILE: CompanyProfileCard.cs
// PROJECT: ProGlassAutomation
// MODULE: CompanySetupViewModel.cs
//
// CompanyProfileCard split out of CompanySetupViewModel.cs (Phase 11/12 God-VM split) — behaviour unchanged.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.MasterData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step1CompanySetup
{
    public class CompanyProfileCard : ViewModelBase
    {
        public int Id { get; set; }
        public string CompanyName { get; set; } = "";
        public string Trn { get; set; } = "";
        public string Location { get; set; } = "";
        public int UsageCount { get; set; }
        public string LastUsed { get; set; } = "";
        public string LogoPath { get; set; } = "";
        public string Representative { get; set; } = "";
        public string RepresentativeMobile { get; set; } = "";
        public string CompanyEmail { get; set; } = "";
        public string AccountNumber { get; set; } = "";
        public string BankName { get; set; } = "";
        public bool IsDefault { get; set; }

        // v3 — logo placement carried from the Company Center record.
        public CompanyLogoLayout LogoLayout { get; set; } = CompanyLogoLayout.None;
        public double LogoWidthMm { get; set; } = 22;
        public double LogoHeightMm { get; set; } = 26;
        // IsDefault removed 2026-09-04: default-mark mechanism deleted;
        // Step 1 loads a company only when the user picks it (or it is
        // restored from a saved invoice's snapshot).

        /// <summary>
        /// Privacy-safe TRN display — "100**********003".
        /// Shows first 3 + last 3 digits, masks middle with *.
        /// </summary>
        public string TrnMasked
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Trn))
                    return "No TRN";

                var digits = new string(Trn.Where(char.IsDigit).ToArray());
                if (digits.Length < 6) return digits;

                int visibleEnd = Math.Min(3, digits.Length);
                int visibleStart = Math.Max(0, digits.Length - 3);
                int maskLen = Math.Max(0, digits.Length - visibleEnd
                                               - (digits.Length - visibleStart));

                return digits.Substring(0, visibleEnd)
                     + new string('*', maskLen)
                     + digits.Substring(visibleStart);
            }
        }

        /// <summary>Usage badge label — "Used 5×" or "New".</summary>
        public string UsageBadgeText =>
            UsageCount > 0 ? $"Used {UsageCount}×" : "New";
    }
}
