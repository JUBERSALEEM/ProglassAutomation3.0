// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step7PrintPreview/PrintPreviewViewModel.cs
// ═══════════════════════════════════════════════════════════════════════
// CHAT 15 — NAMESPACE FIX
//   using Step2ProjectSpecs → Step2ClientProfile
//   using Step3ClientProfile → Step3ProjectSpecs
//   Constructor param order: (company, CLIENT, PROJECT, ...)
//   PrintableInvoiceViewModel call order updated to match
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.ViewModels;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step1CompanySetup;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step2ClientProfile;   // FIXED
using ProGlassAutomation.ViewModels.ProformaInvoice.Step3ProjectSpecs;    // FIXED
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.InvoiceMainSummary;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.PrintableInvoice;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.TermsAndConditions;

using ProGlassAutomation.Services.Dialogs;
using ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository;
namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview
{
    /// <summary>
    /// Main ViewModel for Step 7 — Print Preview.
    /// Hosts InvoiceMainSummary, Terms, and PrintableInvoice.
    ///
    /// CHAT 15: Parameter order corrected after step swap.
    ///   Step 2 = ClientProfileViewModel  (was ProjectSpecsViewModel)
    ///   Step 3 = ProjectSpecsViewModel   (was ClientProfileViewModel)
    /// </summary>
    public class PrintPreviewViewModel : ViewModelBase
    {
        public string Title { get; } = "PRINT PREVIEW & REVIEW";
        public string Subtitle { get; } =
            "Final review of financial totals, editable terms, and printable A4 preview.";

        /// <summary>Phase 7.12 — Invoice financial totals card.</summary>
        public InvoiceMainSummaryViewModel InvoiceMainSummary { get; }

        /// <summary>Phase 7.14 — Editable Terms &amp; Conditions.</summary>
        public TermsAndConditionsViewModel Terms { get; }

        /// <summary>Phase 7.13 — Full A4 printable invoice preview.</summary>
        public PrintableInvoiceViewModel PrintableInvoice { get; }

        /// <summary>
        /// Live PDF-layout switcher shown above the paper. One instance,
        /// shared with the printable invoice, so the toolbar and the document
        /// can never disagree about which layout is active.
        /// </summary>
        public ProGlassAutomation.ViewModels.Shared.PrintLayoutViewModel PrintLayout { get; }
            = new ProGlassAutomation.ViewModels.Shared.PrintLayoutViewModel();

        /// <summary>
        /// CHAT 15: Parameter order corrected.
        /// Was: (company, ProjectSpecsViewModel, ClientProfileViewModel, ...)
        /// Now: (company, ClientProfileViewModel, ProjectSpecsViewModel, ...)
        /// ProformaInvoiceViewModel passes Step2VM=Client, Step3VM=Project.
        /// </summary>
                private readonly IDialogService _dialogs = null!;
        private readonly IProformaInvoiceRepository _invoices = null!;

public PrintPreviewViewModel(
            IDialogService? dialogs,
            IProformaInvoiceRepository? invoices,
            CompanySetupViewModel companyVM,
            ClientProfileViewModel clientVM,      // FIXED: was ProjectSpecsViewModel
            ProjectSpecsViewModel projectVM,     // FIXED: was ClientProfileViewModel
            DataGridViewModel dataGridVM,
            OtherChargesViewModel otherChargesVM)
        {
            _dialogs = dialogs!;
            _invoices = invoices!;
            InvoiceMainSummary = new InvoiceMainSummaryViewModel(
                dataGridVM,
                otherChargesVM);

            Terms = new TermsAndConditionsViewModel();

            // FIXED: pass clientVM and projectVM in correct order
            PrintableInvoice = new PrintableInvoiceViewModel(
                _dialogs,
                _invoices,
                companyVM,
                clientVM,       // ClientProfileViewModel — Step 2
                projectVM,      // ProjectSpecsViewModel  — Step 3
                dataGridVM,
                otherChargesVM,
                InvoiceMainSummary,
                Terms);

            // Same instance on both sides — see PrintLayout doc comment.
            PrintableInvoice.PrintLayout = PrintLayout;
        }
    }
}