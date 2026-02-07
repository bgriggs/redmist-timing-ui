using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace RedMist.Timing.UI.Services;

public class PreferencesService : IPreferencesService
{
    private readonly string _preferencesFilePath;
    private Dictionary<string, object> _preferences = new();
    private readonly Lock _lock = new();

    public PreferencesService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appDataPath, "RedMist.Timing.UI");
        Directory.CreateDirectory(appFolder);
        _preferencesFilePath = Path.Combine(appFolder, "preferences.json");
        LoadPreferences();
    }

    public string Get(string key, string defaultValue)
    {
        lock (_lock)
        {
            if (_preferences.TryGetValue(key, out var value) && value is JsonElement element)
            {
                return element.GetString() ?? defaultValue;
            }
            return defaultValue;
        }
    }

    public bool Get(string key, bool defaultValue)
    {
        lock (_lock)
        {
            if (_preferences.TryGetValue(key, out var value) && value is JsonElement element)
            {
                return element.GetBoolean();
            }
            return defaultValue;
        }
    }

    public int Get(string key, int defaultValue)
    {
        lock (_lock)
        {
            if (_preferences.TryGetValue(key, out var value) && value is JsonElement element)
            {
                return element.GetInt32();
            }
            return defaultValue;
        }
    }

    public void Set(string key, string value)
    {
        lock (_lock)
        {
            _preferences[key] = value;
            SavePreferences();
        }
    }

    public void Set(string key, bool value)
    {
        lock (_lock)
        {
            _preferences[key] = value;
            SavePreferences();
        }
    }

    public void Set(string key, int value)
    {
        lock (_lock)
        {
            _preferences[key] = value;
            SavePreferences();
        }
    }

    public void Remove(string key)
    {
        lock (_lock)
        {
            _preferences.Remove(key);
            SavePreferences();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _preferences.Clear();
            SavePreferences();
        }
    }

    private void LoadPreferences()
    {
        try
        {
            if (File.Exists(_preferencesFilePath))
            {
                var json = File.ReadAllText(_preferencesFilePath);
                _preferences = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
            }
        }
        catch
        {
            _preferences = new();
        }
    }

    private void SavePreferences()
    {
        try
        {
            var json = JsonSerializer.Serialize(_preferences, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_preferencesFilePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }
}
