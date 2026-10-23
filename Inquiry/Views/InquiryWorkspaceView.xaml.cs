using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using ProGlassAutomation.Services.Inquiry;
using ProGlassAutomation.Services.Inquiry.InquiryInbox;
using ProGlassAutomation.ViewModels.Inquiry;

namespace ProGlassAutomation.Inquiry;

/// <summary>
/// The Inquiry module's workspace: owns the single list view-model and
/// switches register ↔ inbox ↔ detail in place. Created once per shell
/// (a long-lived tab item); the local SQLite database (schema migrations
/// included) is initialized here on first open, before the register's
/// first query runs.
///
/// Website inbox: a separate page (owner spec) fed by
/// <see cref="InquiryInboxService"/> — pull, verify, stage, rename,
/// acknowledge — with a 1-minute watch tick that honours the configured
/// automatic-check interval, plus a tray notification the moment a new
/// delivery arrives. Offline is a neutral state, never an error.
/// </summary>
public partial class InquiryWorkspaceView : UserControl
{
    private enum Page { List, Inbox }

    private readonly InquiryRepository _repository;
    private readonly InquiryListViewModel _list;
    private readonly InquiryInboxService _inboxService;
    private readonly InquiryInboxIngestService _ingestService;
    private readonly InquiryInboxNotifier _notifier;

    private InquiryInboxViewModel? _inbox;
    private DispatcherTimer? _syncTimer;
    private DateTime _lastAutoSyncUtc = DateTime.MinValue;
    private Page _returnTo = Page.List;
    private bool _started;

