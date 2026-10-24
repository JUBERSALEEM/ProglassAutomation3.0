using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProGlassAutomation.Customization;

/// <summary>
/// Selects existing editable text when a TextBox first receives keyboard focus.
/// A second click in an already-focused box keeps normal caret-placement behavior.
/// </summary>
public static class TextBoxFocusBehavior
{
    public static readonly DependencyProperty SelectAllOnFocusProperty =
        DependencyProperty.RegisterAttached(
            "SelectAllOnFocus",
            typeof(bool),
            typeof(TextBoxFocusBehavior),
            new PropertyMetadata(false, OnSelectAllOnFocusChanged));

    public static bool GetSelectAllOnFocus(DependencyObject element) =>
        (bool)element.GetValue(SelectAllOnFocusProperty);

    public static void SetSelectAllOnFocus(DependencyObject element, bool value) =>
        element.SetValue(SelectAllOnFocusProperty, value);

    private static void OnSelectAllOnFocusChanged(
        DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not TextBox textBox) return;

        if (args.NewValue is true)
        {
            textBox.GotKeyboardFocus += OnGotKeyboardFocus;
            textBox.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        }
        else
        {
            textBox.GotKeyboardFocus -= OnGotKeyboardFocus;
            textBox.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        }
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (sender is TextBox textBox) textBox.SelectAll();
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (sender is not TextBox textBox || textBox.IsKeyboardFocusWithin) return;
        if (!textBox.Focus()) return;
        textBox.SelectAll();
        args.Handled = true;
    }
}
