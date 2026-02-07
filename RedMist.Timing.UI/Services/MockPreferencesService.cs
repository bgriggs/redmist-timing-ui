using System.Collections.Generic;

namespace RedMist.Timing.UI.Services;

/// <summary>
/// Mock preferences service for design-time use
/// </summary>
public class MockPreferencesService : IPreferencesService
{
    private readonly Dictionary<string, object> _storage = new();

    public string Get(string key, string defaultValue)
    {
        return _storage.TryGetValue(key, out var value) && value is string str ? str : defaultValue;
    }

    public bool Get(string key, bool defaultValue)
    {
        return _storage.TryGetValue(key, out var value) && value is bool b ? b : defaultValue;
    }

    public int Get(string key, int defaultValue)
    {
        return _storage.TryGetValue(key, out var value) && value is int i ? i : defaultValue;
    }

    public void Set(string key, string value)
    {
        _storage[key] = value;
    }

    public void Set(string key, bool value)
    {
        _storage[key] = value;
    }

    public void Set(string key, int value)
    {
        _storage[key] = value;
    }

    public void Remove(string key)
    {
        _storage.Remove(key);
    }

    public void Clear()
    {
        _storage.Clear();
    }
}
