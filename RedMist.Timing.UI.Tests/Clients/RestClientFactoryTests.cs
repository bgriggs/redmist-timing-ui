using BigMission.Shared.RestSharp;
using Microsoft.Extensions.Configuration;
using RedMist.Timing.UI.Clients;
using RestSharp;
using System.Net.Http;
using System.Reflection;

namespace RedMist.Timing.UI.Tests.Clients;

/// <summary>
/// Covers what the app's REST clients share: one connection pool and one Keycloak token between
/// them, and the MessagePack setup the factory hand-rolls to get there.
/// </summary>
[TestClass]
public sealed class RestClientFactoryTests
{
    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Server:EventUrl"] = "http://localhost/event",
            ["Server:OrganizationUrl"] = "http://localhost/organization",
            ["Keycloak:AuthServerUrl"] = "http://localhost/auth",
            ["Keycloak:Realm"] = "test",
            ["Keycloak:ClientId"] = "test-client",
            ["Keycloak:ClientSecret"] = "test-secret",
        })
        .Build();

    /// <summary>
    /// RestSharp keeps the client it was handed on an internal property, which is the only place the
    /// socket pool is visible from outside.
    /// </summary>
    private static object? HttpClientOf(RestClient client)
        => typeof(RestClient).GetProperty("HttpClient", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(client);

    [TestMethod]
    public void EveryClientSharesOneConnectionPool()
    {
        using var factory = new RestClientFactory(Configuration());

        var events = factory.Create("Server:EventUrl");
        var organizations = factory.Create("Server:OrganizationUrl");

        Assert.IsNotNull(HttpClientOf(events), "RestSharp no longer exposes its HttpClient - this test needs revisiting.");
        Assert.AreSame(HttpClientOf(events), HttpClientOf(organizations));
    }

    [TestMethod]
    public void TheSharedClientLeavesTimeoutsToRestSharp()
    {
        // RestSharp bounds every request with its own cancellation token and hands out clients set
        // to infinite for that reason. Left at HttpClient's 100 second default this would silently
        // cap any request asking for longer, and would push timeout classification onto RestSharp's
        // fallback of sniffing the exception message.
        using var factory = new RestClientFactory(Configuration());

        var client = HttpClientOf(factory.Create("Server:EventUrl")) as HttpClient;

        Assert.IsNotNull(client, "RestSharp no longer exposes its HttpClient - this test needs revisiting.");
        Assert.AreEqual(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [TestMethod]
    public void TheHandlerMatchesTheOneRestSharpWouldHaveBuilt()
    {
        // CreateHandler transcribes RestSharp's internal ConfigureHttpMessageHandler, because the
        // constructor that takes an HttpClient never calls it. Run the original against a fresh
        // handler and compare, so their recipe changing shows up as a failure here rather than as a
        // quiet loss of gzip on every response.
        var configure = typeof(RestClient).GetMethod("ConfigureHttpMessageHandler",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(configure, "RestSharp no longer has ConfigureHttpMessageHandler - this test needs revisiting.");

        var theirs = new HttpClientHandler();
        configure.Invoke(null, [theirs, new RestClientOptions("http://localhost/event")]);
        var mine = RestClientFactory.CreateHandler();

        var differences = new List<string>();
        var compared = 0;
        foreach (var property in typeof(HttpClientHandler).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!property.CanRead)
                continue;

            // Some are unsupported on the running platform, and some answer a fresh object every
            // read. Either way there is nothing to compare, and neither is what this is watching for.
            string? expected, actual;
            try
            {
                expected = Describe(property.GetValue(theirs));
                actual = Describe(property.GetValue(mine));
            }
            catch (Exception)
            {
                continue;
            }

            compared++;
            if (expected != actual)
                differences.Add($"{property.Name}: RestSharp {expected}, ours {actual}");
        }

        Assert.IsGreaterThan(10, compared, "Almost nothing was comparable, so this proved nothing.");
        Assert.IsEmpty(differences, string.Join("; ", differences));

        static string? Describe(object? value) => value switch
        {
            null => "<null>",
            System.Collections.ICollection collection => $"count {collection.Count}",
            _ => value.ToString(),
        };
    }

    [TestMethod]
    public void EveryClientSharesOneToken()
    {
        // The token is cached on the authenticator instance, so sharing the instance is what stops
        // each client fetching its own copy of the same credentials.
        using var factory = new RestClientFactory(Configuration());

        var events = factory.Create("Server:EventUrl");
        var organizations = factory.Create("Server:OrganizationUrl");

        Assert.IsNotNull(events.Options.Authenticator);
        Assert.AreSame(events.Options.Authenticator, organizations.Options.Authenticator);
    }

    [TestMethod]
    public void ClientsKeepTheirOwnBaseUrl()
    {
        using var factory = new RestClientFactory(Configuration());

        Assert.AreEqual("http://localhost/event", factory.Create("Server:EventUrl").Options.BaseUrl?.ToString());
        Assert.AreEqual("http://localhost/organization", factory.Create("Server:OrganizationUrl").Options.BaseUrl?.ToString());
    }

    [TestMethod]
    public void SerializationMatchesTheLibrarysOwnMessagePackClient()
    {
        // Create() reproduces RestClientExtensions.CreateWithMessagePack so that it can supply the
        // shared HttpClient, which that extension has no overload for. Compare the two directly so
        // the copy cannot drift from the original.
        using var factory = new RestClientFactory(Configuration());
        var mine = factory.Create("Server:EventUrl");
        using var theirs = new RestClientOptions("http://localhost/event").CreateWithMessagePack();

        CollectionAssert.AreEqual(theirs.AcceptedContentTypes, mine.AcceptedContentTypes);
        CollectionAssert.AreEqual(
            theirs.DefaultParameters.Select(p => $"{p.Name}={p.Value}").ToList(),
            mine.DefaultParameters.Select(p => $"{p.Name}={p.Value}").ToList());
    }

    [TestMethod]
    public void AMissingServerUrlNamesTheKeyItWanted()
    {
        using var factory = new RestClientFactory(Configuration());

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => factory.Create("Server:MissingUrl"));

        StringAssert.Contains(ex.Message, "Server:MissingUrl");
    }
}
