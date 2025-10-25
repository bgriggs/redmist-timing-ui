using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignPlatformDetectionService : IPlatformDetectionService
{
    public AppPlatform GetCurrentPlatform() => AppPlatform.Desktop;
    
    public bool ShouldCheckVersion() => false;
}
