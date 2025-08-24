namespace RedMist.Timing.UI;

/// <summary>
/// Used to track the current event and session selected in the application.
/// </summary>
public class EventContext
{
    public int EventId { get; private set; }
    public int SessionId { get; private set; }

    public void SetContext(int eventId, int sessionId)
    {
        EventId = eventId;
        SessionId = sessionId;
    }

    public void ClearContext()
    {
        EventId = 0;
        SessionId = 0;
    }
}
