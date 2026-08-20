using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using PromptHelper.Infrastructure;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed record SettingsPrimaryWriteToken(
    bool Exists,
    byte[]? Sha256);

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

    public SettingsPrimaryWriteToken CapturePrimaryWriteToken()
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(_settingsPath);
            return new SettingsPrimaryWriteToken(
                Exists: true,
                Sha256: SHA256.HashData(bytes));
        }
        catch (FileNotFoundException)
        {
            return new SettingsPrimaryWriteToken(false, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new SettingsPrimaryWriteToken(false, null);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new SettingsReadException(_settingsPath, ex);
        }
    }

    public SettingsSaveResult SaveIfPrimaryUnchanged(
        AppSettings settings,
        SettingsPrimaryWriteToken expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        SettingsPrimaryWriteToken actual = CapturePrimaryWriteToken();

        if (!WriteTokensEqual(expected, actual))
        {
            throw new InvalidOperationException(
                "Prompt Helper settings changed while the data-folder transition " +
                "was in progress. Nothing was committed. Reopen Tools & Settings and retry.");
        }

        return Save(settings);
    }

    private static bool WriteTokensEqual(
        SettingsPrimaryWriteToken expected,
        SettingsPrimaryWriteToken actual)
    {
        if (expected.Exists != actual.Exists)
        {
            return false;
        }

        if (!expected.Exists)
        {
            return true;
        }

        return expected.Sha256 is not null &&
               actual.Sha256 is not null &&
               expected.Sha256.AsSpan().SequenceEqual(actual.Sha256);
    }

    public SettingsLoadResult LoadOrRecover()
    {
        SettingsReadState primaryState = ReadState(_settingsPath);

        // Future primary: authoritative incompatibility. Do not even inspect backup.
        if (primaryState is SettingsReadState.FutureSchema futurePrimary)
        {
            throw new UnsupportedSettingsSchemaException(futurePrimary.Version);
        }

        // Temporarily unreadable primary: do not substitute stale backup.
        if (primaryState is SettingsReadState.Unreadable unreadablePrimary)
        {
            throw new SettingsReadException(_settingsPath, unreadablePrimary.Error);
        }

        if (primaryState is SettingsReadState.Valid validPrimary)
        {
            SettingsReadState backupState = ReadState(_backupPath);

            if (backupState is SettingsReadState.FutureSchema futureBackup)
            {
                return new SettingsLoadResult(
                    validPrimary.Settings,
                    RecoveredFromBackup: false,
                    Warning:
                        $"Prompt Helper loaded settings.json, but settings.backup.json " +
                        $"was created by a newer settings schema ({futureBackup.Version}). " +
                        "The newer backup was preserved and was not overwritten.");
            }

            if (backupState is SettingsReadState.Unreadable unreadableBackup)
            {
                return new SettingsLoadResult(
                    validPrimary.Settings,
                    RecoveredFromBackup: false,
                    Warning:
                        $"Prompt Helper loaded settings.json, but settings.backup.json " +
                        $"could not be inspected or synchronized: {unreadableBackup.Error.Message}");
            }

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
                warning =
                    "Settings loaded from settings.json, but settings.backup.json " +
                    $"could not be synchronized: {ex.Message}";
            }

            return new SettingsLoadResult(validPrimary.Settings, false, warning);
        }

        // Backup is only needed after a missing/corrupt primary.
        SettingsReadState backupStateForRecovery = ReadState(_backupPath);

        if (primaryState is SettingsReadState.Missing &&
            backupStateForRecovery is SettingsReadState.Missing)
        {
            return new SettingsLoadResult(new AppSettings(), false, null);
        }

        if (backupStateForRecovery is SettingsReadState.FutureSchema futureBackupForRecovery)
        {
            throw new UnsupportedSettingsSchemaException(futureBackupForRecovery.Version);
        }

        if (backupStateForRecovery is SettingsReadState.Unreadable unreadableBackupForRecovery)
        {
            throw new SettingsReadException(_backupPath, unreadableBackupForRecovery.Error);
        }

        if (backupStateForRecovery is SettingsReadState.Valid validBackup)
        {
            string warning =
                "Prompt Helper recovered its data-folder setting from settings.backup.json.\r\n\r\n" +
                "The configured prompt library itself was not modified by this recovery.";

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
                warning =
                    "Settings were recovered from settings.backup.json, but settings.json " +
                    $"could not be restored: {ex.Message}";
            }

            return new SettingsLoadResult(validBackup.Settings, true, warning);
        }

        if (backupStateForRecovery is SettingsReadState.Corrupt corruptBackup)
        {
            throw new InvalidDataException(
                $"Settings file '{_backupPath}' is corrupt: {corruptBackup.Error.Message}",
                corruptBackup.Error);
        }

        if (primaryState is SettingsReadState.Corrupt corruptPrimary)
        {
            throw new InvalidDataException(
                $"Settings file '{_settingsPath}' is corrupt and no valid backup exists: " +
                corruptPrimary.Error.Message,
                corruptPrimary.Error);
        }

        throw new InvalidDataException(
            $"Failed to load settings from '{_settingsPath}'.");
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

        SettingsReadState primaryBefore = ReadState(_settingsPath);

        if (primaryBefore is SettingsReadState.FutureSchema futurePrimary)
        {
            throw new UnsupportedSettingsSchemaException(futurePrimary.Version);
        }

        if (primaryBefore is SettingsReadState.Unreadable unreadablePrimary)
        {
            throw new SettingsReadException(_settingsPath, unreadablePrimary.Error);
        }

        // Capture backup state before primary mutation.
        SettingsReadState backupBefore = ReadState(_backupPath);

        settings.DataRootPath = NormalizeAndValidateDataRoot(settings.DataRootPath);
        string json = JsonSerializer.Serialize(settings, JsonOptions);

        string? settingsDir = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(settingsDir))
        {
            Directory.CreateDirectory(settingsDir);
        }

        _writer.Write(_settingsPath, json);

        if (backupBefore is SettingsReadState.FutureSchema futureBackup)
        {
            return new SettingsSaveResult(
                $"The setting was saved, but settings.backup.json uses newer schema " +
                $"{futureBackup.Version}. The newer backup was preserved and was not overwritten.");
        }

        if (backupBefore is SettingsReadState.Unreadable unreadableBackup)
        {
            return new SettingsSaveResult(
                "The setting was saved, but settings.backup.json could not be inspected " +
                $"or synchronized: {unreadableBackup.Error.Message}");
        }

        try
        {
            string? backupDir = Path.GetDirectoryName(_backupPath);
            if (!string.IsNullOrEmpty(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }

            _writer.Write(_backupPath, json);
            return new SettingsSaveResult(null);
        }
        catch (Exception ex)
        {
            return new SettingsSaveResult(
                "The data folder was saved, but the settings backup could not be " +
                $"synchronized: {ex.Message}");
        }
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

        ValidateSchemaPropertyBeforeDeserialization(json, path);

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

    private static void ValidateSchemaPropertyBeforeDeserialization(
        string json,
        string path)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Failed to parse settings JSON from '{path}': {ex.Message}",
                ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"Root of settings JSON must be an object: '{path}'.");
            }

            int count = 0;
            int version = 0;

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(
                        property.Name,
                        "schemaVersion",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                count++;

                if (property.Value.ValueKind != JsonValueKind.Number ||
                    !property.Value.TryGetInt32(out version))
                {
                    throw new InvalidDataException(
                        $"Property 'schemaVersion' must be an integer in '{path}'.");
                }
            }

            if (count == 0)
            {
                throw new InvalidDataException(
                    $"Missing required 'schemaVersion' property in '{path}'.");
            }

            if (count > 1)
            {
                throw new InvalidDataException(
                    $"Multiple 'schemaVersion' properties found in '{path}'.");
            }

            if (version > AppSettings.CurrentSchemaVersion)
            {
                throw new UnsupportedSettingsSchemaException(version);
            }

            if (version != AppSettings.CurrentSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported settings schema version: {version}.");
            }
        }
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
