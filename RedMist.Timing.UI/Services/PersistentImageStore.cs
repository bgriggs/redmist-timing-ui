using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Keeps remote images on disk between runs and revalidates them against the server.
/// </summary>
/// <remarks>
/// The bitmap caches above this are memory only, so every cold start used to re-download every
/// logo on the events list. The assets run from about 4 KB to 90 KB each and an events page spans
/// several organizations, which is a few hundred KB on every launch - paid at a track, on cell
/// data, to fetch images that had not changed in months.
///
/// Freshness is handled by conditional GET rather than by a timer. The CDN sends Last-Modified on
/// every object and answers If-Modified-Since with a 304 and an empty body, so a check costs about
/// a kilobyte of headers and confirms the exact bytes rather than inferring from a size or an age.
/// That is why there is no max-age tier here: checking is cheap enough to always do, and a logo
/// replaced today appears today rather than whenever an expiry happened to elapse.
///
/// Nothing here is on the critical path. A caller gets the stored bytes immediately and the
/// revalidation runs behind it, so a slow or dead network - the normal condition at a track - shows
/// the logo that was already on disk instead of an empty box. A failure leaves the stored copy in
/// place and reports itself as unanswered, so the check is retried rather than counted as done.
///
/// The one invariant everything else rests on: bytes are never served without the validator that
/// goes with them. An entry is the pair, and a read that cannot produce both is a miss.
/// </remarks>
public class PersistentImageStore
{
    /// <summary>
    /// Named client for image traffic. Configured apart from the app's default client for the two
    /// reasons that matter here: HTTP/2, so a page's worth of revalidations multiplex over one
    /// connection instead of opening a TLS handshake each, and a short timeout, because none of
    /// this is worth holding a request open for the default 100 seconds when there is always a
    /// stored copy to fall back to.
    /// </summary>
    public const string HttpClientName = "cdn-images";

    /// <summary>
    /// How long an entry nothing has used is kept. Not a freshness window - revalidation handles
    /// that - but a floor that reclaims the space taken by organizations and sponsors which have
    /// stopped appearing, so the directory does not grow without bound.
    /// </summary>
    internal static readonly TimeSpan EntryLifetime = TimeSpan.FromDays(90);

    /// <summary>
    /// How long a file with no live entry behind it is left alone before the sweep removes it.
    /// </summary>
    /// <remarks>
    /// Only stray files are aged this way: a temporary from a write that never finished, or image
    /// bytes whose metadata never landed. Both are indistinguishable from a write that is happening
    /// right now, which is what the grace period is for - long enough that no in-flight write can
    /// be mistaken for wreckage, short enough that wreckage does not outlive the session by much.
    /// </remarks>
    private static readonly TimeSpan OrphanGrace = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger logger;
    private readonly string? cacheDirectory;

    /// <summary>Set once the first caller has kicked off the sweep for expired entries.</summary>
    private int sweepStarted;

    /// <summary>
    /// Set after a disk operation has failed, so a store that cannot be read or written reports it
    /// once and then behaves as if it were not there rather than logging on every image.
    /// </summary>
    private volatile bool diskDisabled;

    public PersistentImageStore(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
        : this(httpClientFactory, loggerFactory, ResolveCacheDirectory())
    {
    }

    /// <param name="cacheDirectory">
    /// Where entries live, or null to run without a disk tier. A parameter so tests and the
    /// designer can point the store somewhere other than the real profile.
    /// </param>
    internal PersistentImageStore(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, string? cacheDirectory)
    {
        this.httpClientFactory = httpClientFactory;
        logger = loggerFactory.CreateLogger<PersistentImageStore>();
        this.cacheDirectory = cacheDirectory;
    }

    /// <summary>
    /// Where entries live, or null when this platform has nowhere to put them.
    /// </summary>
    /// <remarks>
    /// Null under WebAssembly, where LocalApplicationData is an in-memory Emscripten filesystem that
    /// does not survive a reload - so a store there would do the work of caching and keep none of
    /// it. Nothing is lost by skipping it: the browser applies Cache-Control and If-Modified-Since
    /// to these requests itself, which is the same mechanism this class implements by hand for the
    /// platforms whose handler does not.
    ///
    /// On phones the cache folder rather than the data folder, which is not a detail. Everything
    /// here is re-downloadable, and the data folder is backed up: on iOS that means Apple's storage
    /// guidelines are against it, and on Android it means spending the app's 25 MB auto-backup
    /// quota on logos. The cache folder is excluded from backup on both, and the OS may purge it
    /// under storage pressure - which for this data is the correct outcome, not a hazard.
    ///
    /// The two phones get there differently. iOS resolves InternetCache to Library/Caches, which is
    /// what <see cref="CrashReporting"/> already relies on. Android resolves it to nothing at all -
    /// the runtime maps no special folder to Context.CacheDir - so the head has to hand the path
    /// over through <see cref="CacheRootOverride"/>, and without it this would silently fall back
    /// to the backed-up directory it is trying to avoid.
    /// </remarks>
    private static string? ResolveCacheDirectory()
    {
        if (OperatingSystem.IsBrowser())
        {
            return null;
        }

        var root = CacheRootOverride ?? string.Empty;

        if (string.IsNullOrEmpty(root) && OperatingSystem.IsIOS())
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.InternetCache);
        }

