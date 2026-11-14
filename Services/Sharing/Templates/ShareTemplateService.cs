// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/Templates/ShareTemplateService.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — message template engine (Stage 1)
//
// Renders the share message from real document data. Variables are ONLY
// what ShareContext carries — there are no fake variables, and unknown
// {tokens} are left untouched (visible, so a typo in a custom template is
// obvious instead of silently blank).
//
// RESOLUTION (prompt §18, kept simple — AppConfig is the store)
// ──────────────
//   DOCTYPE+PROVIDER override  →  DOCTYPE override  →
//   PROVIDER override          →  GLOBAL override   →
//   built-in DOCTYPE default   →  built-in global default
//
// Operators edit the four override levels in Settings → Sharing;
// built-ins are the floor that always renders.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ProGlassAutomation.Services.Sharing.Templates
{
    public static class ShareTemplateService
    {
        // ── Built-in defaults (the floor; always available) ──────────

        public const string BuiltInGlobal =
            "Dear {CustomerAttentionName},\n\n" +
            "Please find attached your {DocumentType} {DocumentNumber}.\n\n" +
            "Thank you,\n\n" +
            "{PreparedByName}\n" +
            "{CompanyName}";

        private static readonly Dictionary<string, string> BuiltInByDocType =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["PI"] = "Dear {CustomerAttentionName},\n\n" +
                         "Please find attached your Proforma Invoice {DocumentNumber} " +
                         "amounting to {Currency} {Amount}.\n\n" +
                         "Valid until {Date}. Kindly confirm to proceed.\n\n" +
                         "Thank you,\n\n" +
                         "{PreparedByName}\n" +
                         "{CompanyName}",
                ["TI"] = "Dear {CustomerAttentionName},\n\n" +
                         "Please find attached your Tax Invoice {DocumentNumber} " +
                         "amounting to {Currency} {Amount}.\n\n" +
                         "Thank you,\n\n" +
                         "{PreparedByName}\n" +
                         "{CompanyName}",
                ["DN"] = "Dear {CustomerAttentionName},\n\n" +
                         "Please find attached Delivery Note {DocumentNumber}.\n\n" +
                         "Thank you,\n\n" +
                         "{PreparedByName}\n" +
                         "{CompanyName}",
                ["JO"] = "Dear {CustomerAttentionName},\n\n" +
                         "Please find attached Job Order {DocumentNumber}.\n\n" +
                         "Thank you,\n\n" +
                         "{PreparedByName}\n" +
                         "{CompanyName}",
            };

        /// <summary>
        /// Override keys understood in SharingSettings.Templates:
        /// "GLOBAL", "WHATSAPP" (provider), "PI" (doctype),
        /// "PI+WHATSAPP" (doctype+provider). Case-insensitive.
        /// </summary>
        public static string ResolveTemplateText(
            string? documentTypeKey,
            string? providerId,
            IDictionary<string, string>? overrides)
        {
            string doc = (documentTypeKey ?? "").Trim().ToUpperInvariant();
            string prov = (providerId ?? "").Trim().ToUpperInvariant();

            if (overrides != null)
            {
                if (doc.Length > 0 && prov.Length > 0
                    && TryOverride(overrides, doc + "+" + prov, out string? both))
                    return both!;
                if (doc.Length > 0
                    && TryOverride(overrides, doc, out string? byDoc))
                    return byDoc!;
                if (prov.Length > 0
                    && TryOverride(overrides, prov, out string? byProv))
                    return byProv!;
                if (TryOverride(overrides, "GLOBAL", out string? global))
                    return global!;
            }

            if (doc.Length > 0 && BuiltInByDocType.TryGetValue(doc, out string? builtIn))
                return builtIn;
            return BuiltInGlobal;
        }

        private static bool TryOverride(
            IDictionary<string, string> overrides, string key, out string? text)
        {
            text = null;
            foreach (var pair in overrides)
            {
                if (string.Equals(pair.Key?.Trim(), key, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(pair.Value))
                {
                    text = pair.Value;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Renders the resolved template against the context. Unknown
        /// {tokens} are preserved verbatim (a visible typo beats a silent
        /// blank). Never throws — worst case returns the raw template.
        /// </summary>
        public static string Render(
            string? template,
            ShareContext context,
            ShareRecipient? recipient,
            ShareSenderInfo? sender = null)
        {
            if (string.IsNullOrEmpty(template)) return "";
            if (context == null) return template;

            try
            {
                var values = BuildVariables(context, recipient, sender);

                var sb = new StringBuilder(template.Length + 64);
                for (int i = 0; i < template.Length; i++)
                {
                    if (template[i] == '{')
                    {
                        int end = template.IndexOf('}', i + 1);
                        if (end > i + 1)
                        {
                            string token = template.Substring(i + 1, end - i - 1);
                            if (values.TryGetValue(token, out string? value))
                            {
                                sb.Append(value);
                                i = end;
                                continue;
                            }
                            // Unknown token: fall through and keep it.
                        }
                    }
                    sb.Append(template[i]);
                }
                return CollapseBlankLines(sb.ToString());
            }
            catch
            {
                return template;
            }
        }

        /// <summary>
        /// Every variable the engine knows. Keys are the exact {tokens}.
        /// </summary>
        public static IReadOnlyDictionary<string, string> BuildVariables(
            ShareContext context,
            ShareRecipient? recipient,
            ShareSenderInfo? sender)
        {
            string docType = string.IsNullOrWhiteSpace(context.DocumentTypeDisplay)
                ? context.DocumentTypeKey ?? ""
                : context.DocumentTypeDisplay;

            // Attention falls back to customer name so "Dear ," never renders.
            string attention = !string.IsNullOrWhiteSpace(recipient?.Name)
                && recipient.Kind == ShareRecipientKind.CustomerAttention
                ? recipient.Name
                : !string.IsNullOrWhiteSpace(context.AttentionName)
                    ? context.AttentionName
                    : !string.IsNullOrWhiteSpace(recipient?.Name)
                        ? recipient.Name
                        : context.CustomerName ?? "";

            string amount = context.Amount.HasValue
                ? context.Amount.Value.ToString("N2", CultureInfo.InvariantCulture)
                : "";

            string date = context.DocumentDate != default
                ? context.DocumentDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture)
                : "";

            string fileName = context.Artifacts.Count > 0
                ? context.Artifacts[0].FileName ?? ""
                : "";

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CustomerName"] = context.CustomerName ?? "",
                ["CustomerAttentionName"] = attention.Trim(),
                ["DocumentType"] = (docType ?? "").Trim(),
                ["DocumentNumber"] = context.DocumentNumber ?? "",
                ["PreparedByName"] = sender?.Name ?? context.PreparedByName ?? "",
                ["PreparedByRole"] = sender?.Role ?? context.PreparedByRole ?? "",
                ["SalesmanName"] = context.SalesmanName ?? "",
                ["CompanyName"] = context.CompanyName ?? "",
                ["Amount"] = amount,
                ["Currency"] = context.Currency ?? "",
                ["Date"] = date,
                ["FileName"] = fileName,
            };
        }

        /// <summary>
        /// Collapses 3+ consecutive newlines to 2 (blank variables leave
        /// gaps, not holes).
        /// </summary>
        private static string CollapseBlankLines(string text)
        {
            while (text.Contains("\n\n\n", StringComparison.Ordinal))
                text = text.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
            return text.Trim();
        }
    }

    /// <summary>
    /// Sender display info for templates (resolved FROM without taking a
    /// dependency on the contacts namespace from templates UI).
    /// </summary>
    public sealed class ShareSenderInfo
    {
        public string Name { get; }
        public string Role { get; }

        public ShareSenderInfo(string name, string role = "")
        {
            Name = (name ?? "").Trim();
            Role = (role ?? "").Trim();
        }
    }
}
