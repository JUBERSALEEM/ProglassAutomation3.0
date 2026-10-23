// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxClient.cs
// MODULE: Inquiry — website inbox remote client (Supabase REST).
//
// C# port of the Node bridge's remote calls
// (website/scripts/inquiry-inbox-bridge.mjs). The wire contract is the
// same as @supabase/supabase-js v2:
//
//   sign-in   POST {url}/auth/v1/token?grant_type=password
//             body {"email","password"}, header apikey
//   rpc       POST {url}/rest/v1/rpc/{name}
//             header apikey + Authorization Bearer + Content-Profile:public
//   download  GET  {url}/storage/v1/object/{bucket}/{objectKey}
//             header Authorization Bearer
//
// Failure kinds are separated so the service can stay NEUTRAL (not an
// error state) when the website backend is simply unreachable — the
// desktop must work fully local offline.
// ═══════════════════════════════════════════════════════════════════════

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ProGlassAutomation.Services.Inquiry.InquiryInbox
{
    public enum InboxRemoteErrorKind
    {
        /// <summary>Network unreachable / timeout — treat as "offline", never an error.</summary>
        Offline,
        /// <summary>Credentials rejected by the website backend.</summary>
        Auth,
        /// <summary>Backend reachable but the RPC/role/config is wrong.</summary>
        Remote
    }

    public sealed class InboxRemoteException : Exception
    {
        public InboxRemoteErrorKind Kind { get; }
        /// <summary>HTTP status of the failing request, when there was one.</summary>
        public int? HttpStatusCode { get; }
        public InboxRemoteException(InboxRemoteErrorKind kind, string message, int? httpStatus = null)
            : base(message)
        {
            Kind = kind;
            HttpStatusCode = httpStatus;
        }
    }

    public sealed class InboxSignInResult
    {
        public bool Success { get; private init; }
        public string? Error { get; private init; }
        public static InboxSignInResult Ok() => new() { Success = true };
        public static InboxSignInResult Fail(string error) => new() { Success = false, Error = error };
    }

    public sealed class InquiryInboxClient : IDisposable
    {
        private readonly InquiryInboxSettings _settings;
        private readonly HttpClient _http;
        private string? _accessToken;
        private DateTime _tokenExpiresUtc = DateTime.MinValue;
        private bool _disposed;

        public InquiryInboxClient(InquiryInboxSettings settings)
        {
            _settings = settings;
            _http = new HttpClient
            {
                // Downloads can be 30 MB; plan/ack are instant.
                Timeout = TimeSpan.FromSeconds(120)
            };
        }

        private string Url => TrimEnd(_settings.SupabaseUrl);
        private static string TrimEnd(string url)
        {
            var u = url.Trim();
            while (u.EndsWith('/')) u = u[..^1];
            return u;
        }

        // ── Auth ──────────────────────────────────────────────────────────

        /// <summary>Signs in the bridge service account. Offline-safe (returns a result).</summary>
        public async Task<InboxSignInResult> SignInAsync(CancellationToken ct = default)
        {
            var body = JsonSerializer.Serialize(new { email = _settings.BridgeEmail, password = _settings.GetPassword() });
            var (status, payload, _) = await PostAsync(
                Url + "/auth/v1/token?grant_type=password",
                body,
                withBearer: false,
                ct).ConfigureAwait(false);

            if (status == (int)HttpStatusCode.BadRequest || status == 401 || status == 403)
                return InboxSignInResult.Fail(DescribeAuth(status, payload));

            if (status != 200)
                return InboxSignInResult.Fail($"Sign-in failed (HTTP {status}).");

            if (string.IsNullOrWhiteSpace(payload))
                return InboxSignInResult.Fail("Sign-in returned an empty response.");
            using var doc = JsonDocument.Parse(payload);
            var token = doc.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(token))
                return InboxSignInResult.Fail("Sign-in did not return an access token.");

            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) && e.ValueKind == JsonValueKind.Number
                ? e.GetInt32()
                : 3600;
            _accessToken = token;
            _tokenExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn - 60));
            return InboxSignInResult.Ok();
        }

        private static string DescribeAuth(int status, string payload)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var msg = doc.RootElement.TryGetProperty("msg", out var m) ? m.GetString()
                    : doc.RootElement.TryGetProperty("error_description", out var d) ? d.GetString() : null;
                if (!string.IsNullOrEmpty(msg)) return msg;
            }
            catch { }
            return status == 403 ? "The bridge account does not have access (role missing?)" : "Invalid email or password.";
        }

        private async Task EnsureTokenAsync(CancellationToken ct)
        {
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiresUtc) return;
            var result = await SignInAsync(ct).ConfigureAwait(false);
            if (!result.Success)
                throw new InboxRemoteException(InboxRemoteErrorKind.Auth, result.Error ?? "Sign-in failed.");
        }

        // ── RPCs ──────────────────────────────────────────────────────────

        /// <summary>plan_customer_inquiry_deliveries_v2 — rows as raw JSON objects.</summary>
        public async Task<List<(InboxSubmissionPlan Plan, string RawJson)>> PlanDeliveriesAsync(int limit, CancellationToken ct)
        {
            var body = JsonSerializer.Serialize(new { p_limit = limit });
            var (status, payload, kind) = await PostRpcAsync("plan_customer_inquiry_deliveries_v2", body, ct).ConfigureAwait(false);
            if (status != 200) throw new InboxRemoteException(kind, $"Inbox plan failed: {ExtractError(payload)} (HTTP {status}).", (int?)status);

            var result = new List<(InboxSubmissionPlan, string)>();
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                var raw = row.GetRawText();
                var plan = JsonSerializer.Deserialize<InboxSubmissionPlan>(raw,
                    JsonOpts) ?? throw new InboxRemoteException(InboxRemoteErrorKind.Remote, "Malformed inbox plan row.");
                result.Add((plan, raw));
            }
            return result;
        }

        /// <summary>plan_customer_inquiry_delivery_documents for one submission.</summary>
        public async Task<List<InboxDocumentPlan>> PlanDocumentsAsync(string submissionId, CancellationToken ct)
        {
            var body = JsonSerializer.Serialize(new { p_submission_id = submissionId });
            var (status, payload, kind) = await PostRpcAsync("plan_customer_inquiry_delivery_documents", body, ct).ConfigureAwait(false);
            if (status != 200) throw new InboxRemoteException(kind, $"Document plan failed: {ExtractError(payload)} (HTTP {status}).", (int?)status);

            var result = new List<InboxDocumentPlan>();
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var row in doc.RootElement.EnumerateArray())
                result.Add(JsonSerializer.Deserialize<InboxDocumentPlan>(row.GetRawText(), JsonOpts)
                    ?? throw new InboxRemoteException(InboxRemoteErrorKind.Remote, "Malformed document plan row."));
            return result;
        }

        /// <summary>acknowledge_customer_inquiry_delivery_v2.</summary>
        public async Task AcknowledgeAsync(string submissionId, string manifestSha256, int documentCount,
            string routingKey, string assignmentSnapshotJson, CancellationToken ct)
        {
            var body = JsonSerializer.Serialize(new
            {
                p_submission_id = submissionId,
                p_manifest_sha256 = manifestSha256,
                p_document_count = documentCount,
                p_routing_key = routingKey,
                p_assignment_snapshot = JsonSerializer.Deserialize<object>(assignmentSnapshotJson)
            }, JsonOpts);
            var (status, payload, kind) = await PostRpcAsync("acknowledge_customer_inquiry_delivery_v2", body, ct).ConfigureAwait(false);
            if (status != 200) throw new InboxRemoteException(kind, $"Acknowledgement failed: {ExtractError(payload)} (HTTP {status}).", (int?)status);
        }

        /// <summary>
        /// update_customer_inquiry_status_v1 — optional pipeline status
        /// write-back (contract + deploy SQL in
        /// docs/07.Website-Integration-Contracts.md).
        /// </summary>
        public async Task UpdateInquiryStatusAsync(string submissionId, string status, string note, CancellationToken ct)
        {
            var body = JsonSerializer.Serialize(new
            {
                p_submission_id = submissionId,
                p_status = status,
                p_note = note
            }, JsonOpts);
            var (status2, payload, kind) = await PostRpcAsync("update_customer_inquiry_status_v1", body, ct).ConfigureAwait(false);
            if (status2 != 200) throw new InboxRemoteException(kind, $"Status update failed: {ExtractError(payload)} (HTTP {status2}).", (int?)status2);
        }

        /// <summary>
        /// list_customer_inquiry_communications_v1 — read-only pull of
        /// the per-inquiry website chat channel (table
        /// public.inquiry_communications). The website-side function is
        /// deployed from docs/07.Website-Integration-Contracts.md; a
        /// 404 (function not deployed yet) is reported through
        /// HttpStatusCode so the service can stay neutral.
        /// </summary>
        public async Task<List<InquiryWebsiteMessage>> ListInquiryCommunicationsAsync(string submissionId, CancellationToken ct)
        {
            var body = JsonSerializer.Serialize(new { p_submission_id = submissionId });
            var (status, payload, kind) = await PostRpcAsync("list_customer_inquiry_communications_v1", body, ct).ConfigureAwait(false);
            if (status != 200) throw new InboxRemoteException(kind, $"Website chat read failed: {ExtractError(payload)} (HTTP {status}).", (int?)status);

            var result = new List<InquiryWebsiteMessage>();
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var row in doc.RootElement.EnumerateArray())
                result.Add(JsonSerializer.Deserialize<InquiryWebsiteMessage>(row.GetRawText(), JsonOpts)
                    ?? throw new InboxRemoteException(InboxRemoteErrorKind.Remote, "Malformed website chat row."));
            return result;
        }

        /// <summary>Storage object download (raw bytes).</summary>
        public async Task<byte[]> DownloadObjectAsync(string bucket, string objectKey, CancellationToken ct)
        {
            await EnsureTokenAsync(ct).ConfigureAwait(false);
            var url = Url + "/storage/v1/object/" + bucket.Trim('/') + "/" + objectKey.TrimStart('/');
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var text = await SafeReadTextAsync(response).ConfigureAwait(false);
                    throw new InboxRemoteException(InboxRemoteErrorKind.Remote,
                        $"Download failed (HTTP {(int)response.StatusCode}): {ExtractError(text)}",
                        (int?)response.StatusCode);
                }
                return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new InboxRemoteException(InboxRemoteErrorKind.Offline, ex.Message);
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested == false)
            {
                throw new InboxRemoteException(InboxRemoteErrorKind.Offline, "Download timed out.");
            }
        }

        // ── HTTP plumbing ─────────────────────────────────────────────────

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        private async Task<(int Status, string Payload, InboxRemoteErrorKind Kind)> PostAsync(
            string url, string body, bool withBearer, CancellationToken ct)
        {
            if (withBearer) await EnsureTokenAsync(ct).ConfigureAwait(false);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                request.Headers.Add("apikey", _settings.AnonKey);
                if (withBearer)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
                    request.Headers.Add("Content-Profile", "public");
                }
                using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
                var payload = await SafeReadTextAsync(response).ConfigureAwait(false);
                var kind = Classify((int)response.StatusCode);
                return ((int)response.StatusCode, payload, kind);
            }
            catch (HttpRequestException ex)
            {
                throw new InboxRemoteException(InboxRemoteErrorKind.Offline, ex.Message);
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested == false)
            {
                throw new InboxRemoteException(InboxRemoteErrorKind.Offline, "Request timed out.");
            }
        }

        private async Task<(int, string, InboxRemoteErrorKind)> PostRpcAsync(string name, string body, CancellationToken ct)
        {
            return await PostAsync(Url + "/rest/v1/rpc/" + name, body, withBearer: true, ct).ConfigureAwait(false);
        }

        private static InboxRemoteErrorKind Classify(int status) =>
            status is 401 or 403 ? InboxRemoteErrorKind.Auth : InboxRemoteErrorKind.Remote;

        private static async Task<string> SafeReadTextAsync(HttpResponseMessage response)
        {
            try { return await response.Content.ReadAsStringAsync().ConfigureAwait(false); }
            catch { return ""; }
        }

        private static string ExtractError(string payload)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                var msg = root.TryGetProperty("message", out var m) ? m.GetString()
                    : root.TryGetProperty("msg", out var mm) ? mm.GetString()
                    : root.TryGetProperty("error_description", out var d) ? d.GetString()
                    : root.TryGetProperty("error", out var e) ? e.GetString()
                    : null;
                if (!string.IsNullOrEmpty(msg)) return msg;
            }
            catch { }
            return payload.Length > 160 ? payload[..160] + "…" : payload;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
        }
    }
}
