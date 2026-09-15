using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.Tests.ViewModels;
using System.Reflection;

namespace RedMist.Timing.UI.Tests.Clients;

/// <summary>
/// Covers how HubClient opens its connection to the status hub.
/// </summary>
[TestClass]
public sealed class HubClientConnectionTests
{
    /// <summary>A hub client that hands out the connection it builds. Building one does not connect.</summary>
    private sealed class ConnectionBuildingHubClient()
        : HubClient(new RecordingLoggerFactory(), TestViewModelFactory.CreateConfiguration(), new EventAccessCodeStore(new MockPreferencesService()))
    {
        public HubConnection Build() => GetConnection();
    }

    /// <summary>
    /// The options a built connection will connect with. SignalR keeps them in private fields - the
    /// connection factory on the hub connection, and the options on that factory - so a SignalR
    /// upgrade that renames either fails here rather than letting the test pass without looking.
    /// </summary>
    private static HttpConnectionOptions OptionsOf(HubConnection connection)
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        var factory = typeof(HubConnection).GetField("_connectionFactory", Private)?.GetValue(connection);
        Assert.IsNotNull(factory, "SignalR no longer keeps the connection factory where this test looks for it.");
        var options = factory.GetType().GetField("_httpConnectionOptions", Private)?.GetValue(factory) as HttpConnectionOptions;
        Assert.IsNotNull(options, "SignalR no longer keeps the connection options where this test looks for them.");
        return options;
    }

    [TestMethod]
    public async Task TheHub_ConnectsWithTheWebSocketAlone()
    {
        // A negotiate creates the connection on the replica that answers it, so an upgrade that
        // reaches another replica is answered 404 and costs a retry - about one hub upgrade in six on a
        // live weekend with two status API replicas, before the ingress hashed on the client's address.
        // With no negotiate there is one request to route.
        var connection = new ConnectionBuildingHubClient().Build();
        try
        {
            var options = OptionsOf(connection);

            Assert.IsTrue(options.SkipNegotiation, "The hub negotiates before it connects.");
            Assert.AreEqual(HttpTransportType.WebSockets, options.Transports,
                "SignalR only skips negotiation when WebSockets is the only transport.");
            Assert.IsNotNull(options.AccessTokenProvider, "The access token still has to go with the upgrade.");
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}
