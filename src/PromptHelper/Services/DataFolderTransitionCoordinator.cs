using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed class DataFolderTransitionCoordinator : IDataFolderTransitionService
{
    private readonly string _activeCurrentRoot;
    private readonly AppSettingsRepository _settingsRepo;
    private readonly DataFolderMigrationService _migrationService;
    private readonly IUserConfirmationService _confirmationService;
    private readonly DataRootCapabilityValidator _capabilityValidator;
    private readonly ManagedDataRootPolicy _rootPolicy;
    private readonly IPhysicalPathResolver _physicalPathResolver;
    private readonly MigrationManifestRepository _manifestRepo;
    private readonly IMigrationFileOps _fileOps;

    public DataFolderTransitionCoordinator(
        string activeCurrentRoot,
        AppSettingsRepository settingsRepo,
        DataFolderMigrationService migrationService,
        IUserConfirmationService confirmationService,
        DataRootCapabilityValidator? capabilityValidator = null,
        IPhysicalPathResolver? pathResolver = null)
        : this(
            activeCurrentRoot,
            settingsRepo,
            migrationService,
            confirmationService,
            capabilityValidator,
            pathResolver,
            manifestRepo: null,
            fileOps: null,
            caseInspector: null)
    {
    }

    internal DataFolderTransitionCoordinator(
        string activeCurrentRoot,
        AppSettingsRepository settingsRepo,
        DataFolderMigrationService migrationService,
        IUserConfirmationService confirmationService,
        DataRootCapabilityValidator? capabilityValidator,
        IPhysicalPathResolver? pathResolver,
        MigrationManifestRepository? manifestRepo,
        IMigrationFileOps? fileOps,
        IDirectoryCaseSensitivityInspector? caseInspector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeCurrentRoot);

        _physicalPathResolver = pathResolver ?? new WindowsPhysicalPathResolver();
        _rootPolicy = new ManagedDataRootPolicy(_physicalPathResolver, caseInspector);
        _activeCurrentRoot = PathIdentity.NormalizeForComparison(activeCurrentRoot);
        _settingsRepo = settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _capabilityValidator = capabilityValidator ?? new DataRootCapabilityValidator();
        _manifestRepo = manifestRepo ?? new MigrationManifestRepository();
        _fileOps = fileOps ?? new DefaultMigrationFileOps();
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

        // 3. Initial topology check & target physical binding
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

        if (File.Exists(relationship.LexicalTarget))
        {
            throw new ArgumentException($"Selected path is a file, not a directory: {relationship.LexicalTarget}", nameof(candidateRoot));
        }

        BoundTargetRoot bound = new(
            relationship.LexicalTarget,
            relationship.PhysicalTarget,
            relationship);

        // Initial Read-Only Target Inspection ON BOUND PHYSICAL ROOT
        var initialInspection = _migrationService.InspectTarget(bound.PhysicalRoot);

        switch (initialInspection.Kind)
        {
            case DataFolderMigrationService.TargetLibraryKind.InterruptedMigration:
                return HandleEmptyTargetTransition(
                    activePhysicalRoot,
                    bound,
                    bootstrapRoot,
                    settingsSnapshot,
                    isInterruptedRecovery: true);

            case DataFolderMigrationService.TargetLibraryKind.CorruptPrimaryWithValidBackup:
                throw new InvalidDataException(
                    "The target folder contains a corrupt primary library.json and a safety backup. Start Prompt Helper on that folder to recover it before selecting it as a migration target.",
                    initialInspection.Error);

            case DataFolderMigrationService.TargetLibraryKind.FutureSchema:
                throw initialInspection.Error ?? new UnsupportedLibrarySchemaException(999);

            case DataFolderMigrationService.TargetLibraryKind.Unreadable:
                throw initialInspection.Error ?? new InvalidDataException($"The target data folder cannot be read: '{bound.PhysicalRoot}'.");

            case DataFolderMigrationService.TargetLibraryKind.Unstable:
                throw initialInspection.Error ?? new TargetInspectionUnstableException("Target library changed while being inspected. Retry with a stable target.");

            case DataFolderMigrationService.TargetLibraryKind.Invalid:
                throw initialInspection.Error is InvalidDataException ide
                    ? ide
                    : new InvalidDataException($"The target data folder contains invalid or unreadable library data: '{bound.PhysicalRoot}'. {initialInspection.Error?.Message}", initialInspection.Error);

            case DataFolderMigrationService.TargetLibraryKind.ValidPrimary:
            case DataFolderMigrationService.TargetLibraryKind.RecoverableBackupOnly:
                return HandleExistingTargetTransition(
                    activePhysicalRoot,
                    bound,
                    bootstrapRoot,
                    initialInspection,
                    settingsSnapshot);

            case DataFolderMigrationService.TargetLibraryKind.Empty:
            default:
                return HandleEmptyTargetTransition(
                    activePhysicalRoot,
                    bound,
                    bootstrapRoot,
                    settingsSnapshot,
                    isInterruptedRecovery: false);
        }
    }

    private void AssertLocatorStillMapsToBoundTarget(
        string activeRoot,
        BoundTargetRoot bound,
        string bootstrapRoot)
    {
        DataRootRelationship actual = _rootPolicy.ValidateTransition(
            activeRoot,
            bound.LexicalRoot,
            bootstrapRoot);

        if (actual.SamePhysicalRoot ||
            !PathIdentity.Equals(actual.PhysicalTarget, bound.PhysicalRoot))
        {
            throw new InvalidOperationException(
                "The physical target folder changed while the data-folder transition was in progress. " +
                "The selected data-folder path changed physical identity while the transition was in progress. Nothing was committed.");
        }
    }

    private DataFolderTransitionResult HandleExistingTargetTransition(
        string activePhysicalRoot,
        BoundTargetRoot bound,
        string bootstrapRoot,
        DataFolderMigrationService.TargetInspection initialInspection,
        SettingsTransitionSnapshot settingsSnapshot)
    {
        // 1. User Confirmation BEFORE mutating or probing target
        bool confirmed = _confirmationService.ConfirmExistingLibrarySwitch(
            bound.LexicalRoot,
            initialInspection.Warning);

        if (!confirmed)
        {
            return new DataFolderTransitionResult(
                Changed: false,
                RestartRequired: false,
                ExistingLibrarySelected: true,
                NormalizedTargetRoot: bound.LexicalRoot,
                Warning: null);
        }

        // 2. Acquire target lock reservation ON BOUND PHYSICAL TARGET
        using var reservation = TargetRootReservation.TryAcquire(bound.PhysicalRoot);
        if (reservation is null)
        {
            throw new InvalidOperationException(
                $"The selected target library is currently in use by another instance: '{bound.PhysicalRoot}'. Close other instances and retry.");
        }

        // 3. PHYSICAL REVALIDATION #1 under reservation lock
        AssertLocatorStillMapsToBoundTarget(activePhysicalRoot, bound, bootstrapRoot);

        // 4. Re-inspect target under reservation lock on BOUND PHYSICAL TARGET
        var lockedInspection = _migrationService.InspectTarget(bound.PhysicalRoot);
        if (lockedInspection.Kind != initialInspection.Kind ||
            !FingerprintsEqual(lockedInspection.Fingerprint, initialInspection.Fingerprint))
        {
            TargetReservationCleanupResult precommitCleanup = reservation.Release();
            if (!precommitCleanup.Success)
            {
                throw new InvalidOperationException(
                    "The selected target library changed while confirmation was open. " +
                    $"Additionally, cleanup failed: {precommitCleanup.ToWarning()}");
            }

            throw new InvalidOperationException(
                "The selected target library changed while confirmation was open. No settings were changed. Review the target and retry.");
        }

        // 5. Capability probe & existing managed-file validation inside target ON BOUND PHYSICAL TARGET
        var existingContext = initialInspection.EffectiveDocument != null && initialInspection.EffectiveMetadataPath != null
            ? new ExistingLibraryCapabilityContext(initialInspection.EffectiveMetadataPath, initialInspection.EffectiveDocument)
            : null;

        CapabilityValidationResult capResult;
        try
        {
            capResult = _capabilityValidator.ValidateWritable(bound.PhysicalRoot, null, existingContext);
        }
        catch
        {
            TargetReservationCleanupResult capCleanup = reservation.Release();
            if (!capCleanup.Success)
            {
                throw;
            }
            throw;
        }

        // 6. PHYSICAL REVALIDATION #2 immediately before settings commit
        AssertLocatorStillMapsToBoundTarget(activePhysicalRoot, bound, bootstrapRoot);

        // 7. Final fingerprint check immediately before settings commit ON BOUND PHYSICAL TARGET
        var preCommitInspection = _migrationService.InspectTarget(bound.PhysicalRoot);
        if (preCommitInspection.Kind != initialInspection.Kind ||
            !FingerprintsEqual(preCommitInspection.Fingerprint, initialInspection.Fingerprint))
        {
            TargetReservationCleanupResult precommitCleanup = reservation.Release();
            throw new InvalidOperationException(
                "The selected target library changed before settings could be saved. No settings were changed. Review the target and retry.");
        }

        // 8. Commit Settings with precondition token
        var newSettings = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = bound.LexicalRoot
        };

        SettingsSaveResult saveResult = _settingsRepo.SaveIfUnchanged(newSettings, settingsSnapshot.Precondition);

        // POINT OF NO RETURN
        TargetReservationCleanupResult cleanup = reservation.Release();

        string? warning = WarningCombiner.Combine(
            settingsSnapshot.Warning,
            initialInspection.Warning,
            lockedInspection.Warning,
            capResult.Warning,
            saveResult.Warning,
            cleanup.ToWarning());

        return new DataFolderTransitionResult(
            Changed: true,
            RestartRequired: true,
            ExistingLibrarySelected: true,
            NormalizedTargetRoot: bound.LexicalRoot,
            Warning: warning);
    }

    private DataFolderTransitionResult HandleEmptyTargetTransition(
        string activePhysicalRoot,
        BoundTargetRoot bound,
        string bootstrapRoot,
        SettingsTransitionSnapshot settingsSnapshot,
        bool isInterruptedRecovery)
    {
        // 1. Capture snapshot of source FIRST before any target mutation
        MigrationPayloadSnapshot snapshot = _migrationService.CaptureSourcePayloadSnapshot(activePhysicalRoot);

        // 2. Acquire target lock reservation ON BOUND PHYSICAL TARGET
        using var reservation = TargetRootReservation.TryAcquire(bound.PhysicalRoot);
        if (reservation is null)
        {
            throw new InvalidOperationException(
                $"The selected target folder is currently in use by another instance: '{bound.PhysicalRoot}'. Close other instances and retry.");
        }

        // 3. PHYSICAL REVALIDATION #1 after directory creation under lock
        AssertLocatorStillMapsToBoundTarget(activePhysicalRoot, bound, bootstrapRoot);

        // 4. Resolve interrupted state if needed, then target must be empty
        if (isInterruptedRecovery)
        {
            MigrationTargetRecoveryService.ResolveInterruptedTarget(
                bound.PhysicalRoot,
                _manifestRepo,
                _fileOps);
        }

        var targetInspection = _migrationService.InspectTarget(bound.PhysicalRoot);
        if (targetInspection.Kind != DataFolderMigrationService.TargetLibraryKind.Empty)
        {
            TargetReservationCleanupResult precommitCleanup = reservation.Release();
            throw new InvalidOperationException(
                $"The target folder '{bound.PhysicalRoot}' is no longer empty. Transition aborted.");
        }

        // 5. Create durable Copying manifest BEFORE payload copy
        Guid attemptId = Guid.NewGuid();
        string markerPath = Path.Combine(bound.PhysicalRoot, ".prompthelper-migration.json");

        var manifest = new MigrationAttemptManifest
        {
            AttemptId = attemptId,
            SourcePhysicalRoot = activePhysicalRoot,
            TargetPhysicalRoot = bound.PhysicalRoot,
            SourceLibrarySha256Hex = Convert.ToHexStringLower(snapshot.Files.First(f => f.Role == MigrationPayloadRole.PrimaryMetadata).Sha256),
            Phase = MigrationManifestPhase.Copying,
            Artifacts = snapshot.Files.Select(f => new MigrationManifestArtifact
            {
                RelativePath = f.RelativePath,
                Sha256Hex = Convert.ToHexStringLower(f.Sha256),
                Length = f.Length,
                Role = f.Role
            }).ToList()
        };

        _manifestRepo.WriteDurable(markerPath, manifest);

        // 6. Perform migration under target transaction
        using var tx = new DataFolderMigrationService.MigrationTargetTransaction();
        bool settingsCommitted = false;
        CapabilityValidationResult? capResult = null;
        SettingsSaveResult? saveResult = null;

        try
        {
            _migrationService.CopySnapshotToTarget(
                activePhysicalRoot,
                bound.PhysicalRoot,
                snapshot,
                attemptId,
                tx);

            capResult = _capabilityValidator.ValidateWritable(bound.PhysicalRoot, tx, null);

            // Update manifest to ReadyToCommit
            manifest.Phase = MigrationManifestPhase.ReadyToCommit;
            _manifestRepo.WriteDurable(markerPath, manifest);

            // 7. PHYSICAL REVALIDATION #2 immediately before settings commit
            AssertLocatorStillMapsToBoundTarget(activePhysicalRoot, bound, bootstrapRoot);

            // 8. Commit settings with precondition token
            var newSettings = new AppSettings
            {
                SchemaVersion = AppSettings.CurrentSchemaVersion,
                DataRootPath = bound.LexicalRoot
            };

            saveResult = _settingsRepo.SaveIfUnchanged(newSettings, settingsSnapshot.Precondition);
            settingsCommitted = true;
            tx.Commit();
        }
        catch (Exception original)
        {
            if (!settingsCommitted)
            {
                try
                {
                    _manifestRepo.Delete(markerPath);
                }
                catch
                {
                }

                MigrationRollbackResult rollback = tx.Rollback();
                TargetReservationCleanupResult resCleanup = reservation.Release();

                var allFailures = new List<MigrationRollbackFailure>(rollback.Failures);
                allFailures.AddRange(resCleanup.Failures);

                if (allFailures.Count > 0)
                {
                    throw new MigrationRollbackException(original, bound.PhysicalRoot, allFailures);
                }

                throw;
            }
        }

        // POINT OF NO RETURN
        string? manifestCleanupWarning = null;
        try
        {
            _manifestRepo.Delete(markerPath);
        }
        catch (Exception ex)
        {
            manifestCleanupWarning = $"Could not delete migration marker: {ex.Message}";
        }

        TargetReservationCleanupResult postcommitCleanup = reservation.Release();

        string? warning = WarningCombiner.Combine(
            settingsSnapshot.Warning,
            capResult?.Warning,
            saveResult?.Warning,
            manifestCleanupWarning,
            postcommitCleanup.ToWarning());

        return new DataFolderTransitionResult(
            Changed: true,
            RestartRequired: true,
            ExistingLibrarySelected: false,
            NormalizedTargetRoot: bound.LexicalRoot,
            Warning: warning);
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
