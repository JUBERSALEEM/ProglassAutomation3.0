// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/ViewModels/InquiryInboxViewModel.cs
// MODULE: Inquiry — website inbox page (separate page from the register).
//
// Owns the inbox service + ingest service + desktop notifier for the
// inbox page. The page shows:
//   * connection status + last sync + counts (ready / ingested / failed)
//   * the website connection card (settings; password via PasswordBox)
//   * the deliveries grid with Check now / ingest / open actions
//
// Offline behaviour is neutral: "unreachable" is a state, not an error.
// ═══════════════════════════════════════════════════════════════════════

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Services.Inquiry;
using ProGlassAutomation.Services.Inquiry.InquiryInbox;
using ProGlassAutomation.ThemeStudio;

namespace ProGlassAutomation.ViewModels.Inquiry
{
    /// <summary>One deliveries-grid row (ledger + manifest display data).</summary>
    public sealed class InquiryInboxItemRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Raise([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public InquiryInboxItem Ledger { get; }
        public InquiryInboxItemRow(InquiryInboxItem ledger) => Ledger = ledger;

        public string SubmissionId => Ledger.SubmissionId;
        public string Reference => Ledger.Reference;
        public string Company { get; private set; } = "";
        public string Project { get; private set; } = "";
        public string InquiryType { get; private set; } = "";
        public string Estimator { get; private set; } = "";
        public string DocumentsText => Ledger.DocumentCount == 0 ? "No files" : $"{Ledger.DocumentCount} file{(Ledger.DocumentCount == 1 ? "" : "s")}";
        public string ReceivedText { get; private set; } = "";
        public string LocalNumber => Ledger.LocalInquiryNumber ?? "";
        public int? LocalInquiryId => Ledger.LocalInquiryId;
        public string Error => Ledger.Error ?? "";

        public string State => Ledger.State switch
        {
            InquiryInboxStates.Ingested => "Ingested",
            InquiryInboxStates.Failed => "Failed",
            _ => "Downloaded"
        };

        public string StateTag => Ledger.State switch
        {
            InquiryInboxStates.Ingested => "Success",
            InquiryInboxStates.Failed => "Error",
            _ => "Information"
        };

        public bool CanIngest => Ledger.State == InquiryInboxStates.Downloaded;
        public bool CanOpenInquiry => Ledger.State == InquiryInboxStates.Ingested && Ledger.LocalInquiryId != null;

        public void HydrateDisplay(string company, string project, string inquiryType, string estimator, string submittedAtIso)
        {
            Company = company;
            Project = project;
            InquiryType = inquiryType;
            Estimator = estimator;
            if (DateTime.TryParse(submittedAtIso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var dt))
                ReceivedText = dt.ToString("dd MMM yyyy · hh:mm tt", CultureInfo.CurrentCulture);
            Raise(nameof(Company));
            Raise(nameof(Project));
            Raise(nameof(InquiryType));
            Raise(nameof(Estimator));
            Raise(nameof(ReceivedText));
        }
    }

    public sealed class InquiryInboxViewModel : ViewModelBase
    {
        private readonly InquiryInboxService _service;
        private readonly InquiryInboxIngestService _ingest;
        private readonly InquiryInboxNotifier _notifier;
        private string _pendingPassword = "";

        public InquiryInboxViewModel(
            InquiryInboxService service,
            InquiryInboxIngestService ingest,
            InquiryInboxNotifier notifier)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _ingest = ingest ?? throw new ArgumentNullException(nameof(ingest));
            _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
            _notifier.EnsureCreated();

            Items = new ObservableCollection<InquiryInboxItemRow>();

            CheckNowCommand = new DelegateCommand(async () => await CheckNowAsync(), () => !IsSyncing && AccessReport?.CanPlanBridge != false);
            TestConnectionCommand = new DelegateCommand(async () => await TestConnectionAsync());
            SaveSettingsCommand = new DelegateCommand(async () => await SaveSettingsAsync());
            IngestSelectedCommand = new DelegateCommand(
                () => { if (SelectedItem != null) _ = IngestSelectedAsync(); },
                () => SelectedItem != null && SelectedItem.CanIngest && !IsSyncing);
            OpenInquirySelectedCommand = new DelegateCommand(
                () => { if (SelectedItem?.CanOpenInquiry == true && SelectedItem.LocalInquiryId != null)
                    OpenInquiryRequested?.Invoke(SelectedItem.LocalInquiryId.Value); },
                () => SelectedItem?.CanOpenInquiry == true);
            OpenFolderSelectedCommand = new DelegateCommand(
                () => { if (SelectedItem != null) _ = OpenFolderAsync(); },
                () => SelectedItem != null && FolderExistsFor(SelectedItem));
            BackCommand = new DelegateCommand(() => BackRequested?.Invoke());

            LoadSettingsIntoEditor();
            _service.StateChanged += OnServiceStateChanged;
            RefreshItems();
        }

        // ── Wiring (set by the workspace view) ───────────────────────────

        public Action? BackRequested { get; set; }
        /// <summary>Open the local editor for a created inquiry (id).</summary>
        public Action<int>? OpenInquiryRequested { get; set; }
        /// <summary>Read the PasswordBox value (view code-behind).</summary>
        public Func<string>? GetPasswordFunc { get; set; }
        /// <summary>Raised after settings were saved (view clears the box).</summary>
        public event EventHandler? SettingsSaved;
        /// <summary>Raised when an ingest created a local inquiry (register refresh needed).</summary>
        public event EventHandler? RegisterDirty;

        // ── Settings editor fields ───────────────────────────────────────

        private string _supabaseUrl = "";
        public string SupabaseUrl { get => _supabaseUrl; set => SetField(ref _supabaseUrl, value); }

        private string _anonKey = "";
        public string AnonKey { get => _anonKey; set => SetField(ref _anonKey, value); }

        private string _bridgeEmail = "";
        public string BridgeEmail { get => _bridgeEmail; set => SetField(ref _bridgeEmail, value); }

        private string _inboxRoot = InquiryInboxSettings.DefaultInboxRoot;
        public string InboxRoot { get => _inboxRoot; set => SetField(ref _inboxRoot, value); }

        private int _pollMinutes = 5;
        public int PollMinutes
        {
            get => _pollMinutes;
            set
            {
                var clamped = Math.Clamp(value, 1, 120);
                if (SetField(ref _pollMinutes, clamped)) RefreshAutoSyncText();
            }
        }

        public IReadOnlyList<int> PollMinutesOptions { get; } = new[] { 1, 5, 15, 30, 60 };

        private bool _notificationsEnabled = true;
        public bool NotificationsEnabled { get => _notificationsEnabled; set => SetField(ref _notificationsEnabled, value); }

        private bool _notificationSound = true;
        public bool NotificationSound { get => _notificationSound; set => SetField(ref _notificationSound, value); }

        private bool _reportStatusToWebsite;
        public bool ReportStatusToWebsite
        {
            get => _reportStatusToWebsite;
            set
            {
                if (SetField(ref _reportStatusToWebsite, value))
                    OnPropertyChanged(nameof(ReportStatusHint));
            }
        }

        public string ReportStatusHint => _reportStatusToWebsite
            ? "Workflow changes (Prepared / Sent / Confirmed / Hold / Cancelled) are mirrored to the website for its reports page. Needs the website-side update_customer_inquiry_status_v1 function."
            : "Off by default — the website only receives the download acknowledgement.";

        private bool _syncWebsiteChat;
        public bool SyncWebsiteChat
        {
            get => _syncWebsiteChat;
            set
            {
                if (SetField(ref _syncWebsiteChat, value))
                    OnPropertyChanged(nameof(ChatSyncHint));
            }
        }

        public string ChatSyncHint => _syncWebsiteChat
            ? "Each inbox check pulls the website chat channel into every ingested inquiry's communications feed. Needs the website-side list_customer_inquiry_communications_v1 function."
            : "Off by default — website chat messages are not pulled into the app.";

        public bool HasSavedPassword => _service.Settings.HasPassword;
        public string SavedPasswordHint => HasSavedPassword
            ? "A password is saved on this computer — leave empty to keep it."
            : "Enter the bridge account password.";

        private bool _isConfigured;
        public bool IsConfigured
        {
            get => _isConfigured;
            private set
            {
                if (SetField(ref _isConfigured, value))
                    OnPropertyChanged(nameof(ConnectionText));
            }
        }

        // ── Status surface ───────────────────────────────────────────────

        private bool _isSyncing;
        public bool IsSyncing { get => _isSyncing; private set { if (SetField(ref _isSyncing, value)) RaiseCommandState(); } }

        private string _statusText = "Ready.";
        private string? _initializationWarning;
        public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }

