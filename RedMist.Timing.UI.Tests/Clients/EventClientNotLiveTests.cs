using MessagePack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Services;
using System.Net;
using System.Net.Http.Headers;

namespace RedMist.Timing.UI.Tests.Clients;

/// <summary>
/// Covers how the status call reports an event that has nothing running.
/// </summary>
/// <remarks>
/// The server answers 404 once an event's processor is gone - finished, not started, or
/// unreachable. The live timing screen polls that call, and used to treat the answer as a failure:
/// three attempts per poll and an error report at the end of each, for as long as the screen stayed
/// open on a finished event. One viewer's phone did that for most of a day.
///
/// Driven through a real RestClient over a stubbed handler rather than by throwing the exception by
/// hand, because the translation rests on what RestSharp builds for a response - and that depends on
/// the body as well as the status, which is exactly the part a hand-thrown exception would skip.
/// </remarks>
[TestClass]
public sealed class EventClientNotLiveTests
{
    private sealed class FixedResponseHandler(HttpStatusCode status, Func<HttpContent>? body) : HttpMessageHandler
    {
        private int requests;

        public int Requests => Volatile.Read(ref requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref requests);
            var response = new HttpResponseMessage(status) { RequestMessage = request };
            if (body is not null)
                response.Content = body();
            return Task.FromResult(response);
        }
    }

    private sealed class Server : IDisposable
    {
        private readonly RestClientFactory factory;

        public Server(HttpStatusCode status, Func<HttpContent>? body = null)
        {
            Handler = new FixedResponseHandler(status, body);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Server:EventUrl"] = "http://localhost/event" })
                .Build();

            // No authenticator: the token request would otherwise go to a Keycloak that is not there,
            // and fail before the call under test is ever made.
            factory = new RestClientFactory(configuration, authenticator: null, Handler);
            Client = new EventClient(factory, Logs, new EventAccessCodeStore(new MockPreferencesService()));
        }

        public FixedResponseHandler Handler { get; }
        public RecordingLoggerFactory Logs { get; } = new();
        public EventClient Client { get; }

        public void Dispose() => factory.Dispose();
    }

    /// <summary>
    /// What an action declared as producing MessagePack sends for <c>NotFound("...")</c> or
    /// <c>StatusCode(408, "...")</c>: the message itself, serialized.
    /// </summary>
    private static Func<HttpContent> MessagePackString(string text) => () =>
    {
        var content = new ByteArrayContent(MessagePackSerializer.Serialize(text));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-msgpack");
        return content;
    };

    [TestMethod]
    public async Task NotFound_ReadsAsNothingLive()
    {
        using var server = new Server(HttpStatusCode.NotFound);

        var thrown = await Assert.ThrowsExactlyAsync<EventNotLiveException>(() => server.Client.LoadEventStatusAsync(7));

        Assert.AreEqual(7, thrown.EventId);
    }

    [TestMethod]
    public async Task NotFound_WithAMessageInTheBody_StillReadsAsNothingLive()
    {
        // The StatusApi's own "no processor" answer carries a message. RestSharp deserializes the
        // body of a failed response too, and when that fails it replaces the exception carrying the
        // status with the serializer's - so the status has to be read from the response, not from
        // whichever exception RestSharp settled on.
        using var server = new Server(HttpStatusCode.NotFound, MessagePackString("Event processor endpoint not found"));

        await Assert.ThrowsExactlyAsync<EventNotLiveException>(() => server.Client.LoadEventStatusAsync(7));
    }

    [TestMethod]
    public async Task ARefusalWithAMessageInTheBody_IsReportedByItsStatus()
    {
        // REDMIST-APP-2G: the server's 408 "Request timeout" arrived as "Unexpected msgpack code 175
        // (fixstr)", which reads as a defect in the app and hides the status crash reporting groups by.
        using var server = new Server(HttpStatusCode.RequestTimeout, MessagePackString("Request timeout"));

        var thrown = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => server.Client.LoadEventStatusAsync(7));

        Assert.AreEqual(HttpStatusCode.RequestTimeout, thrown.StatusCode);
    }

    [TestMethod]
    public async Task ASuccessThatCannotBeRead_IsStillReportedAsUnreadable()
    {
        // The control for the two above: the status only stands in for the serializer's exception when
        // the server refused. A 200 whose body will not deserialize is a real defect and says so.
        using var server = new Server(HttpStatusCode.OK, MessagePackString("not a session state"));

        var thrown = await Assert.ThrowsAsync<Exception>(() => server.Client.LoadEventStatusAsync(7));

        Assert.IsNotInstanceOfType<HttpRequestException>(thrown, $"Got {thrown.GetType().Name}: {thrown.Message}");
    }

    [TestMethod]
    [DataRow(HttpStatusCode.TooManyRequests)]
    [DataRow(HttpStatusCode.InternalServerError)]
    [DataRow(HttpStatusCode.BadGateway)]
    public async Task OtherRefusals_AreStillFailures(HttpStatusCode status)
    {
        // Only a 404 means there is nothing running. A rate limit or a server fault is a failure,
        // and is worth both retrying and reporting.
        using var server = new Server(status);

        var thrown = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => server.Client.LoadEventStatusAsync(7));

        Assert.AreEqual(status, thrown.StatusCode);
    }

    [TestMethod]
    public async Task NothingLive_IsNeitherRetriedNorReported()
    {
        using var server = new Server(HttpStatusCode.NotFound);

        await Assert.ThrowsExactlyAsync<EventNotLiveException>(() => server.Client.ExecuteWithRetryAsync(
            () => server.Client.LoadEventStatusAsync(7), nameof(EventClient.LoadEventStatusAsync)));

        Assert.AreEqual(1, server.Handler.Requests, "Asking again half a second later gets the same answer.");
        Assert.IsFalse(server.Logs.Entries.Any(e => e.Level >= LogLevel.Warning),
            "An event with nothing running is not a fault, so it must not be logged as one.");
    }

    [TestMethod]
    public async Task ARealFailure_IsStillRetried()
    {
        // The control for the test above: the early exit has to be for this answer alone, not a
        // retry loop that no longer retries anything.
        using var server = new Server(HttpStatusCode.InternalServerError);

        var result = await server.Client.ExecuteWithRetryAsync(
            () => server.Client.LoadEventStatusAsync(7), nameof(EventClient.LoadEventStatusAsync), maxRetries: 2);

        Assert.IsNull(result);
        Assert.AreEqual(2, server.Handler.Requests);
    }
}
