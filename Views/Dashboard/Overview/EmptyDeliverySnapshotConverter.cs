using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using ProGlassAutomation.Models.Dashboard;

namespace ProGlassAutomation.Views.Dashboard.Overview
{
    /// <summary>
    /// Empty-state presentation only. Never totals the mixed notes/pieces units.
    /// Avoids index bindings into the initially empty observable collection.
    /// The VM raises DeliveryMixSlices after each complete snapshot refresh.
    /// </summary>
    public sealed class EmptyDeliverySnapshotConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not IEnumerable<ChartSliceModel> slices) return false;
            var snapshot = slices.ToArray();
            return snapshot.Length > 0 && snapshot.All(s =>
                s.RawValue == 0m && !string.IsNullOrWhiteSpace(s.ValueDisplay) && s.ValueDisplay != "\u2014");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
