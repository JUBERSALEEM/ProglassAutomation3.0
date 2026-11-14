// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Behaviors/TextBoxBehaviors.cs                                           ║
// ║  ProGlass Automation — Global TextBox Attached Behaviors                 ║
// ║                                                                          ║
// ║  CHAT 7 ADDITIONS (Bug 5):                                               ║
// ║    + AcceptDecimals    — decimal input filter + shake animation          ║
// ║    + AcceptIntegersOnly — integer-only filter + shake animation          ║
// ║                                                                          ║
// ║  PRESERVED EXACTLY (Bug 2 already worked):                               ║
// ║    ✅ SelectAllOnFocus — all three handlers unchanged                    ║
// ╚══════════════════════════════════════════════════════════════════════════╝

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ProGlassAutomation.Behaviors
{
    /// <summary>
    /// Global attached behaviors for TextBox controls.
    /// Provides UX enhancements that can be enabled per-TextBox or globally via styles.
    /// </summary>
    public static class TextBoxBehaviors
    {
        // ══════════════════════════════════════════════════════════════════
        // REGION 1 — SelectAllOnFocus (PRESERVED EXACTLY — already working)
        // ══════════════════════════════════════════════════════════════════

        #region SelectAllOnFocus

        // When enabled, clicking or tabbing into a TextBox auto-selects all its
        // text so the user can immediately overwrite without manually deleting.
        //
        // Usage in XAML:
        //   <TextBox behaviors:TextBoxBehaviors.SelectAllOnFocus="True" ... />
        //
        // Or apply globally in a style:
        //   <Style TargetType="TextBox">
        //     <Setter Property="behaviors:TextBoxBehaviors.SelectAllOnFocus" Value="True"/>
        //   </Style>

        public static readonly DependencyProperty SelectAllOnFocusProperty =
            DependencyProperty.RegisterAttached(
                "SelectAllOnFocus",
                typeof(bool),
                typeof(TextBoxBehaviors),
                new UIPropertyMetadata(false, OnSelectAllOnFocusChanged));

        public static bool GetSelectAllOnFocus(DependencyObject obj)
            => (bool)obj.GetValue(SelectAllOnFocusProperty);

        public static void SetSelectAllOnFocus(DependencyObject obj, bool value)
            => obj.SetValue(SelectAllOnFocusProperty, value);

        private static void OnSelectAllOnFocusChanged(
            DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox textBox) return;

            // Detach first to prevent double-hooking
            textBox.GotKeyboardFocus -= TextBox_GotKeyboardFocus;
            textBox.PreviewMouseLeftButtonDown -= TextBox_PreviewMouseLeftButtonDown;
            textBox.GotFocus -= TextBox_GotFocus;

            if ((bool)e.NewValue)
            {
                textBox.GotKeyboardFocus += TextBox_GotKeyboardFocus;
                textBox.PreviewMouseLeftButtonDown += TextBox_PreviewMouseLeftButtonDown;
                textBox.GotFocus += TextBox_GotFocus;
            }
        }

        private static void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.SelectAll();
        }

        private static void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
                tb.SelectAll();
        }

        /// <summary>
        /// If TextBox doesn't have keyboard focus yet, intercept the mouse click,
        /// give it focus (which triggers SelectAll via GotKeyboardFocus),
        /// then swallow the click so caret doesn't reposition.
        /// When already focused, still SelectAll on every click so a single
        /// mouse press always prepares the field for immediate overwrite — the
        /// operator expectation for price/size boxes in Step 5.
        /// </summary>
        private static void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (!tb.IsKeyboardFocusWithin)
                {
                    e.Handled = true;
                    tb.Focus();
                }
                else
                {
                    // Already focused — single click should still select all for
                    // quick overwrite of Base/Surcharge and grid cells. Swallow
                    // the click so the caret doesn't move after the SelectAll.
                    tb.SelectAll();
                    e.Handled = true;
                }
            }
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        // REGION 2 — AcceptDecimals (NEW — Bug 5)
        //
        // Restricts TextBox input to valid decimal numbers only.
        // Allows: digits 0-9, ONE decimal point, Backspace/Delete/Arrows
        // Rejects: letters, symbols, second decimal point
        // UX: subtle shake animation on invalid input attempt
        //
        // Usage:
        //   <TextBox behaviors:TextBoxBehaviors.AcceptDecimals="True"/>
        //
        // For decimal binding use StringFormat={}{0:0.###} on the Binding
        // ══════════════════════════════════════════════════════════════════

        #region AcceptDecimals

        public static readonly DependencyProperty AcceptDecimalsProperty =
            DependencyProperty.RegisterAttached(
                "AcceptDecimals",
                typeof(bool),
                typeof(TextBoxBehaviors),
                new UIPropertyMetadata(false, OnAcceptDecimalsChanged));

        public static bool GetAcceptDecimals(DependencyObject obj)
            => (bool)obj.GetValue(AcceptDecimalsProperty);

        public static void SetAcceptDecimals(DependencyObject obj, bool value)
            => obj.SetValue(AcceptDecimalsProperty, value);

        private static void OnAcceptDecimalsChanged(
            DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox tb) return;

            // Unsubscribe first — prevents double-subscription on style reload
            tb.PreviewTextInput -= OnDecimalPreviewTextInput;
            tb.PreviewKeyDown -= OnNumericPreviewKeyDown;
            DataObject.RemovePastingHandler(tb, OnDecimalPasting);

            if ((bool)e.NewValue)
            {
                tb.PreviewTextInput += OnDecimalPreviewTextInput;
                tb.PreviewKeyDown += OnNumericPreviewKeyDown;
                DataObject.AddPastingHandler(tb, OnDecimalPasting);
            }
        }

        private static void OnDecimalPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox tb) return;

            var input = e.Text;

            // Allow decimal point (always use "." internally — invariant culture)
            if (input == "." || input == ",")
            {
                // Simulate what text will look like after this character is inserted
                string projected = GetTextAfterEdit(tb, input);

                // Count decimal separators in projected result
                int dotCount = 0;
                foreach (char c in projected)
                    if (c == '.' || c == ',') dotCount++;

                if (dotCount > 1)
                {
                    // Already has a decimal point — reject and shake
                    e.Handled = true;
                    PlayShakeAnimation(tb);
                }
                // else: allow the first decimal point through
                return;
            }

            // Allow digits 0–9
            if (input.Length == 1 && char.IsDigit(input[0]))
                return; // Allow

            // Reject everything else
            e.Handled = true;
            PlayShakeAnimation(tb);
        }

        private static void OnDecimalPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox tb) return;

            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var text = (string)e.DataObject.GetData(typeof(string));
                if (!IsValidDecimalString(text))
                {
                    e.CancelCommand();
                    PlayShakeAnimation(tb);
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        private static bool IsValidDecimalString(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return decimal.TryParse(
                text.Trim(),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out _);
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        // REGION 3 — AcceptIntegersOnly (NEW — Bug 5 integer variant)
        //
        // Restricts TextBox input to whole integers only.
        // Allows: digits 0-9, Backspace/Delete/Arrows
        // Rejects: decimal points, letters, symbols
        // UX: subtle shake animation on invalid input attempt
        //
        // Usage:
        //   <TextBox behaviors:TextBoxBehaviors.AcceptIntegersOnly="True"/>
        // ══════════════════════════════════════════════════════════════════

        #region AcceptIntegersOnly

        public static readonly DependencyProperty AcceptIntegersOnlyProperty =
            DependencyProperty.RegisterAttached(
                "AcceptIntegersOnly",
                typeof(bool),
                typeof(TextBoxBehaviors),
                new UIPropertyMetadata(false, OnAcceptIntegersOnlyChanged));

        public static bool GetAcceptIntegersOnly(DependencyObject obj)
            => (bool)obj.GetValue(AcceptIntegersOnlyProperty);

        public static void SetAcceptIntegersOnly(DependencyObject obj, bool value)
            => obj.SetValue(AcceptIntegersOnlyProperty, value);

        private static void OnAcceptIntegersOnlyChanged(
            DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox tb) return;

            // Unsubscribe first
            tb.PreviewTextInput -= OnIntegerPreviewTextInput;
            tb.PreviewKeyDown -= OnNumericPreviewKeyDown;
            DataObject.RemovePastingHandler(tb, OnIntegerPasting);

            if ((bool)e.NewValue)
            {
                tb.PreviewTextInput += OnIntegerPreviewTextInput;
                tb.PreviewKeyDown += OnNumericPreviewKeyDown;
                DataObject.AddPastingHandler(tb, OnIntegerPasting);
            }
        }

        private static void OnIntegerPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox tb) return;

            // Only allow digits — no decimal point
            if (e.Text.Length == 1 && char.IsDigit(e.Text[0]))
                return; // Allow

            // Reject everything else (including ".", ",")
            e.Handled = true;
            PlayShakeAnimation(tb);
        }

        private static void OnIntegerPasting(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is not TextBox tb) return;

            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var text = (string)e.DataObject.GetData(typeof(string));
                if (!int.TryParse(text.Trim(), out _))
                {
                    e.CancelCommand();
                    PlayShakeAnimation(tb);
                }
            }
            else
            {
                e.CancelCommand();
            }
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        // REGION 4 — Shared numeric key handler (NEW — supports Regions 2+3)
        //
        // Shared PreviewKeyDown for BOTH AcceptDecimals and AcceptIntegersOnly.
        // Explicitly allows navigation/editing/clipboard keys so they always work.
        // All actual character filtering is handled by PreviewTextInput above.
        // ══════════════════════════════════════════════════════════════════

        #region Shared Numeric Key Handler

        private static void OnNumericPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Always allow navigation, editing, and clipboard shortcuts
            // PreviewTextInput handles the actual character validation
            switch (e.Key)
            {
                // Navigation
                case Key.Left:
                case Key.Right:
                case Key.Home:
                case Key.End:
                case Key.Up:
                case Key.Down:
                // Editing
                case Key.Back:
                case Key.Delete:
                case Key.Tab:
                case Key.Enter:
                case Key.Escape:
                    return; // Allow — do NOT set e.Handled
            }

            // Clipboard + undo shortcuts (Ctrl+key)
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.A: // Select All
                    case Key.C: // Copy
                    case Key.V: // Paste (DataObject.PastingHandler validates content)
                    case Key.X: // Cut
                    case Key.Z: // Undo
                    case Key.Y: // Redo
                        return; // Allow
                }
            }

            // Numpad decimal — let PreviewTextInput handle "." validation
            if (e.Key == Key.Decimal || e.Key == Key.OemPeriod)
                return; // Allow through to PreviewTextInput

            // Everything else passes to PreviewTextInput for character validation
            // DO NOT set e.Handled here — would block PreviewTextInput from firing
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════
        // REGION 5 — Shake animation (NEW — invalid input UX feedback)
        //
        // Subtle 300ms horizontal oscillation.
        // Tells user "that character is not allowed" without being jarring.
        // Fire-and-forget — does not block typing.
        // Uses FillBehavior=Stop so TextBox returns to exact original position.
        // ══════════════════════════════════════════════════════════════════

        #region Shake Animation

        private static void PlayShakeAnimation(TextBox tb)
        {
            // Ensure we have a TranslateTransform for horizontal movement
            // Only replace if not already a TranslateTransform (preserve existing)
            if (tb.RenderTransform is not TranslateTransform)
                tb.RenderTransform = new TranslateTransform(0, 0);

            if (tb.RenderTransform is not TranslateTransform tt)
                return;

            // Stop any running shake before starting a new one
            // (prevents animation accumulation on rapid invalid input)
            tt.BeginAnimation(TranslateTransform.XProperty, null);

            // 7 keyframes — oscillates and decays to 0
            // Total duration: 300ms — noticeable but not intrusive
            var shake = new DoubleAnimationUsingKeyFrames
            {
                Duration = new Duration(TimeSpan.FromMilliseconds(300)),
                FillBehavior = FillBehavior.Stop // MUST be Stop — returns to X=0
            };

            shake.KeyFrames.Add(NewFrame(0, 0.00));
            shake.KeyFrames.Add(NewFrame(5, 0.15));
            shake.KeyFrames.Add(NewFrame(-5, 0.35));
            shake.KeyFrames.Add(NewFrame(4, 0.52));
            shake.KeyFrames.Add(NewFrame(-3, 0.68));
            shake.KeyFrames.Add(NewFrame(2, 0.82));
            shake.KeyFrames.Add(NewFrame(0, 1.00));

            tt.BeginAnimation(TranslateTransform.XProperty, shake);
        }

        private static EasingDoubleKeyFrame NewFrame(double value, double percent) =>
            new EasingDoubleKeyFrame(value, KeyTime.FromPercent(percent))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

        #endregion

        // ══════════════════════════════════════════════════════════════════
        // REGION 6 — Helpers
        // ══════════════════════════════════════════════════════════════════

        #region Helpers

        /// <summary>
        /// Projects what the TextBox text will look like AFTER a character is typed.
        /// Used to validate "would a second decimal point appear in the result?"
        /// Mirrors exactly how WPF inserts text: replaces selection with new input.
        /// </summary>
        private static string GetTextAfterEdit(TextBox tb, string newInput)
        {
            var text = tb.Text ?? string.Empty;
            int start = tb.SelectionStart;
            int length = tb.SelectionLength;

            return text.Substring(0, start)
                   + newInput
                   + text.Substring(start + length);
        }

        #endregion
    }
}