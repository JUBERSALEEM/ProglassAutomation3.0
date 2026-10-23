// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/ViewModels/InquiryEditorViewModel.cs
// MODULE: Inquiry — workspace (detail page) view-model.
//
// Ported from the legacy InquiryEditorViewModel. Behaviour carried
// over:
//   - S4: "attach before save" dead end → the draft is auto-saved
//     before the first attachment so the user's action completes;
//   - S6: Cancel silently discarded work → dirty-state tracking +
//     discard-confirmation callback;
//   - S8: silent decimal binding failures → explicit ValueText parsing
//     with inline validation;
//   - A5: unobserved fire-and-forget loads → SafeFireAndForget with
//     guards.
//
// Form state lives in bindable properties (UpdateSourceTrigger=
// PropertyChanged in the view), so a button click can never commit a
// half-updated record.
//
// V3 scope: customer / location / salesman suggestions come from the
// register itself (distinct values) because the master-data module is
// not ported yet; the master-data write-back is skipped. The
// validation rules are the shared InquiryValidationRules — untouched.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Services.Inquiry;
using ProGlassAutomation.ThemeStudio;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.Inquiry
{
    public sealed class InquiryEditorViewModel : ViewModelBase
    {
        private readonly IInquiryRepository _repository;
        private readonly IInquiryWorkflowService _workflow;
        private readonly InquiryAttachmentService _attachments;
        // Register-sourced suggestion lists (master data not yet ported):
        // distinct customers, and (customer, location) pairs.
        private List<string> _savedCustomers = new();
        private List<(string Customer, string Location)> _customerLocations = new();
        // Tracks whether the user manually edited CustomerLocation so we
        // don't stomp their value when a known customer is picked.
        private bool _locationManuallyEdited;
        // Set by CancelLoaders when the editor is dismissed; the
        // fire-and-forget loaders check it before mutating VM state.
        private bool _isDisposed;

        public InquiryRecord Record { get; }
        public bool IsNewRecord => Record.Id == 0;
        public string HeaderTitle => IsNewRecord ? "New inquiry" : Record.InquiryNumber;
        public string SaveCommandText => IsNewRecord ? "Save inquiry" : "Save changes";
        public string HeaderSubtitle => IsNewRecord
            ? "Capture the commercial intake — attachments can be added right away"
            : $"Received {Record.ReceivedAt:dd MMM yyyy} · last updated {Record.UpdatedAt:dd MMM yyyy}";

        public ObservableCollection<InquiryAttachmentItem> AttachmentItems { get; } = new();
        public bool HasAttachments => AttachmentItems.Count > 0;

        // ── Scope of supply (BOQ) ──────────────────────────────────────
        // Editable rows — what the customer actually asked for,
        // dimension-first (like the PI grid). ConvertToPi imports these
        // into Step 5 spec groups so quoted shapes are never re-typed
        // by the estimator.
        public ObservableCollection<InquiryLineItemRow> LineItems { get; } = new();
        public bool HasLineItems => LineItems.Count > 0;
        public decimal LineItemsAreaSqm => LineItems
            .Sum(r => (decimal)r.WidthMm * r.HeightMm * r.Qty) / 1_000_000m;
        public string LineItemsAreaText =>
            LineItemsAreaSqm == 0 ? "0" : LineItemsAreaSqm.ToString("N1", CultureInfo.CurrentCulture);
        /// <summary>Glass builds allowed in BOQ rows — mirrors PI builds.</summary>
        public static IReadOnlyList<string> BoqGlassTypes { get; } =
            new[] { "SGU", "DGU", "LAM", "DGU+LAM" };

        public ICommand AddLineItemCommand { get; }
        public ICommand RemoveLineItemCommand { get; }

        private void AddLineItem()
        {
            var row = new InquiryLineItemRow();
            row.DirtyChanged += OnLineItemDirtyChanged;
            LineItems.Add(row);
            IsDirty = true;
            OnPropertyChanged(nameof(HasLineItems));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(LineItemsAreaSqm));
            OnPropertyChanged(nameof(LineItemsAreaText));
            AsyncUtility.InvalidateCommandsThrottled();
        }

        private void RemoveLineItem(object? parameter)
        {
            if (parameter is not InquiryLineItemRow row) return;
            row.DirtyChanged -= OnLineItemDirtyChanged;
            LineItems.Remove(row);
            IsDirty = true;
            OnPropertyChanged(nameof(HasLineItems));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(LineItemsAreaSqm));
            OnPropertyChanged(nameof(LineItemsAreaText));
            AsyncUtility.InvalidateCommandsThrottled();
        }

        private void OnLineItemDirtyChanged(object? sender, EventArgs e)
        {
            IsDirty = true;
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(LineItemsAreaSqm));
            OnPropertyChanged(nameof(LineItemsAreaText));
            AsyncUtility.InvalidateCommandsThrottled();
        }

        public Func<string[]?>? SelectFilesRequested { get; set; }
        public Func<bool>? ConfirmDiscardRequested { get; set; }
        public Func<InquiryAttachmentItem, bool>? ConfirmDeleteRequested { get; set; }
        public Action? OpenAttachmentsFolderRequested { get; set; }

        // ── Lifecycle & activity sections ──────────────────────────────
        /// <summary>
        /// The six commercial-chain stages, display order. Rebuilt from
        /// the record's own downstream link fields (V3); DN / TI /
        /// STMT stay NotStarted until those modules are ported.
        /// </summary>
        public ObservableCollection<InquiryStageInfo> Stages { get; } = new();
        /// <summary>Recent communications (status changes, BOQ saves, …).</summary>
        public ObservableCollection<InquiryActivityItem> RecentActivity { get; } = new();
        public bool HasRecentActivity => RecentActivity.Count > 0;

        public ObservableCollection<string> Priorities { get; } = new() { "Urgent", "High", "Normal", "Low" };
        public ObservableCollection<string> Salesmen { get; } = new();
        public ObservableCollection<string> Customers { get; } = new();
        /// <summary>
        /// Distinct saved locations for the current customer. The bound
        /// ComboBox shows them as suggestions but the user can still
        /// type a new value. Refreshed whenever the customer name
        /// changes.
        /// </summary>
        public ObservableCollection<string> CustomerLocations { get; } = new();

        // ── Form fields (each setter marks the form dirty) ─────────────

        private string _reference = "";
        public string Reference { get => _reference; set => SetField(ref _reference, value); }
        private string _customerName = "";
        public string CustomerName
        {
            get => _customerName;
            set
            {
                if (!SetField(ref _customerName, value)) return;
                RefreshCustomerLocations();
                // Auto-fill the location the first time the user picks a
                // known customer — but never clobber a value they typed
                // themselves.
                if (!_locationManuallyEdited)
                {
                    var match = FindClientLocation(_customerName);
                    if (match != null)
                        SetLocation(match, markManual: false);
                }
            }
        }
        private string _customerLocation = "";
        public string CustomerLocation
        {
            get => _customerLocation;
            set
            {
                if (!SetField(ref _customerLocation, value)) return;
                // Any time the user touches the location box, mark it
                // manually edited so future auto-fills don't overwrite it.
                _locationManuallyEdited = !string.IsNullOrWhiteSpace(_customerLocation)
                    || _locationManuallyEdited;
            }
        }
        private string _projectName = "";
        public string ProjectName { get => _projectName; set => SetField(ref _projectName, value); }
        private string _projectDescription = "";
        public string ProjectDescription { get => _projectDescription; set => SetField(ref _projectDescription, value); }
        private string _salesmanName = "";
        public string SalesmanName
        {
            get => _salesmanName;
            set
            {
                if (SetField(ref _salesmanName, value))
                {
                    // The dropdown list grows for genuinely new names so
                    // the value stays visible while typing.
                    if (!string.IsNullOrWhiteSpace(_salesmanName)
                        && !Salesmen.Any(s => string.Equals(s, _salesmanName, StringComparison.OrdinalIgnoreCase)))
                    {
                        Salesmen.Add(_salesmanName);
                    }
                }
            }
        }
        private string _notes = "";
        public string Notes { get => _notes; set => SetField(ref _notes, value); }
        private string _priority = "Normal";
        public string Priority { get => _priority; set => SetField(ref _priority, value); }
        private DateTime _receivedDate = DateTime.Today;
        public DateTime ReceivedDate
        {
            get => _receivedDate;
            set { if (SetField(ref _receivedDate, value.Date)) OnPropertyChanged(nameof(ReceivedHint)); }
        }
        private string _valueText = "";
        public string ValueText
        {
            get => _valueText;
            set
            {
                if (SetField(ref _valueText, value))
                {
                    OnPropertyChanged(nameof(HasValueError));
                    OnPropertyChanged(nameof(ValueHint));
                }
            }
        }
        public bool HasValueError
        {
            get
            {
                var text = _valueText.Trim();
                if (text.Length == 0) return false;
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value))
                    return true;
                return value < 0m || value > InquiryValidationRules.MaxInquiryValue;
            }
        }
        public string ValueHint => HasValueError
            ? $"Enter an AED amount from 0 to {InquiryValidationRules.MaxInquiryValue:N0}."
            : "Estimated inquiry value in AED.";

        // Received time is preserved from the original record; the date
        // box only changes the calendar day.
        public string ReceivedHint => $"Received at {ReceivedDate.Add(_receivedTime):hh:mm tt}";
        private readonly TimeSpan _receivedTime;

        // ── Status & commands ──────────────────────────────────────────

        public string Status => Record.Status;
        public string StatusTag => Record.Status switch
        {
            InquiryStatuses.Confirmed => "Success",
            InquiryStatuses.Prepared => "Information",
            InquiryStatuses.Sent => "Information",
            InquiryStatuses.Hold => "Warning",
            InquiryStatuses.Cancelled => "Error",
            _ => ""
        };
        public bool CanPrepare => InquiryWorkflowService.CanTransition(Record.Status, InquiryStatuses.Prepared);
        public bool CanSend => InquiryWorkflowService.CanTransition(Record.Status, InquiryStatuses.Sent);
        public bool CanConfirm => InquiryWorkflowService.CanTransition(Record.Status, InquiryStatuses.Confirmed);
        public bool CanHold => InquiryWorkflowService.CanTransition(Record.Status, InquiryStatuses.Hold);
        public bool CanCancel => InquiryWorkflowService.CanTransition(Record.Status, InquiryStatuses.Cancelled);
        public string PreparedDateText => Record.PreparedAt?.ToString("dd MMM · hh:mm tt") ?? "";
        public string SentDateText => Record.SentAt?.ToString("dd MMM · hh:mm tt") ?? "";
        public string ConfirmedDateText => Record.ConfirmedAt?.ToString("dd MMM · hh:mm tt") ?? "";
        /// <summary>
        /// Pipeline progress computed from the record's own downstream
        /// link fields (V3): 1 = inquiry only, 2 = PI linked,
        /// 3 = JO linked. DN / TI / STMT promote it once ported.
        /// </summary>
        public string PipelineProgressText
        {
            get
            {
                int furthest = 0;
                if (!string.IsNullOrWhiteSpace(Record.ProformaNumber)) furthest = 1;
                if (!string.IsNullOrWhiteSpace(Record.JobOrderNumber)) furthest = 2;
                return $"{furthest + 1}/6";
            }
        }

        public ICommand SaveCommand { get; }
        /// <summary>"← Register" button: guarded exit (discards if dirty).</summary>
        public ICommand GoBackCommand { get; }
        public ICommand ChangeStatusCommand { get; }
        public ICommand AddAttachmentCommand { get; }
        public ICommand DeleteAttachmentCommand { get; }
        public ICommand OpenAttachmentCommand { get; }
        public ICommand OpenAttachmentsFolderCommand { get; }
        public ICommand CancelCommand { get; }

        /// <summary>Raised with true when the record was saved and the editor should close.</summary>
        public event Action<bool>? CloseRequested;

        public InquiryEditorViewModel(
            IInquiryRepository repository,
            IInquiryWorkflowService workflow,
            InquiryAttachmentService attachments,
            InquiryRecord? source = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            _attachments = attachments ?? throw new ArgumentNullException(nameof(attachments));
            Record = source ?? new InquiryRecord
            {
                InquiryReference = "Customer Inquiry", Status = InquiryStatuses.Received,
                Priority = "Normal", ReceivedAt = DateTime.Now
            };
            _reference = Record.InquiryReference;
            _customerName = Record.CustomerName;
            _customerLocation = Record.CustomerLocation;
            _locationManuallyEdited = !string.IsNullOrWhiteSpace(_customerLocation);
            _projectName = Record.ProjectName;
            _projectDescription = Record.ProjectDescription;
            _salesmanName = Record.SalesmanName;
            _salesmanManuallyEdited = !string.IsNullOrWhiteSpace(_salesmanName);
            _notes = Record.Notes;
            _priority = string.IsNullOrWhiteSpace(Record.Priority) ? "Normal" : Record.Priority;
            _receivedDate = Record.ReceivedAt == default ? DateTime.Today : Record.ReceivedAt.Date;
            _receivedTime = Record.ReceivedAt == default ? DateTime.Now.TimeOfDay : Record.ReceivedAt.TimeOfDay;
            _valueText = Record.InquiryValue == 0 ? "" : Record.InquiryValue.ToString("N2", CultureInfo.CurrentCulture);
            _isDirty = false;

            // Hydrate the scope of supply. Detail loads Include
            // LineItems; a fresh record starts empty. Hydration must NOT
            // mark the form dirty — the event is wired after
            // construction.
            foreach (var item in (Record.LineItems ?? new List<InquiryLineItem>())
                     .OrderBy(x => x.SortOrder))
            {
                LineItems.Add(InquiryLineItemRow.FromEntity(item));
            }
            foreach (var row in LineItems)
                row.DirtyChanged += OnLineItemDirtyChanged;

            AddLineItemCommand = new DelegateCommand(AddLineItem, () => !IsBusy);
            RemoveLineItemCommand = new DelegateCommand(p => RemoveLineItem(p), () => !IsBusy);

            SaveCommand = new DelegateCommand(async () => await SaveAndCloseAsync(), () => CanSave && !IsBusy);
            ChangeStatusCommand = new DelegateCommand(p => _ = ChangeStatusAsync(p as string), () => !IsBusy);
            AddAttachmentCommand = new DelegateCommand(async () => await AddAttachmentsAsync(), () => !IsBusy);
            DeleteAttachmentCommand = new DelegateCommand(async p =>
            {
                if (p is InquiryAttachmentItem item && ConfirmDeleteRequested?.Invoke(item) == true)
                {
                    IsBusy = true;
                    try
                    {
                        bool removed = await _attachments.DeleteAsync(item.Attachment);
                        if (removed) AttachmentItems.Remove(item);
                        SetMessage(removed ? $"Deleted '{item.FileName}'." : "Attachment was already removed.");
                        OnPropertyChanged(nameof(HasAttachments));
                    }
                    catch (Exception ex) { SetMessage("Delete failed: " + ExceptionMessages.Deep(ex), isError: true); }
                    finally { IsBusy = false; }
                }
            }, () => !IsBusy);
            OpenAttachmentCommand = new DelegateCommand(p =>
            {
                if (p is InquiryAttachmentItem item)
                {
                    var result = _attachments.Open(item.Attachment);
                    if (!result.IsSuccess) SetMessage(result.Error, isError: true);
                }
            }, () => !IsBusy);
            OpenAttachmentsFolderCommand = new DelegateCommand(() =>
            {
                if (Record.Id > 0)
                    OpenAttachmentsFolderRequested?.Invoke();
            }, () => !IsNewRecord);
            CancelCommand = new DelegateCommand(CloseOrDiscard);
            GoBackCommand = new DelegateCommand(CloseOrDiscard);

            RefreshStages();
            AsyncUtility.SafeFireAndForget(LoadSuggestListsAsync);
            AsyncUtility.SafeFireAndForget(LoadAttachmentsAsync);
            AsyncUtility.SafeFireAndForget(LoadCommunicationsAsync);
        }

        private async Task LoadCommunicationsAsync()
        {
            if (_isDisposed) return;
            if (Record.Id <= 0) return;
            try
            {
                var rows = await _repository.GetCommunicationsAsync(Record.Id);
                if (_isDisposed) return;
                RecentActivity.Clear();
                foreach (var x in rows)
                {
                    RecentActivity.Add(new InquiryActivityItem
                    {
                        Title = x.Title,
                        Description = x.Description,
                        Actor = x.ActorName,
                        OccurredAt = x.OccurredAt
                    });
                }
                OnPropertyChanged(nameof(HasRecentActivity));
            }
            catch (Exception ex) { SetMessage("Activity unavailable: " + ExceptionMessages.Deep(ex), isError: true); }
        }

        /// <summary>
        /// Rebuilds the six pipeline stage rows from the record's
        /// downstream link fields. Called at load and after every
        /// status change / save.
        /// </summary>
        private void RefreshStages()
        {
            Stages.Clear();
            bool hasPi = !string.IsNullOrWhiteSpace(Record.ProformaNumber);
            bool hasJo = !string.IsNullOrWhiteSpace(Record.JobOrderNumber);
            string inquiryState = Record.Status switch
            {
                InquiryStatuses.Cancelled => "Skipped",
                InquiryStatuses.Hold => "Hold",
                InquiryStatuses.Confirmed => "Completed",
                _ => "Active"
            };
            Stages.Add(new InquiryStageInfo("Inquiry", "INQ",
                $"{Record.InquiryNumber} · {Record.Status}", InquiryStageTag(inquiryState)));
            Stages.Add(new InquiryStageInfo("Proforma Invoice", "PI",
                hasPi ? $"{Record.ProformaNumber} · Active" : "Not started",
                hasPi ? "Information" : ""));
            Stages.Add(new InquiryStageInfo("Job Order", "JO",
                hasJo ? $"{Record.JobOrderNumber} · Active" : "Not started",
                hasJo ? "Information" : ""));
            Stages.Add(new InquiryStageInfo("Delivery Note", "DN", "Not started", ""));
            Stages.Add(new InquiryStageInfo("Tax Invoice", "TI", "Not started", ""));
            Stages.Add(new InquiryStageInfo("Statement", "STMT", "Not started", ""));
        }

        private static string InquiryStageTag(string state) => state switch
        {
            "Completed" => "Success",
            "Active" => "Information",
            "Hold" => "Warning",
            "Skipped" => "Error",
            _ => ""
        };

        private bool _isDirty;
        public bool IsDirty
        {
            get => _isDirty;
            private set { if (SetField(ref _isDirty, value)) OnPropertyChanged(nameof(HeaderDotText)); }
        }
        public string HeaderDotText => IsDirty ? "●" : "";

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }

        public bool CanSave =>
            !string.IsNullOrWhiteSpace(CustomerName)
            && !string.IsNullOrWhiteSpace(ProjectName)
            && !HasValueError
            && InquiryValidationRules.ValidateLineItems(
                LineItems.Select(r => r.ToEntity()).ToList()).Count == 0;

        private async Task LoadSuggestListsAsync()
        {
            if (_isDisposed) return;
            try
            {
                _savedCustomers = (await _repository.GetDistinctCustomersAsync()).ToList();
                _customerLocations = (await _repository.GetCustomerLocationsAsync()).ToList();
                if (_isDisposed) return;
                foreach (var x in _savedCustomers)
                    if (!string.IsNullOrWhiteSpace(x) && !Customers.Contains(x)) Customers.Add(x);
                foreach (var x in await _repository.GetDistinctSalesmenAsync())
                    if (!string.IsNullOrWhiteSpace(x) && !Salesmen.Contains(x)) Salesmen.Add(x);
                if (!string.IsNullOrWhiteSpace(_salesmanName) && !Salesmen.Contains(_salesmanName))
                    Salesmen.Add(_salesmanName);
                RefreshCustomerLocations();
            }
            catch (Exception ex) { SetMessage("Reference lists unavailable: " + ExceptionMessages.Deep(ex), isError: true); }
        }

        // ── Suggestion helpers (register-sourced in V3) ────────────────

        /// <summary>
        /// Returns the location the register has recorded for this
        /// customer (first one seen). Null when the customer is unknown
        /// or never had a location — auto-fill is then skipped, never
        /// guessed.
        /// </summary>
        private string? FindClientLocation(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _customerLocations
                .FirstOrDefault(x => string.Equals(x.Customer, name, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(x.Location)).Location;
        }

        private void RefreshCustomerLocations()
        {
            CustomerLocations.Clear();
            if (string.IsNullOrWhiteSpace(_customerName)) return;
            var addresses = _customerLocations
                .Where(x => string.Equals(x.Customer, _customerName, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Location)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var a in addresses) CustomerLocations.Add(a);
            // Make sure the currently-displayed location still shows up
            // even if the register doesn't list it.
            if (!string.IsNullOrWhiteSpace(_customerLocation)
                && !CustomerLocations.Contains(_customerLocation))
            {
                CustomerLocations.Insert(0, _customerLocation);
            }
        }

        private void SetLocation(string value, bool markManual)
        {
            if (_customerLocation == value) return;
            _customerLocation = value;
            OnPropertyChanged(nameof(CustomerLocation));
            if (markManual) _locationManuallyEdited = true;
            IsDirty = true;
            OnPropertyChanged(nameof(CanSave));
        }

        private async Task LoadAttachmentsAsync()
        {
            if (_isDisposed) return;
            if (Record.Id <= 0) return;
            try
            {
                // Manifest-backed — no EF read, immune to legacy
                // child-table drift; legacy rows migrate on first call.
                var rows = await _attachments.GetAsync(Record.Id);
                if (_isDisposed) return;
                AttachmentItems.Clear();
                foreach (var x in rows) AttachmentItems.Add(InquiryAttachmentItem.FromModel(x));
                OnPropertyChanged(nameof(HasAttachments));
            }
            catch (Exception ex) { SetMessage("Attachments unavailable: " + ExceptionMessages.Deep(ex), isError: true); }
        }

        /// <summary>
        /// Marks the VM as disposed so the fire-and-forget loaders
        /// short-circuit on their next await and the closures that
        /// captured `this` can be collected.
        /// </summary>
        public void CancelLoaders()
        {
            _isDisposed = true;
        }

        // ── Save ───────────────────────────────────────────────────────

        private void ApplyToRecord()
        {
            Record.InquiryReference = string.IsNullOrWhiteSpace(Reference) ? "Customer Inquiry" : Reference.Trim();
            Record.CustomerName = CustomerName.Trim();
            Record.CustomerLocation = CustomerLocation.Trim();
            Record.ProjectName = ProjectName.Trim();
            Record.ProjectDescription = ProjectDescription.Trim();
            Record.SalesmanName = SalesmanName.Trim();
            Record.Notes = Notes;
            Record.Priority = Priorities.Contains(Priority) ? Priority : "Normal";
            Record.ReceivedAt = ReceivedDate == DateTime.MinValue
                ? DateTime.Now
                : ReceivedDate.Add(_receivedTime);
            Record.InquiryValue = decimal.TryParse(ValueText, NumberStyles.Number,
                CultureInfo.CurrentCulture, out decimal value) ? decimal.Round(value, 2) : 0m;
        }

        /// <summary>Creates or updates the record. Returns the saved record or null on failure.</summary>
        private async Task<InquiryRecord?> SaveAsync()
        {
            if (!CanSave)
            {
                SetMessage(string.IsNullOrWhiteSpace(CustomerName)
                    ? "Customer is required before saving."
                    : string.IsNullOrWhiteSpace(ProjectName)
                        ? "Project is required before saving."
                        : "Fix the highlighted fields before saving.", isError: true);
                return null;
            }
            ApplyToRecord();
            // Build the scope-of-supply entities once, then run the
            // shared industrial gate. The button CanSave uses the same
            // rules, but SaveAsync is the non-bypassable boundary for
            // shortcuts, attachment auto-save and tests.
            var boqToPersist = LineItems.Select(r => r.ToEntity()).ToList();
            var blockingIssues = InquiryValidationRules.ValidateForSave(Record, boqToPersist);
            if (blockingIssues.Count > 0)
            {
                SetMessage(string.Join(" ", blockingIssues.Take(3)), isError: true);
                return null;
            }
            // A fresh record must go to CreateAsync HEADER-ONLY: EF
            // would otherwise graph-insert the rows via the navigation,
            // and the replace-all call below would re-write them.
            var wasNewRecord = Record.Id == 0;
            Record.LineItems = new List<InquiryLineItem>();
            int expectedRowVersion = Record.RowVersion;
            var saved = wasNewRecord
                ? await _workflow.CreateAsync(Record)
                : await _workflow.UpdateAsync(Record, expectedRowVersion);
            Record.RowVersion = saved.RowVersion;
            Record.LineItems = boqToPersist;
            // Scope persistence must never break a header save — the
            // replace-all call runs in its own commit and advances the
            // RowVersion again.
            if (boqToPersist.Count > 0 || !wasNewRecord)
            {
                int boqRowVersion = await _repository.SaveLineItemsAsync(
                    saved.Id,
                    boqToPersist,
                    saved.RowVersion,
                    Services.Audit.CurrentOperator.DisplayName);
                Record.RowVersion = saved.RowVersion = boqRowVersion;
            }
            IsDirty = false;
            OnPropertyChanged(nameof(IsNewRecord));
            OnPropertyChanged(nameof(HeaderTitle));
            OnPropertyChanged(nameof(HeaderSubtitle));
            OnPropertyChanged(nameof(SaveCommandText));
            OnPropertyChanged(nameof(PipelineProgressText));
            RefreshStages();
            return saved;
        }

        private async Task SaveAndCloseAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                SetMessage("Saving…");
                if (await SaveAsync() != null)
                {
                    SetMessage($"Saved {Record.InquiryNumber}.");
                    CloseRequested?.Invoke(true);
                }
            }
            catch (Services.Database.ConcurrencyConflictException)
            {
                SetMessage("This inquiry was changed by someone else after you opened it. Your changes were not saved — reload the register and re-apply your changes.", isError: true);
            }
            catch (Exception ex) { SetMessage(ExceptionMessages.Deep(ex), isError: true); }
            finally { IsBusy = false; }
        }

        // ── Attachments ────────────────────────────────────────────────

        /// <summary>Public entry for drag-and-drop from the view.</summary>
        public Task AddDroppedFilesAsync(string[] files) => AddFilesAsync(files);

        private async Task AddAttachmentsAsync()
        {
            var files = SelectFilesRequested?.Invoke();
            if (files == null || files.Length == 0) return;
            await AddFilesAsync(files);
        }

        private async Task AddFilesAsync(string[] files)
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                // Auto-save the draft before the first attachment so the
                // user's action always completes instead of dead-ending.
                if (Record.Id == 0)
                {
                    SetMessage("Saving draft before attaching…");
                    if (await SaveAsync() == null) return;
                }
                int added = 0;
                foreach (var path in files)
                {
                    try
                    {
                        AttachmentItems.Add(InquiryAttachmentItem.FromModel(await _attachments.AddAsync(Record.Id, path)));
                        added++;
                    }
                    catch (Exception ex)
                    {
                        SetMessage($"'{System.IO.Path.GetFileName(path)}' failed: {ExceptionMessages.Deep(ex)}", isError: true);
                    }
                }
                if (added > 0)
                {
                    SetMessage($"{added} attachment{(added == 1 ? "" : "s")} added.");
                    OnPropertyChanged(nameof(HasAttachments));
                }
            }
            catch (Exception ex) { SetMessage("Attachment failed: " + ExceptionMessages.Deep(ex), isError: true); }
            finally { IsBusy = false; }
        }

        // ── Status workflow ────────────────────────────────────────────

        private async Task ChangeStatusAsync(string? next)
        {
            if (string.IsNullOrWhiteSpace(next) || Record.Id <= 0 || IsBusy) return;
            // Guard the record before a workflow transition (dirty edits
            // belong in the save, not silently in a status change).
            if (IsDirty)
            {
                SetMessage("Save your changes before changing the status.", isError: true);
                return;
            }
            if (!InquiryWorkflowService.CanTransition(Record.Status, next))
            {
                SetMessage($"Cannot move from {Record.Status} to {next}.", isError: true);
                return;
            }
            IsBusy = true;
            try
            {
                if (await _workflow.ChangeStatusAsync(
                    Record.Id, Record.Status, next, Record.RowVersion))
                {
                    Record.RowVersion++;
                    Record.Status = InquiryStatuses.Normalize(next);
                    // Mirror the workflow timestamps so the stepper
                    // reflects the transition immediately (persisted by
                    // the repository).
                    if (Record.Status == InquiryStatuses.Prepared) Record.PreparedAt ??= DateTime.Now;
                    if (Record.Status == InquiryStatuses.Sent) Record.SentAt ??= DateTime.Now;
                    if (Record.Status == InquiryStatuses.Confirmed) Record.ConfirmedAt ??= DateTime.Now;
                    Record.UpdatedAt = DateTime.Now;
                    RefreshStages();
                    RefreshStatusProperties();
                    SetMessage($"Status changed to {Record.Status}.");
                }
            }
            catch (Exception ex) { SetMessage(ExceptionMessages.Deep(ex), isError: true); }
            finally { IsBusy = false; }
        }

        private void RefreshStatusProperties()
        {
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusTag));
            OnPropertyChanged(nameof(CanPrepare));
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanConfirm));
            OnPropertyChanged(nameof(CanHold));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(PreparedDateText));
            OnPropertyChanged(nameof(SentDateText));
            OnPropertyChanged(nameof(ConfirmedDateText));
            AsyncUtility.InvalidateCommandsThrottled();
        }

        // ── Close ──────────────────────────────────────────────────────

        private void CloseOrDiscard()
        {
            if (IsDirty && ConfirmDiscardRequested?.Invoke() != true) return;
            CloseRequested?.Invoke(false);
        }

        private string _statusText = "";
        public string StatusText
        {
            get => _statusText;
            private set { if (SetField(ref _statusText, value)) OnPropertyChanged(nameof(IsStatusError)); }
        }
        private bool _isStatusError;
        public bool IsStatusError { get => _isStatusError; private set => SetField(ref _isStatusError, value); }

        private void SetMessage(string text, bool isError = false)
        {
            StatusText = text;
            IsStatusError = isError;
        }
    }

    /// <summary>
    /// One row of the lifecycle section: a pipeline stage with its
    /// current detail text and coarse state (StatusBadge Tag).
    /// </summary>
    public sealed class InquiryStageInfo
    {
        public string Title { get; }
        public string Short { get; }
        public string Detail { get; }
        public string StateTag { get; }
        public bool IsStarted => !string.Equals(Detail, "Not started", StringComparison.Ordinal);

        public InquiryStageInfo(string title, string shortLabel, string detail, string stateTag)
        {
            Title = title; Short = shortLabel; Detail = detail; StateTag = stateTag;
        }
    }

    /// <summary>
    /// One editable scope-of-supply row in the inquiry editor.
    /// INPC wrapper around the persistence entity so grid edits mark the
    /// form dirty AND re-render (the entity itself is deliberately plain
    /// for EF use). Raised <see cref="DirtyChanged"/> on any field edit;
    /// hydration via <see cref="FromEntity"/> never raises it.
    /// </summary>
    public sealed class InquiryLineItemRow : ViewModelBase
    {
        /// <summary>Raised when any editable field changes.</summary>
        public event EventHandler? DirtyChanged;

        public static InquiryLineItemRow FromEntity(InquiryLineItem e) => new()
        {
            GlassType = e.GlassType,
            SpecName = e.SpecName,
            OuterGlassColor = e.OuterGlassColor,
            InnerGlassColor = e.InnerGlassColor,
            WidthMm = e.WidthMm,
            HeightMm = e.HeightMm,
            Qty = e.Qty,
            RatePerSqm = e.RatePerSqm,
            Notes = e.Notes
        };

        public InquiryLineItem ToEntity() => new()
        {
            GlassType = (GlassType ?? "").Trim(),
            SpecName = (SpecName ?? "").Trim(),
            OuterGlassColor = (OuterGlassColor ?? "").Trim(),
            InnerGlassColor = (InnerGlassColor ?? "").Trim(),
            WidthMm = WidthMm,
            HeightMm = HeightMm,
            Qty = Qty,
            RatePerSqm = RatePerSqm,
            Notes = Notes ?? ""
        };

        /// <summary>True when the row can produce a PI grid line.</summary>
        public bool IsImportable =>
            !string.IsNullOrWhiteSpace(SpecName) && WidthMm > 0 && HeightMm > 0 && Qty > 0;

        private string _glassType = "SGU";
        public string GlassType
        {
            get => _glassType;
            set { if (_glassType == value) return; _glassType = value; Notify(); }
        }

        private string _specName = "";
        public string SpecName
        {
            get => _specName;
            set { if (_specName == value) return; _specName = value; OnPropertyChanged(nameof(IsImportable)); Notify(); }
        }

        private string _outerGlassColor = "";
        public string OuterGlassColor
        {
            get => _outerGlassColor;
            set { if (_outerGlassColor == value) return; _outerGlassColor = value; Notify(); }
        }

        private string _innerGlassColor = "";
        public string InnerGlassColor
        {
            get => _innerGlassColor;
            set { if (_innerGlassColor == value) return; _innerGlassColor = value; Notify(); }
        }

        private int _widthMm;
        public int WidthMm
        {
            get => _widthMm;
            set { if (_widthMm == value) return; _widthMm = value; OnPropertyChanged(nameof(IsImportable)); Notify(); }
        }

        private int _heightMm;
        public int HeightMm
        {
            get => _heightMm;
            set { if (_heightMm == value) return; _heightMm = value; OnPropertyChanged(nameof(IsImportable)); Notify(); }
        }

        private int _qty = 1;
        public int Qty
        {
            get => _qty;
            set { if (_qty == value) return; _qty = value; OnPropertyChanged(nameof(IsImportable)); Notify(); }
        }

        private decimal _ratePerSqm;
        public decimal RatePerSqm
        {
            get => _ratePerSqm;
            set { if (_ratePerSqm == value) return; _ratePerSqm = value; Notify(); }
        }

        private string _notes = "";
        public string Notes
        {
            get => _notes;
            set { if (_notes == value) return; _notes = value; OnPropertyChanged(); DirtyChanged?.Invoke(this, EventArgs.Empty); }
        }

        private void Notify([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            OnPropertyChanged(name ?? "");
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
