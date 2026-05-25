using RedMist.Timing.UI.Services;
using System;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.ViewModels.Design;

public class DesignAccessCodePromptViewModel : AccessCodePromptViewModel
{
    public DesignAccessCodePromptViewModel()
        : base(
            eventId: 42,
            eventName: "Test Series Friday Night Practice",
            organizationName: "World Racing League",
            eventClient: new DesignEventClient(new DesignConfiguration()),
            store: new EventAccessCodeStore(new MockPreferencesService()),
            loggerFactory: new DebugLoggerFactory(),
            onSuccess: () => Task.CompletedTask,
            onCancel: () => { })
    {
    }
}
