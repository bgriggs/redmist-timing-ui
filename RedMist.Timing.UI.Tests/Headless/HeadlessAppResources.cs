using Avalonia;
using Avalonia.Controls;
using System.Reflection;

namespace RedMist.Timing.UI.Tests.Headless;

/// <summary>
/// What a headless test needs before it can build one of the app's real views.
/// </summary>
internal static class HeadlessAppResources
{
    /// <summary>
    /// Puts the app's own resources and a control theme in reach of the view under test.
    /// </summary>
    /// <remarks>
    /// HeadlessTestApp deliberately loads neither - see its remarks - so the view's StaticResource
    /// lookups for geometries and converters would throw, and with no control theme an ItemsControl
    /// gets no template and so never builds a panel at all. Both are added inside the dispatch and
    /// go away with it, leaving ResourceFallbackTests the bare application it needs.
    ///
    /// The trampoline is how Avalonia's own generated InitializeComponent loads compiled XAML.
    /// AvaloniaXamlLoader.Load cannot stand in for it: that fails on an Application built outside an
    /// AppBuilder, which is exactly the situation here.
    /// </remarks>
    public static void Load()
    {
        var app = new RedMist.Timing.UI.App();
        var populate = typeof(RedMist.Timing.UI.App).GetMethod("!XamlIlPopulateTrampoline",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(populate, "Avalonia no longer emits the populate trampoline - this needs another way to reach App.axaml's resources.");
        populate.Invoke(null, [app]);

        // Copied entry by entry: a ResourceDictionary refuses to be merged into a second owner.
        var resources = new ResourceDictionary();
        foreach (var entry in app.Resources)
            resources.Add(entry.Key, entry.Value!);

        Application.Current!.Resources.MergedDictionaries.Add(resources);
        Application.Current!.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
    }
}
