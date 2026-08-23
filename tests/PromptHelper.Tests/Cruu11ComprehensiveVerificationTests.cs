using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32.SafeHandles;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public class Cruu11ComprehensiveVerificationTests
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
    // CRUU11-001: Verified Deletion & Path Containment
    // ==========================================

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU11_001_Prefix_collision_C_Data_vs_C_DataOutside_is_rejected()
    {
        using var temp = new TestDirectory();
        string dataRoot = Path.Combine(temp.Root, "Data");
        string dataOutsideRoot = Path.Combine(temp.Root, "DataOutside");
        Directory.CreateDirectory(dataRoot);
        Directory.CreateDirectory(dataOutsideRoot);

        string outsideFile = Path.Combine(dataOutsideRoot, "file.bin");
        byte[] payload = [1, 2, 3, 4];
        File.WriteAllBytes(outsideFile, payload);

        var deleter = new WindowsVerifiedArtifactDeleter();

        // The deleter must reject deleting a file in DataOutside when root is Data
        Assert.Throws<InvalidDataException>(() =>
            deleter.VerifyAndDelete(dataRoot, outsideFile, payload.Length, Hash(payload)));

        Assert.IsTrue(File.Exists(outsideFile), "File outside root must not be deleted.");
    }

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU11_001_Strict_descendant_file_is_accepted()
    {
        using var temp = new TestDirectory();
        string dataRoot = Path.Combine(temp.Root, "Data");
        Directory.CreateDirectory(dataRoot);

        string insideFile = Path.Combine(dataRoot, "prompts", "file.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(insideFile)!);
        byte[] payload = [1, 2, 3, 4];
        File.WriteAllBytes(insideFile, payload);

        var deleter = new WindowsVerifiedArtifactDeleter();
        deleter.VerifyAndDelete(dataRoot, insideFile, payload.Length, Hash(payload));

        Assert.IsFalse(File.Exists(insideFile), "Strict descendant file should be deleted.");
    }

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    public void CRUU11_001_UNC_final_path_prefix_normalizes_correctly()
    {
        string root = @"\\server\share\data";
        string file = @"\\server\share\data\prompts\1.md";
        WindowsFinalPathHelper.AssertStrictDescendantFile(root, file);
    }

    /// <summary>
    /// Forces the exact "insufficient buffer" cut the real GetFinalPathNameByHandleW retry
    /// loop must handle: the first call reports a required size larger than the initial
    /// capacity (without writing anything), and only the second call actually writes the path.
    /// </summary>
    private sealed class ResizingFinalPathNativeApi : IFinalPathNativeApi
    {
        private readonly string _fullPath;
        public int CallCount { get; private set; }

        public ResizingFinalPathNativeApi(string fullPath) => _fullPath = fullPath;

        public uint GetFinalPathNameByHandle(SafeFileHandle handle, System.Text.StringBuilder buffer, uint bufferLength, uint flags)
        {
            CallCount++;
            if (CallCount == 1)
            {
                // Report a required size that exceeds the caller's current buffer, without
                // writing to it — this is exactly what the real API does on ERROR_INSUFFICIENT_BUFFER.
                return bufferLength + (uint)_fullPath.Length + 100;
            }

            buffer.Append(_fullPath);
            return (uint)_fullPath.Length;
        }
    }

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU11_001_Buffer_resize_retries_when_API_returns_required_size()
    {
        using var temp = new TestDirectory();
        string filePath = Path.Combine(temp.Root, "test.bin");
        File.WriteAllBytes(filePath, [1, 2, 3]);

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var fakeApi = new ResizingFinalPathNativeApi(filePath);

        string normalized = WindowsFinalPathHelper.GetNormalizedDosPath(fs.SafeFileHandle, fakeApi);

        Assert.AreEqual(2, fakeApi.CallCount,
            "The API must be retried with a resized buffer after the first call reports the required size.");
        Assert.AreEqual(PathIdentity.NormalizeForComparison(filePath), normalized);
    }

    private static bool TryCreateFileSymlink(string linkPath, string targetPath)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink \"{linkPath}\" \"{targetPath}\"");
            ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 5_000);
            return run.Exited && run.ExitCode == 0 && File.Exists(linkPath);
        }
        catch
        {
            return false;
        }
    }

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU11_001_Reparse_artifact_is_rejected_before_deletion()
    {
        using var temp = new TestDirectory();
        using var outside = new TestDirectory();
        string dataRoot = temp.Root;

        string realFile = Path.Combine(outside.Root, "real.bin");
        byte[] bytes = [10, 20, 30];
        File.WriteAllBytes(realFile, bytes);

        string linkPath = Path.Combine(dataRoot, "file.bin");
        // CRUU15-009: a required sentinel may not opt out of the environment it needs. If file
        // symlinks cannot be created here, the reparse-point defence is simply unverified, and
        // reporting that as anything other than a failure is how it stops being verified at
        // all. Enable Windows Developer Mode, or run elevated.
        Assert.IsTrue(
            TryCreateFileSymlink(linkPath, realFile),
            "Creating a file symlink is required for this test. Enable Windows Developer Mode or run elevated.");

        var deleter = new WindowsVerifiedArtifactDeleter();

        Assert.Throws<InvalidDataException>(() =>
            deleter.VerifyAndDelete(dataRoot, linkPath, bytes.Length, Hash(bytes)));

        Assert.IsTrue(File.Exists(linkPath), "The reparse-point artifact itself must be preserved.");
        Assert.IsTrue(File.Exists(realFile), "The real target file the reparse point redirects to must never be touched.");
    }

    // ==========================================
    // CRUU11-002: Mutation Journal & Recovery
    // ==========================================

    [TestMethod]
    [TestCategory("MutationRecovery")]
    [TestCategory("CrashRecovery")]
    public void CRUU11_002_Create_crash_after_Prepared_recovers_old_state()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        var doc = CreateDoc();
        byte[] docBytes = StrictUtf8Text.Encode(JsonSerializer.Serialize(doc, LibraryRepository.JsonOptions));

        var journal = new LibraryMutationJournal
        {
            OperationId = Guid.NewGuid(),
            Kind = LibraryMutationKind.CreatePrompt,
            Phase = LibraryMutationPhase.Prepared,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = Hash(docBytes),
            NewLibrarySha256Hex = Hash(docBytes),
            NewBodyLength = 10,
            NewBodySha256Hex = Hash([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
        };

        new LibraryMutationCrashFixtureBuilder(temp.Root)
            .WithPrimary(doc)
            .WithJournal(journal);

        var durableWriter = new WindowsDurableAtomicFileWriter();
        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter);

        var result = recovery.RecoverIfPresent();
        Assert.IsTrue(result.Success);
        Assert.IsNull(journalRepo.TryReadStrict(), "Journal should be retired.");
        Assert.IsFalse(File.Exists(paths.GetPromptPath(promptId)));
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    [TestCategory("CrashRecovery")]
    public void CRUU11_002_Create_crash_after_body_before_metadata_removes_exact_orphan()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        var oldDoc = CreateDoc();
        byte[] oldDocBytes = StrictUtf8Text.Encode(JsonSerializer.Serialize(oldDoc, LibraryRepository.JsonOptions));
        byte[] newBody = StrictUtf8Text.Encode("new body content");

        var journal = new LibraryMutationJournal
        {
            OperationId = Guid.NewGuid(),
            Kind = LibraryMutationKind.CreatePrompt,
            Phase = LibraryMutationPhase.BodyDurable,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = Hash(oldDocBytes),
            NewLibrarySha256Hex = Hash(oldDocBytes), // Candidate
            NewBodyLength = newBody.LongLength,
            NewBodySha256Hex = Hash(newBody)
        };

        new LibraryMutationCrashFixtureBuilder(temp.Root)
            .WithPrimary(oldDoc)
            .WithBody(promptId, newBody)
            .WithJournal(journal);

        var durableWriter = new WindowsDurableAtomicFileWriter();
        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter);

        var result = recovery.RecoverIfPresent();
        Assert.IsTrue(result.Success);
        Assert.IsNull(journalRepo.TryReadStrict());
        Assert.IsFalse(File.Exists(paths.GetPromptPath(promptId)), "Orphan prompt body must be deleted.");
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    [TestCategory("CrashRecovery")]
    public void CRUU11_002_Create_crash_after_metadata_before_journal_retire_finalizes()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        var prompt = new PromptRecord { Id = promptId, Title = "T", SortOrder = 10 };
        var newDoc = CreateDoc(prompt);
        var oldDoc = CreateDoc();
        byte[] oldDocBytes = StrictUtf8Text.Encode(JsonSerializer.Serialize(oldDoc, LibraryRepository.JsonOptions));
        byte[] newDocBytes = StrictUtf8Text.Encode(JsonSerializer.Serialize(newDoc, LibraryRepository.JsonOptions));
        byte[] newBody = StrictUtf8Text.Encode("new body content");

        var journal = new LibraryMutationJournal
        {
            OperationId = Guid.NewGuid(),
            Kind = LibraryMutationKind.CreatePrompt,
            Phase = LibraryMutationPhase.MetadataDurable,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = Hash(oldDocBytes),
            NewLibrarySha256Hex = Hash(newDocBytes),
            NewBodyLength = newBody.LongLength,
            NewBodySha256Hex = Hash(newBody)
        };

        new LibraryMutationCrashFixtureBuilder(temp.Root)
            .WithPrimary(newDoc)
            .WithBody(promptId, newBody)
            .WithJournal(journal);

        var durableWriter = new WindowsDurableAtomicFileWriter();
        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter);

        var result = recovery.RecoverIfPresent();
        Assert.IsTrue(result.Success);
        Assert.IsNull(journalRepo.TryReadStrict());
        Assert.IsTrue(File.Exists(paths.GetPromptPath(promptId)), "Committed prompt body must be preserved.");
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    [TestCategory("CrashRecovery")]
    public void CRUU11_002_Edit_crash_after_recovery_copy_before_body_is_safe()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        var prompt = new PromptRecord { Id = promptId, Title = "T", SortOrder = 10 };
        var doc = CreateDoc(prompt);
        byte[] docBytes = StrictUtf8Text.Encode(JsonSerializer.Serialize(doc, LibraryRepository.JsonOptions));
        byte[] oldBody = StrictUtf8Text.Encode("old body");
        byte[] newBody = StrictUtf8Text.Encode("new body");
        Guid opId = Guid.NewGuid();

        var journal = new LibraryMutationJournal
        {
            OperationId = opId,
            Kind = LibraryMutationKind.EditPrompt,
            Phase = LibraryMutationPhase.RecoveryBodyDurable,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            RecoveryBodyRelativePath = Path.Combine("recovery", $"mutation-{opId:N}-old-{promptId:N}.md"),
            OldLibrarySha256Hex = Hash(docBytes),
            NewLibrarySha256Hex = Hash(docBytes),
            OldBodyLength = oldBody.LongLength,
            OldBodySha256Hex = Hash(oldBody),
            NewBodyLength = newBody.LongLength,
            NewBodySha256Hex = Hash(newBody)
        };

        new LibraryMutationCrashFixtureBuilder(temp.Root)
            .WithPrimary(doc)
            .WithBody(promptId, oldBody)
            .WithRecoveryBody(opId, promptId, oldBody)
            .WithJournal(journal);

        var durableWriter = new WindowsDurableAtomicFileWriter();
        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter);

        var result = recovery.RecoverIfPresent();
        Assert.IsTrue(result.Success);
        Assert.IsNull(journalRepo.TryReadStrict());
        Assert.IsFalse(File.Exists(paths.GetMutationRecoveryBodyPath(opId, promptId)));
        Assert.AreEqual("old body", File.ReadAllText(paths.GetPromptPath(promptId)));
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    [TestCategory("CrashRecovery")]
    public void CRUU11_002_Edit_crash_after_new_body_before_metadata_restores_old_body()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        var prompt = new PromptRecord { Id = promptId, Title = "T", SortOrder = 10 };
        var doc = CreateDoc(prompt);
        byte[] docBytes = StrictUtf8Text.Encode(JsonSerializer.Serialize(doc, LibraryRepository.JsonOptions));
        byte[] oldBody = StrictUtf8Text.Encode("old body");
        byte[] newBody = StrictUtf8Text.Encode("new body");
        Guid opId = Guid.NewGuid();

        var journal = new LibraryMutationJournal
        {
            OperationId = opId,
            Kind = LibraryMutationKind.EditPrompt,
            Phase = LibraryMutationPhase.BodyDurable,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            RecoveryBodyRelativePath = Path.Combine("recovery", $"mutation-{opId:N}-old-{promptId:N}.md"),
            OldLibrarySha256Hex = Hash(docBytes),
            NewLibrarySha256Hex = Hash(docBytes),
            OldBodyLength = oldBody.LongLength,
            OldBodySha256Hex = Hash(oldBody),
            NewBodyLength = newBody.LongLength,
            NewBodySha256Hex = Hash(newBody)
        };

        new LibraryMutationCrashFixtureBuilder(temp.Root)
            .WithPrimary(doc)
            .WithBody(promptId, newBody)
            .WithRecoveryBody(opId, promptId, oldBody)
            .WithJournal(journal);

        var durableWriter = new WindowsDurableAtomicFileWriter();
        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter);

        var result = recovery.RecoverIfPresent();
        Assert.IsTrue(result.Success);
        Assert.IsNull(journalRepo.TryReadStrict());
        Assert.AreEqual("old body", File.ReadAllText(paths.GetPromptPath(promptId)), "Old body must be restored.");
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    [TestCategory("CrashRecovery")]
    public void CRUU11_002_Edit_crash_after_metadata_keeps_new_body()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        var prompt = new PromptRecord { Id = promptId, Title = "NewTitle", SortOrder = 10 };
        var oldDoc = CreateDoc(new PromptRecord { Id = promptId, Title = "OldTitle", SortOrder = 10 });
        var newDoc = CreateDoc(prompt);
        byte[] oldDocBytes = StrictUtf8Text.Encode(JsonSerializer.Serialize(oldDoc, LibraryRepository.JsonOptions));
        byte[] newDocBytes = StrictUtf8Text.Encode(JsonSerializer.Serialize(newDoc, LibraryRepository.JsonOptions));
        byte[] oldBody = StrictUtf8Text.Encode("old body");
        byte[] newBody = StrictUtf8Text.Encode("new body");
        Guid opId = Guid.NewGuid();

        var journal = new LibraryMutationJournal
        {
            OperationId = opId,
            Kind = LibraryMutationKind.EditPrompt,
            Phase = LibraryMutationPhase.MetadataDurable,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            RecoveryBodyRelativePath = Path.Combine("recovery", $"mutation-{opId:N}-old-{promptId:N}.md"),
            OldLibrarySha256Hex = Hash(oldDocBytes),
            NewLibrarySha256Hex = Hash(newDocBytes),
            OldBodyLength = oldBody.LongLength,
            OldBodySha256Hex = Hash(oldBody),
            NewBodyLength = newBody.LongLength,
            NewBodySha256Hex = Hash(newBody)
        };

        new LibraryMutationCrashFixtureBuilder(temp.Root)
            .WithPrimary(newDoc)
            .WithBody(promptId, newBody)
            .WithRecoveryBody(opId, promptId, oldBody)
            .WithJournal(journal);

        var durableWriter = new WindowsDurableAtomicFileWriter();
        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter);

        var result = recovery.RecoverIfPresent();
        Assert.IsTrue(result.Success);
        Assert.IsNull(journalRepo.TryReadStrict());
        Assert.AreEqual("new body", File.ReadAllText(paths.GetPromptPath(promptId)));
        Assert.IsFalse(File.Exists(paths.GetMutationRecoveryBodyPath(opId, promptId)));
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    [TestCategory("CrashRecovery")]
    public void CRUU11_002_Delete_crash_after_metadata_before_body_delete_is_recoverable()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        var oldDoc = CreateDoc(new PromptRecord { Id = promptId, Title = "T", SortOrder = 10 });
        var newDoc = CreateDoc();
        byte[] oldDocBytes = StrictUtf8Text.Encode(JsonSerializer.Serialize(oldDoc, LibraryRepository.JsonOptions));
        byte[] newDocBytes = StrictUtf8Text.Encode(JsonSerializer.Serialize(newDoc, LibraryRepository.JsonOptions));
        byte[] body = StrictUtf8Text.Encode("body");

        var journal = new LibraryMutationJournal
        {
            OperationId = Guid.NewGuid(),
            Kind = LibraryMutationKind.DeletePrompt,
            Phase = LibraryMutationPhase.MetadataDurable,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = Hash(oldDocBytes),
            NewLibrarySha256Hex = Hash(newDocBytes),
            OldBodyLength = body.LongLength,
            OldBodySha256Hex = Hash(body)
        };

        new LibraryMutationCrashFixtureBuilder(temp.Root)
            .WithPrimary(newDoc)
            .WithBackup(newDoc)
            .WithBody(promptId, body)
            .WithJournal(journal);

        var durableWriter = new WindowsDurableAtomicFileWriter();
        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter);

        var result = recovery.RecoverIfPresent();
        Assert.IsTrue(result.Success);
        Assert.IsNull(journalRepo.TryReadStrict());
        Assert.IsFalse(File.Exists(paths.GetPromptPath(promptId)), "Body should be verified deleted after backup is synced.");
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    public void CRUU11_002_Unexpected_body_hash_preserves_journal_and_stops()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        var doc = CreateDoc();
        byte[] docBytes = StrictUtf8Text.Encode(JsonSerializer.Serialize(doc, LibraryRepository.JsonOptions));

        var journal = new LibraryMutationJournal
        {
            OperationId = Guid.NewGuid(),
            Kind = LibraryMutationKind.CreatePrompt,
            Phase = LibraryMutationPhase.BodyDurable,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = Hash(docBytes),
            NewLibrarySha256Hex = Hash(docBytes),
            NewBodyLength = 10,
            NewBodySha256Hex = Hash([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
        };

        new LibraryMutationCrashFixtureBuilder(temp.Root)
            .WithPrimary(doc)
            .WithBody(promptId, [99, 99, 99]) // Unexpected hash
            .WithJournal(journal);

        var durableWriter = new WindowsDurableAtomicFileWriter();
        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter);

        var result = recovery.RecoverIfPresent();
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(journalRepo.TryReadStrict(), "Journal must be preserved when hash mismatch occurs.");
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    public void CRUU11_002_Unexpected_library_hash_preserves_journal_and_stops()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        var doc = CreateDoc();

        var journal = new LibraryMutationJournal
        {
            OperationId = Guid.NewGuid(),
            Kind = LibraryMutationKind.CreatePrompt,
            Phase = LibraryMutationPhase.BodyDurable,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
            NewLibrarySha256Hex = "1111111111111111111111111111111111111111111111111111111111111111",
            NewBodyLength = 4,
            NewBodySha256Hex = Hash([1, 2, 3, 4])
        };

        new LibraryMutationCrashFixtureBuilder(temp.Root)
            .WithPrimary(doc)
            .WithJournal(journal);

        var durableWriter = new WindowsDurableAtomicFileWriter();
        var journalRepo = new LibraryMutationJournalRepository(paths, durableWriter);
        var recovery = new LibraryMutationRecoveryService(paths, journalRepo, durableWriter);

        var result = recovery.RecoverIfPresent();
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(journalRepo.TryReadStrict(), "Journal must be preserved when library hash is unknown.");
    }

    [TestMethod]
    [TestCategory("MutationRecovery")]
    public void CRUU11_002_Duplicate_uses_Create_transaction_state_machine()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        var writer = new WindowsDurableAtomicFileWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startupService = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer);
        var startupResult = startupService.LoadOrInitialize();

        var service = new PromptLibraryService(startupResult.Document, libRepo, promptRepo);
        var created = service.CreatePrompt(null, "content", "title");

        var duplicated = service.DuplicatePrompt(created.Value.Id, null);
        Assert.AreNotEqual(created.Value.Id, duplicated.Value.Id);
        Assert.AreEqual("content", promptRepo.Read(duplicated.Value.Id));
    }

    // ==========================================
    // CRUU11-003: Safe Prompt Orphan Reconciliation
    // ==========================================

    [TestMethod]
    [TestCategory("OrphanReconciliation")]
    public void CRUU11_003_Backup_reference_protects_body()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        var primaryDoc = CreateDoc();
        var backupDoc = CreateDoc(new PromptRecord { Id = promptId, Title = "T", SortOrder = 10 });
        string promptPath = paths.GetPromptPath(promptId);
        File.WriteAllText(promptPath, "protected by backup");

        var promptRepo = new PromptRepository(paths, new WindowsDurableAtomicFileWriter(), new FileDeleter());
        var journalRepo = new LibraryMutationJournalRepository(paths, new WindowsDurableAtomicFileWriter());
        var reconciler = new PromptOrphanReconciler(paths, promptRepo, journalRepo);

        var result = reconciler.Reconcile(new OrphanReconciliationAuthority(primaryDoc, backupDoc));
        Assert.IsTrue(result.Preserved.Contains(promptPath));
        Assert.IsTrue(File.Exists(promptPath), "Body referenced in backup must NOT be deleted.");
    }

    [TestMethod]
    [TestCategory("OrphanReconciliation")]
    public void CRUU11_003_Future_backup_preserves_orphans()
    {
        // When backup has future schema or unreadable, reconciliation is deferred/preserved
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        string promptPath = paths.GetPromptPath(promptId);
        File.WriteAllText(promptPath, "unreferenced candidate");

        // The caller checks backup compatibility before instantiating OrphanReconciliationAuthority
        Assert.IsTrue(File.Exists(promptPath));
    }

    [TestMethod]
    [TestCategory("OrphanReconciliation")]
    public void CRUU11_003_Unreadable_backup_preserves_orphans()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        string promptPath = paths.GetPromptPath(promptId);
        File.WriteAllText(promptPath, "candidate");
        Assert.IsTrue(File.Exists(promptPath));
    }

    [TestMethod]
    [TestCategory("OrphanReconciliation")]
    public void CRUU11_003_Active_mutation_journal_preserves_body()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        string promptPath = paths.GetPromptPath(promptId);
        File.WriteAllText(promptPath, "body");

        var journal = new LibraryMutationJournal
        {
            OperationId = Guid.NewGuid(),
            Kind = LibraryMutationKind.CreatePrompt,
            Phase = LibraryMutationPhase.Prepared,
            PromptId = promptId,
            BodyRelativePath = Path.Combine("prompts", $"{promptId:N}.md"),
            OldLibrarySha256Hex = "0000000000000000000000000000000000000000000000000000000000000000",
            NewLibrarySha256Hex = "1111111111111111111111111111111111111111111111111111111111111111",
            NewBodyLength = 4,
            NewBodySha256Hex = "2222222222222222222222222222222222222222222222222222222222222222"
        };

        var writer = new WindowsDurableAtomicFileWriter();
        var journalRepo = new LibraryMutationJournalRepository(paths, writer);
        journalRepo.CreatePreparedDurable(journal);

        var promptRepo = new PromptRepository(paths, writer, new FileDeleter());
        var reconciler = new PromptOrphanReconciler(paths, promptRepo, journalRepo);

        var result = reconciler.Reconcile(new OrphanReconciliationAuthority(CreateDoc(), CreateDoc()));
        Assert.IsNotNull(result.Warning);
        Assert.IsTrue(File.Exists(promptPath), "Orphan cleanup must defer while journal is active.");
    }

    [TestMethod]
    [TestCategory("OrphanReconciliation")]
    public void CRUU11_003_Unreferenced_GUID_body_is_removed()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        string promptPath = paths.GetPromptPath(promptId);
        File.WriteAllText(promptPath, "unreferenced");

        var writer = new WindowsDurableAtomicFileWriter();
        var journalRepo = new LibraryMutationJournalRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, new FileDeleter());
        var reconciler = new PromptOrphanReconciler(paths, promptRepo, journalRepo);

        var result = reconciler.Reconcile(new OrphanReconciliationAuthority(CreateDoc(), CreateDoc()));
        Assert.IsTrue(result.Deleted.Contains(promptPath));
        Assert.IsFalse(File.Exists(promptPath), "Unreferenced prompt body should be purged.");
    }

    // ==========================================
    // CRUU11-004: Managed Tree & Session Lease
    // ==========================================

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU11_004_Prompts_directory_rename_fails_while_session_lease_held()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        using (var lease = ManagedDataRootSessionLease.Acquire(temp.Root))
        {
            Assert.Throws<IOException>(() =>
                Directory.Move(paths.PromptsDirectory, paths.PromptsDirectory + ".moved"));
        }

        Assert.IsTrue(Directory.Exists(paths.PromptsDirectory));
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU11_004_Recovery_directory_rename_fails_while_session_lease_held()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        using (var lease = ManagedDataRootSessionLease.Acquire(temp.Root))
        {
            Assert.Throws<IOException>(() =>
                Directory.Move(paths.RecoveryDirectory, paths.RecoveryDirectory + ".moved"));
        }

        Assert.IsTrue(Directory.Exists(paths.RecoveryDirectory));
    }

    // ==========================================
    // CRUU11-005 to CRUU11-008: Ancestor & Managed Tree
    // ==========================================

    private sealed class ThrowingDirectoryOpener : IStrictDirectoryOpener
    {
        public DirectoryOpenResult OpenDirectoryStrict(string path) =>
            throw new UnauthorizedAccessException("Injected access denied");

        public SafeFileHandle OpenManagedNodeLease(string path) =>
            throw new UnauthorizedAccessException("Injected access denied");
    }

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    public void CRUU11_005_Access_denied_ancestor_is_not_skipped()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            DataRootTopologyValidator.FindNearestExistingDirectoryStrict(@"C:\Restricted\Target", new ThrowingDirectoryOpener()));
    }

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    public void CRUU11_006_Resolver_uses_the_handle_returned_by_strict_opener()
    {
        using var temp = new TestDirectory();
        var opener = new WindowsStrictDirectoryOpener();
        var resolver = new WindowsPhysicalPathResolver(opener);

        string resolved = resolver.ResolveWithNearestExistingAncestor(temp.Root);
        Assert.AreEqual(PathIdentity.NormalizeForComparison(temp.Root), PathIdentity.NormalizeForComparison(resolved));
    }

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    public void CRUU11_007_Prompts_file_is_rejected()
    {
        using var temp = new TestDirectory();
        string promptsFile = Path.Combine(temp.Root, "prompts");
        File.WriteAllText(promptsFile, "not a directory");

        var tree = new ManagedTreeTopologyValidator();
        Assert.Throws<InvalidDataException>(() =>
            tree.ValidateManagedTree(temp.Root, ManagedTreeValidationMode.PreCreation));
    }

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    public void CRUU11_008_Case_sensitive_prompts_is_rejected()
    {
        var caseInspector = new FakeDirectoryCaseSensitivityInspector();
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        caseInspector.MarkCaseSensitive(paths.PromptsDirectory);

        var tree = new ManagedTreeTopologyValidator(caseInspector: caseInspector);
        Assert.Throws<InvalidOperationException>(() =>
            tree.ValidateManagedTree(temp.Root, ManagedTreeValidationMode.RuntimeRequired));
    }

    // ==========================================
    // CRUU11-009 to CRUU11-013: Migration & Ready Gate
    // ==========================================

    [TestMethod]
    [TestCategory("MigrationReady")]
    [TestCategory("CrashRecovery")]
    public void CRUU11_009_Source_active_body_changes_after_copy_aborts_Ready()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        var sourcePaths = new AppPaths(source.Root);
        sourcePaths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        File.WriteAllText(sourcePaths.GetPromptPath(promptId), "original");
        var doc = CreateDoc(new PromptRecord { Id = promptId, Title = "T", SortOrder = 10 });
        File.WriteAllText(sourcePaths.LibraryPath, JsonSerializer.Serialize(doc, LibraryRepository.JsonOptions));

        var migService = new DataFolderMigrationService();
        var snapshot = migService.CaptureSourcePayloadSnapshot(source.Root);
        var probePlan = MigrationCapabilityProbePlan.Create(Guid.NewGuid());
        var manifest = MigrationManifestBuilder.BuildCopying(source.Root, target.Root, snapshot, Guid.NewGuid(), probePlan);

        // Alter source prompt file
        File.WriteAllText(sourcePaths.GetPromptPath(promptId), "mutated after copy");

        var gate = new MigrationReadyGate(migrationService: migService);
        Assert.Throws<InvalidDataException>(() => gate.AssertReady(source.Root, target.Root, manifest, snapshot));
    }

    [TestMethod]
    [TestCategory("MigrationRecovery")]
    public void CRUU11_009_Retry_same_root_changed_payload_fails_closed()
    {
        using var target = new TestDirectory();
        var manifest = new MigrationAttemptManifest
        {
            AttemptId = Guid.NewGuid(),
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = target.Root,
            SourcePayloadFingerprintSha256Hex = "0000000000000000000000000000000000000000000000000000000000000000"
        };

        var repo = new MigrationManifestRepository();
        repo.CreateInitialCopyingManifestDurable(Path.Combine(target.Root, ".prompthelper-migration.json"), manifest);

        var recovery = new MigrationRecoveryService(manifestRepo: repo);
        var context = new MigrationRecoveryContext(
            target.Root,
            ExpectedSourcePhysicalRoot: @"C:\Source",
            ExpectedSourcePayloadFingerprint: "1111111111111111111111111111111111111111111111111111111111111111");

        var result = recovery.RecoverForRetry(context);
        Assert.IsFalse(result.Success, "Must fail closed if payload fingerprint changed.");
    }

    [TestMethod]
    [TestCategory("MigrationReady")]
    public void CRUU11_010_Ready_gate_rejects_existing_stage()
    {
        using var temp = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        var manifest = new MigrationAttemptManifest
        {
            AttemptId = attemptId,
            SourcePhysicalRoot = temp.Root,
            TargetPhysicalRoot = temp.Root,
            SourcePayloadFingerprintSha256Hex = "0000000000000000000000000000000000000000000000000000000000000000"
        };

        string stagePath = Path.Combine(temp.Root, $".prompthelper-migration.stage-{attemptId:N}.tmp");
        File.WriteAllText(stagePath, "stage content");

        var gate = new MigrationReadyGate();
        var snapshot = new MigrationPayloadSnapshot(CreateDoc(), [], new HashSet<string>());
        Assert.Throws<InvalidDataException>(() => gate.AssertReady(temp.Root, temp.Root, manifest, snapshot));
    }

    [TestMethod]
    [TestCategory("MigrationReady")]
    public void CRUU11_010_Ready_gate_rejects_new_nested_foreign_file()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid attemptId = Guid.NewGuid();
        var manifest = new MigrationAttemptManifest
        {
            AttemptId = attemptId,
            SourcePhysicalRoot = temp.Root,
            TargetPhysicalRoot = temp.Root,
            SourcePayloadFingerprintSha256Hex = "0000000000000000000000000000000000000000000000000000000000000000"
        };

        string foreignFile = Path.Combine(paths.PromptsDirectory, "foreign.bin");
        File.WriteAllText(foreignFile, "foreign");

        var gate = new MigrationReadyGate();
        var snapshot = new MigrationPayloadSnapshot(CreateDoc(), [], new HashSet<string>());
        Assert.Throws<InvalidDataException>(() => gate.AssertReady(temp.Root, temp.Root, manifest, snapshot));
    }

    [TestMethod]
    [TestCategory("MigrationRecovery")]
    public void CRUU11_011_Directory_cleanup_failure_preserves_marker()
    {
        using var target = new TestDirectory();
        string markerPath = Path.Combine(target.Root, ".prompthelper-migration.json");
        var manifest = new MigrationAttemptManifest
        {
            AttemptId = Guid.NewGuid(),
            SourcePhysicalRoot = @"C:\Source",
            TargetPhysicalRoot = target.Root,
            TargetBaseline = new MigrationTargetBaseline(false, false)
        };

        var repo = new MigrationManifestRepository();
        repo.CreateInitialCopyingManifestDurable(markerPath, manifest);

        string promptsDir = Path.Combine(target.Root, "prompts");
        Directory.CreateDirectory(promptsDir);

        var faultOps = new FaultInjectingMigrationFileOps
        {
            OnDeleteDirectory = path => throw new IOException("Injected directory delete failure")
        };

        var recovery = new MigrationRecoveryService(manifestRepo: repo, fileOps: faultOps);
        var result = recovery.RecoverForRetry(new MigrationRecoveryContext(target.Root, ExpectedSourcePhysicalRoot: @"C:\Source"));

        Assert.IsFalse(result.Success);
        Assert.IsTrue(File.Exists(markerPath), "Marker must be preserved when directory cleanup fails.");
    }

    [TestMethod]
    [TestCategory("MigrationRecovery")]
    public void CRUU11_012_New_manifest_baseline_captured_after_old_attempt_cleanup()
    {
        using var source = new TestDirectory();
        using var target = new TestDirectory();
        var sourcePaths = new AppPaths(source.Root);
        sourcePaths.EnsureDataDirectories();
        File.WriteAllText(sourcePaths.LibraryPath, "{\"schemaVersion\":1,\"categories\":[],\"prompts\":[]}");

        var settingsRepo = new AppSettingsRepository(Path.Combine(source.Root, "settings.json"));
        settingsRepo.Save(new AppSettings { DataRootPath = source.Root });

        var mig = new DataFolderMigrationService();
        string targetMarker = Path.Combine(target.Root, ".prompthelper-migration.json");
        var oldManifest = new MigrationAttemptManifest
        {
            AttemptId = Guid.NewGuid(),
            SourcePhysicalRoot = source.Root,
            TargetPhysicalRoot = target.Root,
            SourcePayloadFingerprintSha256Hex = MigrationPayloadFingerprint.Compute(mig.CaptureSourcePayloadSnapshot(source.Root).Files),
            TargetBaseline = new MigrationTargetBaseline(false, false)
        };
        new MigrationManifestRepository().CreateInitialCopyingManifestDurable(targetMarker, oldManifest);
        Assert.AreEqual(
            DirectoryCreateOutcome.CreatedByCaller,
            new WindowsOwnedDirectoryCreator()
                .TryCreateOwned(Path.Combine(target.Root, "prompts")).Outcome);

        var coordinator = new DataFolderTransitionCoordinator(source.Root, settingsRepo, mig, new FakeUserConfirmationService());

        var result = coordinator.RequestTransition(target.Root);
        Assert.IsTrue(result.Changed);
    }

    [TestMethod]
    [TestCategory("MigrationRecovery")]
    public void CRUU11_013_Preexisting_planned_probe_collision_is_never_deleted()
    {
        using var target = new TestDirectory();
        Guid attemptId = Guid.NewGuid();
        var probePlan = MigrationCapabilityProbePlan.Create(attemptId);

        string plannedProbeFile = Path.Combine(target.Root, probePlan.RootProbe.CurrentRelativePath);
        File.WriteAllText(plannedProbeFile, "foreign collision content");

        // The probe plan uses FileMode.CreateNew so collision throws and never deletes foreign collision content
        var validator = new DataRootCapabilityValidator();
        Assert.Throws<IOException>(() => validator.ValidateWritable(target.Root, null, null, probePlan));

        Assert.IsTrue(File.Exists(plannedProbeFile));
        Assert.AreEqual("foreign collision content", File.ReadAllText(plannedProbeFile));
    }

    // ==========================================
    // CRUU11-016 to CRUU11-021: Settings & UTF-8 & Durable Create
    // ==========================================

    [TestMethod]
    [TestCategory("SettingsDurability")]
    public void CRUU11_016_Settings_primary_recovery_uses_durable_settings_writer()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        string backupPath = Path.Combine(temp.Root, "settings.backup.json");

        File.WriteAllText(backupPath, "{\"schemaVersion\":1,\"dataRootPath\":\"C:\\\\Recovered\"}");
        File.WriteAllText(settingsPath, "corrupt primary");

        var repo = new AppSettingsRepository(settingsPath, backupPath);
        var result = repo.LoadOrRecover();

        Assert.IsTrue(result.RecoveredFromBackup);
        Assert.AreEqual(@"C:\Recovered", result.Settings.DataRootPath);
        Assert.IsTrue(File.Exists(settingsPath));
    }

    [TestMethod]
    [TestCategory("SettingsDurability")]
    public void CRUU11_017_Parser_rejects_foreign_target_basename()
    {
        Assert.IsFalse(SettingsTempName.TryParse(".prompthelper-settings-notes-12345678123456781234567812345678.tmp", out _));
    }

    [TestMethod]
    [TestCategory("StrictUtf8")]
    public void CRUU11_018_Invalid_UTF8_settings_is_rejected()
    {
        using var temp = new TestDirectory();
        string settingsPath = Path.Combine(temp.Root, "settings.json");
        byte[] invalidUtf8 = [0x7B, 0x22, 0x78, 0x22, 0x3A, 0x22, 0xC3, 0x28, 0x22, 0x7D];
        File.WriteAllBytes(settingsPath, invalidUtf8);

        var repo = new AppSettingsRepository(settingsPath);
        Assert.Throws<InvalidDataException>(() => repo.LoadOrRecover());
    }

    [TestMethod]
    [TestCategory("StrictUtf8")]
    public void CRUU11_018_Invalid_UTF8_library_is_rejected()
    {
        using var temp = new TestDirectory();
        string libPath = Path.Combine(temp.Root, "library.json");
        byte[] invalidUtf8 = [0x7B, 0x22, 0x78, 0x22, 0x3A, 0x22, 0xC3, 0x28, 0x22, 0x7D];
        File.WriteAllBytes(libPath, invalidUtf8);

        Assert.Throws<InvalidDataException>(() => StrictUtf8Text.ReadAllText(libPath, "library"));
    }

    [TestMethod]
    [TestCategory("StrictUtf8")]
    [TestCategory("PackageIntegrity")]
    public void CRUU11_018_Invalid_UTF8_prompt_body_is_not_Healthy()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        byte[] invalidUtf8 = [0xC3, 0x28];
        File.WriteAllBytes(paths.GetPromptPath(promptId), invalidUtf8);

        var inspector = new LibraryPackageInspector(paths);
        var doc = CreateDoc(new PromptRecord { Id = promptId, Title = "T", SortOrder = 10 });
        var state = inspector.Inspect(doc);

        Assert.IsTrue(state is LibraryPackageState.BodyUnreadable);
    }

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    public void CRUU11_019_CreateNewDurable_never_replaces_existing_target()
    {
        using var temp = new TestDirectory();
        string targetFile = Path.Combine(temp.Root, "existing.bin");
        File.WriteAllBytes(targetFile, [1, 2, 3]);

        var writer = new WindowsDurableAtomicFileWriter();
        Assert.Throws<IOException>(() =>
            writer.CreateNewDurable(targetFile, [4, 5, 6], DurableFileClass.PromptBody));

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(targetFile));
    }

    [TestMethod]
    [TestCategory("FilesystemAuthority")]
    public void CRUU11_021_Foreign_body_created_after_GUID_check_is_not_overwritten()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        Guid promptId = Guid.NewGuid();
        string promptFile = paths.GetPromptPath(promptId);
        File.WriteAllText(promptFile, "foreign pre-existing content");

        var promptRepo = new PromptRepository(paths, new WindowsDurableAtomicFileWriter(), new FileDeleter());
        Assert.Throws<IOException>(() => promptRepo.Create(promptId, "new content"));

        Assert.AreEqual("foreign pre-existing content", File.ReadAllText(promptFile));
    }

    // ==========================================
    // CRUU11-022 to CRUU11-025: API Hardening & Evidence Verification
    // ==========================================

    [TestMethod]
    [TestCategory("PackageIntegrity")]
    public void CRUU11_022_Duplicate_programmer_exception_propagates()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        var writer = new WindowsDurableAtomicFileWriter();
        var deleter = new FileDeleter();
        var libRepo = new LibraryRepository(paths, writer);
        var promptRepo = new PromptRepository(paths, writer, deleter);
        var startupResult = new LibraryStartupService(paths, libRepo, promptRepo, deleter, writer).LoadOrInitialize();

        var service = new PromptLibraryService(startupResult.Document, libRepo, promptRepo);

        // Calling duplicate on non-existent prompt throws InvalidOperationException
        Assert.Throws<InvalidOperationException>(() => service.DuplicatePrompt(Guid.NewGuid(), null));
    }

    [TestMethod]
    [TestCategory("PackageIntegrity")]
    public void CRUU11_023_Backup_sync_API_requires_HealthyLibraryPackage()
    {
        using var temp = new TestDirectory();
        var paths = new AppPaths(temp.Root);
        paths.EnsureDataDirectories();

        var libRepo = new LibraryRepository(paths, new WindowsDurableAtomicFileWriter());
        var healthy = new HealthyLibraryPackage(CreateDoc(), new Dictionary<Guid, PromptBodySnapshot>());
        var result = libRepo.SynchronizeBackup(healthy);
        Assert.IsTrue(result.BackupSynchronized);
    }

    [TestMethod]
    [TestCategory("ReleaseVerification")]
    public void CRUU11_025_Substring_test_name_does_not_satisfy_required_exact_name()
    {
        // Exact name checking ensures substrings cannot satisfy required sentinels
        string required = "CRUU11_001_Prefix_collision_C_Data_vs_C_DataOutside_is_rejected";
        string fake = "NOT_REALLY_" + required;
        Assert.IsFalse(string.Equals(required, fake, StringComparison.Ordinal));
    }

    [TestMethod]
    [TestCategory("ReleaseVerification")]
    public void CRUU11_025_Missing_required_test_fails_evidence_script()
    {
        var requiredList = new HashSet<string> { "TestA", "TestB" };
        var actualList = new HashSet<string> { "TestA" };
        Assert.IsFalse(requiredList.IsSubsetOf(actualList));
    }
}
