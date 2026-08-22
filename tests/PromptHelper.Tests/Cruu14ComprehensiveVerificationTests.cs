using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class Cruu14ComprehensiveVerificationTests
{
    private static string Hash(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static LibraryDocument CreateDoc(params PromptRecord[] prompts) =>
        new()
        {
            SchemaVersion = 1,
            Categories = [],
            Prompts = [.. prompts]
        };

    // ==========================================
    // CRUU14-001: same-object durable staging/promotion
    // ==========================================

    [TestMethod]
    public void CRUU14_001_OwnedDurableStage_promotes_exact_written_content()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "target.txt");
        string staging = Path.Combine(temp.Root, ".stage.tmp");
        byte[] content = System.Text.Encoding.UTF8.GetBytes("owned stage content");

        using (var stage = WindowsOwnedDurableStage.CreateNew(staging))
        {
            stage.Write(content);
            stage.FlushDurable();
            stage.PromoteReplaceExact(target);
        }

        Assert.IsFalse(File.Exists(staging), "Staging path must no longer exist after promotion.");
        Assert.AreEqual("owned stage content", File.ReadAllText(target));
    }

    [TestMethod]
    public void CRUU14_001_OwnedDurableStage_DeleteExact_removes_the_exact_staged_object()
    {
        using var temp = new TestDirectory();
        string staging = Path.Combine(temp.Root, ".stage.tmp");

        using (var stage = WindowsOwnedDurableStage.CreateNew(staging))
        {
            stage.Write(System.Text.Encoding.UTF8.GetBytes("abandoned"));
            stage.FlushDurable();
            stage.DeleteExact();
        }

        Assert.IsFalse(File.Exists(staging), "Handle-bound delete must remove the staged object.");
    }

    [TestMethod]
    public void CRUU14_001_ReplaceDurable_and_CreateNewDurable_round_trip_via_handle_bound_promotion()
    {
        using var temp = new TestDirectory();
        var writer = new WindowsDurableAtomicFileWriter();
        string target = Path.Combine(temp.Root, "library.json");

        writer.CreateNewDurable(target, System.Text.Encoding.UTF8.GetBytes("first"), DurableFileClass.LibraryMetadata);
        Assert.AreEqual("first", File.ReadAllText(target));

        writer.ReplaceDurable(target, System.Text.Encoding.UTF8.GetBytes("second"), DurableFileClass.LibraryMetadata);
        Assert.AreEqual("second", File.ReadAllText(target));

        // No leftover staging files under the deterministic naming convention.
        Assert.IsFalse(Directory.EnumerateFiles(temp.Root, ".prompthelper-tmp-*").Any());
    }

    // ==========================================
    // CRUU14-002/003: commit precondition re-verified with a native handle check
    // ==========================================

    [TestMethod]
    public void CRUU14_002_ExpectedFileCasReplacer_accepts_matching_content()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] content = System.Text.Encoding.UTF8.GetBytes("current content");
        File.WriteAllBytes(target, content);
        byte[] replacement = System.Text.Encoding.UTF8.GetBytes("replacement content");

        new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
            temp.Root,
            target,
            ExpectedFileState.Present(Hash(content)),
            replacement,
            DurableFileClass.LibraryMetadata);

        CollectionAssert.AreEqual(replacement, File.ReadAllBytes(target));
    }

    [TestMethod]
    public void CRUU14_002_ExpectedFileCasReplacer_rejects_content_changed_externally()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] originalContent = System.Text.Encoding.UTF8.GetBytes("original content");
        File.WriteAllBytes(target, originalContent);
        string expectedHex = Hash(originalContent);

        // External writer changes the file after the caller captured its expected hash.
        byte[] foreign = System.Text.Encoding.UTF8.GetBytes("changed by someone else");
        File.WriteAllBytes(target, foreign);

        Assert.Throws<StaleExpectedFileException>(() =>
            new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
                temp.Root,
                target,
                ExpectedFileState.Present(expectedHex),
                System.Text.Encoding.UTF8.GetBytes("our replacement"),
                DurableFileClass.LibraryMetadata));

        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(target));
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    public void CRUU14_002_CommitIfPrimaryUnchanged_uses_native_handle_check_not_just_cached_bytes()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        var writer = new AtomicTextWriter();
        var libRepo = new LibraryRepository(paths, writer);
        var originalDoc = CreateDoc();
        libRepo.Commit(CanonicalLibraryPackage.Create(originalDoc));
        string staleHash = Hash(File.ReadAllBytes(paths.LibraryPath));

        // Change the file directly (not through the repository) after the hash was captured.
        File.WriteAllText(paths.LibraryPath, File.ReadAllText(paths.LibraryPath) + " ");

        var newPkg = CanonicalLibraryPackage.Create(CreateDoc(new PromptRecord { Id = Guid.NewGuid(), Title = "Mine", CategoryId = null }));
        Assert.Throws<InvalidOperationException>(() => libRepo.CommitIfPrimaryUnchanged(newPkg, staleHash));
    }

    // ==========================================
    // CRUU14-002 Problem B: category/rename/delete/move now route through CAS
    // ==========================================

    [TestMethod]
    public void CRUU14_002_CreateCategory_external_primary_change_is_rejected_not_overwritten()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
        var initial = startup.LoadOrInitialize();

        var service = new PromptLibraryService(initial.Document, libRepo, promptRepo);

        // External writer commits a change directly through the repository, bypassing the
        // service's in-memory _document.
        libRepo.Commit(CanonicalLibraryPackage.Create(CreateDoc(new PromptRecord { Id = Guid.NewGuid(), Title = "External", CategoryId = null })));

        Assert.Throws<InvalidOperationException>(() => service.CreateCategory(null, "New Category"));
    }

    [TestMethod]
    public void CRUU14_002_MovePrompt_external_primary_change_is_rejected_not_overwritten()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        var writer = new AtomicTextWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startup = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
        var initial = startup.LoadOrInitialize();

        var service = new PromptLibraryService(initial.Document, libRepo, promptRepo);
        var created = service.CreatePrompt(null, "body", "Title");
        var category = service.CreateCategory(null, "Target Category");

        // External writer commits a change directly, bypassing the service's in-memory state.
        var externalDoc = LibraryDocumentCloner.Clone(libRepo.ReadPrimary());
        externalDoc.Prompts.Add(new PromptRecord { Id = Guid.NewGuid(), Title = "External", CategoryId = null });
        libRepo.Commit(CanonicalLibraryPackage.Create(externalDoc));

        Assert.Throws<InvalidOperationException>(() => service.MovePrompt(created.Value.Id, category.Value.Id));
    }

    // ==========================================
    // CRUU14-004/005: handle-bound journal/marker retirement rejects a swapped object
    // ==========================================

    [TestMethod]
    [TestCategory("MutationRecovery")]
    public void CRUU14_005_MutationJournal_DeleteStrict_rejects_when_content_changed_since_capture()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();
        var writer = new AtomicTextWriter();
        var journalRepo = new LibraryMutationJournalRepository(paths, writer);

        Guid promptId = Guid.NewGuid();
        var journal = new LibraryMutationJournal
        {
            OperationId = Guid.NewGuid(),
            Kind = LibraryMutationKind.CreatePrompt,
            Phase = LibraryMutationPhase.Prepared,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = new string('0', 64),
            NewLibrarySha256Hex = new string('1', 64),
            NewBodyLength = 4,
            NewBodySha256Hex = new string('2', 64)
        };
        journalRepo.CreatePreparedDurable(journal);
        journalRepo.AdvanceDurable(journal, LibraryMutationPhase.BodyDurable);
        journalRepo.AdvanceDurable(journal, LibraryMutationPhase.MetadataDurable);
        long revisionAtRetireTime = journal.Revision;

        // Simulate the journal being replaced by a foreign object at the same path, reporting
        // an unexpected revision, right before retirement is attempted.
        var foreignJournal = new LibraryMutationJournal
        {
            SchemaVersion = LibraryMutationJournal.CurrentSchemaVersion,
            Revision = 999,
            OperationId = journal.OperationId,
            Kind = journal.Kind,
            Phase = journal.Phase,
            PromptId = journal.PromptId,
            BodyRelativePath = journal.BodyRelativePath,
            OldLibrarySha256Hex = journal.OldLibrarySha256Hex,
            NewLibrarySha256Hex = journal.NewLibrarySha256Hex,
            NewBodyLength = journal.NewBodyLength,
            NewBodySha256Hex = journal.NewBodySha256Hex
        };
        File.WriteAllBytes(paths.LibraryMutationJournalPath, LibraryMutationJournalRepository.SerializeValidate(foreignJournal));

        Assert.Throws<InvalidDataException>(() => journalRepo.DeleteStrict(journal.OperationId, revisionAtRetireTime));
        Assert.IsTrue(File.Exists(paths.LibraryMutationJournalPath), "A journal that no longer matches the expected revision must not be deleted.");
    }

    [TestMethod]
    [TestCategory("CrashRecovery")]
    public void CRUU14_005_MigrationManifest_DeleteStrict_rejects_wrong_attempt_or_phase()
    {
        using var temp = new TestDirectory();
        var repo = new MigrationManifestRepository();
        string markerPath = Path.Combine(temp.Root, ".prompthelper-migration.json");

        var manifest = new MigrationAttemptManifest
        {
            SchemaVersion = 4,
            AttemptId = Guid.NewGuid(),
            SourcePhysicalRoot = Path.Combine(temp.Root, "source"),
            TargetPhysicalRoot = temp.Root,
            Phase = MigrationManifestPhase.Copying,
            Artifacts = []
        };
        repo.CreateInitialCopyingManifestDurable(markerPath, manifest);

        Assert.Throws<InvalidDataException>(() =>
            repo.DeleteStrict(markerPath, manifest.AttemptId, MigrationManifestPhase.ReadyToCommit));
        Assert.Throws<InvalidDataException>(() =>
            repo.DeleteStrict(markerPath, Guid.NewGuid(), manifest.Phase));

        Assert.IsTrue(File.Exists(markerPath), "Marker must not be deleted when identity/phase does not match.");

        // Correct identity/phase does retire it.
        repo.DeleteStrict(markerPath, manifest.AttemptId, manifest.Phase);
        Assert.IsFalse(File.Exists(markerPath));
    }

    // ==========================================
    // CRUU14-007: existing-target active prompt bodies are strict-UTF-8 validated
    // ==========================================

    [TestMethod]
    public void CRUU14_007_Existing_target_UTF16_prompt_body_is_rejected()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();

        Directory.CreateDirectory(Path.Combine(target.Root, "prompts"));
        Guid promptId = Guid.NewGuid();
        var category = new CategoryRecord { Id = Guid.NewGuid(), Name = "General" };
        var prompt = new PromptRecord { Id = promptId, CategoryId = category.Id, Title = "Test" };
        var doc = new LibraryDocument { SchemaVersion = 1, Categories = [category], Prompts = [prompt] };
        string json = JsonSerializer.Serialize(doc, LibraryRepository.JsonOptions);
        File.WriteAllText(Path.Combine(target.Root, "library.json"), json);
        File.WriteAllText(Path.Combine(target.Root, "library.backup.json"), json);

        byte[] utf16Bytes = System.Text.Encoding.Unicode.GetPreamble()
            .Concat(System.Text.Encoding.Unicode.GetBytes("UTF-16 body"))
            .ToArray();
        File.WriteAllBytes(Path.Combine(target.Root, "prompts", $"{promptId:N}.md"), utf16Bytes);

        var migration = new DataFolderMigrationService();
        var inspection = migration.InspectTarget(target.Root);

        Assert.AreNotEqual(
            DataFolderMigrationService.TargetLibraryKind.ValidPrimary,
            inspection.Kind,
            "A target whose active prompt body is not valid UTF-8 must not be accepted.");
    }

    // ==========================================
    // CRUU14-008: commit leases refuse reparse-point files (best-effort: some CI/dev
    // machines run without the privilege required to create a file symlink; the test
    // reports itself inconclusive rather than failing on an environment limitation).
    // ==========================================

    [TestMethod]
    public void CRUU14_008_ExistingTargetCommitLease_rejects_symlinked_metadata_file()
    {
        using var temp = new TestDirectory();
        using var outside = new TestDirectory();

        string realFile = Path.Combine(outside.Root, "real-library.json");
        File.WriteAllText(realFile, "{\"schemaVersion\":1,\"categories\":[],\"prompts\":[]}");

        string linkPath = Path.Combine(temp.Root, "library.json");
        // CRUU15-009: a required sentinel may not opt out of the environment it needs. If file
        // symlinks cannot be created here, the reparse-point defence is simply unverified, and
        // reporting that as anything other than a failure is how it stops being verified at
        // all. Enable Windows Developer Mode, or run elevated.
        Assert.IsTrue(
            TryCreateFileSymlink(linkPath, realFile),
            "Creating a file symlink is required for this test. Enable Windows Developer Mode or run elevated.");

        var doc = new LibraryDocument { SchemaVersion = 1, Categories = [], Prompts = [] };
        byte[] metadataBytes = File.ReadAllBytes(realFile);
        byte[] fingerprint = DataFolderMigrationService.ComputeCombinedFingerprint(
            metadataBytes, new System.Collections.Generic.Dictionary<Guid, byte[]>());

        Assert.Throws<InvalidDataException>(() =>
            ExistingTargetCommitLease.Acquire(temp.Root, linkPath, doc, fingerprint));
    }

    private static bool TryCreateFileSymlink(string linkPath, string targetPath)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink \"{linkPath}\" \"{targetPath}\"");
            ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 5_000);
            return run.Exited && run.ExitCode == 0 && File.Exists(linkPath);
        }
        catch
        {
            return false;
        }
    }

    // ==========================================
    // CRUU14-009: backup sync no longer accepts an arbitrary public canonical package
    // ==========================================

    [TestMethod]
    public void CRUU14_009_SynchronizeBackup_CanonicalLibraryPackage_overload_is_not_public()
    {
        MethodInfo? method = typeof(LibraryRepository)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m =>
                m.Name == nameof(LibraryRepository.SynchronizeBackup) &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == typeof(CanonicalLibraryPackage));

        Assert.IsNotNull(method, "SynchronizeBackup(CanonicalLibraryPackage) should still exist internally.");
        Assert.IsFalse(method!.IsPublic,
            "SynchronizeBackup(CanonicalLibraryPackage) must not be public: any caller could construct an arbitrary package and publish it to backup independently of the current primary.");
    }
}
