using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProGlassAutomation.Behaviors
{
    /// <summary>
    /// Fixes nested scroll conflict when a virtualized ListView sits inside
    /// a scrollable parent (e.g. Step 5 DataGrid — outer ScrollViewer with
    /// SmoothScrollBehavior + inner ListView with MaxHeight).
    ///
    /// PROBLEM WITHOUT THIS BEHAVIOR:
    ///   1. Inner ListView doesn't reliably scroll on mouse wheel when
    ///      TextBox has focus (WPF quirk with virtualization + ScrollUnit=Item)
    ///   2. When inner is at top/bottom, wheel events don't bubble to
    ///      outer ScrollViewer → user gets "stuck"
    ///
    /// HOW THIS BEHAVIOR WORKS:
    ///   PreviewMouseWheel handler runs BEFORE ListView's internal logic.
    ///   
    ///   Strategy:
    ///   - If inner CAN scroll in wheel direction → manually scroll inner
    ///     and mark event handled (guarantees scroll happens regardless of focus)
    ///   - If inner CAN'T scroll (at boundary) → do NOT handle, let event
    ///     bubble naturally to outer ScrollViewer
    ///
    /// USAGE:
    ///   xmlns:behaviors="clr-namespace:ProGlassAutomation.Behaviors"
    ///
    ///   <ListView behaviors:SmartScrollBubblingBehavior.IsEnabled="True" ...>
    /// </summary>
    public static class SmartScrollBubblingBehavior
    {
        // ═══════════════════════════════════════════════════════════════
        // TUNING
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Pixels scrolled per wheel notch. Matches SmoothScrollBehavior default.
        /// Wheel Delta is ±120 per notch, so 3 items per notch feels natural
        /// for a data-entry grid where each row is ~34px tall.
        /// </summary>
        private const double PixelsPerNotch = 80.0;

        // ═══════════════════════════════════════════════════════════════
        // ATTACHED PROPERTY: IsEnabled
        // ═══════════════════════════════════════════════════════════════
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled", typeof(bool), typeof(SmartScrollBubblingBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject target, bool value) =>
            target.SetValue(IsEnabledProperty, value);

        public static bool GetIsEnabled(DependencyObject target) =>
            (bool)target.GetValue(IsEnabledProperty);

        private static void OnIsEnabledChanged(DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement el) return;

            if ((bool)e.NewValue)
                el.PreviewMouseWheel += OnPreviewMouseWheel;
            else
                el.PreviewMouseWheel -= OnPreviewMouseWheel;
        }

        // ═══════════════════════════════════════════════════════════════
        // WHEEL EVENT HANDLER
        // ═══════════════════════════════════════════════════════════════
        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DependencyObject dep) return;

            // Find the ScrollViewer INSIDE this ListView/ItemsControl
            var innerScrollViewer = FindDescendantScrollViewer(dep);
            if (innerScrollViewer == null) return;

            // Nothing to scroll inside — bubble up
            if (innerScrollViewer.ScrollableHeight <= 0.0)
                return; // don't set e.Handled → event bubbles to outer

            bool scrollingUp = e.Delta > 0;
            bool scrollingDown = e.Delta < 0;

            double currentOffset = innerScrollViewer.VerticalOffset;
            double maxOffset = innerScrollViewer.ScrollableHeight;

            bool atTop = currentOffset <= 0.0;
            bool atBottom = currentOffset >= maxOffset - 0.5;

            // At boundary and trying to scroll past → let event bubble
            if ((atTop && scrollingUp) || (atBottom && scrollingDown))
                return; // don't set e.Handled → outer ScrollViewer takes over

            // Inner CAN scroll in this direction → do it manually
            // (guarantees scroll happens regardless of focus state)
            double deltaScroll = (e.Delta / 120.0) * PixelsPerNotch;
            double newOffset = currentOffset - deltaScroll;

            // Clamp to valid range
            if (newOffset < 0) newOffset = 0;
            if (newOffset > maxOffset) newOffset = maxOffset;

            innerScrollViewer.ScrollToVerticalOffset(newOffset);

            // Mark handled so outer ScrollViewer doesn't also scroll
            e.Handled = true;
        }

        // ═══════════════════════════════════════════════════════════════
        // VISUAL TREE HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Walks DOWN the visual tree to find the ScrollViewer inside a control
        /// (ListView/ListBox/ItemsControl typically contains one internally).
        /// </summary>
        private static ScrollViewer FindDescendantScrollViewer(DependencyObject parent)
        {
            if (parent == null) return null;

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is ScrollViewer sv)
                    return sv;

                var found = FindDescendantScrollViewer(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}