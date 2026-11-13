// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.b.2.ProformaInvoiceJson.cs
// MODULE: step6 — the stored document body.
//
// The whole wizard state (header, sections, groups, charges, summary, terms
// and the last P&L) is stored as one JSON document beside the indexed columns
// a register queries. That is deliberate: the body is a document, not a
// relational ledger, and a single value either round-trips exactly or does
// not. The columns outside it exist so the register never deserializes JSON
// to answer "whose invoice is this and what is it worth".
//
// Rules this file enforces:
//   • an unreadable body is REPORTED, never replaced by an empty invoice (a
//     silent "{}" would look like a lost quotation that never existed);
//   • a body written by a NEWER format is refused, not half-understood;
//   • enums are stored by name, so a renumbered enum cannot change meaning;
//   • reading never throws for bad JSON — it returns a failure with a message.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Proforma;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>Serializes and restores a draft's body.</summary>
    public static class ProformaInvoiceJson
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>The stored body. Never null; never contains WPF types.</summary>
        public static string Write(ProformaInvoiceDraft draft)
        {
            if (draft == null) return "{}";
            draft.FormatVersion = ProformaInvoiceDraft.CurrentFormatVersion;
            return JsonSerializer.Serialize(draft, Options);
        }

        /// <summary>Reads one body, stating why when it cannot.</summary>
        public static ProformaInvoiceJsonResult Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
                return ProformaInvoiceJsonResult.Fail(
                    "This invoice carries no stored document body.");

            ProformaInvoiceDraft? draft;
            try
            {
                draft = JsonSerializer.Deserialize<ProformaInvoiceDraft>(json, Options);
            }
            catch (JsonException error)
            {
                return ProformaInvoiceJsonResult.Fail(
                    "The stored invoice body is not readable: " + error.Message);
            }
            catch (NotSupportedException error)
            {
                return ProformaInvoiceJsonResult.Fail(
                    "The stored invoice body uses a shape this build does not know: " + error.Message);
            }

            if (draft == null)
                return ProformaInvoiceJsonResult.Fail("The stored invoice body is empty.");

            if (draft.FormatVersion > ProformaInvoiceDraft.CurrentFormatVersion)
                return ProformaInvoiceJsonResult.Fail(
                    "This invoice was saved by a newer version of ProGlass (format " +
                    draft.FormatVersion + "). Update the application before opening it.");

            draft.EnsureStructure();
            return ProformaInvoiceJsonResult.Ok(draft);
        }
    }

    /// <summary>Either a draft or the sentence that explains why there is none.</summary>
    public sealed class ProformaInvoiceJsonResult
    {
        public bool Succeeded { get; init; }
        public ProformaInvoiceDraft? Draft { get; init; }
        public string Message { get; init; } = "";

        public static ProformaInvoiceJsonResult Ok(ProformaInvoiceDraft draft) =>
            new() { Succeeded = true, Draft = draft };

        public static ProformaInvoiceJsonResult Fail(string message) =>
            new() { Succeeded = false, Message = message };
    }
}
