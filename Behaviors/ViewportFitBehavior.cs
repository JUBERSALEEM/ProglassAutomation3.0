// Global adaptive viewport — NOT a Viewbox lock on the whole app.
//
// Root causes of the laptop / desktop mismatch:
//   1. Wrapping MainWindow in a 1440×900 Viewbox froze composition and
//      hid overflow instead of reflowing.
//   2. Dialogs with MinHeight=600 overflowed 768p work areas.
//   3. Density tokens existed but user zoom did not.
//
// This guard:
//   • caps every window to the live work area
//   • applies ThemeManager.UiScale as a LayoutTransform so Ctrl+Wheel
//     zoom shows MORE or LESS content (content size = client / scale)
//   • never locks a design canvas on the shell
//   • applies the zoom to the MAIN workspace window ONLY — modal
//     dialogs and secondary windows always render at their designed
//     size (scaling a fixed-size dialog's content makes it look
//     oversized and, with SizeToContent, collapses it)
// Splash is a poster: it uses a local Viewbox in SplashView.xaml only.

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using ProGlassAutomation.Services;
using ProGlassAutomation.Services.AI.AIConfig;
using ProGlassAutomation.Services.Viewport;

namespace ProGlassAutomation.Behaviors
{
    public static class ViewportFitBehavior
    {
        public static readonly DependencyProperty SkipProperty =
            DependencyProperty.RegisterAttached(
                "Skip", typeof(bool), typeof(ViewportFitBehavior),
                new PropertyMetadata(false));

        public static readonly DependencyProperty DisableZoomProperty =
            DependencyProperty.RegisterAttached(
                "DisableZoom", typeof(bool), typeof(ViewportFitBehavior),
                new PropertyMetadata(false));

        public static bool GetSkip(DependencyObject d) => (bool)d.GetValue(SkipProperty);
        public static void SetSkip(DependencyObject d, bool v) => d.SetValue(SkipProperty, v);
        public static bool GetDisableZoom(DependencyObject d) => (bool)d.GetValue(DisableZoomProperty);
        public static void SetDisableZoom(DependencyObject d, bool v) => d.SetValue(DisableZoomProperty, v);

        private static readonly DependencyProperty HookedProperty =
            DependencyProperty.RegisterAttached(
                "Hooked", typeof(bool), typeof(ViewportFitBehavior),
                new PropertyMetadata(false));

        private static bool _installed;

