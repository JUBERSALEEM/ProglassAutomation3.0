// ═══════════════════════════════════════════════════════════════════════
// FILE: Behaviors/ContextMenuButtonBehavior.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 2.5 — MVVM leakage sweep. Opens a ContextMenu on LEFT click:
//
//     <Button behaviors:ContextMenuButtonBehavior.Menu="{Binding ElementName=ExportMenu}">
//         <Button.ContextMenu>
//             <ContextMenu x:Name="ExportMenu" .../>
//         </Button.ContextMenu>
//     </Button>
//
// Replaces the "PlacementTarget + Placement + IsOpen = true" code-behind.
// ═══════════════════════════════════════════════════════════════════════

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ProGlassAutomation.Behaviors
{
    public static class ContextMenuButtonBehavior
    {
        public static readonly DependencyProperty MenuProperty =
            DependencyProperty.RegisterAttached(
                "Menu", typeof(ContextMenu), typeof(ContextMenuButtonBehavior),
                new PropertyMetadata(null, OnMenuChanged));

        public static void SetMenu(DependencyObject d, ContextMenu? value) => d.SetValue(MenuProperty, value);
        public static ContextMenu? GetMenu(DependencyObject d) => (ContextMenu?)d.GetValue(MenuProperty);

        private static void OnMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ButtonBase button) return;

            button.Click -= OnClick;
            if (e.NewValue is ContextMenu)
                button.Click += OnClick;
        }

        private static void OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is not ButtonBase button) return;
            var menu = GetMenu(button) ?? button.ContextMenu;
            if (menu == null) return;

            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.VerticalOffset = 4;
            menu.IsOpen = true;
            e.Handled = true;
        }
    }
}
