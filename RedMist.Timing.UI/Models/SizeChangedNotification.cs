using Avalonia;

namespace RedMist.Timing.UI.Models;

public class SizeChangedNotification(Size size)
{
    public Size Size { get; } = size;
}
