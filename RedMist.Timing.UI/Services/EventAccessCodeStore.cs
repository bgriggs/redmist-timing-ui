using System.Collections.Generic;
using System.Text.Json;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Tracks per-event access codes for private events. Codes are persisted via
/// <see cref="IPreferencesService"/> so the user only enters them once per device.
/// Treated as meeting-style passcodes, not credentials.
/// </summary>
public class EventAccessCodeStore
{
    public const string HeaderName = "X-Event-Access-Code";
    private const string PreferencesKey = "PrivateEventAccessCodes";

    private readonly IPreferencesService preferences;
    private readonly Dictionary<int, string> codes;
    private readonly object gate = new();

    public EventAccessCodeStore(IPreferencesService preferences)
    {
        this.preferences = preferences;
        codes = Load(preferences);
    }

    public string? Get(int eventId)
    {
        lock (gate)
        {
            return codes.TryGetValue(eventId, out var code) ? code : null;
        }
    }

    public void Set(int eventId, string code)
    {
        lock (gate)
        {
            codes[eventId] = code;
            Save();
        }
    }

    public void Clear(int eventId)
    {
        lock (gate)
        {
            if (codes.Remove(eventId))
            {
                Save();
            }
        }
    }

    private static Dictionary<int, string> Load(IPreferencesService prefs)
    {
        var json = prefs.Get(PreferencesKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<int, string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(codes);
        preferences.Set(PreferencesKey, json);
    }
}
