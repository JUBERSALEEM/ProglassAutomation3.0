using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ProGlassAutomation.Views.Dashboard.Overview
{
    /// <summary>
    /// Resolves the existing dashboard semantic keys into the page's own palette.
    /// Unlike the app-wide BrushKeyConverter, this cannot pick dark-theme fills
    /// for a warm-white Indigo card. Data/status names are never changed here.
    /// All colours are supplied by IndigoStudioResources.xaml, not duplicated.
    /// </summary>
    public sealed class IndigoStatusBrushConverter : IValueConverter
    {
        public Brush Accent { get; set; } = Brushes.Transparent;
        public Brush AccentSoft { get; set; } = Brushes.Transparent;
        public Brush Success { get; set; } = Brushes.Transparent;
        public Brush SuccessSoft { get; set; } = Brushes.Transparent;
        public Brush Danger { get; set; } = Brushes.Transparent;
        public Brush DangerSoft { get; set; } = Brushes.Transparent;
        public Brush Warning { get; set; } = Brushes.Transparent;
        public Brush WarningSoft { get; set; } = Brushes.Transparent;
        public Brush Neutral { get; set; } = Brushes.Transparent;
        public Brush NeutralSoft { get; set; } = Brushes.Transparent;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string key = value as string ?? string.Empty;
            bool soft = key.StartsWith("Tint", StringComparison.OrdinalIgnoreCase);
            if (key.Contains("Red", StringComparison.OrdinalIgnoreCase) || key == "DangerBrush")
                return soft ? DangerSoft : Danger;
            if (key.Contains("Green", StringComparison.OrdinalIgnoreCase) || key == "SuccessBrush")
                return soft ? SuccessSoft : Success;
            if (key.Contains("Amber", StringComparison.OrdinalIgnoreCase) || key == "WarningBrush")
                return soft ? WarningSoft : Warning;
            if (key.Contains("Blue", StringComparison.OrdinalIgnoreCase) || key.Contains("Indigo", StringComparison.OrdinalIgnoreCase)
                || key.Contains("Purple", StringComparison.OrdinalIgnoreCase) || key is "InfoBrush" or "AccentBrush")
                return soft ? AccentSoft : Accent;
            return soft ? NeutralSoft : Neutral;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
