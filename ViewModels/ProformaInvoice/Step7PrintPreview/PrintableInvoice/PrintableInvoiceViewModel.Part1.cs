// ═══════════════════════════════════════════════════════════════════════
// FILE: PrintableInvoiceViewModel.Part1.cs (Phase 3.8 split — member census verified)
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Csv;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Services.Export.Excel;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.Services.ProformaInvoice.Export;
using ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository;
using ProGlassAutomation.Services.Sharing;
using ProGlassAutomation.Services.MasterData.Repository;
using ProGlassAutomation.ViewModels;
using ProGlassAutomation.ViewModels.ProformaInvoice.SavedInvoicesList;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step1CompanySetup;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step2ClientProfile;   // FIXED: was Step2ProjectSpecs
using ProGlassAutomation.ViewModels.ProformaInvoice.Step3ProjectSpecs;    // FIXED: was Step3ClientProfile
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.InvoiceMainSummary;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;   // PHASE B
using ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.TermsAndConditions;
using ProGlassAutomation.Views.ProformaInvoice.SavedInvoicesList;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using ProGlassAutomation.Services.Localization;
using ProGlassAutomation.Services.Dialogs;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.PrintableInvoice
{
        public partial class PrintableInvoiceViewModel : ViewModelBase
    {
            // FIXED: property types now use new namespaces
        public CompanySetupViewModel Company { get; }
        public ClientProfileViewModel Client { get; }  // was ProjectSpecsViewModel
        public ProjectSpecsViewModel Project { get; }  // was ClientProfileViewModel
        public DataGridViewModel DataGrid { get; }
        public OtherChargesViewModel OtherCharges { get; }
        public InvoiceMainSummaryViewModel MainSummary { get; }
        public TermsAndConditionsViewModel Terms { get; }

        // ══════════════════════════════════════════════════════════════════
        //  PHASE 7.16a — Prepared By fields
        // ══════════════════════════════════════════════════════════════════

        private string _preparedByNameOverride;
        public string PreparedByName
        {
            get => string.IsNullOrWhiteSpace(_preparedByNameOverride)
                ? (Client?.DocumentPreparedBy ?? "")
                : _preparedByNameOverride;
            set
            {
                if (_preparedByNameOverride == value) return;
                _preparedByNameOverride = value;
                OnPropertyChanged();
            }
        }

        private string _preparedByDept = "";
        public string PreparedByDept
        {
            get => _preparedByDept;
            set
            {
                if (_preparedByDept == value) return;
                _preparedByDept = value;
                OnPropertyChanged();
            }
        }

        public string PreparedByStatus
        {
            get => string.IsNullOrWhiteSpace(Company?.ProformaStatus)
                ? "Draft"
                : Company.ProformaStatus;
            set
            {
                string next = string.IsNullOrWhiteSpace(value)
                    ? "Draft"
                    : value.Trim();

                if (string.Equals(Company?.ProformaStatus, next,
                        StringComparison.OrdinalIgnoreCase))
                    return;

                if (Company != null)
                    Company.ProformaStatus = next;

                OnPropertyChanged();
            }
        }

        private string _termsText =
            "Standard warranties apply to manufactured glass products upon POD signature clearance.";
        public string TermsText
        {
            get => _termsText;
            set
            {
                if (_termsText == value) return;
                _termsText = value;
                OnPropertyChanged();
            }
        }

        private string _footerDisclaimer =
            "Confidential Document generated automatically by ProGlass Automation ERP platform.";
        public string FooterDisclaimer
        {
            get => _footerDisclaimer;
            set
            {
                if (_footerDisclaimer == value) return;
                _footerDisclaimer = value;
                OnPropertyChanged();
            }
        }

        // ── Commands ──────────────────────────────────────────────────────

        public ICommand PrintCommand { get; }
        public ICommand ExportPdfCommand { get; }
        public ICommand SaveInvoiceCommand { get; }
        public ICommand LoadInvoiceCommand { get; }
        public ICommand SendEmailCommand { get; }
        public ICommand ExportExcelCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ShareCommand { get; }

        public FrameworkElement PrintTarget { get; set; }

        // ══════════════════════════════════════════════════════════════════
        //  PHASE B — Step 9 Profit & Loss link
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Step 9 Profit &amp; Loss view-model, assigned by the wizard shell
        /// after both step VMs exist. Optional and nullable: the printable
        /// invoice renders exactly as before when it is null — the mapper
        /// simply persists an empty P&amp;L block in that case.
        /// </summary>
        public ProfitLossStepViewModel? ProfitLoss
        {
            get => _profitLoss;
            set
            {
                if (ReferenceEquals(_profitLoss, value)) return;
                _profitLoss = value;
                OnPropertyChanged();
            }
        }
        private ProfitLossStepViewModel? _profitLoss;

        /// <summary>
        /// Live PDF-layout switcher. Assigned by the wizard shell so the
        /// Step 7 toolbar and the paper share ONE instance — two instances
        /// would each subscribe to DocumentStyle and could fight over the
        /// saved setting. The paper binds <c>PrintLayout.Kind</c> to rebuild
        /// letterhead, section, totals and signature chrome, so what the
        /// operator sees here is what the exporter renders.
        /// </summary>
        public Shared.PrintLayoutViewModel? PrintLayout
        {
            get => _printLayout;
            set
            {
                if (ReferenceEquals(_printLayout, value)) return;
                _printLayout = value;
                OnPropertyChanged();
            }
        }
        private Shared.PrintLayoutViewModel? _printLayout;

        /// <summary>
        /// Optional link back to Step 4 so save/load can persist the active
        /// pricing-editor state without making the printable VM own Step 4.
        /// </summary>
        public PricingEstimatorViewModel? Step4Pricing { get; set; }

        /// <summary>
        /// Furthest wizard step the operator reached. Set by the wizard
        /// shell (ProformaInvoiceViewModel) before every save so the
        /// snapshot can persist step-completion progress. Zero means
        /// "not set" — the mapper treats it as step 1 on restore.
        /// </summary>
        public int FurthestStepReached { get; set; }

        /// <summary>
        /// CHAT 15: Parameter order corrected.
        /// Was: (company, ProjectSpecsViewModel project, ClientProfileViewModel client, ...)
        /// Now: (company, ClientProfileViewModel client, ProjectSpecsViewModel project, ...)
        /// Matches PrintPreviewViewModel which receives Step2VM=Client, Step3VM=Project.
        /// </summary>
                private readonly IDialogService _dialogs = null!;
        private readonly IProformaInvoiceRepository _invoices = null!;

public PrintableInvoiceViewModel(
            IDialogService? dialogs,
            IProformaInvoiceRepository? invoices,
            CompanySetupViewModel company,
            ClientProfileViewModel client,        // FIXED: was ProjectSpecsViewModel
            ProjectSpecsViewModel project,       // FIXED: was ClientProfileViewModel
            DataGridViewModel dataGrid,
            OtherChargesViewModel otherCharges,
            InvoiceMainSummaryViewModel mainSummary,
            TermsAndConditionsViewModel terms)
        {
            _dialogs = dialogs!;
            _invoices = invoices!;
            Company = company ?? throw new ArgumentNullException(nameof(company));
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Project = project ?? throw new ArgumentNullException(nameof(project));
            DataGrid = dataGrid ?? throw new ArgumentNullException(nameof(dataGrid));
            OtherCharges = otherCharges ?? throw new ArgumentNullException(nameof(otherCharges));
            MainSummary = mainSummary ?? throw new ArgumentNullException(nameof(mainSummary));
            Terms = terms ?? throw new ArgumentNullException(nameof(terms));

            // BUG-60: all handlers now wrapped safely
            PrintCommand = new RelayCommand(_ => ExecutePrint());
            ExportPdfCommand = new RelayCommand(_ => AsyncUtility.SafeFireAndForget(ExecuteExportPdfAsync, "PI Step7"));
            SaveInvoiceCommand = new RelayCommand(_ => AsyncUtility.SafeFireAndForget(async () => { await ExecuteSaveInvoiceAsync(); }, "PI Step7"));
            LoadInvoiceCommand = new RelayCommand(_ => AsyncUtility.SafeFireAndForget(ExecuteLoadInvoiceAsync, "PI Step7"));
            SendEmailCommand = new RelayCommand(_ => ShowComingSoon("Send Email", "Phase 7.20"));
            ExportExcelCommand = new RelayCommand(_ => AsyncUtility.SafeFireAndForget(ExecuteExportExcelAsync, "PI Step7"));
            ExportCsvCommand = new RelayCommand(_ => AsyncUtility.SafeFireAndForget(ExecuteExportCsvAsync, "PI Step7"));
            ShareCommand = new RelayCommand(_ => AsyncUtility.SafeFireAndForget(ExecuteShareAsync, "PI Step7 share"));

            Client.PropertyChanged += OnClientChanged;
            Company.PropertyChanged += OnCompanyChanged;
        }

        // ══════════════════════════════════════════════════════════════════
        //  BUG-60: Safe async fire wrapper
        // ══════════════════════════════════════════════════════════════════

        

        private void OnClientChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ClientProfileViewModel.DocumentPreparedBy)
                && string.IsNullOrWhiteSpace(_preparedByNameOverride))
            {
                OnPropertyChanged(nameof(PreparedByName));
            }
        }

        private void OnCompanyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CompanySetupViewModel.ProformaStatus))
                OnPropertyChanged(nameof(PreparedByStatus));
        }

        // ══════════════════════════════════════════════════════════════════
        //  PRINT — 2026-09-11: ONE renderer for every output.
        //
        //  The old path rebuilt the invoice a THIRD time in code-behind
        //  visuals with a hard-coded indigo palette, no logo and no theme —
        //  so the physical printout disagreed with both the preview and the
        //  PDF. Printing now goes through the exact same QuestPDF layout
        //  (ProformaInvoicePdfLayout → Step 7 preview-parity design) that
        //  Export PDF uses. What you see is what prints, on paper too.
        // ══════════════════════════════════════════════════════════════════

        private void ExecutePrint()
        {
            try
            {
                if (!EnsureCustomerDocumentCalculationsValid("print")) return;

                var entity = ProformaInvoiceMapper.BuildEntityFromWizard(
                    Company, Client, Project, DataGrid, OtherCharges,
                    MainSummary, Terms, this);
                var snapshot = ProformaInvoiceMapper.DeserializeSnapshot(entity);

                var layout = new ProformaInvoicePdfLayout(entity, snapshot);
                var engine = new PdfExportEngine();

                AsyncUtility.SafeFireAndForget(
                    () => engine.PrintAsync(
                        layout, $"Proforma Invoice - {Company.ProformaId}"),
                    "PI Step7 print");
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Print] PI print failed: {ExceptionMessages.Deep(ex)}");
                _dialogs.Show(
                    string.Format(
                        LocalizationService.Instance.T("Msg.XExportFailedY"),
                        "print", ex.Message),
                    LocalizationService.Instance.T("Msg.CapExportFailed"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  SAVE INVOICE (BUG-60)
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// AUTO-DRAFT: silent save used when the user leaves the wizard
        /// without pressing SAVE. Never shows dialogs, never blocks
        /// navigation, never throws. Only persists when the wizard
        /// holds meaningful data (client name or at least one row).
        /// </summary>
        public async Task<bool> SilentDraftSaveAsync()
        {
            try
            {
                if (_invoices == null) return false;
                if (string.IsNullOrWhiteSpace(Company?.ProformaId))
                    return false;

                bool meaningful =
                    !string.IsNullOrWhiteSpace(Client?.CustomerName) ||
                    DataGrid?.SpecGroups?.Any(g =>
                        g.ItemRows?.Any(r => r.Qty > 0) == true) == true;
                if (!meaningful) return false;

                var entity = ProformaInvoiceMapper.BuildEntityFromWizard(
                    Company, Client, Project, DataGrid, OtherCharges,
                    MainSummary, Terms, this);

                if (string.IsNullOrWhiteSpace(entity.ProformaId))
                    return false;

                var existing = await _invoices.GetByProformaIdAsync(entity.ProformaId);

                // ERP hardening (Phase 2): never let the auto/silent draft save
                // rewrite the body of a posted (Approved) or cancelled PI. The
                // committed document is immutable; a background navigation save
                // must not silently mutate it.
                if (existing != null &&
                    !ProGlassAutomation.Services.Workflow.DocumentLifecyclePolicy.CanEditBody(
                        ProGlassAutomation.Services.Workflow.StatusTransitions.Kind.ProformaInvoice,
                        existing.Status))
                {
                    return false;
                }

                entity.Status = NormalizeSaveOnlyStatus(existing?.Status);
                Company.ProformaStatus = entity.Status;
                OnPropertyChanged(nameof(PreparedByStatus));

                if (existing != null)
                    await _invoices.UpdateAsync(entity);
                else
                    await _invoices.InsertAsync(entity);

                AdoptAllocatedProformaId(entity);
                return true;
            }
            catch
            {
                return false; // never block navigation on a draft save
            }
        }

        // ── CROSS-STEP VALIDATION SNAPSHOT ─────────────────────────
        // Builds a snapshot of the current invoice state for the
        // CrossStepValidator. Decoupled from the VMs so the validator
        // can be tested independently.
        // ────────────────────────────────────────────────────────────
        private ProGlassAutomation.Services.ProformaInvoice.Validation.InvoiceValidationSnapshot
            BuildValidationSnapshot()
        {
            return new ProGlassAutomation.Services.ProformaInvoice.Validation.InvoiceValidationSnapshot
            {
                // Step 1: Company
                ProformaId = Company?.ProformaId,
                CompanyName = Company?.CompanyName,
                IssueDate = Company?.IssueDate ?? DateTime.Now,
                ValidUntil = Company?.ValidUntil ?? DateTime.Now.AddDays(7),

                // Step 2: Client
                CustomerName = Client?.CustomerName,

                // Step 5: Data grid
                SpecGroupCount = DataGrid?.SpecGroups?.Count ?? 0,
                TotalSqm = DataGrid?.SummaryViewModel?.GrandTotalSqm ?? 0m,
                TotalPieces = DataGrid?.SummaryViewModel?.GrandTotalQty ?? 0m,
                TotalAmount = DataGrid?.SummaryViewModel?.GrandTotalValue ?? 0m,

                // Step 6: Other charges
                TotalOtherCharges = OtherCharges?.TotalOtherCharges ?? 0m,
                ActiveChargeRows = OtherCharges?.ChargeRows?.Count(r => r.IsActive) ?? 0,
                HasWastageRows = OtherCharges?.WastagesSection?.WastageRows?.Any(r => r.IsActive) ?? false,

                // Step 7: Summary — include the full waterfall so save can
                // fail closed if a stale snapshot or missed recalculation ever
                // makes Step 7 disagree with Step 5/6 or VAT law.
                SummaryGlassMaterialsSubtotal = MainSummary?.GlassMaterialsSubtotal ?? 0m,
                SummaryOtherChargesSubtotal = MainSummary?.OtherChargesSubtotal ?? 0m,
                NetSubtotal = MainSummary?.NetSubtotal ?? 0m,
                DiscountAmount = MainSummary?.DiscountAmount ?? 0m,
                SubtotalBeforeVat = MainSummary?.SubtotalBeforeVat ?? 0m,
                GrandTotal = MainSummary?.GrandTotalVatInclusive ?? 0m,
                VatAmount = MainSummary?.VatAmount ?? 0m,
                VatPercent = MainSummary?.VatPercent ?? 0m,
                IsVatApplicable = MainSummary?.IsVatApplicable ?? false,
                DiscountPercent = (MainSummary?.NetSubtotal ?? 0m) > 0m
                    ? Math.Round((MainSummary?.DiscountAmount ?? 0m) /
                        (MainSummary?.NetSubtotal ?? 1m) * 100m, 2)
                    : 0m,

                // Step 9: Profit & Loss
                ProfitMarginPercent = ProfitLoss?.MarginPercent ?? 0m,
                TotalCost = (ProfitLoss?.TotalGlassCost ?? 0m) + (ProfitLoss?.TotalProductionCost ?? 0m),
                TotalRevenue = ProfitLoss?.GrossVatInclusive ?? 0m
            };
        }
        // ── END CROSS-STEP VALIDATION SNAPSHOT ─────────────────────

        /// <summary>
        /// Customer-facing output gate. Printing/exporting without this check
        /// is as dangerous as saving a bad invoice: it can send a wrong total
        /// to a customer before anyone notices. Errors block; warnings require
        /// explicit operator confirmation.
        /// </summary>
        private bool EnsureCustomerDocumentCalculationsValid(string operationLabel)
        {
            var validationResult = ProGlassAutomation.Services.ProformaInvoice
                .Validation.CrossStepValidator.Validate(BuildValidationSnapshot());

            if (!validationResult.IsValid)
            {
                _dialogs.Show(
                    $"Cannot {operationLabel} until these issues are fixed:\n\n" +
                    string.Join("\n\n", validationResult.Errors),
                    "Calculation Validation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            if (validationResult.HasWarnings)
            {
                var proceed = _dialogs.Show(
                    $"Review these warnings before {operationLabel}:\n\n" +
                    string.Join("\n\n", validationResult.Warnings) +
                    "\n\nProceed anyway?",
                    "Calculation Warnings",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                return proceed == MessageBoxResult.Yes;
            }

            return true;
        }

        private async Task<bool> ExecuteSaveInvoiceAsync()
        {
            if (_invoices == null)
            {
                _dialogs.Show(
                    LocalizationService.Instance.T("Msg.InvoiceDatabaseIsNotInitialized"),
                    LocalizationService.Instance.T("Msg.CapSaveFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (string.IsNullOrWhiteSpace(Company.ProformaId))
            {
                _dialogs.Show(
                    LocalizationService.Instance.T("Msg.CannotSaveProformaIDIs"),
                    LocalizationService.Instance.T("Msg.CapSaveFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // ERP hardening (Phase 2): a posted proforma (Approved) — or a
            // cancelled one — has its body frozen. It is corrected by a new
            // revision, not by rewriting the committed document. Status-only
            // moves go through the list's Change-status action, not this Save.
            try
            {
                var locked = await _invoices.GetByProformaIdAsync(Company.ProformaId);
                if (locked != null &&
                    !ProGlassAutomation.Services.Workflow.DocumentLifecyclePolicy.CanEditBody(
                        ProGlassAutomation.Services.Workflow.StatusTransitions.Kind.ProformaInvoice,
                        locked.Status))
                {
                    _dialogs.Show(
                        LocalizationService.Instance.T("Lock.PostedBodyLocked"),
                        LocalizationService.Instance.T("Lock.PostedBodyLockedShort"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            catch (Exception lockEx)
            {
                // The lock check must not itself break a legitimate save on a
                // transient read error; log and continue (the repository still
                // enforces permissions/read-only mode).
                AppLogService.Warning($"[PI Save] body-lock check skipped: {ExceptionMessages.Deep(lockEx)}");
            }

            // ── CROSS-STEP VALIDATION ──────────────────────────────
            // Before saving, validate business rules that span multiple
            // steps. Errors block the save; warnings show a confirm dialog.
            try
            {
                var snapshot = BuildValidationSnapshot();
                var validationResult = ProGlassAutomation.Services.ProformaInvoice
                    .Validation.CrossStepValidator.Validate(snapshot);

                if (!validationResult.IsValid)
                {
                    string errorMsg = string.Join("\n\n", validationResult.Errors);
                    _dialogs.Show(
                        "Please fix the following issues before saving:\n\n" + errorMsg,
                        "Validation Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }

                if (validationResult.HasWarnings)
                {
                    string warnMsg = string.Join("\n\n", validationResult.Warnings);
                    var proceed = _dialogs.Show(
                        "The following warnings were detected:\n\n" + warnMsg +
                        "\n\nDo you want to proceed with saving?",
                        "Validation Warnings",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (proceed != MessageBoxResult.Yes)
                        return false;
                }
            }
            catch (Exception valEx)
            {
                // Fail closed: if the industrial validator itself cannot
                // evaluate the invoice, saving/exporting a potentially wrong
                // financial document is riskier than asking the operator to
                // retry after fixing the underlying state.
                ProGlassAutomation.Services.Logging.AppLogService.Debug(
                    $"[PI Save] Cross-step validation error: {ExceptionMessages.Deep(valEx)}");
                _dialogs.Show(
                    "Unable to validate invoice calculations. Save is blocked to prevent an incorrect customer document. Reopen the invoice or recalculate the affected steps, then try again.",
                    "Validation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
            // ── END CROSS-STEP VALIDATION ──────────────────────────

            // ── DUPLICATE DETECTION ────────────────────────────────
            // Check if company/client/project already exist in master data.
            // Show a warning so the user knows existing records will be updated.
            try
            {
                var masterData = App.ServiceProvider?.GetService<IMasterDataRepository>();
                if (masterData != null)
                {
                    var duplicates = new List<string>();

                    // Check company
                    if (!string.IsNullOrWhiteSpace(Company.CompanyName))
                    {
                        var existingCompany = await masterData.FindCompanyByNameAsync(Company.CompanyName);
                        if (existingCompany != null)
                        {
                            duplicates.Add($"• Company '{Company.CompanyName}' already exists and will be updated");
                        }
                    }

                    // Check client
                    if (!string.IsNullOrWhiteSpace(Client.CustomerName))
                    {
                        var existingClient = await masterData.FindClientByNameAsync(Client.CustomerName);
                        if (existingClient != null)
                        {
                            duplicates.Add($"• Client '{Client.CustomerName}' already exists and will be updated");
                        }
                    }

                    // Check project
                    if (!string.IsNullOrWhiteSpace(Project.ProjectIdentifierName) && 
                        !string.IsNullOrWhiteSpace(Client.CustomerName))
                    {
                        var existingProject = await masterData.FindProjectByNameAsync(
                            Project.ProjectIdentifierName, Client.CustomerName);
                        if (existingProject != null)
                        {
                            duplicates.Add($"• Project '{Project.ProjectIdentifierName}' already exists and will be updated");
                        }
                    }

                    // Show duplicate warning if any found
                    if (duplicates.Count > 0)
                    {
                        string duplicateMsg = "The following master data records already exist:\n\n" +
                            string.Join("\n", duplicates) +
                            "\n\nSaving will UPDATE these existing records with the new information.\n\n" +
                            "Do you want to proceed?";

                        var proceedWithDuplicates = _dialogs.Show(
                            duplicateMsg,
                            "Duplicate Records Detected",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (proceedWithDuplicates != MessageBoxResult.Yes)
                            return false;
                    }
                }
            }
            catch (Exception dupEx)
            {
                // Duplicate detection failed — log but don't block save.
                // The upsert logic in the repository will handle duplicates correctly.
                ProGlassAutomation.Services.Logging.AppLogService.Debug(
                    $"[PI Save] Duplicate detection error: {ExceptionMessages.Deep(dupEx)}");
            }
            // ── END DUPLICATE DETECTION ────────────────────────────

            // v2 permission gate — the operation itself verifies access.
            if (!ProGlassAutomation.Services.Audit.PermissionService.Require(
                ProGlassAutomation.Services.Audit.AppPermissions.ProformaCreate,
                "save a proforma invoice"))
                return false;

            // CHAT 15: BuildEntityFromWizard now expects (company, client, project, ...)
            var entity = ProformaInvoiceMapper.BuildEntityFromWizard(
                Company, Client, Project, DataGrid, OtherCharges,
                MainSummary, Terms, this);

            var existing = await _invoices.GetByProformaIdAsync(entity.ProformaId);
            entity.Status = NormalizeSaveOnlyStatus(existing?.Status);
            Company.ProformaStatus = entity.Status;
            OnPropertyChanged(nameof(PreparedByStatus));

            bool alreadyExists = existing != null;

            if (alreadyExists)
            {
                string suggestedId =
                    await _invoices.SuggestNextIdAsync(entity.ProformaId);

                var result = _dialogs.Show(
                    string.Format(LocalizationService.Instance.T("Msg.InvoiceWithIDXAlready"), entity.ProformaId, suggestedId),
                    LocalizationService.Instance.T("Msg.CapDuplicateProformaID"),
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel) return false;

                if (result == MessageBoxResult.Yes)
                {
                    if (!ProGlassAutomation.Services.Audit.PermissionService.Require(
                        ProGlassAutomation.Services.Audit.AppPermissions.ProformaEdit,
                        "update an existing invoice"))
                        return false;

                    await _invoices.UpdateAsync(entity);
                    AdoptAllocatedProformaId(entity);
                    await StampSaveArtifactsAsync(entity, isUpdate: true);
                    ShowSaveSuccess(entity.ProformaId, isUpdate: true);
                    return true;
                }

                entity.ProformaId = suggestedId;
                entity.Status = NormalizeSaveOnlyStatus(null);
                Company.ProformaId = suggestedId;
                Company.ProformaStatus = entity.Status;
                OnPropertyChanged(nameof(PreparedByStatus));
                await _invoices.InsertAsync(entity);
                AdoptAllocatedProformaId(entity);
                await StampSaveArtifactsAsync(entity, isUpdate: false);
                ShowSaveSuccess(entity.ProformaId, isUpdate: false);
                return true;
            }

            await _invoices.InsertAsync(entity);
            AdoptAllocatedProformaId(entity);
            await StampSaveArtifactsAsync(entity, isUpdate: false);
            ShowSaveSuccess(entity.ProformaId, isUpdate: false);
            return true;
        }

        /// <summary>
        /// InsertAsync may reallocate a seed / colliding id. Keep Step 1
        /// bound to the persisted ProformaId so a later save does not
        /// create a second row.
        /// </summary>
        private void AdoptAllocatedProformaId(SavedProformaInvoice entity)
        {
            if (entity == null || string.IsNullOrWhiteSpace(entity.ProformaId))
                return;
            if (Company == null) return;
            if (string.Equals(Company.ProformaId, entity.ProformaId,
                    StringComparison.Ordinal))
                return;
            Company.ProformaId = entity.ProformaId;
        }

        private string NormalizeSaveOnlyStatus(string? currentStoredStatus)
        {
            string current = NormalizeWorkflowStatus(currentStoredStatus);
            if (string.Equals(current, "Approved", StringComparison.OrdinalIgnoreCase))
                return "Approved";

            string requested = NormalizeWorkflowStatus(Company?.ProformaStatus);
            if (string.Equals(requested, "Approved", StringComparison.OrdinalIgnoreCase))
                return current;

            return requested;
        }

        private static string NormalizeWorkflowStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
                return "Draft";

            return status.Trim();
        }

        /// <summary>
        /// v2: after every successful save — record created/updated
        /// metadata and append a restorable version snapshot.
        /// Never allowed to break the save itself.
        /// </summary>
        private static async Task StampSaveArtifactsAsync(
            SavedProformaInvoice entity, bool isUpdate)
        {
            try
            {
                if (isUpdate)
                    await RecordMetadataService.StampUpdatedAsync(
                        "PI", entity.ProformaId);
                else
                    await RecordMetadataService.StampCreatedAsync(
                        "PI", entity.ProformaId);

                int version = await VersionHistoryService.AddVersionAsync(
                    "PI", entity.ProformaId,
                    isUpdate ? "Invoice updated" : "Invoice created",
                    entity.FullSnapshotJson);

                AppLogService.Debug(
                    $"[PI] Version {version} stored for {entity.ProformaId}");
            }
            catch (Exception ex)
            {
                AppLogService.Error(
                    "[PI] Save version-history artifact failed", ex);
            }
        }

        private void ShowSaveSuccess(string proformaId, bool isUpdate)
        {
            // Single audit point — all three save paths funnel through
            // here, so one line covers create AND update.
            AuditService.Log(
                AuditSections.Proforma,
                isUpdate ? "Invoice updated" : "Invoice saved",
                proformaId, "PI", proformaId);

            string verb = isUpdate ? "updated" : "saved";
            _dialogs.Show(
                string.Format(LocalizationService.Instance.T("Msg.InvoiceXWasYSuccessfully"), proformaId, verb),
                LocalizationService.Instance.T("Msg.CapSaveSuccessful"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ══════════════════════════════════════════════════════════════════
        //  LOAD INVOICE (BUG-60/62)
        // ══════════════════════════════════════════════════════════════════

        private async Task ExecuteLoadInvoiceAsync()
        {
            if (_invoices == null)
            {
                _dialogs.Show(
                    LocalizationService.Instance.T("Msg.InvoiceDatabaseIsNotInitialized"),
                    LocalizationService.Instance.T("Msg.CapLoadFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bool hasCurrentData =
                !string.IsNullOrWhiteSpace(Company.CompanyName) ||
                !string.IsNullOrWhiteSpace(Client.CustomerName) ||
                DataGrid.SpecGroups.Any(g => g.ItemRows.Count > 0);

            if (hasCurrentData)
            {
                var choice = _dialogs.Show(
                    LocalizationService.Instance.T("Msg.LoadingASavedInvoiceWill"),
                    LocalizationService.Instance.T("Inquiry.UnsavedChanges"),
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (choice == MessageBoxResult.Cancel) return;
                if (choice == MessageBoxResult.Yes)
                {
                    // A failed, cancelled, or blocked save must not open the
                    // picker. ExecuteSaveInvoiceAsync used to return void, so
                    // the load continued and replaced the invoice the user
                    // had just failed to keep.
                    bool saved = await ExecuteSaveInvoiceAsync();
                    if (!saved) return;
                }
            }

            var dialog = _dialogs.Create<SavedInvoicesListDialog>();

            bool? ok = dialog.ShowDialog();
            if (ok != true || dialog.SelectedInvoiceId <= 0) return;

            var entity = await _invoices.GetByIdAsync(dialog.SelectedInvoiceId);
            if (entity == null)
            {
                _dialogs.Show(
                    LocalizationService.Instance.T("Msg.SelectedInvoiceCouldNotBe"),
                    LocalizationService.Instance.T("Msg.CapLoadFailed"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var snapshot = ProformaInvoiceMapper.DeserializeSnapshot(entity);

            MainSummary.BeginBatchUpdate();
            try
            {
                // CHAT 15: RestoreWizardFromSnapshot now expects (snap, company, client, project, ...)
                ProformaInvoiceMapper.RestoreWizardFromSnapshot(
                    snapshot,
                    Company, Client, Project, DataGrid, OtherCharges,
                    MainSummary, Terms, this);
            }
            finally
            {
                MainSummary.EndBatchUpdate();
            }

            _dialogs.Show(
                string.Format(LocalizationService.Instance.T("Msg.InvoiceXLoadedSuccessfullyAll"), entity.ProformaId),
                LocalizationService.Instance.T("Msg.CapLoadSuccessful"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        // ══════════════════════════════════════════════════════════════════
        //  EXPORT COMMANDS (BUG-60)
        // ══════════════════════════════════════════════════════════════════

        private async Task ExecuteExportPdfAsync()
        {
            if (!EnsureExportable("PDF")) return;

            // CHAT 15: BuildEntityFromWizard param order corrected
            var entity = ProformaInvoiceMapper.BuildEntityFromWizard(
                Company, Client, Project, DataGrid, OtherCharges,
                MainSummary, Terms, this);
            var snapshot = ProformaInvoiceMapper.DeserializeSnapshot(entity);

            var layout = new ProformaInvoicePdfLayout(entity, snapshot);
            var engine = new PdfExportEngine();
            var request = new ExportRequest { PromptForLocation = true, OpenAfterSave = true };

            var result = await engine.ExportAsync(layout, request);
            ShowExportFailureIfAny(result, "PDF");
        }

        private async Task ExecuteExportExcelAsync()
        {
            if (!EnsureExportable("Excel")) return;

            var entity = ProformaInvoiceMapper.BuildEntityFromWizard(
                Company, Client, Project, DataGrid, OtherCharges,
                MainSummary, Terms, this);
            var snapshot = ProformaInvoiceMapper.DeserializeSnapshot(entity);

            var layout = new ProformaInvoiceExcelLayout(entity, snapshot);
            var engine = new ExcelExportEngine();
            var request = new ExportRequest { PromptForLocation = true, OpenAfterSave = true };

            var result = await engine.ExportAsync(layout, request);
            ShowExportFailureIfAny(result, "Excel");
        }

        private async Task ExecuteExportCsvAsync()
        {
            if (!EnsureExportable("CSV")) return;

            var entity = ProformaInvoiceMapper.BuildEntityFromWizard(
                Company, Client, Project, DataGrid, OtherCharges,
                MainSummary, Terms, this);
            var snapshot = ProformaInvoiceMapper.DeserializeSnapshot(entity);

            var layout = new ProformaInvoiceCsvLayout(entity, snapshot);
            var engine = new CsvExportEngine();
            var request = new ExportRequest { PromptForLocation = true, OpenAfterSave = true };

            var result = await engine.ExportAsync(layout, request);
            ShowExportFailureIfAny(result, "CSV");
        }

        // Sharing Center — the SAME wizard entity + layouts as the exports
        // above, staged once into the share temp folder (PI rule's default
        // format), then the composer. Draft or loaded invoice alike: the
        // context (attention, phones, salesman, validity) comes from the
        // live wizard snapshot, exactly like the printed preview.
        private async Task ExecuteShareAsync()
        {
            if (!EnsureExportable("Share")) return;

            var entity = ProformaInvoiceMapper.BuildEntityFromWizard(
                Company, Client, Project, DataGrid, OtherCharges,
                MainSummary, Terms, this);
            var snapshot = ProformaInvoiceMapper.DeserializeSnapshot(entity);
            var context = ShareContextFactory.FromProformaInvoice(entity, snapshot);

            await ShareLauncher.GenerateAndShareAsync(context, async folder =>
            {
                ExportFormat format = ShareLauncher.RuleFormat("PI");
                IDocumentLayout layout = format switch
                {
                    ExportFormat.Excel => new ProformaInvoiceExcelLayout(entity, snapshot),
                    ExportFormat.Csv => new ProformaInvoiceCsvLayout(entity, snapshot),
                    _ => new ProformaInvoicePdfLayout(entity, snapshot)
                };

                var staged = await ShareLauncher.ExportLayoutToFolderAsync(
                    layout, format, folder);
                if (!staged.Success)
                    _dialogs.Error(staged.ErrorMessage ?? "Export failed.", "Share");
                return staged.SavedPath;
            }, _dialogs);
        }

        private bool EnsureExportable(string formatLabel)
        {
            if (string.IsNullOrWhiteSpace(Company.ProformaId))
            {
                _dialogs.Show(
                    LocalizationService.Instance.T("Msg.CannotExportProformaIDIs"),
                    LocalizationService.Instance.T("Msg.CapXExportFailed"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }
            return EnsureCustomerDocumentCalculationsValid($"export {formatLabel}");
        }

        private void ShowExportFailureIfAny(ExportResult result, string formatLabel)
        {
            if (result.Success || result.Cancelled) return;
            _dialogs.Show(
                string.Format(LocalizationService.Instance.T("Msg.XExportFailedY"), formatLabel, result.ErrorMessage),
                LocalizationService.Instance.T("Msg.CapExportFailed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        // ══════════════════════════════════════════════════════════════════
        //  NOTE — 2026-09-11: the legacy code-behind FixedDocument builder
        //  (BuildPrintableDocument / BuildCompanyHeader / cached brushes)
        //  was deleted. It was a third, divergent invoice design with a
        //  hard-coded palette and no logo/theming. Printing now renders the
        //  same QuestPDF preview-parity layout as Export PDF — see
        //  ExecutePrint above.
        // ══════════════════════════════════════════════════════════════════
    }
}
