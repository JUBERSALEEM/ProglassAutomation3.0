// ═══════════════════════════════════════════════════════════════════════
// FILE: Behaviors/SmoothScrollBehavior.cs
// PROJECT: ProGlassAutomation
// PURPOSE: Frame-stable, constant-FPS scroll tuning for ScrollViewer /
//          virtualized lists.
//
// ARCHITECTURE (2026-08-30 senior perf rewrite):
//   The previous version eased every wheel notch with a
//   CompositionTarget.Rendering loop. On real screens that loop did a
//   ScrollToVerticalOffset per frame (~60-144 fps); with CanContentScroll=
//   False content (dashboards, Step 5 grid, wizard pages) every frame
//   forced a full re-measure/re-layout/re-render of the ENTIRE non-
//   virtualized subtree — that is the classic "smooth scroll is laggy"
//   problem, and on virtualized pixel lists it made the panel re-realise
//   containers continuously. The perceived "rubber-band" ease-out was
//   also a large part of the laggy feel.
//
//   PERF-8 fix — no render loop at all:
//     • VIRTUALIZED lists (IsVirtualizing=true + ScrollUnit=Pixel, the
//       .NET 4.5 combo used by every list page) are handed back to the
//       native WPF ScrollViewer. WPF already scrolls these by pixel on
//       each wheel event without re-realising items between notches, and
//       it does NOT do per-frame work while the wheel is idle. Our job is
//       simply to get out of the way.
//     • All other scrollers (page scroll viewports, dashboards, settings,
//       dialogs, the Step 5 grid) scroll the full 120px per notch
//       INSTANTLY — one ScrollToVerticalOffset per wheel event, zero
//       animation clocks, zero continuous re-render. Scroll cost is
//       proportional to actual input, not to display refresh rate.
//
//   This gives a Chrome/Linear-like pixel wheel feel with the lowest
//   possible per-frame cost. If a buttery-eased feel is ever needed on a
//   cheap, non-virtualized page, that should be done with a single
//   DoubleAnimation on the ScrollViewer's templated offset (one clock,
//   stops itself), never with a global Rendering loop.
//
// FEATURES:
//   • Mouse wheel — 120 px per notch, immediate (no easing lag)
//   • Precision touchpad — fine-grained delta honoured as-is
//   • Virtualized pixel lists — delegated to the native scroller
//   • Works attached to ScrollViewer OR ListBox/ListView/ItemsControl
//     (previously attaching to a ListBox silently did nothing)
//   • Auto-cleanup via ConditionalWeakTable + Unloaded (leak-proof)
//   • No per-frame work while the wheel is idle
//
// USAGE:
//   xmlns:behaviors="clr-namespace:ProGlassAutomation.Behaviors"
//   <ScrollViewer behaviors:SmoothScrollBehavior.IsEnabled="True" .../>
//   <ListBox behaviors:SmoothScrollBehavior.IsEnabled="True" .../>
//
// SENIOR DEV NOTE:
//   Do NOT nest scroll surfaces. If inner content is a virtualized list,
//   set CanContentScroll=True + VirtualizingStackPanel.ScrollUnit=Pixel
//   so WPF keeps virtualization AND pixel scrolling.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ProGlassAutomation.Behaviors
{
    /// <summary>
    /// Attached behavior providing pixel-accurate mouse-wheel scrolling.
    /// Zero-allocation per wheel event, virtualization-aware, no render loop.
    /// </summary>
    public static class SmoothScrollBehavior
    {
        // ═══════════════════════════════════════════════════════════════
        //  TUNING CONSTANTS — Chrome/Linear-like
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Pixels per mouse wheel notch (matches Windows 120 delta).</summary>
        private const double WheelNotchPixels = 120.0;

        /// <summary>
        /// Delta magnitude below which input is treated as precision touchpad
        /// (fine-grained scroll from Windows precision touchpads).
        /// </summary>
        private const double PrecisionThreshold = 60.0;

        // ═══════════════════════════════════════════════════════════════
        //  STATE — per ScrollViewer, GC-safe via ConditionalWeakTable
        // ═══════════════════════════════════════════════════════════════

        private sealed class ScrollState
        {
            /// <summary>
            /// True when the scroll viewer hosts a virtualized, pixel-scrolling
            /// items panel — these are delegated to native WPF scrolling.
            /// Refreshed on every wheel event (cheap visual-tree probe).
            /// </summary>
            public bool IsVirtualizedPixelList;
        }

        // ConditionalWeakTable auto-cleans state when ScrollViewer is GC'd.
        private static readonly ConditionalWeakTable<ScrollViewer, ScrollState>
            _states = new();

        // ItemsControl → inner ScrollViewer, so a behavior attached to a
        // ListBox/ListView can detach the exact scroller it attached to.
        private static readonly ConditionalWeakTable<ItemsControl, ScrollViewer>
            _itemsToScroller = new();

        // ═══════════════════════════════════════════════════════════════
        //  ATTACHED PROPERTY: IsEnabled
        // ═══════════════════════════════════════════════════════════════

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(SmoothScrollBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject target, bool value)
            => target.SetValue(IsEnabledProperty, value);

        public static bool GetIsEnabled(DependencyObject target)
            => (bool)target.GetValue(IsEnabledProperty);

        private static void OnIsEnabledChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                // PERF-8: accept ScrollViewer AND ItemsControl. Feed-style
                // list pages attach the behavior to the ListBox/ListView,
                // not to its internal ScrollViewer; the old code dropped
                // those silently (scroll never got tuned, behavior was inert).
                switch (d)
                {
                    case ScrollViewer sv:
                        Attach(sv);
                        break;
                    case ItemsControl ic:
                        AttachItemsControl(ic);
                        break;
                }
            }
            else
            {
                switch (d)
                {
                    case ScrollViewer sv:
                        Detach(sv);
                        break;
                    case ItemsControl ic:
                        DetachItemsControl(ic);
                        break;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  ATTACH / DETACH
        // ═══════════════════════════════════════════════════════════════

        private static void AttachItemsControl(ItemsControl ic)
        {
            // Inner ScrollViewer exists only after the control template has
            // been applied, so resolve it on Loaded (before that the
            // attached property is inert — same as the old ScrollViewer path).
            ic.Loaded += OnItemsControlLoaded;
            ic.Unloaded -= OnItemsControlUnloaded;
            ic.Unloaded += OnItemsControlUnloaded;
        }

        private static void DetachItemsControl(ItemsControl ic)
        {
            ic.Loaded -= OnItemsControlLoaded;
            ic.Unloaded -= OnItemsControlUnloaded;

            if (_itemsToScroller.TryGetValue(ic, out var sv))
            {
                Detach(sv);
                _itemsToScroller.Remove(ic);
            }
        }

        private static void OnItemsControlLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ItemsControl ic) return;

            // Template is applied by now; resolve its internal ScrollViewer.
            var sv = FindDescendantScrollViewer(ic);
            if (sv == null)
            {
                // Exotic hosting (ItemsControl inside another template) can
                // fire Loaded before the template pass builds the scroller.
                // Retry once on the next dispatcher pass; native scrolling
                // remains active either way, so this is best-effort.
                if (_itemsToScroller.TryGetValue(ic, out _)) return;
                ic.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var again = FindDescendantScrollViewer(ic);
                    if (again == null || _states.TryGetValue(again, out _)) return;
                    _itemsToScroller.AddOrUpdate(ic, again);
                    Attach(again);
                }));
                return;
            }

            if (_states.TryGetValue(sv, out _)) return;

            _itemsToScroller.AddOrUpdate(ic, sv);
            Attach(sv);
        }

        private static void OnItemsControlUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ItemsControl ic) return;

            ic.Loaded -= OnItemsControlLoaded;
            ic.Unloaded -= OnItemsControlUnloaded;

            if (_itemsToScroller.TryGetValue(ic, out var sv))
            {
                Detach(sv);
                _itemsToScroller.Remove(ic);
            }
        }

        private static void Attach(ScrollViewer sv)
        {
            if (_states.TryGetValue(sv, out _)) return;

            _states.Add(sv, new ScrollState
            {
                // Best-effort at attach; re-probed on every wheel event
                // because the items panel is realized lazily.
                IsVirtualizedPixelList = IsVirtualizedPixelList(sv)
            });

            sv.PreviewMouseWheel += OnPreviewMouseWheel;
            // Unloaded detach keeps long-lived windows (MainWindow content
            // swaps DataTemplates) from accumulating handlers on dead views.
            sv.Unloaded -= OnScrollViewerUnloaded;
            sv.Unloaded += OnScrollViewerUnloaded;
        }

        private static void Detach(ScrollViewer sv)
        {
            if (!_states.TryGetValue(sv, out _)) return;

            sv.PreviewMouseWheel -= OnPreviewMouseWheel;
            sv.Unloaded -= OnScrollViewerUnloaded;

            _states.Remove(sv);
        }

        private static void OnScrollViewerUnloaded(
            object sender, RoutedEventArgs e)
        {
            if (sender is ScrollViewer sv)
                Detach(sv);
        }

        // ═══════════════════════════════════════════════════════════════
        //  MOUSE WHEEL HANDLER
        // ═══════════════════════════════════════════════════════════════

        private static void OnPreviewMouseWheel(
            object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            if (!_states.TryGetValue(sv, out var state)) return;

            // Passthrough for Ctrl+Wheel (zoom) / Shift+Wheel (horizontal)
            if (Keyboard.Modifiers != ModifierKeys.None) return;

            state.IsVirtualizedPixelList = IsVirtualizedPixelList(sv);

            // PERF-8: virtualized pixel lists scroll natively. The native
            // path honours the wheel per event and — crucially — does not
            // animate, so the panel never churns containers at frame rate.
            if (state.IsVirtualizedPixelList)
                return; // not handled → WPF ScrollViewer scrolls internally

            double currentOffset = sv.VerticalOffset;
            double maxOffset = sv.ScrollableHeight;

            // Nested-scroll etiquette: at the top/bottom boundary the wheel
            // must bubble to the OUTER scroller (this is what the dashboard
            // SmartScrollBubblingBehavior relies on). The old code handled
            // the event unconditionally, so a bounded inner scroller could
            // swallow the wheel and leave nested pages feeling "stuck".
            bool atTop = currentOffset <= 0.0;
            bool atBottom = currentOffset >= maxOffset - 0.5;
            bool scrollingUp = e.Delta > 0;
            bool scrollingDown = e.Delta < 0;
            if ((atTop && scrollingUp) || (atBottom && scrollingDown))
                return; // not handled → bubbles to the outer scroll surface

            double delta = Math.Abs(e.Delta) < PrecisionThreshold
                ? e.Delta * 1.2                 // precision touchpad
                : (e.Delta / 120.0) * WheelNotchPixels;

            double newOffset = currentOffset - delta;

            if (newOffset < 0) newOffset = 0;
            else if (newOffset > maxOffset) newOffset = maxOffset;

            // Single jump per wheel event — no animation clocks, no
            // continuous re-render while the wheel is idle.
            sv.ScrollToVerticalOffset(newOffset);
            e.Handled = true;
        }

        // ═══════════════════════════════════════════════════════════════
        //  VIRTUALIZATION PROBE
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// True when the ScrollViewer hosts an ItemsControl whose items panel
        /// virtualizes AND scrolls by pixel. With
        /// CanContentScroll=True + VirtualizingPanel.ScrollUnit=Pixel
        /// (the modern WPF combo) virtualization stays ON while WPF scrolls
        /// by pixel — the best of both worlds, and it must never be driven
        /// from an external render loop.
        /// </summary>
        private static bool IsVirtualizedPixelList(ScrollViewer sv)
        {
            var ic = FindHostingItemsControl(sv);
            if (ic == null) return false;

            try
            {
                if (!VirtualizingPanel.GetIsVirtualizing(ic))
                    return false;

                if (VirtualizingPanel.GetScrollUnit(ic) != ScrollUnit.Pixel)
                    return false;

                // Belt-and-braces: the panel must actually be a virtualizing
                // panel (ItemsPanel override could kill virtualization even
                // when the flags say otherwise).
                return FindDescendantVirtualizingPanel(ic) != null;
            }
            catch (System.Exception)
            {
                // Attached property unavailable on this element — treat as
                // non-virtualized; direct scroll is still correct.
                return false;
            }
        }

        /// <summary>
        /// Locates the ItemsControl whose scrolling this ScrollViewer serves.
        /// Two shapes exist in the codebase:
        ///   1. ListBox/ListView OWNS the scroller (behavior attached to the
        ///      items control) → the ItemsControl is an ANCESTOR of the
        ///      ScrollViewer.
        ///   2. The ScrollViewer HOSTS an ItemsControl (dashboard feed, chat
        ///      transcript) → it is a DESCENDANT of the ScrollViewer.
        /// </summary>
        private static ItemsControl? FindHostingItemsControl(ScrollViewer sv)
        {
            // Case 1 — ancestor chain (ListBox internal ScrollViewer).
            if (VisualTreeHelper.GetParent(sv) is DependencyObject parent)
            {
                while (parent != null)
                {
                    if (parent is ItemsControl ic)
                        return ic;
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }

            // Case 2 — descendant (ScrollViewer wrapping an ItemsControl).
            return FindDescendantItemsControl(sv);
        }

        // ═══════════════════════════════════════════════════════════════
        //  VISUAL TREE HELPERS
        // ═══════════════════════════════════════════════════════════════

        private static ScrollViewer? FindDescendantScrollViewer(DependencyObject parent)
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer sv)
                    return sv;

                var found = FindDescendantScrollViewer(child);
                if (found != null) return found;
            }
            return null;
        }

        private static ItemsControl? FindDescendantItemsControl(DependencyObject parent)
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ItemsControl ic)
                    return ic;

                var found = FindDescendantItemsControl(child);
                if (found != null) return found;
            }
            return null;
        }

        private static VirtualizingStackPanel? FindDescendantVirtualizingPanel(DependencyObject parent)
        {
            if (parent == null) return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is VirtualizingStackPanel vsp)
                    return vsp;

                var found = FindDescendantVirtualizingPanel(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
