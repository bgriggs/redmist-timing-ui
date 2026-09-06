using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels.Design;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// Covers how the icon caches decide when to check a stored image against the server.
/// </summary>
/// <remarks>
/// Under the headless session because the cache decodes bitmaps, which needs a platform. The store
/// is faked: the point here is the caching policy - what is checked, how often, and what happens to
/// the result - not the disk and HTTP behavior <c>PersistentImageStoreTests</c> already covers.
///
/// Every revalidation is deliberately fire-and-forget, so the assertions poll rather than await.
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class ImageCacheRevalidationTests
{
    private const string Key = "org-1";

    [TestMethod]
    public Task NetworkBytes_AreNotCheckedAgainAsync() => HeadlessTest.OnDispatcher(async () =>
    {
        // Nothing came off disk, so there is nothing to confirm - the bytes are current already.
        var store = new FakeStore { Stored = new StoredImage([1, 2, 3], FromDisk: false) };
        var cache = new TestCache(store);

        Assert.IsNotNull(await cache.GetImageAsync(Key));
        await Settle();

        Assert.AreEqual(0, store.Revalidations);
    });

    [TestMethod]
    public Task StoredBytes_AreCheckedOnceAsync() => HeadlessTest.OnDispatcher(async () =>
    {
        var store = new FakeStore { OnRevalidate = () => RevalidationResult.Unchanged };
        var cache = new TestCache(store);

        await cache.GetImageAsync(Key);
        await WaitFor(() => store.Revalidations >= 1, "the first check");

        // Further loads answer from memory. Once the server has confirmed the image, asking again
        // this session would be a request per screen for a logo that changes a few times a year.
        for (var i = 0; i < 5; i++)
        {
            await cache.GetImageAsync(Key);
        }

        await Settle();
        Assert.AreEqual(1, store.Revalidations);
    });

    [TestMethod]
    public Task FailedCheck_IsRetriedOnALaterLoadAsync() => HeadlessTest.OnDispatcher(async () =>
    {
        // The launch-with-no-signal case, and the reason the check tracks what is owed rather than
        // what has been attempted. A check that is marked done when it starts is never retried, so
        // every logo would stay unverified until the app was restarted.
        var store = new FakeStore { OnRevalidate = () => RevalidationResult.Unanswered };
        var cache = new TestCache(store);

        await cache.GetImageAsync(Key);
        await WaitFor(() => store.Revalidations >= 1, "the first check");

        // Signal comes back, and the events list rebuilds - which reloads every icon.
        store.OnRevalidate = () => RevalidationResult.Unchanged;
        await WaitFor(
            async () =>
            {
                await cache.GetImageAsync(Key);
                return store.Revalidations >= 2;
            },
            "the check to be retried after it failed");

        // ...and once it is answered, it settles down again.
        var afterAnswer = store.Revalidations;
        for (var i = 0; i < 5; i++)
        {
            await cache.GetImageAsync(Key);
        }

        await Settle();
        Assert.AreEqual(afterAnswer, store.Revalidations, "An answered check should not be repeated.");
    });

    [TestMethod]
    public Task ChangedImage_ReplacesTheBitmapAndAnnouncesItAsync() => HeadlessTest.OnDispatcher(async () =>
    {
        var store = new FakeStore { OnRevalidate = () => RevalidationResult.Replaced([9, 9, 9]) };
        var cache = new TestCache(store);

        var updates = new List<(string Key, Bitmap Bitmap)>();
        cache.ImageUpdated += (key, bitmap) => updates.Add((key, bitmap));

        var first = await cache.GetImageAsync(Key);
        await WaitFor(() => updates.Count >= 1, "the changed image to be announced");

        Assert.AreEqual(Key, updates[0].Key);
        Assert.AreNotSame(first, updates[0].Bitmap, "A changed image should be a different bitmap.");
        Assert.AreSame(updates[0].Bitmap, cache.GetCachedImage(Key),
            "The announced bitmap has to be the one the cache now holds, or views would diverge.");
    });

    [TestMethod]
    public Task BytesThatDoNotDecode_AreDiscardedFromTheStoreAsync() => HeadlessTest.OnDispatcher(async () =>
    {
        // A 200 with a validator carrying something that is not an image - an error page from in
        // front of the CDN, or a file the filesystem lost part of. Nothing below the decode can
        // tell, so without discarding it here the entry would be re-read and re-rejected every
        // launch until it aged out three months later.
        var store = new FakeStore();
        var cache = new TestCache(store) { ShouldFailDecode = static _ => true };

        Assert.IsNull(await cache.GetImageAsync(Key));

        await WaitFor(() => store.Invalidations >= 1, "the unusable entry to be discarded");
    });

    [TestMethod]
    public Task ConcurrentLoadsOfOneKey_CheckItOnceAsync() => HeadlessTest.OnDispatcher(async () =>
    {
        // What PreloadImagesAsync does on every events-list rebuild: a burst of loads for the same
        // organizations at once. Without the in-flight guard each one starts its own conditional
        // GET, so a page of logos becomes a page of duplicate requests.
        var gate = new TaskCompletionSource<RevalidationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeStore { OnRevalidateAsync = () => gate.Task };
        var cache = new TestCache(store);

        // Prime the cache so every call below takes the memory-hit path, which is where a rebuild's
        // reloads land and where the check is scheduled from.
        await cache.GetImageAsync(Key);
        await WaitFor(() => store.Revalidations >= 1, "the first check to start");

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetImageAsync(Key)));
        await Settle();

        Assert.AreEqual(1, store.Revalidations, "One check should have been in flight for all of them.");

        gate.SetResult(RevalidationResult.Unchanged);
        await Settle();

        // And once it is answered, the burst does not restart it either.
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetImageAsync(Key)));
        await Settle();
        Assert.AreEqual(1, store.Revalidations);
    });

    [TestMethod]
    public Task ReplacementThatDoesNotDecode_IsDiscardedAndTheOldImageKeptAsync() => HeadlessTest.OnDispatcher(async () =>
    {
        // The stored bytes are fine; what the server sends back is not. The bitmap on screen has to
        // survive that, and the entry the store just wrote has to go - otherwise the next launch
        // reads back bytes already known to be unusable.
        var store = new FakeStore { OnRevalidate = () => RevalidationResult.Replaced([9, 9, 9]) };
        var cache = new TestCache(store) { ShouldFailDecode = static b => b.Length == 3 && b[0] == 9 };

        var updates = 0;
        cache.ImageUpdated += (_, _) => updates++;

        var original = await cache.GetImageAsync(Key);
        Assert.IsNotNull(original);

        await WaitFor(() => store.Invalidations >= 1, "the unusable replacement to be discarded");
        await Settle();

        Assert.AreEqual(0, updates, "A replacement that cannot be decoded is not an update.");
        Assert.AreSame(original, cache.GetCachedImage(Key), "The image already on screen has to stay.");
    });

    /// <summary>Lets any fire-and-forget work started by the calls above run to completion.</summary>
    private static async Task Settle()
    {
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(10);
        }
    }

    private static async Task WaitFor(Func<bool> condition, string what)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition(), $"Timed out waiting for {what}.");
    }

    private static async Task WaitFor(Func<Task<bool>> condition, string what)
    {
        for (var i = 0; i < 200; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    /// <summary>
    /// Stands in for the disk and the network, so a test can say what the store found without
    /// arranging files or responses to produce it.
    /// </summary>
    private sealed class FakeStore : PersistentImageStore
    {
        public FakeStore()
            : base(new DesignHttpClientFactory(), new DebugLoggerFactory(), cacheDirectory: null)
        {
        }

        public StoredImage Stored { get; set; } = new([1, 2, 3], FromDisk: true);

        public Func<RevalidationResult> OnRevalidate { get; set; } = () => RevalidationResult.Unchanged;

        /// <summary>Set to hold a check open, so a test can observe what happens while one is in flight.</summary>
        public Func<Task<RevalidationResult>>? OnRevalidateAsync { get; set; }

        private int revalidations;
        private int invalidations;

        public int Revalidations => Volatile.Read(ref revalidations);

        public int Invalidations => Volatile.Read(ref invalidations);

        public override Task<StoredImage> GetAsync(string url) => Task.FromResult(Stored);

        public override Task<RevalidationResult> RevalidateAsync(string url)
        {
            Interlocked.Increment(ref revalidations);
            return OnRevalidateAsync?.Invoke() ?? Task.FromResult(OnRevalidate());
        }

        public override Task InvalidateAsync(string url)
        {
            Interlocked.Increment(ref invalidations);
            return Task.CompletedTask;
        }
    }

    private sealed class TestCache(PersistentImageStore store)
        : ImageCacheServiceBase<string>(store, new DebugLoggerFactory().CreateLogger("TestCache"))
    {
        /// <summary>
        /// Which payloads fail to decode. The headless platform's decoder accepts any bytes at all,
        /// so there is no way to reach the decode-failure path with a real image otherwise. Chosen
        /// per payload rather than as a flag, because one test needs the stored bytes to decode and
        /// only the replacement to fail - and flipping a flag between the two is a race.
        /// </summary>
        public Func<byte[], bool> ShouldFailDecode { get; set; } = static _ => false;

        protected override string GetImageUrl(string key) => $"https://assets.example/logos/{key}.img";

        protected override Bitmap Decode(byte[] bytes) =>
            ShouldFailDecode(bytes) ? throw new InvalidOperationException("not an image") : base.Decode(bytes);
    }
}
