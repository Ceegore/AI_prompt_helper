using System;
using System.Collections.Generic;
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
    private readonly IPhysicalPathResolver _physicalPathResolver;

    public DataFolderTransitionCoordinator(
        string activeCurrentRoot,
        AppSettingsRepository settingsRepo,
        DataFolderMigrationService migrationService,
        IUserConfirmationService confirmationService,
        DataRootCapabilityValidator? capabilityValidator = null,
        IPhysicalPathResolver? pathResolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeCurrentRoot);

        _physicalPathResolver = pathResolver ?? new WindowsPhysicalPathResolver();
        _rootPolicy = new ManagedDataRootPolicy(_physicalPathResolver);
        _activeCurrentRoot = PathIdentity.NormalizeForComparison(activeCurrentRoot);
        _settingsRepo = settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _capabilityValidator = capabilityValidator ?? new DataRootCapabilityValidator();
    }

    public DataFolderTransitionResult RequestTransition(string candidateRoot)
    {
        if (string.IsNullOrWhiteSpace(candidateRoot))
        {
            throw new ArgumentException("Selected data folder path cannot be empty or whitespace.", nameof(candidateRoot));
        }

        string cleanTarget = PathIdentity.NormalizeForComparison(candidateRoot.Trim());
        string cleanCurrent = _activeCurrentRoot;

        // 1. Capture snapshot of settings & dual-file precondition under lease
        SettingsTransitionSnapshot settingsSnapshot = _settingsRepo.LoadForTransitionAndCapturePrecondition();

        string bootstrapRoot = Path.GetDirectoryName(_settingsRepo.SettingsPath) ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper");

        // 2. Physical active root vs settings identity check
        string settingsLexicalRoot = _settingsRepo.GetEffectiveDataRoot(settingsSnapshot.Settings);
        string settingsPhysicalRoot = _rootPolicy.ValidateConfiguredRootForStartup(settingsLexicalRoot, bootstrapRoot);
        string activePhysicalRoot = DataRootTopologyValidator.ResolvePhysicalOrThrow(
            _physicalPathResolver,
            _activeCurrentRoot,
            "active data folder");

        if (!PathIdentity.Equals(activePhysicalRoot, settingsPhysicalRoot))
        {
            throw new InvalidOperationException(
                "Prompt Helper settings no longer identify the active running library. " +
                "The data-folder transition was cancelled. Reopen Tools & Settings and retry.");
        }

        // 3. Initial topology check
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
                return HandleExistingTargetTransition(
                    cleanCurrent,
                    cleanTarget,
                    bootstrapRoot,
                    relationship,
                    initialInspection,
                    settingsSnapshot);

            case DataFolderMigrationService.TargetLibraryKind.Empty:
            default:
                return HandleEmptyTargetTransition(
                    cleanCurrent,
                    cleanTarget,
                    bootstrapRoot,
                    relationship,
                    settingsSnapshot);
        }
    }

    private DataRootRelationship RevalidateTargetIdentity(
        string cleanCurrent,
        string cleanTarget,
        string bootstrapRoot,
        DataRootRelationship expected)
    {
        DataRootRelationship actual = _rootPolicy.ValidateTransition(
            cleanCurrent,
            cleanTarget,
            bootstrapRoot);

        if (actual.SamePhysicalRoot)
        {
            throw new InvalidOperationException("The selected target now resolves to the active library.");
        }

        if (!PathIdentity.Equals(actual.PhysicalTarget, expected.PhysicalTarget))
        {
            throw new InvalidOperationException(
                "The physical target folder changed while the data-folder transition was in progress. " +
                "Nothing was committed. Retry with a stable target.");
        }

        return actual;
    }

    private DataFolderTransitionResult HandleExistingTargetTransition(
        string cleanCurrent,
        string cleanTarget,
        string bootstrapRoot,
        DataRootRelationship initialRelationship,
        DataFolderMigrationService.TargetInspection initialInspection,
        SettingsTransitionSnapshot settingsSnapshot)
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

        // 3. PHYSICAL REVALIDATION #1 under reservation lock
        RevalidateTargetIdentity(cleanCurrent, cleanTarget, bootstrapRoot, initialRelationship);

        // 4. Re-inspect target under reservation lock to prevent TOCTOU tampering
        var lockedInspection = _migrationService.InspectTarget(cleanTarget);
        if (lockedInspection.Kind != initialInspection.Kind ||
            !FingerprintsEqual(lockedInspection.Fingerprint, initialInspection.Fingerprint))
        {
            throw new InvalidOperationException(
                "The selected target library changed while confirmation was open. No settings were changed. Review the target and retry.");
        }

        // 5. Capability probe & existing managed-file validation inside target under reservation lock
        var existingContext = initialInspection.EffectiveDocument != null && initialInspection.EffectiveMetadataPath != null
            ? new ExistingLibraryCapabilityContext(initialInspection.EffectiveMetadataPath, initialInspection.EffectiveDocument)
            : null;

        _capabilityValidator.ValidateWritable(cleanTarget, null, existingContext);

        // 6. PHYSICAL REVALIDATION #2 immediately before settings commit
        RevalidateTargetIdentity(cleanCurrent, cleanTarget, bootstrapRoot, initialRelationship);

        // 7. Final fingerprint check immediately before settings commit
        var preCommitInspection = _migrationService.InspectTarget(cleanTarget);
        if (preCommitInspection.Kind != initialInspection.Kind ||
            !FingerprintsEqual(preCommitInspection.Fingerprint, initialInspection.Fingerprint))
        {
            throw new InvalidOperationException(
                "The selected target library changed before settings could be saved. No settings were changed. Review the target and retry.");
        }

        // 8. Commit Settings with precondition token
        var newSettings = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = cleanTarget
        };

        var saveResult = _settingsRepo.SaveIfUnchanged(newSettings, settingsSnapshot.Precondition);

        reservation.Release();

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
        string bootstrapRoot,
        DataRootRelationship initialRelationship,
        SettingsTransitionSnapshot settingsSnapshot)
    {
        // 1. Capture snapshot of source FIRST before any target mutation
        var snapshot = _migrationService.CaptureSourceSnapshot(cleanCurrent);

        // 2. Acquire target lock reservation (creates directory if missing)
        using var reservation = TargetRootReservation.TryAcquire(cleanTarget);
        if (reservation is null)
        {
            throw new InvalidOperationException(
                $"The selected target folder is currently in use by another instance: '{cleanTarget}'. Close other instances and retry.");
        }

        // 3. PHYSICAL REVALIDATION #1 after directory creation under lock
        RevalidateTargetIdentity(cleanCurrent, cleanTarget, bootstrapRoot, initialRelationship);

        // 4. Target must still be empty
        var targetInspection = _migrationService.InspectTarget(cleanTarget);
        if (targetInspection.Kind != DataFolderMigrationService.TargetLibraryKind.Empty)
        {
            throw new InvalidOperationException(
                $"The target folder '{cleanTarget}' is no longer empty. Transition aborted.");
        }

        // 5. Perform migration under target transaction
        using var tx = new DataFolderMigrationService.MigrationTargetTransaction();
        try
        {
            _migrationService.CopySnapshotToTarget(cleanCurrent, cleanTarget, snapshot, tx);
            _capabilityValidator.ValidateWritable(cleanTarget, tx, null);

            // 6. PHYSICAL REVALIDATION #2 immediately before settings commit
            RevalidateTargetIdentity(cleanCurrent, cleanTarget, bootstrapRoot, initialRelationship);

            // 7. Commit settings with precondition token
            var newSettings = new AppSettings
            {
                SchemaVersion = AppSettings.CurrentSchemaVersion,
                DataRootPath = cleanTarget
            };

            var saveResult = _settingsRepo.SaveIfUnchanged(newSettings, settingsSnapshot.Precondition);

            tx.Commit();
            reservation.Release();

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
            TargetReservationCleanupResult resCleanup = reservation.Release();

            var allFailures = new List<MigrationRollbackFailure>(rollback.Failures);
            allFailures.AddRange(resCleanup.Failures);

            if (allFailures.Count > 0)
            {
                throw new MigrationRollbackException(original, cleanTarget, allFailures);
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
