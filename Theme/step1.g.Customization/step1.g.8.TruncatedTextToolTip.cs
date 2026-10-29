using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ProGlassAutomation.Customization;

/// <summary>
/// Shows the whole value in a tooltip when a cell could not show it.
///
/// A register cell trims a long value with an ellipsis so one wide name cannot
/// push every other column off the page. Trimming is a silent loss: the operator
/// sees "Al Mansoori Contracting &amp; Fit…" and has no way to read the rest.
/// WPF will not tell us whether a TextBlock trimmed its text, so this compares
/// the measured width of the text against the width the block was given.
///
/// The tooltip is evaluated twice, so it is already correct before the pointer
/// arrives: once when the block settles its size (the usual case — a register
/// lays out long before anyone hovers it) and again on mouse-enter, which covers
/// a value that changed without changing the block's width. It is cleared on
/// mouse-leave, so nothing lingers over a row that has scrolled.
/// </summary>
public static class TruncatedTextToolTip
{
    /// <summary>How long the full value stays on screen; the default is too short to read.</summary>
    private const int ShowDurationMilliseconds = 20000;

    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(TruncatedTextToolTip),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject element) =>
        (bool)element.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject element, bool value) =>
        element.SetValue(EnabledProperty, value);

    /// <summary>
    /// True when the text is wider than the space it was given. Separated from the
    /// measuring so the rule can be checked without a window.
    /// </summary>
    public static bool IsClipped(string? text, double availableWidth, double measuredWidth) =>
        !string.IsNullOrWhiteSpace(text)
        && availableWidth > 0
        && measuredWidth > availableWidth + 0.5;

    private static void OnEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not TextBlock block) return;

        if (args.NewValue is true)
        {
            block.MouseEnter += OnMouseEnter;
            block.MouseLeave += OnMouseLeave;
            block.SizeChanged += OnSizeChanged;
        }
        else
        {
            block.MouseEnter -= OnMouseEnter;
            block.MouseLeave -= OnMouseLeave;
            block.SizeChanged -= OnSizeChanged;
            block.ClearValue(FrameworkElement.ToolTipProperty);
        }
    }

    private static void OnMouseEnter(object sender, MouseEventArgs args)
    {
        if (sender is TextBlock block) Evaluate(block);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is TextBlock block) Evaluate(block);
    }

    /// <summary>Keeps the tooltip present only while the value is actually clipped.</summary>
    private static void Evaluate(TextBlock block)
    {
        var available = block.ActualWidth - Padding(block);
        if (!IsClipped(block.Text, available, Measure(block, block.Text)))
        {
            block.ClearValue(FrameworkElement.ToolTipProperty);
            return;
        }

        block.ToolTip = block.Text;
        ToolTipService.SetShowDuration(block, ShowDurationMilliseconds);
        ToolTipService.SetBetweenShowDelay(block, 0);
    }

    private static void OnMouseLeave(object sender, MouseEventArgs args)
    {
        if (sender is TextBlock block) block.ClearValue(FrameworkElement.ToolTipProperty);
    }

    private static double Padding(TextBlock block) =>
        block.Padding.Left + block.Padding.Right;

    /// <summary>The width the text would need at this block's own font.</summary>
    private static double Measure(TextBlock block, string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var typeface = new Typeface(
            block.FontFamily,
            block.FontStyle,
            block.FontWeight,
            block.FontStretch);

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            block.FlowDirection,
            typeface,
            block.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(block).PixelsPerDip);

        // Wrapped text is not clipped by width, it is clipped by height, and the
        // register never wraps, so unwrapped measurement is the right comparison.
        return formatted.WidthIncludingTrailingWhitespace;
    }
}
