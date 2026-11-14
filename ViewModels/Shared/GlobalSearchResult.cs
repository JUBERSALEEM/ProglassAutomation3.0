// ═══════════════════════════════════════════════════════════════════════
// FILE: GlobalSearchResult.cs
// PROJECT: ProGlassAutomation
// MODULE: GlobalSearchViewModel.cs
//
// GlobalSearchResult split out of GlobalSearchViewModel.cs (Phase 11/12 God-VM split) — behaviour unchanged.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.Shared
{
    public class GlobalSearchResult
    {
        /// <summary>Module label shown as the leading chip ("Job Orders").</summary>
        public string ModuleLabel { get; set; } = "";

        /// <summary>Navigation key passed to MainViewModel.Navigate.</summary>
        public string NavigateTarget { get; set; } = "";

        /// <summary>Primary line — usually the document id or name.</summary>
        public string Title { get; set; } = "";

        /// <summary>Secondary line — customer, project, status…</summary>
        public string Subtitle { get; set; } = "";

        /// <summary>Segoe MDL2 glyph for the row icon.</summary>
        public string Glyph { get; set; } = "\uE71E";
    }
}
