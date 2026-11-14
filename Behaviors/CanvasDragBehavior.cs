// ═══════════════════════════════════════════════════════════════════════
// FILE: Behaviors/CanvasDragBehavior.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 2.5 — MVVM leakage sweep. Makes an element drag a panel inside a
// Canvas (floating chat window, HUD, …) without code-behind:
//
//     <Border behaviors:CanvasDragBehavior.Panel="{Binding ElementName=ChatPanel}"
//                     behaviors:CanvasDragBehavior.Canvas="{Binding ElementName=ChatCanvas}"
//                     behaviors:CanvasDragBehavior.ShadowResourceKey="PanelShadow"/>
//
// Mirrors the previous AiChatView code-behind drag exactly: mouse capture
// on the header, clamped canvas repositioning, and effect (drop shadow)
// restored on release.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ProGlassAutomation.Services.Logging;

namespace ProGlassAutomation.Behaviors
{
    public static class CanvasDragBehavior
    {
        public static readonly DependencyProperty PanelProperty =
            DependencyProperty.RegisterAttached(
                "Panel", typeof(FrameworkElement), typeof(CanvasDragBehavior),
                new PropertyMetadata(null, OnDragPropertyChanged));

        public static readonly DependencyProperty CanvasProperty =
            DependencyProperty.RegisterAttached(
                "Canvas", typeof(Canvas), typeof(CanvasDragBehavior),
                new PropertyMetadata(null, OnDragPropertyChanged));

        /// <summary>
        /// Resource key of the drop-shadow effect to restore on the panel
        /// when the drag ends (the effect is cleared while dragging).
        /// </summary>
        public static readonly DependencyProperty ShadowResourceKeyProperty =
            DependencyProperty.RegisterAttached(
                "ShadowResourceKey", typeof(string), typeof(CanvasDragBehavior),
                new PropertyMetadata("PanelShadow"));

        public static void SetPanel(DependencyObject d, FrameworkElement? value) => d.SetValue(PanelProperty, value);
        public static FrameworkElement? GetPanel(DependencyObject d) => (FrameworkElement?)d.GetValue(PanelProperty);

        public static void SetCanvas(DependencyObject d, Canvas? value) => d.SetValue(CanvasProperty, value);
        public static Canvas? GetCanvas(DependencyObject d) => (Canvas?)d.GetValue(CanvasProperty);

        public static void SetShadowResourceKey(DependencyObject d, string? value) => d.SetValue(ShadowResourceKeyProperty, value);
        public static string? GetShadowResourceKey(DependencyObject d) => (string?)d.GetValue(ShadowResourceKeyProperty);

        private static void OnDragPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element) return;

            element.RemoveHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMouseLeftButtonDown));
            element.RemoveHandler(UIElement.MouseMoveEvent, new MouseEventHandler(OnMouseMove));
            element.RemoveHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnMouseLeftButtonUp));
            element.RemoveHandler(UIElement.LostMouseCaptureEvent, new MouseEventHandler(OnLostMouseCapture));

            if (GetPanel(element) != null || GetCanvas(element) != null)
            {
                element.AddHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMouseLeftButtonDown));
                element.AddHandler(UIElement.MouseMoveEvent, new MouseEventHandler(OnMouseMove));
                element.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnMouseLeftButtonUp));
                element.AddHandler(UIElement.LostMouseCaptureEvent, new MouseEventHandler(OnLostMouseCapture));
            }
        }

        private sealed class DragState
        {
            public bool Dragging;
            public Point StartMouse;
            public Point StartOffset;
            public Point LastApplied;
        }

        private static readonly ConditionalWeakTable<UIElement, DragState> States = new();

        private static DragState GetState(UIElement header)
            => States.GetValue(header, _ => new DragState());

        private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not UIElement header) return;
            if (IsInsideButton(e.OriginalSource as DependencyObject)) return;

            var canvas = GetCanvas(header);
            var panel = GetPanel(header);
            if (canvas == null || panel == null) return;

            var state = GetState(header);
            state.Dragging = true;
            state.StartMouse = e.GetPosition(canvas);

            double left = Canvas.GetLeft(panel);
            double top = Canvas.GetTop(panel);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            state.StartOffset = new Point(left, top);
            state.LastApplied = state.StartOffset;

            panel.ClearValue(UIElement.EffectProperty);
            header.CaptureMouse();
            e.Handled = true;
        }

        private static void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not UIElement header || !header.IsMouseCaptured) return;
            var state = GetState(header);
            if (!state.Dragging) return;

            var canvas = GetCanvas(header);
            var panel = GetPanel(header);
            if (canvas == null || panel == null) return;

            var mouse = e.GetPosition(canvas);
            double left = state.StartOffset.X + (mouse.X - state.StartMouse.X);
            double top = state.StartOffset.Y + (mouse.Y - state.StartMouse.Y);

            double maxLeft = Math.Max(0, canvas.ActualWidth - 120);
            double maxTop = Math.Max(0, canvas.ActualHeight - 48);
            double minLeft = -(panel.ActualWidth - 120);
            double minTop = 0;

            left = Math.Clamp(left, minLeft, maxLeft);
            top = Math.Clamp(top, minTop, maxTop);

            if (Math.Abs(left - state.LastApplied.X) < 1 &&
                Math.Abs(top - state.LastApplied.Y) < 1) return;

            state.LastApplied = new Point(left, top);
            Canvas.SetLeft(panel, left);
            Canvas.SetTop(panel, top);
        }

        private static void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            if (sender is not UIElement header) return;
            var state = GetState(header);
            if (state.Dragging)
                EndDrag(header, state);
        }

        private static void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not UIElement header) return;
            var state = GetState(header);
            if (!state.Dragging) return;
            EndDrag(header, state);
            e.Handled = true;
        }

        private static void EndDrag(UIElement header, DragState state)
        {
            state.Dragging = false;

            if (header.IsMouseCaptured)
                header.ReleaseMouseCapture();

            var panel = GetPanel(header);
            if (panel != null)
            {
                var key = GetShadowResourceKey(header) ?? "PanelShadow";
                panel.SetResourceReference(UIElement.EffectProperty, key);
            }
        }

        private static bool IsInsideButton(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is ButtonBase) return true;

                DependencyObject? parent = null;
                try { parent = VisualTreeHelper.GetParent(d); }
                catch (Exception ex)
                {
                    AppLogService.Debug("[CanvasDragBehavior] IsInsideButton: template boundary: " + ex.Message);
                }
                if (parent == null && d is FrameworkElement fe && fe.TemplatedParent != null)
                    parent = fe.TemplatedParent;
                d = parent;
            }
            return false;
        }
    }
}
