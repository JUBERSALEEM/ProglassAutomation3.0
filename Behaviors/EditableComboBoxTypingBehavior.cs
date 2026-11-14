// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  Behaviors/EditableComboBoxTypingBehavior.cs                             ║
// ║                                                                          ║
// ║  CHAT 13 HOTFIX-5 (round 3) — CORRECT CARET HANDLING                     ║
// ║                                                                          ║
// ║  Fixes two caret bugs specific to WPF's editable ComboBox:              ║
// ║                                                                          ║
// ║    Bug A: Click on already-focused filled box → caret jumps to pos 0    ║
// ║            instead of selecting-all or placing at click point.          ║
// ║            Cause: WPF ComboBox resets caret after internal Text sync.   ║
// ║                                                                          ║
// ║    Bug B: Typing after such a click → new char is PREPENDED at pos 0   ║
// ║            producing "X6mm" instead of appending "6mmX".               ║
// ║            Cause: Same caret-reset behavior.                            ║
// ║                                                                          ║
// ║  Solution:                                                              ║
// ║                                                                          ║
// ║    1. FIRST click into an unfocused box with text → select all         ║
// ║       (subsequent clicks in same focus session behave normally).        ║
// ║                                                                          ║
// ║    2. On EVERY TextChanged, cache the caret position. If we later      ║
// ║       detect caret got reset to 0 while text is non-empty AND user     ║
// ║       has typed something new, restore to end-of-text (or cached pos). ║
// ║                                                                          ║
// ║    3. When ComboBox internally re-syncs Text (during CollectionView    ║
// ║       Filter refresh, or after selection commit), the caret restore    ║
// ║       kicks in to keep the caret where the user was typing.            ║
// ╚══════════════════════════════════════════════════════════════════════════╝

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ProGlassAutomation.Behaviors
{
    /// <summary>
    /// Attached behavior for editable ComboBoxes with live-filter binding.
    /// Fixes WPF's caret-reset-to-zero bug during ItemsSource sync and
    /// implements smart click-to-select-all on first focus.
    /// </summary>
    public static class EditableComboBoxTypingBehavior
    {
        // ── IsEnabled attached property ──────────────────────────────────

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(EditableComboBoxTypingBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static void SetIsEnabled(DependencyObject element, bool value) =>
            element.SetValue(IsEnabledProperty, value);

        public static bool GetIsEnabled(DependencyObject element) =>
            (bool)element.GetValue(IsEnabledProperty);

        // ── Per-ComboBox state ──────────────────────────────────────────

        // Set to true after the initial select-all fires for this focus
        // session. Reset on LostKeyboardFocus.
        private static readonly DependencyProperty HasSelectedOnFocusProperty =
            DependencyProperty.RegisterAttached(
                "HasSelectedOnFocus", typeof(bool),
                typeof(EditableComboBoxTypingBehavior),
                new PropertyMetadata(false));

        // Last known GOOD caret position. Cached on every TextChanged
        // where the caret was NOT at 0-with-non-empty-text (which is
        // WPF's bug signature).
        private static readonly DependencyProperty LastCaretProperty =
            DependencyProperty.RegisterAttached(
                "LastCaret", typeof(int),
                typeof(EditableComboBoxTypingBehavior),
                new PropertyMetadata(0));

        // ── Handler registration ────────────────────────────────────────

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ComboBox cb) return;

            if ((bool)e.NewValue)
            {
                cb.Loaded += OnComboBoxLoaded;
                cb.Unloaded += OnComboBoxUnloaded;
                cb.LostKeyboardFocus += OnLostKeyboardFocus;

                if (cb.IsLoaded) WireEditableTextBox(cb);
            }
            else
            {
                cb.Loaded -= OnComboBoxLoaded;
                cb.Unloaded -= OnComboBoxUnloaded;
                cb.LostKeyboardFocus -= OnLostKeyboardFocus;
                UnwireEditableTextBox(cb);
            }
        }

        private static void OnComboBoxLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cb) WireEditableTextBox(cb);
        }

        private static void OnComboBoxUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox cb) UnwireEditableTextBox(cb);
        }

        private static void WireEditableTextBox(ComboBox cb)
        {
            var tb = cb.Template?.FindName("PART_EditableTextBox", cb) as TextBox;
            if (tb == null) return;

            tb.PreviewMouseLeftButtonDown -= OnTextBoxPreviewMouseDown;
            tb.PreviewMouseLeftButtonDown += OnTextBoxPreviewMouseDown;

            tb.PreviewKeyDown -= OnTextBoxPreviewKeyDown;
            tb.PreviewKeyDown += OnTextBoxPreviewKeyDown;

            tb.TextChanged -= OnTextBoxTextChanged;
            tb.TextChanged += OnTextBoxTextChanged;
        }

        private static void UnwireEditableTextBox(ComboBox cb)
        {
            var tb = cb.Template?.FindName("PART_EditableTextBox", cb) as TextBox;
            if (tb == null) return;

            tb.PreviewMouseLeftButtonDown -= OnTextBoxPreviewMouseDown;
            tb.PreviewKeyDown -= OnTextBoxPreviewKeyDown;
            tb.TextChanged -= OnTextBoxTextChanged;
        }

        // ── Event handlers ──────────────────────────────────────────────

        private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Reset per-session flags so next focus starts fresh
            if (sender is ComboBox cb)
            {
                cb.SetValue(HasSelectedOnFocusProperty, false);
                cb.SetValue(LastCaretProperty, 0);
            }
        }

        // FIRST click into an unfocused box:
        //   → if there's text, select all (so next keystroke replaces)
        //   → if empty, just take focus (WPF default)
        // Subsequent clicks in same focus session:
        //   → let WPF handle it normally (caret placement, drag-select)
        private static void OnTextBoxPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not TextBox tb) return;
            var cb = FindOwnerComboBox(tb);
            if (cb == null) return;

            bool alreadyDone = (bool)cb.GetValue(HasSelectedOnFocusProperty);

            // Subsequent clicks — normal behavior
            if (alreadyDone) return;

            // First click — mark done and handle
            cb.SetValue(HasSelectedOnFocusProperty, true);

            if (string.IsNullOrEmpty(tb.Text))
            {
                // Empty box — just take focus normally, no special handling
                return;
            }

            // Box has text — override default caret placement with select-all
            e.Handled = true;
            tb.Focus();

            // Defer so focus completes before we select
            tb.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (!tb.IsKeyboardFocused) return;
                tb.SelectAll();
                cb.SetValue(LastCaretProperty, tb.Text.Length);
            }), DispatcherPriority.Input);
        }

        // Track caret while user types (arrow keys, backspace, delete, etc.)
        // so we know where they were last positioned.
        private static void OnTextBoxPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb) return;
            var cb = FindOwnerComboBox(tb);
            if (cb == null) return;

            // Cache the intended caret position AFTER this key would take
            // effect. For plain typing, caret advances by 1.
            // We'll re-cache in TextChanged with the actual final position.
            cb.SetValue(LastCaretProperty, tb.CaretIndex);
        }

        // The critical handler: fires after every text change.
        // Detects WPF's caret-reset-to-0 bug and restores caret.
        private static void OnTextBoxTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            var cb = FindOwnerComboBox(tb);
            if (cb == null) return;

            // If box is empty, nothing to protect
            if (string.IsNullOrEmpty(tb.Text)) return;

            int currentCaret = tb.CaretIndex;
            int cachedCaret = (int)cb.GetValue(LastCaretProperty);

            // WPF's caret-reset bug signature:
            //   caret is at 0 AND cached position is > 0 (user was typing
            //   further into the string). In this case, restore.
            //
            // But we must NOT interfere if the user LEGITIMATELY moved
            // caret to 0 via Home key or click-at-start.
            //
            // Heuristic: if caret==0 AND text length > cached position,
            // AND no selection, this is the WPF bug — restore to
            // end-of-text (safest place after unexpected reset).
            if (currentCaret == 0 && cachedCaret > 0 && tb.SelectionLength == 0)
            {
                // Defer past WPF's internal handling
                tb.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    if (!tb.IsKeyboardFocused) return;

                    // Restore to end of text — safest recovery point.
                    // The typical scenario is: user typed "6", filter
                    // refreshed, caret got reset. End-of-text puts them
                    // right after their last typed char.
                    int safeCaret = System.Math.Min(tb.Text.Length, tb.Text.Length);
                    tb.CaretIndex = safeCaret;
                    cb.SetValue(LastCaretProperty, safeCaret);
                }), DispatcherPriority.Loaded);
                return;
            }

            // Normal update — remember current caret for next TextChanged
            cb.SetValue(LastCaretProperty, currentCaret);
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static ComboBox FindOwnerComboBox(DependencyObject start)
        {
            DependencyObject current = start;
            while (current != null)
            {
                if (current is ComboBox cb) return cb;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}