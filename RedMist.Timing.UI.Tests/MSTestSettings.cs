using System.Globalization;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace RedMist.Timing.UI.Tests;

[TestClass]
public static class TestAssemblySetup
{
    /// <summary>
    /// Pins the culture for the whole run.
    /// </summary>
    /// <remarks>
    /// Several view models format times for display with the current culture (deliberately - a
    /// viewer should see their own locale's clock), so assertions on "2:05 PM" or "1:25.000" would
    /// otherwise fail on a machine set to fi-FI or ja-JP. Set at the default-thread level because
    /// per-test culture is unreliable once tests run in parallel across the thread pool.
    /// </remarks>
    [AssemblyInitialize]
    public static void Initialize(TestContext _)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
