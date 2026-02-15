using RedMist.Timing.UI.Services;
using UIKit;

namespace RedMist.Timing.UI.iOS;

/// <summary>
/// iOS implementation of screen wake service using IdleTimerDisabled.
/// </summary>
public class iOSScreenWakeService : IScreenWakeService
{
    public void SetKeepScreenOn(bool keepOn)
    {
        UIApplication.SharedApplication.IdleTimerDisabled = keepOn;
    }
}
