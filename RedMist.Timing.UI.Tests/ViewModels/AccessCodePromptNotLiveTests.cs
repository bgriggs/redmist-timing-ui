using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Clients;
using RedMist.Timing.UI.Services;
using RedMist.Timing.UI.ViewModels;
using RedMist.TimingCommon.Models;

namespace RedMist.Timing.UI.Tests.ViewModels;

/// <summary>
/// Covers the access code prompt against an event that has nothing live.
/// </summary>
/// <remarks>
/// The prompt confirms a code by calling the status endpoint, which checks the code before it looks
/// for a live session. So an event that is private and not running answers with the not-live 404
/// for a correct code - an accepted code, not the network error it used to be logged as.
/// </remarks>
[TestClass]
public sealed class AccessCodePromptNotLiveTests
{
    private sealed class NotLiveEventClient(RestClientFactory factory, EventAccessCodeStore store)
        : EventClient(factory, new RecordingLoggerFactory(), store)
    {
        public override async Task<SessionState?> LoadEventStatusAsync(int eventId)
        {
            await Task.Yield();
            throw new EventNotLiveException(eventId);
        }
    }

    [TestMethod]
    public async Task ACorrectCodeForAnEventNotRunning_IsAcceptedWithoutAWarning()
    {
        using var factory = new RestClientFactory(TestViewModelFactory.CreateConfiguration());
        var store = new EventAccessCodeStore(new MockPreferencesService());
        var logs = new RecordingLoggerFactory();
        var accepted = false;

        var prompt = new AccessCodePromptViewModel(7, "Event", "Organization",
            new NotLiveEventClient(factory, store), store, logs,
            onSuccess: () => { accepted = true; return Task.CompletedTask; },
            onCancel: () => { });
        prompt.Code = "1234";

        await prompt.ContinueCommand.ExecuteAsync(null);

        Assert.IsTrue(accepted, "The code was accepted by the server; only the event is not running.");
        Assert.AreEqual("1234", store.Get(7), "The code is kept for the data screens to send.");
        Assert.IsFalse(prompt.HasError);
        var warnings = logs.Entries.Where(e => e.Level >= LogLevel.Warning).Select(e => e.Message).ToList();
        Assert.IsEmpty(warnings, "Nothing went wrong, so nothing should be logged as if it had: " + string.Join("; ", warnings));
    }
}
