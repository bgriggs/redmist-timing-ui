using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RedMist.TimingCommon.Models;
using System;
using System.Globalization;

namespace RedMist.Timing.UI.Converters;

public class FlagToBrushConverter : IValueConverter
{
    private const string TIMINGFLAG_GREEN_BACKGROUND = "timingFlagGreenBackground";
    private const string TIMINGFLAG_YELLOW_BACKGROUND = "timingFlagYellowBackground";
    private const string TIMINGFLAG_RED_BACKGROUND = "timingFlagRedBackground";
    private const string TIMINGFLAG_BLACK_BACKGROUND = "timingFlagBlackBackground";
    private const string TIMINGFLAG_WHITE_BACKGROUND = "timingFlagWhiteBackground";
    private const string TIMINGFLAG_PURPLE_BACKGROUND = "timingFlagPurpleBackground";
    private const string TIMINGFLAG_CHECKERED_BACKGROUND = "timingFlagCheckeredBackground";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string flag)
        {
            if (flag == "Green")
                return GetResource(TIMINGFLAG_GREEN_BACKGROUND);
            else if (flag == "Yellow")
                return GetResource(TIMINGFLAG_YELLOW_BACKGROUND);
            else if (flag == "Red")
                return GetResource(TIMINGFLAG_RED_BACKGROUND);
            else if (flag == "Black")
                return GetResource(TIMINGFLAG_BLACK_BACKGROUND);
            else if (flag == "White")
                return GetResource(TIMINGFLAG_WHITE_BACKGROUND);
            else if (flag == "Purple")
                return GetResource(TIMINGFLAG_PURPLE_BACKGROUND);
            else if (flag == "Checkered")
                return GetResource(TIMINGFLAG_CHECKERED_BACKGROUND);
        }
        else if (value is Flags fe)
        {
            return fe switch
            {
                Flags.Green => GetResource(TIMINGFLAG_GREEN_BACKGROUND),
                Flags.Yellow => GetResource(TIMINGFLAG_YELLOW_BACKGROUND),
                Flags.Red => GetResource(TIMINGFLAG_RED_BACKGROUND),
                Flags.Black => GetResource(TIMINGFLAG_BLACK_BACKGROUND),
                Flags.White => GetResource(TIMINGFLAG_WHITE_BACKGROUND),
                Flags.Purple35 => GetResource(TIMINGFLAG_PURPLE_BACKGROUND),
                Flags.Checkered => GetResource(TIMINGFLAG_CHECKERED_BACKGROUND),
                _ => Brushes.Transparent,
            };
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static IBrush GetResource(string key)
    {
        return (IBrush?)Application.Current?.FindResource(Application.Current.ActualThemeVariant, key) ?? Brushes.Transparent;
    }
}
