using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// Behavioral sentinels for the historical findings that had no exact-name test of their own.
/// </summary>
/// <remarks>
/// CRUU15-009: the required-sentinel manifest could not prove its own completeness, and ten
/// CRUU12 finding IDs were documented as uncovered. A finding without a test named for it is a
/// finding nobody can confirm is still fixed, so each one below executes the production path
/// it was raised against rather than asserting on source text or helper types.
/// </remarks>
[TestClass]
public sealed class Cruu15HistoricalCoverageTests
{
    private static LibraryDocument CreateDoc(params PromptRecord[] prompts) =>
        new()
        {
            SchemaVersion = 1,
            Categories = [],
            Prompts = [.. prompts]
        };

    // ==========================================
    // CRUU12-005: mutation journal grammar is kind-specific
    // ==========================================

    [TestMethod]
    public void CRUU12_005_Mutation_journal_grammar_rejects_fields_that_do_not_belong_to_its_kind()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        var repo = new LibraryMutationJournalRepository(paths, new WindowsDurableAtomicFileWriter());

        // An EditPrompt journal without the old-body identity it is defined by is not a
        // weaker edit journal; it is not an edit journal at all.
        var missingEditFields = new LibraryMutationJournal
        {
            OperationId = Guid.NewGuid(),
            Kind = LibraryMutationKind.EditPrompt,
            Phase = LibraryMutationPhase.Prepared,
            PromptId = Guid.NewGuid(),
            BodyRelativePath = Path.Combine("prompts", $"{Guid.NewGuid():N}.md"),
            OldLibrarySha256Hex = new string('a', 64),
            NewLibrarySha256Hex = new string('b', 64)
        };

