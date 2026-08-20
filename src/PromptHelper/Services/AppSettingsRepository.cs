using System;
using System.IO;
using System.Security;
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

    private abstract record SettingsReadState
    {
        public sealed record Missing : SettingsReadState;
        public sealed record Valid(AppSettings Settings) : SettingsReadState;
        public sealed record FutureSchema(int Version) : SettingsReadState;
        public sealed record Corrupt(Exception Error) : SettingsReadState;
        public sealed record Unreadable(Exception Error) : SettingsReadState;
    }

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
        SettingsReadState primaryState = ReadState(_settingsPath);
        SettingsReadState backupState = ReadState(_backupPath);

        // 1. Missing / Missing -> Default settings
        if (primaryState is SettingsReadState.Missing && backupState is SettingsReadState.Missing)
        {
            return new SettingsLoadResult(new AppSettings(), false, null);
        }

        // 2. Primary Future Schema -> HALT immediately without touching files or reading backup
        if (primaryState is SettingsReadState.FutureSchema futurePrimary)
        {
            throw new UnsupportedSettingsSchemaException(futurePrimary.Version);
        }

        // 3. Primary Unreadable -> HALT immediately without falling back to stale backup
        if (primaryState is SettingsReadState.Unreadable unreadablePrimary)
        {
            throw new SettingsReadException(_settingsPath, unreadablePrimary.Error);
        }

        // 4. Primary Valid -> Authoritative primary wins
        if (primaryState is SettingsReadState.Valid validPrimary)
        {
            string? warning = null;
            try
            {
                string? dir = Path.GetDirectoryName(_backupPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonSerializer.Serialize(validPrimary.Settings, JsonOptions);
                _writer.Write(_backupPath, json);
            }
            catch (Exception ex)
            {
                warning = $"Settings loaded from settings.json, but settings.backup.json could not be synchronized: {ex.Message}";
            }

            return new SettingsLoadResult(validPrimary.Settings, false, warning);
        }

        // 5. Primary Corrupt or Missing -> Backup evaluation
        if (primaryState is SettingsReadState.Corrupt or SettingsReadState.Missing)
        {
            if (backupState is SettingsReadState.FutureSchema futureBackup)
            {
                throw new UnsupportedSettingsSchemaException(futureBackup.Version);
            }

            if (backupState is SettingsReadState.Unreadable unreadableBackup)
            {
                throw new SettingsReadException(_backupPath, unreadableBackup.Error);
            }

            if (backupState is SettingsReadState.Valid validBackup)
            {
                string? warning = "Prompt Helper recovered its data-folder setting from settings.backup.json.\r\n\r\nThe configured prompt library itself was not modified by this recovery.";
                try
                {
                    string? dir = Path.GetDirectoryName(_settingsPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    string json = JsonSerializer.Serialize(validBackup.Settings, JsonOptions);
                    _writer.Write(_settingsPath, json);
                }
                catch (Exception ex)
                {
                    warning = $"Settings were recovered from settings.backup.json, but settings.json could not be restored: {ex.Message}";
                }

                return new SettingsLoadResult(validBackup.Settings, true, warning);
            }

            if (backupState is SettingsReadState.Corrupt corruptBackup)
            {
                throw new InvalidDataException(
                    $"Settings file '{_backupPath}' is corrupt: {corruptBackup.Error.Message}", corruptBackup.Error);
            }

            if (primaryState is SettingsReadState.Corrupt corruptPrimary)
            {
                throw new InvalidDataException(
                    $"Settings file '{_settingsPath}' is corrupt and no valid backup exists: {corruptPrimary.Error.Message}", corruptPrimary.Error);
            }
        }

        throw new InvalidDataException($"Failed to load settings from '{_settingsPath}'.");
    }

    public AppSettings Load()
    {
        return LoadOrRecover().Settings;
    }

    public SettingsSaveResult Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.SchemaVersion != AppSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Cannot save unsupported settings schema version: {settings.SchemaVersion}.");
        }

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

    private static SettingsReadState ReadState(string path)
    {
        string json;

        try
        {
            json = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return new SettingsReadState.Missing();
        }
        catch (DirectoryNotFoundException)
        {
            return new SettingsReadState.Missing();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new SettingsReadState.Unreadable(ex);
        }

        try
        {
            return new SettingsReadState.Valid(ParseAndValidate(json, path));
        }
        catch (UnsupportedSettingsSchemaException ex)
        {
            return new SettingsReadState.FutureSchema(ex.SchemaVersion);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            return new SettingsReadState.Corrupt(ex);
        }
    }

    private static AppSettings ParseAndValidate(string json, string path)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException(
                $"Settings file is empty or whitespace: '{path}'");
        }

        AppSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Failed to deserialize settings from '{path}': {ex.Message}", ex);
        }

        if (settings is null)
        {
            throw new InvalidDataException(
                $"Settings deserialized to null from '{path}'.");
        }

        if (settings.SchemaVersion > AppSettings.CurrentSchemaVersion)
        {
            throw new UnsupportedSettingsSchemaException(settings.SchemaVersion);
        }

        if (settings.SchemaVersion < AppSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Invalid settings schema version: {settings.SchemaVersion}.");
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
