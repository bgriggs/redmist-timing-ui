using Microsoft.Extensions.Configuration;
using RestSharp;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Clients;

public class OrganizationClient : BaseRestClient
{
    private readonly string cdnLogosUrl = "https://assets.redmist.racing/logos";

    public OrganizationClient(IConfiguration configuration, RestClientFactory restClientFactory)
        : base(restClientFactory, "Server:OrganizationUrl")
    {
        if (configuration["Cdn:BaseUrl"] != null && configuration["Cdn:Logos"] != null)
        {
            var baseUrl = configuration["Cdn:BaseUrl"]!.TrimEnd('/');
            var logosPath = configuration["Cdn:Logos"]!.TrimStart('/').TrimEnd('/');
            cdnLogosUrl = $"{baseUrl}/{logosPath}";
        }
    }

    public virtual async Task<byte[]> GetOrganizationIconAsync(int organizationId)
    {
        var request = new RestRequest("GetOrganizationIcon", Method.Get);
        request.AddQueryParameter("organizationId", organizationId.ToString());
        var response = await RestClient.ExecuteAsync(request);

        if (!response.IsSuccessful || response.RawBytes == null)
        {
            return [];
        }

        return response.RawBytes;
    }

    /// <summary>
    /// Where an organization's logo lives on the CDN.
    /// </summary>
    /// <remarks>
    /// The URL rather than the bytes, because <see cref="Services.PersistentImageStore"/> does the
    /// fetching: it has to hold the request open to attach If-Modified-Since and read Last-Modified
    /// back off the response, which a method that returns only a byte array cannot express.
    /// </remarks>
    public virtual string GetOrganizationIconCdnUrl(int organizationId) => $"{cdnLogosUrl}/org-{organizationId}.img";
}
