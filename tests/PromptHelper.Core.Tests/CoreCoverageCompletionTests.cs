using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Infrastructure;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Core.Tests;

[TestClass]
public sealed class CoreCoverageCompletionTests
{
    private sealed class ObservableProbe : ObservableObject
    {
        private string _value = "initial";

        public string Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        public void RaiseExplicit(string propertyName) =>
            OnPropertyChanged(propertyName);
    }

    [TestMethod]
    public void TextUtilities_cover_unicode_truncation_preview_and_forbidden_characters()
    {
        Assert.AreEqual(2, TextUtilities.GetTextElementCount("e\u0301😀"));
        Assert.AreEqual("short", TextUtilities.TruncateWithEllipsis("short", 5));
        Assert.AreEqual("ab…", TextUtilities.TruncateWithEllipsis("abcdef", 3));
        Assert.AreEqual(
            "one two three",
            TextUtilities.CreateCompactPreview("  one\r\n\t two   three  ", 50));
        Assert.AreEqual(string.Empty, TextUtilities.CreateCompactPreview(null));
        Assert.AreEqual(string.Empty, TextUtilities.CreateCompactPreview(" \r\n "));
        Assert.IsFalse(TextUtilities.ContainsForbiddenSingleLineCharacter("normal 😀"));
        Assert.IsTrue(TextUtilities.ContainsForbiddenSingleLineCharacter("line\nfeed"));
        Assert.IsTrue(TextUtilities.ContainsForbiddenSingleLineCharacter("tab\there"));
        Assert.IsTrue(TextUtilities.ContainsForbiddenSingleLineCharacter("separator\u2028here"));
        Assert.IsTrue(TextUtilities.ContainsForbiddenSingleLineCharacter("paragraph\u2029here"));

        Assert.Throws<ArgumentNullException>(() =>
            TextUtilities.GetTextElementCount(null!));
        Assert.Throws<ArgumentNullException>(() =>
            TextUtilities.TruncateWithEllipsis(null!, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TextUtilities.TruncateWithEllipsis("abc", 1));
        Assert.Throws<ArgumentNullException>(() =>
            TextUtilities.ContainsForbiddenSingleLineCharacter(null!));
    }

    [TestMethod]
    public void ObservableObject_only_notifies_when_value_changes()
    {
        var probe = new ObservableProbe();
        var names = new List<string?>();

        probe.PropertyChanged += (_, e) => names.Add(e.PropertyName);

        probe.Value = "initial";
        Assert.AreEqual(0, names.Count);

        probe.Value = "changed";
        CollectionAssert.AreEqual(new[] { "Value" }, names);

        probe.RaiseExplicit("Manual");
        CollectionAssert.AreEqual(new[] { "Value", "Manual" }, names);
    }

    [TestMethod]
    public void AppPaths_exposes_all_managed_paths_and_creates_directories()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "PromptHelper-CorePaths-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);
        Guid promptId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();

        try
        {
            Assert.AreEqual(root, paths.RootDirectory);
            Assert.AreEqual(Path.Combine(root, ".app.lock"), paths.LockPath);
            Assert.AreEqual(Path.Combine(root, "initializing.marker"), paths.InitializationMarkerPath);
            Assert.AreEqual(Path.Combine(root, ".prompthelper-migration.json"), paths.MigrationMarkerPath);
            Assert.AreEqual(Path.Combine(root, ".prompthelper-library-mutation.json"), paths.LibraryMutationJournalPath);
            Assert.AreEqual(Path.Combine(root, "library.json"), paths.LibraryPath);
            Assert.AreEqual(Path.Combine(root, "library.backup.json"), paths.LibraryBackupPath);
            Assert.AreEqual(Path.Combine(root, "prompts"), paths.PromptsDirectory);
            Assert.AreEqual(Path.Combine(root, "recovery"), paths.RecoveryDirectory);
            Assert.AreEqual(
                Path.Combine(root, "prompts", promptId.ToString("N") + ".md"),
                paths.GetPromptPath(promptId));
            Assert.AreEqual(
                Path.Combine(
                    root,
                    "recovery",
                    $"mutation-{operationId:N}-old-{promptId:N}.md"),
                paths.GetMutationRecoveryBodyPath(operationId, promptId));

            paths.EnsureRootDirectory();
            Assert.IsTrue(Directory.Exists(root));

            Directory.Delete(root, recursive: true);
            paths.EnsureDataDirectories();
            Assert.IsTrue(Directory.Exists(root));
            Assert.IsTrue(Directory.Exists(paths.PromptsDirectory));
            Assert.IsTrue(Directory.Exists(paths.RecoveryDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void DurableFileNaming_maps_all_classes_and_rejects_unknown_value()
    {
        Assert.AreEqual("settings", DurableFileNaming.GetClassTag(DurableFileClass.Settings));
        Assert.AreEqual("library", DurableFileNaming.GetClassTag(DurableFileClass.LibraryMetadata));
        Assert.AreEqual("prompt", DurableFileNaming.GetClassTag(DurableFileClass.PromptBody));
        Assert.AreEqual("recovery", DurableFileNaming.GetClassTag(DurableFileClass.RecoveryArtifact));
        Assert.AreEqual("init", DurableFileNaming.GetClassTag(DurableFileClass.InitializationControl));
        Assert.AreEqual("migration", DurableFileNaming.GetClassTag(DurableFileClass.MigrationControl));
        Assert.AreEqual("mutation", DurableFileNaming.GetClassTag(DurableFileClass.MutationControl));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DurableFileNaming.GetClassTag((DurableFileClass)int.MaxValue));
    }

    [TestMethod]
    public void Result_records_roundtrip_all_fields()
    {
        Guid id = Guid.NewGuid();
        var document = new LibraryDocument();
        var settings = new AppSettings
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            DataRootPath = "/tmp/example",
            UseDarkMode = true
        };

        Assert.AreEqual("warning", new CommitResult(false, "warning").Warning);
        Assert.IsFalse(new CommitResult(false, "warning").BackupSynchronized);

        var startup = new StartupResult(document, true, "restored");
        Assert.AreSame(document, startup.Document);
        Assert.IsTrue(startup.RecoveredFromBackup);
        Assert.AreEqual("restored", startup.Warning);

        var display = new PromptDisplayRecord(id, "title", "body", false, "load");
        Assert.AreEqual(id, display.Id);
        Assert.AreEqual("title", display.Title);
        Assert.AreEqual("body", display.Content);
        Assert.IsFalse(display.IsContentAvailable);
        Assert.AreEqual("load", display.LoadError);

        var summary = new PromptSummaryRecord(id, "summary");
        Assert.AreEqual(id, summary.Id);
        Assert.AreEqual("summary", summary.Title);

        var preview = new PromptPreviewResult("preview", true, null, true, 42);
        Assert.AreEqual("preview", preview.PreviewText);
        Assert.IsTrue(preview.IsContentAvailable);
        Assert.IsNull(preview.LoadError);
        Assert.IsTrue(preview.IsTruncated);
        Assert.AreEqual(42, preview.FileSizeBytes);

        var folder = new DataFolderChangeResult("/root", true, false, "folder warning");
        Assert.AreEqual("/root", folder.NormalizedTargetRoot);
        Assert.IsTrue(folder.ExistingLibraryFound);
        Assert.IsFalse(folder.Copied);
        Assert.AreEqual("folder warning", folder.Warning);

        var load = new SettingsLoadResult(settings, true, "settings warning");
        Assert.AreSame(settings, load.Settings);
        Assert.IsTrue(load.RecoveredFromBackup);
        Assert.AreEqual("settings warning", load.Warning);

        Assert.AreEqual("save warning", new SettingsSaveResult("save warning").Warning);
    }

    [TestMethod]
    public void LibraryValidator_accepts_valid_document_and_input_helpers()
    {
        Guid categoryId = Guid.NewGuid();
        var document = new LibraryDocument
        {
            PremadePackVersion = 1,
            Categories =
            [
                new CategoryRecord
                {
                    Id = categoryId,
                    ParentId = null,
                    Name = "Category",
                    SortOrder = 0
                }
            ],
            Prompts =
            [
                new PromptRecord
                {
                    Id = Guid.NewGuid(),
                    CategoryId = categoryId,
                    SortOrder = 0,
                    Title = "Title"
                }
            ]
        };

        LibraryValidator.Validate(document);

        Assert.IsNull(LibraryValidator.ValidatePromptTitleInput(null));
        Assert.IsNull(LibraryValidator.ValidatePromptTitleInput("  "));
        Assert.IsNull(LibraryValidator.ValidatePromptTitleInput(" Valid "));
        Assert.IsNotNull(LibraryValidator.ValidatePromptTitleInput("bad\nline"));
        Assert.IsNotNull(
            LibraryValidator.ValidatePromptTitleInput(
                new string('x', LibraryValidator.MaximumPromptTitleTextElements + 1)));

        Assert.IsNotNull(LibraryValidator.ValidateCategoryNameInput(null, []));
        Assert.IsNotNull(LibraryValidator.ValidateCategoryNameInput("  ", []));
        Assert.IsNotNull(LibraryValidator.ValidateCategoryNameInput("bad\tname", []));
        Assert.IsNotNull(
            LibraryValidator.ValidateCategoryNameInput(
                new string('x', LibraryValidator.MaximumCategoryNameLength + 1),
                []));
        Assert.IsNotNull(
            LibraryValidator.ValidateCategoryNameInput(
                "duplicate",
                ["DUPLICATE"]));
        Assert.IsNull(
            LibraryValidator.ValidateCategoryNameInput(
                "Unique",
                ["Other"]));
    }

    [TestMethod]
    public void LibraryValidator_rejects_each_structural_invalidity()
    {
        Assert.Throws<ArgumentNullException>(() => LibraryValidator.Validate(null!));

        ExpectInvalid(new LibraryDocument { SchemaVersion = 999 });
        ExpectInvalid(new LibraryDocument { PremadePackVersion = -1 });

        ExpectInvalid(new LibraryDocument
        {
            Categories =
            [
                Category(Guid.Empty, null, "A")
            ]
        });

        Guid duplicateCategory = Guid.NewGuid();
        ExpectInvalid(new LibraryDocument
        {
            Categories =
            [
                Category(duplicateCategory, null, "A"),
                Category(duplicateCategory, null, "B")
            ]
        });

        foreach (string badName in new string?[]
        {
            null!,
            "   ",
            " padded ",
            "bad\nname",
            new string('x', LibraryValidator.MaximumCategoryNameLength + 1)
        })
        {
            ExpectInvalid(new LibraryDocument
            {
                Categories =
                [
                    Category(Guid.NewGuid(), null, badName)
                ]
            });
        }

        Guid parent = Guid.NewGuid();
        Guid child = Guid.NewGuid();

        ExpectInvalid(new LibraryDocument
        {
            Categories =
            [
                Category(child, Guid.Empty, "Child")
            ]
        });

        ExpectInvalid(new LibraryDocument
        {
            Categories =
            [
                Category(child, child, "Self")
            ]
        });

        ExpectInvalid(new LibraryDocument
        {
            Categories =
            [
                Category(child, Guid.NewGuid(), "Missing parent")
            ]
        });

        ExpectInvalid(new LibraryDocument
        {
            Categories =
            [
                Category(parent, child, "A"),
                Category(child, parent, "B")
            ]
        });

        ExpectInvalid(new LibraryDocument
        {
            Categories =
            [
                Category(Guid.NewGuid(), null, "Same"),
                Category(Guid.NewGuid(), null, "same")
            ]
        });

        ExpectInvalid(new LibraryDocument
        {
            Prompts =
            [
                Prompt(Guid.Empty, null, null)
            ]
        });

        Guid duplicatePrompt = Guid.NewGuid();
        ExpectInvalid(new LibraryDocument
        {
            Prompts =
            [
                Prompt(duplicatePrompt, null, null),
                Prompt(duplicatePrompt, null, null)
            ]
        });

        foreach ((Guid? CategoryId, string? Title) invalid in new (Guid?, string?)[]
        {
            (Guid.Empty, null),
            (Guid.NewGuid(), null),
            (null, "   "),
            (null, " padded "),
            (null, "bad\nline"),
            (null, new string('x', LibraryValidator.MaximumPromptTitleTextElements + 1))
        })
        {
            ExpectInvalid(new LibraryDocument
            {
                Prompts =
                [
                    Prompt(Guid.NewGuid(), invalid.CategoryId, invalid.Title)
                ]
            });
        }
    }

    private static CategoryRecord Category(
        Guid id,
        Guid? parentId,
        string name) =>
        new()
        {
            Id = id,
            ParentId = parentId,
            Name = name,
            SortOrder = 0
        };

    private static PromptRecord Prompt(
        Guid id,
        Guid? categoryId,
        string? title) =>
        new()
        {
            Id = id,
            CategoryId = categoryId,
            SortOrder = 0,
            Title = title
        };

    private static void ExpectInvalid(LibraryDocument document) =>
        Assert.Throws<InvalidDataException>(() =>
            LibraryValidator.Validate(document));
}
