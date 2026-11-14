// ═══════════════════════════════════════════════════════════════════════
// FILE: PrintableInvoiceViewModel.Part2.cs (Phase 3.8 split)
// ═══════════════════════════════════════════════════════════════════════
//
// 2026-09-11 — This file used to carry the legacy code-behind print
// renderer (BuildClientProjectBlock, BuildSpecTable, BuildGrandSpecSummary,
// BuildOtherChargesSection, BuildFinancialSummary, BuildTermsAndConditions,
// BuildSignatureBlocks, MakeText, …). That renderer was a THIRD invoice
// design — hard-coded indigo palette, no company logo, no theming — so the
// physical printout never matched the Step 7 preview or the exported PDF.
// All of it was deleted when printing moved onto the shared QuestPDF
// preview-parity layout (see ExecutePrint in Part1). Only non-rendering
// helpers remain below.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Localization;
using System.Windows;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.PrintableInvoice
{
    public partial class PrintableInvoiceViewModel : ViewModelBase
    {
        private void ShowComingSoon(string featureName, string phase)
        {
            _dialogs.Show(
                string.Format(LocalizationService.Instance.T("Msg.XIsComingInY"), featureName, phase),
                LocalizationService.Instance.T("Msg.CapXComingSoon"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
