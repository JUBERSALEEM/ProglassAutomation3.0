// ═══════════════════════════════════════════════════════════════════════
// FILE: ClientProfileCard.cs.cs
// PROJECT: ProGlassAutomation
// MODULE: Client Profile
//
// ClientProfileCard.cs split out (Phase 11/12 God-VM split) — behaviour unchanged.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.MasterData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step2ClientProfile
{
    public class ClientProfileCard : ViewModelBase
    {
        public int Id { get; set; }
        public string CustomerName { get; set; } = "";
        public string CustomerTrn { get; set; } = "";
        public string CustomerReferenceCode { get; set; } = "";
        public string SalesRepresentative { get; set; } = "";
        public string DocumentPreparedBy { get; set; } = "";
        public string BillingAddress { get; set; } = "";
        public string AttentionExecutiveName { get; set; } = "";
        public string ClientContactPhone { get; set; } = "";
        public int UsageCount { get; set; }
        public string LastUsed { get; set; } = "";

        /// <summary>Privacy-safe TRN — "100**********003".</summary>
        public string TrnMasked
        {
            get
            {
                if (string.IsNullOrWhiteSpace(CustomerTrn))
                    return "No TRN";

                var digits = new string(CustomerTrn.Where(char.IsDigit).ToArray());
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

        /// <summary>First 40 chars of billing address.</summary>
        public string BillingAddressPreview =>
            string.IsNullOrWhiteSpace(BillingAddress)
                ? "No address"
                : BillingAddress.Length > 40
                    ? BillingAddress.Substring(0, 40) + "…"
                    : BillingAddress;

        /// <summary>Usage badge label.</summary>
        public string UsageBadgeText =>
            UsageCount > 0 ? $"Used {UsageCount}×" : "New";
    }
}
