using System;
using System.IO;
using System.Security.Cryptography;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed class DataFolderTransitionCoordinator
{
    private readonly string _activeCurrentRoot;
    private readonly AppSettingsRepository _settingsRepo;
    private readonly DataFolderMigrationService _migrationService;
    private readonly IUserConfirmationService _confirmationService;
    private readonly DataRootCapabilityValidator _capabilityValidator;
    private readonly ManagedDataRootPolicy _rootPolicy;

    public DataFolderTransitionCoordinator(
        string activeCurrentRoot,
        AppSettingsRepository settingsRepo,
        DataFolderMigrationService migrationService,
        IUserConfirmationService confirmationService,
        DataRootCapabilityValidator? capabilityValidator = null,
        IPhysicalPathResolver? pathResolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeCurrentRoot);

        _activeCurrentRoot = PathIdentity.NormalizeForComparison(activeCurrentRoot);
        _settingsRepo = settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _capabilityValidator = capabilityValidator ?? new DataRootCapabilityValidator();
        _rootPolicy = new ManagedDataRootPolicy(pathResolver ?? new WindowsPhysicalPathResolver());
    }

    public DataFolderTransitionResult RequestTransition(string candidateRoot)
    {
        if (string.IsNullOrWhiteSpace(candidateRoot))
        {
            throw new ArgumentException("Selected data folder path cannot be empty or whitespace.", nameof(candidateRoot));
        }

        string cleanTarget = PathIdentity.NormalizeForComparison(candidateRoot.Trim());
        string cleanCurrent = _activeCurrentRoot;

        // Capture settings write token at the start of transition
        SettingsPrimaryWriteToken settingsToken = _settingsRepo.CapturePrimaryWriteToken();

        // Verify current settings on disk resolve to the active running root if settings exist
        if (settingsToken.Exists)
        {
            string currentSettingsRoot = _settingsRepo.GetEffectiveDataRoot();
            if (!PathIdentity.Equals(cleanCurrent, currentSettingsRoot))
            {
                throw new InvalidOperationException(
                    "Prompt Helper settings on disk do not match the active running library root. " +
                    "The data-folder transition was cancelled. Reopen Tools & Settings and retry.");
            }
        }

        string bootstrapRoot = Path.GetDirectoryName(_settingsRepo.SettingsPath) ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper");

        DataRootRelationship relationship = _rootPolicy.ValidateTransition(
            cleanCurrent,
            cleanTarget,
            bootstrapRoot);

        // Same physical root is a clean no-op
        if (relationship.SamePhysicalRoot)
        {
            return new DataFolderTransitionResult(
                Changed: false,
                RestartRequired: false,
                ExistingLibrarySelected: false,
                NormalizedTargetRoot: relationship.LexicalTarget,
                Warning: null);
        }

        if (File.Exists(cleanTarget))
        {
            throw new ArgumentException($"Selected path is a file, not a directory: {cleanTarget}", nameof(candidateRoot));
        }

        // Initial Read-Only Target Inspection (NO file/dir creation, NO probes, NO lock)
        var initialInspection = _migrationService.InspectTarget(cleanTarget);

        switch (initialInspection.Kind)
        {
            case DataFolderMigrationService.TargetLibraryKind.CorruptPrimaryWithValidBackup:
                throw new InvalidDataException(
                    "The target folder contains a corrupt primary library.json and a safety backup. Start Prompt Helper on that folder to recover it before selecting it as a migration target.",
                    initialInspection.Error);

            case DataFolderMigrationService.TargetLibraryKind.FutureSchema:
                throw initialInspection.Error ?? new UnsupportedLibrarySchemaException(999);

            case DataFolderMigrationService.TargetLibraryKind.Invalid:
                throw initialInspection.Error is InvalidDataException ide
                    ? ide
                    : new InvalidDataException($"The target data folder contains invalid or unreadable library data: '{cleanTarget}'. {initialInspection.Error?.Message}", initialInspection.Error);

            case DataFolderMigrationService.TargetLibraryKind.ValidPrimary:
            case DataFolderMigrationService.TargetLibraryKind.RecoverableBackupOnly:
                return HandleExistingTargetTransition(cleanTarget, initialInspection, settingsToken);

            case DataFolderMigrationService.TargetLibraryKind.Empty:
            default:
                return HandleEmptyTargetTransition(cleanCurrent, cleanTarget, settingsToken);
        }
    }

    private DataFolderTransitionResult HandleExistingTargetTransition(
        string cleanTarget,
        DataFolderMigrationService.TargetInspection initialInspection,
        SettingsPrimaryWriteToken settingsToken)
    {
        // 1. User Confirmation BEFORE mutating or probing target
        bool confirmed = _confirmationService.ConfirmExistingLibrarySwitch(
            cleanTarget,
            initialInspection.Warning);

        if (!confirmed)
        {
            return new DataFolderTransitionResult(
                Changed: false,
                RestartRequired: false,
                ExistingLibrarySelected: true,
                NormalizedTargetRoot: cleanTarget,
                Warning: null);
        }

        // 2. Acquire target lock reservation
        using var reservation = TargetRootReservation.TryAcquire(cleanTarget);
        if (reservation is null)
        {
            throw new InvalidOperationException(
                $"The selected target library is currently in use by another instance: '{cleanTarget}'. Close other instances and retry.");
        }

        // 3. Re-inspect target under reservation lock to prevent TOCTOU tampering
        var lockedInspection = _migrationService.InspectTarget(cleanTarget);
        if (lockedInspection.Kind != initialInspection.Kind ||
            !FingerprintsEqual(lockedInspection.Fingerprint, initialInspection.Fingerprint))
        {
            throw new InvalidOperationException(
                "The selected target library changed while confirmation was open. No settings were changed. Review the target and retry.");
        }

        // 4. Capability probe inside target under reservation lock
        _capabilityValidator.ValidateWritable(cleanTarget);

        // 5. Final fingerprint check immediately before settings commit
        var preCommitInspection = _migrationService.InspectTarget(cleanTarget);
        if (preCommitInspection.Kind != initialInspection.Kind ||
            !FingerprintsEqual(preCommitInspection.Fingerprint, initialInspection.Fingerprint))
        {
            throw new InvalidOperationException(
                "The selected target library changed before settings could be saved. No settings were changed. Review the target and retry.");
        }

        // 6. Commit Settings with precondition token
        var newSettings = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = cleanTarget
        };

        var saveResult = _settingsRepo.SaveIfPrimaryUnchanged(newSettings, settingsToken);

        return new DataFolderTransitionResult(
            Changed: true,
            RestartRequired: true,
            ExistingLibrarySelected: true,
            NormalizedTargetRoot: cleanTarget,
            Warning: saveResult.Warning ?? lockedInspection.Warning);
    }

    private DataFolderTransitionResult HandleEmptyTargetTransition(
        string cleanCurrent,
        string cleanTarget,
        SettingsPrimaryWriteToken settingsToken)
    {
        // 1. Capture snapshot of source FIRST before any target mutation
        var snapshot = _migrationService.CaptureSourceSnapshot(cleanCurrent);

        // 2. Acquire target lock reservation
        using var reservation = TargetRootReservation.TryAcquire(cleanTarget);
        if (reservation is null)
        {
            throw new InvalidOperationException(
                $"The selected target folder is currently in use by another instance: '{cleanTarget}'. Close other instances and retry.");
        }

        // 3. Perform migration under target transaction
        using var tx = new DataFolderMigrationService.MigrationTargetTransaction();
        try
        {
            _migrationService.CopySnapshotToTarget(cleanCurrent, cleanTarget, snapshot, tx);
            _capabilityValidator.ValidateWritable(cleanTarget);

            // 4. Commit settings with precondition token
            var newSettings = new AppSettings
            {
                SchemaVersion = AppSettings.CurrentSchemaVersion,
                DataRootPath = cleanTarget
            };

            var saveResult = _settingsRepo.SaveIfPrimaryUnchanged(newSettings, settingsToken);

            tx.Commit();

            return new DataFolderTransitionResult(
                Changed: true,
                RestartRequired: true,
                ExistingLibrarySelected: false,
                NormalizedTargetRoot: cleanTarget,
                Warning: saveResult.Warning);
        }
        catch (Exception original)
        {
            MigrationRollbackResult rollback = tx.Rollback();
            if (!rollback.Success)
            {
                throw new MigrationRollbackException(original, cleanTarget, rollback.Failures);
            }

            throw;
        }
    }

    private static bool FingerprintsEqual(byte[]? a, byte[]? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.AsSpan().SequenceEqual(b);
    }
}
