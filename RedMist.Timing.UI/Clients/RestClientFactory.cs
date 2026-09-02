using BigMission.Shared.Auth;
using BigMission.Shared.RestSharp;
using Microsoft.Extensions.Configuration;
using RestSharp;
using RestSharp.Authenticators;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace RedMist.Timing.UI.Clients;

/// <summary>
/// Builds the app's REST clients over one connection pool and one Keycloak token.
/// </summary>
/// <remarks>
/// Every REST client here talks to the same API host with the same service credentials, so there is
/// nothing for a per-client connection pool or a per-client token to keep apart. Left to itself
/// RestSharp gives each client both: <c>new RestClient(options)</c> allocates its own
/// <see cref="HttpClientHandler"/>, and <see cref="KeycloakServiceAuthenticator"/> caches its token
/// on the instance. The clients were registered transient on top of that, and resolving
/// MainViewModel walks the whole graph, so startup allocated eight handlers - four EventClients, two
/// OrganizationClients, two SponsorClients - and each one that went on to make a request fetched its
/// own copy of the same token.
///
/// Sharing is safe because <see cref="RestClient"/> does not touch a supplied
/// <see cref="HttpClient"/>: base URL and default headers are held per client, and cookies are off,
/// so what the clients now have in common is the socket pool, the handler's settings and the
/// timeout - all of which they were already agreeing on separately. The authenticator is shared
/// deliberately: its whole purpose is the token cache, and one cache for one set of credentials is
/// the point.
/// </remarks>
public sealed class RestClientFactory : IDisposable
{
    private readonly IConfiguration configuration;
    private readonly HttpClient httpClient;
    private readonly IAuthenticator authenticator;
    private bool disposed;


    public RestClientFactory(IConfiguration configuration)
    {
        this.configuration = configuration;

        var authUrl = configuration["Keycloak:AuthServerUrl"] ?? throw new InvalidOperationException("Keycloak URL is not configured.");
        var realm = configuration["Keycloak:Realm"] ?? throw new InvalidOperationException("Keycloak realm is not configured.");
        var clientId = configuration["Keycloak:ClientId"] ?? throw new InvalidOperationException("Keycloak client ID is not configured.");
        var clientSecret = configuration["Keycloak:ClientSecret"] ?? throw new InvalidOperationException("Keycloak client secret is not configured.");
        authenticator = new KeycloakServiceAuthenticator(string.Empty, authUrl, realm, clientId, clientSecret);

        // RestSharp enforces its own per-request timeout with a cancellation token, and sets the
        // handed-out client to infinite for that reason. A supplied client keeps whatever timeout it
        // arrived with, so it has to be said here instead.
        httpClient = new HttpClient(CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
    }


    /// <summary>
    /// Creates a client for the server whose URL is at <paramref name="serverUrlConfigKey"/>.
    /// </summary>
    /// <remarks>
    /// Only the options RestSharp reads per request take effect here. The handler was built once in
    /// the constructor, so anything RestSharp would normally feed from options into the handler -
    /// AutomaticDecompression, Proxy, Credentials, ClientCertificates, ConfigureMessageHandler - is
    /// ignored if set below. Put those in <see cref="CreateHandler"/> instead, where they apply to
    /// every client.
    /// </remarks>
    public RestClient Create(string serverUrlConfigKey)
    {
        var url = configuration[serverUrlConfigKey] ?? throw new InvalidOperationException($"{serverUrlConfigKey} is not configured.");
        var options = new RestClientOptions(url) { Authenticator = authenticator };

        // What RestClientExtensions.CreateWithMessagePack does, minus the handler it would allocate.
        // RestClientFactoryTests pins the two together so this cannot quietly drift from the
        // library's own idea of a MessagePack client.
        var client = new RestClient(httpClient, options, disposeHttpClient: false,
            configureSerialization: s => s.UseSerializer(() => new MessagePackRestSerializer()));
        client.AddDefaultHeader("Accept", "application/msgpack, application/json");
        return client;
    }

    /// <summary>
    /// Matches the handler RestSharp would have built from default options, so sharing one changes
    /// nothing but the number of them.
    /// </summary>
    /// <remarks>
    /// This is a transcription of an internal method in a library we do not control, which is the
    /// fragile half of this class. RestClientFactoryTests calls that method by reflection and diffs
    /// every property against this one, so a change on their side fails here rather than quietly
    /// costing the app its response compression.
    /// </remarks>
    internal static HttpClientHandler CreateHandler()
    {
        var handler = new HttpClientHandler();

        // Browser has no sockets of its own to configure and throws on most of these.
        if (!OperatingSystem.IsBrowser())
        {
            handler.UseCookies = false;
            handler.AutomaticDecompression = DecompressionMethods.All;
        }

        // RestSharp follows redirects itself so that it can carry parameters and cookies across the
        // hop; leaving the handler to do it as well would hide them from it.
        handler.AllowAutoRedirect = false;
        return handler;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        // The clients built above hold this client but do not own it - disposeHttpClient is false -
        // so this is the only thing that can release the pool. Note that the app never gets here:
        // shutdown stops the host without disposing it, and the mobile heads have no shutdown hook
        // at all, so in practice the pool goes when the process does. This exists so ownership is
        // unambiguous, and so tests can release what they open.
        httpClient.Dispose();
    }
}
