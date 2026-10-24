using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Inquiry;
using ProGlassAutomation.Services.Inquiry.InquiryInbox;
using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.ViewModels.Inquiry;

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
            var parsedManifest = InquiryInboxService.ParseManifest(manifestBytes);
            if (parsedManifest.RoutingKey != route || parsedManifest.Documents.Count != 1
                || parsedManifest.SubmissionRaw.Contains("submission_reference", StringComparison.Ordinal) == false)
                throw new InvalidOperationException("The stored delivery manifest is not valid JSON or lost its routing/submission data.");
            var legacyManifest = Encoding.UTF8.GetString(manifestBytes)
                .Replace("\"key\": \"unassigned\"", "\"unassigned\"", StringComparison.Ordinal);
            var legacyParsed = InquiryInboxService.ParseManifest(Encoding.UTF8.GetBytes(legacyManifest));
            if (legacyParsed.RoutingKey != route)
                throw new InvalidOperationException("The pre-fix manifest compatibility repair failed.");
            var ready = File.ReadAllText(Path.Combine(folder, "READY")).Trim();
            if (ready != manifestHash)
                throw new InvalidOperationException("READY does not contain the manifest-byte SHA-256.");
            var receipt = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "delivery.json"))).RootElement;
            if (receipt.GetProperty("manifest_sha256").GetString() != manifestHash
                || receipt.GetProperty("document_count").GetInt32() != 1
                || receipt.GetProperty("acked_by").GetString() != "operator@example.invalid"
                || string.IsNullOrWhiteSpace(receipt.GetProperty("acked_at_utc").GetString()))
                throw new InvalidOperationException("Delivery receipt is incomplete or does not match the acknowledged manifest.");
            using (var db = new InquiryDbContext())
            {
                var ledger = db.InboxItems.Single(i => i.SubmissionId == submissionId);
                if (ledger.AcknowledgedBy != "operator@example.invalid"
                    || ledger.AcknowledgedAtUtc != receipt.GetProperty("acked_at_utc").GetString())
                    throw new InvalidOperationException("The local ledger did not retain the acknowledgement identity and time.");
            }
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
        RunAckFailureDoesNotReack();
        RunImportFailureKeepsPayloadAndOffersReimport();
        RunAlreadyDeliveredConflict();
        RunStorageFailureThenPlanDisappearance();
        RunEmptyPlanIsNeutral();
    }

    private static (byte[] Bytes, string Sha, string SubmissionId, string Reference, string Snapshot) Fixture()
    {
        var bytes = Encoding.UTF8.GetBytes("fixture-document-content");
        return (bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Guid.NewGuid().ToString(), "WEB-2026-000001",
            "[{\"role\":\"primary\",\"estimatorCode\":\"EST-TEST\",\"displayName\":\"Test Estimator\"}]");
    }

    private static InquiryInboxSettings Settings(string inboxRoot) => new()
    {
        SupabaseUrl = "https://unit.invalid",
        AnonKey = "public-test-key",
        BridgeEmail = "operator@example.invalid",
        EncryptedPassword = Convert.ToBase64String(new byte[] { 1 }),
        InboxRoot = inboxRoot
    };

    private static void RunAckFailureDoesNotReack()
    {
        var priorRoot = AppDataRoot.TestFolderOverride;
        var root = Path.Combine(Path.GetTempPath(), "ProGlass-InboxAckFailure-" + Guid.NewGuid().ToString("N"));
        var inboxRoot = Path.Combine(root, "files");
        var fixture = Fixture();
        AppDataRoot.TestFolderOverride = Path.Combine(root, "db");
        var handler = new DeliveryHandler(fixture.Bytes, fixture.Sha, fixture.SubmissionId,
            fixture.Reference, "unassigned", fixture.Snapshot, inboxRoot, HttpStatusCode.InternalServerError);
        try
        {
            new InquiryRepository().InitializeAsync().GetAwaiter().GetResult();
            InquiryInboxClient.SetHttpHandlerFactoryForTests(() => handler);
            using var service = new InquiryInboxService(Settings(inboxRoot));
            var failedAck = service.SynchronizeAsync().GetAwaiter().GetResult();
            if (failedAck.Failed != 1 || handler.AckCalls != 1 || handler.DownloadCalls != 1)
                throw new InvalidOperationException("An acknowledgement failure did not stop at one fetch/ack attempt.");
            using (var db = new InquiryDbContext())
            {
                var item = db.InboxItems.Single(i => i.SubmissionId == fixture.SubmissionId);
                if (item.State != InquiryInboxStates.ImportPending)
                    throw new InvalidOperationException("A placed payload after ack failure was not marked ImportPending.");
            }
            var folder = InquiryInboxService.FolderPath(inboxRoot, "unassigned", fixture.Reference, fixture.SubmissionId);
            if (!File.Exists(Path.Combine(folder, "READY")))
                throw new InvalidOperationException("The placed payload was deleted after acknowledgement failure.");
            service.SynchronizeAsync().GetAwaiter().GetResult();
            if (handler.AckCalls != 1 || handler.DownloadCalls != 1)
                throw new InvalidOperationException("A later check repeated the fetch or acknowledgement for ImportPending.");
        }
        finally
        {
            InquiryInboxClient.SetHttpHandlerFactoryForTests(null);
            AppDataRoot.TestFolderOverride = priorRoot;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void RunImportFailureKeepsPayloadAndOffersReimport()
    {
        var priorRoot = AppDataRoot.TestFolderOverride;
        var root = Path.Combine(Path.GetTempPath(), "ProGlass-InboxImportFailure-" + Guid.NewGuid().ToString("N"));
        var inboxRoot = Path.Combine(root, "files");
        var fixture = Fixture();
        AppDataRoot.TestFolderOverride = Path.Combine(root, "db");
        var repository = new InquiryRepository();
        var handler = new DeliveryHandler(fixture.Bytes, fixture.Sha, fixture.SubmissionId,
            fixture.Reference, "unassigned", fixture.Snapshot, inboxRoot);
        try
        {
            repository.InitializeAsync().GetAwaiter().GetResult();
            InquiryInboxClient.SetHttpHandlerFactoryForTests(() => handler);
            using var service = new InquiryInboxService(Settings(inboxRoot));
            var fetched = service.SynchronizeAsync().GetAwaiter().GetResult();
            if (fetched.Delivered != 1 || handler.AckCalls != 1)
                throw new InvalidOperationException("Import-failure fixture delivery did not complete.");

            var importer = new InquiryInboxIngestService(repository, new ThrowingWorkflow(),
                new InquiryAttachmentService(repository), service);
            var result = importer.IngestAsync(fixture.SubmissionId).GetAwaiter().GetResult();
            if (result.Success) throw new InvalidOperationException("The injected import failure unexpectedly succeeded.");
            var folder = InquiryInboxService.FolderPath(inboxRoot, "unassigned", fixture.Reference, fixture.SubmissionId);
            if (!File.Exists(Path.Combine(folder, "READY")))
                throw new InvalidOperationException("Import failure deleted the delivered payload.");
            var row = new InquiryInboxItemRow(service.GetItemRows().Single(i => i.SubmissionId == fixture.SubmissionId));
            if (row.State != "Import pending" || !row.CanIngest || row.IngestActionLabel != "Re-import from disk")
                throw new InvalidOperationException("Import failure did not offer a disk-only re-import.");

            service.SynchronizeAsync().GetAwaiter().GetResult();
            if (handler.AckCalls != 1 || handler.DownloadCalls != 1)
                throw new InvalidOperationException("An import failure triggered another fetch or acknowledgement.");
        }
        finally
        {
            InquiryInboxClient.SetHttpHandlerFactoryForTests(null);
            AppDataRoot.TestFolderOverride = priorRoot;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void RunAlreadyDeliveredConflict()
    {
        var priorRoot = AppDataRoot.TestFolderOverride;
        var root = Path.Combine(Path.GetTempPath(), "ProGlass-InboxAckConflict-" + Guid.NewGuid().ToString("N"));
        var inboxRoot = Path.Combine(root, "files");
        var fixture = Fixture();
        AppDataRoot.TestFolderOverride = Path.Combine(root, "db");
        var handler = new DeliveryHandler(fixture.Bytes, fixture.Sha, fixture.SubmissionId,
            fixture.Reference, "unassigned", fixture.Snapshot, inboxRoot, HttpStatusCode.Conflict)
        { AckErrorPayload = "{\"code\":\"23505\",\"message\":\"duplicate delivery\"}" };
        try
        {
            new InquiryRepository().InitializeAsync().GetAwaiter().GetResult();
            InquiryInboxClient.SetHttpHandlerFactoryForTests(() => handler);
            using var service = new InquiryInboxService(Settings(inboxRoot));
            service.SynchronizeAsync().GetAwaiter().GetResult();
            var item = service.GetItemRows().Single(i => i.SubmissionId == fixture.SubmissionId);
            var row = new InquiryInboxItemRow(item);
            row.SetLocalPayload(true);
            if (item.State != InquiryInboxStates.DeliveredElsewhere
                || !item.Error.Contains("delivered by another operator", StringComparison.OrdinalIgnoreCase)
                || row.State == "Failed" || !row.CanIngest || row.IngestActionLabel != "Re-import from disk")
                throw new InvalidOperationException("HTTP 23505 was not represented as DeliveredElsewhere with a disk-only import action.");
            service.SynchronizeAsync().GetAwaiter().GetResult();
            if (handler.AckCalls != 1 || handler.DownloadCalls != 1)
                throw new InvalidOperationException("The duplicate-delivery case retried the ack or download.");
        }
        finally
        {
            InquiryInboxClient.SetHttpHandlerFactoryForTests(null);
            AppDataRoot.TestFolderOverride = priorRoot;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void RunStorageFailureThenPlanDisappearance()
    {
        var priorRoot = AppDataRoot.TestFolderOverride;
        var root = Path.Combine(Path.GetTempPath(), "ProGlass-InboxPlanGone-" + Guid.NewGuid().ToString("N"));
        var inboxRoot = Path.Combine(root, "files");
        var fixture = Fixture();
        AppDataRoot.TestFolderOverride = Path.Combine(root, "db");
        var handler = new DeliveryHandler(fixture.Bytes, fixture.Sha, fixture.SubmissionId,
            fixture.Reference, "unassigned", fixture.Snapshot, inboxRoot,
            storageStatus: HttpStatusCode.Unauthorized);
        try
        {
            new InquiryRepository().InitializeAsync().GetAwaiter().GetResult();
            InquiryInboxClient.SetHttpHandlerFactoryForTests(() => handler);
            using var service = new InquiryInboxService(Settings(inboxRoot));
            service.SynchronizeAsync().GetAwaiter().GetResult();
            var failed = service.GetItemRows().Single(i => i.SubmissionId == fixture.SubmissionId);
            if (failed.State != InquiryInboxStates.Failed
                || !failed.Error.Contains("Storage", StringComparison.Ordinal)
                || !failed.Error.Contains("401", StringComparison.Ordinal)
                || handler.AckCalls != 0)
                throw new InvalidOperationException("Storage HTTP 401 was not reported without acknowledgement.");
            var incoming = Path.Combine(inboxRoot, "incoming");
            if (Directory.Exists(incoming) && Directory.EnumerateDirectories(incoming).Any())
                throw new InvalidOperationException("A failed storage fetch left its partial folder behind.");

            handler.EmptyPlan = true;
            service.SynchronizeAsync().GetAwaiter().GetResult();
            var reconciled = service.GetItemRows().Single(i => i.SubmissionId == fixture.SubmissionId);
            if (reconciled.State != InquiryInboxStates.DeliveredElsewhere
                || !reconciled.Error.Contains("Delivered to this PC on", StringComparison.Ordinal)
                || !reconciled.Error.Contains("Customer submissions -> open the submission -> Download", StringComparison.Ordinal)
                || !reconciled.Error.Contains(InquiryInboxSettings.DefaultInboxRoot, StringComparison.Ordinal)
                || handler.AckCalls != 0 || handler.DownloadCalls != 1)
                throw new InvalidOperationException("A failed local row omitted DeliveredElsewhere guidance after leaving the plan.");
        }
        finally
        {
            InquiryInboxClient.SetHttpHandlerFactoryForTests(null);
            AppDataRoot.TestFolderOverride = priorRoot;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void RunEmptyPlanIsNeutral()
    {
        var priorRoot = AppDataRoot.TestFolderOverride;
        var root = Path.Combine(Path.GetTempPath(), "ProGlass-InboxEmptyPlan-" + Guid.NewGuid().ToString("N"));
        var inboxRoot = Path.Combine(root, "files");
        AppDataRoot.TestFolderOverride = Path.Combine(root, "db");
        var repository = new InquiryRepository();
        var settings = Settings(inboxRoot);
        var handler = new DeliveryHandler(Array.Empty<byte>(),
            Convert.ToHexString(SHA256.HashData(Array.Empty<byte>())).ToLowerInvariant(),
            Guid.NewGuid().ToString(), "WEB-2026-000001", "unassigned", "[]", inboxRoot)
        { EmptyPlan = true };
        try
        {
            repository.InitializeAsync().GetAwaiter().GetResult();
            InquiryInboxClient.SetHttpHandlerFactoryForTests(() => handler);
            using var service = new InquiryInboxService(settings);
            var ingest = new InquiryInboxIngestService(repository, new ThrowingWorkflow(),
                new InquiryAttachmentService(repository), service);
            var viewModel = new InquiryInboxViewModel(service, ingest, new InquiryInboxNotifier());
            var before = service.GetItemRows().Count;
            Task.Run(() => viewModel.CheckNowAsync()).GetAwaiter().GetResult();
            var after = service.GetItemRows().Count;
            const string expected = "No submissions are waiting. The website hands a submission to this PC only after a ProGlass operator accepts it; everything accepted and not yet delivered appears here.";
            if (viewModel.StatusText != expected || viewModel.FailedCount != 0
                || service.Settings.LastSyncStatus != "OK" || before != after || after != 0
                || handler.AckCalls != 0 || handler.DownloadCalls != 0
                || !viewModel.CheckNowCommand.CanExecute(null))
                throw new InvalidOperationException("An empty successful plan was not neutral, unchanged, and retryable.");
            viewModel.Dispose();
        }
        finally
        {
            InquiryInboxClient.SetHttpHandlerFactoryForTests(null);
            AppDataRoot.TestFolderOverride = priorRoot;
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private sealed class ThrowingWorkflow : IInquiryWorkflowService
    {
        public Task<InquiryRecord> CreateAsync(InquiryRecord inquiry, CancellationToken ct = default) =>
            Task.FromException<InquiryRecord>(new InvalidOperationException("fixture import failed"));
        public Task<InquiryRecord> UpdateAsync(InquiryRecord inquiry, int expectedRowVersion, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ChangeStatusAsync(int id, string current, string next, int expectedRowVersion, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<InquiryRecord> DuplicateAsync(int sourceId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DeleteInquiryResult> DeleteAsync(int id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> SetPriorityAsync(int id, string priority, CancellationToken ct = default) => throw new NotImplementedException();
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
        private readonly HttpStatusCode _ackStatus;
        private readonly HttpStatusCode _storageStatus;
        public bool EmptyPlan { get; set; }
        public string AckErrorPayload { get; set; } = "{\"message\":\"fixture ack failure\"}";
        public int AckCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public bool AckSawReadyPayload { get; private set; }
        public bool AckSawReceipt { get; private set; }
        public bool AckArgumentsMatched { get; private set; }

        public DeliveryHandler(byte[] bytes, string sha, string submissionId, string reference,
            string route, string snapshot, string root, HttpStatusCode ackStatus = HttpStatusCode.OK,
            HttpStatusCode storageStatus = HttpStatusCode.OK)
        {
            _bytes = bytes;
            _sha = sha;
            _submissionId = submissionId;
            _reference = reference;
            _route = route;
            _snapshot = snapshot;
            _root = root;
            _ackStatus = ackStatus;
            _storageStatus = storageStatus;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/auth/v1/token", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "{\"access_token\":\"fixture-token\",\"expires_in\":3600}");
            if (path.EndsWith("/get_my_inquiry_access_v1", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "{\"account_email\":\"operator@example.invalid\",\"roles\":[\"inquiry_bridge\"],\"capabilities\":[\"inbox.plan.bridge\"]}");
            if (path.EndsWith("/plan_customer_inquiry_deliveries_v2", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, EmptyPlan ? "[]" : "[{\"submission_id\":\"" + _submissionId + "\",\"submission_reference\":\"" + _reference + "\",\"submitted_at\":\"2026-10-24T08:00:00Z\",\"status\":\"accepted\",\"company_name\":\"Fixture Co\",\"project_name\":\"Fixture Project\",\"document_count\":1,\"primary_estimator_code\":\"EST-TEST\",\"assignment_snapshot\":" + _snapshot + ",\"routing_key\":\"" + _route + "\"}]");
            if (path.EndsWith("/plan_customer_inquiry_delivery_documents", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "[{\"document_id\":\"doc-1\",\"original_filename\":\"drawing.pdf\",\"detected_mime\":\"application/pdf\",\"byte_size\":" + _bytes.LongLength + ",\"content_sha256\":\"" + _sha + "\",\"storage_bucket\":\"inquiry-intake-documents\",\"storage_object_key\":\"fixture/drawing.pdf\"}]");
            if (path.Contains("/storage/v1/object/inquiry-intake-documents/", StringComparison.Ordinal))
            {
                DownloadCalls++;
                return _storageStatus == HttpStatusCode.OK
                    ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_bytes) })
                    : Json(_storageStatus, "{\"message\":\"fixture storage refusal\"}");
            }
            if (path.EndsWith("/acknowledge_customer_inquiry_delivery_v2", StringComparison.Ordinal))
            {
                AckCalls++;
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
                return Json(_ackStatus, _ackStatus == HttpStatusCode.OK ? "{}" : AckErrorPayload);
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
