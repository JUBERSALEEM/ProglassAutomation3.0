// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxSettings.cs
// MODULE: Inquiry — website inbox settings (persisted, password protected).
//
// The WPF side speaks to the website's Supabase backend as the dedicated
// "inquiry inbox bridge" service account (email/password). The password
// is never stored in plain text — it is DPAPI-encrypted to the current
// Windows user (same trust model as the legacy build). All other fields
// are plain JSON under AppData.
// ═══════════════════════════════════════════════════════════════════════

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProGlassAutomation.Services.Inquiry.InquiryInbox
{
    public sealed class InquiryInboxSettings
    {
        public const string DefaultInboxRoot = @"C:\ProgramData\ProGlass\CustomerInquiryInbox";

        /// <summary>Website backend (Supabase) URL, e.g. https://xxxx.supabase.co</summary>
        [JsonPropertyName("supabaseUrl")] public string SupabaseUrl { get; set; } = "";
        [JsonPropertyName("anonKey")] public string AnonKey { get; set; } = "";
        /// <summary>Bridge service account email.</summary>
        [JsonPropertyName("bridgeEmail")] public string BridgeEmail { get; set; } = "";
        /// <summary>DPAPI-encrypted bridge password (base64).</summary>
        [JsonPropertyName("encryptedPassword")] public string EncryptedPassword { get; set; } = "";

        /// <summary>
        /// Local inbox root. Must match the Node bridge's
        /// GLASS_INBOX_ROOT when both run on the same machine.
        /// </summary>
        [JsonPropertyName("inboxRoot")] public string InboxRoot { get; set; } = DefaultInboxRoot;
        /// <summary>Automatic sync interval (minutes).</summary>
        [JsonPropertyName("pollMinutes")] public int PollMinutes { get; set; } = 5;
        [JsonPropertyName("notificationsEnabled")] public bool NotificationsEnabled { get; set; } = true;
        [JsonPropertyName("notificationSound")] public bool NotificationSound { get; set; } = true;
        /// <summary>
        /// Opt-in: mirror local workflow transitions (Prepared/Sent/
        /// Confirmed/Hold/Cancelled) to the website so its reports page
        /// can show the inquiry's status. Requires the website-side RPC
        /// from docs/website-inquiry-status-writeback.sql.
        /// </summary>
        [JsonPropertyName("reportStatusToWebsite")] public bool ReportStatusToWebsite { get; set; } = false;

        /// <summary>Operational state (not user config) — last sync bookkeeping.</summary>
        [JsonPropertyName("lastSyncAtUtc")] public string? LastSyncAtUtc { get; set; }
        [JsonPropertyName("lastSyncStatus")] public string? LastSyncStatus { get; set; }

        public bool HasPassword => !string.IsNullOrWhiteSpace(EncryptedPassword);

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(SupabaseUrl)
            && !string.IsNullOrWhiteSpace(AnonKey)
            && !string.IsNullOrWhiteSpace(BridgeEmail)
            && HasPassword;

        /// <summary>Decrypted bridge password ("" when absent or undecryptable).</summary>
        public string GetPassword()
        {
            if (!HasPassword) return "";
            try
            {
                var blob = Convert.FromBase64String(EncryptedPassword);
                var plain = ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(plain);
            }
            catch { return ""; }
        }

        public string EncryptPassword(string plain)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(plain ?? "");
            return Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
        }

        // ── Persistence ─────────────────────────────────────────────────

        private static string FilePath =>
            Path.Combine(ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder, "InquiryInbox", "config.json");

        public static InquiryInboxSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<InquiryInboxSettings>(File.ReadAllText(FilePath)) ?? new();
            }
            catch { /* fall through to defaults */ }
            return new();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    }));
            }
            catch { /* settings are best-effort */ }
        }
    }
}
