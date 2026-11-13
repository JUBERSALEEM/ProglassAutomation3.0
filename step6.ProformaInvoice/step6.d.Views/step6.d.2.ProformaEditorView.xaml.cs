// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.d.Views/step6.d.2.ProformaEditorView.xaml.cs
// MODULE: step6 — the editor's composition.
//
// The editor creates nothing: it inherits the page's view model, which is the
// point of splitting the page into panels. No handler is needed here, so the
// file exists only to complete the XAML class.
// ═══════════════════════════════════════════════════════════════════════

using System.Windows.Controls;

namespace ProGlassAutomation.Proforma;

/// <summary>The proforma invoice editor: rail, lifecycle commands and stage host.</summary>
public partial class ProformaEditorView : UserControl
{
    public ProformaEditorView() => InitializeComponent();
}
