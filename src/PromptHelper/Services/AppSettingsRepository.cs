using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed record SettingsFileToken(
    bool Exists,
    byte[]? Sha256);

public sealed record SettingsWritePrecondition(
    SettingsFileToken Primary,
    SettingsFileToken Backup);

public sealed record SettingsTransitionSnapshot(
    AppSettings Settings,
    SettingsWritePrecondition Precondition,
    string? Warning);

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
    private readonly string _lockPath;
    private readonly string _bootstrapRoot;
    private readonly IDurableSettingsFileWriter _durableWriter;
    private readonly IAtomicExpectedFileReplacer _atomicReplacer;
    private readonly SettingsLeasePolicy _leasePolicy;

    public AppSettingsRepository(
        IDurableSettingsFileWriter? durableWriter = null,
        string? settingsPathOverride = null,
        string? backupPathOverride = null,
        SettingsLeasePolicy? leasePolicy = null)
        : this(durableWriter, settingsPathOverride, backupPathOverride, leasePolicy, atomicReplacer: null)
    {
    }

    internal AppSettingsRepository(
        IDurableSettingsFileWriter? durableWriter,
        string? settingsPathOverride,
        string? backupPathOverride,
        SettingsLeasePolicy? leasePolicy,
        IAtomicExpectedFileReplacer? atomicReplacer)
    {
        _durableWriter = durableWriter ?? new WindowsDurableSettingsFileWriter();
        _atomicReplacer = atomicReplacer
            ?? durableWriter as IAtomicExpectedFileReplacer
            ?? new WindowsAtomicExpectedFileReplacer();
        _leasePolicy = leasePolicy ?? SettingsLeasePolicy.Default;

        if (settingsPathOverride != null)
        {
            _settingsPath = settingsPathOverride;
            string dir = Path.GetDirectoryName(settingsPathOverride) ?? string.Empty;
            _backupPath = backupPathOverride ?? Path.Combine(dir, "settings.backup.json");
            _lockPath = Path.Combine(dir, ".settings.lock");
            _bootstrapRoot = string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : dir;
        }
        else
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PromptHelper");
            _settingsPath = Path.Combine(root, "settings.json");
            _backupPath = backupPathOverride ?? Path.Combine(root, "settings.backup.json");
            _lockPath = Path.Combine(root, ".settings.lock");
            _bootstrapRoot = root;
        }
    }

    public AppSettingsRepository(string settingsPath, string? backupPath = null, string? lockPath = null)
        : this((IDurableSettingsFileWriter?)null, settingsPath, backupPath, null)
    {
        if (lockPath != null)
        {
            _lockPath = lockPath;
        }
    }

    public AppSettings Load() => LoadOrRecover().Settings;

    public static string? NormalizeAndValidateDataRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string trimmed = path.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new InvalidDataException("Configured data-root path must be fully qualified.");
        }

        return PathIdentity.NormalizeForComparison(trimmed);
    }

    internal SettingsWritePrecondition CaptureWritePreconditionCore() => CapturePrecondition();

    public string SettingsPath => _settingsPath;
    public string BackupPath => _backupPath;
    public string LockPath => _lockPath;

    public SettingsMutationLease AcquireMutationLease(SettingsLeasePolicy? policy = null)
    {
        return SettingsMutationLease.Acquire(_lockPath, policy ?? _leasePolicy);
    }

    public SettingsMutationLease AcquireMutationLease(int timeoutMs)
    {
        return SettingsMutationLease.Acquire(_lockPath, new SettingsLeasePolicy(TimeSpan.FromMilliseconds(timeoutMs), TimeSpan.FromMilliseconds(25)));
    }

    public SettingsFileToken CaptureFileToken(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            return new SettingsFileToken(
                Exists: true,
                Sha256: SHA256.HashData(bytes));
        }
        catch (FileNotFoundException)
        {
            return new SettingsFileToken(false, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new SettingsFileToken(false, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            throw new SettingsReadException(path, "Settings CAS token could not be read.", ex);
        }
    }

    public SettingsWritePrecondition CapturePrecondition()
    {
        return new SettingsWritePrecondition(
            CaptureFileToken(_settingsPath),
            CaptureFileToken(_backupPath));
    }

    public SettingsTransitionSnapshot LoadForTransitionAndCapturePrecondition()
    {
        using var lease = AcquireMutationLease();

        // CRUU16-004: the result is consumed, not discarded. An unresolved compare-and-swap
        // means committed settings may be sitting in a pre-image; reading on would interpret
        // that crash window as ordinary state (a momentarily absent settings.json looks
        // exactly like "no settings yet").
        SettingsTempReconciler.Reconcile(
            _settingsPath,
            _backupPath).ThrowIfUnresolved();

        SettingsLoadResult loadResult = LoadOrRecoverInternal();
        SettingsWritePrecondition precondition = CapturePrecondition();

        return new SettingsTransitionSnapshot(
            loadResult.Settings,
            precondition,
            loadResult.Warning);
    }

    public SettingsLoadResult LoadOrRecover()
    {
        using var lease = AcquireMutationLease();

        // CRUU16-004: the result is consumed, not discarded. An unresolved compare-and-swap
        // means committed settings may be sitting in a pre-image; reading on would interpret
        // that crash window as ordinary state (a momentarily absent settings.json looks
        // exactly like "no settings yet").
        SettingsTempReconciler.Reconcile(
            _settingsPath,
            _backupPath).ThrowIfUnresolved();

        return LoadOrRecoverInternal();
    }

    private SettingsLoadResult LoadOrRecoverInternal()
    {
        SettingsReadState primaryState = ReadSettingsFileState(_settingsPath);
        if (primaryState is SettingsReadState.FutureSchema primaryFuture)
        {
            throw new UnsupportedSettingsSchemaException(primaryFuture.Version);
        }

        if (primaryState is SettingsReadState.Unreadable primaryUnreadable)
        {
            throw new SettingsReadException(_settingsPath, primaryUnreadable.Error.Message, primaryUnreadable.Error);
        }

        if (primaryState is SettingsReadState.Valid primaryValid)
        {
            // Primary is valid. Synchronize backup if needed.
            string? backupWarning = TrySynchronizeBackupInternal(primaryValid.Settings);
            return new SettingsLoadResult(primaryValid.Settings, false, backupWarning);
        }

        // Primary is either Missing or Corrupt. Inspect backup.
        SettingsReadState backupState = ReadSettingsFileState(_backupPath);
        if (backupState is SettingsReadState.FutureSchema backupFuture)
        {
            throw new UnsupportedSettingsSchemaException(backupFuture.Version);
        }

        if (backupState is SettingsReadState.Unreadable backupUnreadable)
        {
            throw new SettingsReadException(_backupPath, backupUnreadable.Error.Message, backupUnreadable.Error);
        }

        if (backupState is SettingsReadState.Valid backupValid)
        {
            // Backup is valid. Recover primary from backup.
            string recoveryWarning = primaryState is SettingsReadState.Corrupt
                ? "Settings were recovered from backup because the primary settings file was corrupt."
                : "Settings were restored from backup.";

            try
            {
                string json = JsonSerializer.Serialize(backupValid.Settings, JsonOptions);
                _durableWriter.WriteDurable(
                    _settingsPath,
                    json);
            }
            catch (Exception ex)
            {
                recoveryWarning += $" Note: settings.json could not be restored: {ex.Message}";
            }

            return new SettingsLoadResult(backupValid.Settings, true, recoveryWarning);
        }

        // Both primary and backup are missing or corrupt. Default settings.
        var defaultSettings = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = null
        };

        if (primaryState is SettingsReadState.Missing && backupState is SettingsReadState.Missing)
        {
            return new SettingsLoadResult(defaultSettings, false, null);
        }

        // Corrupt and no valid backup
        throw new InvalidDataException("Settings file is corrupt and no valid backup is available.");
    }

    public SettingsSaveResult Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.SchemaVersion != AppSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Invalid settings schema version: {settings.SchemaVersion}. Expected {AppSettings.CurrentSchemaVersion}.");
        }

        using var lease = AcquireMutationLease();

        // CRUU16-004: the result is consumed, not discarded. An unresolved compare-and-swap
        // means committed settings may be sitting in a pre-image; reading on would interpret
        // that crash window as ordinary state (a momentarily absent settings.json looks
        // exactly like "no settings yet").
        SettingsTempReconciler.Reconcile(
            _settingsPath,
            _backupPath).ThrowIfUnresolved();

        return SaveCore(settings);
    }

    public SettingsSaveResult SaveIfUnchanged(
        AppSettings settings,
        SettingsWritePrecondition precondition)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(precondition);
        if (settings.SchemaVersion != AppSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Invalid settings schema version: {settings.SchemaVersion}. Expected {AppSettings.CurrentSchemaVersion}.");
        }

        using var lease = AcquireMutationLease();

        // CRUU16-004: the result is consumed, not discarded. An unresolved compare-and-swap
        // means committed settings may be sitting in a pre-image; reading on would interpret
        // that crash window as ordinary state (a momentarily absent settings.json looks
        // exactly like "no settings yet").
        SettingsTempReconciler.Reconcile(
            _settingsPath,
            _backupPath).ThrowIfUnresolved();

        SettingsWritePrecondition current = CapturePrecondition();
        if (!PreconditionsMatch(precondition, current))
        {
            throw new InvalidOperationException("Settings were modified concurrently. Save operation cancelled.");
        }

        return SaveCoreWithCas(settings, precondition);
    }

    private static bool PreconditionsMatch(
        SettingsWritePrecondition expected,
        SettingsWritePrecondition actual)
    {
        if (expected.Primary.Exists != actual.Primary.Exists) return false;
        if (expected.Primary.Exists && !TokensMatch(expected.Primary.Sha256, actual.Primary.Sha256))
        {
            return false;
        }

        if (expected.Backup.Exists != actual.Backup.Exists) return false;
        if (expected.Backup.Exists && !TokensMatch(expected.Backup.Sha256, actual.Backup.Sha256))
        {
            return false;
        }

        return true;
    }

    private static bool TokensMatch(byte[]? a, byte[]? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.AsSpan().SequenceEqual(b);
    }

    private SettingsSaveResult SaveCore(AppSettings settings)
    {
        SettingsReadState primaryState = ReadSettingsFileState(_settingsPath);
        if (primaryState is SettingsReadState.FutureSchema primaryFuture)
        {
            throw new UnsupportedSettingsSchemaException(primaryFuture.Version);
        }
        if (primaryState is SettingsReadState.Unreadable primaryUnreadable)
        {
            throw new SettingsReadException(_settingsPath, primaryUnreadable.Error.Message, primaryUnreadable.Error);
        }

        AppSettings normalized = new()
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = NormalizeAndValidateDataRoot(settings.DataRootPath)
        };

        string json = JsonSerializer.Serialize(normalized, JsonOptions);

        _durableWriter.WriteDurable(
            _settingsPath,
            json);

        string? backupWarning = TrySynchronizeBackupInternal(normalized);
        return new SettingsSaveResult(backupWarning);
    }

    /// <summary>
    /// Used only by <see cref="SaveIfUnchanged"/>: re-verifies the precondition with a
    /// native, share-restricted handle check immediately before the write (CRUU14-003),
    /// instead of trusting the plain content read <see cref="CapturePrecondition"/> did
    /// earlier. The write itself still goes through the injected
    /// <see cref="IDurableSettingsFileWriter"/> so fault-injection tests covering the write
    /// step keep working.
    /// </summary>
    private SettingsSaveResult SaveCoreWithCas(AppSettings settings, SettingsWritePrecondition precondition)
    {
        SettingsReadState primaryState = ReadSettingsFileState(_settingsPath);
        if (primaryState is SettingsReadState.FutureSchema primaryFuture)
        {
            throw new UnsupportedSettingsSchemaException(primaryFuture.Version);
        }
        if (primaryState is SettingsReadState.Unreadable primaryUnreadable)
        {
            throw new SettingsReadException(_settingsPath, primaryUnreadable.Error.Message, primaryUnreadable.Error);
        }

        AppSettings normalized = new()
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = NormalizeAndValidateDataRoot(settings.DataRootPath)
        };

        string json = JsonSerializer.Serialize(normalized, JsonOptions);

        // CRUU15-004: the settings primary write is the point of no return for a data-folder
        // transition, so its precondition is enforced by the write itself. That includes the
        // "was missing" case: an earlier File.Exists proves only that the file was absent at
        // some point in the past, whereas no-overwrite promotion refuses to destroy anything
        // that has appeared since.
        ExpectedFileState expectedPrimary = precondition.Primary.Exists && precondition.Primary.Sha256 != null
            ? ExpectedFileState.Present(Convert.ToHexStringLower(precondition.Primary.Sha256))
            : ExpectedFileState.Missing;

        try
        {
            _atomicReplacer.ReplaceIfExpected(
                _bootstrapRoot,
                _settingsPath,
                expectedPrimary,
                new UTF8Encoding(false).GetBytes(json),
                DurableFileClass.Settings);
        }
        catch (StaleExpectedFileException ex)
        {
            throw new InvalidOperationException("Settings were modified concurrently. Save operation cancelled.", ex);
        }

        string? backupWarning = TrySynchronizeBackupWithCas(normalized, precondition.Backup);
        return new SettingsSaveResult(backupWarning);
    }

    private string? TrySynchronizeBackupWithCas(AppSettings settings, SettingsFileToken expectedBackupToken)
    {
        SettingsReadState backupState = ReadSettingsFileState(_backupPath);
        if (backupState is SettingsReadState.FutureSchema future)
        {
            return $"Settings backup uses newer schema version {future.Version} and was preserved without overwrite.";
        }
        if (backupState is SettingsReadState.Unreadable)
        {
            return "The settings backup could not be synchronized (settings.backup.json could not be synchronized, could not be inspected or synchronized).";
        }

        try
        {
            AppSettings normalized = new()
            {
                SchemaVersion = AppSettings.CurrentSchemaVersion,
                DataRootPath = NormalizeAndValidateDataRoot(settings.DataRootPath)
            };

            string json = JsonSerializer.Serialize(normalized, JsonOptions);

            // Same expected-state binding as the primary, for the same reason: the
            // future-schema check above is a promise to preserve a newer backup, and a
            // newer backup written after that check must not be destroyed by it.
            ExpectedFileState expected = expectedBackupToken.Exists && expectedBackupToken.Sha256 != null
                ? ExpectedFileState.Present(Convert.ToHexStringLower(expectedBackupToken.Sha256))
                : ExpectedFileState.Missing;

            _atomicReplacer.ReplaceIfExpected(
                _bootstrapRoot,
                _backupPath,
                expected,
                new UTF8Encoding(false).GetBytes(json),
                DurableFileClass.Settings);

            return null;
        }
        catch (StaleExpectedFileException ex)
        {
            return "The settings backup was changed by something else while it was being " +
                   $"synchronized. The existing backup was preserved and was not overwritten. {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"The settings backup could not be synchronized (settings.backup.json could not be synchronized, could not be inspected or synchronized): {ex.Message}";
        }
    }

    private string? TrySynchronizeBackupInternal(AppSettings settings)
    {
        SettingsReadState backupState = ReadSettingsFileState(_backupPath);
        if (backupState is SettingsReadState.FutureSchema future)
        {
            return $"Settings backup uses newer schema version {future.Version} and was preserved without overwrite.";
        }
        if (backupState is SettingsReadState.Unreadable)
        {
            return "The settings backup could not be synchronized (settings.backup.json could not be synchronized, could not be inspected or synchronized).";
        }

        try
        {
            AppSettings normalized = new()
            {
                SchemaVersion = AppSettings.CurrentSchemaVersion,
                DataRootPath = NormalizeAndValidateDataRoot(settings.DataRootPath)
            };

            string json = JsonSerializer.Serialize(normalized, JsonOptions);
            _durableWriter.WriteDurable(
                _backupPath,
                json);
            return null;
        }
        catch (Exception ex)
        {
            return $"The settings backup could not be synchronized (settings.backup.json could not be synchronized, could not be inspected or synchronized): {ex.Message}";
        }
    }

    private static SettingsReadState ReadSettingsFileState(string path)
    {
        string raw;
        try
        {
            raw = StrictUtf8Text.ReadAllText(path, $"settings file '{path}'");
        }
        catch (FileNotFoundException)
        {
            return new SettingsReadState.Missing();
        }
        catch (DirectoryNotFoundException)
        {
            return new SettingsReadState.Missing();
        }
        catch (InvalidDataException ex)
        {
            return new SettingsReadState.Corrupt(ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new SettingsReadState.Unreadable(ex);
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new SettingsReadState.Corrupt(new InvalidDataException("Settings root must be a JSON object."));
            }

            var seenProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int? schemaVersion = null;

            foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
            {
                if (!seenProps.Add(prop.Name))
                {
                    return new SettingsReadState.Corrupt(new InvalidDataException($"Settings contains duplicate property '{prop.Name}'."));
                }

                if (string.Equals(prop.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out int sv))
                    {
                        return new SettingsReadState.Corrupt(new InvalidDataException("schemaVersion must be an integer."));
                    }
                    schemaVersion = sv;
                }
            }

            if (!schemaVersion.HasValue)
            {
                return new SettingsReadState.Corrupt(new InvalidDataException("Missing required property 'schemaVersion'."));
            }

            if (schemaVersion.Value > AppSettings.CurrentSchemaVersion)
            {
                return new SettingsReadState.FutureSchema(schemaVersion.Value);
            }

            if (schemaVersion.Value <= 0 || schemaVersion.Value < AppSettings.CurrentSchemaVersion)
            {
                return new SettingsReadState.Corrupt(new InvalidDataException($"Unsupported settings schema version: {schemaVersion.Value}. Expected {AppSettings.CurrentSchemaVersion}."));
            }

            StrictJsonObjectAuthority.ValidateExactObject(
                doc.RootElement,
                allowedMembers: ["schemaVersion", "dataRootPath"],
                requiredMembers: ["schemaVersion"],
                description: $"settings file '{path}'");

            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(raw, JsonOptions);
            if (settings == null)
            {
                return new SettingsReadState.Corrupt(new InvalidDataException("Failed to deserialize settings."));
            }

            settings.DataRootPath = NormalizeAndValidateDataRoot(settings.DataRootPath);

            return new SettingsReadState.Valid(settings);
        }
        catch (Exception ex)
        {
            return new SettingsReadState.Corrupt(ex);
        }
    }

    public string GetEffectiveDataRoot(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.DataRootPath))
        {
            return _bootstrapRoot;
        }

        return settings.DataRootPath;
    }

    public string GetEffectiveDataRoot()
    {
        SettingsLoadResult result = LoadOrRecover();
        return GetEffectiveDataRoot(result.Settings);
    }
}
