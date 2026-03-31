namespace KerwaKasse.Core.Services;

public interface ISettingsService
{
    T? Get<T>(string key, T? defaultValue = default);
    void Set<T>(string key, T value);
    bool SetIfAbsent<T>(string key, T value);
    void Save();
}
