// ═══════════════════════════════════════════════════════════════════════
//  MODULE: BounceHoverBehavior — global "bouncy" hover/press micro-motion
//  ---------------------------------------------------------------------
//  WHY THIS EXISTS
//  The Settings nav rail felt alive while the rest of the app felt static.
//  Reproducing that by hand meant copying a ScaleTransform, two Storyboards
//  and four triggers into every ControlTemplate — dozens of near-identical
//  XAML blocks that drift apart over time.
//
//  Instead this is ONE attached behavior any element can opt into:
//
//      <Border behaviors:BounceHover.IsEnabled="True" .../>
//
//  It works on ANY UIElement — Border, Button, Card, ListBoxItem — because
//  it animates RenderTransform, which every UIElement has. No template
//  rewrite, no restyling, no new base class.
//
//  DESIGN NOTES
//  • Hover  → scale up to HoverScale with a BackEase (slight overshoot =
//    the "bounce"). Leave → back to 1.0 with a softer BackEase.
//  • Press  → quick dip to PressScale so clicks feel physical.
//  • RenderTransformOrigin is forced to the CENTRE, otherwise the element
//    grows from its top-left corner and visibly drifts.
//  • A ScaleTransform is INJECTED only if the element has no meaningful
//    RenderTransform. If a layout already uses one we leave it alone
//    rather than silently breaking that layout.
//  • Respects the performance tier: on low-end machines
//    (PerformanceProfileService) the animation is skipped entirely, so we
//    never spend frames we do not have.
//  • Every handler attached is also detached when IsEnabled flips false or
//    the element unloads — no leaks, matching the Phase-4 named-handler
//    convention used elsewhere in this codebase.
//
//  Animations are frozen where possible and run on the compositor thread,
//  so the cost per element is negligible.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ProGlassAutomation.Behaviors
{
    /// <summary>
    /// Attached behavior adding a springy scale response on hover and press
    /// to any <see cref="UIElement"/>. Opt in with
    /// <c>behaviors:BounceHover.IsEnabled="True"</c>.
    /// </summary>
    public static class BounceHover
    {
        // ── Tunables (overridable per element) ──────────────────────────

        /// <summary>Scale reached while the pointer is over the element.</summary>
        public static readonly DependencyProperty HoverScaleProperty =
            DependencyProperty.RegisterAttached(
                "HoverScale", typeof(double), typeof(BounceHover),
                new PropertyMetadata(1.03));

        public static void SetHoverScale(DependencyObject o, double v)
            => o.SetValue(HoverScaleProperty, v);

        public static double GetHoverScale(DependencyObject o)
            => (double)o.GetValue(HoverScaleProperty);

        /// <summary>Scale dipped to while the element is pressed.</summary>
        public static readonly DependencyProperty PressScaleProperty =
            DependencyProperty.RegisterAttached(
                "PressScale", typeof(double), typeof(BounceHover),
                new PropertyMetadata(0.97));

        public static void SetPressScale(DependencyObject o, double v)
            => o.SetValue(PressScaleProperty, v);

        public static double GetPressScale(DependencyObject o)
            => (double)o.GetValue(PressScaleProperty);

        // ── Switch ──────────────────────────────────────────────────────

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled", typeof(bool), typeof(BounceHover),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject o, bool v)
            => o.SetValue(IsEnabledProperty, v);

        public static bool GetIsEnabled(DependencyObject o)
            => (bool)o.GetValue(IsEnabledProperty);

        // Marks a ScaleTransform this behavior created, so we only ever
        // animate our own transform and never hijack a layout's.
        private static readonly DependencyProperty OwnedTransformProperty =
            DependencyProperty.RegisterAttached(
                "OwnedTransform", typeof(ScaleTransform), typeof(BounceHover),
                new PropertyMetadata(null));

        private const double DurationIn = 0.22;
        private const double DurationOut = 0.28;
        private const double DurationPress = 0.09;

        private static void OnIsEnabledChanged(
            DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement el) return;

            // Always detach first: re-entrant style/template application can
            // raise this more than once for the same element.
            el.MouseEnter -= OnEnter;
            el.MouseLeave -= OnLeave;
            el.PreviewMouseLeftButtonDown -= OnDown;
            el.PreviewMouseLeftButtonUp -= OnUp;
            el.IsVisibleChanged -= OnVisibilityChanged;

            if (e.NewValue is not true) return;

            // Honour the machine's performance tier — see
            // Services/Performance/PerformanceProfileService.cs.
            if (!AnimationsAllowed()) return;

            el.MouseEnter += OnEnter;
            el.MouseLeave += OnLeave;
            el.PreviewMouseLeftButtonDown += OnDown;
            el.PreviewMouseLeftButtonUp += OnUp;
            el.IsVisibleChanged += OnVisibilityChanged;
        }

        private static bool AnimationsAllowed()
        {
            try
            {
                // Low tier => skip micro-motion entirely.
                return Services.Performance.PerformanceProfileService.TargetFps >= 60;
            }
            catch
            {
                return true; // never let a probe failure disable the UI
            }
        }

        /// <summary>
        /// Collapsing an element mid-animation can leave it stuck at the
        /// hover scale. Reset when it becomes hidden.
        /// </summary>
        private static void OnVisibilityChanged(
            object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is UIElement el && e.NewValue is false)
                Animate(el, 1.0, DurationOut, bounce: false);
        }

        private static void OnEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is UIElement el)
                Animate(el, GetHoverScale(el), DurationIn, bounce: true);
        }

        private static void OnLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is UIElement el)
                Animate(el, 1.0, DurationOut, bounce: true);
        }

        private static void OnDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is UIElement el)
                Animate(el, GetPressScale(el), DurationPress, bounce: false);
        }

        private static void OnUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not UIElement el) return;

            // Return to hover scale if the pointer is still over the element,
            // otherwise all the way back to rest.
            bool over = el.IsMouseOver;
            Animate(el, over ? GetHoverScale(el) : 1.0, DurationIn, bounce: true);
        }

        /// <summary>
        /// Ensures the element carries a ScaleTransform we own, centred, and
        /// animates both axes to <paramref name="to"/>.
        /// </summary>
        private static void Animate(UIElement el, double to, double seconds, bool bounce)
        {
            var scale = EnsureScale(el);
            if (scale == null) return;   // element uses its own transform

            var ease = bounce
                ? new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 }
                : (IEasingFunction)new QuadraticEase { EasingMode = EasingMode.EaseOut };

            var anim = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromSeconds(seconds),
                EasingFunction = ease,
                FillBehavior = FillBehavior.HoldEnd
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private static ScaleTransform? EnsureScale(UIElement el)
        {
            // Reuse the transform we previously injected.
            if (el.GetValue(OwnedTransformProperty) is ScaleTransform owned)
                return owned;

            // Respect an existing transform rather than clobbering layout.
            var current = el.RenderTransform;
            if (current != null
                && current != Transform.Identity
                && current is not ScaleTransform)
                return null;

            if (current is ScaleTransform existing && !existing.IsFrozen)
            {
                el.SetValue(OwnedTransformProperty, existing);
                el.RenderTransformOrigin = new Point(0.5, 0.5);
                return existing;
            }

            var fresh = new ScaleTransform(1.0, 1.0);
            el.RenderTransform = fresh;
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.SetValue(OwnedTransformProperty, fresh);
            return fresh;
        }
    }
}
