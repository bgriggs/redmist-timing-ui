using Microsoft.Extensions.Configuration;
using RedMist.Timing.UI.Services;

namespace RedMist.Timing.UI.Tests.Services;

/// <summary>
/// Covers how crash reporting reads its configuration, and the safety contract around it: it must
/// never be the reason the app fails to start.
/// </summary>
/// <remarks>
/// No test here calls <c>CrashReporting.Init</c>, and none should. On a developer machine it
/// resolves the real DSN out of user secrets, starts a Sentry session and sends anything captured
/// afterwards to the live project - so a test suite run would show up as production telemetry.
///
/// The configuration decisions are asserted through <c>CrashReporting.ReadSettings</c> against a
/// supplied configuration rather than by calling <c>Init</c>. Init reads the embedded appsettings
/// plus the developer's own user secrets, so a test driven through it would assert whatever happens
/// to be on the machine running it - and would flip from passing to failing the moment someone set
/// a real DSN locally.
/// </remarks>
[TestClass]
public sealed class CrashReportingTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [TestMethod]
    public void ReadSettings_WithNoDsn_DisablesReporting()
    {
        // The shipped appsettings carries an empty DSN, so this is the state of any build that has
        // not had one provisioned.
        var settings = CrashReporting.ReadSettings(Config());

        Assert.IsFalse(settings.ReportingEnabled);
    }

    [TestMethod]
    public void ReadSettings_WithABlankDsn_DisablesReporting()
    {
        var settings = CrashReporting.ReadSettings(Config(("Sentry:Dsn", "   ")));

        Assert.IsFalse(settings.ReportingEnabled, "Whitespace must not count as a configured DSN.");
    }

    [TestMethod]
    public void ReadSettings_WithADsn_EnablesReporting()
    {
        var settings = CrashReporting.ReadSettings(Config(("Sentry:Dsn", "https://key@o1.ingest.sentry.io/2")));

        Assert.IsTrue(settings.ReportingEnabled);
        Assert.AreEqual("https://key@o1.ingest.sentry.io/2", settings.Dsn);
    }

    [TestMethod]
    public void ReadSettings_WithNullConfiguration_DisablesReportingRatherThanThrowing()
    {
        // Init reaches this when the embedded appsettings resource cannot be found.
        var settings = CrashReporting.ReadSettings(null);

        Assert.IsFalse(settings.ReportingEnabled);
        Assert.IsTrue(settings.CrashOnUnhandledUiException);
    }

    [TestMethod]
    public void ReadSettings_CrashOnUnhandledUiException_DefaultsToTrue()
    {
        // Swallowing UI-thread exceptions is what made the native crashes unattributable, so going
        // back to that has to be an explicit choice.
        Assert.IsTrue(CrashReporting.ReadSettings(Config()).CrashOnUnhandledUiException);
    }

    [TestMethod]
    public void ReadSettings_CrashOnUnhandledUiException_CanBeTurnedOff()
    {
        var settings = CrashReporting.ReadSettings(Config(("Sentry:CrashOnUnhandledUiException", "false")));

        Assert.IsFalse(settings.CrashOnUnhandledUiException);
    }

    [TestMethod]
    public void ReadSettings_CrashOnUnhandledUiException_IgnoresAnUnparseableValue()
    {
        var settings = CrashReporting.ReadSettings(Config(("Sentry:CrashOnUnhandledUiException", "yes-please")));

        Assert.IsTrue(settings.CrashOnUnhandledUiException, "A malformed setting must not silently disable the crash path.");
    }

    [TestMethod]
    public void ReadSettings_EnvironmentDefaultsToProduction()
    {
        Assert.AreEqual("production", CrashReporting.ReadSettings(Config()).Environment);
        Assert.AreEqual("staging", CrashReporting.ReadSettings(Config(("Sentry:Environment", "staging"))).Environment);
    }

    [TestMethod]
    public void ReadSettings_DebugDefaultsToOff()
    {
        Assert.IsFalse(CrashReporting.ReadSettings(Config()).Debug);
        Assert.IsTrue(CrashReporting.ReadSettings(Config(("Sentry:Debug", "true"))).Debug);
    }

    [TestMethod]
    public void Flush_WhenTheSdkWasNeverStarted_DoesNotThrow()
    {
        // Called from the terminal path of the exception handlers, where a throw would replace the
        // real fault with this one.
        CrashReporting.Flush(TimeSpan.FromMilliseconds(1));
    }

    [TestMethod]
    public void TheSdkIsNotRunningUnderTest()
    {
        // Guards the rule in this class's remarks. If a test ever calls Init, this fails and says
        // why, rather than the suite quietly shipping telemetry to the live project.
        Assert.IsFalse(CrashReporting.IsEnabled,
            "A test initialized the Sentry SDK. Tests must not call CrashReporting.Init - on a " +
            "developer machine it resolves the real DSN and posts sessions and events to the " +
            "production project.");
    }
}
