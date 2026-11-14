// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step7PrintPreview/PrintableInvoice/PrintableInvoiceViewModel.cs
// ═══════════════════════════════════════════════════════════════════════
// CHAT 15 — NAMESPACE FIX
//   using Step2ProjectSpecs → Step2ClientProfile
//   using Step3ClientProfile → Step3ProjectSpecs
//   Constructor param order: (company, CLIENT, PROJECT, ...)
//     was: (company, ProjectSpecsViewModel, ClientProfileViewModel, ...)
//     now: (company, ClientProfileViewModel, ProjectSpecsViewModel, ...)
//   Property types updated to new namespaces
//   ALL print/render/export logic preserved exactly — zero changes
// ═══════════════════════════════════════════════════════════════════════

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
    /// <summary>
    /// PrintableInvoice ViewModel — hosts the A4 preview, print engine,
    /// save/load coordinator, and PDF/Excel/CSV export coordinators.
    ///
    /// 2026-09-11 WYSIWYG redesign: printing and PDF export now share ONE
    /// renderer — ProformaInvoicePdfLayout with the Step 7 preview-parity
    /// design. The legacy code-behind FixedDocument builder (a third,
    /// unthemed invoice design) was deleted; see ExecutePrint in Part1.
    ///
    /// BUG-60 [CRITICAL]: async void handlers replaced with SafeFireAsync.
    /// BUG-62 [HIGH]: LoadInvoice uses SafeFireAsync.
    /// CHAT 15: Namespace fix — Step2=Client, Step3=Project.
    /// </summary>
    public partial class PrintableInvoiceViewModel : ViewModelBase
    {
    }
}