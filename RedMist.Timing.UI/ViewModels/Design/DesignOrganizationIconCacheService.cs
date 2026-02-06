using Avalonia.Media.Imaging;
using RedMist.Timing.UI.Services;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignOrganizationIconCacheService : OrganizationIconCacheService
{
    public DesignOrganizationIconCacheService() 
        : base(new DesignOrganizationClient(), new DebugLoggerFactory())
    {
    }

    public new Task<Bitmap?> GetOrganizationIconAsync(int organizationId)
    {
        return Task.FromResult<Bitmap?>(null);
    }

    public new Bitmap? GetCachedIcon(int organizationId)
    {
        return null;
    }
}
