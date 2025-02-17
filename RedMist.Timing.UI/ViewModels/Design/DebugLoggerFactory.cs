using Microsoft.Extensions.Logging;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DebugLoggerFactory : ILoggerFactory
{
    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new DebugLogger();
    }

    public void Dispose()
    {
    }
}
