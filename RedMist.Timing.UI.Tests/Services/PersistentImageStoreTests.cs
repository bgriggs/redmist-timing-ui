using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels.Design;
using System.Net;

namespace RedMist.Timing.UI.Tests.Services;

/// <summary>
/// Covers the disk tier and the conditional GET behind the organization and sponsor icon caches.
/// </summary>
/// <remarks>
/// Driven through a scripted handler rather than the real CDN, so the assertions are about what the
/// store does with a response - what it stores, what it sends back on the next request, and what it
/// hands its caller - rather than about a network that is not there during a test run.
/// </remarks>
[TestClass]
public sealed class PersistentImageStoreTests
{
    private const string Url = "https://assets.example/logos/org-1.img";
    private static readonly DateTimeOffset Modified = new(2026, 8, 29, 11, 8, 57, TimeSpan.Zero);

    private string directory = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), "redmist-image-store-tests", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A sweep started by the store may still hold a handle. The directory is under the
            // temp path and uniquely named, so leaving it is harmless.
        }
    }

    private PersistentImageStore CreateStore(ScriptedHandler handler, string? cacheDirectory = null) =>
        new(new ScriptedHttpClientFactory(handler), new DebugLoggerFactory(), cacheDirectory ?? directory);

    [TestMethod]
    public async Task FirstGet_FetchesFromTheNetworkAndReportsItAsSuchAsync()
    {
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);

        var result = await store.GetAsync(Url);

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result.Bytes);
        Assert.IsFalse(result.FromDisk, "Bytes that just came off the network are current and must not be revalidated.");
        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public async Task SecondGet_ComesFromDiskWithoutAnotherRequestAsync()
    {
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        await CreateStore(handler).GetAsync(Url);

        // A separate instance over the same directory, which is what a later run of the app is.
        var result = await CreateStore(handler).GetAsync(Url);

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, result.Bytes);
        Assert.IsTrue(result.FromDisk, "A stored image should be reported as needing a revalidation.");
        Assert.AreEqual(1, handler.RequestCount, "The second launch should not re-download the image.");
    }

    [TestMethod]
    public async Task ResponseWithoutAValidator_IsNotStoredAsync()
    {
        // Nothing to send an If-Modified-Since against later. Storing it would mean serving it
        // unchanged until the entry aged out, which is the staleness the store exists to avoid.
        var handler = ScriptedHandler.Serving([1, 2, 3], lastModified: null);
        var store = CreateStore(handler);

        var first = await store.GetAsync(Url);
        var second = await store.GetAsync(Url);

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, first.Bytes);
        Assert.IsFalse(second.FromDisk);
        Assert.AreEqual(2, handler.RequestCount);
    }

    [TestMethod]
    public async Task NotFound_IsNotStoredSoALogoAddedLaterStillAppearsAsync()
    {
        var handler = ScriptedHandler.Returning(HttpStatusCode.NotFound);
        var store = CreateStore(handler);

        var missing = await store.GetAsync(Url);
        Assert.AreEqual(0, missing.Bytes.Length);

        handler.Respond = ScriptedHandler.Body([9, 9], Modified);
        var added = await store.GetAsync(Url);

        CollectionAssert.AreEqual(new byte[] { 9, 9 }, added.Bytes);
    }

    [TestMethod]
    public async Task Revalidate_AsksAgainstTheStoredLastModifiedAsync()
    {
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);
        await store.GetAsync(Url);

        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotModified);
        await store.RevalidateAsync(Url);

        Assert.AreEqual(2, handler.RequestCount);
        Assert.AreEqual(Modified, handler.IfModifiedSinceSeen[1],
            "The check has to quote the validator the server sent, or it cannot answer 304.");
    }

    [TestMethod]
    public async Task Revalidate_Unchanged_IsAnsweredWithNoReplacementAsync()
    {
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);
        await store.GetAsync(Url);

        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotModified);

        var result = await store.RevalidateAsync(Url);
        Assert.IsTrue(result.Answered, "A 304 settles the question.");
        Assert.IsNull(result.Bytes);
    }

    [TestMethod]
    public async Task Revalidate_Changed_ReturnsTheNewBytesAndReplacesTheStoredCopyAsync()
    {
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);
        await store.GetAsync(Url);

        var newer = Modified.AddDays(1);
        handler.Respond = ScriptedHandler.Body([7, 7, 7], newer);

        var fresh = await store.RevalidateAsync(Url);
        Assert.IsTrue(fresh.Answered);
        CollectionAssert.AreEqual(new byte[] { 7, 7, 7 }, fresh.Bytes);

        // The replacement is what a later launch gets, and it is checked against the new validator.
        var afterRestart = await CreateStore(handler).GetAsync(Url);
        CollectionAssert.AreEqual(new byte[] { 7, 7, 7 }, afterRestart.Bytes);

        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotModified);
        await store.RevalidateAsync(Url);
        Assert.AreEqual(newer, handler.IfModifiedSinceSeen[^1]);
    }

    [TestMethod]
    public async Task Revalidate_WithNothingStored_MakesNoRequestAsync()
    {
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);

        var result = await store.RevalidateAsync(Url);
        Assert.IsFalse(result.Answered, "Nothing was asked, so nothing has been settled.");
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task Revalidate_FailedRequest_ReportsItAsUnansweredAsync()
    {
        // The distinction the caller's retry is built on. Reporting a dead network the same way as
        // a 304 would retire the check on the strength of a request that never happened, and every
        // logo on a launch with no signal would stay unverified until the app was restarted.
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);
        await store.GetAsync(Url);

        handler.Respond = _ => throw new HttpRequestException("no network");

        var result = await store.RevalidateAsync(Url);
        Assert.IsFalse(result.Answered, "A request that never completed settles nothing.");
        Assert.IsNull(result.Bytes);

        handler.Respond = ScriptedHandler.Body([1, 2, 3], Modified);
        var stored = await CreateStore(handler).GetAsync(Url);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, stored.Bytes);
        Assert.IsTrue(stored.FromDisk, "A failed check must not discard what is on disk.");
    }

    [TestMethod]
    public async Task Revalidate_ServerError_ReportsItAsUnansweredAsync()
    {
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);
        await store.GetAsync(Url);

        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        Assert.IsFalse((await store.RevalidateAsync(Url)).Answered);
    }

    [TestMethod]
    public async Task ImageWithNoMetadata_IsRefusedRatherThanServedAsync()
    {
        // The half-written entry a process death between the two writes leaves behind. Serving it
        // would mean bytes with no validator, which is the one thing the store must never do.
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        await CreateStore(handler).GetAsync(Url);

        File.Delete(Directory.GetFiles(directory, "*.meta").Single());

        var result = await CreateStore(handler).GetAsync(Url);

        Assert.IsFalse(result.FromDisk, "An image with no validator is not an entry.");
        Assert.AreEqual(2, handler.RequestCount, "It should have been fetched again.");
    }

    [TestMethod]
    public async Task UnreadableMetadata_IsRefusedRatherThanServedAsync()
    {
        // Checking only that the metadata file exists is the subtle version of this bug: the bytes
        // would be served every session with nothing to revalidate them against.
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        await CreateStore(handler).GetAsync(Url);

        File.WriteAllText(Directory.GetFiles(directory, "*.meta").Single(), string.Empty);

        var result = await CreateStore(handler).GetAsync(Url);

        Assert.IsFalse(result.FromDisk, "A validator that does not parse is not a validator.");
        Assert.AreEqual(2, handler.RequestCount);
    }

    [TestMethod]
    public async Task Revalidate_ChangedButWithNoValidator_DropsTheStoredEntryAsync()
    {
        // A changed image from a host that sends no Last-Modified - a sponsor logo on someone
        // else's server, or the CDN behind a proxy that strips it. The new bytes cannot be stored,
        // because nothing could ever revalidate them. Keeping the old pair instead would be worse
        // than storing nothing: disk would hold an image the server no longer serves, alongside a
        // validator it has moved past, and every launch would paint the old logo, fetch the new one,
        // swap it on screen and write nothing - forever, since reading it keeps resetting its age.
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);
        await store.GetAsync(Url);
        await QuiesceAsync(store);

        handler.Respond = ScriptedHandler.Body([7, 7, 7], lastModified: null);
        var fresh = await store.RevalidateAsync(Url);
        CollectionAssert.AreEqual(new byte[] { 7, 7, 7 }, fresh.Bytes);

        Assert.AreEqual(0, Directory.GetFiles(directory).Length,
            "The superseded entry must not be left behind describing an image the server no longer has.");
    }

    [TestMethod]
    public async Task Invalidate_DropsTheEntrySoItIsFetchedAgainAsync()
    {
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);
        await store.GetAsync(Url);

        await store.InvalidateAsync(Url);

        Assert.AreEqual(0, Directory.GetFiles(directory).Length);

        var result = await store.GetAsync(Url);
        Assert.IsFalse(result.FromDisk);
        Assert.AreEqual(2, handler.RequestCount);
    }

    [TestMethod]
    public async Task Sweep_RemovesStrayFilesNoEntryAccountsForAsync()
    {
        // Ageing entries by their metadata means anything without metadata is invisible to the
        // expiry pass. Without a stray pass these accumulate forever, and EntryLifetime stops being
        // a bound on the directory at all.
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);
        await store.GetAsync(Url);
        await QuiesceAsync(store);

        var orphan = Path.Combine(directory, "0123456789ABCDEF0123456789ABCDEF.img");
        var temporary = Path.Combine(directory, "0123456789ABCDEF0123456789ABCDEF.img.a1b2c3d4.tmp");
        File.WriteAllBytes(orphan, [4, 5]);
        File.WriteAllBytes(temporary, [6, 7]);
        var stale = DateTime.UtcNow - TimeSpan.FromDays(1);
        File.SetLastWriteTimeUtc(orphan, stale);
        File.SetLastWriteTimeUtc(temporary, stale);

        await store.SweepExpiredAsync();

        Assert.IsFalse(File.Exists(orphan), "An image with no metadata is unreachable and should be reclaimed.");
        Assert.IsFalse(File.Exists(temporary), "A temporary from a write that never finished should be reclaimed.");

        // The live entry is untouched.
        Assert.IsTrue((await CreateStore(handler).GetAsync(Url)).FromDisk);
    }

    [TestMethod]
    public async Task Sweep_LeavesStrayFilesThatCouldStillBeInFlightAsync()
    {
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);
        await store.GetAsync(Url);
        await QuiesceAsync(store);

        // Freshly written, so indistinguishable from a write happening right now.
        var temporary = Path.Combine(directory, "0123456789ABCDEF0123456789ABCDEF.img.a1b2c3d4.tmp");
        File.WriteAllBytes(temporary, [6, 7]);

        await store.SweepExpiredAsync();

        Assert.IsTrue(File.Exists(temporary), "The sweep must not delete a write that is still in progress.");
    }

    [TestMethod]
    public async Task Sweep_RemovesEntriesNothingHasAskedForAsync()
    {
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);
        await store.GetAsync(Url);
        await QuiesceAsync(store);

        Backdate(PersistentImageStore.EntryLifetime + TimeSpan.FromDays(1));
        await store.SweepExpiredAsync();

        Assert.AreEqual(0, Directory.GetFiles(directory).Length);
    }

    [TestMethod]
    public async Task Sweep_KeepsEntriesInsideTheLifetimeAsync()
    {
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);
        await store.GetAsync(Url);
        await QuiesceAsync(store);

        Backdate(PersistentImageStore.EntryLifetime - TimeSpan.FromDays(1));
        await store.SweepExpiredAsync();

        var result = await CreateStore(handler).GetAsync(Url);
        Assert.IsTrue(result.FromDisk);
        Assert.AreEqual(1, handler.RequestCount);
    }

    [TestMethod]
    public async Task Sweep_KeepsAnImageAReadHasJustUsedAsync()
    {
        // The trap in ageing entries by write time: a logo shown on every launch for three months
        // is the most-used kind there is, and would be the first thing evicted if only writes
        // counted as use. It matters most on a phone that is usually offline, which is the one that
        // can never confirm anything with the server and would lose its whole cache.
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);
        await store.GetAsync(Url);
        await QuiesceAsync(store);

        Backdate(PersistentImageStore.EntryLifetime + TimeSpan.FromDays(1));

        // A plain read, with no network involved at all.
        Assert.IsTrue((await store.GetAsync(Url)).FromDisk);

        await store.SweepExpiredAsync();

        var result = await CreateStore(handler).GetAsync(Url);
        Assert.IsTrue(result.FromDisk, "Reading an entry is what using it means; it should reset the age.");
        Assert.AreEqual(1, handler.RequestCount, "Nothing here should have gone to the network.");
    }

    [TestMethod]
    public async Task Sweep_KeepsAnImageA304HasJustConfirmedAsync()
    {
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = CreateStore(handler);
        await store.GetAsync(Url);
        await QuiesceAsync(store);

        Backdate(PersistentImageStore.EntryLifetime + TimeSpan.FromDays(1));

        handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.NotModified);
        await store.RevalidateAsync(Url);

        await store.SweepExpiredAsync();

        var result = await CreateStore(handler).GetAsync(Url);
        Assert.IsTrue(result.FromDisk, "A 304 says the image is still in use and should reset its age.");
    }

    [TestMethod]
    public async Task WithNoCacheDirectory_FetchesEveryTimeAndKeepsWorkingAsync()
    {
        // The WebAssembly shape: nowhere to persist, so every load is a fetch and the browser's own
        // cache is what makes that cheap.
        var handler = ScriptedHandler.Serving([1, 2, 3], Modified);
        var store = new PersistentImageStore(new ScriptedHttpClientFactory(handler), new DebugLoggerFactory(), cacheDirectory: null);

        var first = await store.GetAsync(Url);
        var second = await store.GetAsync(Url);

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, first.Bytes);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, second.Bytes);
        Assert.IsFalse(second.FromDisk);
        Assert.AreEqual(2, handler.RequestCount);
        Assert.IsFalse((await store.RevalidateAsync(Url)).Answered);
    }

    /// <summary>
    /// Ages the entry by rewriting the metadata file's timestamp, which is what the sweep reads.
    /// Found by enumeration rather than by recomputing the store's file naming, so the test does not
    /// have to agree with it.
    /// </summary>
    /// <summary>
    /// Waits out the sweep the store starts by itself on the first request.
    /// </summary>
    /// <remarks>
    /// Without this, a test that backdates an entry and then sweeps explicitly is racing that one:
    /// if it is scheduled after the backdate it sees a ninety-day-old entry and removes it, and the
    /// test fails having proved nothing about the sweep it meant to exercise.
    /// </remarks>
    private static async Task QuiesceAsync(PersistentImageStore store)
    {
        if (store.AutomaticSweep is { } sweep)
        {
            await sweep;
        }
    }

    private void Backdate(TimeSpan age)
    {
        var meta = Directory.GetFiles(directory, "*.meta").Single();
        File.SetLastWriteTimeUtc(meta, DateTime.UtcNow - age);
    }

    private sealed class ScriptedHttpClientFactory(ScriptedHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public required Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; }

        public int RequestCount { get; private set; }

        /// <summary>
        /// The conditional header on each request in order. Recorded at send time because the store
        /// disposes the request message once the response is read.
        /// </summary>
        public List<DateTimeOffset?> IfModifiedSinceSeen { get; } = [];

        public static ScriptedHandler Serving(byte[] bytes, DateTimeOffset? lastModified) =>
            new() { Respond = Body(bytes, lastModified) };

        public static ScriptedHandler Returning(HttpStatusCode status) =>
            new() { Respond = _ => new HttpResponseMessage(status) };

        public static Func<HttpRequestMessage, HttpResponseMessage> Body(byte[] bytes, DateTimeOffset? lastModified) =>
            _ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                response.Content.Headers.LastModified = lastModified;
                return response;
            };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            IfModifiedSinceSeen.Add(request.Headers.IfModifiedSince);
            return Task.FromResult(Respond(request));
        }
    }
}
