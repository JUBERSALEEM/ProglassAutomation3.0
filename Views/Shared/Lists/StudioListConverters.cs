using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ProGlassAutomation.Views.Shared.Lists
{
    /// <summary>Presentation only. Preserve semantic keys and resolve them against the scoped Indigo palette.</summary>
    public sealed class StudioListBrushConverter : IValueConverter
    {
        public IValueConverter Palette { get; set; } = null!;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string key = value as string ?? string.Empty;
            bool ink = string.Equals(parameter as string, "ink", StringComparison.OrdinalIgnoreCase);
            bool soft = !ink && (string.Equals(parameter as string, "soft", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("Tint", StringComparison.OrdinalIgnoreCase));
            string tone = key.Contains("Danger", StringComparison.OrdinalIgnoreCase) || key.Contains("Red", StringComparison.OrdinalIgnoreCase) ? "Red"
                : key.Contains("Success", StringComparison.OrdinalIgnoreCase) || key.Contains("Green", StringComparison.OrdinalIgnoreCase) ? "Green"
                : key.Contains("Warning", StringComparison.OrdinalIgnoreCase) || key.Contains("Amber", StringComparison.OrdinalIgnoreCase) ? "Amber"
                : key.Contains("Accent", StringComparison.OrdinalIgnoreCase) || key.Contains("Info", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("Blue", StringComparison.OrdinalIgnoreCase) || key.Contains("Purple", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("Indigo", StringComparison.OrdinalIgnoreCase) ? "Indigo" : "Gray";
            return Palette.Convert((soft ? "Tint" : "") + tone + "Brush", targetType, parameter, culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Some dossier buttons use Tag as a VM bridge, not an icon. Never stringify that VM in a button template.</summary>
    public sealed class StudioListTagGlyphConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string text ? text : string.Empty;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class StudioListGutterConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => new Thickness(0, 0, value is double width && double.IsFinite(width) ? Math.Max(0, width) : 0, 0);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>ScrollViewer.ViewportWidth/Height are zero until first layout. Never send infinity/NaN into Width/Height.</summary>
    public sealed class StudioListExtentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is double extent && double.IsFinite(extent) ? Math.Max(0, extent) : 0.0;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
    /// <summary>Keep a usable register viewport in short/narrow windows; controls above it remain scroll-reachable.</summary>
    public sealed class StudioListToolbarHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is double height && double.IsFinite(height) && height > 0
                ? Math.Max(100, Math.Min(360, height * 0.6)) : 360.0;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

}
