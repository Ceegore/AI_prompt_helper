using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Platform.Linux.Tests;

[TestClass]
[DoNotParallelize]
public sealed class LinuxPromptLibraryStoreCoverageTests
{
    [TestMethod]
    public void Missing_primary_is_restored_from_valid_backup()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var initial = new LinuxPromptLibraryStore(root);
            initial.Initialize();

            string primary = Path.Combine(root, "library.json");
            string backup = Path.Combine(root, "library.backup.json");
            byte[] expected = File.ReadAllBytes(backup);
            File.Delete(primary);

            var reopened = new LinuxPromptLibraryStore(root);
            reopened.Initialize();

            CollectionAssert.AreEqual(expected, File.ReadAllBytes(primary));
            Assert.IsTrue(reopened.StartupWarnings.Any(w =>
                w.Contains("restored", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void Corrupt_primary_without_backup_fails_closed()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var initial = new LinuxPromptLibraryStore(root);
            initial.Initialize();

            File.Delete(Path.Combine(root, "library.backup.json"));
            File.WriteAllText(Path.Combine(root, "library.json"), "{ broken");

            var reopened = new LinuxPromptLibraryStore(root);
            InvalidDataException ex =
                Assert.Throws<InvalidDataException>(() => reopened.Initialize());

            StringAssert.Contains(ex.Message, "no backup");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void Future_primary_schema_fails_closed_and_is_preserved()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var initial = new LinuxPromptLibraryStore(root);
            initial.Initialize();

            string primary = Path.Combine(root, "library.json");
            byte[] future = System.Text.Encoding.UTF8.GetBytes(
                """
                {
                  "schemaVersion": 999,
                  "premadePackVersion": 0,
                  "categories": [],
                  "prompts": []
                }
                """);
            File.WriteAllBytes(primary, future);

            var reopened = new LinuxPromptLibraryStore(root);
            InvalidDataException ex =
                Assert.Throws<InvalidDataException>(() => reopened.Initialize());

            StringAssert.Contains(ex.Message, "newer schema version 999");
            CollectionAssert.AreEqual(future, File.ReadAllBytes(primary));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void Future_backup_is_preserved_and_orphan_body_is_reported()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();

            string backup = Path.Combine(root, "library.backup.json");
            byte[] future = System.Text.Encoding.UTF8.GetBytes(
                """
                {
                  "schemaVersion": 999,
                  "premadePackVersion": 0,
                  "categories": [],
                  "prompts": []
                }
                """);
            File.WriteAllBytes(backup, future);

            OperationResult<CategoryRecord> created =
                store.CreateCategory(null, "Backup warning");

            Assert.IsNotNull(created.Warning);
            StringAssert.Contains(created.Warning, "newer schema");
            CollectionAssert.AreEqual(future, File.ReadAllBytes(backup));

            string orphan = Path.Combine(
                root,
                "prompts",
                Guid.NewGuid().ToString("N") + ".md");
            File.WriteAllText(orphan, "preserve me");

            var reopened = new LinuxPromptLibraryStore(root);
            reopened.Initialize();

            Assert.IsTrue(reopened.StartupWarnings.Any(w =>
                w.Contains("unreferenced prompt body", StringComparison.OrdinalIgnoreCase)));
            Assert.IsTrue(File.Exists(orphan));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void Fresh_initialization_accepts_exact_existing_default_body()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            DefaultLibraryPackage defaults = DefaultLibraryFactory.CreateDefaults();
            KeyValuePair<Guid, string> existing = defaults.PromptContents.First();

            string prompts = Path.Combine(root, "prompts");
            Directory.CreateDirectory(prompts);
            File.WriteAllBytes(
                Path.Combine(prompts, existing.Key.ToString("N") + ".md"),
                StrictUtf8Text.Encode(existing.Value));

            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();

            Assert.IsTrue(File.Exists(Path.Combine(root, "library.json")));
            Assert.IsTrue(store.CurrentDocument.Prompts.Any(p => p.Id == existing.Key));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void Fresh_initialization_refuses_modified_default_body()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            DefaultLibraryPackage defaults = DefaultLibraryFactory.CreateDefaults();
            Guid id = defaults.PromptContents.Keys.First();

            string prompts = Path.Combine(root, "prompts");
            Directory.CreateDirectory(prompts);
            File.WriteAllText(
                Path.Combine(prompts, id.ToString("N") + ".md"),
                "foreign content");

            var store = new LinuxPromptLibraryStore(root);
            InvalidDataException ex =
                Assert.Throws<InvalidDataException>(() => store.Initialize());

            StringAssert.Contains(ex.Message, "unexpected content");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void Crud_validation_preview_noop_and_title_only_paths_are_fail_closed()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();

            Assert.AreEqual("Home", store.GetBreadcrumbs(null).Single().Name);

            CategoryRecord a = store.CreateCategory(null, "A").Value;
            CategoryRecord b = store.CreateCategory(null, "B").Value;
            CategoryRecord child = store.CreateCategory(a.Id, " Child ").Value;
            Assert.AreEqual("Child", child.Name);

            Assert.Throws<InvalidOperationException>(() =>
                store.CreateCategory(Guid.NewGuid(), "Missing parent"));
            Assert.Throws<InvalidOperationException>(() =>
                store.CreateCategory(null, "a"));
            Assert.Throws<InvalidOperationException>(() =>
                store.RenameCategory(Guid.NewGuid(), "Missing"));
            Assert.Throws<InvalidOperationException>(() =>
                store.RenameCategory(b.Id, "A"));
            Assert.Throws<InvalidOperationException>(() =>
                store.DeleteCategory(Guid.NewGuid()));

            Assert.Throws<InvalidOperationException>(() =>
                store.CreatePrompt(Guid.NewGuid(), "body", "title"));

            PromptRecord prompt =
                store.CreatePrompt(a.Id, "abcdefghij", "Title").Value;

            Assert.AreEqual("abcde…", store.ReadPromptPreview(prompt.Id, 5));
            Assert.AreEqual("abcdefghij", store.ReadPromptPreview(prompt.Id, 50));
            Assert.AreEqual(1, store.GetPrompts(a.Id).Count);

            OperationResult noop =
                store.EditPrompt(prompt.Id, "abcdefghij", "Title");
            Assert.IsNull(noop.Warning);

            store.EditPrompt(prompt.Id, "changed body", "Title");
            Assert.AreEqual("changed body", store.ReadPrompt(prompt.Id));

            store.EditPrompt(prompt.Id, "changed body", "New title");
            Assert.AreEqual(
                "New title",
                store.CurrentDocument.Prompts.Single(p => p.Id == prompt.Id).Title);

            Assert.Throws<InvalidOperationException>(() =>
                store.EditPrompt(prompt.Id, "changed body", "bad\ntitle"));
            Assert.Throws<InvalidOperationException>(() =>
                store.EditPrompt(Guid.NewGuid(), "body", "title"));
            Assert.Throws<InvalidOperationException>(() =>
                store.DeletePrompt(Guid.NewGuid()));
            Assert.Throws<InvalidOperationException>(() =>
                store.MovePrompt(prompt.Id, Guid.NewGuid()));
            Assert.Throws<InvalidOperationException>(() =>
                store.MovePrompt(Guid.NewGuid(), null));
            Assert.Throws<InvalidOperationException>(() =>
                store.DuplicatePrompt(prompt.Id, Guid.NewGuid()));
            Assert.Throws<InvalidOperationException>(() =>
                store.DuplicatePrompt(Guid.NewGuid(), null));

            store.MovePrompt(prompt.Id, child.Id);
            Assert.AreEqual(child.Id, store.GetPrompts(child.Id).Single().CategoryId);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void Create_prompt_commit_conflict_retires_new_body()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();

            string[] before = Directory.GetFiles(Path.Combine(root, "prompts"), "*.md");

            LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests = target =>
            {
                if (Path.GetFileName(target) == "library.json")
                {
                    LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests = null;
                    File.AppendAllText(target, " ");
                }
            };

            Assert.Throws<InvalidOperationException>(() =>
                store.CreatePrompt(null, "new body", "new title"));

            string[] after = Directory.GetFiles(Path.Combine(root, "prompts"), "*.md");
            CollectionAssert.AreEquivalent(before, after);
        }
        finally
        {
            ResetHooks();
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void Duplicate_prompt_commit_conflict_retires_duplicate_body()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();
            PromptRecord source =
                store.CreatePrompt(null, "source body", "source").Value;

            string[] before = Directory.GetFiles(Path.Combine(root, "prompts"), "*.md");

            LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests = target =>
            {
                if (Path.GetFileName(target) == "library.json")
                {
                    LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests = null;
                    File.AppendAllText(target, " ");
                }
            };

            Assert.Throws<InvalidOperationException>(() =>
                store.DuplicatePrompt(source.Id, null));

            string[] after = Directory.GetFiles(Path.Combine(root, "prompts"), "*.md");
            CollectionAssert.AreEquivalent(before, after);
            Assert.AreEqual("source body", File.ReadAllText(
                Path.Combine(root, "prompts", source.Id.ToString("N") + ".md")));
        }
        finally
        {
            ResetHooks();
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void Edit_prompt_metadata_conflict_rolls_back_body()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();
            PromptRecord prompt =
                store.CreatePrompt(null, "old body", "old title").Value;

            LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests = target =>
            {
                if (Path.GetFileName(target) == "library.json")
                {
                    LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests = null;
                    File.AppendAllText(target, " ");
                }
            };

            Assert.Throws<InvalidOperationException>(() =>
                store.EditPrompt(prompt.Id, "new body", "new title"));

            Assert.AreEqual("old body", store.ReadPrompt(prompt.Id));
            Assert.AreEqual(
                "old title",
                store.CurrentDocument.Prompts.Single(p => p.Id == prompt.Id).Title);
            Assert.IsTrue(
                File.ReadAllText(Path.Combine(root, "library.json"))
                    .EndsWith(" ", StringComparison.Ordinal));
        }
        finally
        {
            ResetHooks();
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void Delete_prompt_preserves_concurrent_foreign_body_and_returns_warning()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();
            PromptRecord prompt =
                store.CreatePrompt(null, "old body", "title").Value;
            string bodyPath =
                Path.Combine(root, "prompts", prompt.Id.ToString("N") + ".md");
            byte[] foreign = System.Text.Encoding.UTF8.GetBytes("foreign body");

            LinuxExactFileRetirement.BeforeQuarantineRenameForTests = path =>
            {
                if (Path.GetFullPath(path) == Path.GetFullPath(bodyPath))
                {
                    LinuxExactFileRetirement.BeforeQuarantineRenameForTests = null;
                    AtomicExternalReplace(path, foreign);
                }
            };

            OperationResult deleted = store.DeletePrompt(prompt.Id);

            Assert.IsNotNull(deleted.Warning);
            StringAssert.Contains(deleted.Warning, "changed concurrently");
            CollectionAssert.AreEqual(foreign, File.ReadAllBytes(bodyPath));
            Assert.IsFalse(store.CurrentDocument.Prompts.Any(p => p.Id == prompt.Id));
        }
        finally
        {
            ResetHooks();
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void Backup_synchronization_failure_is_returned_as_warning_without_losing_primary_commit()
    {
        if (!OperatingSystem.IsLinux()) return;

        string root = CreateRoot();
        try
        {
            var store = new LinuxPromptLibraryStore(root);
            store.Initialize();

            string backup = Path.Combine(root, "library.backup.json");
            File.Delete(backup);
            Directory.CreateDirectory(backup);

            OperationResult<CategoryRecord> result =
                store.CreateCategory(null, "Primary still commits");

            Assert.IsNotNull(result.Warning);
            StringAssert.Contains(result.Warning, "backup could not be synchronized");
            Assert.IsTrue(
                store.CurrentDocument.Categories.Any(c => c.Id == result.Value.Id));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static void AtomicExternalReplace(string target, byte[] bytes)
    {
        string scratch = Path.Combine(
            Path.GetDirectoryName(target)!,
            "external-" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(scratch, bytes);
        File.Move(scratch, target, overwrite: true);
    }

    private static void ResetHooks()
    {
        LinuxAtomicExpectedFileReplacer.PreSwapBarrierForTests = null;
        LinuxExactFileRetirement.BeforeQuarantineRenameForTests = null;
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-LinuxStoreCoverage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        ResetHooks();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
