// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step5DataGrid/SpecGroup.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — Step 5 data grid
//
// SpecGroup.cs extracted from the large DataGridViewModel file (Phase 11/12
// God-VM split) — behaviour unchanged, relocated for organisation only.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Behaviors;
using ProGlassAutomation.Services.ProformaInvoice.SpecificationsRepository;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.Specifications;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid.GrandTotalSummary;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid
{
    // PARTIAL CLASS (senior audit — future safe extraction seam)
    // ─────────────────────────────────────────────────────────
    // Declared partial so a follow-up PR can move regions into
    // sibling files (SpecGroup.Paste.cs, SpecGroup.FindReplace.cs,
    // SpecGroup.Selection.cs, SpecGroup.Rendering.cs) without
    // touching this file's git blame or its 700+ commits of
    // audit history. Splitting today would move blocks whose
    // semantics are stable; the file's real cost is search,
    // not risk.
    public partial class SpecGroup : ViewModelBase, IExcelPasteable
    {
    }
}