    public InquiryWorkspaceView()
    {
        _repository = new InquiryRepository();
        // One-time, fast: ensure schema + migrations before any query.
        // A failure here must not take the shell down — the register's
        // first load reports it on its error surface.
        try
        {
            _repository.InitializeAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Surface on the list page instead of crashing the window.
        }

        _list = new InquiryListViewModel(_repository);
        _list.EditRequested = RunEditorAsync;
        _list.OpenAttachmentsFolderRequested = row =>
        {
            var result = FileOpener.TryOpenFolder(InquiryAttachmentService.StorageFolder(row.Id));
            if (!result.IsSuccess)
                MessageBox.Show(Window.GetWindow(this), result.Error, "Attachments", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
        _list.ExportPathRequested = () =>
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export filtered inquiries",
                Filter = "CSV file (*.csv)|*.csv",
                FileName = "inquiries.csv"
            };
            return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
        };
        _list.ImportPathRequested = () =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import inquiries from CSV",
                Filter = "CSV file (*.csv)|*.csv"
            };
            return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FileName : null;
        };
        _list.ConfirmImportRequested = count =>
            MessageBox.Show(Window.GetWindow(this),
                $"Import {count} inquiries from the file? Existing inquiry numbers keep their records; duplicates get new numbers.",
                "Import inquiries?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;
        _list.ConfirmDeleteInquiryRequested = row =>
            MessageBox.Show(Window.GetWindow(this),
                $"Delete inquiry {row.InquiryNumber} ({row.CustomerName})?\nAttachments and line items are removed too. This cannot be undone.",
                "Delete inquiry?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;

        // ── Website inbox wiring ────────────────────────────────────────
        _inboxService = new InquiryInboxService(InquiryInboxSettings.Load());
        _ingestService = new InquiryInboxIngestService(
            _repository,
            new InquiryWorkflowService(_repository),
            new InquiryAttachmentService(_repository),
            _inboxService);
        _notifier = new InquiryInboxNotifier();
        _notifier.EnsureCreated();
        _notifier.DoubleClicked += (_, _) =>
            Dispatcher.BeginInvoke(new Action(ShowInboxPage));
        _notifier.SetSoundEnabled(_inboxService.Settings.NotificationSound);
        _inboxService.StateChanged += OnInboxStateChanged;

        _list.OpenInboxRequested = ShowInboxPage;

        InitializeComponent();
        ListView.DataContext = _list;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started) return;
        _started = true;

        // Adopt READY folders already on disk (Node bridge parity), then
        // the first remote check when configured.
        _ = StartInboxAsync();

        _syncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _syncTimer.Tick += OnSyncTick;
        _syncTimer.Start();
    }

    private async System.Threading.Tasks.Task StartInboxAsync()
    {
        try
        {
            _inboxService.ScanLocalInbox();
            RefreshListInboxCard();
            if (_inboxService.Settings.IsConfigured)
                await _inboxService.SynchronizeAsync();
            RefreshListInboxCard();
        }
        catch
        {
            // The inbox must never take the register down.
        }
    }

    private void OnSyncTick(object? sender, EventArgs e)
    {
        if (!_inboxService.Settings.IsConfigured) return;
        var dueMinutes = Math.Clamp(_inboxService.Settings.PollMinutes, 1, 120);
        if ((DateTime.UtcNow - _lastAutoSyncUtc).TotalMinutes < dueMinutes) return;
        _lastAutoSyncUtc = DateTime.UtcNow;
        _ = SafeAutoSyncAsync();
    }

    private async System.Threading.Tasks.Task SafeAutoSyncAsync()
    {
        try
        {
            await _inboxService.SynchronizeAsync();
        }
        catch
        {
            // A failed automatic check is invisible by design; the next
            // tick retries and the user can always press Check now.
        }
    }

    private void OnInboxStateChanged(object? sender, EventArgs e)
    {
        // Service events arrive off the UI thread — marshal.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _lastAutoSyncUtc = DateTime.UtcNow;
            RefreshListInboxCard();
        }));
    }

    private void RefreshListInboxCard()
    {
        try
        {
            _list.UpdateInboxCard(_inboxService.GetSummary());
        }
        catch
        {
            // The register card is a courtesy; never break the list.
        }
    }

    // ── Page switching ──────────────────────────────────────────────

    private void ShowInboxPage()
    {
        EnsureInboxViewModel();
        ListView.Visibility = Visibility.Collapsed;
        InboxView.Visibility = Visibility.Visible;
    }

    private void EnsureInboxViewModel()
    {
        if (_inbox != null) return;
        _inbox = new InquiryInboxViewModel(_inboxService, _ingestService, _notifier);
        _inbox.BackRequested = ShowListPage;
        _inbox.OpenInquiryRequested = id => _ = OpenInquiryFromInboxAsync(id);
        _inbox.RegisterDirty += (_, _) =>
        {
            RefreshListInboxCard();
            _list.Reload();
        };
        InboxView.DataContext = _inbox;
    }

    private void ShowListPage()
    {
        InboxView.Visibility = Visibility.Collapsed;
        ListView.Visibility = Visibility.Visible;
        // Returning from the inbox: an ingest or an editor save may have
        // changed the register. (The list's own edit flow reloads itself
        // and never passes through here.)
        _list.Reload();
    }

    private async System.Threading.Tasks.Task OpenInquiryFromInboxAsync(int inquiryId)
    {
        _returnTo = Page.Inbox;
        var record = await _repository.GetDetailAsync(inquiryId);
        if (record != null)
            await RunEditorAsync(record);
    }

    // ── Detail editor ───────────────────────────────────────────────

    /// <summary>
    /// Opens the detail editor for <paramref name="source"/> (null for a
    /// new inquiry) and blocks until it closes. Returns true when the
    /// editor saved, so the register can reset to page 1 and reload.
    /// Returns to whichever page (register / inbox) opened it.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> RunEditorAsync(Models.Inquiry.InquiryRecord? source)
    {
        var editor = new InquiryEditorViewModel(
            _repository,
            new InquiryWorkflowService(_repository),
            new InquiryAttachmentService(_repository),
            source);

        var closed = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        editor.CloseRequested += saved => closed.TrySetResult(saved);

        EditorView.DataContext = editor;
        EditorView.Visibility = Visibility.Visible;
        ListView.Visibility = Visibility.Collapsed;
        InboxView.Visibility = Visibility.Collapsed;
        EditorView.Focus();

        bool saved = await closed.Task;

        EditorView.DataContext = null;
        EditorView.Visibility = Visibility.Collapsed;
        editor.CancelLoaders();

        if (_returnTo == Page.Inbox)
        {
            EnsureInboxViewModel();
            InboxView.Visibility = Visibility.Visible;
            _returnTo = Page.List;
        }
        else
        {
            ListView.Visibility = Visibility.Visible;
        }
        return saved;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // The workspace is a long-lived tab item; this effectively means
        // the application is shutting down — release the tray icon.
        _syncTimer?.Stop();
        _inbox?.Dispose();
        _inboxService.StateChanged -= OnInboxStateChanged;
        _inboxService.Dispose();
        _notifier.Dispose();
    }
}
