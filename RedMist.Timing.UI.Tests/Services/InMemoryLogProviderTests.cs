using Microsoft.Extensions.Logging;
using RedMist.Timing.UI.Services;

namespace RedMist.Timing.UI.Tests.Services;

[TestClass]
public sealed class InMemoryLogProviderTests
{
    [TestMethod]
    public void GetLogEntries_ReturnsNewestFirst()
    {
        var provider = new InMemoryLogProvider();
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("first");
        logger.LogInformation("second");
        logger.LogInformation("third");

        var messages = provider.GetLogEntries().Select(e => e.Message).ToArray();
        CollectionAssert.AreEqual(new[] { "third", "second", "first" }, messages);
    }

    [TestMethod]
    public void GetLogEntries_TrimsToTheConfiguredMaximum()
    {
        var provider = new InMemoryLogProvider(maxEntries: 3);
        var logger = provider.CreateLogger("Test");

        for (int i = 0; i < 10; i++)
        {
            logger.LogInformation("entry {Index}", i);
        }

        var messages = provider.GetLogEntries().Select(e => e.Message).ToArray();
        CollectionAssert.AreEqual(new[] { "entry 9", "entry 8", "entry 7" }, messages);
    }

    /// <summary>
    /// The reason the problem buffer exists: routine traffic is logged roughly once a second, so a
    /// failure would otherwise be pushed out of the rolling buffer within a minute.
    /// </summary>
    [TestMethod]
    public void Warnings_SurviveRoutineTrafficThatEvictsTheRollingBuffer()
    {
        var provider = new InMemoryLogProvider(maxEntries: 3, maxProblemEntries: 5);
        var logger = provider.CreateLogger("Test");

        logger.LogError("the failure");
        for (int i = 0; i < 20; i++)
        {
            logger.LogInformation("routine {Index}", i);
        }

        Assert.IsFalse(provider.GetLogEntries().Any(e => e.Message == "the failure"),
            "The rolling buffer should have evicted it.");
        Assert.IsTrue(provider.GetProblemEntries().Any(e => e.Message == "the failure"),
            "The problem buffer should have retained it.");
    }

    [TestMethod]
    [DataRow(LogLevel.Warning)]
    [DataRow(LogLevel.Error)]
    [DataRow(LogLevel.Critical)]
    public void ProblemBuffer_RetainsWarningAndAbove(LogLevel level)
    {
        var provider = new InMemoryLogProvider();
        provider.CreateLogger("Test").Log(level, "kept");

        Assert.AreEqual(1, provider.GetProblemEntries().Count());
    }

    [TestMethod]
    [DataRow(LogLevel.Trace)]
    [DataRow(LogLevel.Debug)]
    [DataRow(LogLevel.Information)]
    public void ProblemBuffer_IgnoresBelowWarning(LogLevel level)
    {
        var provider = new InMemoryLogProvider();
        provider.CreateLogger("Test").Log(level, "routine");

        Assert.AreEqual(0, provider.GetProblemEntries().Count());
    }

    [TestMethod]
    public void ProblemBuffer_TrimsToItsOwnMaximum()
    {
        var provider = new InMemoryLogProvider(maxEntries: 100, maxProblemEntries: 2);
        var logger = provider.CreateLogger("Test");

        logger.LogError("oldest");
        logger.LogError("middle");
        logger.LogError("newest");

        var messages = provider.GetProblemEntries().Select(e => e.Message).ToArray();
        CollectionAssert.AreEqual(new[] { "newest", "middle" }, messages);
    }

    [TestMethod]
    public void ProblemEntries_AreTheSameInstancesAsTheRollingEntries()
    {
        // RefreshLogMessages de-duplicates the two sections by reference, so they have to match.
        var provider = new InMemoryLogProvider();
        provider.CreateLogger("Test").LogError("boom");

        var rolling = provider.GetLogEntries().Single();
        var problem = provider.GetProblemEntries().Single();

        Assert.AreSame(rolling, problem);
    }

    [TestMethod]
    public void LogAdded_FiresForEveryEntry()
    {
        var provider = new InMemoryLogProvider();
        var seen = new List<string>();
        provider.LogAdded += (_, entry) => seen.Add(entry.Message);

        var logger = provider.CreateLogger("Test");
        logger.LogInformation("one");
        logger.LogError("two");

        CollectionAssert.AreEqual(new[] { "one", "two" }, seen.ToArray());
    }

    /// <summary>
    /// These logs are written from inside catch blocks, and LoggerFactory's composite logger wraps
    /// a faulting provider in an AggregateException. A throwing subscriber must not escape back
    /// through the logging call and take out the handler reporting the original failure.
    /// </summary>
    [TestMethod]
    public void LogAdded_SubscriberExceptionDoesNotEscapeTheLoggingCall()
    {
        var provider = new InMemoryLogProvider();
        provider.LogAdded += (_, _) => throw new InvalidOperationException("subscriber blew up");
        var logger = provider.CreateLogger("Test");

        logger.LogError("the original failure");

        // And the entry is still retained despite the subscriber failing.
        Assert.AreEqual("the original failure", provider.GetProblemEntries().Single().Message);
    }

    [TestMethod]
    public void FormattedMessage_IncludesLevelCategoryAndMessage()
    {
        var provider = new InMemoryLogProvider();
        provider.CreateLogger("MyCategory").LogWarning("something odd");

        var formatted = provider.GetLogEntries().Single().FormattedMessage;

        StringAssert.Contains(formatted, "[Warning]");
        StringAssert.Contains(formatted, "MyCategory");
        StringAssert.Contains(formatted, "something odd");
    }

    [TestMethod]
    public void FormattedMessage_IncludesTheExceptionWhenPresent()
    {
        var provider = new InMemoryLogProvider();
        provider.CreateLogger("Test").LogError(new InvalidOperationException("inner detail"), "wrapper");

        var formatted = provider.GetLogEntries().Single().FormattedMessage;

        StringAssert.Contains(formatted, "inner detail");
    }

    [TestMethod]
    public void FormattedMessage_IsBuiltOnceAndCached()
    {
        // Exception.ToString() re-walks the stack trace on every call, and retained entries are
        // re-rendered on every refresh of the in-app log.
        var entry = new LogEntry
        {
            Timestamp = new DateTime(2026, 8, 9, 13, 45, 12, 345),
            LogLevel = LogLevel.Error,
            Category = "Test",
            Message = "boom",
            Exception = new InvalidOperationException("detail"),
        };

        Assert.AreSame(entry.FormattedMessage, entry.FormattedMessage);
    }
}
