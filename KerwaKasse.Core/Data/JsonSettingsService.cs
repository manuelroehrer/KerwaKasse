using System.Text.Json;
using KerwaKasse.Core.Services;
using Microsoft.Extensions.Logging;

namespace KerwaKasse.Core.Data;

public class JsonSettingsService : ISettingsService
{
    private readonly string _filePath;
    private readonly ILogger<JsonSettingsService> _logger;
    private Dictionary<string, JsonElement> _settings = new();

    // Keys whose value actually changed since the last Save, so the log can name what was written.
    private readonly HashSet<string> _changedKeys = new();

    public JsonSettingsService(string filePath, ILogger<JsonSettingsService> logger)
    {
        _filePath = filePath;
        _logger = logger;
        Load();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            _settings = new Dictionary<string, JsonElement>();
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            _settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)
                        ?? new Dictionary<string, JsonElement>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings file {FilePath} could not be read, falling back to defaults", _filePath);
            _settings = new Dictionary<string, JsonElement>();
        }
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
        var element = JsonDocument.Parse(json).RootElement.Clone();

        // Setting the same value again is not a change; skip it so the change log stays accurate.
        if (_settings.TryGetValue(key, out var existing) && existing.GetRawText() == element.GetRawText())
            return;

        _settings[key] = element;
        _changedKeys.Add(key);
    }

    public bool SetIfAbsent<T>(string key, T value)
    {
        if (_settings.ContainsKey(key))
            return false;
        Set(key, value);
        return true;
    }

    public void Save()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(_settings, options);
        var dir = Path.GetDirectoryName(_filePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, json);

        if (_changedKeys.Count > 0)
        {
            string label = _changedKeys.Count == 1 ? "changed entry" : "changed entries";
            _logger.LogDebug("Settings saved, {Label}: {ChangedEntries}", label,
                string.Join(", ", _changedKeys.Select(k => $"{k}={_settings[k].GetRawText()}")));
        }
        else
        {
            _logger.LogDebug("Settings saved, no entries changed");
        }
        _changedKeys.Clear();
    }
}
