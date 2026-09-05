namespace RedMist.Timing.UI.Models;

/// <summary>
/// Raised when the hub's event subscription has been re-established on a new connection.
/// </summary>
/// <remarks>
/// A reconnect leaves the screen holding a grid that stopped updating when the transport dropped,
/// and the server does not send current state on subscribe - SubscribeToEventV2 only adds the
/// connection to the event's group, so what resumes is the delta stream and nothing else. Anything
/// that changed during the gap is simply missing: a car that pitted, positions that shuffled, a
/// flag that came and went.
///
/// The five-second poll used to repair that within five seconds by accident, because it ran whether
/// or not the hub was healthy. Now that it stands down while the hub is delivering - and a freshly
/// reconnected hub is delivering - the repair has to be asked for.
///
/// Raised on every transition to Connected, which includes the first one, so entering an event
/// costs two full-state fetches rather than one. That is deliberate: the alternative is state
/// tracking whose only purpose is to save a single request, on a path that just gave back the other
/// seven hundred an hour. Nothing downstream needs to tell the two apart - a resync is a resync.
/// </remarks>
public class HubResubscribedNotification(int eventId)
{
    /// <summary>The event whose subscription was restored.</summary>
    public int EventId { get; } = eventId;
}
