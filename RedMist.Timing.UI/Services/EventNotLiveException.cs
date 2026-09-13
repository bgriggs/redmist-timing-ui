using System;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Thrown by the status call when the server says an event has nothing live to report (HTTP 404
/// from the StatusApi): the event has finished, has not started yet, or its processor is gone.
/// </summary>
/// <remarks>
/// An answer rather than a failure, which is why it is its own type. Retrying it gets the same
/// answer half a second later, and reporting it as an error files a bug for every poll of a screen
/// someone left open on a finished event.
/// </remarks>
public class EventNotLiveException : Exception
{
    public int EventId { get; }

    public EventNotLiveException(int eventId, Exception? innerException = null)
        : base($"Event {eventId} has nothing live to report.", innerException)
    {
        EventId = eventId;
    }
}