        private InquiryInboxAccessReport? _accessReport;
        public InquiryInboxAccessReport? AccessReport
        {
            get => _accessReport;
            private set
            {
                if (SetField(ref _accessReport, value))
                {
                    OnPropertyChanged(nameof(AccessReportText));
                    RaiseCommandState();
                }
            }
        }

        public string AccessReportText
        {
            get
            {
                if (AccessReport == null) return "";
                var text = $"Account email: {AccessReport.AccountEmail} · Roles: {AccessReport.RolesText}";
                if (AccessReport.DenialReason != null)
                    text += " · Denial reason: " + AccessReport.DenialReason;
                return text;
            }
        }

        public void ReportInitializationWarning(string warning)
        {
            if (string.IsNullOrWhiteSpace(warning)) return;
            _initializationWarning = warning;
            StatusText = "Database initialization warning: " + warning;
        }

        private string _lastSyncText = "";
        public string LastSyncText { get => _lastSyncText; private set => SetField(ref _lastSyncText, value); }

        private int _readyCount;
        public int ReadyCount { get => _readyCount; private set => SetField(ref _readyCount, value); }

        private int _ingestedCount;
        public int IngestedCount { get => _ingestedCount; private set => SetField(ref _ingestedCount, value); }

        private int _failedCount;
        public int FailedCount { get => _failedCount; private set => SetField(ref _failedCount, value); }

