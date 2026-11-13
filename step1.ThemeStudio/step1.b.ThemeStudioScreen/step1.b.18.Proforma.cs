// ═══════════════════════════════════════════════════════════════════════
// FILE: step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.18.Proforma.cs
// MODULE: shell — the Proforma Invoice page on the navigation rail.
//
// Appended as workspace index 12, for the same reason every earlier page was
// appended: the indexes before it keep their meaning, so no saved workspace
// selection and no sign-in redirect changes target.
//
// It lives in its own part because step1.b.17 is a grandfathered file: the
// navigation state for a new page is added beside it, never inside it.
// ═══════════════════════════════════════════════════════════════════════

using System.Windows.Input;

namespace ProGlassAutomation.ThemeStudio;

public sealed partial class ThemeStudioViewModel
{
    /// <summary>step6 Proforma Invoice workspace (index 12; appended so 0–11 keep their meaning).</summary>
    public bool IsProforma { get => selectedWorkspace == 12; set { if (value) SelectedWorkspace = 12; } }

    /// <summary>Opens the Proforma Invoice page.</summary>
    public ICommand ShowProforma { get; private set; } = null!;
}
