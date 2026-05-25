using System;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Thrown by data clients when a private event rejects the request because the
/// access code is missing or wrong (HTTP 401 from the StatusApi).
/// </summary>
public class EventAccessDeniedException : Exception
{
    public int EventId { get; }

    public EventAccessDeniedException(int eventId)
        : base($"Access denied for event {eventId}. A valid access code is required.")
    {
        EventId = eventId;
    }
}
