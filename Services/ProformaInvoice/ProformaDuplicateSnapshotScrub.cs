using System.Text.Json.Nodes;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    /// <summary>
    /// Last-resort detach for a duplicated proforma snapshot.
    /// Used only when the mapper cannot round-trip the JSON. Never returns
    /// the original document: a failed scrub must not keep the source inquiry link.
    /// </summary>
    internal static class ProformaDuplicateSnapshotScrub
    {
        internal static string? TryDetach(string? json, string? newProformaId)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            JsonObject? root;
            try
            {
                root = JsonNode.Parse(json) as JsonObject;
            }
            catch
            {
                return null;
            }

            if (root == null)
                return null;

            if (root["Company"] is not JsonObject company)
                return null;

            company["InquiryId"] = null;
            company["ProformaId"] = newProformaId ?? string.Empty;
            return root.ToJsonString();
        }
    }
}
