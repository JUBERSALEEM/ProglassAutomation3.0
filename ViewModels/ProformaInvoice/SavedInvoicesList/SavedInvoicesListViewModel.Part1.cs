// ═══════════════════════════════════════════════════════════════════════
// FILE: SavedInvoicesListViewModel.Part1.cs (Phase 3.8 split — member census verified)
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Workflow;
using ProGlassAutomation.Models.ProformaInvoice.Common;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Csv;
using ProGlassAutomation.Services.Export.Excel;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.Services.JobOrder.Mapper;
using ProGlassAutomation.Services.ProformaInvoice.Attachments;
using ProGlassAutomation.Services.ProformaInvoice.Export;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Documents;
using ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository;
using ProGlassAutomation.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProGlassAutomation.Utilities;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ProGlassAutomation.Services.Localization;
using ProGlassAutomation.Services.Dialogs;
using ProGlassAutomation.Services.JobOrder.Repository;
using Microsoft.Extensions.DependencyInjection;
using ProGlassAutomation.Views.ProformaInvoice.SavedInvoicesList;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.SavedInvoicesList
{
        public partial class SavedInvoicesListViewModel : ViewModelBase, IDisposable
    {
            private readonly IDialogService _dialogs = null!;
        private readonly IDatabaseServices _database = null!;
        private readonly IProformaInvoiceRepository _invoices = null!;
        private readonly IJobOrderRepository _jobs = null!;
        private readonly IStatusService _status = null!;

        public SavedInvoicesListViewModel(IDialogService? dialogs, IProformaInvoiceRepository? invoices, IJobOrderRepository? jobs, IStatusService? status, IDatabaseServices? database)
        {
            _dialogs = dialogs!;
            _invoices = invoices!;
            _jobs = jobs!;
            _status = status!;
            _database = database!;
        
            // ERR-633EEB: run shared initialization on EVERY construction
            // path. DI selects this (greediest) ctor, so without this call the
            // parameterless ctor's setup never ran and commands stayed null.
            InitializeCore();
        }

        // Phase 4: Named handler for static event — detachable
        private Func<Task>? _dataSeededHandler;
        private bool _disposed;

        private int _loadingFlag;
        private int _reloadQueued;
        private int _interactiveLoad;
        private bool _hasCompletedLoad;
        private DispatcherTimer? _autoRefresh;
        private NextActionKind _nextAction = NextActionKind.None;

        public string Title { get; } = "SAVED PROFORMA INVOICES";
        public string Subtitle { get; } = "Browse, load, duplicate, or delete previously saved invoices.";

        public Action<int> OnInvoiceLoadRequested { get; set; }

        /// <summary>
        /// Fired when approving and the invoice has no Step 8 attachments:
        /// the shell opens that invoice in the wizard AT Step 8 (Attachments)
        /// and runs the single APPROVE INVOICE action there (one picker for
        /// images AND PDFs — the type is auto-detected). 2026-09-10: the
        /// uploadPdf bool died with the merged choice dialog.
        /// </summary>
        public Action<int> OnInvoiceApproveRequested { get; set; }

        private List<SavedInvoiceListItem> _allItems = new();
        public BulkObservableCollection<SavedInvoiceListItem> FilteredItems { get; } = new();

        public List<string> StatusFilterOptions { get; } = new()
        {
            "All Statuses",
            "PI created", "PI sent", "PI confirmed", "Full job created",
            "Partially delivered", "Fully delivered",
            "Partially invoiced", "Fully invoiced", "Cancelled"
        };

        public List<string> SortPresetOptions { get; } = new()
        {
            "Newest First", "Oldest First", "Highest Value", "Lowest Value",
            "Company A-Z", "Company Z-A"
        };

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); ApplyFilters(); }
        }

        // ERP lifecycle hardening (Phase 1): voided invoices are hidden by
        // default; flip this to include them (reports/VAT always see them).
        private bool _showVoided;
        public bool ShowVoided
        {
            get => _showVoided;
            set { if (_showVoided == value) return; _showVoided = value; OnPropertyChanged(); ApplyFilters(); }
        }

        private string _selectedStatusFilter = "All Statuses";
        public string SelectedStatusFilter
        {
            get => _selectedStatusFilter;
            set { if (_selectedStatusFilter == value) return; _selectedStatusFilter = value; OnPropertyChanged(); ApplyFilters(); }
        }

        private DateTime? _fromDate;
        public DateTime? FromDate
        {
            get => _fromDate;
            set { if (_fromDate == value) return; _fromDate = value; OnPropertyChanged(); ApplyFilters(); }
        }

        private DateTime? _toDate;
        public DateTime? ToDate
        {
            get => _toDate;
            set { if (_toDate == value) return; _toDate = value; OnPropertyChanged(); ApplyFilters(); }
        }

        private string _selectedSortPreset = "Newest First";
        public string SelectedSortPreset
        {
            get => _selectedSortPreset;
            set { if (_selectedSortPreset == value) return; _selectedSortPreset = value; OnPropertyChanged(); ApplyFilters(); }
        }

        private int _totalCount;
        public int TotalCount
        {
            get => _totalCount;
            private set { _totalCount = value; OnPropertyChanged(); }
        }

        private int _filteredCount;
        public int FilteredCount
        {
            get => _filteredCount;
            private set { _filteredCount = value; OnPropertyChanged(); }
        }

        private decimal _totalValueSum;
        public decimal TotalValueSum
        {
            get => _totalValueSum;
            private set { _totalValueSum = value; OnPropertyChanged(); }
        }

        private SavedInvoiceListItem _selectedItem;
        public SavedInvoiceListItem SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem == value) return;
                _selectedItem = value;
                OnPropertyChanged();
                PublishSelectionWorkflow();
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            private set { _isLoading = value; OnPropertyChanged(); }
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            private set { if (_hasError == value) return; _hasError = value; OnPropertyChanged(); }
        }

        private string _errorMessage = "";
        public string ErrorMessage
        {
            get => _errorMessage;
            private set { if (_errorMessage == value) return; _errorMessage = value; OnPropertyChanged(); }
        }

        // BUG-75: user feedback for export operations
        private string _lastExportStatus = "";
        public string LastExportStatus
        {
            get => _lastExportStatus;
            private set
            {
                if (_lastExportStatus == value) return;
                _lastExportStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasExportStatus));
            }
        }
        public bool HasExportStatus => !string.IsNullOrEmpty(_lastExportStatus);

        private string _syncStatus = "Live sync starts with the first load.";
        public string SyncStatus
        {
            get => _syncStatus;
            private set { if (_syncStatus == value) return; _syncStatus = value; OnPropertyChanged(); }
        }

        private string _lastSyncedDisplay = "";
        public string LastSyncedDisplay
        {
            get => _lastSyncedDisplay;
            private set { if (_lastSyncedDisplay == value) return; _lastSyncedDisplay = value; OnPropertyChanged(); }
        }

        private bool _isBackgroundRefresh;
        public bool IsBackgroundRefresh
        {
            get => _isBackgroundRefresh;
            private set { if (_isBackgroundRefresh == value) return; _isBackgroundRefresh = value; OnPropertyChanged(); }
        }

        public IReadOnlyList<ProformaStageChip> StageChips { get; private set; } =
            Array.Empty<ProformaStageChip>();

        private string _workflowTitle = "Next step";
        public string WorkflowTitle
        {
            get => _workflowTitle;
            private set { if (_workflowTitle == value) return; _workflowTitle = value; OnPropertyChanged(); }
        }

        private string _workflowHint = "Select a saved invoice. The next step follows its live status.";
        public string WorkflowHint
        {
            get => _workflowHint;
            private set { if (_workflowHint == value) return; _workflowHint = value; OnPropertyChanged(); }
        }

        private string _nextActionLabel = "Select an invoice";
        public string NextActionLabel
        {
            get => _nextActionLabel;
            private set { if (_nextActionLabel == value) return; _nextActionLabel = value; OnPropertyChanged(); }
        }

        private bool _isNextActionEnabled;
        public bool IsNextActionEnabled
        {
            get => _isNextActionEnabled;
            private set { if (_isNextActionEnabled == value) return; _isNextActionEnabled = value; OnPropertyChanged(); }
        }

        private string _emptyTitle = "No saved invoices found";
        public string EmptyTitle
        {
            get => _emptyTitle;
            private set { if (_emptyTitle == value) return; _emptyTitle = value; OnPropertyChanged(); }
        }

        private string _emptyMessage = "Create one with New inquiry or Blank.";
        public string EmptyMessage
        {
            get => _emptyMessage;
            private set { if (_emptyMessage == value) return; _emptyMessage = value; OnPropertyChanged(); }
        }

        // ── Commands ─────────────────────────────────────────────────────────

        public ICommand RefreshCommand { get; private set; }
        public ICommand ClearFiltersCommand { get; private set; }
        public ICommand LoadInvoiceCommand { get; private set; }
        public ICommand DuplicateInvoiceCommand { get; private set; }
        public ICommand CreateRevisionCommand { get; private set; }
        public ICommand DeleteInvoiceCommand { get; private set; }
        public ICommand ChangeStatusCommand { get; private set; }
        public ICommand ExportPdfFromListCommand { get; private set; }
        public ICommand ExportExcelFromListCommand { get; private set; }
        public ICommand ExportCsvFromListCommand { get; private set; }   // BUG-76
        public ICommand ShareFromListCommand { get; private set; } = null!;

        /// <summary>Full dossier, customer-safe — NO profit &amp; loss.</summary>
        public ICommand ExportDossierCommand { get; private set; }

        /// <summary>Full dossier INCLUDING profit &amp; loss. Manager/Admin only.</summary>
        public ICommand ExportInternalDossierCommand { get; private set; }

        /// <summary>Dossier as PDF, customer-safe — NO profit &amp; loss.</summary>
        public ICommand ExportDossierPdfCommand { get; private set; }

        /// <summary>Dossier as PDF INCLUDING profit &amp; loss. Manager/Admin only.</summary>
        public ICommand ExportInternalDossierPdfCommand { get; private set; }

        // v2: quick "today" filter + share the filtered list as Excel
        public ICommand SetTodayFilterCommand { get; private set; }
        public ICommand ExportFilteredExcelCommand { get; private set; }
        public ICommand SelectStageCommand { get; private set; }
        public ICommand NextActionCommand { get; private set; }

        // v2: version history (§24)
        public ICommand ShowVersionsCommand { get; private set; }

        /// <summary>
        /// v2: fired when the user restores an old version — payload is
        /// the version's snapshot JSON. The wizard shell opens it.
        /// </summary>
        public Action<string>? OnSnapshotRestoreRequested { get; set; }

        /// <summary>
        /// Opens the live job order for a saved invoice. The host navigates
        /// to Job Order. The load picker has no host, so the action explains
        /// that instead of switching modules from a dialog.
        /// </summary>
        public Action<int>? OnOpenJobRequested { get; set; }

        public SavedInvoicesListViewModel()
         : this(
            App.ServiceProvider?.GetService<IDialogService>()?? new DialogService(),
            App.ServiceProvider?.GetService<IProformaInvoiceRepository>()!,
            App.ServiceProvider?.GetService<IJobOrderRepository>()!,
            App.ServiceProvider?.GetService<IStatusService>()!,
            App.ServiceProvider?.GetService<IDatabaseServices>()!){ }

        // ── Shared initialization (ERR-633EEB) ─────────────────────
        // Extracted verbatim from the parameterless ctor so both it
        // and the DI-selected parameterized ctor initialize fully.
        private void InitializeCore()
        {
            RefreshCommand = new RelayCommand(async _ =>
            {
                Interlocked.Exchange(ref _interactiveLoad, 1);
                await LoadInvoicesAsync();
            });

            // Phase 4: Named handler — detachable, no lambda leak
            _dataSeededHandler = async () => { try { await LoadInvoicesAsync(); } catch (Exception ex) { AppLogService.Warning($"Suppressed exception: {ExceptionMessages.Deep(ex)}"); } };
            ProGlassAutomation.Services.SampleData.SampleDataService.DataSeeded += _dataSeededHandler;

            // Instant refresh when this PI changes OR when a related JO / DN /
            // TI is created or deleted. Display status is derived from those
            // documents, so a delete in another module must reload this list
            // without waiting for a manual Refresh.
            _database?.SubscribeDocumentsChanged(LoadInvoicesAsync);
            ClearFiltersCommand = new RelayCommand(_ => ClearFilters());
            LoadInvoiceCommand = new RelayCommand(p => RequestLoad(p as SavedInvoiceListItem));

            DuplicateInvoiceCommand = new RelayCommand(
                async p => await DuplicateAsync(p as SavedInvoiceListItem));
            CreateRevisionCommand = new RelayCommand(
                async p => await CreateRevisionAsync(p as SavedInvoiceListItem));
            DeleteInvoiceCommand = new RelayCommand(
                async p => await DeleteAsync(p as SavedInvoiceListItem));

            // BUG-72 [HIGH]: accept Tuple parameter matching code-behind
            ChangeStatusCommand = new RelayCommand(async p =>
            {
                switch (p)
                {
                    // View code-behind CreateStatusItem returns MenuItem
                    // that fires an event capturing the tuple pattern:
                    case Tuple<SavedInvoiceListItem, string> tuple
                        when tuple.Item1 != null && !string.IsNullOrWhiteSpace(tuple.Item2):
                        await ChangeStatusAsync(tuple.Item1, tuple.Item2);
                        break;

                    case ValueTuple<SavedInvoiceListItem, string> vtuple
                        when vtuple.Item1 != null && !string.IsNullOrWhiteSpace(vtuple.Item2):
                        await ChangeStatusAsync(vtuple.Item1, vtuple.Item2);
                        break;

                    // Backward-compat: original object[] shape
                    case object[] arr when arr.Length == 2
                        && arr[0] is SavedInvoiceListItem item
                        && arr[1] is string newStatus:
                        await ChangeStatusAsync(item, newStatus);
                        break;
                }
            });

            ExportPdfFromListCommand = new RelayCommand(
                async p => await ExportPdfFromListAsync(p as SavedInvoiceListItem));
            ExportExcelFromListCommand = new RelayCommand(
                async p => await ExportExcelFromListAsync(p as SavedInvoiceListItem));

            // BUG-76: CSV export from list for parity with detail view
            ExportCsvFromListCommand = new RelayCommand(
                async p => await ExportCsvFromListAsync(p as SavedInvoiceListItem));

            // Sharing Center: staged export (rule's default format) + composer.
            ShareFromListCommand = new RelayCommand(
                async p => await ShareFromListAsync(p as SavedInvoiceListItem));

            // Full dossier. The customer-safe variant is the plain command;
            // the P&L one is deliberately separate and separately gated, so
            // margin can never leave the building by accident.
            ExportDossierCommand = new RelayCommand(
                async p => await ExportDossierAsync(p as SavedInvoiceListItem, false));
            ExportInternalDossierCommand = new RelayCommand(
                async p => await ExportDossierAsync(p as SavedInvoiceListItem, true));

            // Same P&L gate either way — the format is not a loophole.
            ExportDossierPdfCommand = new RelayCommand(
                async p => await ExportDossierAsync(p as SavedInvoiceListItem, false, true));
            ExportInternalDossierPdfCommand = new RelayCommand(
                async p => await ExportDossierAsync(p as SavedInvoiceListItem, true, true));

            // v2: one tap → show today's invoices (FromDate/ToDate both today)
            SetTodayFilterCommand = new RelayCommand(_ =>
            {
                SelectedStatusFilter = "All Statuses";
                FromDate = DateTime.Today;
                ToDate = DateTime.Today;
            });

            // v2: version history per invoice
            ShowVersionsCommand = new RelayCommand(
                async p => await ShowVersionsAsync(p as SavedInvoiceListItem));

            // v2: share the CURRENT FILTERED list as one styled Excel table
            ExportFilteredExcelCommand = new RelayCommand(
                async _ => await ExportFilteredExcelAsync());

            SelectStageCommand = new RelayCommand(p =>
            {
                string key = p as string ?? "All Statuses";
                if (!StatusFilterOptions.Contains(key))
                    key = "All Statuses";
                SelectedStatusFilter = key;
            });
            NextActionCommand = new RelayCommand(async _ => await RunNextActionAsync());

            StartAutoRefresh();
            Interlocked.Exchange(ref _interactiveLoad, 1);
            _ = LoadInvoicesAsync();
        }

        /// <summary>
        /// Quiet refresh while this register is alive. Document writes already
        /// raise DocumentLifecycle; the timer covers a second workstation or a
        /// missed event without covering the grid or opening a dialog.
        /// </summary>
        private void StartAutoRefresh()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            _autoRefresh = new DispatcherTimer(
                TimeSpan.FromSeconds(20),
                DispatcherPriority.Background,
                (_, _) =>
                {
                    if (_disposed) return;
                    _ = LoadInvoicesAsync();
                },
                dispatcher);
        }

        /// <summary>
        /// v2: version history dialog. Restore hands the snapshot JSON
        /// to the wizard shell via OnSnapshotRestoreRequested.
        /// </summary>
        private async Task ShowVersionsAsync(SavedInvoiceListItem? item)
        {
            if (item == null) return;

            var versions = await VersionHistoryService
                .GetVersionsAsync("PI", item.ProformaId);

            var restoreId = Views.ProformaInvoice.SavedInvoicesList
                .VersionsDialog.Show(
                    Application.Current.MainWindow,
                    item.ProformaId, versions,
                    canRestore: OnSnapshotRestoreRequested != null);

            if (restoreId == null) return;

            var full = await VersionHistoryService
                .GetByIdAsync(restoreId.Value);

            if (full == null || string.IsNullOrEmpty(full.SnapshotJson))
                return;

            OnSnapshotRestoreRequested?.Invoke(full.SnapshotJson);

            AuditService.Log(
                AuditSections.Proforma, "Version restored",
                $"{item.ProformaId} → version {full.VersionNo} " +
                $"(by {full.Username})",
                "PI", item.ProformaId);
        }

        /// <summary>
        /// v2: exports whatever the filter currently shows (e.g. today's
        /// prepared invoices) as a single Excel register using the shared
        /// export engine + WorkbookStyle.
        /// </summary>
        private async Task ExportFilteredExcelAsync()
        {
            if (!ProGlassAutomation.Services.Audit.PermissionService.Require(
                ProGlassAutomation.Services.Audit.AppPermissions.ProformaExport, "export the invoice register"))
                return;

            try
            {
                var items = FilteredItems.ToList();

                if (items.Count == 0)
                {
                    _dialogs.Show(
                        LocalizationService.Instance.T("Msg.NothingToExportTheCurrent"),
                        LocalizationService.Instance.T("DeliveryNote.ExportExcel"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                bool isToday = FromDate == DateTime.Today &&
                               ToDate == DateTime.Today;

                string title = isToday
                    ? $"PROFORMA INVOICES PREPARED {DateTime.Today:dd MMM yyyy}"
                    : "PROFORMA INVOICE REGISTER";

                var layout = new ProformaInvoiceListExcelLayout(items, title);
                var engine = new ExcelExportEngine();

                var result = await engine.ExportAsync(layout,
                    new ExportRequest
                    {
                        PromptForLocation = true,
                        OpenAfterSave = true
                    });

                LastExportStatus = result.Success
                    ? $"✓ Exported {items.Count} invoices to Excel"
                    : result.Cancelled ? "" : "✗ Excel export failed";

                if (result.Success)
                    ProGlassAutomation.Services.Audit.AuditService.Log(
                        ProGlassAutomation.Services.Audit.AuditSections.Proforma,
                        "Invoice list exported",
                        $"{items.Count} invoices → Excel ({title})",
                        "PI", "REGISTER");
            }
            catch (Exception ex)
            {
                LastExportStatus = $"✗ Excel export failed: {ExceptionMessages.Deep(ex)}";
                _dialogs.Show(
                    string.Format(LocalizationService.Instance.T("Msg.ExcelExportFailedX"), ExceptionMessages.Deep(ex)),
                    LocalizationService.Instance.T("Msg.CapExportFailed"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ── Data loading ──────────────────────────────────────────────────────

        /// <summary>
        /// BUG-73 [HIGH]: Only called on explicit Refresh, initial load, and
        /// after mutations (delete/duplicate/status change). Filter changes
        /// no longer trigger this — filtering is now in-memory only.
        /// </summary>
        public async Task LoadInvoicesAsync()
        {
            if (Interlocked.CompareExchange(ref _loadingFlag, 1, 0) != 0)
            {
                Interlocked.Exchange(ref _reloadQueued, 1);
                return;
            }

            try
            {
                do
                {
                    Interlocked.Exchange(ref _reloadQueued, 0);
                    await LoadInvoicesCoreAsync();
                }
                while (Interlocked.CompareExchange(ref _reloadQueued, 0, 1) == 1);
            }
            finally
            {
                Interlocked.Exchange(ref _loadingFlag, 0);
                if (Interlocked.CompareExchange(ref _reloadQueued, 0, 1) == 1)
                    _ = LoadInvoicesAsync();
            }
        }

        private async Task LoadInvoicesCoreAsync()
        {
            bool interactive = Interlocked.Exchange(ref _interactiveLoad, 0) == 1
                || !_hasCompletedLoad;
            if (interactive)
            {
                IsLoading = true;
                HasError = false;
                ErrorMessage = "";
            }
            else
            {
                // Keep the last live line. A 20-second tick must not flash
                // "Syncing" over the register the operator is reading.
                IsBackgroundRefresh = true;
            }

            try
            {
                if (_invoices == null || _database == null)
                    throw new InvalidOperationException("Proforma Invoice database services are not initialized.");

                // PERF-9: list-page projection — GetAllAsync() dragged every
                // FullSnapshotJson blob (whole invoice) into memory just to
                // render one row per invoice. GetListRowsAsync() reads only
                // the scalar columns this page binds.
                var entities = await _invoices.GetListRowsAsync();

                var progress = await DocumentProgressSnapshot.LoadAsync();
                var confirmedWithAttachments = await LoadApprovalProofStateAsync(entities);

                _allItems = entities.Select(e =>
                {
                    var item = new SavedInvoiceListItem
                    {
                        Id = e.Id,
                        ProformaId = e.ProformaId,
                        CompanyName = e.CompanyName,
                        CustomerName = e.CustomerName,
                        ProjectName = e.ProjectName,
                        Status = e.Status,
                        GrandTotal = e.GrandTotalVatInclusive,
                        CreatedAt = e.CreatedAt,
                        UpdatedAt = e.UpdatedAt,
                        IsVoid = e.IsVoid
                    };

                    bool hasApprovalProof = confirmedWithAttachments.Contains(e.ProformaId ?? "");
                    var ls = DocumentListStatus.ForProforma(
                        e.Status ?? "Draft",
                        hasApprovalProof,
                        progress.ForProforma(e.ProformaId));
                    item.DisplayStatus = ls.Status;
                    item.DisplayStatusBrushKey = ls.BrushKey;
                    return item;
                }).ToList();

                TotalCount = _allItems.Count;
                _hasCompletedLoad = true;
                ApplyFilters();
                SyncStatus = "Live. Job, delivery, and tax invoice changes refresh this list.";
                LastSyncedDisplay = "Refreshed " + DateTime.Now.ToString("g");
                HasError = false;
                ErrorMessage = "";
            }
            catch (Exception ex)
            {
                // A background tick must not hide a good list or pop a dialog
                // over the operator. An explicit Refresh, or a first load with
                // nothing to show, still surfaces the failure.
                bool keepLastList = !interactive && _allItems.Count > 0;
                if (keepLastList)
                {
                    SyncStatus = "Live sync failed. Showing the last list. Press Refresh to retry.";
                }
                else
                {
                    HasError = true;
                    ErrorMessage = "Could not refresh saved invoices. Retry with Refresh. " + ExceptionMessages.Deep(ex);
                    SyncStatus = "Could not refresh saved invoices.";
                    if (interactive)
                    {
                        _dialogs?.Show(
                            string.Format(LocalizationService.Instance.T("Msg.FailedToLoadSavedInvoices"), ExceptionMessages.Deep(ex)),
                            LocalizationService.Instance.T("Msg.CapLoadError"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
            finally
            {
                IsLoading = false;
                IsBackgroundRefresh = false;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  LIFECYCLE STATUS (computed list status)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Loads Step 8 confirmation proof state once for the current list.
        /// A PI is only shown as confirmed when it is stored Approved AND it
        /// has at least one attachment uploaded through Step 8.
        ///
        /// PERF-10: bounded concurrency. The old code started one
        /// File.ReadAllTextAsync + JSON deserialize per invoice ALL AT ONCE
        /// (Task.WhenAll over an unbounded projection). With hundreds of
        /// invoices that thrashed the disk cache and stalled the page for
        /// seconds. A fixed 8-way gate keeps the read pipeline saturated
        /// without flooding the IO subsystem, and results only need
        /// manifest.Count > 0, so the JSON is only touched for files that
        /// exist (File.Exists probe is a single metadata call).
        /// </summary>
        private static readonly SemaphoreSlim ManifestReadGate =
            new(8, 8);

        private async Task<HashSet<string>> LoadApprovalProofStateAsync(
            IEnumerable<ProformaListRow> entities)
        {
            var confirmed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var service = new ProformaAttachmentService();
                var ids = (entities ?? Enumerable.Empty<ProformaListRow>())
                    .Select(x => x.ProformaId)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var results = await Task.WhenAll(ids.Select(async id =>
                {
                    // Gate file IO concurrency — bounded, not unbounded.
                    await ManifestReadGate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        var manifest = await service.LoadManifestAsync(id!)
                            .ConfigureAwait(false);
                        return (Id: id!, HasProof: manifest.Count > 0);
                    }
                    finally
                    {
                        ManifestReadGate.Release();
                    }
                }));

                foreach (var row in results)
                    if (row.HasProof)
                        confirmed.Add(row.Id);
            }
            catch (Exception ex)
            {
                AppLogService.Warning($"Suppressed exception: {ExceptionMessages.Deep(ex)}");
            }

            return confirmed;
        }



        /// <summary>
        /// BUG-73/74: Pure in-memory filter over _allItems.
        /// Uses standard Clear() + AddRange pattern which is what WPF
        /// virtualization expects. Incremental diff would be premature
        /// optimization at typical invoice counts (dozens to low hundreds).
        /// </summary>
        private void ApplyFilters()
        {
            IEnumerable<SavedInvoiceListItem> query = _allItems;

            // ERP lifecycle hardening (Phase 1): hide voided (soft-closed)
            // invoices unless the user explicitly asks to see them.
            if (!_showVoided)
                query = query.Where(x => !x.IsVoid);

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string s = _searchText.Trim().ToLowerInvariant();
                query = query.Where(x =>
                    (x.ProformaId ?? "").ToLowerInvariant().Contains(s)
                    || (x.CompanyName ?? "").ToLowerInvariant().Contains(s)
                    || (x.CustomerName ?? "").ToLowerInvariant().Contains(s)
                    || (x.ProjectName ?? "").ToLowerInvariant().Contains(s));
            }

            if (_fromDate.HasValue)
                query = query.Where(x => x.CreatedAt.Date >= _fromDate.Value.Date);
            if (_toDate.HasValue)
                query = query.Where(x => x.CreatedAt.Date <= _toDate.Value.Date);

            // Stage counts ignore the stage chip itself, so every chip stays
            // a real count of the current search and date window.
            var stageScope = query.ToList();
            PublishStageChips(stageScope);

            IEnumerable<SavedInvoiceListItem> filtered = stageScope;
            if (!string.IsNullOrWhiteSpace(_selectedStatusFilter)
                && _selectedStatusFilter != "All Statuses")
                filtered = filtered.Where(x => string.Equals(
                    x.DisplayStatus, _selectedStatusFilter,
                    StringComparison.OrdinalIgnoreCase));

            filtered = _selectedSortPreset switch
            {
                "Oldest First" => filtered.OrderBy(x => x.CreatedAt),
                "Highest Value" => filtered.OrderByDescending(x => x.GrandTotal),
                "Lowest Value" => filtered.OrderBy(x => x.GrandTotal),
                "Company A-Z" => filtered.OrderBy(x => x.CompanyName),
                "Company Z-A" => filtered.OrderByDescending(x => x.CompanyName),
                _ => filtered.OrderByDescending(x => x.CreatedAt)
            };

            var result = filtered.ToList();
            int? keepId = _selectedItem?.Id;

            // BUG-74: standard clear + refill pattern
            FilteredItems.ClearSilent();
            FilteredItems.AddRangeSilent(result);
            FilteredItems.RaiseReset();

            FilteredCount = result.Count;
            TotalValueSum = result.Sum(x => x.GrandTotal);
            PublishEmptyCopy();

            var match = keepId == null
                ? null
                : result.FirstOrDefault(x => x.Id == keepId.Value);
            if (!ReferenceEquals(_selectedItem, match))
                SelectedItem = match;
            else
                PublishSelectionWorkflow();
        }

        private void PublishStageChips(IReadOnlyList<SavedInvoiceListItem> stageScope)
        {
            var chips = new List<ProformaStageChip>(StatusFilterOptions.Count);
            foreach (string option in StatusFilterOptions)
            {
                int count = option == "All Statuses"
                    ? stageScope.Count
                    : stageScope.Count(x => string.Equals(
                        x.DisplayStatus, option, StringComparison.OrdinalIgnoreCase));
                chips.Add(new ProformaStageChip
                {
                    Key = option,
                    Label = count.ToString("N0") + "  " + ShortStageLabel(option),
                    Tip = option + " · " + count.ToString("N0"),
                    IsSelected = string.Equals(
                        _selectedStatusFilter, option, StringComparison.Ordinal)
                });
            }

            StageChips = chips;
            OnPropertyChanged(nameof(StageChips));
        }

        private static string ShortStageLabel(string option) => option switch
        {
            "All Statuses" => "All",
            "PI created" => "Created",
            "PI sent" => "Sent",
            "PI confirmed" => "Confirmed",
            "Full job created" => "Job",
            "Partially delivered" => "Part delivered",
            "Fully delivered" => "Delivered",
            "Partially invoiced" => "Part invoiced",
            "Fully invoiced" => "Invoiced",
            "Cancelled" => "Cancelled",
            _ => option
        };

        private static bool IsJobOwnedStatus(string display) =>
            display == DocumentListStatus.FullJobCreated
            || display == DocumentListStatus.PartiallyDelivered
            || display == DocumentListStatus.FullyDelivered
            || display == DocumentListStatus.PartiallyInvoiced
            || display == DocumentListStatus.FullyInvoiced;

        private async Task OpenRelatedJobAsync(SavedInvoiceListItem item)
        {
            if (item == null) return;
            if (_jobs == null)
            {
                _dialogs.Show(
                    "Job orders are not available from this window.",
                    LocalizationService.Instance.T("Dashboard.JobOrders"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var jobs = await _jobs.GetBySourceProformaIdAsync(item.ProformaId ?? "");
            var live = jobs?
                .Where(j => j != null && !j.IsVoid)
                .OrderByDescending(j => j.UpdatedAt)
                .FirstOrDefault();
            if (live == null)
            {
                _dialogs.Show(
                    "No live job order is linked to " + (item.ProformaId ?? "this invoice") + " yet.",
                    LocalizationService.Instance.T("Dashboard.JobOrders"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (OnOpenJobRequested == null)
            {
                _dialogs.Show(
                    "Job order " + live.JobOrderId + " is linked. Open it from the Job Order module. This load picker cannot switch modules.",
                    LocalizationService.Instance.T("Dashboard.JobOrders"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            OnOpenJobRequested(live.Id);
        }

        private void PublishEmptyCopy()
        {
            if (TotalCount == 0)
            {
                EmptyTitle = LocalizationService.Instance.T("ProformaInvoice.NoSavedInvoicesFound");
                EmptyMessage = "Create one with New inquiry or Blank. Nothing is saved yet.";
            }
            else
            {
                EmptyTitle = "No invoices match";
                EmptyMessage = "Clear the stage, search, or dates. Saved invoices are still in the register.";
            }
        }

        private void PublishSelectionWorkflow()
        {
            var item = _selectedItem;
            if (item == null)
            {
                WorkflowTitle = "Next step";
                WorkflowHint = "Select a saved invoice. The next step follows its live status.";
                NextActionLabel = "Select an invoice";
                IsNextActionEnabled = false;
                _nextAction = NextActionKind.None;
                return;
            }

            string stored = item.Status ?? "";
            string display = item.DisplayStatus ?? "";
            WorkflowTitle = (item.ProformaId ?? "Invoice") + " · " + display;

            if (item.IsVoid || DocumentListStatus.IsCancelledProforma(stored))
            {
                NextActionLabel = "Create revision";
                WorkflowHint = "A cancelled invoice stays on record. A revision is a new draft; the original is not rewritten.";
                _nextAction = NextActionKind.Revision;
            }
            else if (string.Equals(stored, "Expired", StringComparison.OrdinalIgnoreCase))
            {
                NextActionLabel = "Reopen as draft";
                WorkflowHint = "Expired can return to Draft, then be sent again. Confirmation still happens in Step 8.";
                _nextAction = NextActionKind.ReopenDraft;
            }
            else if (display == DocumentListStatus.PiConfirmed)
            {
                NextActionLabel = "Create job order";
                WorkflowHint = "This invoice is confirmed, but no job order is linked yet. This creates or releases it without approving again.";
                _nextAction = NextActionKind.EnsureJob;
            }
            else if (IsJobOwnedStatus(display))
            {
                NextActionLabel = "Open job order";
                WorkflowHint = display + " is owned by the job order. Delivery and tax invoice changes still refresh this row.";
                _nextAction = NextActionKind.OpenJob;
            }
            else if (string.Equals(stored, ProformaInvoiceStatus.Approved, StringComparison.OrdinalIgnoreCase))
            {
                NextActionLabel = "Confirm in Step 8";
                WorkflowHint = "The invoice is marked approved, but the confirmation document is not on the row yet. Step 8 is the approval path.";
                _nextAction = NextActionKind.Confirm;
            }
            else if (display == DocumentListStatus.PiSent
                || string.Equals(stored, "Sent", StringComparison.OrdinalIgnoreCase))
            {
                NextActionLabel = "Confirm in Step 8";
                WorkflowHint = "Confirmation needs the signed PI or LPO. That approves the invoice and creates or releases the job order.";
                _nextAction = NextActionKind.Confirm;
            }
            else if (display == DocumentListStatus.PiCreated
                || string.Equals(stored, "Draft", StringComparison.OrdinalIgnoreCase))
            {
                NextActionLabel = "Mark sent";
                WorkflowHint = "Sending records that the quotation left the office. Confirmation still requires the signed PI or LPO in Step 8.";
                _nextAction = NextActionKind.MarkSent;
            }
            else
            {
                NextActionLabel = "Open invoice";
                WorkflowHint = display + " is driven by the job, delivery, and tax invoice. Those documents update this row live.";
                _nextAction = NextActionKind.Open;
            }

            IsNextActionEnabled = true;
        }

        private async Task RunNextActionAsync()
        {
            var item = _selectedItem;
            if (item == null || _nextAction == NextActionKind.None)
                return;

            switch (_nextAction)
            {
                case NextActionKind.MarkSent:
                    await ChangeStatusAsync(item, "Sent");
                    break;
                case NextActionKind.ReopenDraft:
                    await ChangeStatusAsync(item, "Draft");
                    break;
                case NextActionKind.Confirm:
                    if (_invoices == null) return;
                    var toApprove = await _invoices.GetByIdAsync(item.Id);
                    if (toApprove != null)
                        await ApproveAsync(toApprove);
                    break;
                case NextActionKind.Revision:
                    await CreateRevisionAsync(item);
                    break;
                case NextActionKind.EnsureJob:
                    await EnsureJobForConfirmedAsync(item);
                    break;
                case NextActionKind.OpenJob:
                    await OpenRelatedJobAsync(item);
                    break;
                default:
                    RequestLoad(item);
                    break;
            }
        }

        private void ClearFilters()
        {
            _searchText = "";
            _selectedStatusFilter = "All Statuses";
            _fromDate = null;
            _toDate = null;
            _selectedSortPreset = "Newest First";

            OnPropertyChanged(nameof(SearchText));
            OnPropertyChanged(nameof(SelectedStatusFilter));
            OnPropertyChanged(nameof(FromDate));
            OnPropertyChanged(nameof(ToDate));
            OnPropertyChanged(nameof(SelectedSortPreset));

            ApplyFilters();
        }

        // ── Actions ───────────────────────────────────────────────────────────

        /// <summary>
        /// 2026-09-11 PI REVISION SUPPORT: a posted PI's body is frozen by
        /// DocumentLifecyclePolicy (the Save command refuses to rewrite
        /// Approved/Cancelled bodies — "corrected by a new revision"). This
        /// action IS that revision: it clones the committed document into a
        /// NEW Draft row sharing the original's RootProformaId
        /// ("PI-0001-R1"), leaving the original byte-identical for audit.
        /// The revision opens in the wizard immediately (Draft bodies are
        /// editable) and re-enters the normal save → send → approve flow.
        /// </summary>
        private async Task CreateRevisionAsync(SavedInvoiceListItem? item)
        {
            if (item == null) return;
            if (!PermissionService.Require(
                    AppPermissions.ProformaCreate, "create a proforma revision"))
                return;

            try
            {
                var source = await _invoices.GetByIdAsync(item.Id);
                if (source == null) return;

                // Policy gates: drafts are already editable; voided /
                // cancelled documents are terminal history.
                string status = ProformaInvoiceStatus.Normalize(source.Status);
                if (status == ProformaInvoiceStatus.Draft)
                    return;
                if (source.IsVoid || status == ProformaInvoiceStatus.Cancelled)
                    return;

                string root = string.IsNullOrWhiteSpace(source.RootProformaId)
                    ? source.ProformaId
                    : source.RootProformaId;
                var chain = await _invoices.GetRevisionChainAsync(root);
                int nextRev = chain.Count == 0
                    ? 1
                    : chain.Max(x => x.RevisionNumber) + 1;

                var revision = new SavedProformaInvoice
                {
                    ProformaId = $"{root}-R{nextRev}",
                    RootProformaId = root,
                    RevisionNumber = nextRev,
                    InquiryId = source.InquiryId,
                    CompanyName = source.CompanyName,
                    CustomerName = source.CustomerName,
                    ProjectName = source.ProjectName,
                    Status = ProformaInvoiceStatus.Draft,
                    IssueDate = source.IssueDate,
                    ValidUntil = source.ValidUntil,
                    GlassMaterialsSubtotal = source.GlassMaterialsSubtotal,
                    OtherChargesSubtotal = source.OtherChargesSubtotal,
                    DiscountAmount = source.DiscountAmount,
                    VatAmount = source.VatAmount,
                    GrandTotalVatInclusive = source.GrandTotalVatInclusive,
                    FullSnapshotJson = source.FullSnapshotJson,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    RowVersion = 0
                };

                // Uniqueness guard (pathological collisions only).
                int guard = 0;
                while (await _invoices.ExistsByProformaIdAsync(revision.ProformaId)
                       && guard++ < 5)
                    revision.ProformaId = $"{root}-R{nextRev}-{guard}";

                int newId = await _invoices.InsertAsync(revision);
                AuditService.Log(
                    AuditSections.Proforma, "PI revision created",
                    $"{revision.ProformaId} (rev {nextRev} of {root})");

                // Refresh the list, then open the revision in the wizard —
                // a Draft body is editable, which is the entire point.
                await LoadInvoicesAsync();
                OnInvoiceLoadRequested?.Invoke(newId);
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    $"[SavedInvoicesList] revision failed: {ExceptionMessages.Deep(ex)}");
            }
        }

        private void RequestLoad(SavedInvoiceListItem item)
        {
            if (item == null) return;
            OnInvoiceLoadRequested?.Invoke(item.Id);
        }

        private async Task DuplicateAsync(SavedInvoiceListItem item)
        {
            // A-1 (UI layer): tell the user BEFORE the confirm
            // dialog. The repository guard is the real boundary.
            if (!PermissionService.Require(
                    AppPermissions.ProformaCreate, "duplicate a proforma invoice"))
                return;

            if (item == null || _invoices == null) return;

            var confirm = _dialogs.Show(
                string.Format(LocalizationService.Instance.T("Msg.DuplicateInvoiceXANew"), item.ProformaId),
                LocalizationService.Instance.T("Msg.CapConfirmDuplicate"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                var original = await _invoices.GetByIdAsync(item.Id);
                if (original == null) return;

                string newId = await _invoices.SuggestNextIdAsync(
                    original.ProformaId);

                // Senior audit fix (Aug 2026): scrub Inquiry / PI id
                // fields from the snapshot before duplicating so the
                // copy does NOT silently inherit the source's
                // Inquiry back-link. The entity's InquiryId is
                // already omitted below; without this, the first
                // save of the duplicate re-populated Company.InquiryId
                // from the snapshot and pointed a second PI at the
                // same inquiry — bypassing the Convert-to-PI guard.
                string sanitizedSnapshot = ScrubIdsForDuplicate(
                    original.FullSnapshotJson, newId);

                var copy = new SavedProformaInvoice
                {
                    ProformaId = newId,
                    // InquiryId deliberately NOT copied — a duplicate
                    // is a fresh quotation, not another PI for the
                    // same inquiry.
                    CompanyName = original.CompanyName,
                    CustomerName = original.CustomerName,
                    ProjectName = original.ProjectName,
                    Status = "Draft",
                    IssueDate = DateTime.Now,
                    ValidUntil = DateTime.Now.AddDays(7),
                    GlassMaterialsSubtotal = original.GlassMaterialsSubtotal,
                    OtherChargesSubtotal = original.OtherChargesSubtotal,
                    DiscountAmount = original.DiscountAmount,
                    VatAmount = original.VatAmount,
                    GrandTotalVatInclusive = original.GrandTotalVatInclusive,
                    FullSnapshotJson = sanitizedSnapshot
                };

                await _invoices.InsertAsync(copy);
                await LoadInvoicesAsync();

                _dialogs.Show(
                    string.Format(LocalizationService.Instance.T("Msg.DuplicatedAsX"), newId),
                    LocalizationService.Instance.T("Msg.CapDuplicateSuccessful"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _dialogs.Show(
                    string.Format(LocalizationService.Instance.T("Msg.FailedToDuplicateX"), ExceptionMessages.Deep(ex)),
                    LocalizationService.Instance.T("Msg.CapDuplicateFailed"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task DeleteAsync(SavedInvoiceListItem item)
        {
            if (!ProGlassAutomation.Services.Audit.PermissionService.Require(
                ProGlassAutomation.Services.Audit.AppPermissions.ProformaDelete, "delete an invoice"))
                return;

            if (item == null || _invoices == null) return;

            // Phase 14: orphan protection — cross-database dependency check.
            // ERP hardening (Phase 3, G-4): a posted parent (not hard-deletable)
            // uses STRICT mode so a failed verification fails closed (blocks)
            // rather than risk orphaning its children.
            bool strict = !DocumentLifecyclePolicy.CanHardDelete(
                StatusTransitions.Kind.ProformaInvoice, item.Status, hasChildren: false);
            var guard = await _database.CheckDependencyAsync(
                DependencyGuard.Kind.ProformaInvoice,
                item.ProformaId ?? "", strict);
            bool hasChildren = guard.HasDependents;

            // ERP lifecycle hardening (Phase 1): three-way decision.
            //   HardDelete → childless pre-posting draft
            //   Blocked    → has LIVE children (handle those first)
            //   Void       → posted but childless (soft-close, keep the record)
            var decision = DocumentLifecyclePolicy.DecideDelete(
                StatusTransitions.Kind.ProformaInvoice, item.Status, hasChildren);

            if (decision == DocumentLifecyclePolicy.DeleteDecision.Blocked)
            {
                _dialogs.Show(guard.Message, LocalizationService.Instance.T("Msg.CapCannotDelete"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (decision == DocumentLifecyclePolicy.DeleteDecision.HardDelete)
            {
                var confirm = _dialogs.Show(
                    string.Format(LocalizationService.Instance.T("Msg.DeleteInvoiceXPermanentlyThis"), item.ProformaId),
                    LocalizationService.Instance.T("Msg.CapConfirmDelete"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes) return;

                try
                {
                    await _invoices.DeleteAsync(item.Id);
                    AuditService.Log(
                        AuditSections.Proforma, "Invoice deleted",
                        item.ProformaId, "PI", item.ProformaId);
                    await LoadInvoicesAsync();
                }
                catch (Exception ex)
                {
                    _dialogs.Show(
                        string.Format(LocalizationService.Instance.T("Msg.FailedToDeleteX2"), ExceptionMessages.Deep(ex)),
                        LocalizationService.Instance.T("Msg.CapDeleteFailed"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                return;
            }

            // Must void, not delete. Explain why, capture a mandatory reason.
            await VoidDocumentAsync(item);
        }

        /// <summary>
        /// ERP lifecycle hardening (Phase 1): soft-close (void) a PI that may
        /// not be hard-deleted. Explains why, captures a mandatory reason via
        /// the shared dialog, calls the repository VoidAsync, and audits it.
        /// </summary>
        private async Task VoidDocumentAsync(SavedInvoiceListItem item)
        {
            if (item == null || _invoices == null) return;

            var proceed = _dialogs.Show(
                string.Format(LocalizationService.Instance.T("Void.MsgConfirmVoidX"), item.ProformaId),
                LocalizationService.Instance.T("Void.Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (proceed != MessageBoxResult.Yes) return;

            var dlg = _dialogs.Create<ProGlassAutomation.Views.Shared.VoidReasonDialog>(item.ProformaId);
            if (dlg.ShowDialog() != true) return;
            string reason = dlg.ResultReason;

            try
            {
                await _invoices.VoidAsync(item.Id, reason);

                AuditService.Log(
                    AuditSections.Proforma, "Invoice voided",
                    item.ProformaId, "PI", item.ProformaId, reason);

                _dialogs.Show(
                    string.Format(LocalizationService.Instance.T("Void.MsgVoidedX"), item.ProformaId),
                    LocalizationService.Instance.T("Void.CapVoided"),
                    MessageBoxButton.OK, MessageBoxImage.Information);

                await LoadInvoicesAsync();
            }
            catch (Exception ex)
            {
                _dialogs.Show(
                    string.Format(LocalizationService.Instance.T("Void.MsgVoidFailedX"), ExceptionMessages.Deep(ex)),
                    LocalizationService.Instance.T("Void.CapVoidFailed"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ChangeStatusAsync(
            SavedInvoiceListItem item, string newStatus)
        {
            if (item == null
                || string.IsNullOrWhiteSpace(newStatus)
                || _invoices == null) return;

            try
            {
                var entity = await _invoices.GetByIdAsync(item.Id);
                if (entity == null) return;

                // Already approved — nothing to do (approval also converts once).
                if (string.Equals(newStatus, "Approved", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entity.Status, "Approved", StringComparison.OrdinalIgnoreCase))
                {
                    await LoadInvoicesAsync();
                    return;
                }

                // Approving always routes through Step 8 (upload signed PI/LPO);
                // the confirmation pattern runs after the upload.
                if (string.Equals(newStatus, "Approved", StringComparison.OrdinalIgnoreCase))
                {
                    await ApproveAsync(entity);
                    return;   // ApproveAsync forwards to Step 8
                }

                // Phase 3: enforce centralized status-transition rules at the
                // business boundary.
                var transition = ProGlassAutomation.Services.Workflow.StatusTransitions
                    .Validate(
                        ProGlassAutomation.Services.Workflow.StatusTransitions
                            .Kind.ProformaInvoice,
                        entity.Status, newStatus);
                if (!transition.IsValid)
                {
                    _dialogs.Show(transition.Reason ?? "Invalid status change.",
                        LocalizationService.Instance.T("Msg.CapStatusChangeNotAllowed"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Capture the pre-change status BEFORE mutating the entity.
                bool wasCancelled = IsCancelledStatus(entity.Status);

                entity.Status = newStatus;
                entity.UpdatedAt = DateTime.Now;
                SyncSnapshotWorkflow(entity, newStatus, null);
                await _invoices.UpdateAsync(entity);

                // Cancelling / rejecting a PI clears its Step 8 attachments
                // so a later re-approval starts clean — but ONLY when this
                // change actually moves the PI INTO the cancelled state. A
                // redundant re-cancel (e.g. after a job-order delete where
                // the user chose KEEP ATTACHMENTS) must not destroy files.
                if (IsCancelledStatus(newStatus) && !wasCancelled)
                {
                    await ClearCancelledAttachmentsAsync(entity.ProformaId ?? "");
                }

                await LoadInvoicesAsync();
            }
            catch (Exception ex)
            {
                _dialogs.Show(
                    string.Format(LocalizationService.Instance.T("Msg.FailedToChangeStatusX2"), ExceptionMessages.Deep(ex)),
                    LocalizationService.Instance.T("Msg.CapStatusChangeFailed"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static bool IsCancelledStatus(string? status) =>
            string.Equals(status, "Rejected", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Best-effort cleanup of a cancelled PI's Step 8 attachments
        /// (files + manifest). A clear failure must never roll back or
        /// fail the status change — it is logged, not thrown.
        /// </summary>
        private async Task ClearCancelledAttachmentsAsync(string proformaId)
        {
            try
            {
                int cleared = await new ProformaAttachmentService()
                    .ClearAllAsync(proformaId);

                if (cleared > 0)
                {
                    AuditService.Log(
                        AuditSections.Proforma, "Attachments cleared",
                        $"{proformaId} — {cleared} Step 8 attachment(s) " +
                        "removed after cancellation",
                        "PI", proformaId);
                }
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    $"Could not clear attachments for cancelled PI " +
                    $"{proformaId}: {ExceptionMessages.Deep(ex)}",
                    new LogContext
                    {
                        Module = "Proforma",
                        Class = nameof(SavedInvoicesListViewModel),
                        Operation = "ChangeStatus"
                    });
            }
        }

        /// <summary>
        /// Approve gate. Selecting "Approved" ALWAYS routes through Step 8
        /// (Attachments) so the user can check/attach the signed Proforma
        /// Invoice or LPO (jpg / pdf). This is deliberately NOT a
        /// click-to-approve: the Signed-PI / LPO confirmation pattern runs only
        /// AFTER an upload in Step 8 (via CompleteApprovalAsync).
        /// </summary>
    }
}