        if (string.IsNullOrEmpty(root))
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        return Path.Combine(root, "RedMist.Timing.UI", "image-cache");
    }

    /// <summary>
    /// The directory the cache is created under, for a platform that has to name it itself.
    /// </summary>
    /// <remarks>
    /// Set by a platform head before the host is built, the same way
    /// <see cref="App.ScreenWakeServiceFactory"/> and <see cref="App.PlatformLogProviderFactory"/>
    /// are. Only Android needs it: .NET maps no <see cref="Environment.SpecialFolder"/> to
    /// Context.CacheDir, so asking Android is the only way to find the directory that is excluded
    /// from auto-backup.
    /// </remarks>
    public static string? CacheRootOverride { get; set; }

    /// <summary>
    /// The bytes for a URL, from disk when they are held and from the network otherwise.
    /// </summary>
    /// <returns>
    /// The bytes and where they came from. Empty when the image could not be fetched and is not
    /// stored. <see cref="StoredImage.FromDisk"/> tells the caller whether a revalidation is worth
    /// scheduling: bytes that just came off the network are already current.
    /// </returns>
    public virtual async Task<StoredImage> GetAsync(string url)
    {
        StartSweepOnce();

        // Off the calling thread on purpose. The events list starts its icon load from the UI
        // thread, and a synchronous read of a dozen files there is a stall at the exact moment the
        // list is trying to paint.
        var entry = await OnDiskThreadAsync(() =>
        {
            var read = ReadEntry(url);
            if (read is not null)
            {
                // Reading the entry is what "used" means. Without this an offline phone that shows
                // the same logos every launch for ninety days would have the whole cache swept out
                // from under it, having never once been able to confirm anything with the server.
                TouchEntry(url);
            }

            return read;
        }).ConfigureAwait(false);

        if (entry is not null)
        {
            return new StoredImage(entry.Value.Bytes, FromDisk: true);
        }

        var fetched = await FetchAsync(url, ifModifiedSince: null).ConfigureAwait(false);
        if (fetched.Bytes.Length > 0)
        {
            await OnDiskThreadAsync(() => WriteEntry(url, fetched.Bytes, fetched.LastModified)).ConfigureAwait(false);
        }

        return new StoredImage(fetched.Bytes, FromDisk: false);
    }

    /// <summary>
    /// Asks the server whether a stored image has changed.
    /// </summary>
    /// <remarks>
    /// Whether the server answered is reported separately from what it said, because the caller
    /// records the check as done and will not repeat it this session. Collapsing "unchanged" and
    /// "could not ask" into one result reads fine here and is wrong there: it would retire the
    /// check on the strength of a request that never completed, so a launch with no signal would
    /// leave every logo unverified until the app was restarted - the case the disk tier exists for.
    /// </remarks>
    public virtual async Task<RevalidationResult> RevalidateAsync(string url)
    {
        var entry = await OnDiskThreadAsync(() => ReadEntry(url)).ConfigureAwait(false);
        if (entry is null)
        {
            // Nothing stored, or an entry that cannot produce a validator. Nothing was asked, so
            // nothing has been settled.
            return RevalidationResult.Unanswered;
        }

        var fetched = await FetchAsync(url, entry.Value.LastModified).ConfigureAwait(false);
        if (fetched.Failed)
        {
            return RevalidationResult.Unanswered;
        }

        if (fetched.NotModified)
        {
            await OnDiskThreadAsync(() => TouchEntry(url)).ConfigureAwait(false);
            return RevalidationResult.Unchanged;
        }

        if (fetched.Bytes.Length == 0)
        {
            return RevalidationResult.Unanswered;
        }

        await OnDiskThreadAsync(() => WriteEntry(url, fetched.Bytes, fetched.LastModified)).ConfigureAwait(false);
        return RevalidationResult.Replaced(fetched.Bytes);
    }

    /// <summary>
    /// Drops an entry whose bytes turned out to be unusable.
    /// </summary>
    /// <remarks>
    /// The store cannot tell an image from anything else that arrives with a 200 and a
    /// Last-Modified - these assets are served as application/octet-stream, so the content type
    /// says nothing - and only the caller, at the point it tries to decode, finds out. Without a
    /// way to say so, bytes that fail to decode would be re-read and re-rejected every launch until
    /// they aged out, which is a logo missing for three months rather than for one session.
    /// </remarks>
    public virtual Task InvalidateAsync(string url) => OnDiskThreadAsync(() =>
    {
        var metaPath = MetaPath(url);
        var imagePath = ImagePath(url);
        if (metaPath is not null && imagePath is not null)
        {
            DropEntry(imagePath, metaPath, url);
        }
    });

    /// <summary>
    /// Removes both halves of an entry.
    /// </summary>
    /// <remarks>
    /// Metadata first, so a failure partway leaves an entry no read will accept rather than image
    /// bytes with a validator that no longer describes them. An image left behind that way is
    /// collected by the sweep's stray pass.
    /// </remarks>
    private void DropEntry(string imagePath, string metaPath, string url)
    {
        try
        {
            DeleteIfExists(metaPath);
            DeleteIfExists(imagePath);
            logger.LogInformation("Discarded the stored image for {Url}", url);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not discard the stored image for {Url}", url);
        }
    }

    private readonly record struct FetchResult(byte[] Bytes, DateTimeOffset? LastModified, bool NotModified, bool Failed);

    private async Task<FetchResult> FetchAsync(string url, DateTimeOffset? ifModifiedSince)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (ifModifiedSince is { } since)
            {
                request.Headers.IfModifiedSince = since;
            }

            using var response = await httpClient.SendAsync(request).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                return new FetchResult([], null, NotModified: true, Failed: false);
            }

            if (!response.IsSuccessStatusCode)
            {
                // Includes the 404 an organization without a logo returns. Deliberately not stored:
                // the CDN marks those no-cache, and persisting the absence would keep a logo added
                // later from appearing until the entry expired.
                //
                // Reported as a failure rather than as an answer, because most of these are
                // transient and the alternative is retiring the check on the strength of a 503. A
                // genuinely deleted logo costs a small request per list rebuild until the entry
                // ages out, which is the cheaper way to be wrong.
                logger.LogDebug("Image request for {Url} returned {Status}", url, (int)response.StatusCode);
                return new FetchResult([], null, NotModified: false, Failed: true);
            }

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return new FetchResult(bytes, response.Content.Headers.LastModified, NotModified: false, Failed: false);
        }
        catch (Exception ex)
        {
            // Covers the short timeout as well as an unreachable network. Warning rather than
            // error: the caller has a stored copy or an empty logo, neither of which is a fault.
            logger.LogWarning(ex, "Image request for {Url} failed", url);
            return new FetchResult([], null, NotModified: false, Failed: true);
        }
    }

    /// <summary>
    /// Runs a piece of disk work away from the caller's thread, or skips it when there is no disk.
    /// </summary>
    private Task OnDiskThreadAsync(Action work)
    {
        if (cacheDirectory is null || diskDisabled)
        {
            return Task.CompletedTask;
        }

        return Task.Run(work);
    }

    private Task<T?> OnDiskThreadAsync<T>(Func<T?> work) where T : struct
    {
        if (cacheDirectory is null || diskDisabled)
        {
            return Task.FromResult<T?>(null);
        }

        return Task.Run(work);
    }

    // Two files per entry, and the metadata is written second so its presence is what commits the
    // entry. An interrupted write leaves image bytes that no read will accept, which are re-fetched
    // and overwritten rather than served with no validator behind them.
    private string? ImagePath(string url) => EntryPath(url, ".img");

    private string? MetaPath(string url) => EntryPath(url, ".meta");

    private string? EntryPath(string url, string extension)
    {
        if (cacheDirectory is null || diskDisabled)
        {
            return null;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32];
        return Path.Combine(cacheDirectory, hash + extension);
    }

    /// <summary>
    /// An entry: the image and the validator that goes with it.
    /// </summary>
    internal readonly record struct Entry(byte[] Bytes, DateTimeOffset LastModified);

    /// <summary>
    /// Reads an entry, or null when there is not a complete one to read.
    /// </summary>
    /// <remarks>
    /// The validator is parsed here rather than left to whoever asks for it later, so there is one
    /// answer to "is this entry usable" and every caller gets it. Checking only that the metadata
    /// file exists is the subtle version of the same code and is not equivalent: a truncated or
    /// empty metadata file passes that check, and the bytes would then be served every session with
    /// nothing to revalidate them against - the exact state writing refuses to create.
    /// </remarks>
    private Entry? ReadEntry(string url)
    {
        var imagePath = ImagePath(url);
        var metaPath = MetaPath(url);
        if (imagePath is null || metaPath is null)
        {
            return null;
        }

        try
        {
            if (!File.Exists(imagePath) || !File.Exists(metaPath))
            {
                return null;
            }

            if (!DateTimeOffset.TryParse(File.ReadAllText(metaPath), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var lastModified))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(imagePath);
            return bytes.Length > 0 ? new Entry(bytes, lastModified) : null;
        }
        catch (Exception ex)
        {
            NoteDiskFailure(ex, "read");
            return null;
        }
    }

    private void WriteEntry(string url, byte[] bytes, DateTimeOffset? lastModified)
    {
        var imagePath = ImagePath(url);
        var metaPath = MetaPath(url);
        if (imagePath is null || metaPath is null)
        {
            return;
        }

        if (lastModified is null)
        {
            // Without a validator there is nothing to revalidate against later, and an entry that
            // can never be checked is worse than no entry: it would be served unchanged until
            // EntryLifetime ran out. So this is not stored - but whatever is already there has to
            // go with it. Simply returning would leave the previous image and the previous
            // validator standing while the caller has been handed different bytes, and the pair on
            // disk would then describe an image the server no longer serves. Every launch after
            // that would paint the old logo, revalidate against a validator the server has moved
            // past, replace it on screen, and store nothing - forever, since reading it keeps
            // resetting its lifetime.
            DropEntry(imagePath, metaPath, url);
            return;
        }

        try
        {
            Directory.CreateDirectory(cacheDirectory!);

            // The old validator goes first. Replacing an entry in place would otherwise have a
            // window the commit order is supposed to rule out: new bytes written, old metadata
            // still standing, and an interruption there leaves an entry that reads as complete
            // while describing itself with the wrong Last-Modified.
            DeleteIfExists(metaPath);

            // Written through a temporary and moved into place, so a reader never sees a half
            // written image. Several images are fetched in parallel, and a torn file would decode
            // as a corrupt bitmap rather than fail in a way anyone would notice.
            WriteAtomic(imagePath, path => File.WriteAllBytes(path, bytes));
            WriteAtomic(metaPath, path => File.WriteAllText(path, lastModified.Value.ToString("o", CultureInfo.InvariantCulture)));
        }
        catch (Exception ex)
        {
            NoteDiskFailure(ex, "write");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void WriteAtomic(string path, Action<string> write)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            write(temporary);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch
            {
                // The write already failed; a leftover temporary is not worth a second exception.
                // The sweep collects it later on OrphanGrace.
            }

            throw;
        }
    }

    /// <summary>
    /// Marks an entry as still in use. The metadata file's write time is what
    /// <see cref="SweepExpiredAsync"/> ages against, so this is the whole of "last used".
    /// </summary>
    private void TouchEntry(string url)
    {
        var metaPath = MetaPath(url);
        if (metaPath is null)
        {
            return;
        }

        try
        {
            if (File.Exists(metaPath))
            {
                File.SetLastWriteTimeUtc(metaPath, DateTime.UtcNow);
            }
        }
        catch (Exception ex)
        {
            // Not worth disabling the store over: the entry stays usable, it just ages from when it
            // was last written rather than when it was last used.
            logger.LogDebug(ex, "Could not touch the cache entry for {Url}", url);
        }
    }

    /// <summary>
    /// Records a failed disk operation, turning the store off only when the location itself looks
    /// unusable.
    /// </summary>
    /// <remarks>
    /// The distinction is worth drawing, because disabling is permanent for the run. A bare
    /// IOException is usually momentary - a file briefly locked by a concurrent read, a volume
    /// briefly full - and treating one of those as fatal would trade a single cache miss for a
    /// whole session of them. A permissions or path fault will not fix itself, and retrying it once
    /// per image is just noise.
    /// </remarks>
    private void NoteDiskFailure(Exception ex, string operation)
    {
        if (ex is UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException or NotSupportedException)
        {
            DisableDisk(ex, operation);
            return;
        }

        logger.LogDebug(ex, "Image cache {Operation} failed; treating it as a miss", operation);
    }

    private void DisableDisk(Exception ex, string operation)
    {
        if (diskDisabled)
        {
            return;
        }

        diskDisabled = true;
        logger.LogWarning(ex, "Image cache disabled after a failed {Operation}; images will be fetched each session", operation);
    }

    private void StartSweepOnce()
    {
        if (cacheDirectory is null || diskDisabled)
        {
            return;
        }

        if (Interlocked.Exchange(ref sweepStarted, 1) != 0)
        {
            return;
        }

        AutomaticSweep = SweepExpiredAsync();
    }

    /// <summary>
    /// The sweep started by the first image request, once there is one.
    /// </summary>
    /// <remarks>
    /// Exposed so a test can wait for it. It is unobserved by design in the app - nothing is
    /// waiting on a housekeeping pass - but a test that arranges file timestamps and then sweeps
    /// explicitly is racing this one, and the flake would look like the sweep deleting an entry the
    /// test had just refreshed.
    /// </remarks>
    internal Task? AutomaticSweep { get; private set; }

    /// <summary>
    /// Drops entries nothing has used in <see cref="EntryLifetime"/>, and the stray files that no
    /// entry accounts for.
    /// </summary>
    /// <remarks>
    /// Runs once per process, off the first image request rather than at startup, so it costs
    /// nothing on a launch that never shows an image. On a thread of its own, because it stats
    /// every file in the directory and the first image request comes from the UI thread.
    ///
    /// The stray pass is what makes <see cref="EntryLifetime"/> an actual bound. Ageing entries by
    /// their metadata means anything without metadata is invisible to it: a temporary from a write
    /// the process did not survive, image bytes whose metadata never landed, or the image half of a
    /// pair whose deletion below got as far as the metadata and then failed. None of those is ever
    /// read, and none would ever be removed either.
    /// </remarks>
    internal Task SweepExpiredAsync() => Task.Run(() =>
    {
        try
        {
            if (cacheDirectory is null || !Directory.Exists(cacheDirectory))
            {
                return;
            }

            var now = DateTime.UtcNow;
            var expiry = now - EntryLifetime;
            var strayCutoff = now - OrphanGrace;
            var removed = 0;

            foreach (var metaPath in Directory.EnumerateFiles(cacheDirectory, "*.meta"))
            {
                if (Age(metaPath) is not { } written || written >= expiry)
                {
                    continue;
                }

                // Metadata first, for the same reason it is written last: without it the image is
                // already unreadable, so an interrupted sweep leaves nothing that can be served.
                // Should the image deletion then fail, the stray pass below collects it.
                if (TryDelete(metaPath))
                {
                    TryDelete(Path.ChangeExtension(metaPath, ".img"));
                    removed++;
                }
            }

            var strays = 0;
            foreach (var path in Directory.EnumerateFiles(cacheDirectory))
            {
                var extension = Path.GetExtension(path);
                var orphanedImage = extension.Equals(".img", StringComparison.OrdinalIgnoreCase)
                    && !File.Exists(Path.ChangeExtension(path, ".meta"));
                var abandonedTemporary = extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase);

                if (!orphanedImage && !abandonedTemporary)
                {
                    continue;
                }

                // Aged, because a write happening right now looks exactly like wreckage from one
                // that did not finish.
                if (Age(path) is { } written && written < strayCutoff && TryDelete(path))
                {
                    strays++;
                }
            }

            if (removed > 0 || strays > 0)
            {
                logger.LogInformation("Image cache sweep removed {Removed} entries unused for {Days} days and {Strays} stray files",
                    removed, EntryLifetime.TotalDays, strays);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Image cache sweep failed");
        }
    });

    private DateTime? Age(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read the age of {Path}", path);
            return null;
        }
    }

    private bool TryDelete(string path)
    {
        try
        {
            DeleteIfExists(path);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not remove the cache file {Path}", path);
            return false;
        }
    }
}

/// <param name="Bytes">The image, or empty when it could not be obtained.</param>
/// <param name="FromDisk">
/// True when these bytes came from the store and have not been checked against the server this
/// session. False for bytes just fetched, which are current by definition.
/// </param>
public readonly record struct StoredImage(byte[] Bytes, bool FromDisk);

/// <summary>
/// What a revalidation found, and whether it found anything at all.
/// </summary>
/// <param name="Bytes">The replacement image, or null when there is nothing to replace.</param>
/// <param name="Answered">
/// True when the server settled the question - the image is unchanged, or here is the new one.
/// False when it was never asked or never replied, which leaves the check still owed.
/// </param>
public readonly record struct RevalidationResult(byte[]? Bytes, bool Answered)
{
    /// <summary>Nothing was settled; the check should be tried again.</summary>
    public static RevalidationResult Unanswered => new(null, Answered: false);

    /// <summary>The server confirmed the stored image is current.</summary>
    public static RevalidationResult Unchanged => new(null, Answered: true);

    /// <summary>The server sent a new image, which is now stored.</summary>
    public static RevalidationResult Replaced(byte[] bytes) => new(bytes, Answered: true);
}
