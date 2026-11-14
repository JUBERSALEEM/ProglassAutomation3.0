// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Behaviors/DataGridNavigationBehavior.cs                                 ║
// ║  ProGlass Automation                                                     ║
// ║                                                                          ║
// ║  CHAT 20 FINAL — Multi-row selection + persistent Find/Replace.         ║
// ║                                                                          ║
// ║  NEW IN THIS VERSION:                                                    ║
// ║    SEL-01 : Excel-style multi-row selection                              ║
// ║              • Click row (outside TextBox) → select single row           ║
// ║              • Shift+Click → range select from anchor                    ║
// ║              • Ctrl+Click → toggle row in/out of selection               ║
// ║              • Escape → clear selection                                  ║
// ║              • Ctrl+A → select all rows in group                         ║
// ║    SEL-02 : Ctrl+D across multiple selected rows                        ║
// ║              • 0 or 1 selected → single-column fill from row above      ║
// ║              • 2+ selected → source=first, targets=others,              ║
// ║                fill writable columns (GlassRef,W1,H1,W2,H2,Qty)         ║
// ║    FIND-01: Persistent Find dialog — stays open for Find Next          ║
// ║              • Callback pattern — dialog fires event on click           ║
// ║              • Close button or Escape closes dialog                     ║
// ║              • Wrap-around search                                       ║
// ║    FIND-02: Persistent Find/Replace dialog                             ║
// ║              • Replace All fires callback, dialog stays open           ║
// ║              • Multiple replace operations possible                    ║
// ║                                                                          ║
// ║  INTERFACE ADDITIONS (must be implemented in SpecGroup):                ║
// ║    void ClearRowSelection()                                             ║
// ║    void SelectAllRows()                                                 ║
// ║    void SetRowSelection(object, bool)                                   ║
// ║    void SelectRowRange(object, object)                                  ║
// ║    List<object> GetSelectedRows()                                       ║
// ║    void FillDownAcrossRows(object, List<object>)                        ║
// ║                                                                          ║
// ║  PRESERVED FROM PREVIOUS:                                               ║
// ║    ✅ Tab/Shift+Tab, Enter, Arrow navigation                            ║
// ║    ✅ Ctrl+V multi-cell paste routing                                   ║
// ║    ✅ Ctrl+Home/End, Ctrl+Delete                                        ║
// ║    ✅ Per-ListView anchor properties                                    ║
// ║    ✅ TextBox cache with StatusChanged invalidation                     ║
// ║    ✅ AutoAddRowAndFocus on Down/Enter at last row                      ║
// ║    ✅ Row boundary rules for Left/Right                                 ║
// ║    ✅ Virtualization-safe scroll                                        ║
// ╚══════════════════════════════════════════════════════════════════════════╝
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProGlassAutomation.Behaviors
{
    public static partial class DataGridNavigationBehavior
    {
    }
}
