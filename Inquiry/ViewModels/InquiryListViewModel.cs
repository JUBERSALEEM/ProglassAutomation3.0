// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/ViewModels/InquiryListViewModel.cs
// MODULE: Inquiry — register (list page) view-model.
//
// Ported from the legacy InquiryListViewModel (three partial files
// consolidated). Read path: status chips → database-side paged
// projection + statistics. Write path: row commands → workflow service
// (guarded transitions, optimistic RowVersion).
//
// Design notes carried over from the senior analysis:
//   - date filter defaults to "All time" (S1: saved inquiries vanished);
//   - cancellations return quietly (S7: log spam);
//   - selection survives a reload (A7);
//   - no placeholder commands — every visible control does real work.
//
// V3 scope:
//   - the side detail drawer is replaced by the in-module editor page
//     (EditRequested → the workspace view navigates list → detail);
//   - filter dropdowns are sourced from the register itself (distinct
//     salesman / prepared-by) because the master-data module is not
//     ported yet;
//   - the Convert-to-PI action ships with the Proforma module; the
//     register still carries the PI / JO columns and the lifecycle
//     snapshot that the conversion will consume.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Services.Inquiry;
using ProGlassAutomation.ThemeStudio;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ProGlassAutomation.ViewModels.Inquiry
{
    public sealed partial class InquiryListViewModel : ViewModelBase, IDisposable
    {
        private readonly IInquiryRepository _repository;
        private readonly IInquiryWorkflowService _workflow;
        private readonly InquiryAttachmentService _attachments;
        private readonly InquiryImportExportService _io;
        private CancellationTokenSource? _loadCts;
        private CancellationTokenSource? _debounceCts;
        private CancellationTokenSource? _importCts;
        private int _reloadGeneration;

        public ObservableCollection<InquiryListRow> Inquiries { get; } = new();
        public ObservableCollection<InquiryStatusTab> StatusTabs { get; } = new();
        public ObservableCollection<InquiryModuleTab> ModuleTabs { get; } = new();
        private bool _isInquiryActive = true;
        public bool IsInquiryActive { get => _isInquiryActive; private set => SetField(ref _isInquiryActive, value); }
        private string _selectedModuleKey = "Inquiries";
        public string SelectedModuleKey { get => _selectedModuleKey; private set => SetField(ref _selectedModuleKey, value); }
        private string _selectedModuleLabel = "Inquiries";
        public string SelectedModuleLabel { get => _selectedModuleLabel; private set => SetField(ref _selectedModuleLabel, value); }

        public ObservableCollection<string> DateRanges { get; } =
            new() { "All time", "Today", "Yesterday", "Last 7 Days", "Last 30 Days" };
        public ObservableCollection<string> Salesmen { get; } = new() { "All salesmen" };
        public ObservableCollection<string> PreparedByUsers { get; } = new() { "All prepared by" };
        public ObservableCollection<string> Priorities { get; } = new() { "All priorities", "Urgent", "High", "Normal", "Low" };
        public ObservableCollection<string> SortOptions { get; } =
            new() { "Updated newest", "Updated oldest", "Received newest", "Received oldest", "Inquiry number", "Value highest" };

        // ── Selection ─────────────────────────────────────────────────

        private InquiryListRow? _selectedInquiry;
        public InquiryListRow? SelectedInquiry
        {
            get => _selectedInquiry;
            set
            {
                if (ReferenceEquals(_selectedInquiry, value)) return;
                _selectedInquiry = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                AsyncUtility.InvalidateCommandsThrottled();
            }
        }
        public bool HasSelection => SelectedInquiry != null;

        // ── KPI strip ──────────────────────────────────────────────────

        private decimal _pipelineValue;
        public decimal PipelineValue
        {
            get => _pipelineValue;
            private set { if (SetField(ref _pipelineValue, value)) OnPropertyChanged(nameof(PipelineValueText)); }
        }
        public string PipelineValueText => PipelineValue == 0 ? "—" : $"AED {PipelineValue:N0}";
        private int _openCount;
        public int OpenCount { get => _openCount; private set => SetField(ref _openCount, value); }
        private int _awaitingCount;
        public int AwaitingCount { get => _awaitingCount; private set => SetField(ref _awaitingCount, value); }
        private int _confirmedCount;
        public int ConfirmedCount { get => _confirmedCount; private set => SetField(ref _confirmedCount, value); }

        private string _pageStatus = "Loading…";
        public string PageStatus { get => _pageStatus; private set => SetField(ref _pageStatus, value); }
        public bool HasInquiries => Inquiries.Count > 0;

        // ── Filters ────────────────────────────────────────────────────

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasActiveFilters));
                DebounceReload();
            }
        }
        // "All time" default — a register must not hide rows by accident.
        private string _selectedDateRange = "All time";
        public string SelectedDateRange { get => _selectedDateRange; set { if (SetField(ref _selectedDateRange, value)) ResetAndReload(); } }
        private string _selectedSalesman = "All salesmen";
        public string SelectedSalesman { get => _selectedSalesman; set { if (SetField(ref _selectedSalesman, value)) ResetAndReload(); } }
        private string _selectedPreparedBy = "All prepared by";
        public string SelectedPreparedBy { get => _selectedPreparedBy; set { if (SetField(ref _selectedPreparedBy, value)) ResetAndReload(); } }
        private string _selectedPriority = "All priorities";
        public string SelectedPriority { get => _selectedPriority; set { if (SetField(ref _selectedPriority, value)) ResetAndReload(); } }
        private string _selectedSort = "Updated newest";
        public string SelectedSort { get => _selectedSort; set { if (SetField(ref _selectedSort, value)) ResetAndReload(); } }
        private bool _hasAttachmentsOnly;
        public bool HasAttachmentsOnly { get => _hasAttachmentsOnly; set { if (SetField(ref _hasAttachmentsOnly, value)) ResetAndReload(); } }

        public bool HasActiveFilters =>
            !string.IsNullOrWhiteSpace(SearchText) || SelectedDateRange != "All time"
            || SelectedSalesman != "All salesmen" || SelectedPreparedBy != "All prepared by"
            || SelectedPriority != "All priorities" || HasAttachmentsOnly
            || SelectedStatusKey.Length > 0;

        /// <summary>
        /// The status ComboBox's selection.
        ///
        /// Owner direction: the seven status chips ("all received prepared sent
        /// confirmed etc") belong in one dropdown rather than a full-width strip
        /// of seven buttons above the register. Selecting here delegates to
        /// SelectedStatusKey, so every existing filter path - HasActiveFilters,
        /// ResetFilters, the reload - keeps working unchanged.
        /// </summary>
        public InquiryStatusTab? SelectedStatusTab
        {
            get => StatusTabs.FirstOrDefault(tab => tab.IsSelected);
            set
            {
                if (value == null || value.IsSelected) return;
                SelectedStatusKey = value.Key;
                OnPropertyChanged();
                ResetAndReload();
            }
        }

        /// <summary>
        /// The module ComboBox's selection (Inquiries, Proforma Invoices, …).
        /// Planned modules are listed and disabled; selecting one is a no-op, the
        /// same rule SelectModuleTabCommand enforces.
        /// </summary>
        public InquiryModuleTab? SelectedModuleTab
        {
            get => ModuleTabs.FirstOrDefault(tab => tab.IsSelected);
            set
            {
                if (value == null) return;

                if (!value.IsEnabled)
                {
                    // A planned module was picked. Raise the change back on the
                    // dispatcher: notifying inside the setter, while the ComboBox
                    // is still committing its own selection, is ignored, and the
                    // control would go on showing a module that is not routed.
                    var self = this;
                    Dispatcher.CurrentDispatcher.BeginInvoke(new Action(
                        () => self.OnPropertyChanged(nameof(SelectedModuleTab))));
                    return;
                }

                SelectModuleTabCommand.Execute(value.Key);
                OnPropertyChanged();
            }
        }

        private bool _areFiltersExpanded;

        /// <summary>
        /// Advanced filters (date, salesman, preparer, priority, sort, attachments)
        /// live in a row that is collapsed until asked for. A register should open
        /// showing rows, not controls; the Filters button carries the count of
        /// active filters so a collapsed row can never hide a filter silently.
        /// </summary>
        public bool AreFiltersExpanded
        {
            get => _areFiltersExpanded;
            set
            {
                if (!SetField(ref _areFiltersExpanded, value)) return;
                OnPropertyChanged(nameof(FiltersToggleLabel));
            }
        }

        /// <summary>
        /// How many advanced filters are narrowing the register right now.
        /// Search is deliberately not counted: it has its own box in the bar, so
        /// it can never hide behind the collapsed row. Everything that does hide
        /// there is counted, which is what lets the label say so.
        /// </summary>
        public int ActiveFilterCount
        {
            get
            {
                int count = 0;
                if (SelectedDateRange != "All time") count++;
                if (SelectedSalesman != "All salesmen") count++;
                if (SelectedPreparedBy != "All prepared by") count++;
                if (SelectedPriority != "All priorities") count++;
                if (SelectedSort != "Updated newest") count++;
                if (HasAttachmentsOnly) count++;
                if (SelectedStatusKey.Length > 0) count++;
                return count;
            }
        }

        public string FiltersToggleLabel =>
            AreFiltersExpanded ? "Hide filters"
            : ActiveFilterCount == 0 ? "Filters"
            : $"Filters ({ActiveFilterCount})";

        public ICommand ToggleFiltersCommand { get; }

        /// <summary>"" = All statuses; otherwise a controlled status value.</summary>
        public string SelectedStatusKey
        {
            get => StatusTabs.FirstOrDefault(x => x.IsSelected)?.Key ?? "";
            set
            {
                foreach (var tab in StatusTabs)
                    tab.IsSelected = tab.Key == value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedStatusTab));
                OnPropertyChanged(nameof(HasActiveFilters));
            }
        }

        // ── UI callbacks (kept UI-free for testability) ────────────────

        /// <summary>
        /// Opens the editor for the given record (null = new inquiry).
        /// Returns true when the editor closed without saving so the
        /// register can skip the reload.
        /// </summary>
        public Func<InquiryRecord?, Task<bool>>? EditRequested { get; set; }
        public Func<string?>? ExportPathRequested { get; set; }
        public Func<string?>? ImportPathRequested { get; set; }
        public Func<int, bool>? ConfirmImportRequested { get; set; }
        public Func<InquiryListRow, bool>? ConfirmDeleteInquiryRequested { get; set; }
        public Action<InquiryListRow>? OpenAttachmentsFolderRequested { get; set; }

        // ── Commands ──────────────────────────────────────────────────

        public ICommand NewInquiryCommand { get; }
        public ICommand EditInquiryCommand { get; }
        public ICommand SelectStatusTabCommand { get; }
        public ICommand SelectModuleTabCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ResetFiltersCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand PreviousPageCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand OpenInquiryCommand { get; }
        public ICommand OpenInquiryRowCommand { get; }
        public ICommand ChangeStatusRowCommand { get; }
        public ICommand PrepareRowCommand { get; }
        public ICommand SendRowCommand { get; }
        public ICommand ConfirmRowCommand { get; }
        public ICommand HoldRowCommand { get; }
        public ICommand CancelRowCommand { get; }
        public ICommand CopyInquiryNumberCommand { get; }
        public ICommand CopySummaryCommand { get; }
        public ICommand CopyProformaNumberCommand { get; }
        public ICommand CopyJobOrderNumberCommand { get; }
        public ICommand OpenAttachmentsFolderCommand { get; }
        public ICommand EditInquiryRowCommand { get; }
        public ICommand DuplicateInquiryRowCommand { get; }
        public ICommand DeleteInquiryRowCommand { get; }
        public ICommand SetUrgentPriorityCommand { get; }
        public ICommand SetHighPriorityCommand { get; }
        public ICommand SetNormalPriorityCommand { get; }
        public ICommand SetLowPriorityCommand { get; }

        public InquiryListViewModel() : this(new InquiryRepository()) { }

        public InquiryListViewModel(IInquiryRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _workflow = new InquiryWorkflowService(repository);
            _attachments = new InquiryAttachmentService(repository);
            _io = new InquiryImportExportService();

            StatusTabs.Add(new InquiryStatusTab { Key = "", Label = "All", IsSelected = true });
            StatusTabs.Add(new InquiryStatusTab { Key = InquiryStatuses.Received, Label = "Received" });
            StatusTabs.Add(new InquiryStatusTab { Key = InquiryStatuses.Prepared, Label = "Prepared" });
            StatusTabs.Add(new InquiryStatusTab { Key = InquiryStatuses.Sent, Label = "Sent" });
            StatusTabs.Add(new InquiryStatusTab { Key = InquiryStatuses.Confirmed, Label = "Confirmed" });
            StatusTabs.Add(new InquiryStatusTab { Key = InquiryStatuses.Hold, Label = "Hold" });
            StatusTabs.Add(new InquiryStatusTab { Key = InquiryStatuses.Cancelled, Label = "Cancelled" });

            ModuleTabs.Add(new InquiryModuleTab { Key = "Inquiries", Label = "Inquiries", IsSelected = true });
            ModuleTabs.Add(new InquiryModuleTab { Key = "Proforma Invoices", Label = "Proforma Invoices", IsPlanned = true });
            ModuleTabs.Add(new InquiryModuleTab { Key = "Job Orders", Label = "Job Orders", IsPlanned = true });
            ModuleTabs.Add(new InquiryModuleTab { Key = "Delivery Notes", Label = "Delivery Notes", IsPlanned = true });
            ModuleTabs.Add(new InquiryModuleTab { Key = "Tax Invoices", Label = "Tax Invoices", IsPlanned = true });
            ModuleTabs.Add(new InquiryModuleTab { Key = "Statements", Label = "Statements", IsPlanned = true });

            NewInquiryCommand = new DelegateCommand(async () => await NewInquiryAsync());
            EditInquiryCommand = new DelegateCommand(
                () => { if (SelectedInquiry != null) _ = EditRowAsync(SelectedInquiry); },
                () => HasSelection);
            SelectStatusTabCommand = new DelegateCommand<string>(key =>
            {
                SelectedStatusKey = key ?? "";
                ResetAndReload();
            });
            SelectModuleTabCommand = new DelegateCommand<string>(key =>
            {
                // This page currently owns the live Inquiries register.
                // Other module pages are intentionally not routed here;
                // their tabs are disabled until their views are delivered.
                if (!string.Equals(key, "Inquiries", StringComparison.Ordinal)) return;
                foreach (var tab in ModuleTabs) tab.IsSelected = tab.Key == key;
                OnPropertyChanged(nameof(SelectedModuleTab));
            });
            ExportCommand = new DelegateCommand(() => _ = ExportAsync());
            ImportCommand = new DelegateCommand(() => _ = ImportAsync());
            ResetFiltersCommand = new DelegateCommand(ResetFilters);
            ToggleFiltersCommand = new DelegateCommand(() => AreFiltersExpanded = !AreFiltersExpanded);
            RefreshCommand = new DelegateCommand(Reload);
            PreviousPageCommand = new DelegateCommand(
                () => { if (CanGoPrevious) { CurrentPage--; Reload(); } }, () => CanGoPrevious);
            NextPageCommand = new DelegateCommand(
                () => { if (CanGoNext) { CurrentPage++; Reload(); } }, () => CanGoNext);
            OpenInquiryCommand = new DelegateCommand(
                () => { if (SelectedInquiry != null) _ = EditRowAsync(SelectedInquiry); },
                () => HasSelection);
            OpenInquiryRowCommand = new DelegateCommand<InquiryListRow>(row => _ = EditRowAsync(row),
                r => r != null);
            // Row-targeted status commands: each promotes the row into
            // the selection (so KPI strip and double-click handlers stay
            // in sync) and applies the legal transition. WPF's
            // CommandParameter carries a single value, so one command per
            // status is simpler than a MultiBinding + converter.
            ChangeStatusRowCommand = new DelegateCommand<InquiryListRow>(row =>
            {
                if (row != null) SelectedInquiry = row;
            }, r => r != null);
            PrepareRowCommand = new DelegateCommand<InquiryListRow>(row => ExecuteStatusRowChange(row, InquiryStatuses.Prepared),
                r => r is { CanPrepareWorkflow: true });
            SendRowCommand = new DelegateCommand<InquiryListRow>(row => ExecuteStatusRowChange(row, InquiryStatuses.Sent),
                r => r is { CanSendWorkflow: true });
            ConfirmRowCommand = new DelegateCommand<InquiryListRow>(row => ExecuteStatusRowChange(row, InquiryStatuses.Confirmed),
                r => r is { CanConfirmWorkflow: true });
            HoldRowCommand = new DelegateCommand<InquiryListRow>(row => ExecuteStatusRowChange(row, InquiryStatuses.Hold),
                r => r is { CanHoldWorkflow: true });
            CancelRowCommand = new DelegateCommand<InquiryListRow>(row => ExecuteStatusRowChange(row, InquiryStatuses.Cancelled),
                r => r is { CanCancelWorkflow: true });
            CopyInquiryNumberCommand = new DelegateCommand<InquiryListRow>(row =>
            {
                if (row != null && !string.IsNullOrWhiteSpace(row.InquiryNumber))
                {
                    try
                    {
                        Clipboard.SetText(row.InquiryNumber);
                        SetStatus($"Copied '{row.InquiryNumber}' to clipboard.");
                    }
                    catch (Exception ex) { SetStatus("Copy failed: " + ExceptionMessages.Deep(ex)); }
                }
            }, r => r != null);
            CopySummaryCommand = new DelegateCommand<InquiryListRow>(row =>
            {
                if (row == null) return;
                try
                {
                    Clipboard.SetText(ComposeSummary(row));
                    SetStatus("Copied one-line summary to clipboard.");
                }
                catch (Exception ex) { SetStatus("Copy failed: " + ExceptionMessages.Deep(ex)); }
            }, r => r != null);
            CopyProformaNumberCommand = new DelegateCommand<InquiryListRow>(row =>
                CopyToClipboard(row, r => r.Lifecycle.ProformaNumber),
                r => r is { Lifecycle.HasProforma: true });
            CopyJobOrderNumberCommand = new DelegateCommand<InquiryListRow>(row =>
                CopyToClipboard(row, r => r.Lifecycle.JobOrderNumber),
                r => r is { Lifecycle.HasJobOrder: true });
            OpenAttachmentsFolderCommand = new DelegateCommand<InquiryListRow>(row =>
            {
                if (row == null) return;
                if (OpenAttachmentsFolderRequested != null) OpenAttachmentsFolderRequested(row);
                else SetStatus("Attachments folder handler is not wired.");
            }, r => r is { AttachmentCount: > 0 });
            EditInquiryRowCommand = new DelegateCommand<InquiryListRow>(row =>
            {
                if (row != null)
                {
                    SelectedInquiry = row;
                    _ = EditRowAsync(row);
                }
            }, r => r != null);
            DuplicateInquiryRowCommand = new DelegateCommand<InquiryListRow>(row =>
                _ = DuplicateInquiryAsync(row), r => r != null);
            DeleteInquiryRowCommand = new DelegateCommand<InquiryListRow>(row =>
                _ = DeleteInquiryAsync(row), r => r != null);
            SetUrgentPriorityCommand = new DelegateCommand<InquiryListRow>(row => _ = ApplyPriorityAsync(row, "Urgent"),
                r => r != null);
            SetHighPriorityCommand = new DelegateCommand<InquiryListRow>(row => _ = ApplyPriorityAsync(row, "High"),
                r => r != null);
            SetNormalPriorityCommand = new DelegateCommand<InquiryListRow>(row => _ = ApplyPriorityAsync(row, "Normal"),
                r => r != null);
            SetLowPriorityCommand = new DelegateCommand<InquiryListRow>(row => _ = ApplyPriorityAsync(row, "Low"),
                r => r != null);

            // Website inbox card at the top of the register. The card is
            // always visible (entry point to the website inbox), so the
            // command is always available.
            OpenInboxCommand = new DelegateCommand(() => OpenInboxRequested?.Invoke());

            AsyncUtility.SafeFireAndForget(LoadFilterSourcesAsync);
            Reload();
        }

        // ── Website inbox card (top of the register) ───────────────────

        private bool _inboxCardVisible;
        public bool InboxCardVisible { get => _inboxCardVisible; private set => SetField(ref _inboxCardVisible, value); }

        private int _inboxReadyCount;
        public int InboxReadyCount { get => _inboxReadyCount; private set => SetField(ref _inboxReadyCount, value); }

        private int _inboxIngestedCount;
        public int InboxIngestedCount { get => _inboxIngestedCount; private set => SetField(ref _inboxIngestedCount, value); }

        private int _inboxFailedCount;
        public int InboxFailedCount { get => _inboxFailedCount; private set => SetField(ref _inboxFailedCount, value); }

        private string _inboxSummaryText = "";
        public string InboxSummaryText { get => _inboxSummaryText; private set => SetField(ref _inboxSummaryText, value); }

        private string _inboxLastSyncText = "";
        public string InboxLastSyncText { get => _inboxLastSyncText; private set => SetField(ref _inboxLastSyncText, value); }

        /// <summary>Wired by the workspace view: navigate to the inbox page.</summary>
        public Action? OpenInboxRequested { get; set; }
        public ICommand OpenInboxCommand { get; private set; }

        /// <summary>Refreshes the top-of-register inbox card from the inbox service.</summary>
        public void UpdateInboxCard(ProGlassAutomation.Services.Inquiry.InquiryInbox.InboxSummary summary)
        {
            InboxCardVisible = true;
            InboxReadyCount = summary.DownloadedCount;
            InboxIngestedCount = summary.IngestedCount;
            InboxFailedCount = summary.FailedCount;
            InboxSummaryText = !summary.Configured
                ? "Connect the customer website to receive online inquiries"
                : summary.Offline
                    ? "Website unreachable — inbox keeps working locally"
                    : summary.DownloadedCount == 0 && summary.FailedCount == 0
                        ? "No new website inquiries"
                        : summary.DownloadedCount + " awaiting local entry"
                          + (summary.FailedCount > 0 ? $" · {summary.FailedCount} failed" : "");
            InboxLastSyncText = BuildLastSyncText(summary);
        }

        private static string BuildLastSyncText(ProGlassAutomation.Services.Inquiry.InquiryInbox.InboxSummary summary)
        {
            var when = summary.LastSyncAtUtc;
            if (!string.IsNullOrEmpty(when) && when.Length > 15)
                return "Last check " + when.Substring(11, 5) + " UTC · " + summary.LastSyncStatus;
            // No timestamp: either never checked, or a status-only summary
            // (e.g. a failed refresh reporting its error — never blank).
            return string.IsNullOrEmpty(summary.LastSyncStatus) ? "Not checked yet" : summary.LastSyncStatus;
        }

        // ── Actions ───────────────────────────────────────────────────

        private async Task NewInquiryAsync()
        {
            if (EditRequested == null) { SetStatus("Inquiry editor is unavailable."); return; }
            if (await EditRequested(null)) ResetAndReload();
        }

        private async Task EditRowAsync(InquiryListRow row)
        {
            if (EditRequested == null) { SetStatus("Inquiry editor is unavailable."); return; }
            var detail = await _repository.GetDetailAsync(row.Id);
            if (await EditRequested(detail)) ResetAndReload();
        }

        private async Task ApplyPriorityAsync(InquiryListRow? row, string priority)
        {
            if (row == null) return;
            try
            {
                bool ok = await _workflow.SetPriorityAsync(row.Id, priority);
                if (ok)
                {
                    SetStatus($"{row.InquiryNumber} priority → {priority}.");
                    Reload();
                }
            }
            catch (Exception ex) { SetStatus("Change priority failed: " + ExceptionMessages.Deep(ex)); }
        }

        private void ExecuteStatusRowChange(InquiryListRow? row, string nextStatus)
        {
            if (row == null) return;
            SelectedInquiry = row;
            _ = ChangeSelectedStatusAsync(nextStatus);
        }

        private async Task ChangeSelectedStatusAsync(string next)
        {
            var row = SelectedInquiry;
            if (row == null || string.IsNullOrWhiteSpace(next)) return;
            try
            {
                if (!InquiryWorkflowService.CanTransition(row.Status, next))
                {
                    SetStatus($"An inquiry cannot move from {row.Status} to {next}.");
                    return;
                }
                bool changed = await _workflow.ChangeStatusAsync(row.Id, row.Status, next, row.RowVersion);
                if (changed)
                {
                    SetStatus($"Inquiry {row.InquiryNumber} is now {InquiryStatuses.Normalize(next)}.");
                    Reload();
                }
            }
            catch (Exception ex) { SetStatus(ExceptionMessages.Deep(ex)); }
        }

        private async Task DuplicateInquiryAsync(InquiryListRow row)
        {
            try
            {
                var clone = await _workflow.DuplicateAsync(row.Id);
                SetStatus($"Duplicated {row.InquiryNumber} → {clone.InquiryNumber}.");
                Reload();
            }
            catch (Exception ex) { SetStatus("Duplicate failed: " + ExceptionMessages.Deep(ex)); }
        }

        private async Task DeleteInquiryAsync(InquiryListRow row)
        {
            bool proceed = ConfirmDeleteInquiryRequested?.Invoke(row) ?? false;
            if (!proceed) return;
            try
            {
                var result = await _workflow.DeleteAsync(row.Id);
                if (result.IsSuccess)
                {
                    SetStatus($"Deleted inquiry {row.InquiryNumber}.");
                    if (ReferenceEquals(SelectedInquiry, row)) SelectedInquiry = null;
                    Reload();
                }
                else if (result.Outcome == DeleteInquiryOutcome.NotFound)
                {
                    SetStatus("This inquiry no longer exists — the register will refresh.");
                    Reload();
                }
                else
                {
                    string list = result.BlockingDocuments.Count == 0
                        ? "downstream documents"
                        : string.Join(", ", result.BlockingDocuments);
                    SetStatus($"Cannot delete {row.InquiryNumber}: a {list} already exists. " +
                              "Delete is only allowed when no Proforma Invoice has been created. " +
                              "Use 'Change status → Cancel' to close the inquiry instead.");
                }
            }
            catch (Exception ex) { SetStatus("Delete failed: " + ExceptionMessages.Deep(ex)); }
        }

        // ── Export / import ────────────────────────────────────────────

        private async Task ExportAsync()
        {
            string? path = ExportPathRequested?.Invoke();
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                int count = await ExportInquiriesToPathAsync(path);
                SetStatus($"Exported {count} inquiries to {System.IO.Path.GetFileName(path)}.");
            }
            catch (Exception ex) { SetStatus("Export failed: " + ExceptionMessages.Deep(ex)); }
        }

        private async Task<int> ExportInquiriesToPathAsync(string path)
        {
            // Export every row matching the current filter (paged fetch,
            // bounded), not just the visible page.
            var range = DateRange(SelectedDateRange, DateTime.Now);
            InquiryQuery Build(int page) => new()
            {
                Search = SearchText,
                Status = SelectedStatusKey.Length == 0 ? "All statuses" : SelectedStatusKey,
                SalesmanName = SelectedSalesman, PreparedByName = SelectedPreparedBy,
                From = range.From, ToExclusive = range.ToExclusive,
                HasAttachmentsOnly = HasAttachmentsOnly, Priority = SelectedPriority,
                Sort = SelectedSort, Page = page, PageSize = 200
            };
            var rows = new System.Collections.Generic.List<InquiryListProjection>();
            var result = await _repository.QueryAsync(Build(1));
            rows.AddRange(result.Items);
            while (result.HasNext && rows.Count < 5000)
            {
                result = await _repository.QueryAsync(Build(result.Page + 1));
                rows.AddRange(result.Items);
            }
            await _io.ExportCsvAsync(path, rows);
            return rows.Count;
        }

        private async Task ImportAsync()
        {
            string? path = ImportPathRequested?.Invoke();
            if (string.IsNullOrWhiteSpace(path)) return;
            // Cancel any in-flight import so a second click doesn't
            // double-parse the same file.
            _importCts?.Cancel();
            _importCts?.Dispose();
            _importCts = new CancellationTokenSource();
            var ct = _importCts.Token;
            try
            {
                var parse = await _io.ParseCsvAsync(path, ct);
                if (parse.Rows.Count == 0)
                {
                    SetStatus(parse.SkippedInvalidRows > 0
                        ? $"Import contains no valid rows ({parse.SkippedInvalidRows} rows lacked customer or project)."
                        : "Import contains no rows.");
                    return;
                }
                if (ConfirmImportRequested?.Invoke(parse.Rows.Count) != true) return;
                int created = 0, failed = 0;
                foreach (var row in parse.Rows)
                {
                    if (ct.IsCancellationRequested) return;
                    try { await _workflow.CreateAsync(row); created++; }
                    catch (Exception) { failed++; }
                }
                string suffix = parse.SkippedInvalidRows > 0 ? $", {parse.SkippedInvalidRows} invalid skipped" : "";
                string dupes = parse.DuplicateNumbersRegenerated > 0
                    ? $", {parse.DuplicateNumbersRegenerated} duplicate numbers regenerated" : "";
                SetStatus($"Imported {created} inquiries{suffix}{dupes}{(failed > 0 ? $", {failed} failed" : "")}.");
                ResetAndReload();
            }
            catch (OperationCanceledException) { /* user closed the file picker mid-parse */ }
            catch (Exception ex) { SetStatus("Import failed: " + ExceptionMessages.Deep(ex)); }
        }

        private static (DateTime? From, DateTime? ToExclusive) DateRange(string value, DateTime now)
        {
            var today = now.Date;
            return value switch
            {
                "Today" => (today, today.AddDays(1)),
                "Yesterday" => (today.AddDays(-1), today),
                "Last 7 Days" => (today.AddDays(-6), today.AddDays(1)),
                "Last 30 Days" => (today.AddDays(-29), today.AddDays(1)),
                _ => (null, null) // "All time" — the honest default
            };
        }

        // ── Loading ────────────────────────────────────────────────────

        private async Task LoadFilterSourcesAsync()
        {
            try
            {
                foreach (var x in await _repository.GetDistinctSalesmenAsync())
                    if (!string.IsNullOrWhiteSpace(x) && !Salesmen.Contains(x)) Salesmen.Add(x);
                foreach (var user in await _repository.GetDistinctPreparedByAsync())
                    if (!string.IsNullOrWhiteSpace(user) && !PreparedByUsers.Contains(user)) PreparedByUsers.Add(user);
            }
            catch (Exception ex) { SetStatus("Filter sources unavailable: " + ExceptionMessages.Deep(ex)); }
        }

        private void DebounceReload()
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;
            AsyncUtility.SafeFireAndForget(async () =>
            {
                try
                {
                    await Task.Delay(300, token);
                    token.ThrowIfCancellationRequested();
                    ResetAndReload();
                }
                catch (OperationCanceledException) { /* superseded by a newer keystroke */ }
            });
        }

        private void ResetAndReload()
        {
            // Every advanced filter setter funnels through here, so this is the
            // one place that has to keep the collapsed row's badge honest.
            OnPropertyChanged(nameof(ActiveFilterCount));
            OnPropertyChanged(nameof(FiltersToggleLabel));
            CurrentPage = 1;
            Reload();
        }

        /// <summary>
        /// Reloads the register keeping the current filters/page.
        /// Public so the workspace can refresh it after an inbox
        /// ingest created a new inquiry.
        /// </summary>
        public void Reload()
        {
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            int generation = ++_reloadGeneration;
            AsyncUtility.SafeFireAndForget(() => LoadAsync(_loadCts.Token, generation));
        }

        private async Task LoadAsync(CancellationToken ct, int generation)
        {
            IsLoading = true; ErrorText = ""; SetStatus("Loading inquiries…");
            try
            {
                var range = DateRange(SelectedDateRange, DateTime.Now);
                var query = new InquiryQuery
                {
                    Search = SearchText,
                    Status = SelectedStatusKey.Length == 0 ? "All statuses" : SelectedStatusKey,
                    SalesmanName = SelectedSalesman, PreparedByName = SelectedPreparedBy,
                    From = range.From, ToExclusive = range.ToExclusive,
                    HasAttachmentsOnly = HasAttachmentsOnly, Priority = SelectedPriority,
                    Sort = SelectedSort, Page = CurrentPage, PageSize = 50
                };
                var page = await _repository.QueryAsync(query, ct);
                var stats = await _repository.GetStatisticsAsync(query, ct);
                ct.ThrowIfCancellationRequested();
                // Keep the selection across reloads when the row is still
                // on (or returned to) the current page.
                int keepId = SelectedInquiry?.Id ?? 0;
                InquiryListRow? restored = null;
                Inquiries.Clear();
                foreach (var x in page.Items)
                {
                    var row = InquiryListRow.FromProjection(x);
                    Inquiries.Add(row);
                    if (row.Id == keepId) restored = row;
                }
                OnPropertyChanged(nameof(HasInquiries));
                ApplyStatistics(stats);
                TotalPages = page.TotalCount == 0 ? 0 : (int)Math.Ceiling(page.TotalCount / (double)page.PageSize);
                PageStatus = page.TotalCount == 0
                    ? "No inquiries match the current filters"
                    : $"Showing {(page.Page - 1) * page.PageSize + 1}–{Math.Min(page.TotalCount, page.Page * page.PageSize)} of {page.TotalCount}";
                SetStatus(PageStatus);
                if (restored != null && generation == _reloadGeneration)
                    SelectedInquiry = restored;
            }
            catch (OperationCanceledException) { /* superseded */ }
            catch (Exception ex)
            {
                ErrorText = "Could not load inquiries: " + ExceptionMessages.Deep(ex);
                SetStatus(ErrorText);
            }
            finally { if (generation == _reloadGeneration) IsLoading = false; }
        }

        private void ApplyStatistics(InquiryStatistics stats)
        {
            PipelineValue = stats.OpenPipelineValue;
            OpenCount = stats.Received + stats.Prepared;
            AwaitingCount = stats.Sent;
            ConfirmedCount = stats.Confirmed;
            foreach (var tab in StatusTabs)
            {
                tab.Count = tab.Key switch
                {
                    "" => stats.All,
                    InquiryStatuses.Received => stats.Received,
                    InquiryStatuses.Prepared => stats.Prepared,
                    InquiryStatuses.Sent => stats.Sent,
                    InquiryStatuses.Confirmed => stats.Confirmed,
                    InquiryStatuses.Hold => stats.Hold,
                    InquiryStatuses.Cancelled => stats.Cancelled,
                    _ => 0
                };
            }
        }

        // ── Right-click helpers ────────────────────────────────────────

        private static string ComposeSummary(InquiryListRow row) =>
            $"{row.InquiryNumber} · {row.CustomerName} · {row.Project} · AED {row.InquiryValue:N0} · {row.Status} · {row.ReceivedDateText}";

        private static void CopyToClipboard(InquiryListRow? row, Func<InquiryListRow, string?> numberGetter)
        {
            if (row == null) return;
            string? number = numberGetter(row);
            if (string.IsNullOrWhiteSpace(number)) return;
            try { Clipboard.SetText(number); }
            catch { /* clipboard unavailable (rare) */ }
        }

        // ── Pagination & state ─────────────────────────────────────────

        private int _currentPage = 1;
        public int CurrentPage
        {
            get => _currentPage;
            private set
            {
                _currentPage = value; OnPropertyChanged();
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(PageIndicator));
                AsyncUtility.InvalidateCommandsThrottled();
            }
        }
        private int _totalPages;
        public int TotalPages
        {
            get => _totalPages;
            private set
            {
                _totalPages = value; OnPropertyChanged();
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(PageIndicator));
                AsyncUtility.InvalidateCommandsThrottled();
            }
        }
        public string PageIndicator => $"Page {CurrentPage} of {TotalPages}";
        public bool CanGoPrevious => CurrentPage > 1;
        public bool CanGoNext => CurrentPage < TotalPages;

        private bool _isLoading = true;
        public bool IsLoading { get => _isLoading; private set => SetField(ref _isLoading, value); }
        private string _errorText = "";
        public string ErrorText
        {
            get => _errorText;
            private set { if (SetField(ref _errorText, value)) OnPropertyChanged(nameof(HasError)); }
        }
        public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
        private string _statusText = "Loading…";
        public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
        private void SetStatus(string value) => StatusText = value;

        private void ResetFilters()
        {
            _searchText = ""; _selectedDateRange = "All time";
            _selectedSalesman = "All salesmen"; _selectedPreparedBy = "All prepared by";
            _selectedPriority = "All priorities"; _selectedSort = "Updated newest";
            _hasAttachmentsOnly = false;
            foreach (var tab in StatusTabs) tab.IsSelected = tab.Key.Length == 0;
            OnPropertyChanged(nameof(SearchText)); OnPropertyChanged(nameof(SelectedDateRange));
            OnPropertyChanged(nameof(SelectedSalesman)); OnPropertyChanged(nameof(SelectedPreparedBy));
            OnPropertyChanged(nameof(SelectedPriority)); OnPropertyChanged(nameof(SelectedSort));
            OnPropertyChanged(nameof(HasAttachmentsOnly)); OnPropertyChanged(nameof(HasActiveFilters));
            // Reset clears the status by flipping the tabs' own flags, so the
            // dropdown has to be told: it displays the tab, not the key.
            OnPropertyChanged(nameof(SelectedStatusTab));
            ResetAndReload();
        }

        public void Dispose()
        {
            try { _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = null; } catch { /* teardown is best-effort */ }
            try { _debounceCts?.Cancel(); _debounceCts?.Dispose(); _debounceCts = null; } catch { /* teardown is best-effort */ }
            try { _importCts?.Cancel(); _importCts?.Dispose(); _importCts = null; } catch { /* teardown is best-effort */ }
        }
    }
}
