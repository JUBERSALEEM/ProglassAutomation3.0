using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Inquiry;
using ProGlassAutomation.Services.Inquiry.InquiryInbox;

namespace ProGlassAutomation.ThemeTests;

/// <summary>Network-free ordering check: the READY payload exists before ack and receipt after ack.</summary>
internal static class InquiryInboxDeliveryOrderingChecks
{
    public static void Run()
    {
        var priorRoot = AppDataRoot.TestFolderOverride;
        var root = Path.Combine(Path.GetTempPath(), "ProGlass-InboxOrdering-" + Guid.NewGuid().ToString("N"));
        var dbRoot = Path.Combine(root, "db");
        var inboxRoot = Path.Combine(root, "files");
        var bytes = Encoding.UTF8.GetBytes("fixture-document-content");
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var submissionId = Guid.NewGuid().ToString();
        var reference = "WEB-2026-000001";
        var route = "unassigned";
        var snapshot = "[{\"role\":\"primary\",\"estimatorCode\":\"EST-TEST\",\"displayName\":\"Test Estimator\"}]";
        var handler = new DeliveryHandler(bytes, sha, submissionId, reference, route, snapshot, inboxRoot);
        AppDataRoot.TestFolderOverride = dbRoot;

        try
        {
            new InquiryRepository().InitializeAsync().GetAwaiter().GetResult();
            var settings = new InquiryInboxSettings
            {
                SupabaseUrl = "https://unit.invalid",
                AnonKey = "public-test-key",
                BridgeEmail = "operator@example.invalid",
                EncryptedPassword = Convert.ToBase64String(new byte[] { 1 }),
                InboxRoot = inboxRoot
            };
            InquiryInboxClient.SetHttpHandlerFactoryForTests(() => handler);
            using var service = new InquiryInboxService(settings);
            var outcome = service.SynchronizeAsync().GetAwaiter().GetResult();
            if (outcome.Failed != 0 || outcome.Delivered != 1)
                throw new InvalidOperationException("Fake delivery did not complete: " + outcome.StatusText + " " + outcome.Error);
            if (!handler.AckSawReadyPayload || handler.AckSawReceipt)
                throw new InvalidOperationException("Acknowledgement did not occur strictly after placement and before delivery receipt.");
            if (!handler.AckArgumentsMatched)
                throw new InvalidOperationException("Acknowledgement arguments did not match the prepared manifest and plan.");

            var folder = InquiryInboxService.FolderPath(inboxRoot, route, reference, submissionId);
            var manifestBytes = File.ReadAllBytes(Path.Combine(folder, "manifest.json"));
            var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
            var ready = File.ReadAllText(Path.Combine(folder, "READY")).Trim();
            if (ready != manifestHash)
                throw new InvalidOperationException("READY does not contain the manifest-byte SHA-256.");
            var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "delivery.json"))).RootElement;
            if (receipt.GetProperty("manifest_sha256").GetString() != manifestHash
                || receipt.GetProperty("document_count").GetInt32() != 1
                || receipt.GetProperty("acked_by").GetString() != "operator@example.invalid"
                || string.IsNullOrWhiteSpace(receipt.GetProperty("acked_at_utc").GetString()))
                throw new InvalidOperationException("Delivery receipt is incomplete or does not match the acknowledged manifest.");
            var incoming = Path.Combine(inboxRoot, "incoming");
            if (Directory.Exists(incoming) && Directory.EnumerateDirectories(incoming).Any())
                throw new InvalidOperationException("A successful fetch left a partial folder behind.");
        }
        finally
        {
            InquiryInboxClient.SetHttpHandlerFactoryForTests(null);
            AppDataRoot.TestFolderOverride = priorRoot;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private sealed class DeliveryHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        private readonly string _sha;
        private readonly string _submissionId;
        private readonly string _reference;
        private readonly string _route;
        private readonly string _snapshot;
        private readonly string _root;
        public bool AckSawReadyPayload { get; private set; }
        public bool AckSawReceipt { get; private set; }
        public bool AckArgumentsMatched { get; private set; }

        public DeliveryHandler(byte[] bytes, string sha, string submissionId, string reference,
            string route, string snapshot, string root)
        {
            _bytes = bytes;
            _sha = sha;
            _submissionId = submissionId;
            _reference = reference;
            _route = route;
            _snapshot = snapshot;
            _root = root;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/auth/v1/token", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "{\"access_token\":\"fixture-token\",\"expires_in\":3600}");
            if (path.EndsWith("/get_my_inquiry_access_v1", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "{\"account_email\":\"operator@example.invalid\",\"roles\":[\"inquiry_bridge\"],\"capabilities\":[\"inbox.plan.bridge\"]}");
            if (path.EndsWith("/plan_customer_inquiry_deliveries_v2", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "[{\"submission_id\":\"" + _submissionId + "\",\"submission_reference\":\"" + _reference + "\",\"submitted_at\":\"2026-10-24T08:00:00Z\",\"status\":\"accepted\",\"company_name\":\"Fixture Co\",\"project_name\":\"Fixture Project\",\"document_count\":1,\"primary_estimator_code\":\"EST-TEST\",\"assignment_snapshot\":" + _snapshot + ",\"routing_key\":\"" + _route + "\"}]");
            if (path.EndsWith("/plan_customer_inquiry_delivery_documents", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "[{\"document_id\":\"doc-1\",\"original_filename\":\"drawing.pdf\",\"detected_mime\":\"application/pdf\",\"byte_size\":" + _bytes.LongLength + ",\"content_sha256\":\"" + _sha + "\",\"storage_bucket\":\"inquiry-intake-documents\",\"storage_object_key\":\"fixture/drawing.pdf\"}]");
            if (path.Contains("/storage/v1/object/inquiry-intake-documents/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_bytes) });
            if (path.EndsWith("/acknowledge_customer_inquiry_delivery_v2", StringComparison.Ordinal))
            {
                var folder = InquiryInboxService.FolderPath(_root, _route, _reference, _submissionId);
                AckSawReadyPayload = File.Exists(Path.Combine(folder, "READY"))
                    && File.Exists(Path.Combine(folder, "manifest.json"))
                    && File.Exists(Path.Combine(folder, "documents", "doc-1_drawing.pdf"));
                AckSawReceipt = File.Exists(Path.Combine(folder, "delivery.json"));
                using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                var root = body.RootElement;
                var actualSnapshot = root.GetProperty("p_assignment_snapshot").GetRawText();
                AckArgumentsMatched = root.GetProperty("p_document_count").GetInt32() == 1
                    && root.GetProperty("p_routing_key").GetString() == _route
                    && actualSnapshot == _snapshot
                    && root.GetProperty("p_manifest_sha256").GetString() == Convert.ToHexString(
                        SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, "manifest.json")))).ToLowerInvariant();
                return Json(HttpStatusCode.OK, "{}");
            }
            return Json(HttpStatusCode.NotFound, "{\"message\":\"unhandled fixture request\"}");
        }

        private static Task<HttpResponseMessage> Json(HttpStatusCode status, string json) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}
