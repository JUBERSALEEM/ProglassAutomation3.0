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
using System.Text;
using System.Text.Json;
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
        public bool Ran { get; init; }
        public bool Configured { get; init; } = true;
        public bool Offline { get; init; }
        public bool Busy { get; init; }
        public int Planned { get; init; }
        public int Delivered { get; init; }
        public int Verified { get; init; }
        public int Failed { get; init; }
        /// <summary>References newly seen this sync (drives notifications).</summary>
        public string[] NewReferences { get; init; } = Array.Empty<string>();
        public string[] NewCompanyNames { get; init; } = Array.Empty<string>();
        public string[] NewEstimatorNames { get; init; } = Array.Empty<string>();
        public string StatusText { get; init; } = "";
        public string? Error { get; init; }
    }

    public sealed class InquiryInboxService : IDisposable
    {
        private readonly InquiryInboxSettings _settings;
        private readonly InquiryInboxClient _client;
        private CancellationTokenSource? _running;
        private readonly object _gate = new();

        public InquiryInboxService(InquiryInboxSettings settings)
        {
            _settings = settings;
            _client = new InquiryInboxClient(settings);
        }

        public InquiryInboxSettings Settings => _settings;

        /// <summary>Raised after each sync/scan completes (service thread — marshal in the VM).</summary>
        public event EventHandler? StateChanged;

        // ── Guards & paths ───────────────────────────────────────────────

        private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.JsonNumberHandling.AllowReadingFromString
        };

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
            var full = Path.Combine(root, route.Split('/').Where(s => s.Length > 0).ToArray())
                .Replace('\\', Path.DirectorySeparatorChar);
            return Path.Combine(full, folder);
        }

        // ── Synchronize ──────────────────────────────────────────────────

        public async Task<InboxSyncOutcome> SynchronizeAsync(CancellationToken ct = default)
        {
            CancellationTokenSource linked;
            lock (_gate)
            {
                if (_running != null)
                    return new InboxSyncOutcome { Busy = true, StatusText = "Already checking the website." };
                linked = _running = CancellationTokenSource.CreateLinkedTokenSource(ct);
            }
            try
            {
                return await SynchronizeCoreAsync(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate) { _running = null; }
                linked.Dispose();
            }
        }

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
                CleanupStalePartials(Root);

                var signIn = await _client.SignInAsync(ct).ConfigureAwait(false);
                if (!signIn.Success)
                {
                    outcome.Error = signIn.Error;
                    outcome.StatusText = "Website sign-in failed: " + (signIn.Error ?? "unknown error");
                    _settings.LastSyncStatus = "Sign-in failed";
                    _settings.Save();
                    return outcome;
                }

                var planned = await _client.PlanDeliveriesAsync(100, ct).ConfigureAwait(false);
                outcome.Planned = planned.Count;

                using var db = new InquiryDbContext();
                var known = new HashSet<string>(
                    db.InboxItems.AsNoTracking().Select(i => i.SubmissionId));

                foreach (var (plan, rawJson) in planned)
                {
                    ct.ThrowIfCancellationRequested();
                    var isNew = !known.Contains(plan.SubmissionId);
                    try
                    {
                        var docs = await _client.PlanDocumentsAsync(plan.SubmissionId, ct).ConfigureAwait(false);
                        if (docs.Count != plan.DocumentCount)
                            throw new InboxRemoteException(InboxRemoteErrorKind.Remote,
                                $"Remote document count changed ({docs.Count} vs {plan.DocumentCount}).");
                        if (!InboxRouting.IsValid(plan.RoutingKey))
                            throw new InboxRemoteException(InboxRemoteErrorKind.Remote,
                                "Invalid inbox routing key for " + plan.SubmissionReference + ".");

                        var delivered = await DeliverAsync(plan, rawJson, docs, ct).ConfigureAwait(false);
                        if (delivered == ItemDeliveryResult.Verified) outcome.Verified++;
                        else outcome.Delivered++;

                        UpsertState(db, plan, docs.Count, ItemState.Downloaded, null);
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
                    catch (Exception ex)
                    {
                        outcome.Failed++;
                        outcome.Error = ex.Message;
                        UpsertState(db, plan, 0, ItemState.Failed,
                            ex.Message.Length > 490 ? ex.Message[..490] : ex.Message);
                    }
                }

                db.SaveChanges();
                _settings.LastSyncAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                _settings.LastSyncStatus = outcome.Offline
                    ? "Offline (partial)"
                    : outcome.Failed == 0 ? "OK" : "OK (with failures)";
                if (!outcome.Offline)
                    outcome.StatusText = outcome.Failed == 0
                        ? $"Checked the website — {outcome.Planned} pending, {outcome.Delivered} downloaded, {outcome.Verified} already on disk."
                        : $"Checked the website — {outcome.Failed} of {outcome.Planned} failed (details in the inbox page).";
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
                outcome.Error = ex.Message;
                outcome.StatusText = "Website check failed: " + ex.Message;
                _settings.LastSyncStatus = ex.Kind == InboxRemoteErrorKind.Auth ? "Sign-in failed" : "Failed";
                _settings.Save();
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
            return outcome;
        }

        // ── One delivery (bridge apply-loop item) ────────────────────────

        private enum ItemDeliveryResult { Delivered, Verified }
        private enum ItemState { Downloaded, Failed }

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
                AssignmentSnapshotRaw = string.IsNullOrWhiteSpace(plan.AssignmentSnapshotRaw) ? "[]" : plan.AssignmentSnapshotRaw,
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
                await AcknowledgeAsync(plan, manifestHash, docs, ct).ConfigureAwait(false);
                return ItemDeliveryResult.Verified;
            }

            // Stage → download → verify → atomic rename (bridge contract).
            var stage = Path.Combine(Path.Combine(root, ".staging"), plan.SubmissionId + "." + Guid.NewGuid().ToString("N") + ".partial");
            try
            {
                Directory.CreateDirectory(Path.Combine(stage, "documents"));
                foreach (var doc in docs)
                {
                    var bytes = await _client.DownloadObjectAsync(doc.StorageBucket, doc.StorageObjectKey, ct).ConfigureAwait(false);
                    if (bytes.LongLength != doc.ByteSize || Sha256Of(bytes) != doc.ContentSha256)
                        throw new InboxRemoteException(InboxRemoteErrorKind.Remote,
                            "Integrity check failed for " + doc.OriginalFilename + ".");
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
            WriteCheckpoint(root, plan.SubmissionId);
            return ItemDeliveryResult.Delivered;
        }

        private async Task AcknowledgeAsync(InboxSubmissionPlan plan, string manifestHash,
            List<InboxDocumentPlan> docs, CancellationToken ct)
        {
            var snapshot = string.IsNullOrWhiteSpace(plan.AssignmentSnapshotRaw) ? "[]" : plan.AssignmentSnapshotRaw;
            await _client.AcknowledgeAsync(plan.SubmissionId, manifestHash, docs.Count, plan.RoutingKey, snapshot, ct)
                .ConfigureAwait(false);
        }

        private static bool IsInsideRoot(string path, string root)
        {
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            return full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string Sha256Of(byte[] bytes) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

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

        private static void CleanupStalePartials(string root)
        {
            var stageRoot = Path.Combine(root, ".staging");
            if (!Directory.Exists(stageRoot)) return;
            foreach (var dir in Directory.EnumerateDirectories(stageRoot))
            {
                if (!dir.EndsWith(".partial")) continue;
                DeleteDirectorySafe(dir);
            }
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

        private static void UpsertState(InquiryDbContext db, InboxSubmissionPlan plan,
            int documentCount, ItemState state, string? error)
        {
            var existing = db.InboxItems.FirstOrDefault(i => i.SubmissionId == plan.SubmissionId);
            var snapshot = string.IsNullOrWhiteSpace(plan.AssignmentSnapshotRaw) ? "[]" : plan.AssignmentSnapshotRaw;
            if (existing == null)
            {
                db.InboxItems.Add(new InquiryInboxItem
                {
                    SubmissionId = plan.SubmissionId,
                    Reference = plan.SubmissionReference,
                    State = state == ItemState.Downloaded ? InquiryInboxStates.Downloaded : InquiryInboxStates.Failed,
                    DocumentCount = documentCount,
                    RoutingKey = plan.RoutingKey,
                    PrimaryEstimatorCode = plan.PrimaryEstimatorCode ?? "",
                    AssignmentSnapshot = snapshot,
                    DownloadedAt = state == ItemState.Downloaded ? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") : "",
                    Error = error ?? ""
                });
            }
            else
            {
                existing.Reference = plan.SubmissionReference;
                existing.State = state == ItemState.Downloaded ? InquiryInboxStates.Downloaded : InquiryInboxStates.Failed;
                existing.DocumentCount = documentCount;
                existing.RoutingKey = plan.RoutingKey;
                existing.PrimaryEstimatorCode = plan.PrimaryEstimatorCode ?? "";
                existing.AssignmentSnapshot = snapshot;
                if (state == ItemState.Downloaded)
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

        internal static InboxManifest ParseManifest(byte[] manifestBytes)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(manifestBytes);
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
                DownloadedCount = items.Count(i => i.State == InquiryInboxStates.Downloaded),
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
            _client.Dispose();
        }
    }
}
