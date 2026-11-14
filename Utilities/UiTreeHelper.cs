// ═══════════════════════════════════════════════════════════════════════
// FILE: Utilities/UiTreeHelper.cs
// PROJECT: ProGlassAutomation
//
// Shared visual-tree helpers used by code-behind. Before this existed the
// IsDescendantOf helper was duplicated verbatim in MainWindow.xaml.cs and
// SheetStoreView.xaml.cs. Centralising it means one definition to keep
// correct (Phase 11/12 — remove duplication).
//
// v4.1 FIX: Now bridges Popup boundaries (PlacementTarget) so ComboBox
// dropdown items inside ScreenRecordPanel are correctly recognized as
// descendants of their host panel.
// ═══════════════════════════════════════════════════════════════════════

using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ProGlassAutomation.Utilities
{
    /// <summary>Visual-tree helpers for UI code-behind.</summary>
    public static class UiTreeHelper
    {
        /// <summary>
        /// True when <paramref name="element"/> is a descendant of
        /// <paramref name="root"/> in the visual or logical tree.
        /// Handles:
        ///   - Visual tree (VisualTreeHelper)
        ///   - Logical tree (LogicalTreeHelper)
        ///   - FrameworkElement.Parent / TemplatedParent
        ///   - FrameworkContentElement.Parent
        ///   - Popup.PlacementTarget (critical for ComboBox dropdowns which live
        ///     in a separate visual tree rooted at Popup)
        /// </summary>
        public static bool IsDescendantOf(
            DependencyObject element, DependencyObject root)
        {
            if (element == null || root == null) return false;

            DependencyObject? current = element;
            while (current != null)
            {
                if (ReferenceEquals(current, root)) return true;

                DependencyObject? parent = null;

                // Visual tree
                try { parent = VisualTreeHelper.GetParent(current); }
                catch { parent = null; }

                // Logical tree
                if (parent == null)
                {
                    try { parent = LogicalTreeHelper.GetParent(current); }
                    catch { parent = null; }
                }

                // FrameworkElement fallbacks
                if (parent == null && current is FrameworkElement fe)
                {
                    parent = fe.Parent ?? fe.TemplatedParent as DependencyObject;
                }

                // FrameworkContentElement fallback
                if (parent == null && current is FrameworkContentElement fce)
                {
                    parent = fce.Parent ?? fce.TemplatedParent as DependencyObject;
                }

                // Popup boundary bridge
                if (parent == null && current is Popup popup)
                {
                    parent = popup.PlacementTarget;
                }

                current = parent;
            }
            return false;
        }

        public static bool IsVisual(DependencyObject? node)
            => node is Visual or Visual3D;

        public static int SafeChildrenCount(DependencyObject? node)
            => node is Visual or Visual3D
                ? VisualTreeHelper.GetChildrenCount(node)
                : 0;

        public static DependencyObject? SafeGetChild(DependencyObject node, int index)
            => node is Visual or Visual3D
                ? VisualTreeHelper.GetChild(node, index)
                : null;

        public static DependencyObject? SafeGetParent(DependencyObject? node)
        {
            if (node == null) return null;

            // Try visual first
            if (node is Visual or Visual3D)
            {
                var vp = VisualTreeHelper.GetParent(node);
                if (vp != null) return vp;
            }

            // Logical
            var lp = LogicalTreeHelper.GetParent(node);
            if (lp != null) return lp;

            // FrameworkElement.Parent / TemplatedParent
            if (node is FrameworkElement fe)
            {
                var p = fe.Parent ?? fe.TemplatedParent as DependencyObject;
                if (p != null) return p;
            }

            if (node is FrameworkContentElement fce)
            {
                var p = fce.Parent ?? fce.TemplatedParent as DependencyObject;
                if (p != null) return p;
            }

            // Popup bridge
            if (node is Popup popup)
            {
                return popup.PlacementTarget;
            }

            return null;
        }
    }
}
