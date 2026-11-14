// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/Shared/GodViewKpiViewModel.cs
// PROJECT: ProGlassAutomation
// MODULE: Shared — God View (module overview tabs)
//
// One KPI stat card shown at the top of every module's God View
// (Delivery Note, Job Order, Tax Invoice). Theme-aware: TintBrushKey
// references a theme resource key ("TintIndigoBrush", ...) which the
// BrushKeyConverter resolves at render time, so cards recolor
// automatically on light↔dark switch.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.ViewModels.Shared
{
    /// <summary>One KPI tile: icon + big value + label.</summary>
    public class GodViewKpiViewModel : ViewModelBase
    {
        /// <summary>Emoji glyph shown in the icon badge.</summary>
        public string Icon { get; set; } = "📊";

        private string _value = "0";
        /// <summary>Formatted value (count, amount, percentage...).</summary>
        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        /// <summary>Short caption under the value.</summary>
        public string Label { get; set; } = "";

        /// <summary>
        /// Theme brush resource key for the card tint
        /// (e.g. "TintIndigoBrush"). Resolved via BrushKeyConverter.
        /// </summary>
        public string TintBrushKey { get; set; } = "TintIndigoBrush";

        /// <summary>
        /// Theme brush resource key for the value text colour.
        /// Defaults to the accent brush.
        /// </summary>
        public string ValueBrushKey { get; set; } = "AccentBrush";
    }
}
