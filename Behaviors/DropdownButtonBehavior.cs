// ═══════════════════════════════════════════════════════════════════════
// FILE: Behaviors/DropdownButtonBehavior.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 2.5 — MVVM leakage sweep. Replaces the "dropdown button + popup"
// code-behind trio (toggle button click, close after item click, close on
// outside click) with one attached property:
//
//     <Button behaviors:DropdownButtonBehavior.Popup="{Binding ElementName=MyPopup}"/>
//
// Attach it to the toggle button AND to popup items that should close the
// popup when clicked. Clicking anywhere outside the button/popup also
// closes it (window-content PreviewMouseLeftButtonDown).
// ═══════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ProGlassAutomation.Utilities;

namespace ProGlassAutomation.Behaviors
{
    public static class DropdownButtonBehavior
    {
        private static readonly HashSet<ButtonBase> Buttons = new();

        /// <summary>
        /// The popup this button toggles open/closed on click.
        /// </summary>
        public static readonly DependencyProperty PopupProperty =
            DependencyProperty.RegisterAttached(
                "Popup", typeof(Popup), typeof(DropdownButtonBehavior),
                new PropertyMetadata(null, OnPopupChanged));

        public static void SetPopup(DependencyObject d, Popup? value) => d.SetValue(PopupProperty, value);
        public static Popup? GetPopup(DependencyObject d) => (Popup?)d.GetValue(PopupProperty);

        /// <summary>
        /// A popup to close when this button is clicked (for popup items).
        /// </summary>
        public static readonly DependencyProperty ClosePopupProperty =
            DependencyProperty.RegisterAttached(
                "ClosePopup", typeof(Popup), typeof(DropdownButtonBehavior),
                new PropertyMetadata(null, OnPopupChanged));

        public static void SetClosePopup(DependencyObject d, Popup? value) => d.SetValue(ClosePopupProperty, value);
        public static Popup? GetClosePopup(DependencyObject d) => (Popup?)d.GetValue(ClosePopupProperty);

        private static void OnPopupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ButtonBase button) return;

            button.Click -= OnClick;
            button.Loaded -= OnLoaded;
            button.Unloaded -= OnUnloaded;

            if (e.NewValue is Popup)
            {
                button.Click += OnClick;
                button.Loaded += OnLoaded;
                Buttons.Add(button);
            }
            else
            {
                Buttons.Remove(button);
            }
        }

        private static void OnClick(object sender, RoutedEventArgs e)
        {
            if (sender is not ButtonBase button) return;

            if (GetPopup(button) is Popup popup)
            {
                popup.IsOpen = !popup.IsOpen;
                e.Handled = true;
                return;
            }

            if (GetClosePopup(button) is Popup closePopup)
                closePopup.IsOpen = false;
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ButtonBase button) return;

            // Re-register after a view unload/reload cycle (module switches).
            if (GetPopup(button) != null)
                Buttons.Add(button);

            var window = Window.GetWindow(button);
            if (window?.Content is UIElement root)
            {
                // handledEventsToo: also catch clicks that other elements
                // handle, so the dropdown closes on any outside click.
                root.PreviewMouseLeftButtonDown -= OnRootPreviewMouseLeftButtonDown;
                root.PreviewMouseLeftButtonDown += OnRootPreviewMouseLeftButtonDown;
            }

            button.Unloaded += OnUnloaded;
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ButtonBase button) return;

            var window = Window.GetWindow(button);
            if (window?.Content is UIElement root)
                root.PreviewMouseLeftButtonDown -= OnRootPreviewMouseLeftButtonDown;

            button.Unloaded -= OnUnloaded;
            button.Click -= OnClick;
            button.Loaded -= OnLoaded;
            Buttons.Remove(button);
        }

        private static void OnRootPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source) return;
            if (sender is not DependencyObject rootElement) return;
            var window = Window.GetWindow(rootElement);
            if (window == null) return;

            foreach (var button in Buttons)
            {
                if (GetPopup(button) is not Popup popup || !popup.IsOpen) continue;
                if (!ReferenceEquals(Window.GetWindow(button), window)) continue;

                bool insidePopup = popup.Child != null &&
                    UiTreeHelper.IsDescendantOf(source, popup.Child);
                bool insideButton = UiTreeHelper.IsDescendantOf(source, button);

                if (!insidePopup && !insideButton)
                    popup.IsOpen = false;
            }
        }
    }
}
