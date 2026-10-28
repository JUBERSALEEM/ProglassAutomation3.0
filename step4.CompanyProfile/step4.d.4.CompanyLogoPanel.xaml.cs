// ═══════════════════════════════════════════════════════════════════════
// FILE: step4.CompanyProfile/step4.d.4.CompanyLogoPanel.xaml.cs
// MODULE: step4 — the logo card's code, which is none.
//
// The card is markup and bindings: the preview page, the three placement tiles,
// the printed-size picker, the file chip and "Choose image". Picking a file
// needs a dialog, and that stays where it already lives — the page (step4.d.2),
// which owns the view model and the RequestLogoPick handler.
// ═══════════════════════════════════════════════════════════════════════

using System.Windows.Controls;

namespace ProGlassAutomation.Company
{
    /// <summary>The Company page's logo and document-preview card.</summary>
    public partial class CompanyLogoPanel : UserControl
    {
        public CompanyLogoPanel()
        {
            InitializeComponent();
        }
    }
}
