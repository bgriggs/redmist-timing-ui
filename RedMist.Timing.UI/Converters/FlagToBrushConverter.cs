using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace RedMist.Timing.UI.Converters;

public class FlagToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string flag)
        {
            if (flag == "Green")
                return Brush.Parse("#2eb720");
            else if (flag == "Yellow")
                return Brush.Parse("#fefda2");
            else if (flag == "Red")
                return Brush.Parse("#ff5252");
            else if (flag == "Black")
                return Brushes.Black;
            else if (flag == "White")
                return Brushes.WhiteSmoke;
            else if (flag == "Purple")
                return Brushes.Purple;
            else if (flag == "Checkered")
                return Brushes.Chocolate;
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
