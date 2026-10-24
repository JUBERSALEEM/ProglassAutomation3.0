// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryInbox/InquiryInboxService.cs
// MODULE: Inquiry — website inbox sync (cloud → isolated Windows inbox).
//
// C# port of the Node bridge's apply loop with desktop adaptations:
//   * per-item resilience (one failed ack does not stop the batch),
//   * a local SQLite ledger (InquiryInboxItems) so the UI can show
//     state and ingest is idempotent,
//   * offline = neutral: an unreachable backend is "offline", never an
//     error — the desktop is fully local until the network returns.
//
// Folder contract (identical to the bridge, so WPF + Node interoperate
// on the same root):
//   {root}/{unassigned|assigned/{CODE}}/{safe(reference)}_{submissionId}/
//       manifest.json   (canonical v2, hash travels to the ack RPC)
//       READY           (manifest sha256 + \n)
//       documents/{documentId}_{safe(filename)}
// ═══════════════════════════════════════════════════════════════════════

using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Services.Database;

namespace ProGlassAutomation.Services.Inquiry.InquiryInbox
{
    public sealed class InboxSummary
    {
        public bool Configured { get; init; }
        public bool Offline { get; init; }
        public int DownloadedCount { get; init; }
        public int IngestedCount { get; init; }
        public int FailedCount { get; init; }
        public int ReadyCount => DownloadedCount + IngestedCount;
        public string LastSyncAtUtc { get; init; } = "";
        public string LastSyncStatus { get; init; } = "";
        public DateTime? LastSyncAtUtcParsed { get; init; }
    }

    public sealed class InboxSyncOutcome
    {
        // Mutable by design: the sync method builds this result
        // incrementally (counts accumulate, status text is refined,
        // errors are recorded per phase), so the working set uses
        // ordinary setters; Ran/Configured are set once at creation.
        public bool Ran { get; init; }
        public bool Configured { get; init; } = true;
        public bool Offline { get; set; }
        public bool AccessDenied { get; set; }
        public bool Busy { get; init; }
        public int Planned { get; set; }
        public int Delivered { get; set; }
        public int Verified { get; set; }
        public int Failed { get; set; }
        public int ImportPending { get; set; }
        public int DeliveredElsewhere { get; set; }
        /// <summary>References newly seen this sync (drives notifications).</summary>
        public string[] NewReferences { get; set; } = Array.Empty<string>();
        public string[] NewCompanyNames { get; set; } = Array.Empty<string>();
        public string[] NewEstimatorNames { get; set; } = Array.Empty<string>();
        public string StatusText { get; set; } = "";
        public string? Error { get; set; }
    }

    public sealed class InquiryInboxService : IDisposable
    {
        private readonly InquiryInboxSettings _settings;
        private readonly InquiryInboxClient _client;
        private CancellationTokenSource? _running;
        private Task<InboxSyncOutcome>? _runningTask;
        private readonly object _gate = new();
        private bool _disposed;

        public InquiryInboxAccessReport? LastAccessReport { get; private set; }

        public InquiryInboxService(InquiryInboxSettings settings)
        {
            _settings = settings;
            _client = new InquiryInboxClient(settings);
        }

        public InquiryInboxSettings Settings => _settings;

        /// <summary>Raised after each sync/scan completes (service thread — marshal in the VM).</summary>
        public event EventHandler? StateChanged;

        // ── Guards & paths ───────────────────────────────────────────────

        private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = InboxJson.Options;

        private string Root =>
            string.IsNullOrWhiteSpace(_settings.InboxRoot)
                ? InquiryInboxSettings.DefaultInboxRoot
                : _settings.InboxRoot.Trim();

        /// <summary>
        /// Validates the inbox root against the bridge's isolation rules.
        /// Returns null when valid, otherwise the problem.
        /// </summary>
        public static string? ValidateRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return "Inbox folder is empty.";
            try
            {
                var full = Path.GetFullPath(root);
                if (string.Equals(full.TrimEnd('\\'),
                        ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder.TrimEnd('\\'),
                        StringComparison.OrdinalIgnoreCase))
                    return "Inbox must be isolated from the authoritative ProGlass data root.";
                if (Directory.Exists(full) && new DirectoryInfo(full).LinkTarget != null)
                    return "Inbox root must not be a symbolic link.";
            }
            catch (Exception ex) { return "Inbox folder is invalid: " + ex.Message; }
            return null;
        }

        public static string FolderPath(string root, string routingKey, string reference, string submissionId)
        {
            var route = (routingKey ?? "unassigned").Replace('\\', '/').Trim('/');
            var folder = InboxFileNames.Safe(reference) + "_" + submissionId;
            // Path.Combine has no (string, string[]) overload — combine
            // the array in one call instead.
            var routeParts = route.Split('/').Where(s => s.Length > 0).ToArray();
            var full = routeParts.Length > 0
                ? Path.Combine(new string[] { root }.Concat(routeParts).ToArray()).Replace('\\', Path.DirectorySeparatorChar)
                : root;
            return Path.Combine(full, folder);
        }

        // ── Synchronize ──────────────────────────────────────────────────

