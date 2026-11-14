// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/ProformaInvoiceViewModel.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — Wizard Shell (Phase 3: Async Cleanup)
// VERSION: Phase 3 — 2026-08-21 — Removed duplicate SafeFireAsync, uses central AsyncUtility
//
// CHANGES vs Phase 2:
//   ✓ Removed private static async void SafeFireAsync (7 duplicates across app)
//   ✓ All call sites now use Utilities.AsyncUtility.SafeFireAndForget (single async void)
//   ✓ CorrelationId auto-flow via CorrelationContext
//   ✓ No .Result / .Wait() — all await
//   ✓ VSTHRD100 compliant: only event handlers + AsyncUtility have async void
//
// PRESERVED: All 9 wizard steps, S-01/S-02 swap, BUG-77/78/79/80/81, approval flow
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.Step7PrintPreview.TermsAndConditions;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository;
using ProGlassAutomation.Utilities;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProGlassAutomation.Services.Localization;
using ProGlassAutomation.Services.Reporting;

using ProGlassAutomation.Services.Dialogs;
using ProGlassAutomation.Services.MasterData.Repository;
using ProGlassAutomation.Services.ProformaInvoice.SpecificationsRepository;
using Microsoft.Extensions.DependencyInjection;
namespace ProGlassAutomation.ViewModels.ProformaInvoice
{
    public class ProformaInvoiceViewModel : ViewModelBase,
        ProGlassAutomation.Agent.Context.IAgentContextAware,
        IDisposable
    {
        public string Title { get; private set; } = "Proforma Invoice Builder";

        public string AgentContextDetail
        {
            get
            {
                try
                {
                    var id = Step1VM?.ProformaId;
                    return string.IsNullOrWhiteSpace(id) ? "" : $"editing {id}";
                }
                catch { return ""; }
            }
        }
        public string Subtitle { get; private set; } = "Nine-station industrial quotation deck";

        public const int TotalSteps = 9;

        public ObservableCollection<InvoiceItem> Invoices { get; }
            = new ObservableCollection<InvoiceItem>();

        public decimal TotalSales => 0m;
        public int InvoiceCount => 0;
        public int PendingCount => 0;
        public bool HasInvoices => false;

        public Step1CompanySetup.CompanySetupViewModel Step1VM { get; private set; }
        public Step2ClientProfile.ClientProfileViewModel Step2VM { get; private set; }
        public Step3ProjectSpecs.ProjectSpecsViewModel Step3VM { get; private set; }
        public Step4PricingEstimator.PricingEstimatorViewModel Step4VM { get; private set; }
        public Step5DataGrid.DataGridViewModel Step5VM { get; private set; }
        public Step6OtherCharges.OtherChargesViewModel Step6VM { get; private set; }
        public Step7PrintPreview.PrintPreviewViewModel Step7VM { get; private set; }
        public Step8Attachments.AttachmentsStepViewModel Step8VM { get; private set; }
        public Step9ProfitLoss.ProfitLossStepViewModel Step9VM { get; private set; }

        public ObservableCollection<WizardStep> WizardSteps { get; private set; }

        private WizardStep _currentStep;
        private int _furthestStepReached = 1;
        private int? _pendingApprovalInvoiceId;

        /// <summary>
        /// The invoice currently loaded in the wizard (set by
        /// LoadInvoiceIntoWizardAsync). Lets the standalone Step 8
        /// "APPROVE INVOICE" action resolve which invoice to approve
        /// when the flow did not start from the Saved-Invoices list
        /// (2026-09-10 Step 8 approve unification).
        /// </summary>
        private int? _loadedInvoiceId;

        public WizardStep CurrentStep
        {
            get => _currentStep;
            set
            {
                if (_currentStep == value) return;
                if (_currentStep != null)
                    _currentStep.IsActive = false;
                _currentStep = value;
                if (_currentStep != null)
                    _currentStep.IsActive = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentStepNumber));
                OnPropertyChanged(nameof(CurrentStationCode));
                OnPropertyChanged(nameof(CurrentStationTitle));
                OnPropertyChanged(nameof(CurrentStationSubtitle));
                OnPropertyChanged(nameof(StepProgressText));
                OnPropertyChanged(nameof(ProgressPercent));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(IsLastStep));
                ProGlassAutomation.Utilities.AsyncUtility.InvalidateCommandsThrottled();
            }
        }

        public int CurrentStepNumber => CurrentStep?.StepNumber ?? 1;
        public string CurrentStationCode => CurrentStepNumber.ToString("00");
        public string CurrentStationTitle => CurrentStep?.Title ?? "";
        public string CurrentStationSubtitle => CurrentStep?.Subtitle ?? "";
        public string StepProgressText =>
            $"STATION  {CurrentStepNumber:00}  /  {TotalSteps:00}";
        public double ProgressPercent =>
            (double)CurrentStepNumber / TotalSteps;
        public bool CanGoBack => CurrentStepNumber > 1;
        public bool CanGoNext => CurrentStepNumber < TotalSteps;
        public bool IsLastStep => CurrentStepNumber == TotalSteps;

        private bool _isListView = true;
        public bool IsListView
        {
            get => _isListView;
            set
            {
                if (_isListView == value) return;
                _isListView = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWizardView));
            }
        }

        public bool IsWizardView => !_isListView;
        public SavedInvoicesList.SavedInvoicesListViewModel SavedInvoicesListVM { get; private set; }

        public ICommand NextStepCommand { get; private set; }
        public ICommand PreviousStepCommand { get; private set; }
        public ICommand GoToStepCommand { get; private set; }
        public ICommand NewInvoiceCommand { get; private set; }
        public ICommand BackToListCommand { get; private set; }
        public ICommand NewFromInquiryCommand { get; private set; }

        // ── Cross-module callbacks (wired by MainViewModel) ─────────────
        // Ask the host to show the user a 3-way picker for how a "new PI"
        // should start. The host shows a MessageBox; we never call
        // MessageBox from the VM. Returning a value of Cancelled means
        // the user dismissed the dialog; Skip means the user wants to
        // create a blank PI without an inquiry; FromInquiry means the
        // user wants to pick an existing inquiry (or create a new one)
        // first. The default when no callback is wired is Skip, which
        // preserves the pre-fix behaviour.
        public Func<Task<NewProformaStartChoice>>? AskStartFromInquiryRequested { get; set; }

        // Navigate the shell to the Inquiry module so the user can pick
        // or create an inquiry. Set by MainViewModel.
        public Action? NavigateToInquiryForNewPiRequested { get; set; }

        // True while a "new PI from inquiry" round-trip is in flight
        // (we sent the user to the Inquiry module and they're
        // expected to come back via the inquiry's "Convert to
        // Proforma Invoice" action). Used by the PI list header to
        // show a status message.
        private bool _awaitingInquirySelection;
        public bool IsAwaitingInquirySelection
        {
            get => _awaitingInquirySelection;
            set { if (_awaitingInquirySelection == value) return; _awaitingInquirySelection = value; OnPropertyChanged(); }
        }

        public enum NewProformaStartChoice
        {
            Cancelled = 0,
            Skip = 1,
            FromInquiry = 2
        }

        private readonly IDialogService _dialogs = null!;
        private readonly IProformaInvoiceRepository _invoices = null!;
        private readonly IMasterDataRepository _masterData = null!;
        private readonly ISpecificationsRepository _specs = null!;

        
        public ProformaInvoiceViewModel(IDialogService? dialogs,
            IProformaInvoiceRepository? invoices,
            IMasterDataRepository? masterData,
            ISpecificationsRepository? specs)
        {
            _dialogs = dialogs!;
            _invoices = invoices!;
            _masterData = masterData!;
            _specs = specs!;
        
            // ERR-633EEB: run shared initialization on EVERY construction
            // path. DI selects this (greediest) ctor, so without this call the
            // parameterless ctor's setup never ran and commands stayed null.
            InitializeCore();
        }
