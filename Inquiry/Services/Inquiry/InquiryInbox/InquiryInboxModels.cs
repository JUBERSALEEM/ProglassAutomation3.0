// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxModels.cs
// MODULE: Inquiry — website inbox protocol models.
//
// This file is the C# port of the website's cloud-to-Windows inbox
// protocol (Phase 8C/9E). Two compatibility contracts are load-bearing
// and MUST stay byte-identical with the Node bridge:
//
//   1. Manifest v2 serialization — canonical JSON: object keys sorted
//      lexicographically, JSON.stringify(…, null, 2) spacing, JS string
//      escaping, trailing newline. The manifest's SHA-256 travels to the
//      remote acknowledgement RPC, so a hash mismatch means a conflict.
//   2. Safe filename — NFKC normalize, [<>:"/\|?* + C0] → '_', trailing
//      dots/spaces stripped, 100 chars, "item" fallback.
//
// The DTOs mirror the PostgREST RPC rows (snake_case on the wire).
// ═══════════════════════════════════════════════════════════════════════

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProGlassAutomation.Services.Inquiry.InquiryInbox
{
    /// <summary>Row from plan_customer_inquiry_deliveries_v2.</summary>
    public sealed class InboxSubmissionPlan
    {
        [JsonPropertyName("submission_id")] public string SubmissionId { get; set; } = "";
        [JsonPropertyName("submission_reference")] public string SubmissionReference { get; set; } = "";
        [JsonPropertyName("submitted_at")] public string SubmittedAt { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("contact_name")] public string ContactName { get; set; } = "";
        [JsonPropertyName("company_name")] public string CompanyName { get; set; } = "";
        [JsonPropertyName("phone")] public string Phone { get; set; } = "";
        [JsonPropertyName("customer_reference")] public string CustomerReference { get; set; } = "";
        [JsonPropertyName("attention_name")] public string AttentionName { get; set; } = "";
        [JsonPropertyName("vat_trn_number")] public string VatTrnNumber { get; set; } = "";
        [JsonPropertyName("salesman_reference")] public string SalesmanReference { get; set; } = "";
        [JsonPropertyName("handover_estimator_name")] public string HandoverEstimatorName { get; set; } = "";
        [JsonPropertyName("project_name")] public string ProjectName { get; set; } = "";
        [JsonPropertyName("project_location")] public string ProjectLocation { get; set; } = "";
        [JsonPropertyName("inquiry_type")] public string InquiryType { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("document_count")] public long DocumentCount { get; set; }
        [JsonPropertyName("primary_estimator_code")] public string? PrimaryEstimatorCode { get; set; }
        /// <summary>
        /// Raw JSON array from the RPC — [{role, estimatorCode,
        /// displayName, assignedAt}]. Kept raw so the manifest can embed
        /// it verbatim (canonicalized) without a lossy round-trip.
        /// </summary>
        [JsonPropertyName("assignment_snapshot")] public string? AssignmentSnapshotRaw { get; set; }
        [JsonPropertyName("routing_key")] public string RoutingKey { get; set; } = "";

        /// <summary>Human estimator display for the primary assignment ("" when unassigned).</summary>
        public string PrimaryEstimatorDisplay()
        {
            var code = PrimaryEstimatorCode;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(AssignmentSnapshotRaw)) return "";
            try
            {
                using var doc = JsonDocument.Parse(AssignmentSnapshotRaw);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return "";
                foreach (var entry in doc.RootElement.EnumerateArray())
                {
                    var role = entry.TryGetProperty("role", out var r) ? r.GetString() : null;
                    var est = entry.TryGetProperty("estimatorCode", out var e) ? e.GetString() : null;
                    if ((role == "primary" || est == code)
                        && entry.TryGetProperty("displayName", out var d) && d.GetString() is { } name)
                        return name;
                }
            }
            catch { /* snapshot is defensive display data only */ }
            return "";
        }
    }

    /// <summary>Row from plan_customer_inquiry_delivery_documents.</summary>
    public sealed class InboxDocumentPlan
    {
        [JsonPropertyName("document_id")] public string DocumentId { get; set; } = "";
        [JsonPropertyName("original_filename")] public string OriginalFilename { get; set; } = "";
        [JsonPropertyName("detected_mime")] public string DetectedMime { get; set; } = "";
        [JsonPropertyName("byte_size")] public long ByteSize { get; set; }
        [JsonPropertyName("content_sha256")] public string ContentSha256 { get; set; } = "";
        [JsonPropertyName("storage_bucket")] public string StorageBucket { get; set; } = "";
        [JsonPropertyName("storage_object_key")] public string StorageObjectKey { get; set; } = "";
    }

    /// <summary>
    /// Parsed READY-folder manifest (version 2). Also the shape the
    /// writer below produces.
    /// </summary>
    public sealed class InboxManifest
    {
        public int Version { get; set; } = 2;
        public string RoutingKey { get; set; } = "";
        public string? PrimaryEstimatorCode { get; set; }
        public string AssignmentSnapshotRaw { get; set; } = "[]";
        /// <summary>The plan row exactly as the RPC returned it (raw JSON object).</summary>
        public string SubmissionRaw { get; set; } = "{}";
        public List<InboxManifestDocument> Documents { get; set; } = new();

        public sealed class InboxManifestDocument
        {
            public string Id { get; set; } = "";
            public string Filename { get; set; } = "";
            public string Mime { get; set; } = "";
            public long ByteSize { get; set; }
            public string Sha256 { get; set; } = "";
            public string LocalFile { get; set; } = "";
        }
    }

    /// <summary>
    /// Canonical (Node-compatible) JSON writer for the inbox manifest.
    /// Object keys sorted lexicographically (UTF-16 ordinal — same as JS
    /// Array.prototype.sort on ASCII keys), JSON.stringify 2-space
    /// spacing, JS string escaping (only " \\ and control chars), raw
    /// number passthrough, trailing newline added by the caller.
    /// </summary>
    public static class JsCanonicalJson
    {
        public static void Write(StringBuilder sb, JsonElement element, int indent)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    if (!element.EnumerateObject().Any()) { sb.Append("{}"); return; }
                    var props = element.EnumerateObject()
                        .OrderBy(p => p.Name, StringComparer.Ordinal)
                        .ToList();
                    sb.Append("{\n");
                    for (int i = 0; i < props.Count; i++)
                    {
                        Spaces(sb, indent + 1);
                        sb.Append('"').Append(Escape(props[i].Name)).Append("\": ");
                        Write(sb, props[i].Value, indent + 1);
                        if (i < props.Count - 1) sb.Append(',');
                        sb.Append('\n');
                    }
                    Spaces(sb, indent);
                    sb.Append('}');
                    break;
                }
                case JsonValueKind.Array:
                {
                    var items = element.EnumerateArray().ToList();
                    if (items.Count == 0) { sb.Append("[]"); return; }
                    sb.Append("[\n");
                    for (int i = 0; i < items.Count; i++)
                    {
                        Spaces(sb, indent + 1);
                        Write(sb, items[i], indent + 1);
                        if (i < items.Count - 1) sb.Append(',');
                        sb.Append('\n');
                    }
                    Spaces(sb, indent);
                    sb.Append(']');
                    break;
                }
                case JsonValueKind.String:
                    sb.Append('"').Append(Escape(element.GetString() ?? "")).Append('"');
                    break;
                case JsonValueKind.Null:
                    sb.Append("null");
                    break;
                case JsonValueKind.True:
                    sb.Append("true"); break;
                case JsonValueKind.False:
                    sb.Append("false"); break;
                default:
                    // Numbers: keep the exact wire representation.
                    sb.Append(element.GetRawText());
                    break;
            }
        }

        /// <summary>
        /// Builds the manifest bytes for the v2 handoff, then returns the
        /// SHA-256 of those bytes (the value the acknowledgement RPC
        /// receives and the READY file stores).
        /// </summary>
        public static (byte[] ManifestBytes, string Sha256) BuildManifestBytes(InboxManifest manifest)
        {
            using var submission = JsonDocument.Parse(manifest.SubmissionRaw);
            using var snapshot = JsonDocument.Parse(string.IsNullOrWhiteSpace(manifest.AssignmentSnapshotRaw) ? "[]" : manifest.AssignmentSnapshotRaw);
            using var documents = JsonDocument.Parse(BuildDocumentsJson(manifest));
            var sb = new System.Text.StringBuilder();
            // Top-level keys sorted: documents, routing, submission, version.
            sb.Append("{\n");
            // documents
            sb.Append("  \"documents\": ");
            Write(sb, documents.RootElement, 1);
            sb.Append(",\n");
            // routing
            sb.Append("  \"routing\": {\n");
            sb.Append("    \"assignmentSnapshot\": ");
            Write(sb, snapshot.RootElement, 2);
            sb.Append(",\n");
            Spaces(sb, 3);
            sb.Append('"').Append(Escape(manifest.RoutingKey)).Append("\",\n");
            sb.Append("    \"primaryEstimatorCode\": ");
            if (string.IsNullOrWhiteSpace(manifest.PrimaryEstimatorCode)) sb.Append("null");
            else sb.Append('"').Append(Escape(manifest.PrimaryEstimatorCode)).Append('"');
            sb.Append("\n  },\n");
            // submission (raw, canonicalized)
            sb.Append("  \"submission\": ");
            Write(sb, submission.RootElement, 1);
            sb.Append(",\n");
            // version
            sb.Append("  \"version\": 2\n");
            sb.Append("}\n");

            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            return (bytes, hash);
        }

        private static string BuildDocumentsJson(InboxManifest manifest)
        {
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < manifest.Documents.Count; i++)
            {
                var d = manifest.Documents[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"byteSize\":").Append(d.ByteSize)
                  .Append(",\"filename\":\"").Append(Escape(d.Filename)).Append('"')
                  .Append(",\"id\":\"").Append(Escape(d.Id)).Append('"')
                  .Append(",\"localFile\":\"").Append(Escape(d.LocalFile)).Append('"')
                  .Append(",\"mime\":\"").Append(Escape(d.Mime)).Append('"')
                  .Append(",\"sha256\":\"").Append(Escape(d.Sha256)).Append('"')
                  .Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static void Spaces(StringBuilder sb, int count)
        {
            for (int i = 0; i < count; i++) sb.Append("  ");
        }

        /// <summary>JS JSON.stringify string escaping (not C#'s stricter set).</summary>
        public static string Escape(string value)
        {
            var sb = new System.Text.StringBuilder(value.Length + 8);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>Node-compatible safe filename (same as the bridge's safe()).</summary>
    public static class InboxFileNames
    {
        private static readonly Regex Unsafe = new("[<>:\"/\\\\|?*\\u0000-\\u001f]", RegexOptions.Compiled);
        private static readonly Regex Trailing = new("[. ]+$", RegexOptions.Compiled);

        public static string Safe(string value)
        {
            var s = Unsafe.Replace(value.Normalize(System.Text.NormalizationForm.FormKC), "_");
            s = Trailing.Replace(s, "");
            s = s.Length > 100 ? s[..100] : s;
            return string.IsNullOrEmpty(s) ? "item" : s;
        }

        public static string BaseName(string path)
        {
            var name = System.IO.Path.GetFileName(path);
            return name ?? "";
        }
    }

    /// <summary>Routing key contract: "unassigned" or "assigned/{CODE}".</summary>
    public static class InboxRouting
    {
        private static readonly Regex Assigned = new("^assigned/[A-Z0-9][A-Z0-9_-]{1,29}$", RegexOptions.Compiled);

        public static bool IsValid(string? key) =>
            string.Equals(key, "unassigned", StringComparison.Ordinal) || (key != null && Assigned.IsMatch(key));
    }

    /// <summary>
    /// One row of list_customer_inquiry_communications_v1 — the
    /// per-inquiry chat channel on the website (table
    /// public.inquiry_communications, Phase 9D). Read-only pull into
    /// the local communications feed (docs/07, phase 2).
    /// </summary>
    public sealed class InquiryWebsiteMessage
    {
        /// <summary>Website message id (uuid) — dedupe key (ExternalId).</summary>
        public string Id { get; set; } = "";
        /// <summary>"customer" or "internal".</summary>
        public string Visibility { get; set; } = "customer";
        /// <summary>"message" or "missing_information_request".</summary>
        public string MessageKind { get; set; } = "message";
        /// <summary>"open" / "resolved" for requests, null for messages.</summary>
        public string? RequestStatus { get; set; }
        public string Body { get; set; } = "";
        /// <summary>Author display name from public.user_profiles.</summary>
        public string? AuthorDisplayName { get; set; }
        /// <summary>Website created_at (timestamptz → UTC DateTime).</summary>
        public DateTime? CreatedAt { get; set; }
    }
}
