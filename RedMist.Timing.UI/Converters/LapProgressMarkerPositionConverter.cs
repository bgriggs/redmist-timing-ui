using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace RedMist.Timing.UI.Converters;

/// <summary>
/// Converts a fraction (0..1) and a container width into a pixel position.
/// Used to position elements within the lap progress bar.
/// </summary>
public class LapProgressMarkerPositionConverter : IMultiValueConverter
{
    public static readonly LapProgressMarkerPositionConverter Instance = new();

    /// <summary>
    /// The fraction at which the 1.0 threshold sits within the 0–1.3 range (1.0 / 1.3).
    /// </summary>
    public static readonly double ThresholdFraction = 1.0 / 1.3;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2
            && values[0] is double fraction
            && values[1] is double width)
        {
            return fraction * width;
        }
        return 0.0;
    }
}
