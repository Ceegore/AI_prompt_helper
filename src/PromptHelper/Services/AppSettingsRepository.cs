using System.IO;
using System.Text.Json;
using PromptHelper.Infrastructure;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed class AppSettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _settingsPath;
    private readonly IAtomicTextWriter _writer;

    public AppSettingsRepository(IAtomicTextWriter? writer = null, string? settingsPathOverride = null)
    {
        _writer = writer ?? new AtomicTextWriter();
        _settingsPath = settingsPathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper",
            "settings.json");
    }

    public string SettingsPath => _settingsPath;

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        try
        {
            string json = File.ReadAllText(_settingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings == null)
            {
                return new AppSettings();
            }

            if (settings.SchemaVersion != 1)
            {
                throw new InvalidDataException($"Unsupported settings schema version: {settings.SchemaVersion}");
            }

            settings.DataRootPath = NormalizePath(settings.DataRootPath);
            return settings;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Failed to deserialize settings from '{_settingsPath}': {ex.Message}", ex);
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.DataRootPath = NormalizePath(settings.DataRootPath);
        string json = JsonSerializer.Serialize(settings, JsonOptions);

        string? directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer.Write(_settingsPath, json);
    }

    public string GetEffectiveDataRoot(AppSettings? settings = null)
    {
        var activeSettings = settings ?? Load();
        if (!string.IsNullOrWhiteSpace(activeSettings.DataRootPath))
        {
            return Path.GetFullPath(activeSettings.DataRootPath.Trim());
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper");
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = path.Trim();
        return trimmed.Length == 0 ? null : Path.GetFullPath(trimmed);
    }
}
