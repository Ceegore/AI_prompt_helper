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
    private readonly string _bootstrapPhysicalRoot;
    private readonly AppSettingsRepository _settingsRepo;
    private readonly DataFolderMigrationService _migrationService;
    private readonly IUserConfirmationService _confirmationService;
    private readonly DataRootCapabilityValidator _capabilityValidator;
    private readonly ManagedDataRootPolicy _rootPolicy;
    private readonly IPhysicalPathResolver _physicalPathResolver;
    private readonly MigrationManifestRepository _manifestRepo;
    private readonly IMigrationFileOps _fileOps;
    private readonly MigrationRecoveryService _recoveryService;
    private readonly ManagedTreeTopologyValidator _treeValidator;
    private readonly MigrationReadyGate _readyGate;

    public DataFolderTransitionCoordinator(
        string activeCurrentRoot,
        AppSettingsRepository settingsRepo,
        DataFolderMigrationService migrationService,
        IUserConfirmationService confirmationService,
        DataRootCapabilityValidator? capabilityValidator = null,
        IPhysicalPathResolver? pathResolver = null,
        string? bootstrapPhysicalRoot = null)
        : this(
            activeCurrentRoot,
            settingsRepo,
            migrationService,
            confirmationService,
            capabilityValidator,
            pathResolver,
            manifestRepo: null,
            fileOps: null,
            caseInspector: null,
            bootstrapPhysicalRoot: bootstrapPhysicalRoot)
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
        IDirectoryCaseSensitivityInspector? caseInspector,
        string? bootstrapPhysicalRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeCurrentRoot);

        _physicalPathResolver = pathResolver ?? new WindowsPhysicalPathResolver();
        _rootPolicy = new ManagedDataRootPolicy(_physicalPathResolver, caseInspector);
        _activeCurrentRoot = PathIdentity.NormalizeForComparison(activeCurrentRoot);
        _settingsRepo = settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));
        _bootstrapPhysicalRoot = bootstrapPhysicalRoot ?? Path.GetDirectoryName(settingsRepo.SettingsPath) ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PromptHelper");
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _capabilityValidator = capabilityValidator ?? new DataRootCapabilityValidator();
        _manifestRepo = manifestRepo ?? new MigrationManifestRepository();
        _fileOps = fileOps ?? new DefaultMigrationFileOps();
        _treeValidator = new ManagedTreeTopologyValidator(_physicalPathResolver);
        _recoveryService = new MigrationRecoveryService(_manifestRepo, _fileOps, treeValidator: _treeValidator);
        _readyGate = new MigrationReadyGate(tree: _treeValidator, migrationService: _migrationService);
    }

    public DataFolderTransitionResult RequestTransition(string candidateRoot)
    {
        ProductionRuntimeEvidence.Hit("DataFolderTransitionCoordinator.RequestTransition");
        if (string.IsNullOrWhiteSpace(candidateRoot))
        {
            throw new ArgumentException("Selected data folder path cannot be empty or whitespace.", nameof(candidateRoot));
        }

        string cleanTarget = PathIdentity.NormalizeForComparison(candidateRoot.Trim());
        string cleanCurrent = _activeCurrentRoot;

        // 1. Capture snapshot of settings & dual-file precondition under lease
        SettingsTransitionSnapshot settingsSnapshot = _settingsRepo.LoadForTransitionAndCapturePrecondition();

        string bootstrapRoot = _bootstrapPhysicalRoot;

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

        var runtimeContext = DataRootRuntimeContext.Create(activePhysicalRoot, bootstrapRoot, _physicalPathResolver);

        try
        {
            _treeValidator.ValidateManagedTree(activePhysicalRoot);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException or InvalidDataException)
        {
            throw new InvalidOperationException($"Active data folder is invalid: {ex.Message}", ex);
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

        if (new StrictPathAuthority().Probe(relationship.LexicalTarget).Kind == StrictPathKind.File)
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
                    runtimeContext,
                    bound,
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

            case DataFolderMigrationService.TargetLibraryKind.OccupiedNonLibrary:
                throw new InvalidDataException($"The target folder is not empty and does not contain a valid library: '{bound.PhysicalRoot}'. {initialInspection.Error?.Message}", initialInspection.Error);

            case DataFolderMigrationService.TargetLibraryKind.Invalid:
                throw initialInspection.Error is InvalidDataException ide
                    ? ide
                    : new InvalidDataException($"The target data folder contains invalid or unreadable library data: '{bound.PhysicalRoot}'. {initialInspection.Error?.Message}", initialInspection.Error);

            case DataFolderMigrationService.TargetLibraryKind.ValidPrimary:
            case DataFolderMigrationService.TargetLibraryKind.RecoverableBackupOnly:
                return HandleExistingTargetTransition(
                    runtimeContext,
                    bound,
                    initialInspection,
                    settingsSnapshot);

            case DataFolderMigrationService.TargetLibraryKind.Empty:
                return HandleEmptyTargetTransition(
                    runtimeContext,
                    bound,
                    settingsSnapshot,
                    isInterruptedRecovery: false);

            default:
                throw new InvalidOperationException($"Unsupported target-library state: {initialInspection.Kind}.");
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
        DataRootRuntimeContext runtime,
        BoundTargetRoot bound,
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
        try
        {
            AssertLocatorStillMapsToBoundTarget(runtime.ActivePhysicalRoot, bound, runtime.BootstrapLexicalRoot);
            _treeValidator.ValidateManagedTree(bound.PhysicalRoot, ManagedTreeValidationMode.PreCreation);
        }
        catch (Exception ex)
        {
            TargetReservationCleanupResult cleanup = reservation.Release();
            if (!cleanup.Success)
            {
                throw new MigrationRollbackException(ex, bound.PhysicalRoot, cleanup.Failures);
            }
            throw;
        }

        // 4. Target inspection under lock ON BOUND PHYSICAL TARGET
        var lockedInspection = _migrationService.InspectTarget(bound.PhysicalRoot, isReservationActive: true);

        if (lockedInspection.Kind != initialInspection.Kind)
        {
            TargetReservationCleanupResult cleanup = reservation.Release();
            var ex = new InvalidOperationException(
                $"Target library changed state after acquiring lock (was {initialInspection.Kind}, now {lockedInspection.Kind}). Transition cancelled.");
            if (!cleanup.Success)
            {
                throw new MigrationRollbackException(ex, bound.PhysicalRoot, cleanup.Failures);
            }
            throw ex;
        }

        if (!FingerprintsEqual(lockedInspection.Fingerprint, initialInspection.Fingerprint))
        {
            TargetReservationCleanupResult cleanup = reservation.Release();
            var ex = new InvalidOperationException(
                "Target library content modified concurrently while acquiring lock. Transition cancelled.");
            if (!cleanup.Success)
            {
                throw new MigrationRollbackException(ex, bound.PhysicalRoot, cleanup.Failures);
            }
            throw ex;
        }

        // 5. Ephemeral Writable Capability Probing ON BOUND PHYSICAL TARGET
        var existingContext = initialInspection.EffectiveDocument != null
            ? new ExistingLibraryCapabilityContext(
                initialInspection.Kind,
                initialInspection.Kind == DataFolderMigrationService.TargetLibraryKind.ValidPrimary ? initialInspection.EffectiveMetadataPath : null,
                initialInspection.Kind == DataFolderMigrationService.TargetLibraryKind.RecoverableBackupOnly ? initialInspection.EffectiveMetadataPath : null,
                initialInspection.EffectiveDocument)
            : null;

        CapabilityValidationResult capResult;
        try
        {
            capResult = _capabilityValidator.ValidateWritable(bound.PhysicalRoot, null, existingContext);
        }
        catch (Exception ex)
        {
            TargetReservationCleanupResult cleanup = reservation.Release();
            if (!cleanup.Success)
            {
                throw new MigrationRollbackException(ex, bound.PhysicalRoot, cleanup.Failures);
            }
            throw;
        }

        // 6. PHYSICAL REVALIDATION #2 immediately before settings commit
        try
        {
            AssertLocatorStillMapsToBoundTarget(runtime.ActivePhysicalRoot, bound, runtime.BootstrapLexicalRoot);
            _treeValidator.ValidateManagedTree(bound.PhysicalRoot, ManagedTreeValidationMode.PreCreation);
        }
        catch (Exception ex)
        {
            TargetReservationCleanupResult cleanup = reservation.Release();
            if (!cleanup.Success)
            {
                throw new MigrationRollbackException(ex, bound.PhysicalRoot, cleanup.Failures);
            }
            throw;
        }

        // 6b. Acquire a commit lease on the existing target's metadata and active prompt
        // bodies, binding the exact content already verified above through the settings
        // commit below. Without this, an external process could still edit the target
        // between revalidation #2 and the settings write; the open FileShare.Read handles
        // deny concurrent writers for that window, closing the gap.
        if (lockedInspection.EffectiveDocument is null ||
            string.IsNullOrWhiteSpace(lockedInspection.EffectiveMetadataPath) ||
            lockedInspection.Fingerprint is null)
        {
            TargetReservationCleanupResult cleanup = reservation.Release();
            var ex = new InvalidOperationException(
                "Target library inspection is missing content required to acquire a commit lease. Transition cancelled.");
            if (!cleanup.Success)
            {
                throw new MigrationRollbackException(ex, bound.PhysicalRoot, cleanup.Failures);
            }
            throw ex;
        }

        using var commitLease = AcquireExistingTargetCommitLease(bound, reservation, lockedInspection);

        // 7. Atomic Settings Update with Precondition
        var newSettings = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = bound.LexicalRoot
        };

        SettingsSaveResult saveResult;
        try
        {
            saveResult = _settingsRepo.SaveIfUnchanged(newSettings, settingsSnapshot.Precondition);
        }
        catch (CommittedAtomicReplacementRequiresRestartException ex)
        {
            // settings.json is already live. This is a committed transition with a mandatory
            // restart, not a failed selection; reservation cleanup may continue post-commit.
            saveResult = new SettingsSaveResult(ex.Message);
        }
        catch (Exception ex)
        {
            TargetReservationCleanupResult cleanup = reservation.Release();
            if (!cleanup.Success)
            {
                throw new MigrationRollbackException(ex, bound.PhysicalRoot, cleanup.Failures);
            }
            throw;
        }

        // POINT OF NO RETURN
        reservation.CommitRootOwnership();
        TargetReservationCleanupResult cleanupResult = reservation.Release();

        string? warning = WarningCombiner.Combine(
            settingsSnapshot.Warning,
            initialInspection.Warning,
            lockedInspection.Warning,
            capResult.Warning,
            saveResult.Warning,
            cleanupResult.ToWarning());

        return new DataFolderTransitionResult(
            Changed: true,
            RestartRequired: true,
            ExistingLibrarySelected: true,
            NormalizedTargetRoot: bound.LexicalRoot,
            Warning: warning);
    }

    private static ExistingTargetCommitLease AcquireExistingTargetCommitLease(
        BoundTargetRoot bound,
        TargetRootReservation reservation,
        DataFolderMigrationService.TargetInspection lockedInspection)
    {
        try
        {
            return ExistingTargetCommitLease.Acquire(
                bound.PhysicalRoot,
                lockedInspection.EffectiveMetadataPath!,
                lockedInspection.EffectiveDocument!,
                lockedInspection.Fingerprint!);
        }
        catch (Exception ex)
        {
            TargetReservationCleanupResult cleanup = reservation.Release();
            if (!cleanup.Success)
            {
                throw new MigrationRollbackException(ex, bound.PhysicalRoot, cleanup.Failures);
            }
            throw;
        }
    }

    private DataFolderTransitionResult HandleEmptyTargetTransition(
        DataRootRuntimeContext runtime,
        BoundTargetRoot bound,
        SettingsTransitionSnapshot settingsSnapshot,
        bool isInterruptedRecovery)
    {
        // 1. Capture snapshot of source FIRST before any target mutation
        MigrationPayloadSnapshot snapshot = _migrationService.CaptureSourcePayloadSnapshot(runtime.ActivePhysicalRoot);

        // 2. Acquire target lock reservation ON BOUND PHYSICAL TARGET
        using var reservation = TargetRootReservation.TryAcquire(bound.PhysicalRoot);
        if (reservation is null)
        {
            throw new InvalidOperationException(
                $"The selected target folder is currently in use by another instance: '{bound.PhysicalRoot}'. Close other instances and retry.");
        }

        // 2b. Acquire a root-only operation lease immediately, so the target root's identity
        // is protected for the rest of this operation, including retry cleanup below. Only
        // the root is leased here (not yet "prompts"/"recovery") because a deny-delete lease
        // on a directory blocks its own deletion, and retry cleanup may need to delete
        // "prompts"/"recovery" left behind by an interrupted attempt. Once cleanup and copying
        // have settled their final state, they are bound into this same lease.
        var targetOpLease = ManagedTargetOperationLease.AcquireRootOnly(bound.PhysicalRoot);

        try
        {
            // 3. PHYSICAL REVALIDATION #1 after directory creation under lock
            try
            {
                AssertLocatorStillMapsToBoundTarget(runtime.ActivePhysicalRoot, bound, runtime.BootstrapLexicalRoot);
            }
            catch (Exception ex)
            {
                TargetReservationCleanupResult cleanup = reservation.Release();
                if (!cleanup.Success)
                {
                    throw new MigrationRollbackException(ex, bound.PhysicalRoot, cleanup.Failures);
                }
                throw;
            }

            // 4. Resolve interrupted state if needed, then target must be empty
            if (isInterruptedRecovery)
            {
                var primaryFile = snapshot.Files.FirstOrDefault(f => f.Role == MigrationPayloadRole.PrimaryMetadata);
                var recoveryContext = new MigrationRecoveryContext(
                    bound.PhysicalRoot,
                    runtime.BootstrapPhysicalRoot,
                    ExpectedSourcePhysicalRoot: runtime.ActivePhysicalRoot,
                    ExpectedSourcePayloadFingerprint: MigrationPayloadFingerprint.Compute(snapshot.Files),
                    ExpectedSourceLibrarySha256: primaryFile != null ? Convert.ToHexStringLower(primaryFile.Sha256) : null);

                RecoveryResult recoveryResult = _recoveryService.RecoverForRetry(recoveryContext);
                if (!recoveryResult.Success)
                {
                    TargetReservationCleanupResult cleanup = reservation.Release();
                    var ex = recoveryResult.Error ?? new InvalidDataException(recoveryResult.ErrorMessage ?? "Failed to recover interrupted migration target.");
                    if (!cleanup.Success)
                    {
                        throw new MigrationRollbackException(ex, bound.PhysicalRoot, cleanup.Failures);
                    }
                    throw ex;
                }
            }

            _treeValidator.ValidateManagedTree(bound.PhysicalRoot, ManagedTreeValidationMode.PreCreation);

            var targetInspection = _migrationService.InspectTarget(bound.PhysicalRoot, isReservationActive: true);
            if (targetInspection.Kind != DataFolderMigrationService.TargetLibraryKind.Empty)
            {
                TargetReservationCleanupResult precommitCleanup = reservation.Release();
                var ex = new InvalidOperationException(
                    $"The target folder '{bound.PhysicalRoot}' is not empty (found state: {targetInspection.Kind}). Transition aborted.");
                if (!precommitCleanup.Success)
                {
                    throw new MigrationRollbackException(ex, bound.PhysicalRoot, precommitCleanup.Failures);
                }
                throw ex;
            }

            // 5. Capture target baseline from reservation authority and allocate AttemptId
            Guid attemptId = Guid.NewGuid();
            var probePlan = MigrationCapabilityProbePlan.Create(attemptId);
            var targetBaseline = new MigrationTargetBaseline(
                targetRootExistedBefore: reservation.Baseline.RootExistedBefore,
                promptsDirectoryExistedBefore: reservation.Baseline.RootExistedBefore && Directory.Exists(Path.Combine(bound.PhysicalRoot, "prompts")),
                recoveryDirectoryExistedBefore: reservation.Baseline.RootExistedBefore && Directory.Exists(Path.Combine(bound.PhysicalRoot, "recovery")));

            var manifest = MigrationManifestBuilder.BuildCopying(
                runtime.ActivePhysicalRoot,
                bound.PhysicalRoot,
                snapshot,
                attemptId,
                probePlan,
                targetBaseline);

            // If "prompts"/"recovery" already existed before this attempt (e.g. from a prior
            // successful partial state the baseline captured), bind them into the lease now;
            // otherwise they will be bound after copying creates them, below.
            if (targetBaseline.PromptsDirectoryExistedBefore)
            {
                targetOpLease.BindManagedChild(Path.Combine(bound.PhysicalRoot, "prompts"));
            }

            if (targetBaseline.RecoveryDirectoryExistedBefore)
            {
                targetOpLease.BindManagedChild(Path.Combine(bound.PhysicalRoot, "recovery"));
            }

            // 6. Create durable Copying manifest directly at final path
            string markerPath = Path.Combine(bound.PhysicalRoot, ".prompthelper-migration.json");

            try
            {
                _manifestRepo.CreateInitialCopyingManifestDurable(markerPath, manifest);
            }
            catch (Exception ex)
            {
                TargetReservationCleanupResult cleanup = reservation.Release();
                if (!cleanup.Success)
                {
                    throw new MigrationRollbackException(ex, bound.PhysicalRoot, cleanup.Failures);
                }
                throw;
            }

            // 7. Perform migration under target transaction
            using var tx = new DataFolderMigrationService.MigrationTargetTransaction(bound.PhysicalRoot);
            bool settingsCommitted = false;
            CapabilityValidationResult? capResult = null;
            SettingsSaveResult? saveResult = null;

            try
            {
                _migrationService.CopySnapshotToTarget(
                    runtime.ActivePhysicalRoot,
                    bound.PhysicalRoot,
                    snapshot,
                    manifest,
                    tx);

                // Copying may have just created "prompts"/"recovery" for the first time in
                // this attempt; bind whichever now exist and are not already bound so the
                // lease covers them for the remaining commit-critical steps below.
                if (!targetBaseline.PromptsDirectoryExistedBefore)
                {
                    targetOpLease.BindManagedChildIfPresent(Path.Combine(bound.PhysicalRoot, "prompts"));
                }

                if (!targetBaseline.RecoveryDirectoryExistedBefore)
                {
                    targetOpLease.BindManagedChildIfPresent(Path.Combine(bound.PhysicalRoot, "recovery"));
                }

                capResult = _capabilityValidator.ValidateWritable(
                    bound.PhysicalRoot,
                    tx,
                    null,
                    probePlan);

                // Assert ready gate before phase change
                _readyGate.AssertReady(
                    runtime.ActivePhysicalRoot,
                    bound.PhysicalRoot,
                    manifest,
                    snapshot,
                    runtime.BootstrapPhysicalRoot);

                // Update manifest to ReadyToCommit via an owned, handle-bound staging promotion
                manifest.Phase = MigrationManifestPhase.ReadyToCommit;
                _manifestRepo.WriteReadyManifestDurable(markerPath, manifest);

                // CRUU15-001: the ready gate above ran before this phase promotion, so the
                // marker that actually landed has to be re-read and proven identical before
                // the settings commit — otherwise the point of no return would be crossed on
                // the strength of a marker nothing revalidated after it was persisted.
                _manifestRepo.AssertPersistedMarkerMatches(markerPath, manifest);

                // The Ready manifest's own staging file reached a terminal state during that
                // write, so settle its ownership claim now. Leaving the ownership journal in
                // place would make the target non-empty for rollback and would violate the
                // "no in-flight control state" invariant the committed-startup path asserts.
                _fileOps.RetireOwnedArtifacts(bound.PhysicalRoot);

                // 8. PHYSICAL REVALIDATION #2 immediately before settings commit
                AssertLocatorStillMapsToBoundTarget(runtime.ActivePhysicalRoot, bound, runtime.BootstrapLexicalRoot);
                _treeValidator.ValidateManagedTree(bound.PhysicalRoot, ManagedTreeValidationMode.PreCreation);

                // 9. Acquire commit lease on payload files from ReadyGate through settings save
                using var commitLease = MigrationPayloadCommitLease.Acquire(
                    runtime.ActivePhysicalRoot,
                    bound.PhysicalRoot,
                    manifest);

                // 10. Commit settings with precondition token
                var newSettings = new AppSettings
                {
                    SchemaVersion = AppSettings.CurrentSchemaVersion,
                    DataRootPath = bound.LexicalRoot
                };

                try
                {
                    saveResult = _settingsRepo.SaveIfUnchanged(newSettings, settingsSnapshot.Precondition);
                    settingsCommitted = true;
                    tx.Commit();
                }
                catch (CommittedAtomicReplacementRequiresRestartException ex)
                {
                    // Candidate publication is the settings point of no return. The journal
                    // append is post-commit bookkeeping, so target payload must never roll back
                    // merely because that append failed.
                    settingsCommitted = true;
                    tx.Commit();
                    saveResult = new SettingsSaveResult(ex.Message);
                }
            }
            catch (Exception original)
            {
                if (!settingsCommitted)
                {
                    // Release the operation lease before rolling back: it holds "prompts"/
                    // "recovery" open without FILE_SHARE_DELETE (that is the whole point of a
                    // deny-delete lease), which would otherwise block Rollback()'s own removal
                    // of those same directories with a sharing violation. The reservation
                    // (released further below) remains the primary exclusion mechanism during
                    // cleanup.
                    targetOpLease.Dispose();

                    // Rollback transaction payload WHILE manifest is still on disk
                    MigrationRollbackResult rollback = tx.Rollback();
                    var allFailures = new List<MigrationRollbackFailure>(rollback.Failures);

                    bool isBootstrapRoot = PathIdentity.Equals(bound.PhysicalRoot, runtime.BootstrapPhysicalRoot);
                    MigrationTargetInventory inventory = MigrationTargetInventoryInspector.Inspect(bound.PhysicalRoot, manifest, isBootstrapRoot);

                    // The marker itself and the reservation's own ".app.lock" are still on
                    // disk at this point in the rollback (the marker is deleted right after,
                    // and the reservation is still held) so they are expected, not residue.
                    // Any OTHER declared control (a probe file, a staging file) still present
                    // means cleanup did not finish, and the marker must not be retired while
                    // that residue remains, even though it is a "recognized" (manifest-owned)
                    // path rather than an unknown one.
                    string appLockPath = PathIdentity.NormalizeForComparison(Path.Combine(bound.PhysicalRoot, ".app.lock"));
                    string normalizedMarkerPath = PathIdentity.NormalizeForComparison(markerPath);
                    bool hasResidualControls = inventory.DeclaredControls.Any(control =>
                        !PathIdentity.Equals(control, normalizedMarkerPath) &&
                        !PathIdentity.Equals(control, appLockPath));

                    bool cleanRollback = !inventory.HasUnknownEntries &&
                                         inventory.PayloadTemps.Count == 0 &&
                                         inventory.FinalArtifacts.Count == 0 &&
                                         !hasResidualControls &&
                                         inventory.AttemptCreatedDirectories.Count == 0 &&
                                         allFailures.Count == 0;

                    if (cleanRollback)
                    {
                        try
                        {
                            _manifestRepo.DeleteStrict(markerPath, manifest.AttemptId, manifest.Phase);
                            // Marker retirement removes only marker authority. Settle directory
                            // and payload claims whose exact objects the rollback just removed,
                            // so the ownership journal cannot keep a newly-created target root
                            // non-empty after an otherwise clean rollback.
                            _fileOps.RetireOwnedArtifacts(bound.PhysicalRoot);
                        }
                        catch (Exception markerEx)
                        {
                            allFailures.Add(new MigrationRollbackFailure(markerPath, "DeleteManifestMarker", markerEx.Message));
                            cleanRollback = false;
                        }
                    }

                    TargetReservationCleanupResult resCleanup = reservation.Release();
                    allFailures.AddRange(resCleanup.Failures);

                    if (allFailures.Count > 0 || !cleanRollback)
                    {
                        throw new MigrationRollbackException(original, bound.PhysicalRoot, allFailures);
                    }

                    throw;
                }
            }

            // POINT OF NO RETURN
            reservation.CommitRootOwnership();

            string? ownershipCleanupWarning = null;
            bool ownershipRetired = false;
            try
            {
                // The settings commit made the migrated payload ordinary live data. Its
                // pre-commit deletion authority must not pin the append-only ownership
                // ledger forever; retiring the claims never deletes the payload itself.
                _fileOps.RetireCommittedMigrationArtifacts(bound.PhysicalRoot);
                ownershipRetired = true;
            }
            catch (Exception ex)
            {
                // This is post-commit cleanup. Startup can retry it from the Ready marker,
                // but the transition must never be reported as uncommitted or rolled back.
                ownershipCleanupWarning = $"Could not retire committed migration ownership records: {ex.Message}";
            }

            string? manifestCleanupWarning = null;
            if (ownershipRetired)
            {
                try
                {
                    _manifestRepo.DeleteStrict(markerPath, manifest.AttemptId, manifest.Phase);
                    _fileOps.RetireCommittedMigrationArtifacts(bound.PhysicalRoot);
                }
                catch (Exception ex)
                {
                    manifestCleanupWarning = $"Could not delete migration marker: {ex.Message}";
                }
            }

            TargetReservationCleanupResult postcommitCleanup = reservation.Release();

            string? warning = WarningCombiner.Combine(
                settingsSnapshot.Warning,
                capResult?.Warning,
                saveResult?.Warning,
                ownershipCleanupWarning,
                manifestCleanupWarning,
                postcommitCleanup.ToWarning());

            return new DataFolderTransitionResult(
                Changed: true,
                RestartRequired: true,
                ExistingLibrarySelected: false,
                NormalizedTargetRoot: bound.LexicalRoot,
                Warning: warning);
        }
        finally
        {
            targetOpLease.Dispose();
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
