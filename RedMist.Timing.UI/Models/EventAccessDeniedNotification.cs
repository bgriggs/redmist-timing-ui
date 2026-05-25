using CommunityToolkit.Mvvm.Messaging.Messages;

namespace RedMist.Timing.UI.Models;

/// <summary>
/// Broadcast when a request to a private event is rejected because the stored
/// access code is missing or wrong. Listeners (typically the MainViewModel) should
/// clear the stored code and re-prompt the user.
/// </summary>
public class EventAccessDeniedNotification(int eventId) : ValueChangedMessage<int>(eventId)
{
}
