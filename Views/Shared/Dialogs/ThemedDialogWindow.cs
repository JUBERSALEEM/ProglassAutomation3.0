// ═══════════════════════════════════════════════════════════════════════
// FILE: Views/Shared/Dialogs/ThemedDialogWindow.cs
// PROJECT: ProGlassAutomation
// MODULE: Unified dialog platform (2026-09 senior dialog audit)
//
// THE one base class for every modal popup / dialog in the app.
//
// WHY THIS EXISTS
// ───────────────
// Before this class every dialog hand-rolled its own chrome: a transparent
// window + shadow-margin Border + icon/title header + button footer, each
// with different margins, icon sizes, shadows and sizing rules (~28
// copies). That drift is what produced clipped text, too-small windows,
// misaligned cards and inconsistent close/keyboard behaviour. All of that
// now lives in ONE chrome template (Themes/Styles/DialogStyles.xaml) and
// ONE base class (this file).
//
// WHAT DERIVED DIALOGS GET FOR FREE
// ─────────────────────────────────
//   • Borderless glass card with outline + elevation shadow (theme-aware).
//   • Header: 42px icon badge (DialogIcon + DialogIconBrush), title that
//     WRAPS (never clips localized strings), optional subtitle, and a
//     themed close button (tooltip = Action.Close).
//   • Footer: assign FooterContent (e.g. the button StackPanel) — rendered
//     in the shared footer strip, right-aligned.
//   • Sizing that cannot clip: SizeToContent=Height by default, clamped to
//     the monitor WORK AREA (90% H / 94% W) on source-init and after
//     render; the body scrolls when the clamp engages.
//   • Drag-to-move via the header, Esc-to-close (combo-dropdown safe),
//     CenterOwner, ShowInTaskbar=False, consistent TextOptions/rounding.
//   • Smooth body scrolling via SmoothScrollBehavior.
//
// HOW TO WRITE A DIALOG (see docs/DIALOG_SYSTEM_AUDIT.md)
// ────────────────────────────────────────────────────────────────────────
//   <dialogs:ThemedDialogWindow x:Class="..." xmlns:dialogs="clr-namespace:ProGlassAutomation.Views.Shared.Dialogs"
//       Title="…" DialogIcon="&#xE7BA;" DialogIconBrush="{DynamicResource DangerBrush}"
//       Width="520" DialogSubtitle="{Binding DocumentLabel}">
//       <Grid> …content… </Grid>
//       <dialogs:ThemedDialogWindow.FooterContent>
//           <StackPanel Orientation="Horizontal" HorizontalAlignment="Right"> …buttons… </StackPanel>
//       </dialogs:ThemedDialogWindow.FooterContent>
//   </dialogs:ThemedDialogWindow>
//
//   …and the code-behind derives from ThemedDialogWindow instead of Window.
//   Resizable dialogs set Height/MinHeight + ResizeMode="CanResize" in XAML
//   (attribute wins over the base ctor default, as usual in WPF).
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace ProGlassAutomation.Views.Shared.Dialogs
{
    public class ThemedDialogWindow : Window
    {
        // Chrome template part names (see DialogStyles.xaml).
        private const string PartTitleBar = "PART_TitleBar";
        private const string PartCloseButton = "PART_CloseButton";
        private const string PartContentScroll = "PART_ContentScroll";

        private Border? _titleBar;
        private Button? _closeButton;
        private ScrollViewer? _contentScroll;

        static ThemedDialogWindow()
        {
            // Derived dialog classes (VoidReasonDialog, …) must resolve the
            // SAME default style key as this base type so the shared chrome
            // template applies to every subclass with zero XAML ceremony.
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ThemedDialogWindow),
                new FrameworkPropertyMetadata(typeof(ThemedDialogWindow)));
        }

        public ThemedDialogWindow()
        {
            // ── Borderless glass chrome ────────────────────────────────
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;

            // ── Sizing that cannot clip text ───────────────────────────
            // Content-driven height; hard work-area clamps are applied in
            // OnSourceInitialized / OnContentRendered.
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            MinWidth = 420;
            MinHeight = 160;
            MaxWidth = 980;

            // ── Modal-dialog conventions ───────────────────────────────
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            ShowActivated = true;

            // ── Typography / rendering parity with the shell ───────────
            SetResourceReference(FontFamilyProperty, "AppFontFamily");
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            // Default icon tint (dialogs override with a local value).
            SetResourceReference(DialogIconBrushProperty, "AccentBrush");
            // Body padding token — override per-dialog when a layout needs it.
            SetResourceReference(DialogContentPaddingProperty, "Dialog.ContentPadding");

            // Borderless windows still need edge-resize hit-testing when a
            // dialog opts into ResizeMode=CanResize. The band sits inside
            // the transparent shadow margin, so it never fights content.
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(16),
                CornerRadius = default,
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false
            });
        }

        // ─────────────────────────────────────────────────────────────────
        //  SHARED CHROME ATTACHMENT
        // ─────────────────────────────────────────────────────────────────

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);

            // DefaultStyleKey normally resolves the chrome, but subclass
            // windows load through generated InitializeComponent paths that
            // can bypass theme lookups (tests, plug-in hosts). Assigning the
            // typed style explicitly makes the shared chrome deterministic:
            // if the resource is missing (e.g. the crash-report dialog shown
            // before App resources exist), fall back to the stock OS frame —
            // degraded looks, never broken behaviour.
            if (Application.Current != null &&
                Application.Current.Resources != null &&
                Application.Current.Resources.Contains(typeof(ThemedDialogWindow)) &&
                Application.Current.Resources[typeof(ThemedDialogWindow)] is Style chrome)
            {
                Style = chrome;
            }
            else
            {
                // Degraded mode: stock OS frame. The WindowChrome attached in
                // the constructor must go too, or the caption area would be
                // swallowed (CaptionHeight=0) and the dialog undraggable.
                WindowStyle = WindowStyle.SingleBorderWindow;
                AllowsTransparency = false;
                WindowChrome.SetWindowChrome(this, null);
                if (Background == System.Windows.Media.Brushes.Transparent)
                    Background = System.Windows.Media.Brushes.White;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  DIALOG EXTENSIBILITY PROPERTIES (consumed by the chrome template)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Segoe MDL2 glyph shown in the header badge ("", empty = no badge).</summary>
        public static readonly DependencyProperty DialogIconProperty = DependencyProperty.Register(
            nameof(DialogIcon), typeof(string), typeof(ThemedDialogWindow),
            new FrameworkPropertyMetadata(string.Empty));

        public string DialogIcon
        {
            get => (string)GetValue(DialogIconProperty);
            set => SetValue(DialogIconProperty, value);
        }

        /// <summary>Brush for the header glyph (e.g. DangerBrush / WarningBrush / AccentBrush).</summary>
        public static readonly DependencyProperty DialogIconBrushProperty = DependencyProperty.Register(
            nameof(DialogIconBrush), typeof(Brush), typeof(ThemedDialogWindow),
            new FrameworkPropertyMetadata(null));

        public Brush? DialogIconBrush
        {
            get => (Brush?)GetValue(DialogIconBrushProperty);
            set => SetValue(DialogIconBrushProperty, value);
        }

        /// <summary>Optional one-line context under the title (wraps, never clips).</summary>
        public static readonly DependencyProperty DialogSubtitleProperty = DependencyProperty.Register(
            nameof(DialogSubtitle), typeof(string), typeof(ThemedDialogWindow),
            new FrameworkPropertyMetadata(string.Empty));

        public string DialogSubtitle
        {
            get => (string)GetValue(DialogSubtitleProperty);
            set => SetValue(DialogSubtitleProperty, value);
        }

        /// <summary>Buttons/actions row rendered in the shared footer strip; null = no footer.</summary>
        public static readonly DependencyProperty FooterContentProperty = DependencyProperty.Register(
            nameof(FooterContent), typeof(object), typeof(ThemedDialogWindow),
            new FrameworkPropertyMetadata(null));

        public object? FooterContent
        {
            get => GetValue(FooterContentProperty);
            set => SetValue(FooterContentProperty, value);
        }

        /// <summary>Show the header ✕ close button (default true).</summary>
        public static readonly DependencyProperty ShowCloseButtonProperty = DependencyProperty.Register(
            nameof(ShowCloseButton), typeof(bool), typeof(ThemedDialogWindow),
            new FrameworkPropertyMetadata(true));

        public bool ShowCloseButton
        {
            get => (bool)GetValue(ShowCloseButtonProperty);
            set => SetValue(ShowCloseButtonProperty, value);
        }

        /// <summary>Padding around the dialog body content (token Dialog.ContentPadding).</summary>
        public static readonly DependencyProperty DialogContentPaddingProperty = DependencyProperty.Register(
            nameof(DialogContentPadding), typeof(Thickness), typeof(ThemedDialogWindow),
            new FrameworkPropertyMetadata(new Thickness(24)));

        public Thickness DialogContentPadding
        {
            get => (Thickness)GetValue(DialogContentPaddingProperty);
            set => SetValue(DialogContentPaddingProperty, value);
        }

        /// <summary>Work-area height share a dialog may occupy (guarded against tiny screens).</summary>
        public double MaxHeightRatio { get; set; } = 0.90;

        // ─────────────────────────────────────────────────────────────────
        //  TEMPLATE WIRING
        // ─────────────────────────────────────────────────────────────────

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _titleBar = GetTemplateChild(PartTitleBar) as Border;
            _closeButton = GetTemplateChild(PartCloseButton) as Button;
            _contentScroll = GetTemplateChild(PartContentScroll) as ScrollViewer;

            if (_titleBar != null)
            {
                _titleBar.MouseLeftButtonDown += OnTitleBarMouseLeftButtonDown;
            }

            if (_closeButton != null)
            {
                _closeButton.Click += (_, __) => Close();
            }
        }

        private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Left-drag anywhere on the header moves the dialog; ignore
            // clicks that landed on interactive children (close button).
            if (e.OriginalSource is DependencyObject source &&
                _closeButton != null &&
                IsDescendantOf(source, _closeButton))
            {
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("if suppressed");
            }
            }
        }

        private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
        {
            while (node != null)
            {
                if (ReferenceEquals(node, ancestor))
                    return true;
                node = System.Windows.Media.VisualTreeHelper.GetParent(node);
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        //  KEYBOARD + SIZING CONTRACTS
        // ─────────────────────────────────────────────────────────────────

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // Bubbling phase: an open ComboBox/autocomplete dropdown consumes
            // Escape itself (e.Handled), so we only close when it is safe.
            if (e.Key == Key.Escape && !e.Handled)
            {
                e.Handled = true;
                Close();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ClampToWorkArea();
        }

        protected override void OnContentRendered(EventArgs e)
        {
            // Re-clamp after the final layout pass: localized text may have
            // re-measured the window larger than the monitor.
            base.OnContentRendered(e);
            ClampToWorkArea();
            PlayEntrance();
        }

        /// <summary>
        /// Scale-and-fade entrance: the dialog grows past its final size and
        /// settles back, which is what reads as a "pop" rather than a fade.
        ///
        /// RenderTransform, not LayoutTransform — a layout transform would
        /// re-measure the window every frame and fight SizeToContent.
        ///
        /// Everything is set up HERE, never in the constructor. A dialog whose
        /// OnContentRendered does not run must stay a plain visible window;
        /// the one thing this must never do is leave a dialog at Opacity 0
        /// with no animation running to bring it back.
        /// </summary>
        private void PlayEntrance()
        {
            // Honour the Windows "animate controls and elements" setting.
            // Skipping here also skips the Opacity = 0 below, so a machine
            // with animation off can never end up with an invisible dialog.
            if (!SystemParameters.ClientAreaAnimation) return;

            const double from = 0.88;      // start smaller than final
            const double overshoot = 1.03; // the bounce
            var total = TimeSpan.FromMilliseconds(260);
            var peak = TimeSpan.FromMilliseconds(165);

            RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = new ScaleTransform(1, 1); // base = settled, so a
            RenderTransform = scale;              // stopped animation is safe

            var overshootEase = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
            var settleEase = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            {
                var anim = new DoubleAnimationUsingKeyFrames();
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(overshoot, KeyTime.FromTimeSpan(peak)) { EasingFunction = overshootEase });
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(total)) { EasingFunction = settleEase });
                scale.BeginAnimation(prop, anim);
            }

            Opacity = 0;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            // Belt and braces: however the animation ends, the dialog ends up
            // fully opaque with no animation still holding the property.
            fade.Completed += (_, _) =>
            {
                BeginAnimation(UIElement.OpacityProperty, null);
                Opacity = 1;
            };
            BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// <summary>
        /// Guarantees the dialog fits the current monitor: height ≤ 90% of
        /// the work area, width ≤ 94%. When the clamp engages the chrome's
        /// body ScrollViewer keeps everything reachable.
        /// </summary>
        private void ClampToWorkArea()
        {
            double workW = SystemParameters.WorkArea.Width;
            double workH = SystemParameters.WorkArea.Height;
            if (workW <= 0 || workH <= 0)
                return;

            double maxH = Math.Max(240, workH * MaxHeightRatio);
            double maxW = Math.Max(320, workW * 0.94);

            if (MaxHeight > maxH || ActualHeight > maxH)
            {
                MaxHeight = maxH;
                if (ActualHeight > maxH)
                    Height = maxH;
            }

            if (MaxWidth > maxW)
                MaxWidth = maxW;

            if (ActualWidth > maxW)
                Width = maxW;

            _contentScroll?.InvalidateMeasure();
        }
    }
}
