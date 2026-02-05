using Avalonia.Data.Converters;
using RedMist.TimingCommon.Models;
using System;
using System.Globalization;

namespace RedMist.Timing.UI.Converters;

/// <summary>
/// Converter that returns true if the flag is Checkered, false otherwise.
/// </summary>
public class FlagIsCheckeredConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Flags flag)
        {
            return flag == Flags.Checkered;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converter that returns true if the flag is NOT Checkered, false otherwise.
/// </summary>
public class FlagIsNotCheckeredConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Flags flag)
        {
            return flag != Flags.Checkered;
        }
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