        private bool _connectionVerified;
        public string ConnectionText
        {
            get
            {
                if (!IsConfigured) return "Website connection not configured yet";
                if (_connectionVerified) return "Connected to the customer website";
                var savedStatus = _service.Settings.LastSyncStatus ?? "";
                if (savedStatus == "Sign-in failed")
                    return "Sign-in failed — check account, password, and API key";
                if (savedStatus == "Access denied")
                    return "Signed in — this account is not authorized for the website inbox";
                if (savedStatus.StartsWith("Offline", StringComparison.Ordinal))
                    return "Connection settings saved — website unreachable";
                return "Connection settings saved — not yet verified";
            }
        }

        private void SetConnectionVerified(bool verified)
        {
            _connectionVerified = verified;
            OnPropertyChanged(nameof(ConnectionText));
        }

        private string _autoSyncText = "";
        public string AutoSyncText { get => _autoSyncText; private set => SetField(ref _autoSyncText, value); }
        private void RefreshAutoSyncText() =>
            AutoSyncText = $"Automatic check every {PollMinutes} minute{(PollMinutes == 1 ? "" : "s")}";

        // ── Grid ─────────────────────────────────────────────────────────

        public ObservableCollection<InquiryInboxItemRow> Items { get; }

        private InquiryInboxItemRow? _selectedItem;
        public InquiryInboxItemRow? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (SetField(ref _selectedItem, value))
                {
                    OnPropertyChanged(nameof(SelectedErrorText));
                    RaiseCommandState();
                }
            }
        }

        public bool HasItems => Items.Count > 0;

        private string SelectedErrorText => SelectedItem?.Error ?? "";

        // ── Commands ─────────────────────────────────────────────────────

        public ICommand CheckNowCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand SaveSettingsCommand { get; }
        public ICommand IngestSelectedCommand { get; }
        public ICommand OpenInquirySelectedCommand { get; }
        public ICommand OpenFolderSelectedCommand { get; }
        public ICommand BackCommand { get; }

        private void RaiseCommandState()
            => System.Windows.Input.CommandManager.InvalidateRequerySuggested();

        // ── Settings ────────────────────────────────────────────────────

        public void LoadSettingsIntoEditor()
        {
            var s = _service.Settings;
            SupabaseUrl = s.SupabaseUrl;
            AnonKey = s.AnonKey;
            BridgeEmail = s.BridgeEmail;
            InboxRoot = s.InboxRoot;
            PollMinutes = s.PollMinutes;
            NotificationsEnabled = s.NotificationsEnabled;
            NotificationSound = s.NotificationSound;
            ReportStatusToWebsite = s.ReportStatusToWebsite;
            SyncWebsiteChat = s.SyncWebsiteChat;
            OnPropertyChanged(nameof(HasSavedPassword));
            OnPropertyChanged(nameof(SavedPasswordHint));
            IsConfigured = s.IsConfigured;
            SetConnectionVerified(s.LastSyncStatus is "OK" or "OK (with failures)");
            RefreshAutoSyncText();
        }

        /// <summary>Called by the view with the PasswordBox value.</summary>
        public void SetPendingPassword(string password) => _pendingPassword = password ?? "";

        private async Task SaveSettingsAsync()
        {
            try
            {
                StatusText = "Saving settings…";
                var s = _service.Settings;
                var enteredPassword = GetPasswordFunc?.Invoke() ?? _pendingPassword;
                bool accountChanged = !string.Equals(
                    s.BridgeEmail.Trim(), BridgeEmail.Trim(), StringComparison.OrdinalIgnoreCase);
                if (accountChanged && string.IsNullOrEmpty(enteredPassword))
                {
                    StatusText = "Account email changed. Enter that account's Supabase Auth password; the previously saved password belongs to the old account.";
                    return;
                }

                var inboxRoot = string.IsNullOrWhiteSpace(InboxRoot)
                    ? InquiryInboxSettings.DefaultInboxRoot
                    : InboxRoot.Trim();
                var problem = InquiryInboxService.ValidateRoot(inboxRoot);
                if (problem != null)
                {
                    StatusText = problem;
                    return;
                }

                // Validate the whole form before mutating the shared settings
                // object used by background sync. A changed connection must
                // not inherit the previous account's authorization report.
                AccessReport = null;
                s.SupabaseUrl = SupabaseUrl.Trim();
                s.AnonKey = AnonKey.Trim();
                s.BridgeEmail = BridgeEmail.Trim();
                s.InboxRoot = inboxRoot;
                s.PollMinutes = PollMinutes;
                s.NotificationsEnabled = NotificationsEnabled;
                s.NotificationSound = NotificationSound;
                s.ReportStatusToWebsite = ReportStatusToWebsite;
                s.SyncWebsiteChat = SyncWebsiteChat;

                if (!string.IsNullOrEmpty(enteredPassword))
                    s.EncryptedPassword = s.EncryptPassword(enteredPassword);

                s.Save();
                _notifier.SetSoundEnabled(s.NotificationSound);
                OnPropertyChanged(nameof(HasSavedPassword));
                OnPropertyChanged(nameof(SavedPasswordHint));
                IsConfigured = s.IsConfigured;
                StatusText = IsConfigured
                    ? "Connection saved. Checking the website now…"
                    : "Settings saved. Add the website connection details to start syncing.";
                SettingsSaved?.Invoke(this, EventArgs.Empty);

                if (IsConfigured) await CheckNowAsync();
            }
            catch (Exception ex)
            {
                StatusText = "Could not save settings: " + ex.Message;
            }
        }

        private async Task TestConnectionAsync()
        {
            IsSyncing = true;
            AccessReport = null;
            SetConnectionVerified(false);
            try
            {
                // Test the values currently in the editor, not just the last
                // saved settings. Otherwise typing a corrected password and
                // pressing Test connection would silently retry the old one.
                var saved = _service.Settings;
                var candidate = new InquiryInboxSettings
                {
                    SupabaseUrl = SupabaseUrl.Trim(),
                    AnonKey = AnonKey.Trim(),
                    BridgeEmail = BridgeEmail.Trim(),
                    InboxRoot = string.IsNullOrWhiteSpace(InboxRoot)
                        ? InquiryInboxSettings.DefaultInboxRoot
                        : InboxRoot.Trim(),
                    EncryptedPassword = saved.EncryptedPassword
                };

                var enteredPassword = GetPasswordFunc?.Invoke() ?? _pendingPassword;
                bool accountChanged = !string.Equals(
                    saved.BridgeEmail.Trim(), candidate.BridgeEmail, StringComparison.OrdinalIgnoreCase);
                if (accountChanged && string.IsNullOrEmpty(enteredPassword))
                {
                    StatusText = "Account email changed. Enter that account's Supabase Auth password before testing; the saved password belongs to the previous account.";
                    return;
                }

                string savedPassword = saved.GetPassword();
                if (!string.IsNullOrEmpty(enteredPassword))
                {
                    candidate.EncryptedPassword = candidate.EncryptPassword(enteredPassword);
                }
                else if (saved.HasPassword && string.IsNullOrEmpty(savedPassword))
                {
                    StatusText = "The saved password cannot be decrypted for this Windows account. Re-enter it, then test again.";
                    return;
                }

                if (!candidate.IsConfigured)
                {
                    StatusText = "Enter the Supabase URL, public API key, account email, and password before testing.";
                    return;
                }

                bool matchesSavedConnection =
                    string.Equals(candidate.SupabaseUrl, saved.SupabaseUrl, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.AnonKey, saved.AnonKey, StringComparison.Ordinal)
                    && string.Equals(candidate.BridgeEmail, saved.BridgeEmail, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrEmpty(enteredPassword)
                        || string.Equals(enteredPassword, savedPassword, StringComparison.Ordinal));

                StatusText = "Signing in to the website…";
                using var client = new InquiryInboxClient(candidate);
                var result = await client.SignInAsync();
                if (!result.Success)
                {
                    StatusText = "Sign-in failed: " + (result.Error ?? "unknown error");
                    return;
                }

                StatusText = "Signed in — checking account access…";
                try
                {
                    var access = await client.GetMyInquiryAccessAsync();
                    AccessReport = access;
                    if (!access.CanPlanBridge)
                    {
                        string denial = access.MissingCapabilityMessage();
                        _service.Settings.LastSyncStatus = "Access denied";
                        _service.Settings.LastSyncAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                        _service.Settings.Save();
                        SetConnectionVerified(false);
                        RefreshStatusFromSettings();
                        StatusText = denial;
                        return;
                    }

                    var pending = await client.PlanDeliveriesAsync(10, CancellationToken.None);
                    SetConnectionVerified(matchesSavedConnection);
                    StatusText = "Website connection OK — sign-in OK, inbox access OK ("
                        + pending.Count + " pending)."
                        + (matchesSavedConnection
                            ? ""
                            : " These form values are not saved; choose Save connection to use them for automatic checks.");
                }
                catch (InboxRemoteException ex)
                {
                    if (ex.Kind == InboxRemoteErrorKind.PermissionDenied)
                    {
                        _service.Settings.LastSyncStatus = "Access denied";
                        _service.Settings.LastSyncAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                        _service.Settings.Save();
                        SetConnectionVerified(false);
                        RefreshStatusFromSettings();
                        StatusText = "Supabase sign-in succeeded, but this account is not authorized to read website submissions. "
                            + "Ask a website administrator to grant an approved inbox role. Details: " + ex.Message;
                    }
                    else
                    {
                        StatusText = "Supabase sign-in succeeded, but inbox access failed: "
                            + (ex.Kind == InboxRemoteErrorKind.Offline
                                ? "the website is unreachable."
                                : ex.Message);
                    }
                }
                catch (JsonException ex)
                {
                    SetConnectionVerified(false);
                    _service.Settings.LastSyncStatus = "Protocol error";
                    _service.Settings.Save();
                    StatusText = "Sign-in succeeded, but the inbox response could not be read: " + ex.Message;
                }
            }
            catch (InboxRemoteException ex) when (ex.Kind == InboxRemoteErrorKind.Offline)
            {
                StatusText = "The website is unreachable right now (offline).";
            }
            catch (Exception ex)
            {
                StatusText = "Test failed: " + ex.Message;
            }
            finally
            {
                IsSyncing = false;
            }
        }

        // ── Sync ─────────────────────────────────────────────────────────

        public async Task CheckNowAsync()
        {
            if (IsSyncing) return;
            if (AccessReport?.CanPlanBridge == false)
            {
                StatusText = AccessReport.MissingCapabilityMessage();
                return;
            }
            if (!IsConfigured)
            {
                StatusText = "Connect the website first (card below).";
                return;
            }
            IsSyncing = true;
            try
            {
                StatusText = "Checking the customer website…";
                var outcome = await _service.SynchronizeAsync();
                AccessReport = _service.LastAccessReport;
                RefreshItems();
                RefreshStatusFromSettings();

                if (outcome.Offline)
                {
                    StatusText = "Website unreachable right now — the inbox keeps working locally.";
                }
                else if (outcome.Error != null && outcome.Planned == 0 && outcome.Delivered == 0 && outcome.Verified == 0)
                {
                    StatusText = outcome.Error;
                }
                else
                {
                    StatusText = outcome.StatusText;
                }

                if (outcome.NewReferences.Length > 0 && _service.Settings.NotificationsEnabled)
                {
                    for (int i = 0; i < outcome.NewReferences.Length; i++)
                    {
                        _notifier.NotifyNewInquiry(
                            outcome.NewReferences[i],
                            i < outcome.NewCompanyNames.Length ? outcome.NewCompanyNames[i] : "",
                            i < outcome.NewEstimatorNames.Length ? outcome.NewEstimatorNames[i] : "");
                    }
                }
            }
            catch (Exception ex)
            {
                // A sync must never take the app down (async void command).
                StatusText = "Website check failed: " + ex.Message;
            }
            finally
            {
                IsSyncing = false;
            }
        }

        private void RefreshStatusFromSettings()
        {
            LastSyncText = string.IsNullOrEmpty(_service.Settings.LastSyncAtUtc)
                ? "No check yet this session."
                : $"Last check: {_service.Settings.LastSyncAtUtc.Substring(0, 19)} (UTC) · {_service.Settings.LastSyncStatus}";
            SetConnectionVerified(_service.Settings.LastSyncStatus is "OK" or "OK (with failures)");
        }

        private void OnServiceStateChanged(object? sender, EventArgs e)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                RefreshItems();
                RefreshStatusFromSettings();
            }
            else
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    RefreshItems();
                    RefreshStatusFromSettings();
                }));
            }
        }

        // ── Items ───────────────────────────────────────────────────────

        public void RefreshItems()
        {
            try
            {
                var items = _service.GetItemRows();
                Items.Clear();
                int ready = 0, ingested = 0, failed = 0;
                foreach (var item in items)
                {
                    var row = new InquiryInboxItemRow(item);
                    var display = TryReadManifestDisplay(item);
                    if (display != null)
                        row.HydrateDisplay(display.Value.Company, display.Value.Project, display.Value.Type, display.Value.Estimator, display.Value.SubmittedAt);
                    if (item.State == InquiryInboxStates.Downloaded) ready++;
                    else if (item.State == InquiryInboxStates.Ingested) ingested++;
                    else if (item.State == InquiryInboxStates.Failed) failed++;
                    Items.Add(row);
                }
                ReadyCount = ready;
                IngestedCount = ingested;
                FailedCount = failed;
                OnPropertyChanged(nameof(HasItems));
            }
            catch (Exception error)
            {
                // The inbox view-model is created lazily on the user's
                // first click. A missing/outdated local table must be shown
                // on the page, not escape the command and close the app.
                StatusText = _initializationWarning == null
                    ? "Could not load local inbox records: " + error.Message
                    : "Could not load local inbox records: " + error.Message
                      + " Database migration also reported: " + _initializationWarning;
            }
        }

        private (string Company, string Project, string Type, string Estimator, string SubmittedAt)?
            TryReadManifestDisplay(InquiryInboxItem item)
        {
            try
            {
                var folder = InquiryInboxService.FolderPath(
                    _service.Settings.InboxRoot, item.RoutingKey, item.Reference, item.SubmissionId);
                var manifestPath = Path.Combine(folder, "manifest.json");
                if (!File.Exists(manifestPath)) return null;
                var manifest = InquiryInboxService.ParseManifest(File.ReadAllBytes(manifestPath));
                var plan = System.Text.Json.JsonSerializer.Deserialize<InboxSubmissionPlan>(manifest.SubmissionRaw, InboxJson.Options);
                if (plan == null) return null;
                var estimator = plan.PrimaryEstimatorDisplay();
                if (string.IsNullOrWhiteSpace(estimator)) estimator = plan.HandoverEstimatorName ?? "";
                return (plan.CompanyName ?? "", plan.ProjectName ?? "", plan.InquiryType ?? "", estimator, plan.SubmittedAt ?? "");
            }
            catch { return null; }
        }

        private bool FolderExistsFor(InquiryInboxItemRow row)
        {
            var folder = InquiryInboxService.FolderPath(
                _service.Settings.InboxRoot, row.Ledger.RoutingKey, row.Ledger.Reference, row.Ledger.SubmissionId);
            return Directory.Exists(folder);
        }

        // ── Actions ─────────────────────────────────────────────────────

        public async Task IngestSelectedAsync()
        {
            var row = SelectedItem;
            if (row == null || !row.CanIngest) return;
            try
            {
                StatusText = $"Creating local inquiry for {row.Reference}…";
                var result = await _ingest.IngestAsync(row.SubmissionId);
                if (!result.Success)
                {
                    StatusText = "Ingest failed: " + (result.Error ?? "unknown error");
                    return;
                }
                RefreshItems();
                RegisterDirty?.Invoke(this, EventArgs.Empty);
                StatusText = result.AlreadyIngested
                    ? $"Already in the register as {result.InquiryNumber}."
                    : $"Inquiry {result.InquiryNumber} created from {row.Reference} ({result.AttachmentCount} file{(result.AttachmentCount == 1 ? "" : "s")}).";
                if (result.InquiryId != null)
                    OpenInquiryRequested?.Invoke(result.InquiryId.Value);
            }
            catch (Exception ex)
            {
                StatusText = "Ingest failed: " + ex.Message;
            }
        }

        private async Task OpenFolderAsync()
        {
            var row = SelectedItem;
            if (row == null) return;
            var folder = InquiryInboxService.FolderPath(
                _service.Settings.InboxRoot, row.Ledger.RoutingKey, row.Ledger.Reference, row.Ledger.SubmissionId);
            var result = FileOpener.TryOpenFolder(folder);
            if (!result.IsSuccess) StatusText = result.Error ?? "Could not open the folder.";
        }

        public void Dispose()
        {
            _service.StateChanged -= OnServiceStateChanged;
            _notifier.Dispose();
        }
    }
}
