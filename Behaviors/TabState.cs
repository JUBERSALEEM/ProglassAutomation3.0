// ═══════════════════════════════════════════════════════════════════════
// FILE: Behaviors/TabState.cs
// MODULE: Shared — attached "this tab is the active one" flag
// ADDED: 2026-09-06
//
// TabPillButton needs a selected state, and a plain Button has nowhere
// to put one. The alternatives were both worse:
//
//   • ToggleButton/IsChecked — drags in three-state semantics and lets
//     two tabs be checked at once, which is not a tab strip.
//   • RadioButton groups — the group name is global to the visual
//     parent, so two tab strips on one screen silently interfere.
//
// A single attached bool keeps the owning view-model holding ONE string
// (ActiveKey) as the source of truth, exactly like the shell nav does.
// Each tab binds IsActive to "does my key equal ActiveKey", so an
// invalid state is unrepresentable — there is no second copy of the
// selection to drift out of sync.
// ═══════════════════════════════════════════════════════════════════════

using System.Windows;

namespace ProGlassAutomation.Behaviors
{
    public static class TabState
    {
        /// <summary>
        /// True when this element represents the currently active tab.
        /// Bound, not set imperatively — see the file header.
        /// </summary>
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.RegisterAttached(
                "IsActive",
                typeof(bool),
                typeof(TabState),
                new PropertyMetadata(false));

        public static bool GetIsActive(DependencyObject obj)
            => (bool)obj.GetValue(IsActiveProperty);

        public static void SetIsActive(DependencyObject obj, bool value)
            => obj.SetValue(IsActiveProperty, value);
    }
}
