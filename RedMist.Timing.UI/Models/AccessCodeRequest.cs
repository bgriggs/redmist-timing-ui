using CommunityToolkit.Mvvm.Messaging.Messages;
using RedMist.TimingCommon.Models;
using System;
using System.Threading.Tasks;

namespace RedMist.Timing.UI.Models;

/// <summary>
/// Asks the host (MainViewModel) to show the access-code prompt for a private
/// event and invoke the provided callbacks. Used when a flow outside the main
/// event-status routing needs the same gating UI (e.g. In-Car Driver Mode).
/// </summary>
public class AccessCodeRequest
{
    public Event EventModel { get; }
    public string OrganizationName { get; }
    public Func<Task> OnSuccess { get; }
    public Action OnCancel { get; }

    public AccessCodeRequest(Event eventModel, string organizationName, Func<Task> onSuccess, Action onCancel)
    {
        EventModel = eventModel;
        OrganizationName = organizationName;
        OnSuccess = onSuccess;
        OnCancel = onCancel;
    }
}

public class AccessCodeRequestNotification : ValueChangedMessage<AccessCodeRequest>
{
    public AccessCodeRequestNotification(AccessCodeRequest value) : base(value) { }
}
