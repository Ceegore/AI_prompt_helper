using System.Text;
using System.Text.Json;
using PromptHelper.Models;

namespace PromptHelper.Services;

/// <summary>
/// Small Linux settings store for the Avalonia desktop host. Settings remain in the default
/// per-user PromptHelper folder even when the library itself is switched to another data root.
/// </summary>
public sealed class LinuxDesktopSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _root;
    private readonly string _settingsPath;
    private readonly string _backupPath;
    private readonly LinuxDurableAtomicFileWriter _writer = new();

    public LinuxDesktopSettingsStore()
    {
        LinuxNativeFileSystem.EnsureLinux();

        _root = DefaultDataRoot.Path;
        _settingsPath = Path.Combine(_root, "settings.json");
        _backupPath = Path.Combine(_root, "settings.backup.json");
    }

    public string SettingsPath => _settingsPath;

    public AppSettings Load()
    {
        Directory.CreateDirectory(_root);

        if (TryLoad(_settingsPath, out AppSettings settings))
        {
            return settings;
        }

        if (TryLoad(_backupPath, out AppSettings backup))
        {
            Save(backup);
            return backup;
        }

        if (File.Exists(_settingsPath) || File.Exists(_backupPath))
        {
            throw new InvalidDataException(
                "Prompt Helper settings are corrupt and no valid backup is available.");
        }

        return new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = null,
            UseDarkMode = false
        };
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.SchemaVersion != AppSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported settings schema version {settings.SchemaVersion}.");
        }

        Directory.CreateDirectory(_root);

        var normalized = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = NormalizeDataRoot(settings.DataRootPath),
            UseDarkMode = settings.UseDarkMode
        };

        byte[] bytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(normalized, JsonOptions));

        Write(_settingsPath, bytes);
        Write(_backupPath, bytes);
    }

    public string ResolveEffectiveDataRoot(AppSettings settings) =>
        NormalizeDataRoot(settings.DataRootPath) ?? DefaultDataRoot.Path;

    private void Write(string path, byte[] bytes)
    {
        if (File.Exists(path))
        {
            _writer.ReplaceDurable(
                path,
                bytes,
                DurableFileClass.Settings);
        }
        else
        {
            _writer.CreateNewDurable(
                path,
                bytes,
                DurableFileClass.Settings);
        }
    }

    private static bool TryLoad(
        string path,
        out AppSettings settings)
    {
        settings = null!;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            string json =
                StrictUtf8Text.ReadAllText(
                    path,
                    $"settings file '{path}'");

            AppSettings? parsed =
                JsonSerializer.Deserialize<AppSettings>(
                    json,
                    JsonOptions);

            if (parsed is null ||
                parsed.SchemaVersion <= 0 ||
                parsed.SchemaVersion > AppSettings.CurrentSchemaVersion)
            {
                return false;
            }

            if (parsed.SchemaVersion == 1)
            {
                parsed.SchemaVersion =
                    AppSettings.CurrentSchemaVersion;
                parsed.UseDarkMode = false;
            }

            parsed.DataRootPath =
                NormalizeDataRoot(parsed.DataRootPath);

            settings = parsed;
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException or
            InvalidDataException)
        {
            return false;
        }
    }

    public static string? NormalizeDataRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = path.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new InvalidDataException(
                "The data folder must be an absolute path.");
        }

        return PathIdentity.NormalizeForComparison(trimmed);
    }
}
