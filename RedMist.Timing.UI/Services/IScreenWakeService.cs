namespace RedMist.Timing.UI.Services;

/// <summary>
/// Service for controlling screen wake lock (keeping the screen on).
/// </summary>
public interface IScreenWakeService
{
    /// <summary>
    /// Sets whether the screen should remain on.
    /// </summary>
    /// <param name="keepOn">True to keep the screen on, false to allow normal screen timeout.</param>
    void SetKeepScreenOn(bool keepOn);
}
