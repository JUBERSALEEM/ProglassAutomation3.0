// ═══════════════════════════════════════════════════════════════════════
// MODULE: step6 — a stage panel is pure binding, so it has no code.
// It inherits the editor's view model and hands nothing back.
// ═══════════════════════════════════════════════════════════════════════

using System.Windows.Controls;

namespace ProGlassAutomation.Proforma;

public partial class ProformaProfitStage : UserControl
{
    public ProformaProfitStage() => InitializeComponent();
}
