using RedMist.Timing.UI.Clients;
using RedMist.TimingCommon.Models;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignSponsorClient : SponsorClient
{
    public DesignSponsorClient() : base(new DesignConfiguration(), new DesignHttpClientFactory())
    {
    }

    public override Task<List<SponsorInfo>> GetSponsorsAsync() => Task.FromResult<List<SponsorInfo>>([]);
    public override Task<bool> SaveImpressionAsync(string source, string imageId, string eventId = "") => Task.FromResult(true);
    public override Task<bool> SaveViewableImpressionAsync(string source, string imageId, string eventId = "") => Task.FromResult(true);
    public override Task<bool> SaveClickThroughAsync(string source, string imageId, string eventId = "") => Task.FromResult(true);
    public override Task<bool> SaveEngagementDurationAsync(string source, string imageId, int durationMs, string eventId = "") => Task.FromResult(true);

    private class DesignHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