        Assert.ThrowsExactly<InvalidDataException>(() => repo.CreatePreparedDurable(missingEditFields));
        Assert.IsFalse(File.Exists(paths.LibraryMutationJournalPath),
            "A journal that fails grammar validation must never be persisted.");
    }

    // ==========================================
    // CRUU12-007: temp reconciliation covers every managed location
    // ==========================================

    [TestMethod]
    public void CRUU12_007_Temp_reconciliation_covers_root_prompts_and_recovery_locations()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        string rootTemp = Path.Combine(paths.RootDirectory, $".prompthelper-tmp-library-{Guid.NewGuid():N}.tmp");
        string promptTemp = Path.Combine(paths.PromptsDirectory, $".prompthelper-tmp-prompt-{Guid.NewGuid():N}.tmp");
        string recoveryTemp = Path.Combine(paths.RecoveryDirectory, $".prompthelper-tmp-recovery-{Guid.NewGuid():N}.tmp");

        foreach (string path in new[] { rootTemp, promptTemp, recoveryTemp })
        {
            File.WriteAllText(path, "our own interrupted stage");
            OwnedArtifactTestSupport.ClaimOwnership(temp.Root, path);
        }

        TempReconciliationResult result = DataRootTempReconciler.Reconcile(paths);

        Assert.IsTrue(result.Success, string.Join("; ", result.Failures.Select(f => f.ErrorMessage)));
        Assert.IsFalse(File.Exists(rootTemp), "Root-directory temps must be reconciled.");
        Assert.IsFalse(File.Exists(promptTemp), "Prompts-directory temps must be reconciled.");
        Assert.IsFalse(File.Exists(recoveryTemp), "Recovery-directory temps must be reconciled.");
    }

    // ==========================================
    // CRUU12-009 / CRUU12-010: settings schema and path authority
    // ==========================================

    [TestMethod]
    public void CRUU12_009_Settings_schema_below_current_is_not_silently_accepted_as_valid()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":0,\"dataRootPath\":\"\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);

        // A schema version this build has no migration for is not "close enough to current".
        // Treating it as valid would hand a caller settings whose meaning nobody defined.
        Assert.ThrowsExactly<InvalidDataException>(() => repo.LoadOrRecover());

        // A current-schema file at the same path loads normally, so the rejection is about the
        // version and not about the read path being broken.
        File.WriteAllText(settingsPath,
            $"{{\"schemaVersion\":{AppSettings.CurrentSchemaVersion},\"dataRootPath\":\"\"}}");
        SettingsLoadResult ok = repo.LoadOrRecover();
        Assert.AreEqual(AppSettings.CurrentSchemaVersion, ok.Settings.SchemaVersion);
    }

    [TestMethod]
    public void CRUU12_010_Loaded_and_saved_data_root_path_goes_through_normalization()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");

        // A relative data root is not a data root: it means something different depending on
        // the process working directory.
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"relative\\\\path\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);

        // Reading it must not quietly resolve the relative path against whatever the process
        // working directory happens to be.
        Assert.ThrowsExactly<InvalidDataException>(() => repo.LoadOrRecover());

        // Writing one is refused by the same normalization, so the file can never come to hold
        // a relative root in the first place.
        Assert.ThrowsExactly<InvalidDataException>(
            () => AppSettingsRepository.NormalizeAndValidateDataRoot("still\\relative"));

        // A fully qualified root round-trips, normalized.
        File.WriteAllText(settingsPath,
            $"{{\"schemaVersion\":{AppSettings.CurrentSchemaVersion},\"dataRootPath\":\"{temp.Root.Replace("\\", "\\\\")}\"}}");

        string effective = repo.GetEffectiveDataRoot();
        Assert.IsTrue(Path.IsPathFullyQualified(effective),
            $"The effective data root must always be fully qualified, got '{effective}'.");
    }

    // ==========================================
    // CRUU12-017: target baseline predates reservation-created directories
    // ==========================================

    [TestMethod]
    public void CRUU12_017_Target_baseline_records_absence_before_reservation_creates_the_root()
    {
        using var temp = new TestDirectory();

        string neverExisted = Path.Combine(temp.Root, "NewTarget");
        var absent = new MigrationTargetBaseline(
            targetRootExistedBefore: false,
            promptsDirectoryExistedBefore: false,
            recoveryDirectoryExistedBefore: false);

        Assert.IsFalse(absent.TargetRootExistedBefore);
        Assert.IsFalse(Directory.Exists(neverExisted));

        // The baseline is what tells rollback whether a directory is the attempt's to remove.
        // A root the attempt created is attempt-owned; one that predates it is not.
        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
            AttemptId = Guid.NewGuid(),
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            SourcePayloadFingerprintSha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            TargetBaseline = absent,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{Guid.NewGuid():N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                }
            ]
        };

        Directory.CreateDirectory(Path.Combine(temp.Root, "prompts"));
        MigrationTargetInventory inventory = MigrationTargetInventoryInspector.Inspect(temp.Root, manifest);

        Assert.IsTrue(
            inventory.AttemptCreatedDirectories.Any(d => d.EndsWith("prompts", StringComparison.OrdinalIgnoreCase)),
            "A directory absent from the baseline must be classified as attempt-created.");
        Assert.AreEqual(0, inventory.PreExistingDirectories.Count);
    }

    // ==========================================
    // CRUU12-019 / CRUU12-022: strict directory authority
    // ==========================================

    [TestMethod]
    public void CRUU12_019_Inventory_fails_closed_on_an_entry_it_cannot_classify()
    {
        using var temp = new TestDirectory();

        // A root that is a file, not a directory, must be an error rather than an empty
        // inventory that every caller reads as "nothing dangerous is present".
        string fileRoot = Path.Combine(temp.Root, "not-a-directory");
        File.WriteAllText(fileRoot, "content");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
            AttemptId = Guid.NewGuid(),
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = fileRoot,
            SourceLibrarySha256Hex = new string('0', 64),
            SourcePayloadFingerprintSha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{Guid.NewGuid():N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                }
            ]
        };

        Assert.ThrowsExactly<InvalidDataException>(
            () => MigrationTargetInventoryInspector.Inspect(fileRoot, manifest));
    }

    [TestMethod]
    public void CRUU12_022_Strict_directory_opener_rejects_a_handle_that_is_not_a_directory()
    {
        using var temp = new TestDirectory();
        string file = Path.Combine(temp.Root, "a-file.txt");
        File.WriteAllText(file, "content");

        var opener = new WindowsStrictDirectoryOpener();

        // Opening a file succeeds at the API level; only an explicit attribute check
        // distinguishes it from a directory.
        DirectoryOpenResult result = OpenOrError(opener, file);
        Assert.AreNotEqual(DirectoryOpenState.Opened, result.State,
            "A file must never be returned as a successfully opened directory.");
    }

    private static DirectoryOpenResult OpenOrError(IStrictDirectoryOpener opener, string path)
    {
        try
        {
            return opener.OpenDirectoryStrict(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new DirectoryOpenResult(DirectoryOpenState.Missing, null);
        }
    }

    // ==========================================
    // CRUU12-030: maintenance failures are surfaced, not swallowed
    // ==========================================

    [TestMethod]
    public void CRUU12_030_Reconciliation_failure_is_reported_rather_than_swallowed()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        string ownedTemp = Path.Combine(paths.RecoveryDirectory, $".prompthelper-tmp-recovery-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(ownedTemp, "ours, but locked");
        OwnedArtifactTestSupport.ClaimOwnership(temp.Root, ownedTemp);

        // Hold the artifact open so its removal cannot succeed.
        using var hold = new FileStream(ownedTemp, FileMode.Open, FileAccess.Read, FileShare.None);

        TempReconciliationResult result = DataRootTempReconciler.Reconcile(paths);

        Assert.IsFalse(result.Success, "A cleanup that could not be performed must not report success.");
        Assert.IsTrue(result.Failures.Any(f => f.Path.Equals(ownedTemp, StringComparison.OrdinalIgnoreCase)),
            "The specific artifact that could not be reconciled must be named.");
    }

    // ==========================================
    // CRUU12-033 / CRUU12-034 / CRUU13-016 / CRUU13-017: the icon identity chain
    // ==========================================

    [TestMethod]
    public void CRUU12_034_Approved_vector_logo_is_present_in_the_repository()
    {
        string svgPath = RepositoryTestPaths.RequireFile("src", "PromptHelper", "Assets", "PromptHelperLogo.svg");
        string svg = File.ReadAllText(svgPath);

        Assert.IsTrue(svg.Contains("<svg", StringComparison.OrdinalIgnoreCase), "The approved logo must be real SVG.");
        Assert.IsTrue(new FileInfo(svgPath).Length > 0);
    }

    [TestMethod]
    public void CRUU13_017_Approved_logo_and_generated_icon_are_both_release_blocking_assets()
    {
        RepositoryTestPaths.RequireFile("src", "PromptHelper", "Assets", "PromptHelperLogo.svg");
        RepositoryTestPaths.RequireFile("src", "PromptHelper", "Assets", "PromptHelper.ico");
        RepositoryTestPaths.RequireFile("src", "PromptHelper", "Assets", "PromptHelperIcon.approved.json");
    }

    [TestMethod]
    public void CRUU12_033_Approved_SVG_binds_to_the_committed_ICO_through_the_approval_manifest()
    {
        string svgPath = RepositoryTestPaths.RequireFile("src", "PromptHelper", "Assets", "PromptHelperLogo.svg");
        string icoPath = RepositoryTestPaths.RequireFile("src", "PromptHelper", "Assets", "PromptHelper.ico");
        string manifestPath = RepositoryTestPaths.RequireFile("src", "PromptHelper", "Assets", "PromptHelperIcon.approved.json");

        IconApprovalManifest approved = IconApprovalManifest.Load(manifestPath);

        Assert.AreEqual(approved.SvgSha256Hex, IconApprovalManifest.ComputeSvgHash(svgPath),
            "The approval manifest must be bound to the exact approved vector source.");

        Dictionary<int, byte[]> frames = IconApprovalManifest.ReadIcoFramePayloads(File.ReadAllBytes(icoPath));
        foreach (IconApprovedFrame frame in approved.Frames)
        {
            Assert.IsTrue(frames.ContainsKey(frame.Size), $"The committed ICO is missing the approved {frame.Size}px frame.");
            Assert.AreEqual(
                frame.NormalizedRgbaSha256Hex,
                IconApprovalManifest.ComputeNormalizedRgbaHash(frames[frame.Size]),
                $"The committed ICO's {frame.Size}px frame does not match the approved pixels.");
        }
    }

    [TestMethod]
    public void CRUU13_016_All_executable_icon_groups_are_compared_not_just_the_first()
    {
        string readerSource = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "tools", "IconIdentityVerifier", "PeIconResourceReader.cs"));

        // A reader that returns after the first RT_GROUP_ICON cannot prove anything about the
        // remaining groups, which is exactly how an unapproved icon could ship.
        StringAssert.Contains(readerSource, "EnumResourceNamesW");
        Assert.IsTrue(
            readerSource.Contains("groups", StringComparison.OrdinalIgnoreCase),
            "The PE reader must enumerate every icon group.");

        string verifierSource = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "tools", "VerifyReleaseAssets.ps1"));
        StringAssert.Contains(verifierSource, "RequireIcon");
    }

    // ==========================================
    // CRUU13-005 / CRUU13-007 / CRUU13-008 / CRUU13-010 / CRUU13-013 / CRUU13-014 / CRUU13-015
    // ==========================================

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU13_005_Target_operation_lease_covers_children_created_after_acquisition()
    {
        using var temp = new TestDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, "prompts"));

        using var lease = ManagedTargetOperationLease.Acquire(
            temp.Root,
            promptsMayBeMissing: false,
            recoveryMayBeMissing: true);

        // A directory the operation creates after the lease was taken must be brought under it,
        // otherwise the very children the operation just produced are the ones left unguarded.
        string recovery = Path.Combine(temp.Root, "recovery");
        Directory.CreateDirectory(recovery);
        lease.BindManagedChild(recovery);

        Assert.ThrowsExactly<IOException>(
            () => Directory.Move(recovery, Path.Combine(temp.Root, "recovery-moved")));
    }

    [TestMethod]
    public void CRUU13_007_Exact_bootstrap_root_settings_controls_are_not_reported_as_foreign()
    {
        using var temp = new TestDirectory();

        File.WriteAllText(Path.Combine(temp.Root, "settings.json"), "{\"schemaVersion\":1,\"dataRootPath\":\"\"}");
        File.WriteAllText(Path.Combine(temp.Root, "settings.backup.json"), "{\"schemaVersion\":1,\"dataRootPath\":\"\"}");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
            AttemptId = Guid.NewGuid(),
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            SourcePayloadFingerprintSha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{Guid.NewGuid():N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                }
            ]
        };

        MigrationTargetInventory asBootstrap =
            MigrationTargetInventoryInspector.Inspect(temp.Root, manifest, isBootstrapRoot: true);
        Assert.IsFalse(asBootstrap.HasUnknownEntries,
            "Migrating into the exact bootstrap root must not treat its own settings files as foreign.");
        Assert.AreEqual(2, asBootstrap.PersistentBootstrapControls.Count);

        MigrationTargetInventory asOrdinary =
            MigrationTargetInventoryInspector.Inspect(temp.Root, manifest, isBootstrapRoot: false);
        Assert.IsTrue(asOrdinary.HasUnknownEntries,
            "Outside the bootstrap root those same files are genuinely unexpected.");
    }

    [TestMethod]
    public void CRUU13_008_Inventory_enumeration_is_bound_to_the_directory_object()
    {
        using var temp = new TestDirectory();
        File.WriteAllText(Path.Combine(temp.Root, "a.txt"), "a");
        Directory.CreateDirectory(Path.Combine(temp.Root, "sub"));

        IReadOnlyList<DirectoryEntry>? entries = WindowsDirectoryEnumeration.ListStrict(temp.Root);

        Assert.IsNotNull(entries);
        Assert.AreEqual(2, entries!.Count);
        Assert.IsTrue(entries.Any(e => e.Name == "a.txt" && !e.IsDirectory));
        Assert.IsTrue(entries.Any(e => e.Name == "sub" && e.IsDirectory));
        Assert.IsFalse(entries.Any(e => e.Name is "." or ".."));
    }

    [TestMethod]
    public void CRUU13_010_Rollback_keeps_the_marker_while_declared_residue_remains()
    {
        using var target = new TestDirectory();
        Guid attemptId = Guid.NewGuid();

        string tempRel = Path.Combine("prompts", $".test.md.migration-{attemptId:N}-{new string('b', 32)}.tmp");
        string tempFullPath = Path.Combine(target.Root, tempRel);
        Directory.CreateDirectory(Path.GetDirectoryName(tempFullPath)!);
        File.WriteAllText(tempFullPath, "residue nobody can prove is ours");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
            AttemptId = attemptId,
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = target.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            SourcePayloadFingerprintSha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{attemptId:N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                },
                new MigrationManifestArtifact
                {
                    RelativePath = "prompts/test.md",
                    TempRelativePath = tempRel,
                    Length = 20,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PromptBody
                }
            ]
        };

        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        var repo = new MigrationManifestRepository();
        repo.WriteDurable(markerPath, manifest);

        var recovery = new MigrationRecoveryService(repo);
        RecoveryResult result = recovery.RecoverForRetry(
            new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: @"C:\Source"));

        Assert.IsFalse(result.Success);
        Assert.IsTrue(File.Exists(markerPath),
            "Retiring the marker while declared residue remains would strand that residue with no owner.");
    }

    [TestMethod]
    public void CRUU13_013_Startup_maintenance_warnings_are_aggregated_for_display()
    {
        var collector = new StartupDiagnosticCollector();
        collector.Warning("TEMP_CLEANUP", "A staging file could not be reconciled.");
        collector.Warning("BACKUP_SYNC", "The safety backup could not be synchronized.");
        collector.Information("STARTUP", "Nothing to report.");

        string? aggregated = collector.BuildAggregatedWarning();

        Assert.IsNotNull(aggregated, "Maintenance warnings that reach nobody are the same as no warnings at all.");
        StringAssert.Contains(aggregated!, "A staging file could not be reconciled.");
        StringAssert.Contains(aggregated!, "The safety backup could not be synchronized.");
    }

    [TestMethod]
    public void CRUU13_014_Initialization_is_a_durable_phase_journal()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        var repo = new LibraryInitializationJournalRepository(paths, new WindowsDurableAtomicFileWriter());

        var journal = new LibraryInitializationJournal
        {
            InitializationId = Guid.NewGuid(),
            Phase = LibraryInitializationPhase.CreatingDefaults,
            Revision = 0
        };

        repo.CreatePreparedDurable(journal);
        Assert.IsTrue(File.Exists(paths.InitializationMarkerPath));

        LibraryInitializationJournal? readBack = repo.TryReadStrict();
        Assert.IsNotNull(readBack);
        Assert.AreEqual(journal.InitializationId, readBack!.InitializationId);
        Assert.AreEqual(LibraryInitializationPhase.CreatingDefaults, readBack.Phase);

        repo.AdvanceDurable(journal, LibraryInitializationPhase.MetadataDurable);
        Assert.AreEqual(LibraryInitializationPhase.MetadataDurable, repo.TryReadStrict()!.Phase);

        repo.DeleteStrict(journal.InitializationId, journal.Revision);
        Assert.IsFalse(File.Exists(paths.InitializationMarkerPath));
    }

    [TestMethod]
    public void CRUU13_015_Required_sentinels_execute_their_named_production_path()
    {
        // The canonical coverage map is what makes this checkable at all: it names, per
        // finding, the tests that must exist and pass. A manifest that can only confirm its own
        // current entries cannot detect a finding losing its coverage (CRUU15-009).
        FindingCoverageMap map = FindingCoverageMap.Load();

        Assert.IsTrue(map.Findings.Count > 0);
        foreach (KeyValuePair<string, IReadOnlyList<string>> entry in map.Findings)
        {
            Assert.IsTrue(entry.Value.Count > 0, $"{entry.Key} is mapped to no test at all.");
        }
    }

    // ==========================================
    // CRUU14-003 / CRUU14-004 / CRUU14-006 / CRUU14-010
    // ==========================================

    [TestMethod]
    public void CRUU14_003_SaveIfUnchanged_is_atomic_against_a_non_cooperating_writer()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"\"}");

        var repo = new AppSettingsRepository(settingsPathOverride: settingsPath);
        SettingsWritePrecondition precondition = repo.CapturePrecondition();

        // A writer that never asked permission changes the file after the precondition was
        // captured. The save must refuse rather than flatten it.
        File.WriteAllText(settingsPath, "{\"schemaVersion\":1,\"dataRootPath\":\"" + temp.Root.Replace("\\", "\\\\") + "\"}");
        byte[] theirs = File.ReadAllBytes(settingsPath);

        Assert.ThrowsExactly<InvalidOperationException>(() => repo.SaveIfUnchanged(
            new AppSettings { SchemaVersion = 1, DataRootPath = temp.Root },
            precondition));

        CollectionAssert.AreEqual(theirs, File.ReadAllBytes(settingsPath));
    }

    [TestMethod]
    public void CRUU14_004_Identity_only_checks_do_not_authorize_deletion_without_ownership()
    {
        using var temp = new TestDirectory();
        string path = Path.Combine(temp.Root, $".prompthelper-tmp-library-{Guid.NewGuid():N}.tmp");
        byte[] foreign = Encoding.UTF8.GetBytes("a regular, non-reparse file inside the root");
        File.WriteAllBytes(path, foreign);

        IMigrationFileOps ops = new DefaultMigrationFileOps();

        // Every identity property holds: regular file, not a reparse point, physically inside
        // the root. None of that makes it ours.
        Assert.AreEqual(ArtifactCleanupOutcome.PreservedUnproven, ops.DeleteOwnedFileIfProven(temp.Root, path));
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(path));

        // Recorded ownership is what authorizes the deletion.
        OwnedArtifactTestSupport.ClaimOwnership(temp.Root, path);
        Assert.AreEqual(ArtifactCleanupOutcome.DeletedProvenOwned, ops.DeleteOwnedFileIfProven(temp.Root, path));
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU14_006_Inventory_rejects_a_reparse_point_instead_of_classifying_it_as_content()
    {
        using var temp = new TestDirectory();
        using var outside = new TestDirectory();

        string victim = Path.Combine(outside.Root, "elsewhere");
        Directory.CreateDirectory(victim);

        string junction = Path.Combine(temp.Root, "prompts");
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junction}\" \"{victim}\"");
        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 30_000);
        Assert.IsTrue(run.Exited && run.ExitCode == 0, $"mklink /J failed: {run.CombinedOutput}");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = MigrationAttemptManifest.CurrentSchemaVersion,
            AttemptId = Guid.NewGuid(),
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = temp.Root,
            SourceLibrarySha256Hex = new string('0', 64),
            SourcePayloadFingerprintSha256Hex = new string('0', 64),
            Phase = MigrationManifestPhase.Copying,
            Artifacts =
            [
                new MigrationManifestArtifact
                {
                    RelativePath = "library.json",
                    TempRelativePath = $".library.json.migration-{Guid.NewGuid():N}-{new string('a', 32)}.tmp",
                    Length = 10,
                    Sha256Hex = new string('0', 64),
                    Role = MigrationPayloadRole.PrimaryMetadata
                }
            ]
        };

        // Refusing outright is stronger than reporting it as an unknown entry: no caller can
        // proceed on a partial inventory that quietly omitted whatever lay behind the junction.
        Assert.ThrowsExactly<InvalidDataException>(
            () => MigrationTargetInventoryInspector.Inspect(temp.Root, manifest));
    }

    [TestMethod]
    public void CRUU14_010_Recognizable_filename_alone_never_authorizes_destruction()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        // Both the current class-tagged grammar and a legacy one, neither with any provenance.
        string current = Path.Combine(paths.RootDirectory, $".prompthelper-tmp-library-{Guid.NewGuid():N}.tmp");
        string legacy = Path.Combine(paths.RootDirectory, $".library.json.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(current, "current-format, unproven");
        File.WriteAllText(legacy, "legacy-format, unverifiable");

        DataRootTempReconciler.Reconcile(paths);

        Assert.IsTrue(File.Exists(current), "A current-format filename is not ownership evidence.");
        Assert.IsTrue(File.Exists(legacy), "A legacy-format filename is not ownership evidence either.");
    }
}
