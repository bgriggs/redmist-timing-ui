namespace RedMist.Timing.UI.Services;

/// <summary>
/// No-op implementation of screen wake service for platforms that don't support it (Desktop, Browser).
/// </summary>
public class NoOpScreenWakeService : IScreenWakeService
{
    public void SetKeepScreenOn(bool keepOn)
    {
        // No-op on platforms that don't support screen wake lock
    }
}