        // Phase 4: Named handlers for static events — detachable, no lambda leak
        private static EventHandler? _uiScaleHandler;
        private static EventHandler? _displaySettingsHandler;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnWindowLoaded));

            _uiScaleHandler = (_, _) => ApplyScaleToOpenWindows();
            ThemeManager.UiScaleChanged += _uiScaleHandler;

            try
            {
                _displaySettingsHandler = (_, _) =>
                {
                    var app = Application.Current;
                    if (app == null) return;
                    app.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        foreach (Window w in app.Windows)
                        {
                            try
                            {
                                ViewportService.CapWindowToWorkArea(w);
                                ApplyUserScale(w);
                            }
                            catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ViewportFitBehavior] Install: closing window: " + ex.Message); }
                        }
                    }));
                };
                SystemEvents.DisplaySettingsChanged += _displaySettingsHandler;
            }
            catch (System.Exception ex)
            {
                // Session 0 / no interactive desktop.
                ProGlassAutomation.Services.Logging.AppLogService.Debug("[ViewportFitBehavior] Display-settings subscription failed: " + ex.Message);
            }
        }

        public static void Uninstall()
        {
            if (!_installed) return;
            _installed = false;

            if (_uiScaleHandler != null)
            {
                try { ThemeManager.UiScaleChanged -= _uiScaleHandler; } catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ViewportFitBehavior] UiScaleChanged detach failed: " + ex.Message); }
                _uiScaleHandler = null;
            }
            if (_displaySettingsHandler != null)
            {
                try { SystemEvents.DisplaySettingsChanged -= _displaySettingsHandler; } catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ViewportFitBehavior] DisplaySettingsChanged detach failed: " + ex.Message); }
                _displaySettingsHandler = null;
            }
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(sender, e.OriginalSource)) return;
            if (sender is not Window window) return;
            Attach(window);
        }

        public static void Attach(Window window)
        {
            if (window == null) return;
            if (GetSkip(window)) return;

            ViewportService.CapWindowToWorkArea(window);

            if ((bool)window.GetValue(HookedProperty))
            {
                ApplyUserScale(window);
                return;
            }

            window.SetValue(HookedProperty, true);
            window.PreviewMouseWheel += OnPreviewMouseWheel;
            window.PreviewKeyDown += OnPreviewKeyDown;
            window.SizeChanged += (_, _) => ApplyUserScale(window);
            window.DpiChanged += (_, _) =>
            {
                ViewportService.CapWindowToWorkArea(window);
                ApplyUserScale(window);
            };

            ApplyUserScale(window);
        }

        public static void ApplyScaleToOpenWindows()
        {
            var app = Application.Current;
            if (app == null) return;
            foreach (Window w in app.Windows)
            {
                try { ApplyUserScale(w); }
                catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Debug("[ViewportFitBehavior] ApplyScaleToOpenWindows: closing: " + ex.Message); }
            }
        }

        public static void ApplyUserScale(Window window)
        {
            if (window == null) return;
            if (GetSkip(window) || GetDisableZoom(window)) return;
            if (window.Content is not FrameworkElement fe) return;
            if (string.Equals(window.GetType().Name, "SplashView", StringComparison.Ordinal))
                return;

            // Only the main workspace window zooms (Ctrl+Wheel). Modal
            // dialogs and secondary windows must ALWAYS render at their
            // designed size: scaling their content inside a fixed-size
            // window makes the dialog look oversized (and, with
            // SizeToContent, re-applying the transform on SizeChanged
            // chases the re-measure and makes the dialog collapse/jump).
            if (!ReferenceEquals(window, Application.Current?.MainWindow))
            {
                // Clear any transform a previous call may have applied
                // (e.g. a dialog opened while zoom was active).
                fe.ClearValue(FrameworkElement.WidthProperty);
                fe.ClearValue(FrameworkElement.HeightProperty);
                fe.LayoutTransform = Transform.Identity;
                return;
            }

            double scale = ThemeManager.UiScale;
            double availW = window.ActualWidth;
            double availH = window.ActualHeight;
            if (availW < 80 || availH < 80)
                return;

            if (Math.Abs(scale - 1.0) < 0.001)
            {
                fe.ClearValue(FrameworkElement.WidthProperty);
                fe.ClearValue(FrameworkElement.HeightProperty);
                fe.LayoutTransform = Transform.Identity;
                RestoreNativeCrispness(fe);
                return;
            }

            // Zoom out (0.85) → content is measured larger, then drawn
            // smaller, so more of the UI fits. Zoom in is the reverse.
            //
            // Crispness / HD-clarity fixes (root cause of the "blurry
            // zoom in / zoom out" report):
            //   1. The old code assigned a FRACTIONAL size
            //      (availW / scale → e.g. 1920 / 0.85 = 2258.82). Every
            //      glyph, hairline and border then lands on sub-pixel
            //      positions and WPF anti-aliases BOTH edges — soft,
            //      grey text. Rounding to a whole pixel lets layout snap
            //      back onto the pixel grid.
            //   2. The scale was used raw (floating drift across ticks).
            //      Snap it to a stable 2-decimal value so the transform
            //      never jitters between wheel notches.
            //   3. A scale transform makes WPF fall back from ClearType
            //      sub-pixel text to grey-scale AA. Re-assert HD render
            //      options on the scaled element (HighQuality bitmap
            //      sampling + Display/ClearType text + hinting off, so
            //      glyphs are not hinted for the *unscaled* grid and then
            //      distorted by the transform).
            double crispScale = Math.Round(scale, 2);
            fe.Width = Math.Round(availW / crispScale);
            fe.Height = Math.Round(availH / crispScale);
            fe.LayoutTransform = new ScaleTransform(crispScale, crispScale);
            ApplyScaledCrispness(fe);
        }

        /// <summary>
        /// Re-asserts HD rendering on the scaled root content. Without
        /// this, a LayoutTransform/ScaleTransform disables ClearType
        /// sub-pixel text (grey-scale AA) and can sample bitmaps with the
        /// lower-quality default filter.
        /// </summary>
        private static void ApplyScaledCrispness(FrameworkElement fe)
        {
            RenderOptions.SetBitmapScalingMode(fe, BitmapScalingMode.HighQuality);
            TextOptions.SetTextFormattingMode(fe, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(fe, TextRenderingMode.ClearType);
            // "Animated" = no per-glyph pixel-grid hinting. Hinting is
            // computed against the unscaled grid, so it would distort
            // glyphs once the scale transform is applied on top of it.
            TextOptions.SetTextHintingMode(fe, TextHintingMode.Animated);
        }

        /// <summary>
        /// At 100% zoom there is no transform, so the element should fall
        /// back to the native render options declared on the Window XAML
        /// (HighQuality / ClearType / Display / Fixed).
        /// </summary>
        private static void RestoreNativeCrispness(FrameworkElement fe)
        {
            fe.ClearValue(RenderOptions.BitmapScalingModeProperty);
            fe.ClearValue(TextOptions.TextFormattingModeProperty);
            fe.ClearValue(TextOptions.TextRenderingModeProperty);
            fe.ClearValue(TextOptions.TextHintingModeProperty);
        }

        private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not Window window) return;
            if (GetDisableZoom(window)) return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

            double next = ThemeManager.UiScale + (e.Delta > 0
                ? ViewportService.UiScaleStep
                : -ViewportService.UiScaleStep);
            CommitScale(next);
            e.Handled = true;
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                CommitScale(ViewportService.DefaultUiScale);
                e.Handled = true;
            }
            else if (e.Key == Key.OemPlus || e.Key == Key.Add)
            {
                CommitScale(ThemeManager.UiScale + ViewportService.UiScaleStep);
                e.Handled = true;
            }
            else if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                CommitScale(ThemeManager.UiScale - ViewportService.UiScaleStep);
                e.Handled = true;
            }
        }

        private static void CommitScale(double scale)
        {
            scale = ViewportService.ClampUiScale(scale);
            ThemeManager.ApplyUiScale(scale);

            try
            {
                if (Application.Current is App && App.Config?.Appearance != null)
                {
                    App.Config.Appearance.UiScale = scale;
                    ConfigService.Save(App.Config);
                }
            }
            catch (System.Exception ex)
            {
                // Zoom must still work if config is mid-load.
                ProGlassAutomation.Services.Logging.AppLogService.Debug("[ViewportFitBehavior] Zoom config persist failed: " + ex.Message);
            }
        }
    }
}
