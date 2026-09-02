using RedMist.TimingCommon.Models;
using RestSharp;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Clients;

public class SponsorClient : BaseRestClient
{
    private readonly IHttpClientFactory httpClientFactory;


    public SponsorClient(RestClientFactory restClientFactory, IHttpClientFactory httpClientFactory)
        : base(restClientFactory, "Server:SponsorUrl")
    {
        this.httpClientFactory = httpClientFactory;
    }


    public virtual async Task<bool> SaveImpressionAsync(string source, string imageId, string eventId = "")
    {
        var request = new RestRequest("SaveImpression", Method.Post);
        request.AddQueryParameter("source", source);
        request.AddQueryParameter("imageId", imageId);
        request.AddQueryParameter("eventId", eventId);
        var result = await RestClient.PostAsync(request);
        return result.IsSuccessful;
    }

    public virtual async Task<bool> SaveViewableImpressionAsync(string source, string imageId, string eventId = "")
    {
        var request = new RestRequest("SaveViewableImpression", Method.Post);
        request.AddQueryParameter("source", source);
        request.AddQueryParameter("imageId", imageId);
        request.AddQueryParameter("eventId", eventId);
        var result = await RestClient.PostAsync(request);
        return result.IsSuccessful;
    }

    public virtual async Task<bool> SaveClickThroughAsync(string source, string imageId, string eventId = "")
    {
        var request = new RestRequest("SaveClickThrough", Method.Post);
        request.AddQueryParameter("source", source);
        request.AddQueryParameter("imageId", imageId);
        request.AddQueryParameter("eventId", eventId);
        var result = await RestClient.PostAsync(request);
        return result.IsSuccessful;
    }

    public virtual async Task<bool> SaveEngagementDurationAsync(string source, string imageId, int durationMs, string eventId = "")
    {
        var request = new RestRequest("SaveEngagementDuration", Method.Post);
        request.AddQueryParameter("source", source);
        request.AddQueryParameter("imageId", imageId);
        request.AddQueryParameter("durationMs", durationMs.ToString());
        request.AddQueryParameter("eventId", eventId);
        var result = await RestClient.PostAsync(request);
        return result.IsSuccessful;
    }

    /// <summary>
    /// Gets the active sponsors. When <paramref name="eventId"/> is supplied, sponsors excluded by the
    /// organization running that event are filtered out server-side.
    /// </summary>
    public virtual async Task<List<SponsorInfo>> GetSponsorsAsync(string eventId = "")
    {
        var request = new RestRequest("GetSponsors", Method.Get);
        if (!string.IsNullOrEmpty(eventId))
        {
            request.AddQueryParameter("eventId", eventId);
        }
        return await RestClient.GetAsync<List<SponsorInfo>>(request) ?? [];
    }
}