        public Task<InboxSyncOutcome> SynchronizeAsync(CancellationToken ct = default)
        {
            CancellationTokenSource linked;
            TaskCompletionSource<InboxSyncOutcome> completion;
            lock (_gate)
            {
                if (_disposed)
                    return Task.FromResult(NeutralStoppedOutcome());
                if (_running != null)
                    return Task.FromResult(new InboxSyncOutcome { Busy = true, StatusText = "Already checking the website." });

                linked = _running = CancellationTokenSource.CreateLinkedTokenSource(ct);
                completion = new TaskCompletionSource<InboxSyncOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
                _runningTask = completion.Task;
            }

            _ = CompleteSynchronizationAsync(linked, completion);
            return completion.Task;
        }

        private async Task CompleteSynchronizationAsync(
            CancellationTokenSource linked, TaskCompletionSource<InboxSyncOutcome> completion)
        {
            InboxSyncOutcome result = NeutralStoppedOutcome();
            Exception? failure = null;
            try
            {
                result = await SynchronizeCoreAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                result = NeutralStoppedOutcome();
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_running, linked))
                    {
                        _running = null;
                        _runningTask = null;
                    }
                }
                linked.Dispose();
            }

            // Complete only after cleanup so Dispose truly waits until the
            // running sync no longer uses the shared HttpClient.
            if (failure != null) completion.TrySetException(failure);
            else completion.TrySetResult(result);
        }

        private static InboxSyncOutcome NeutralStoppedOutcome() => new()
        {
            Ran = false,
            Offline = false,
            StatusText = "Inbox check stopped.",
            Error = null
        };

        public void CancelSynchronization()
        {
            CancellationTokenSource? running;
            lock (_gate) running = _disposed ? null : _running;
            try { running?.Cancel(); }
            catch (ObjectDisposedException) { /* the sync completed between capture and cancel */ }
        }

        private static InboxRemoteException PlanAccessDenied(string details, int? status) =>
            new(InboxRemoteErrorKind.PermissionDenied,
                "This account may not run the desktop inbox. " + details, status);

        private async Task<InboxSyncOutcome> SynchronizeCoreAsync(CancellationToken ct)
        {
            if (!_settings.IsConfigured)
            {
                _settings.LastSyncStatus = "Not configured";
                _settings.Save();
                return new InboxSyncOutcome { Configured = false, StatusText = "Website connection is not configured yet." };
            }

            var rootProblem = ValidateRoot(Root);
            if (rootProblem != null)
            {
                _settings.LastSyncStatus = rootProblem;
                _settings.Save();
                return new InboxSyncOutcome { Ran = true, Error = rootProblem, StatusText = rootProblem };
            }

            var outcome = new InboxSyncOutcome { Ran = true };
            try
            {
                Directory.CreateDirectory(Root);

                LastAccessReport = null;
                var signIn = await _client.SignInAsync(ct).ConfigureAwait(false);
                if (!signIn.Success)
                {
                    outcome.Error = signIn.Error;
                    outcome.StatusText = "Website sign-in failed: " + (signIn.Error ?? "unknown error");
                    _settings.LastSyncStatus = "Sign-in failed";
                    _settings.Save();
                    return outcome;
                }

                var accessReport = await _client.GetMyInquiryAccessAsync(ct).ConfigureAwait(false);
                LastAccessReport = accessReport;
                if (!accessReport.CanPlanBridge)
                {
                    throw PlanAccessDenied(accessReport.MissingCapabilityMessage(), 403);
                }

                List<(InboxSubmissionPlan Plan, string RawJson)> planned;
                try
                {
                    planned = await _client.PlanDeliveriesAsync(100, ct).ConfigureAwait(false);
                }
                catch (InboxRemoteException ex) when (ex.Kind == InboxRemoteErrorKind.PermissionDenied)
                {
                    throw PlanAccessDenied(ex.Message, ex.HttpStatusCode);
                }
                outcome.Planned = planned.Count;

                using var db = new InquiryDbContext();
                var plannedIds = new HashSet<string>(planned.Select(x => x.Plan.SubmissionId), StringComparer.Ordinal);
                ReconcileUnofferedItems(db, plannedIds);
                var known = new HashSet<string>(
                    db.InboxItems.AsNoTracking().Select(i => i.SubmissionId), StringComparer.Ordinal);

                foreach (var (plan, rawJson) in planned)
                {
                    ct.ThrowIfCancellationRequested();
                    var isNew = !known.Contains(plan.SubmissionId);
                    try
                    {
                        var existingItem = db.InboxItems.AsNoTracking()
                            .FirstOrDefault(i => i.SubmissionId == plan.SubmissionId);
                        if (existingItem?.State == InquiryInboxStates.ImportPending
                            || existingItem?.State == InquiryInboxStates.DeliveredElsewhere)
                        {
                            // Preserve the local evidence and never fetch or acknowledge again.
                            continue;
                        }

                        List<InboxDocumentPlan> docs;
                        try
                        {
                            docs = await _client.PlanDocumentsAsync(plan.SubmissionId, ct).ConfigureAwait(false);
                        }
                        catch (InboxRemoteException ex) when (ex.Kind == InboxRemoteErrorKind.PermissionDenied)
                        {
                            throw PlanAccessDenied(ex.Message, ex.HttpStatusCode);
                        }
                        if (docs.Count != plan.DocumentCount)
                            throw new InboxRemoteException(InboxRemoteErrorKind.Remote,
                                $"Remote document count changed ({docs.Count} vs {plan.DocumentCount}).");
                        if (!InboxRouting.IsValid(plan.RoutingKey))
                            throw new InboxRemoteException(InboxRemoteErrorKind.Remote,
                                "Invalid inbox routing key for " + plan.SubmissionReference + ".");

                        var delivered = await DeliverAsync(plan, rawJson, docs, ct).ConfigureAwait(false);
                        var deliveryFolder = FolderPath(Root, plan.RoutingKey, plan.SubmissionReference, plan.SubmissionId);
                        var receipt = ReadDeliveryReceiptIfPresent(deliveryFolder, plan, docs.Count);
                        if (delivered == ItemDeliveryResult.Verified && receipt == null)
                        {
                            outcome.ImportPending++;
                            UpsertState(db, plan, docs.Count, ItemState.ImportPending,
                                "The verified local folder has no delivery receipt. The acknowledgement was not repeated; re-import from disk.");
                            continue;
                        }
                        if (delivered == ItemDeliveryResult.Verified) outcome.Verified++;
                        else outcome.Delivered++;

                        UpsertState(db, plan, docs.Count, ItemState.Downloaded, null, receipt);
                        if (isNew)
                        {
                            outcome.NewReferences = Append(outcome.NewReferences, plan.SubmissionReference);
                            outcome.NewCompanyNames = Append(outcome.NewCompanyNames, plan.CompanyName);
                            outcome.NewEstimatorNames = Append(outcome.NewEstimatorNames, plan.PrimaryEstimatorDisplay());
                        }
                    }
                    catch (InboxRemoteException ex) when (ex.Kind == InboxRemoteErrorKind.Offline)
                    {
                        // Mid-batch disconnect: stop the batch, keep what we have.
                        outcome.Offline = true;
                        outcome.StatusText = "Website connection lost during the check.";
                        outcome.Error = ex.Message;
                        break;
                    }
                    catch (InboxRemoteException ex) when (ex.Kind == InboxRemoteErrorKind.PermissionDenied)
                    {
                        outcome.AccessDenied = true;
                        outcome.Error = ex.Message;
                        outcome.StatusText = ex.Message;
                        break;
                    }
                    catch (InboxRemoteException ex) when (ex.Kind == InboxRemoteErrorKind.AlreadyDelivered)
                    {
                        outcome.DeliveredElsewhere++;
                        var localFolder = FolderPath(Root, plan.RoutingKey, plan.SubmissionReference, plan.SubmissionId);
                        UpsertState(db, plan, Directory.Exists(localFolder) ? (int)plan.DocumentCount : 0,
                            ItemState.DeliveredElsewhere, ex.Message);
                    }
                    catch (Exception ex)
                    {
                        outcome.Error = ex.Message;
                        var localFolder = FolderPath(Root, plan.RoutingKey, plan.SubmissionReference, plan.SubmissionId);
                        bool payloadPlaced = Directory.Exists(localFolder)
                            && File.Exists(Path.Combine(localFolder, "manifest.json"))
                            && File.Exists(Path.Combine(localFolder, "READY"));
                        List<(InboxSubmissionPlan Plan, string RawJson)>? refreshedPlan = null;
                        try { refreshedPlan = await _client.PlanDeliveriesAsync(100, ct).ConfigureAwait(false); }
                        catch (InboxRemoteException refreshError) when (refreshError.Kind == InboxRemoteErrorKind.PermissionDenied)
                        {
                            outcome.AccessDenied = true;
                            outcome.StatusText = refreshError.Message;
                            break;
                        }
                        catch (InboxRemoteException refreshError) when (refreshError.Kind == InboxRemoteErrorKind.Offline)
                        {
                            outcome.Offline = true;
                            outcome.StatusText = "Website connection lost while confirming delivery state.";
                            outcome.Error = refreshError.Message;
                            break;
                        }
                        if (refreshedPlan == null)
                        {
                            // Do not label an item Failed unless the website confirms it is still offered.
                            continue;
                        }
                        bool stillOffered = refreshedPlan.Any(x => x.Plan.SubmissionId == plan.SubmissionId);
                        if (!stillOffered && !payloadPlaced)
                        {
                            outcome.DeliveredElsewhere++;
                            UpsertState(db, plan, 0, ItemState.DeliveredElsewhere,
                                DeliveredElsewhereMessage(db, plan.SubmissionId));
                        }
                        else if (payloadPlaced)
                        {
                            outcome.ImportPending++;
                            UpsertState(db, plan, (int)plan.DocumentCount, ItemState.ImportPending,
                                ex.Message.Length > 490 ? ex.Message[..490] : ex.Message);
                        }
                        else
                        {
                            outcome.Failed++;
                            UpsertState(db, plan, 0, ItemState.Failed,
                                ex.Message.Length > 490 ? ex.Message[..490] : ex.Message);
                        }
                    }
                }

                // Phase 2 (docs/07): pull the per-inquiry website chat
                // channel into the local communications feed. Best
                // effort — a missing website function or a dropped
                // connection only yields a neutral note, never a failed
                // sync. Runs on the ledger (not the plan) because the
                // plan only lists not-yet-delivered submissions while
                // chat keeps flowing after delivery.
                int chatAdded = 0;
                string? chatNote = null;
                if (_settings.SyncWebsiteChat && !outcome.Offline && !outcome.AccessDenied)
                    (chatAdded, chatNote) = await SyncInboxChatAsync(db, ct).ConfigureAwait(false);

                db.SaveChanges();
                _settings.LastSyncAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                _settings.LastSyncStatus = outcome.AccessDenied
                    ? "Access denied"
                    : outcome.Offline ? "Offline (partial)"
                    : outcome.Failed == 0 ? "OK" : "OK (with failures)";
                if (!outcome.Offline && !outcome.AccessDenied)
                {
                    outcome.StatusText = outcome.Planned == 0
                        ? "No submissions are waiting. The website hands a submission to this PC only after a ProGlass operator accepts it; everything accepted and not yet delivered appears here."
                        : outcome.Failed == 0
                            ? $"Checked the website — {outcome.Planned} pending, {outcome.Delivered} downloaded, {outcome.Verified} already on disk."
                            : $"Checked the website — {outcome.Failed} of {outcome.Planned} failed (details in the inbox page).";
                    if (outcome.ImportPending > 0)
                        outcome.StatusText += $" {outcome.ImportPending} delivery item(s) are ready to re-import from disk.";
                    if (chatNote != null)
                        outcome.StatusText += " " + chatNote;
                    else if (chatAdded > 0)
                        outcome.StatusText = outcome.StatusText.TrimEnd('.', ' ') +
                            $", {chatAdded} new website message(s).";
                }
                _settings.Save();
            }
            catch (JsonException ex)
            {
                outcome.Error = "Website returned inbox data in an unsupported JSON format: " + ex.Message;
                outcome.StatusText = outcome.Error;
                _settings.LastSyncStatus = "Protocol error";
                _settings.Save();
            }
            catch (InboxRemoteException ex) when (ex.Kind == InboxRemoteErrorKind.Offline)
            {
                outcome.Offline = true;
                outcome.StatusText = "Website is unreachable right now — the inbox keeps working locally.";
                outcome.Error = ex.Message;
                _settings.LastSyncStatus = "Offline";
                _settings.Save();
            }
            catch (InboxRemoteException ex)
            {
                outcome.AccessDenied = ex.Kind == InboxRemoteErrorKind.PermissionDenied;
                outcome.Error = ex.Message;
                outcome.StatusText = outcome.AccessDenied
                    ? ex.Message
                    : "Website check failed: " + ex.Message;
                _settings.LastSyncStatus = ex.Kind switch
                {
                    InboxRemoteErrorKind.Auth => "Sign-in failed",
                    InboxRemoteErrorKind.PermissionDenied => "Access denied",
                    _ => "Failed"
                };
                if (outcome.AccessDenied)
                    _settings.LastSyncAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                _settings.Save();
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
            return outcome;
        }

        // ── Website chat pull (docs/07 phase 2) ──────────────────────────

        /// <summary>
        /// Pulls the per-inquiry website chat channel
        /// (list_customer_inquiry_communications_v1) into each ingested
        /// inquiry's local communications feed. Dedupes on the website
        /// message id (ExternalId), so re-syncs are idempotent.
        ///
        /// Returns (added, note). The note is a neutral, operator-facing
        /// sentence appended to the sync status when the chat pull could
        /// not run (offline, RPC not deployed, or a remote failure) —
        /// the sync itself never fails because of it.
        /// </summary>
        private async Task<(int Added, string? Note)> SyncInboxChatAsync(InquiryDbContext db, CancellationToken ct)
        {
            var targets = db.InboxItems.AsNoTracking()
                .Where(i => i.LocalInquiryId != null)
                .OrderByDescending(i => i.IngestedAt)
                .Take(200)
                .Select(i => new { i.SubmissionId, i.LocalInquiryId })
                .ToList();
            if (targets.Count == 0) return (0, null);

            int added = 0;
            foreach (var target in targets)
            {
                ct.ThrowIfCancellationRequested();
                List<InquiryWebsiteMessage> messages;
                try
                {
                    messages = await _client.ListInquiryCommunicationsAsync(target.SubmissionId, ct).ConfigureAwait(false);
                }
                catch (InboxRemoteException ex) when (ex.Kind == InboxRemoteErrorKind.Offline)
                {
                    return (added, "Website chat is not syncing right now (offline).");
                }
                catch (InboxRemoteException ex) when (ex.HttpStatusCode == 404)
                {
                    return (added, "Website chat is not syncing yet — the website chat function is not deployed (see docs/07).");
                }
                catch (Exception)
                {
                    return (added, "Website chat is not syncing — the website chat read failed (files and inbox are unaffected).");
                }

                var localInquiryId = target.LocalInquiryId!.Value;
                foreach (var message in messages)
                {
                    if (string.IsNullOrWhiteSpace(message.Id)) continue;
                    var existing = db.Communications.FirstOrDefault(
                        c => c.InquiryId == localInquiryId && c.ExternalId == message.Id);
                    if (existing != null) continue;

                    var isRequest = message.MessageKind == "missing_information_request";
                    var isInternal = message.Visibility == "internal";
                    var author = string.IsNullOrWhiteSpace(message.AuthorDisplayName)
                        ? "website user"
                        : message.AuthorDisplayName!.Trim();
                    var title = isRequest
                        ? (isInternal ? "Internal information request (website)" : "Information request from the website")
                        : (isInternal ? "Internal website note" : "Website message from " + author);
                    var description = message.Body ?? "";
                    if (isRequest)
                        description += message.RequestStatus == "resolved" ? " (resolved)" : " (open)";

                    var occurred = message.CreatedAt ?? DateTime.UtcNow;
                    if (occurred.Kind == DateTimeKind.Utc) occurred = occurred.ToLocalTime();

                    db.Communications.Add(new InquiryCommunication
                    {
                        InquiryId = localInquiryId,
                        EventType = isRequest ? "Website Information Request" : "Website Chat",
                        Title = title,
                        Description = description,
                        OccurredAt = occurred,
                        ActorName = author,
                        ExternalId = message.Id
                    });
                    added++;
                }
            }
            return (added, null);
        }

        // ── One delivery (bridge apply-loop item) ────────────────────────

        private enum ItemDeliveryResult { Delivered, Verified }
        private enum ItemState { Downloaded, ImportPending, DeliveredElsewhere, Failed }

        private async Task<ItemDeliveryResult> DeliverAsync(InboxSubmissionPlan plan, string rawJson,
            List<InboxDocumentPlan> docs, CancellationToken ct)
        {
            var root = Root;
            var route = plan.RoutingKey;
            var finalPath = FolderPath(root, route, plan.SubmissionReference, plan.SubmissionId);
            if (!IsInsideRoot(finalPath, root))
                throw new InboxRemoteException(InboxRemoteErrorKind.Remote, "Inbox route escaped the isolated root.");

            var manifest = new InboxManifest
            {
                Version = 2,
                RoutingKey = route,
                PrimaryEstimatorCode = string.IsNullOrWhiteSpace(plan.PrimaryEstimatorCode) ? null : plan.PrimaryEstimatorCode,
                AssignmentSnapshotRaw = plan.AssignmentSnapshotRaw ?? "null",
                SubmissionRaw = rawJson,
                Documents = docs.Select(d => new InboxManifest.InboxManifestDocument
                {
                    Id = d.DocumentId,
                    Filename = d.OriginalFilename,
                    Mime = d.DetectedMime,
                    ByteSize = d.ByteSize,
                    Sha256 = d.ContentSha256,
                    LocalFile = "documents/" + d.DocumentId + "_" + InboxFileNames.Safe(InboxFileNames.BaseName(d.OriginalFilename))
                }).ToList()
            };
            var (manifestBytes, manifestHash) = JsCanonicalJson.BuildManifestBytes(manifest);

            // Existing folder: verify (idempotent re-check, same as the bridge).
            if (Directory.Exists(finalPath))
            {
                var existing = File.ReadAllBytes(Path.Combine(finalPath, "manifest.json"));
                if (Sha256Of(existing) != manifestHash)
                    throw new InboxRemoteException(InboxRemoteErrorKind.Remote,
                        "Existing inbox item conflicts for " + plan.SubmissionReference + ".");
                foreach (var doc in manifest.Documents)
                {
                    var path = Path.Combine(finalPath, doc.LocalFile.Replace('/', Path.DirectorySeparatorChar));
                    var bytes = File.ReadAllBytes(path);
                    if (bytes.LongLength != doc.ByteSize || Sha256Of(bytes) != doc.Sha256)
                        throw new InboxRemoteException(InboxRemoteErrorKind.Remote,
                            "Existing inbox integrity check failed for " + doc.Filename + ".");
                }
                if (File.Exists(Path.Combine(finalPath, "delivery.json")))
                    _ = ReadDeliveryReceiptIfPresent(finalPath, plan, docs.Count);
                // A local READY folder is never a reason to replay the remote acknowledgement.
                return ItemDeliveryResult.Verified;
            }

            // Stage → download → verify → atomic rename (bridge contract).
            var stage = Path.Combine(root, "incoming", plan.SubmissionId + "." + Guid.NewGuid().ToString("N") + ".partial");
            try
            {
                Directory.CreateDirectory(Path.Combine(stage, "documents"));
                foreach (var doc in docs)
                {
                    byte[] bytes;
                    try
                    {
                        bytes = await _client.DownloadObjectAsync(doc.StorageBucket, doc.StorageObjectKey, ct).ConfigureAwait(false);
                    }
                    catch (InboxRemoteException ex) when (ex.Kind == InboxRemoteErrorKind.PermissionDenied)
                    {
                        throw new InboxRemoteException(InboxRemoteErrorKind.PermissionDenied,
                            "This account may not read those files. " + ex.Message, ex.HttpStatusCode);
                    }
                    if (bytes.LongLength != doc.ByteSize || Sha256Of(bytes) != doc.ContentSha256)
                        throw new InboxRemoteException(InboxRemoteErrorKind.Remote,
                            "integrity check failed for " + doc.OriginalFilename);
                    var localName = doc.DocumentId + "_" + InboxFileNames.Safe(InboxFileNames.BaseName(doc.OriginalFilename));
                    File.WriteAllBytes(Path.Combine(stage, "documents", localName), bytes);
                }
                File.WriteAllBytes(Path.Combine(stage, "manifest.json"), manifestBytes);
                File.WriteAllText(Path.Combine(stage, "READY"), manifestHash + "\n", new UTF8Encoding(false));
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
                Directory.Move(stage, finalPath);
            }
            catch
            {
                DeleteDirectorySafe(stage);
                throw;
            }

            await AcknowledgeAsync(plan, manifestHash, docs, ct).ConfigureAwait(false);
            var acknowledgedBy = LastAccessReport?.AccountEmail;
            if (string.IsNullOrWhiteSpace(acknowledgedBy)) acknowledgedBy = _settings.BridgeEmail;
            WriteDeliveryReceipt(finalPath, plan, manifestHash, docs.Count, acknowledgedBy);
            WriteCheckpoint(root, plan.SubmissionId);
            return ItemDeliveryResult.Delivered;
        }

        private async Task AcknowledgeAsync(InboxSubmissionPlan plan, string manifestHash,
            List<InboxDocumentPlan> docs, CancellationToken ct)
        {
            var snapshot = plan.AssignmentSnapshotRaw ?? "null";
            try
            {
                await _client.AcknowledgeAsync(plan.SubmissionId, manifestHash, docs.Count, plan.RoutingKey, snapshot, ct)
                    .ConfigureAwait(false);
            }
            catch (InboxRemoteException ex) when (ex.Kind == InboxRemoteErrorKind.PermissionDenied)
            {
                throw PlanAccessDenied(ex.Message, ex.HttpStatusCode);
            }
        }

        private static bool IsInsideRoot(string path, string root)
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            return full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string Sha256Of(byte[] bytes) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

        private static void WriteDeliveryReceipt(string folder, InboxSubmissionPlan plan,
            string manifestHash, int documentCount, string acknowledgedBy)
        {
            var receipt = new InboxDeliveryReceipt
            {
                SubmissionId = plan.SubmissionId,
                Reference = plan.SubmissionReference,
                ManifestSha256 = manifestHash,
                DocumentCount = documentCount,
                RoutingKey = plan.RoutingKey,
                AckedAtUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                AckedBy = acknowledgedBy
            };
            File.WriteAllText(Path.Combine(folder, "delivery.json"),
                JsonSerializer.Serialize(receipt, JsonOpts), new UTF8Encoding(false));
        }

        private static void WriteCheckpoint(string root, string submissionId)
        {
            try
            {
                File.WriteAllText(Path.Combine(root, "checkpoint.json"),
                    "{\n  \"lastAcknowledgedSubmissionId\": \"" + JsCanonicalJson.Escape(submissionId) +
                    "\",\n  \"lastAcknowledgedAt\": \"" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "\"\n}\n",
                    new UTF8Encoding(false));
            }
            catch { /* checkpoint is bookkeeping only */ }
        }

        private static void DeleteDirectorySafe(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch { /* best effort */ }
        }

        private static string[] Append(string[] array, string value)
        {
            var result = new string[array.Length + 1];
            Array.Copy(array, result, array.Length);
            result[array.Length] = value;
            return result;
        }

        // ── Local ledger ─────────────────────────────────────────────────

        private void ReconcileUnofferedItems(InquiryDbContext db, HashSet<string> plannedIds)
        {
            foreach (var item in db.InboxItems.Where(i => i.State != InquiryInboxStates.Ingested)
                .ToList().Where(i => !plannedIds.Contains(i.SubmissionId)))
            {
                var folder = FolderPath(Root, item.RoutingKey, item.Reference, item.SubmissionId);
                if (Directory.Exists(folder)) continue;
                item.State = InquiryInboxStates.DeliveredElsewhere;
                item.Error = DeliveredElsewhereMessage(item);
            }
        }

        private string DeliveredElsewhereMessage(InquiryDbContext db, string submissionId)
        {
            var item = db.InboxItems.FirstOrDefault(i => i.SubmissionId == submissionId);
            return DeliveredElsewhereMessage(item ?? new InquiryInboxItem { SubmissionId = submissionId });
        }

        private static string DeliveredElsewhereMessage(InquiryInboxItem item)
        {
            string time = "time unavailable";
            var acknowledgedAt = string.IsNullOrWhiteSpace(item.AcknowledgedAtUtc)
                ? item.DownloadedAt : item.AcknowledgedAtUtc;
            if (DateTime.TryParse(acknowledgedAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var utc))
                time = utc.AddHours(4).ToString("dd MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            return "Delivered to this PC on " + time + " (Dubai); the files are missing here. "
                + "Fetch them from the website (Customer submissions -> open the submission -> Download) "
                + @"or restore them from C:\ProgramData\ProGlass\CustomerInquiryInbox.";
        }

        private static InboxDeliveryReceipt? ReadDeliveryReceiptIfPresent(string folder,
            InboxSubmissionPlan plan, int documentCount)
        {
            var path = Path.Combine(folder, "delivery.json");
            if (!File.Exists(path)) return null;
            var (_, manifestHash) = ReadReadyFolder(folder);
            var receipt = JsonSerializer.Deserialize<InboxDeliveryReceipt>(File.ReadAllText(path), JsonOpts)
                ?? throw new IOException("Delivery receipt is unreadable.");
            if (receipt.SubmissionId != plan.SubmissionId || receipt.Reference != plan.SubmissionReference
                || receipt.ManifestSha256 != manifestHash || receipt.DocumentCount != documentCount
                || receipt.RoutingKey != plan.RoutingKey || string.IsNullOrWhiteSpace(receipt.AckedAtUtc)
                || string.IsNullOrWhiteSpace(receipt.AckedBy))
                throw new IOException("Delivery receipt does not match the verified payload.");
            return receipt;
        }

        private static string StateFor(ItemState state) => state switch
        {
            ItemState.Downloaded => InquiryInboxStates.Downloaded,
            ItemState.ImportPending => InquiryInboxStates.ImportPending,
            ItemState.DeliveredElsewhere => InquiryInboxStates.DeliveredElsewhere,
            _ => InquiryInboxStates.Failed
        };

        private static void UpsertState(InquiryDbContext db, InboxSubmissionPlan plan,
            int documentCount, ItemState state, string? error, InboxDeliveryReceipt? receipt = null)
        {
            var existing = db.InboxItems.FirstOrDefault(i => i.SubmissionId == plan.SubmissionId);
            var snapshot = string.IsNullOrWhiteSpace(plan.AssignmentSnapshotRaw) ? "[]" : plan.AssignmentSnapshotRaw;
            if (existing == null)
            {
                db.InboxItems.Add(new InquiryInboxItem
                {
                    SubmissionId = plan.SubmissionId,
                    Reference = plan.SubmissionReference,
                    State = StateFor(state),
                    DocumentCount = documentCount,
                    RoutingKey = plan.RoutingKey,
                    PrimaryEstimatorCode = plan.PrimaryEstimatorCode ?? "",
                    AssignmentSnapshot = snapshot,
                    AcknowledgedAtUtc = receipt?.AckedAtUtc ?? "",
                    AcknowledgedBy = receipt?.AckedBy ?? "",
                    DownloadedAt = state is ItemState.Downloaded or ItemState.ImportPending
                        ? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") : "",
                    Error = error ?? ""
                });
            }
            else
            {
                existing.Reference = plan.SubmissionReference;
                existing.State = StateFor(state);
                if (documentCount > 0 || state != ItemState.DeliveredElsewhere)
                    existing.DocumentCount = documentCount;
                existing.RoutingKey = plan.RoutingKey;
                existing.PrimaryEstimatorCode = plan.PrimaryEstimatorCode ?? "";
                existing.AssignmentSnapshot = snapshot;
                if (receipt != null)
                {
                    existing.AcknowledgedAtUtc = receipt.AckedAtUtc;
                    existing.AcknowledgedBy = receipt.AckedBy;
                }
                if (state is ItemState.Downloaded or ItemState.ImportPending)
                    existing.DownloadedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                existing.Error = error ?? "";
            }
        }

        /// <summary>Adopt READY folders already on disk (Node bridge parity or earlier WPF runs).</summary>
        public int ScanLocalInbox()
        {
            var root = Root;
            if (!Directory.Exists(root)) return 0;
            var adopted = 0;
            using var db = new InquiryDbContext();

            foreach (var candidate in EnumerateReadyFolders(root))
            {
                try
                {
                    var (manifest, manifestHash) = ReadReadyFolder(candidate);
                    var plan = JsonSerializer.Deserialize<InboxSubmissionPlan>(manifest.SubmissionRaw, JsonOpts)
                        ?? throw new IOException("Adopted manifest is missing its submission row.");
                    if (db.InboxItems.Any(i => i.SubmissionId == plan.SubmissionId)) continue;
                    var receipt = ReadDeliveryReceiptIfPresent(candidate, plan, manifest.Documents.Count);
                    db.InboxItems.Add(new InquiryInboxItem
                    {
                        SubmissionId = plan.SubmissionId,
                        Reference = plan.SubmissionReference,
                        State = InquiryInboxStates.Downloaded,
                        ManifestSha256 = manifestHash,
                        DocumentCount = manifest.Documents.Count,
                        RoutingKey = manifest.RoutingKey,
                        PrimaryEstimatorCode = manifest.PrimaryEstimatorCode ?? "",
                        AssignmentSnapshot = string.IsNullOrWhiteSpace(manifest.AssignmentSnapshotRaw) ? "[]" : manifest.AssignmentSnapshotRaw,
                        DownloadedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                        AcknowledgedAtUtc = receipt?.AckedAtUtc ?? "",
                        AcknowledgedBy = receipt?.AckedBy ?? "",
                        Error = ""
                    });
                    adopted++;
                }
                catch { /* one bad folder must not stop the scan */ }
            }

            if (adopted > 0)
            {
                db.SaveChanges();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            return adopted;
        }

        private static IEnumerable<string> EnumerateReadyFolders(string root)
        {
            // {root}/unassigned/{ref}_{id}
            var unassigned = Path.Combine(root, "unassigned");
            if (Directory.Exists(unassigned))
                foreach (var f in EnumerateReadyIn(unassigned)) yield return f;
            // {root}/assigned/{CODE}/{ref}_{id}
            var assigned = Path.Combine(root, "assigned");
            if (Directory.Exists(assigned))
                foreach (var codeDir in Directory.EnumerateDirectories(assigned))
                    foreach (var f in EnumerateReadyIn(codeDir)) yield return f;
        }

        private static IEnumerable<string> EnumerateReadyIn(string routingDir)
        {
            foreach (var dir in Directory.EnumerateDirectories(routingDir))
            {
                if (File.Exists(Path.Combine(dir, "manifest.json")) && File.Exists(Path.Combine(dir, "READY")))
                    yield return dir;
            }
        }

        internal static (InboxManifest Manifest, string Hash) ReadReadyFolder(string folder)
        {
            var manifestBytes = File.ReadAllBytes(Path.Combine(folder, "manifest.json"));
            var hash = File.ReadAllText(Path.Combine(folder, "READY")).Trim();
            if (Sha256Of(manifestBytes) != hash)
                throw new IOException("READY marker does not match the manifest hash.");
            var manifest = ParseManifest(manifestBytes);
            foreach (var doc in manifest.Documents)
            {
                var path = Path.Combine(folder, doc.LocalFile.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) throw new IOException("Missing document: " + doc.LocalFile);
                var bytes = File.ReadAllBytes(path);
                if (bytes.LongLength != doc.ByteSize || Sha256Of(bytes) != doc.Sha256)
                    throw new IOException("Integrity check failed for " + doc.Filename + ".");
            }
            return (manifest, hash);
        }

        private static System.Text.Json.JsonDocument ParseManifestJsonWithLegacyRoutingRepair(byte[] manifestBytes)
        {
            try { return System.Text.Json.JsonDocument.Parse(manifestBytes); }
            catch (System.Text.Json.JsonException original)
            {
                // Early 3.0 builds emitted the routing value without its "key" property name.
                // Repair only that exact shape in memory; never rewrite the acknowledged bytes or READY hash.
                var json = System.Text.Encoding.UTF8.GetString(manifestBytes);
                const string pattern = "(\"routing\"\\s*:\\s*\\{[\\s\\S]*?,\\s*)(\"(?:\\\\.|[^\"\\\\])*\")(\\s*,\\s*\"primaryEstimatorCode\"\\s*:)";
                var repaired = System.Text.RegularExpressions.Regex.Replace(json, pattern,
                    match => match.Groups[1].Value + "\"key\": " + match.Groups[2].Value + match.Groups[3].Value,
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant);
                if (string.Equals(json, repaired, StringComparison.Ordinal)) throw;
                try { return System.Text.Json.JsonDocument.Parse(repaired); }
                catch (System.Text.Json.JsonException) { throw original; }
            }
        }

        internal static InboxManifest ParseManifest(byte[] manifestBytes)
        {
            using var doc = ParseManifestJsonWithLegacyRoutingRepair(manifestBytes);
            var root = doc.RootElement;
            var manifest = new InboxManifest
            {
                Version = root.TryGetProperty("version", out var v) ? v.GetInt32() : 2,
                RoutingKey = root.TryGetProperty("routing", out var r) && r.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "",
                PrimaryEstimatorCode = root.TryGetProperty("routing", out var r2) && r2.TryGetProperty("primaryEstimatorCode", out var p) && p.ValueKind != System.Text.Json.JsonValueKind.Null
                    ? p.GetString() : null,
                AssignmentSnapshotRaw = root.TryGetProperty("routing", out var r3) && r3.TryGetProperty("assignmentSnapshot", out var a)
                    ? a.GetRawText() : "[]",
                SubmissionRaw = root.TryGetProperty("submission", out var s) ? s.GetRawText() : "{}",
                Documents = new List<InboxManifest.InboxManifestDocument>()
            };
            if (root.TryGetProperty("documents", out var docs) && docs.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var d in docs.EnumerateArray())
                {
                    manifest.Documents.Add(new InboxManifest.InboxManifestDocument
                    {
                        Id = d.TryGetProperty("id", out var di) ? di.GetString() ?? "" : "",
                        Filename = d.TryGetProperty("filename", out var df) ? df.GetString() ?? "" : "",
                        Mime = d.TryGetProperty("mime", out var dm) ? dm.GetString() ?? "" : "",
                        ByteSize = d.TryGetProperty("byteSize", out var ds) ? ds.GetInt64() : 0,
                        Sha256 = d.TryGetProperty("sha256", out var dh) ? dh.GetString() ?? "" : "",
                        LocalFile = d.TryGetProperty("localFile", out var dl) ? dl.GetString() ?? "" : ""
                    });
                }
            }
            return manifest;
        }

        // ── Read models for the UI ───────────────────────────────────────

        public InboxSummary GetSummary()
        {
            using var db = new InquiryDbContext();
            var items = db.InboxItems.AsNoTracking().ToList();
            var lastSyncAtUtc = _settings.LastSyncAtUtc ?? "";
            return new InboxSummary
            {
                Configured = _settings.IsConfigured,
                Offline = (_settings.LastSyncStatus ?? "").StartsWith("Offline", StringComparison.Ordinal),
                DownloadedCount = items.Count(i => i.State is InquiryInboxStates.Downloaded or InquiryInboxStates.ImportPending),
                IngestedCount = items.Count(i => i.State == InquiryInboxStates.Ingested),
                FailedCount = items.Count(i => i.State == InquiryInboxStates.Failed),
                LastSyncAtUtc = lastSyncAtUtc,
                LastSyncStatus = _settings.LastSyncStatus ?? "",
                LastSyncAtUtcParsed = DateTime.TryParse(lastSyncAtUtc, out var dt) ? dt : (DateTime?)null
            };
        }

        public List<InquiryInboxItem> GetItemRows()
        {
            using var db = new InquiryDbContext();
            return db.InboxItems.AsNoTracking()
                .OrderByDescending(i => i.DownloadedAt)
                .ToList();
        }

        public string GetFolderPath(string submissionId)
        {
            using var db = new InquiryDbContext();
            var item = db.InboxItems.AsNoTracking().FirstOrDefault(i => i.SubmissionId == submissionId);
            if (item == null) return "";
            return FolderPath(Root, item.RoutingKey, item.Reference, item.SubmissionId);
        }

        public void Dispose()
        {
            Task<InboxSyncOutcome>? runningTask;
            CancellationTokenSource? running;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                running = _running;
                runningTask = _runningTask;
            }

            try { running?.Cancel(); }
            catch (ObjectDisposedException) { /* the sync completed between capture and cancel */ }

            // Sync work uses ConfigureAwait(false) throughout, so waiting
            // here cannot depend on the dispatcher. Stop it before the host
            // releases the process-wide HTTP transport.
            try { runningTask?.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            catch (Exception) { /* shutdown is best-effort */ }

            _client.Dispose();
        }
    }
}
