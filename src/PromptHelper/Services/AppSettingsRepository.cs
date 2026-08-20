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
    private readonly string _backupPath;
    private readonly IAtomicTextWriter _writer;

    public AppSettingsRepository(
        IAtomicTextWriter? writer = null,
        string? settingsPathOverride = null,
        string? backupPathOverride = null)
    {
        _writer = writer ?? new AtomicTextWriter();

        if (settingsPathOverride != null)
        {
            _settingsPath = settingsPathOverride;
            _backupPath = backupPathOverride ?? Path.Combine(
                Path.GetDirectoryName(settingsPathOverride) ?? string.Empty,
                "settings.backup.json");
        }
        else
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PromptHelper");
            _settingsPath = Path.Combine(root, "settings.json");
            _backupPath = backupPathOverride ?? Path.Combine(root, "settings.backup.json");
        }
    }

    public string SettingsPath => _settingsPath;
    public string BackupPath => _backupPath;

    public SettingsLoadResult LoadOrRecover()
    {
        bool primaryExists = File.Exists(_settingsPath);
        bool backupExists = File.Exists(_backupPath);

        if (!primaryExists && !backupExists)
        {
            return new SettingsLoadResult(new AppSettings(), false, null);
        }

        AppSettings? primarySettings = null;
        Exception? primaryException = null;

        if (primaryExists)
        {
            try
            {
                primarySettings = ReadAndValidate(_settingsPath);
            }
            catch (Exception ex)
            {
                primaryException = ex;
            }
        }

        if (primarySettings != null)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_backupPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonSerializer.Serialize(primarySettings, JsonOptions);
                _writer.Write(_backupPath, json);
            }
            catch
            {
                // Best effort backup synchronization
            }

            return new SettingsLoadResult(primarySettings, false, null);
        }

        AppSettings? backupSettings = null;
        Exception? backupException = null;

        if (backupExists)
        {
            try
            {
                backupSettings = ReadAndValidate(_backupPath);
            }
            catch (Exception ex)
            {
                backupException = ex;
            }
        }

        if (backupSettings != null)
        {
            try
            {
                string? dir = Path.GetDirectoryName(_settingsPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonSerializer.Serialize(backupSettings, JsonOptions);
                _writer.Write(_settingsPath, json);
            }
            catch
            {
                // Best effort primary restoration
            }

            return new SettingsLoadResult(
                backupSettings,
                true,
                "Prompt Helper recovered its data-folder setting from settings.backup.json.\r\n\r\nThe configured prompt library itself was not modified by this recovery.");
        }

        string errorMsg = $"Failed to load settings from both primary ('{_settingsPath}') and backup ('{_backupPath}').";
        if (primaryException != null)
        {
            errorMsg += $"\nPrimary error: {primaryException.Message}";
        }
        if (backupException != null)
        {
            errorMsg += $"\nBackup error: {backupException.Message}";
        }

        throw new InvalidDataException(errorMsg, primaryException ?? backupException);
    }

    public AppSettings Load()
    {
        return LoadOrRecover().Settings;
    }

    public SettingsSaveResult Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.DataRootPath = NormalizeAndValidateDataRoot(settings.DataRootPath);
        string json = JsonSerializer.Serialize(settings, JsonOptions);

        string? settingsDir = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(settingsDir))
        {
            Directory.CreateDirectory(settingsDir);
        }

        _writer.Write(_settingsPath, json);

        string? warning = null;
        try
        {
            string? backupDir = Path.GetDirectoryName(_backupPath);
            if (!string.IsNullOrEmpty(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }
            _writer.Write(_backupPath, json);
        }
        catch (Exception ex)
        {
            warning = $"The data folder was saved, but the settings backup could not be synchronized: {ex.Message}";
        }

        return new SettingsSaveResult(warning);
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

    private AppSettings ReadAndValidate(string path)
    {
        string json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException($"Settings file is empty or whitespace: '{path}'");
        }

        AppSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Failed to deserialize settings from '{path}': {ex.Message}", ex);
        }

        if (settings == null)
        {
            throw new InvalidDataException($"Settings deserialized to null from '{path}'");
        }

        if (settings.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported settings schema version: {settings.SchemaVersion}");
        }

        settings.DataRootPath = NormalizeAndValidateDataRoot(settings.DataRootPath);
        return settings;
    }

    public static string? NormalizeAndValidateDataRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = path.Trim();

        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new InvalidDataException(
                "Configured dataRootPath must be an absolute filesystem path.");
        }

        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            throw new InvalidDataException(
                $"Configured dataRootPath is invalid: {ex.Message}", ex);
        }
    }
}
