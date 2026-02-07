namespace RedMist.Timing.UI.Services;

public interface IPreferencesService
{
    string Get(string key, string defaultValue);
    bool Get(string key, bool defaultValue);
    int Get(string key, int defaultValue);
    void Set(string key, string value);
    void Set(string key, bool value);
    void Set(string key, int value);
    void Remove(string key);
    void Clear();
}
