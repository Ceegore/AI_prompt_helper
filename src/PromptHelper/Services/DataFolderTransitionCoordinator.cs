using System;
using System.IO;
using PromptHelper.Models;

namespace PromptHelper.Services;

public sealed class DataFolderTransitionCoordinator
{
    private readonly AppSettingsRepository _settingsRepo;
    private readonly DataFolderMigrationService _migrationService;
    private readonly IUserConfirmationService _confirmationService;
    private readonly DataRootCapabilityValidator _capabilityValidator;
    private readonly IPhysicalPathResolver _pathResolver;

    public DataFolderTransitionCoordinator(
        AppSettingsRepository settingsRepo,
        DataFolderMigrationService migrationService,
        IUserConfirmationService confirmationService,
        DataRootCapabilityValidator? capabilityValidator = null,
        IPhysicalPathResolver? pathResolver = null)
    {
        _settingsRepo = settingsRepo ?? throw new ArgumentNullException(nameof(settingsRepo));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _capabilityValidator = capabilityValidator ?? new DataRootCapabilityValidator();
        _pathResolver = pathResolver ?? new WindowsPhysicalPathResolver();
    }

    public DataFolderTransitionResult RequestTransition(string candidateRoot)
    {
        if (string.IsNullOrWhiteSpace(candidateRoot))
        {
            throw new ArgumentException("Selected data folder path cannot be empty or whitespace.", nameof(candidateRoot));
        }

        string cleanTarget = PathIdentity.NormalizeForComparison(candidateRoot.Trim());
        string currentRoot = _settingsRepo.GetEffectiveDataRoot();
        string cleanCurrent = PathIdentity.NormalizeForComparison(currentRoot.Trim());

        if (PathIdentity.Equals(cleanTarget, cleanCurrent))
        {
            return new DataFolderTransitionResult(
                Changed: false,
                RestartRequired: false,
                ExistingLibrarySelected: false,
                NormalizedTargetRoot: cleanTarget,
                Warning: null);
        }

        DataRootTopologyValidator.ValidateDisjointOrSame(cleanCurrent, cleanTarget, null, _pathResolver);

        if (File.Exists(cleanTarget))
        {
            throw new ArgumentException($"Selected path is a file, not a directory: {cleanTarget}", nameof(candidateRoot));
        }

        DataFolderMigrationService.TargetInspection initialInspection = _migrationService.InspectTarget(cleanTarget);

        switch (initialInspection.Kind)
        {
            case DataFolderMigrationService.TargetLibraryKind.ValidPrimary:
            case DataFolderMigrationService.TargetLibraryKind.RecoverableBackupOnly:
                return HandleExistingTargetTransition(cleanTarget, initialInspection);

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

            case DataFolderMigrationService.TargetLibraryKind.Empty:
            default:
                return HandleEmptyTargetTransition(cleanCurrent, cleanTarget);
        }
    }

    private DataFolderTransitionResult HandleExistingTargetTransition(
        string cleanTarget,
        DataFolderMigrationService.TargetInspection initialInspection)
    {
        string confirmationMessage = initialInspection.Kind == DataFolderMigrationService.TargetLibraryKind.RecoverableBackupOnly
            ? "The selected folder contains a recoverable Prompt Helper safety backup. Switching will use that backup on startup without overwriting it or merging your current library.\n\nDo you want to switch to this existing library folder?"
            : "The selected folder already contains an existing Prompt Helper library. Switching will use that library without overwriting or merging your current library.\n\nDo you want to switch to this existing library folder?";

        bool confirmed = _confirmationService.Confirm(
            confirmationMessage,
            "Existing Library Folder Selected");

        if (!confirmed)
        {
            return new DataFolderTransitionResult(
                Changed: false,
                RestartRequired: false,
                ExistingLibrarySelected: true,
                NormalizedTargetRoot: cleanTarget,
                Warning: null);
        }

        using var reservation = TargetRootReservation.TryAcquire(cleanTarget);
        if (reservation is null)
        {
            throw new InvalidOperationException(
                "The selected target library is currently in use by another instance of Prompt Helper.");
        }

        DataFolderMigrationService.TargetInspection lockedInspection = _migrationService.InspectTarget(cleanTarget);
        if (lockedInspection.Kind != initialInspection.Kind)
        {
            throw new InvalidOperationException(
                "The target folder changed while waiting for confirmation. Please retry.");
        }

        _capabilityValidator.ValidateWritable(cleanTarget);

        AppSettings currentSettings = _settingsRepo.Load();
        currentSettings.DataRootPath = cleanTarget;
        SettingsSaveResult saveResult = _settingsRepo.Save(currentSettings);

        return new DataFolderTransitionResult(
            Changed: true,
            RestartRequired: true,
            ExistingLibrarySelected: true,
            NormalizedTargetRoot: cleanTarget,
            Warning: saveResult.Warning ?? initialInspection.Warning);
    }

    private DataFolderTransitionResult HandleEmptyTargetTransition(
        string cleanCurrent,
        string cleanTarget)
    {
        DataFolderMigrationService.MigrationSnapshot snapshot = _migrationService.CaptureSourceSnapshot(cleanCurrent);

        using var reservation = TargetRootReservation.TryAcquire(cleanTarget);
        if (reservation is null)
        {
            throw new InvalidOperationException(
                "The selected target folder is currently locked by another process.");
        }

        DataFolderMigrationService.TargetInspection lockedInspection = _migrationService.InspectTarget(cleanTarget);
        if (lockedInspection.Kind != DataFolderMigrationService.TargetLibraryKind.Empty)
        {
            throw new InvalidOperationException(
                "The target folder was modified by another process. Please retry.");
        }

        using var tx = new DataFolderMigrationService.MigrationTargetTransaction();
        _migrationService.CopySnapshotToTarget(cleanCurrent, cleanTarget, snapshot, tx);
        _capabilityValidator.ValidateWritable(cleanTarget);

        AppSettings currentSettings = _settingsRepo.Load();
        currentSettings.DataRootPath = cleanTarget;
        SettingsSaveResult saveResult = _settingsRepo.Save(currentSettings);

        tx.Commit();

        return new DataFolderTransitionResult(
            Changed: true,
            RestartRequired: true,
            ExistingLibrarySelected: false,
            NormalizedTargetRoot: cleanTarget,
            Warning: saveResult.Warning);
    }
}
