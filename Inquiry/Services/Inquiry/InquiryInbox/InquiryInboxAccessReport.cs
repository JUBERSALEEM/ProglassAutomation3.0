using System.Text.Json;

namespace ProGlassAutomation.Services.Inquiry.InquiryInbox
{
    /// <summary>Self-reported account access returned by get_my_inquiry_access_v1.</summary>
    public sealed class InquiryInboxAccessReport
    {
        public string AccountEmail { get; init; } = "";
        public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Capabilities { get; init; } = Array.Empty<string>();
        public string? DenialReason { get; init; }

        public bool CanPlanBridge => Capabilities.Contains("inbox.plan.bridge", StringComparer.Ordinal);
        public string RolesText => Roles.Count == 0 ? "(no assigned role)" : string.Join(", ", Roles);

        public static InquiryInboxAccessReport Parse(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                if (value.GetArrayLength() == 0)
                    throw new JsonException("The access report was empty.");
                value = value[0];
            }
            if (value.ValueKind != JsonValueKind.Object)
                throw new JsonException("The access report was not an object.");

            return new InquiryInboxAccessReport
            {
                AccountEmail = ReadString(value, "account_email") ?? ReadString(value, "email") ?? "",
                Roles = ReadList(value, "roles"),
                Capabilities = ReadList(value, "capabilities"),
                DenialReason = ReadNullableValue(value, "denial_reason")
            };
        }

        public string MissingCapabilityMessage()
        {
            string email = string.IsNullOrWhiteSpace(AccountEmail) ? "(unknown account)" : AccountEmail;
            string message = $"Account {email}, role(s) {RolesText}, is missing capability inbox.plan.bridge.";
            return DenialReason == null ? message : message + " " + DenialReason;
        }

        private static string? ReadString(JsonElement value, string property) =>
            value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : null;

        private static IReadOnlyList<string> ReadList(JsonElement value, string property)
        {
            if (!value.TryGetProperty(property, out var item) || item.ValueKind == JsonValueKind.Null)
                return Array.Empty<string>();
            if (item.ValueKind == JsonValueKind.String)
                return new[] { item.GetString() ?? "" };
            if (item.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            var result = new List<string>();
            foreach (var entry in item.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String)
                    result.Add(entry.GetString() ?? "");
                else if (entry.ValueKind == JsonValueKind.Object)
                {
                    string? role = ReadString(entry, "role") ?? ReadString(entry, "name");
                    if (role != null) result.Add(role);
                }
            }
            return result;
        }

        private static string? ReadNullableValue(JsonElement value, string property)
        {
            if (!value.TryGetProperty(property, out var item) || item.ValueKind == JsonValueKind.Null)
                return null;
            return item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
        }
    }
}
