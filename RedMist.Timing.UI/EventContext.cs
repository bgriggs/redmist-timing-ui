namespace RedMist.Timing.UI;

/// <summary>
/// Used to track the current event and session selected in the application.
/// </summary>
public class EventContext
{
    public int EventId { get; private set; }
    public int SessionId { get; private set; }
    public bool IsPrivate { get; private set; }

    public void SetContext(int eventId, int sessionId, bool isPrivate = false)
    {
        EventId = eventId;
        SessionId = sessionId;
        IsPrivate = isPrivate;
    }

    public void ClearContext()
    {
        EventId = 0;
        SessionId = 0;
        IsPrivate = false;
    }
}
