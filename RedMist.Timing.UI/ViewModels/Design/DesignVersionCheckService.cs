using RedMist.Timing.UI.Models;
using RedMist.Timing.UI.Services;
using RedMist.TimingCommon;
using RedMist.TimingCommon.Models;
using System;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignVersionCheckService : IVersionCheckService
{
    public VersionCheckResult CheckVersion(Version currentVersion, UIVersionInfo versionInfo, AppPlatform platform)
    {
        return new VersionCheckResult
        {
            Requirement = UpdateRequirement.None,
            CurrentVersion = currentVersion,
            Platform = platform
        };
    }

    public Version GetCurrentApplicationVersion() => new Version(1, 0, 0);

    public Task<UIVersionInfo?> GetVersionInfoAsync(int timeoutSeconds = 5) => Task.FromResult<UIVersionInfo?>(null);
}
