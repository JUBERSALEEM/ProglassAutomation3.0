// ═══════════════════════════════════════════════════════════════════════
// FILE: Behaviors/RoundedEdgeStrokeBehavior.cs
// PROJECT: ProGlassAutomation
//
// WPF Border paints a thick rounded stroke as four side rectangles plus
// a corner arc. Aliased edge mode (and a thick arc whose radius is
// smaller than the stroke) drops that arc, so the straight edge takes
// the plate colour and the corner does not. This behavior forces the
// stroke to anti-alias, and when the edge is thick enough to gap it
// draws the corner as a filled ring in the same brush.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ProGlassAutomation.Behaviors
{
    /// <summary>
    /// Attached to card borders so a defined edge colour reaches the
    /// corner arc, not only the straight sides.
    /// </summary>
    public static class RoundedEdgeStrokeBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(RoundedEdgeStrokeBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value)
            => element.SetValue(IsEnabledProperty, value);

        public static bool GetIsEnabled(DependencyObject element)
            => (bool)element.GetValue(IsEnabledProperty);

        /// <summary>
        /// Live edge colour. When set, a thick ring is drawn from this brush
        /// and the native border stroke is hidden, so the card does not
        /// paint a second line over the first.
        /// </summary>
        public static readonly DependencyProperty EdgeBrushProperty =
            DependencyProperty.RegisterAttached(
                "EdgeBrush",
                typeof(Brush),
                typeof(RoundedEdgeStrokeBehavior),
                new PropertyMetadata(null, OnEdgeBrushChanged));

        public static void SetEdgeBrush(DependencyObject element, Brush? value)
            => element.SetValue(EdgeBrushProperty, value);

        public static Brush? GetEdgeBrush(DependencyObject element)
            => (Brush?)element.GetValue(EdgeBrushProperty);

        private static readonly DependencyPropertyDescriptor ThicknessDescriptor =
            DependencyPropertyDescriptor.FromProperty(Border.BorderThicknessProperty, typeof(Border));

        private static readonly DependencyPropertyDescriptor RadiusDescriptor =
            DependencyPropertyDescriptor.FromProperty(Border.CornerRadiusProperty, typeof(Border));

        private static readonly DependencyPropertyDescriptor BrushDescriptor =
            DependencyPropertyDescriptor.FromProperty(Border.BorderBrushProperty, typeof(Border));

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Border, StrokeState> States = new();

        private sealed class StrokeState
        {
            public RoundedStrokeAdorner? Adorner;
            public bool Listening;
            public bool WatchingSize;
            public bool Updating;
            public bool NativeStrokeSuppressed;
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Border border) return;
            if (e.NewValue is true)
            {
                // Local value beats a style setter of EdgeMode=Aliased.
                // Aliased raster drops the partial-coverage corner pixels,
                // which is why the sides took the colour and the arc did not.
                RenderOptions.SetEdgeMode(border, EdgeMode.Unspecified);
                border.Loaded += OnLoaded;
                border.Unloaded += OnUnloaded;
                if (border.IsLoaded) Attach(border);
            }
            else
            {
                border.Loaded -= OnLoaded;
                border.Unloaded -= OnUnloaded;
                Detach(border);
            }
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border) Attach(border);
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is Border border) Detach(border);
        }

        private static void Attach(Border border)
        {
            if (!States.TryGetValue(border, out var state))
            {
                state = new StrokeState();
                States.Add(border, state);
            }

            if (!state.Listening)
            {
                ThicknessDescriptor.AddValueChanged(border, OnChromeChanged);
                RadiusDescriptor.AddValueChanged(border, OnChromeChanged);
                BrushDescriptor.AddValueChanged(border, OnChromeChanged);
                state.Listening = true;
            }

            UpdateAdorner(border, state);
        }

        private static void Detach(Border border)
        {
            if (!States.TryGetValue(border, out var state)) return;
            if (state.Listening)
            {
                ThicknessDescriptor.RemoveValueChanged(border, OnChromeChanged);
                RadiusDescriptor.RemoveValueChanged(border, OnChromeChanged);
                BrushDescriptor.RemoveValueChanged(border, OnChromeChanged);
                state.Listening = false;
            }

            UnwatchSize(border, state);
            RemoveAdorner(border, state);
            RestoreNativeStroke(border, state);
        }

        private static void RestoreNativeStroke(Border border, StrokeState state)
        {
            if (!state.NativeStrokeSuppressed) return;
            border.ClearValue(Border.BorderBrushProperty);
            state.NativeStrokeSuppressed = false;
        }

        private static void OnEdgeBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Border border && States.TryGetValue(border, out var state))
                UpdateAdorner(border, state);
        }

        private static void OnChromeChanged(object? sender, EventArgs e)
        {
            if (sender is Border border && States.TryGetValue(border, out var state))
                UpdateAdorner(border, state);
        }

        private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Border border
                && States.TryGetValue(border, out var state)
                && state.Adorner != null)
            {
                state.Adorner.InvalidateVisual();
            }
        }

        private static void UpdateAdorner(Border border, StrokeState state)
        {
            if (state.Updating) return;
            state.Updating = true;
            try
            {
                UpdateAdornerCore(border, state);
            }
            finally
            {
                state.Updating = false;
            }
        }

        private static void UpdateAdornerCore(Border border, StrokeState state)
        {
            // Border.BorderThickness is a typed CLR wrapper over a dependency property.
            // A malformed DynamicResource/TemplateBinding can leave a boxed Double in
            // the property store; reading border.BorderThickness then throws
            // InvalidCastException and can take down the entire WPF dispatcher.
            // Inspect the raw value first and repair this one border before layout
            // reads the typed wrapper. Valid Thickness values retain their binding.
            var rawThickness = border.GetValue(Border.BorderThicknessProperty);
            Thickness thickness;
            if (rawThickness is Thickness validThickness)
            {
                thickness = validThickness;
            }
            else
            {
                double fallback = rawThickness is double numericThickness
                    && double.IsFinite(numericThickness)
                    ? Math.Max(0.0, Math.Min(100.0, numericThickness))
                    : 1.0;
                thickness = new Thickness(fallback);

                // Replace the invalid expression/value with a safe typed value.
                // This is intentionally local to the affected Border; all valid
                // theme resources and other controls remain untouched.
                border.SetValue(Border.BorderThicknessProperty, thickness);
                System.Diagnostics.Trace.TraceError(
                    $"RoundedEdgeStrokeBehavior repaired invalid BorderThickness value " +
                    $"'{rawThickness ?? "<null>"}' ({rawThickness?.GetType().FullName ?? "null"}).");
            }

            bool uniform = Nearly(thickness.Left, thickness.Top)
                && Nearly(thickness.Left, thickness.Right)
                && Nearly(thickness.Left, thickness.Bottom);
            var edgeBrush = GetEdgeBrush(border);
            // Hairline stays the native stroke (no extra visual). A defined
            // edge is 2–100 DIP; that is where the arc gaps, so only then
            // is the ring worth an adorner. EdgeBrush lets the native stroke
            // be hidden without losing live colour. Cards that have not set
            // it still get the corner fill on top of the native stroke.
            bool hasBrush = edgeBrush != null
                || (!state.NativeStrokeSuppressed && border.BorderBrush != null);
            bool needsRing = uniform
                && thickness.Left >= 2.0
                && border.CornerRadius.TopLeft > 0.5
                && hasBrush;
            if (!needsRing)
            {
                RestoreNativeStroke(border, state);
                UnwatchSize(border, state);
                RemoveAdorner(border, state);
                return;
            }

            var layer = AdornerLayer.GetAdornerLayer(border);
            if (layer == null) return;

            if (edgeBrush != null && !state.NativeStrokeSuppressed)
            {
                // Hide the native stroke only once the ring can paint.
                // ClearValue restores the style setter on a hairline.
                border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
                state.NativeStrokeSuppressed = true;
            }

            if (state.Adorner == null)
            {
                state.Adorner = new RoundedStrokeAdorner(border);
                layer.Add(state.Adorner);
                border.SizeChanged += OnSizeChanged;
                state.WatchingSize = true;
            }
            else
            {
                state.Adorner.InvalidateVisual();
            }
        }

        private static void UnwatchSize(Border border, StrokeState state)
        {
            if (!state.WatchingSize) return;
            border.SizeChanged -= OnSizeChanged;
            state.WatchingSize = false;
        }

        private static void RemoveAdorner(Border border, StrokeState state)
        {
            if (state.Adorner == null) return;
            AdornerLayer.GetAdornerLayer(border)?.Remove(state.Adorner);
            state.Adorner = null;
        }

        private static bool Nearly(double a, double b) => Math.Abs(a - b) < 0.05;

        private sealed class RoundedStrokeAdorner : Adorner
        {
            public RoundedStrokeAdorner(Border border) : base(border)
            {
                IsHitTestVisible = false;
                RenderOptions.SetEdgeMode(this, EdgeMode.Unspecified);
                SnapsToDevicePixels = false;
            }

            protected override void OnRender(DrawingContext dc)
            {
                if (AdornedElement is not Border border) return;
                double width = border.ActualWidth;
                double height = border.ActualHeight;
                if (width < 2 || height < 2) return;
                var brush = GetEdgeBrush(border) ?? border.BorderBrush;
                if (brush == null) return;
                double thick = border.BorderThickness.Left;
                if (thick < 0.5) return;

                double radius = border.CornerRadius.TopLeft;
                var outer = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
                double innerRadius = Math.Max(0, radius - thick);
                var inner = new RectangleGeometry(
                    new Rect(thick, thick, Math.Max(0, width - thick * 2), Math.Max(0, height - thick * 2)),
                    innerRadius,
                    innerRadius);
                // Clip to the card. An unclipped ring on the window adorner
                // layer paints over the next module when the edge is thick.
                dc.PushClip(outer);
                dc.DrawGeometry(brush, null, new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner));
                dc.Pop();
            }
        }
    }
}