public ProformaInvoiceViewModel() : this(
            App.ServiceProvider?.GetService<IDialogService>() ?? new DialogService(),
            App.ServiceProvider?.GetService<IProformaInvoiceRepository>()!,
            App.ServiceProvider?.GetService<IMasterDataRepository>()!,
            App.ServiceProvider?.GetService<ISpecificationsRepository>()!)
        { }

        // ── Shared initialization (ERR-633EEB) ─────────────────────
        // Extracted verbatim from the parameterless ctor so both it
        // and the DI-selected parameterized ctor initialize fully.
        private void InitializeCore()
        {
            Step1VM = new Step1CompanySetup.CompanySetupViewModel(
                _invoices, _masterData, stepNumber => GoToStep(stepNumber));

            Step2VM = new Step2ClientProfile.ClientProfileViewModel(_masterData);
            Step3VM = new Step3ProjectSpecs.ProjectSpecsViewModel(_masterData);

            Step4VM = new Step4PricingEstimator.PricingEstimatorViewModel(_dialogs, _specs);
            // Step 5 must use this wizard's injected specification repository.
            // Passing only dialogs selected the optional-parameter constructor
            // with a null repository, leaving every Step 5 catalogue empty.
            Step5VM = new Step5DataGrid.DataGridViewModel(_dialogs, _specs);
            Step6VM = new Step6OtherCharges.OtherChargesViewModel(Step5VM);
            Step6VM.BindStep4Pricing(() => Step4VM.CaptureSectionSnapshots());

            // 2026-09-16: SHEET QTY and the optimization % can be read from a
            // finished optimization run instead of typed by hand. The feed
            // prefers a run produced in this app session and falls back to the
            // newest saved session; RefreshAsync refuses to trade live figures
            // for an older saved one.
            //
            // 2026-09-17: a row now reads the run automatically once the feed
            // actually resolves one for its panes, so the operator no longer has
            // to flip a per-row switch to get real numbers. Manual stays fully
            // available — the switch is still there, and an explicit OFF is
            // never overridden. See WastageRow.TryEnableFeedAutomatically.
            //
            // DOCUMENT GUARD: a run stamped for a DIFFERENT proforma is filtered
            // out here, so Step 6 sees an empty feed and keeps its manual values
            // rather than reading another job's sheet count. The join is
            // structural (spec number + side + sheet size), so two quotes that
            // both have a "Spec 1 outer" on the same rack size would otherwise
            // match each other.
            Step6VM.BindOptimizationFeed(
                () => Services.Optimization.OptimizationResultFeed.IsForDocument(CurrentDocumentKey)
                    ? Services.Optimization.OptimizationResultFeed.Current
                    : System.Array.Empty<Services.Optimization.OptimizationPaneResult>());

            AsyncUtility.SafeFireAndForget(async () =>
            {
                // Passes this proforma's identity so the run loaded back from
                // disk is the one THIS quote was cut against, not simply the
                // newest one anywhere in the database.
                await Services.Optimization.OptimizationResultFeed.RefreshAsync(CurrentDocumentKey);
                Step6VM.RefreshOptimizationFeed();
            }, "Step 6 optimization feed");

            // QUICK / OPTIMIZE on the Step 5 top panel: runs the engine over the
            // live grid and publishes to the same feed, stamped with this
            // proforma. Late-bound so the grid never references the
            // optimization module — the same idiom as the two bindings above.
            Step5VM.BindOptimizationRunner(RunOptimizationFromStep5);

            // Step 5's VIEW RESULT. The grid raises it without knowing where
            // "the result" lives; this adds the one thing only the invoice
            // knows — which document the run belongs to — and hands it up to
            // the shell. A run is persisted against its document key, so
            // without it the module would open the global row, which belongs
            // to whoever last optimized with no invoice open.
            Step5VM.ViewOptimizationResultRequested += (_, _) =>
                NavigateToOptimizationSummaryRequested?.Invoke(CurrentDocumentKey);

            // A run finished while this invoice was open. Without this the feed
            // would keep showing the previous run until something else happened
            // to re-resolve it. Static event, so it is torn down in Dispose
            // exactly like the DocumentLifecycle subscription below.
            Services.Optimization.OptimizationResultFeed.RunPublished +=
                OnOptimizationRunPublished;
            // 2026-09-10: inventory auto-use history context (customer from
            // Step 2; consumption entries identify the job like the manual
            // Sheet Store Use dialog does).
            Step6VM.BindJobContext(() =>
                ("", Step2VM?.SelectedClientName ?? "", "", ""));
            Step6VM.BindLaminationEditors(Step4VM.LamVM, Step4VM.DguLamVM);
            Step4VM.PropertyChanged += OnStep4PropertyChanged;

            Step7VM = new Step7PrintPreview.PrintPreviewViewModel(
                _dialogs, _invoices, Step1VM, Step2VM, Step3VM, Step5VM, Step6VM);

            Step8VM = new Step8Attachments.AttachmentsStepViewModel(_dialogs, Step1VM);

            Step9VM = new Step9ProfitLoss.ProfitLossStepViewModel();
            Step9VM.Bind(
                Step5VM,
                Step4VM.DguVM,
                Step4VM.SguVM,
                () => Step7VM.InvoiceMainSummary.GrandTotalVatInclusive,
                Step4VM.LamVM,
                Step4VM.DguLamVM,
                Step6VM,
                () => Step7VM.InvoiceMainSummary.SubtotalBeforeVat,
                Step7VM.InvoiceMainSummary,
                () => Step4VM.CaptureSectionSnapshots(),
                reportDocumentContextProvider: () => new ReportDocumentContext
                {
                    // Read the live wizard bindings at the instant the user
                    // exports: a cached copy here would produce stale PI
                    // identity after a company/customer/project edit.
                    ProformaId = Step1VM.ProformaId,
                    CompanyName = Step1VM.CompanyName,
                    CustomerName = Step2VM.CustomerName,
                    ProjectName = FirstFilledProjectIdentity(),
                    IssueDate = Step1VM.IssueDate,
                    Status = Step1VM.ProformaStatus
                });

            // Step 9's Glass / Spec Comparison report references Step 6
            // U-insert / wastage rows; wire the "Go to Step 6" jump back
            // to the wizard's own step navigation.
            Step9VM.NavigateToStep6Requested = () => GoToStep(6);

            Step7VM.PrintableInvoice.ProfitLoss = Step9VM;
            Step7VM.PrintableInvoice.Step4Pricing = Step4VM;

            WizardSteps = new ObservableCollection<WizardStep>
            {
                new WizardStep { StepNumber = 1, Title = "Company details", Subtitle = "TRN, location and billing" },
                new WizardStep { StepNumber = 2, Title = "Client profile", Subtitle = "Customer, salesman and billing" },
                new WizardStep { StepNumber = 3, Title = "Project specs", Subtitle = "Reference numbers, PO and LPO" },
                new WizardStep { StepNumber = 4, Title = "Glass type & specs", Subtitle = "Estimators, materials & treatments" },
                new WizardStep { StepNumber = 5, Title = "Data grid", Subtitle = "Multi-spec row configurations" },
                new WizardStep { StepNumber = 6, Title = "Other charges", Subtitle = "Supplementary, prep, delivery" },
                new WizardStep { StepNumber = 7, Title = "Print preview", Subtitle = "Review, terms and ERP conversion" },
                new WizardStep { StepNumber = 8, Title = "Attachments", Subtitle = "Photos, drawings & approvals" },
                new WizardStep { StepNumber = 9, Title = "Profit & Loss", Subtitle = "Real cost, margin and profitability" }
            };

            CurrentStep = WizardSteps[0];
            _furthestStepReached = 1;
            foreach (var ws in WizardSteps)
                ws.IsCompleted = false;

            NextStepCommand = new RelayCommand(
                _ => GoToStep(CurrentStepNumber + 1),
                _ => CanGoNext);

            PreviousStepCommand = new RelayCommand(
                _ => GoToStep(CurrentStepNumber - 1),
                _ => CanGoBack);

            GoToStepCommand = new RelayCommand(param =>
            {
                if (param is WizardStep step)
                    GoToStep(step.StepNumber);
            });

            // NewInvoiceCommand: the historically "direct" entry point.
            // We now route through AskStartFromInquiryRequested first so
            // the user gets a 3-way choice (Inquiry / Skip / Cancel)
            // every time, no matter which button (sidebar "+", dashboard
            // quick action, or in-list "NEW INVOICE") triggered the flow.
            // The actual dialog lives in the host (MainViewModel) so the
            // VM stays MessageBox-free.
            NewInvoiceCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(StartNewInvoiceWithPromptAsync, "PI NewInvoice"));

            // NewFromInquiryCommand: the explicit "I want to start from
            // an Inquiry" path. Skips the prompt and goes straight to
            // the Inquiry module so the user can pick or create.
            NewFromInquiryCommand = new RelayCommand(
                _ => BeginNewFromInquiryFlow(),
                _ => NavigateToInquiryForNewPiRequested != null);

            BackToListCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(ReturnToListAsync, "PI BackToList"));

            SavedInvoicesListVM = new SavedInvoicesList.SavedInvoicesListViewModel();

            SavedInvoicesListVM.OnInvoiceLoadRequested = id =>
                AsyncUtility.SafeFireAndForget(() => LoadInvoiceIntoWizardAsync(id), $"PI Load {id}");

            SavedInvoicesListVM.OnInvoiceApproveRequested = id =>
            {
                _pendingApprovalInvoiceId = id;
                AsyncUtility.SafeFireAndForget(() => OpenInvoiceAtStepAndApproveAsync(id), $"PI Approve {id}");
            };

            Step8VM.AttachmentUploaded += OnStep8AttachmentUploaded;

            SavedInvoicesListVM.OnSnapshotRestoreRequested = json =>
                AsyncUtility.SafeFireAndForget(() => RestoreSnapshotIntoWizardAsync(json), "PI RestoreSnapshot");

            Step2VM.PropertyChanged += OnStep2PropertyChanged;
        }

        /// <summary>
        /// Step 9 report filenames and headers use the same concise project
        /// identity as the PI layouts: identifier name first, then reference,
        /// contract number and LPO only when an identifier is unavailable.
        /// </summary>
        private string FirstFilledProjectIdentity()
        {
            string[] candidates =
            {
                Step3VM?.ProjectIdentifierName ?? "",
                Step3VM?.ProjectReference ?? "",
                Step3VM?.ProjectContractNo ?? "",
                Step3VM?.LpoNumber ?? ""
            };

            return candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
        }

        // ══════════════════════════════════════════════════════════════
        //  IDISPOSABLE — event-handler cleanup (P2 fix, senior audit)
        //
        //  The two subscriptions in the ctor (Step8VM.AttachmentUploaded
        //  and Step2VM.PropertyChanged) don't leak in the currently
        //  shipping app because parent + children share the app
        //  lifetime, but they DO leak if a caller ever constructs a
        //  short-lived instance (multi-window / tests / preview panes).
        //  Dispose() unhooks them symmetrically; the child VMs stay
        //  alive on their own (they don't own the parent).
        //
        //  Safe to call more than once — the _disposed guard makes
        //  redundant Dispose() calls a no-op, matching the standard
        //  IDisposable contract.
        // ══════════════════════════════════════════════════════════════
        private bool _disposed;

        /// <summary>
        /// A finished optimization run republished itself. Best-effort per step:
        /// a partially-constructed child must never block the other from
        /// refreshing, which is the same rule Dispose follows.
        /// </summary>
        /// <summary>
        /// Engine host for the Step 5 QUICK / OPTIMIZE buttons.
        /// </summary>
        /// <remarks>
        /// Created on first use and kept: its constructor reads the Job Order
        /// list and the saved sessions from the database, so building one per
        /// click would repeat that for no benefit. It is never bound to a view —
        /// it exists only to run the engine, publish to the feed and persist the
        /// latest-run row, all of which the OPTIMIZATION tab's own buttons do
        /// through the same code paths.
        /// </remarks>
        /// <summary>
        /// Asks the shell to show the optimization module's SUMMARY tab for one
        /// document's latest run. The parameter is that document's key.
        /// </summary>
        public event System.Action<string?>? NavigateToOptimizationSummaryRequested;

        private ViewModels.Optimization.OptimizationViewModel? _optimizationHost;

        /// <summary>
        /// The proforma this wizard is editing. Read at call time rather than
        /// captured at wiring time: the operator may still be on Step 1 when
        /// these bindings are built, and the identity is published as they move
        /// through the wizard.
        /// </summary>
        private static string CurrentDocumentKey =>
            Services.ProformaInvoice.ActiveInvoiceContext.ProformaId ?? string.Empty;

        /// <summary>
        /// Runs a cut plan over the live Step 5 grid. Bound into
        /// <see cref="Step5DataGrid.DataGridViewModel"/> as its optimization
        /// runner; the grid supplies the snapshot, this supplies the engine.
        /// </summary>
        private (bool Started, string Message) RunOptimizationFromStep5(
            System.Collections.Generic.IReadOnlyList<Models.ProformaInvoice.SavedInvoice.SpecGroupSnapshot> specGroups,
            string stage)
        {
            var host = _optimizationHost ??= CreateOptimizationHost();

            // Stamp BEFORE the run: both the live publish and the persisted
            // latest-run row read DocumentKey when the run finishes.
            host.DocumentKey = CurrentDocumentKey;

            bool started = host.RunFromStep5(specGroups, stage, out string message);
            if (!started)
                Step5VM.ReportOptimizationProgress(false, 0, message ?? string.Empty);
            return (started, message);
        }

        /// <summary>
        /// Creates the one headless optimization host this quote drives its runs
        /// through, and wires the engine's own progress onto Step 5.
        /// </summary>
        /// <remarks>
        /// The constructor does database work (job list, saved sessions), so the
        /// host is created once and cached rather than per click.
        /// </remarks>
        private ViewModels.Optimization.OptimizationViewModel CreateOptimizationHost()
        {
            var host = new ViewModels.Optimization.OptimizationViewModel(isHeadlessHost: true);
            // RC-007 FIX: mark as headless host BEFORE LoadJobs async finishes,
            // so RebuildSelectedJobBreakdown does not wipe Pieces/Stocks that
            // RunFromStep5 just populated (race: ctor LoadJobs -> async
            // Rebuild -> Pieces.Clear() after RunFromStep5).
            host.IsHeadlessHost = true;
            host.PropertyChanged += OnOptimizationHostPropertyChanged;
            // Warm the Sheet Store snapshot that RunFromStep5 cross-checks sheet
            // sizes against (2026-09-17). Fire-and-forget and non-blocking: a run
            // started before it lands simply gets no cross-check note for that
            // click, which is better than making the operator wait on a database
            // read to press QUICK.
            Services.ProformaInvoice.SheetStorePricingService.PrimeSyncSnapshot();
            return host;
        }

        /// <summary>
        /// Mirrors the run state onto Step 5, so the operator sees the run
        /// happening on the screen they clicked instead of a frozen grid.
        /// </summary>
        /// <remarks>
        /// Only the three progress properties are mirrored; everything else the
        /// host raises (and it raises a lot) is ignored, because this quote never
        /// displays the host's own tab surface.
        /// </remarks>
        private void OnOptimizationHostPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ViewModels.Optimization.OptimizationViewModel.IsRunning):
                case nameof(ViewModels.Optimization.OptimizationViewModel.RunProgressPercent):
                case nameof(ViewModels.Optimization.OptimizationViewModel.RunProgressDetail):
                    break;
                default:
                    return;
            }

            var host = _optimizationHost;
            if (host == null) return;

            bool running = host.IsRunning;
            int percent = host.RunProgressPercent;
            string detail = host.RunProgressDetail;

            // The engine reports from inside its packing loop, which is not
            // necessarily the UI thread. PropertyChanged marshals itself, but
            // keep the hop explicit so nothing downstream can ever touch a
            // control off-thread.
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(
                    () => Step5VM.ReportOptimizationProgress(running, percent, detail)));
            }
            else
            {
                Step5VM.ReportOptimizationProgress(running, percent, detail);
            }
        }

        private void OnOptimizationRunPublished()
        {
            try { Step6VM?.RefreshOptimizationFeed(); }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Debug(
                    "[ProformaInvoiceViewModel] Step 6 optimization feed refresh failed: " + ex.Message);
            }

            // Only Step 9's sheet tab reads the run directly, and only when its
            // own switch is on. Otherwise a rebuild would produce identical rows
            // for nothing - the tab is expensive enough not to churn on every run.
            var sheets = Step9VM?.SheetProfitability;
            if (sheets == null) return;

            try
            {
                sheets.NotifyOptimizationSourceChanged();
                if (sheets.UseOptimizationSheetSource) sheets.RefreshAnalysis();
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Debug(
                    "[ProformaInvoiceViewModel] Step 9 sheet refresh failed: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                Services.Optimization.OptimizationResultFeed.RunPublished -=
                    OnOptimizationRunPublished;
            }
            catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ProformaInvoiceViewModel] Dispose: best-effort optimization feed unsubscribe failed: " + ex.Message); }

            try { Step2VM.PropertyChanged -= OnStep2PropertyChanged; }
            catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ProformaInvoiceViewModel] Dispose: best-effort teardown: " + ExceptionMessages.Deep(ex)); }
            try { Step4VM.PropertyChanged -= OnStep4PropertyChanged; }
            catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ProformaInvoiceViewModel] Dispose: best-effort Step4 teardown: " + ExceptionMessages.Deep(ex)); }

            try { Step8VM.AttachmentUploaded -= OnStep8AttachmentUploaded; }
            catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ProformaInvoiceViewModel] Dispose: best-effort teardown: " + ExceptionMessages.Deep(ex)); }

            // Follow-up audit pass (Aug 2026): SavedInvoicesListViewModel
            // is IDisposable and subscribes to static DocumentLifecycle
            // + SampleDataService events that outlive any short-lived
            // parent. Chain its disposal here so the whole PI tree
            // tears down cleanly on shutdown / test hosts. Best-effort:
            // a partially-constructed child never blocks teardown.
            try { Step6VM?.Dispose(); }
            catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ProformaInvoiceViewModel] Dispose: best-effort teardown: " + ExceptionMessages.Deep(ex)); }

            // Step 9 registers live-recalculation handlers on Step 4/5/6
            // sources through its SubscriptionManager — tear those down too.
            try { Step9VM?.Dispose(); }
            catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ProformaInvoiceViewModel] Dispose: best-effort teardown: " + ExceptionMessages.Deep(ex)); }

            try { SavedInvoicesListVM?.Dispose(); }
            catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ProformaInvoiceViewModel] Dispose: best-effort teardown: " + ExceptionMessages.Deep(ex)); }

            // PHASE 6 / finding 12 (2026-09-20): the headless Step 5
            // optimization host is a FULL OptimizationViewModel, and
            // CreateOptimizationHost subscribes to its PropertyChanged. That
            // delegate's target is THIS instance, so the host held a strong
            // reference to the whole Proforma Invoice tree — all nine step view
            // models — and nothing ever released it. The host also owns its own
            // DispatcherTimer and two cancellation sources, so a run in flight at
            // teardown rooted: timer → host → this VM → Steps 1-9.
            //
            // Unsubscribe first, then dispose. Order matters: disposing while
            // still subscribed leaves the handler reachable from the host's event
            // list until the host itself is collected.
            try
            {
                var optimizationHost = _optimizationHost;
                if (optimizationHost != null)
                {
                    optimizationHost.PropertyChanged -= OnOptimizationHostPropertyChanged;
                    optimizationHost.Dispose();
                }
                _optimizationHost = null;
            }
            catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ProformaInvoiceViewModel] Dispose: best-effort optimization host teardown: " + ExceptionMessages.Deep(ex)); }

            GC.SuppressFinalize(this);
        }


        private void OnStep4PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (CurrentStepNumber < 4 || Step4VM.IsRestoringPricingSnapshot)
                return;

            if (e.PropertyName is nameof(Step4PricingEstimator.PricingEstimatorViewModel.CurrentSpecFingerprint)
                or nameof(Step4PricingEstimator.PricingEstimatorViewModel.CalculationSectionCount)
                or null
                or "")
            {
                Step5VM.EnsureSpecGroupCount(Step4VM.CalculationSectionCount);
                SyncStep5SelectionsFromStep4();
                Step6VM.RefreshWorkspace();
            }
            else if (e.PropertyName is nameof(Step4PricingEstimator.PricingEstimatorViewModel.EstimatedRatePerSqm))
            {
                SyncStep5BaseRatesFromStep4();
                Step6VM.RefreshWorkspace();
            }
        }

        /// <summary>
        /// Rate-only push: Step 4 pricing numbers (yield/margin/BasePrice)
        /// changed but the chosen glass type/spec did not. Only the quoted
        /// rate (BaseRate/ImportedPrice) is refreshed for Step-4-owned groups;
        /// glass type, spec name, pane colours and sheet sizes are left alone
        /// so a manual Step 5 glass-type choice is never cleared by a price
        /// edit, and HasManualStep5BaseRateOverride is honoured. This was the
        /// source of “glass type getting reset from the price section”:
        /// EstimatedRatePerSqm firing the full ApplyStep4PricingSnapshot.
        /// </summary>
        private void SyncStep5BaseRatesFromStep4()
        {
            var sections = Step4VM.CaptureSectionSnapshots();
            int count = Math.Min(sections.Count, Step5VM.SpecGroups.Count);

            for (int i = 0; i < count; i++)
            {
                var section = sections[i];
                var group = Step5VM.SpecGroups[i];

                if (string.IsNullOrWhiteSpace(section.GeneratedSpecName))
                    continue;

                bool hasNoStep5Spec = string.IsNullOrWhiteSpace(group.SelectedSpecName);
                bool isFreshBootstrap = hasNoStep5Spec
                    && !group.HasManualStep5SpecOverride
                    && !group.HasManualStep5GlassTypeOverride;

                bool selectedSpecMatchesStep4 =
                    string.Equals(group.SelectedSpecName,
                        section.GeneratedSpecName,
                        StringComparison.OrdinalIgnoreCase);

                bool specIsStep4Owned = isFreshBootstrap
                    || selectedSpecMatchesStep4
                    || (!group.HasManualStep5SpecOverride
                        && !string.IsNullOrWhiteSpace(group.LastSyncedStep4SpecName)
                        && string.Equals(group.SelectedSpecName,
                            group.LastSyncedStep4SpecName,
                            StringComparison.OrdinalIgnoreCase));

                bool markersConfirmCurrentStep4Selection =
                    !string.IsNullOrWhiteSpace(group.LastSyncedStep4SpecName)
                    && !string.IsNullOrWhiteSpace(group.LastSyncedStep4GlassType)
                    && string.Equals(group.LastSyncedStep4SpecName,
                        section.GeneratedSpecName,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(group.LastSyncedStep4GlassType,
                        section.SelectedGlassTypeCode,
                        StringComparison.OrdinalIgnoreCase);

                bool selectedGlassTypeMatchesStep4 =
                    string.Equals(group.SelectedGlassType,
                        section.SelectedGlassTypeCode,
                        StringComparison.OrdinalIgnoreCase);

                bool glassTypeIsStep4Owned = isFreshBootstrap
                    || selectedGlassTypeMatchesStep4
                    || (!group.HasManualStep5GlassTypeOverride
                        && string.IsNullOrWhiteSpace(group.SelectedGlassType))
                    || (!group.HasManualStep5GlassTypeOverride
                        && markersConfirmCurrentStep4Selection)
                    || (!group.HasManualStep5GlassTypeOverride
                        && !string.IsNullOrWhiteSpace(group.LastSyncedStep4GlassType)
                        && string.Equals(group.SelectedGlassType,
                            group.LastSyncedStep4GlassType,
                            StringComparison.OrdinalIgnoreCase));

                if (!specIsStep4Owned || !glassTypeIsStep4Owned)
                    continue;

                group.ApplyStep4RateSnapshot(section);
            }
        }

        private void OnStep2PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Step2ClientProfile.ClientProfileViewModel.SelectedClientName))
                return;
            Step3VM.SetClientFilter(Step2VM.SelectedClientName);
        }

        private void SyncProjectFilterFromCurrentClient()
        {
            Step2VM.AdoptExternalCustomerSelection(Step2VM.CustomerName);
            Step3VM.SetClientFilter(Step2VM.CustomerName);
        }

        /// <summary>
        /// Transfers Step 4 sections into their corresponding Step 5 groups
        /// only while a group still mirrors Step 4. A blank Step 5 spec is a
        /// bootstrap group even when its UI default type is SGU; treating that
        /// default as a manual choice was the source of DGU-name/SGU-type pairs.
        /// </summary>
        private void SyncStep5SelectionsFromStep4()
        {
            var sections = Step4VM.CaptureSectionSnapshots();
            int count = Math.Min(sections.Count, Step5VM.SpecGroups.Count);

            for (int i = 0; i < count; i++)
            {
                var section = sections[i];
                var group = Step5VM.SpecGroups[i];

                if (string.IsNullOrWhiteSpace(section.GeneratedSpecName))
                    continue;

                bool hasNoStep5Spec = string.IsNullOrWhiteSpace(group.SelectedSpecName);
                bool isFreshBootstrap = hasNoStep5Spec
                    && !group.HasManualStep5SpecOverride
                    && !group.HasManualStep5GlassTypeOverride;

                // A spec can be owned independently only until a user picks a
                // different name. Important: choosing the SAME generated spec
                // from the dropdown must not block metadata refresh. Step 5 can
                // set the manual flag while the selected name still equals the
                // Step 4 section; in that case pane/sheet/rate metadata remains
                // Step-4-owned and must be refreshed.
                bool selectedSpecMatchesStep4 =
                    string.Equals(group.SelectedSpecName,
                        section.GeneratedSpecName,
                        StringComparison.OrdinalIgnoreCase);

                bool specIsStep4Owned = isFreshBootstrap
                    || selectedSpecMatchesStep4
                    || (!group.HasManualStep5SpecOverride
                        && !string.IsNullOrWhiteSpace(group.LastSyncedStep4SpecName)
                        && string.Equals(group.SelectedSpecName,
                            group.LastSyncedStep4SpecName,
                            StringComparison.OrdinalIgnoreCase));

                // A matching marker pair also identifies a failed legacy
                // bootstrap handoff: the old coordinator wrote the incoming
                // DGU spec marker even when the fresh group's default SGU type
                // was left unchanged. New manual type/spec edits clear these
                // markers in SpecGroup, so this recovery path does not reclaim
                // an operator-owned selection.
                bool markersConfirmCurrentStep4Selection =
                    !string.IsNullOrWhiteSpace(group.LastSyncedStep4SpecName)
                    && !string.IsNullOrWhiteSpace(group.LastSyncedStep4GlassType)
                    && string.Equals(group.LastSyncedStep4SpecName,
                        section.GeneratedSpecName,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(group.LastSyncedStep4GlassType,
                        section.SelectedGlassTypeCode,
                        StringComparison.OrdinalIgnoreCase);

                // Fresh groups initialize their dropdown to SGU before Step 4
                // is consulted. With no selected spec that is a framework
                // default, not a user-owned glass type, so it must accept an
                // incoming DGU/LAM/DGU+LAM type.
                bool selectedGlassTypeMatchesStep4 =
                    string.Equals(group.SelectedGlassType,
                        section.SelectedGlassTypeCode,
                        StringComparison.OrdinalIgnoreCase);

                bool glassTypeIsStep4Owned = isFreshBootstrap
                    || selectedGlassTypeMatchesStep4
                    || (!group.HasManualStep5GlassTypeOverride
                        && string.IsNullOrWhiteSpace(group.SelectedGlassType))
                    || (!group.HasManualStep5GlassTypeOverride
                        && markersConfirmCurrentStep4Selection)
                    || (!group.HasManualStep5GlassTypeOverride
                        && !string.IsNullOrWhiteSpace(group.LastSyncedStep4GlassType)
                        && string.Equals(group.SelectedGlassType,
                            group.LastSyncedStep4GlassType,
                            StringComparison.OrdinalIgnoreCase));

                // Type, spec, colours, sheets and rate are one coherent Step 4
                // selection. Do not update only one part of a manually changed
                // group: a partial update can recreate the stale type/spec
                // pairing this coordinator exists to prevent.
                if (!specIsStep4Owned || !glassTypeIsStep4Owned)
                    continue;

                // Apply in the safe order (type first, spec second) and copy
                // per-section sheet/U-insert metadata directly. This does not
                // depend on the asynchronous Step 4 catalog/registry write.
                // The handoff also records provenance and updates the imported
                // reference only when its generated spec actually changes.
                group.ApplyStep4PricingSnapshot(section);
            }
        }

        /// <summary>
        /// Runs the Step 4 → Step 5 boundary work before downstream views or
        /// the silent draft take a snapshot. It is shared by Continue and a
        /// direct wizard-tab jump that crosses the same boundary.
        /// </summary>
        private void PrepareStep5FromStep4()
        {
            Step5VM.EnsureSpecGroupCount(Step4VM.CalculationSectionCount);
            SyncStep5SelectionsFromStep4();
            Step5VM.EnsureDefaultWorkspace();

            // Catalogue persistence is deliberately non-blocking; Step 5 is
            // already correct because it received the direct snapshot above.
            AsyncUtility.SafeFireAndForget(
                () => Step4VM.AutoSaveCurrentSpecAsync(),
                "PI Step4 AutoSaveSpec");
        }

        private void GoToStep(int stepNumber)
        {
            var step = WizardSteps.FirstOrDefault(s => s.StepNumber == stepNumber);
            if (step == null) return;

            int previousStepNumber = CurrentStepNumber;
            bool crossesStep4Boundary = previousStepNumber <= 4 && stepNumber >= 5;
            bool leavesStep4 = previousStepNumber == 4 && stepNumber != 4;

            CurrentStep = step;
            _furthestStepReached = Math.Max(_furthestStepReached, stepNumber);

            // Publish the invoice's identity for the Specifications Center
            // usage history. Done on every navigation so that by the time
            // Step 4 saves a spec, the PI / customer / project are current
            // no matter which order the user filled them in.
            //
            // Ambient rather than injected: Step 4 has no reference to
            // Steps 1-3, and adding constructor arguments to a DI-resolved
            // view model is precisely the change that broke construction
            // during the ERR-633EEB work.
            try
            {
                Services.ProformaInvoice.ActiveInvoiceContext.Set(
                    Step1VM?.ProformaId,
                    Step2VM?.CustomerName,
                    Step3VM?.ProjectIdentifierName);
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Debug(
                    $"[PI.GoToStep] invoice context publish failed: {ExceptionMessages.Deep(ex)}");
            }
            foreach (var ws in WizardSteps)
                ws.IsCompleted = ws.StepNumber < _furthestStepReached;

            if (stepNumber == 4)
            {
                if (Step5VM.TotalSpecGroups > Step4VM.CalculationSectionCount)
                    Step4VM.EnsureCalculationSectionCount(Step5VM.TotalSpecGroups);

                // RC-016 (2026-09-20): this refresh is destructive — on a Sheet
                // Store miss it clears the size dropdown and zeroes the pane
                // base prices (see PricingEstimatorViewModel.HasPricedStep4Data).
                // It used to fire on EVERY navigation into Step 4 for any
                // invoice that was not loaded from a snapshot, so switching
                // stations and coming back blanked a Step 4 the operator had
                // already priced; the 4→5 handoff then carried the zeroed rate
                // into Step 5 and the silent draft save persisted it.
                //
                // Still refresh a Step 4 that has produced no rate (nothing to
                // lose) so a fresh invoice picks up current stock. A priced one
                // is left alone; genuine stock changes still reach it through
                // SheetStoreVM.StockLedgerChanged, and restores through
                // RestoreFromSnapshot.
                if (!Step4VM.HasRestoredSnapshotState && !Step4VM.HasPricedStep4Data)
                    Step4VM.RefreshSheetStoreData();
            }

            // Continue, Back, and direct tab jumps flush the same snapshot before saving.
            // This closes the historical hole where jumping from Step 4 to a
            // later station skipped the Step 5 synchronization altogether.
            if (crossesStep4Boundary || leavesStep4)
                PrepareStep5FromStep4();

            // A downstream jump still needs the refreshed Step 6 workspace;
            // otherwise Step 7/9 can see pre-handoff wastage suggestions.
            if (stepNumber == 6 || (crossesStep4Boundary && stepNumber > 6))
                Step6VM.RefreshWorkspace();
            if (stepNumber == 8)
                AsyncUtility.SafeFireAndForget(() => Step8VM.LoadAsync(), "PI Step8 Load");
            if (stepNumber == 9)
                AsyncUtility.SafeFireAndForget(() => Step9VM.RecalculateAsync(), "PI Step9 Recalc");

            // Build the silent draft only AFTER the Step 4 → Step 5 handoff.
            // SilentDraftSaveAsync constructs its entity before its first await,
            // so scheduling it earlier persisted the stale pre-Continue group
            // selection even though the visible Step 5 UI later looked right.
            Step7VM.PrintableInvoice.FurthestStepReached = _furthestStepReached;
            AsyncUtility.SafeFireAndForget(
                () => Step7VM.PrintableInvoice.SilentDraftSaveAsync(),
                $"PI AutoSave Step{stepNumber}");
        }

        private async Task StartNewInvoiceAsync()
        {
            Step1VM.InquiryId = null;
            Step1VM.CompanyName = "";
            Step1VM.CompanyTrn = "";
            Step1VM.CompanyLocation = "";
            Step1VM.IssueDate = DateTime.Now;
            Step1VM.ValidUntil = DateTime.Now.AddDays(7);
            Step1VM.ProformaStatus = "Draft";
            await Step1VM.GenerateUniqueProformaIdAsync();

            Step2VM.CustomerName = "";
            Step2VM.CustomerTrn = "";
            Step2VM.CustomerReferenceCode = "";
            Step2VM.SalesRepresentative = "";
            Step2VM.DocumentPreparedBy = Services.Audit.AuditService.CurrentUser?.DisplayName
                ?? Services.Audit.AuditService.CurrentUsername;
            Step2VM.BillingAddress = "";
            Step2VM.AttentionExecutiveName = "";
            Step2VM.ClientContactPhone = "";
            Step2VM.ClearExternalCustomerSelection();

            Step3VM.SetClientFilter("");
            Step3VM.ProjectIdentifierName = "";
            Step3VM.ProjectContractNo = "";
            Step3VM.ProjectReference = "";
            Step3VM.SiteDeliveryLocation = "";
            Step3VM.LpoNumber = "";

            Services.ProformaInvoice.SpecSheetRegistry.Clear();
            Step4VM.ResetForNewInvoice();
            Step5VM.ResetForNewInvoice();
            Step6VM.ResetForNewInvoice();

            await Task.WhenAll(
                Step1VM.LoadCardsAsync(),
                Step2VM.LoadCardsAsync(),
                Step2VM.LoadSalesmanOptionsAsync(),
                Step3VM.LoadCardsAsync()
            );

            await Step8VM.LoadAsync();
            CurrentStep = WizardSteps[0];
            _furthestStepReached = 1;
            foreach (var ws in WizardSteps)
                ws.IsCompleted = false;
            IsListView = false;
        }

        private async Task ReturnToListAsync()
        {
            if (CurrentStepNumber == 4) PrepareStep5FromStep4();
            // Sync the current wizard progress into the printable VM
            // before the silent draft save so the snapshot captures
            // the correct furthest-step-reached value.
            Step7VM.PrintableInvoice.FurthestStepReached = _furthestStepReached;
            await Step7VM.PrintableInvoice.SilentDraftSaveAsync();
            _pendingApprovalInvoiceId = null;
            IsListView = true;
            await SavedInvoicesListVM.LoadInvoicesAsync();
        }

        private async Task LoadInvoiceIntoWizardAsync(int invoiceId)
        {
            if (_invoices == null) return;
            var entity = await _invoices.GetByIdAsync(invoiceId);
            if (entity == null)
            {
                _dialogs.Show(LocalizationService.Instance.T("Msg.SelectedInvoiceCouldNotBe"), LocalizationService.Instance.T("Msg.CapLoadFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var snapshot = ProformaInvoiceMapper.DeserializeSnapshot(entity);
            Step7VM.InvoiceMainSummary.BeginBatchUpdate();
            try
            {
                ProformaInvoiceMapper.RestoreWizardFromSnapshot(
                    snapshot, Step1VM, Step2VM, Step3VM, Step5VM, Step6VM,
                    Step7VM.InvoiceMainSummary, Step7VM.Terms, Step7VM.PrintableInvoice);
                SyncProjectFilterFromCurrentClient();
            }
            finally { Step7VM.InvoiceMainSummary.EndBatchUpdate(); }

            await Task.WhenAll(
                Step1VM.LoadCardsAsync(),
                Step2VM.LoadCardsAsync(),
                Step2VM.LoadSalesmanOptionsAsync(),
                Step3VM.LoadCardsAsync()
            );
            await Step8VM.LoadAsync();

            // Step 9 watches Step 4/5/6 sources for live recalculation.
            // After a restore those sources were rebuilt, so the Step 9
            // wiring must be re-subscribed (old handlers detached first) to
            // guarantee the target KPI follows the loaded invoice.
            try { Step9VM.RebindToCurrentSources(); }
            catch (System.Exception ex) { AppLogService.Debug($"[ProformaInvoiceViewModel] Step9 rebind: {ExceptionMessages.Deep(ex)}"); }

            // Restore step-completion progress from the snapshot so the
            // operator sees the same completed-step indicators they had
            // when the invoice was last saved.
            int restored = Step7VM.PrintableInvoice.FurthestStepReached;
            _furthestStepReached = Math.Max(1, restored);
            _loadedInvoiceId = invoiceId;   // for the standalone Step 8 approve action
            CurrentStep = WizardSteps[0];
            foreach (var ws in WizardSteps)
                ws.IsCompleted = ws.StepNumber < _furthestStepReached;
            IsListView = false;
        }

        /// <summary>
        /// 2026-09-10 unification: opens the invoice at Step 8 and runs the
        /// SAME single "APPROVE INVOICE" command the user sees on the step —
        /// one picker for images and PDFs (type auto-detected). Replaces the
        /// old flow that smuggled a pre-answered uploadPdf bool and fired
        /// AddImage/AddPdf from the dispatcher after navigation.
        /// </summary>
        private async Task OpenInvoiceAtStepAndApproveAsync(int invoiceId)
        {
            await LoadInvoiceIntoWizardAsync(invoiceId);
            GoToStep(8);
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                Step8VM.ApproveInvoiceCommand.Execute(null)));
        }

        private void OnStep8AttachmentUploaded()
        {
            // Approve started from the list uses the pending id; a standalone
            // APPROVE INVOICE pressed directly in Step 8 falls back to the
            // invoice currently loaded in the wizard (same pipeline either way).
            int? id = _pendingApprovalInvoiceId ?? _loadedInvoiceId;
            _pendingApprovalInvoiceId = null;

            if (id == null)
            {
                // RC-017 (2026-09-20): _loadedInvoiceId is assigned in EXACTLY
                // ONE place — LoadInvoiceIntoWizardAsync, i.e. only when the
                // invoice was opened from the list. An invoice CREATED in the
                // wizard and saved on Step 7 never set it, and Step 7's save
                // path (PrintableInvoiceViewModel.ExecuteSaveInvoiceAsync)
                // never hands the new row id back to this VM. So for a newly
                // created quote the standalone APPROVE INVOICE button attached
                // the signed document and then fell straight into the old
                // `if (id == null) return;` — CompleteApprovalAsync never ran,
                // the PI was never marked Approved, and no Job Order was ever
                // auto-created. That is the reported "Step 8 mark-as-approved
                // does not work like the Proforma list's approve", and it is
                // why the Optimization picker then had no released job to show.
                //
                // The wizard always knows the document number — Step 7 refuses
                // to save without one — so resolve the saved row from it and
                // adopt it as the loaded id. Same single approval pipeline
                // (CompleteApprovalAsync → status transition → auto-convert /
                // release the Job Order), just reached honestly.
                if (_invoices == null) return;

                string proformaId = (Step1VM?.ProformaId ?? string.Empty).Trim();
                if (proformaId.Length == 0)
                {
                    AppLogService.Warning(
                        "[PI Step8] APPROVE INVOICE ran with no pending/loaded invoice id and no " +
                        "ProformaId — the attachment was stored but the invoice was NOT approved " +
                        "and no Job Order was created.");
                    return;
                }

                AsyncUtility.SafeFireAndForget(async () =>
                {
                    var saved = await _invoices.GetByProformaIdAsync(proformaId);
                    if (saved == null)
                    {
                        AppLogService.Warning(
                            $"[PI Step8] APPROVE INVOICE could not find a saved invoice for " +
                            $"{proformaId} — save it on Step 7 first. The attachment was stored " +
                            "but the invoice was NOT approved and no Job Order was created.");
                        return;
                    }

                    _loadedInvoiceId = saved.Id;
                    await SavedInvoicesListVM.CompleteApprovalAsync(saved.Id);
                }, $"PI CompleteApproval byProformaId {proformaId}");
                return;
            }

            AsyncUtility.SafeFireAndForget(() => SavedInvoicesListVM.CompleteApprovalAsync(id.Value), $"PI CompleteApproval {id}");
        }

        private async Task RestoreSnapshotIntoWizardAsync(string snapshotJson)
        {
            var wrapper = new Models.ProformaInvoice.SavedInvoice.SavedProformaInvoice
            {
                FullSnapshotJson = snapshotJson
            };
            var snapshot = ProformaInvoiceMapper.DeserializeSnapshot(wrapper);
            if (snapshot == null)
            {
                _dialogs.Show(LocalizationService.Instance.T("Msg.VersionCouldNotBeRead"), LocalizationService.Instance.T("Msg.CapRestoreVersion"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Step7VM.InvoiceMainSummary.BeginBatchUpdate();
            try
            {
                ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository.ProformaInvoiceMapper.RestoreWizardFromSnapshot(
                    snapshot, Step1VM, Step2VM, Step3VM, Step5VM, Step6VM,
                    Step7VM.InvoiceMainSummary, Step7VM.Terms, Step7VM.PrintableInvoice);
                SyncProjectFilterFromCurrentClient();
            }
            finally { Step7VM.InvoiceMainSummary.EndBatchUpdate(); }
            await Step8VM.LoadAsync();

            int restored = Step7VM.PrintableInvoice.FurthestStepReached;
            _furthestStepReached = Math.Max(1, restored);
            CurrentStep = WizardSteps[0];
            foreach (var ws in WizardSteps)
                ws.IsCompleted = ws.StepNumber < _furthestStepReached;
            IsListView = false;
        }

        public void TriggerNewInvoice() =>
            // Same routed flow as the in-VM NewInvoiceCommand: the
            // host (MainViewModel) wires the prompt so every entry
            // point — sidebar "+", dashboard quick action, in-list
            // button, agent trigger — gives the user the same
            // "Inquiry / Skip / Cancel" choice.
            AsyncUtility.SafeFireAndForget(StartNewInvoiceWithPromptAsync, "PI TriggerNewInvoice");

        /// <summary>
        /// Public "navigate to inquiry so the user can pick" entry.
        /// Used by the explicit "New from Inquiry" command and by
        /// the prompt-driven flow. Sets the awaiting flag so the
        /// PI list header can show a "waiting for inquiry pick"
        /// status. The host wires the actual Navigate call.
        /// </summary>
        public void BeginNewFromInquiryFlow()
        {
            if (NavigateToInquiryForNewPiRequested == null)
            {
                // No host — fall back to a direct blank create. The
                // command's CanExecute guards against this in the
                // common case, but the agent / test paths can still
                // reach here.
                AsyncUtility.SafeFireAndForget(StartNewInvoiceAsync, "PI NewFromInquiry fallback");
                return;
            }
            IsAwaitingInquirySelection = true;
            NavigateToInquiryForNewPiRequested();
        }

        /// <summary>
        /// End the "awaiting inquiry" round-trip. Called by the
        /// inquiry module after the user picks a row (or the chain
        /// reaches StartFromInquiryAsync). Resets the flag and
        /// switches the shell back to the Proforma view.
        /// </summary>
        public void CompleteInquirySelectionForNewPi()
        {
            IsAwaitingInquirySelection = false;
        }

        private async Task StartNewInvoiceWithPromptAsync()
        {
            NewProformaStartChoice choice = NewProformaStartChoice.Skip;
            try
            {
                if (AskStartFromInquiryRequested != null)
                    choice = await AskStartFromInquiryRequested();
            }
            catch (Exception ex)
            {
                // The host dialog threw — log and default to Skip so
                // the user is never left without a path. The audit
                // log captures the host-side exception.
                AppLogService.Warning($"PI NewInvoice prompt failed: {ExceptionMessages.Deep(ex)}",
                    new LogContext { Module = "Proforma", Class = nameof(ProformaInvoiceViewModel), Operation = "NewInvoicePrompt" });
                choice = NewProformaStartChoice.Skip;
            }
            switch (choice)
            {
                case NewProformaStartChoice.FromInquiry:
                    BeginNewFromInquiryFlow();
                    break;
                case NewProformaStartChoice.Skip:
                    await StartNewInvoiceAsync();
                    break;
                // Cancelled: do nothing.
            }
        }

        /// <summary>
        /// Pre-fills the wizard from an Inquiry the operator picked.
        ///
        /// Senior-audit fix (Aug 2026):
        ///   • Also carries CustomerLocation → BillingAddress and
        ///     ProjectDescription → ProjectReference (fuller text),
        ///     falling back to InquiryNumber if the description is
        ///     empty. Previously only 5 of the 8 relevant fields
        ///     were copied.
        ///   • Bug #7 protection — refuses to start a new PI when
        ///     the inquiry already has a linked PI. The Inquiry list
        ///     already guards this in the UI, but the VM must guard
        ///     too because other entry points (Agent, tests, dashboard
        ///     quick-actions) reach here without going through the
        ///     list. Once InquiryLinkSync populates ProformaNumber
        ///     correctly, this check is meaningful.
        /// </summary>
        public async Task StartFromInquiryAsync(Models.Inquiry.InquiryRecord inquiry)
        {
            if (inquiry == null) throw new ArgumentNullException(nameof(inquiry));
            // Allow PI creation from Prepared or Confirmed inquiries.
            // The inquiry status is automatically advanced to "Sent"
            // when the PI is created (via InquiryLinkSync), and to
            // "Confirmed" when the PI is approved. This removes the
            // manual confirmation step that was blocking the workflow.
            if (!string.Equals(inquiry.Status, Models.Inquiry.InquiryStatuses.Confirmed, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(inquiry.Status, Models.Inquiry.InquiryStatuses.Prepared, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only a Prepared or Confirmed Inquiry is eligible for Proforma creation.");
            if (inquiry.ProformaInvoiceId.HasValue ||
                !string.IsNullOrWhiteSpace(inquiry.ProformaNumber))
            {
                throw new InvalidOperationException(
                    $"Inquiry {inquiry.InquiryNumber} already has a Proforma Invoice " +
                    $"({inquiry.ProformaNumber}). Delete the existing PI first or " +
                    $"open it via Load Invoice.");
            }

            // Clear the "awaiting inquiry pick" flag so the banner does
            // not linger while the wizard fields populate below. The
            // host performs the actual navigation after this method
            // returns.
            IsAwaitingInquirySelection = false;
            await StartNewInvoiceAsync();

            // Company / commercial context
            Step1VM.InquiryId = inquiry.Id;

            // Client identity — Customer Name is the DB-required field
            // and also the trigger for the Step 3 project filter (see
            // OnStep2PropertyChanged). Setting CustomerName alone does
            // NOT wake the SelectedClientName property; the wizard's
            // saved-clients list normally does that when the operator
            // clicks a card. We therefore also assign BillingAddress
            // from the inquiry so the operator can see a starting
            // point in Step 2 without re-typing.
            Step2VM.CustomerName = inquiry.CustomerName;
            if (!string.IsNullOrWhiteSpace(inquiry.CustomerLocation))
                Step2VM.BillingAddress = inquiry.CustomerLocation;
            Step2VM.SalesRepresentative = inquiry.SalesmanName;
            Step2VM.DocumentPreparedBy = inquiry.PreparedByName;
            Step2VM.AdoptExternalCustomerSelection(inquiry.CustomerName);
            Step3VM.SetClientFilter(inquiry.CustomerName);

            // Project reference. ProjectDescription is the fuller
            // free-text field on the inquiry; when empty we fall back
            // to InquiryNumber so the PI always carries something
            // linking back to the origin.
            Step3VM.ProjectIdentifierName = inquiry.ProjectName;
            Step3VM.ProjectReference =
                !string.IsNullOrWhiteSpace(inquiry.ProjectDescription)
                    ? inquiry.ProjectDescription
                    : inquiry.InquiryNumber;

            // ── PHASE 2: import the inquiry's scope of supply (BOQ) into
            // the data grid. Glass type is written BEFORE the spec name —
            // the grid's setters clear a type-mismatched name, the same
            // order the Step 4 handoff itself uses.
            if (inquiry.LineItems != null && inquiry.LineItems.Count > 0)
            {
                ImportInquiryBoqIntoDataGrid(inquiry.LineItems);

                // PHASE 5: conversion is part of the audit trail, carrying
                // the item snapshot it carried in (fire-and-forget).
                var converted = Services.ProformaInvoice.InquiryBoqConversion
                    .ToImportRows(inquiry.LineItems);
                if (converted.Count > 0)
                {
                    var sb = new System.Text.StringBuilder(
                        $"Inquiry {inquiry.InquiryNumber} opened in the PI wizard — {converted.Count} BOQ line(s): ");
                    foreach (var r in converted.Take(12))
                        sb.Append($"{r.SpecName} {r.WidthMm}x{r.HeightMm}x{r.Qty}; ");
                    if (converted.Count > 12)
                        sb.Append($"… +{converted.Count - 12} more");
                    Services.Audit.AuditService.Log(
                        Services.Audit.AuditSections.Inquiry,
                        "Inquiry converted to PI",
                        sb.ToString(),
                        "Inquiry",
                        inquiry.InquiryNumber ?? "");
                }
            }
        }

        /// <summary>
        /// PHASE 2 — maps inquiry BOQ rows onto Step 5 spec groups.
        /// Only rows carrying spec name + dimensions + quantity are
        /// imported; partial rows stay on the inquiry as notes. Imported
        /// groups are written OUTSIDE the Step 4 sync guard, so the grid
        /// marks them manually-owned — the Step 4 → 5 sync never stomps
        /// a BOQ-imported spec (that is the whole point: these specs came
        /// from the customer's request, not from a Step 4 panel).
        ///
        /// PHASE 4: the conversion DECISIONS (filter / order / normalise)
        /// live in Services.ProformaInvoice.InquiryBoqConversion — pure,
        /// regression-tested. This method is the grid-touching half only.
        /// </summary>
        private void ImportInquiryBoqIntoDataGrid(
            System.Collections.Generic.IReadOnlyList<Models.Inquiry.InquiryLineItem> items)
        {
            var rows = Services.ProformaInvoice.InquiryBoqConversion.ToImportRows(items);
            if (rows.Count == 0) return;

            Step5VM.EnsureSpecGroupCount(rows.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var group = Step5VM.SpecGroups[i];

                // Replace the placeholder row(s) so the group's only content
                // is the BOQ line — nothing from the blank workspace remains.
                if (!string.IsNullOrEmpty(row.GlassType))
                    group.SelectedGlassType = row.GlassType;
                group.SelectedSpecName = row.SpecName;
                group.OuterGlassColor = row.OuterGlassColor;
                group.InnerGlassColor = row.InnerGlassColor;

                if (row.HasRate)
                {
                    group.ImportedPrice = row.RatePerSqm;
                    group.BaseRate = row.RatePerSqm;
                }

                group.ItemRows.Clear();
                group.ItemRows.Add(new ViewModels.ProformaInvoice.Step5DataGrid.GlassItemRow
                {
                    // W1/H1 then Qty then Rate: every setter recomputes
                    // derived sqm/value, so dependencies must land first.
                    W1 = row.WidthMm,
                    H1 = row.HeightMm,
                    Qty = row.Qty,
                    Rate = row.RatePerSqm
                });
            }
        }

        public async Task OpenSavedInvoiceAsync(int invoiceId)
        {
            try { await LoadInvoiceIntoWizardAsync(invoiceId); }
            catch (Exception ex)
            {
                AppLogService.Warning($"Could not open proforma invoice {invoiceId}: {ExceptionMessages.Deep(ex)}",
                    new LogContext { Module = "Proforma", Class = nameof(ProformaInvoiceViewModel), Operation = "OpenFromReports" });
            }
        }
    }
}
