// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/SharingSettings.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — persisted preferences (Stage 1)
//
// Lives as AppConfig.Sharing (JSON, debounced save) — NO separate settings
// system. Every knob here has working code behind it (Settings UI in
// Stage 2 binds these same properties). Defaults keep upgrades
// byte-identical in behavior: sharing is purely additive, nothing existing
// changes until the operator opens the composer.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Sharing
{
    /// <summary>Per-document-type defaults (prompt §20 — data, not code).</summary>
    public sealed class ShareDocumentRule
    {
        /// <summary>Doc key: PI, JO, DN, TI, Statement, Report…</summary>
        public string DocumentTypeKey { get; set; } = "";

        /// <summary>Suggested artifact: Pdf, Excel, Csv.</summary>
        public string DefaultFormat { get; set; } = "Pdf";

        /// <summary>Provider id: WHATSAPP, EMAIL, SYSTEMFILE.</summary>
        public string DefaultProviderId { get; set; } = "WHATSAPP";

        /// <summary>CustomerAttention, Customer, CompanyRepresentative, Salesman.</summary>
        public string DefaultRecipientKind { get; set; } = "CustomerAttention";

        /// <summary>False locks the message box (compliance flows).</summary>
        public bool AllowMessageEdit { get; set; } = true;
    }

    public sealed class SharingSettings
    {
        /// <summary>Provider ids enabled in the composer (order = display).</summary>
        public List<string> EnabledProviders { get; set; } = new()
        {
            "WHATSAPP", "EMAIL", "SYSTEMFILE"
        };

        /// <summary>Preselected provider when no document rule matches.</summary>
        public string DefaultProviderId { get; set; } = "WHATSAPP";

        /// <summary>
        /// Country code applied ONLY to trunk-0 local numbers (PhoneNumbers).
        /// A setting — not an assumption that every number is Emirati.
        /// </summary>
        public string DefaultCountryCode { get; set; } = "971";

        /// <summary>Per-document defaults; missing key = global defaults.</summary>
        public List<ShareDocumentRule> DocumentRules { get; set; } = new()
        {
            new() { DocumentTypeKey = "PI", DefaultFormat = "Pdf", DefaultProviderId = "WHATSAPP", DefaultRecipientKind = "CustomerAttention" },
            new() { DocumentTypeKey = "JO", DefaultFormat = "Pdf", DefaultProviderId = "WHATSAPP", DefaultRecipientKind = "CustomerAttention" },
            new() { DocumentTypeKey = "DN", DefaultFormat = "Pdf", DefaultProviderId = "WHATSAPP", DefaultRecipientKind = "CustomerAttention" },
            new() { DocumentTypeKey = "TI", DefaultFormat = "Pdf", DefaultProviderId = "EMAIL", DefaultRecipientKind = "CustomerAttention" },
            new() { DocumentTypeKey = "Statement", DefaultFormat = "Pdf", DefaultProviderId = "WHATSAPP", DefaultRecipientKind = "CustomerAttention" },
            new() { DocumentTypeKey = "Report", DefaultFormat = "Excel", DefaultProviderId = "EMAIL", DefaultRecipientKind = "Customer" },
        };

        /// <summary>
        /// Template overrides. Keys: GLOBAL, provider id, doc key, or
        /// DOC+PROVIDER (see ShareTemplateService). Empty = built-ins.
        /// </summary>
        public Dictionary<string, string> TemplateOverrides { get; set; } = new();

        /// <summary>Hours staged share copies are kept (stale sweep).</summary>
        public int TempRetentionHours { get; set; } = 24;

        /// <summary>Email large-attachment warning threshold (MB).</summary>
        public int EmailLargeAttachmentMb { get; set; } = 20;

        public ShareDocumentRule RuleFor(string? documentTypeKey)
        {
            if (!string.IsNullOrWhiteSpace(documentTypeKey))
            {
                foreach (var rule in DocumentRules)
                {
                    if (string.Equals(rule.DocumentTypeKey?.Trim(), documentTypeKey.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                        return rule;
                }
            }
            return new ShareDocumentRule { DocumentTypeKey = documentTypeKey ?? "" };
        }

        public bool IsProviderEnabled(string? providerId)
        {
            if (EnabledProviders == null || EnabledProviders.Count == 0)
                return true; // empty list = all enabled (never strand the user)
            if (string.IsNullOrWhiteSpace(providerId)) return false;
            foreach (string id in EnabledProviders)
                if (string.Equals(id?.Trim(), providerId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
