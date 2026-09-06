using RedMist.Timing.UI.Services;

namespace RedMist.Timing.UI.ViewModels.Design;

/// <summary>
/// The image store the design-time view models hand to their icon caches.
/// </summary>
/// <remarks>
/// Built with no cache directory, so the designer never reads or writes the real profile - a
/// preview that populated the running app's image cache would be a surprising thing for opening a
/// XAML file to do, and the sweep for expired entries has no business firing there either.
/// </remarks>
internal static class DesignImageStore
{
    public static PersistentImageStore Create() =>
        new(new DesignHttpClientFactory(), new DebugLoggerFactory(), cacheDirectory: null);
}
