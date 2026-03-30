using System.Text.Json;
using KerwaKasse.Core.Services;

namespace KerwaKasse.Core.Data;

public class JsonSettingsService : ISettingsService
{
    private readonly string _filePath;
    private Dictionary<string, JsonElement> _settings = new();

    public JsonSettingsService(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _settings = new Dictionary<string, JsonElement>();
            return;
        }

        var json = File.ReadAllText(_filePath);
        _settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                    ?? new Dictionary<string, JsonElement>();
    }

    public T? Get<T>(string key, T? defaultValue = default)
    {
        if (!_settings.TryGetValue(key, out var element))
            return defaultValue;

        try
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText());
        }
        catch
        {
            return defaultValue;
        }
    }

    public void Set<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        _settings[key] = JsonDocument.Parse(json).RootElement.Clone();
    }

    public void Save()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_settings, options);
        var dir = Path.GetDirectoryName(_filePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, json);
    }
}
