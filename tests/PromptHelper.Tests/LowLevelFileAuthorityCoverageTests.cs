using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class LowLevelFileAuthorityCoverageTests
{
    [TestMethod]
    public void Durable_temp_parser_accepts_all_supported_classes_case_insensitively()
    {
        Guid id = Guid.NewGuid();
        string n = id.ToString("N");

        var cases = new (string Tag, DurableFileClass Class)[]
        {
            ("settings", DurableFileClass.Settings),
            ("library", DurableFileClass.LibraryMetadata),
            ("prompt", DurableFileClass.PromptBody),
            ("recovery", DurableFileClass.RecoveryArtifact),
            ("init", DurableFileClass.InitializationControl),
            ("migration", DurableFileClass.MigrationControl),
            ("mutation", DurableFileClass.MutationControl)
        };

        foreach ((string tag, DurableFileClass expected) in cases)
        {
            Assert.IsTrue(
                DurableTempReconciler.TryParseDurableTemp(
                    $".prompthelper-tmp-{tag}-{n}.tmp",
                    out DurableFileClass actual));
            Assert.AreEqual(expected, actual);

            Assert.IsTrue(
                DurableTempReconciler.TryParseDurableTemp(
                    $".PROMPTHELPER-TMP-{tag.ToUpperInvariant()}-{n}.TMP",
                    out actual));
            Assert.AreEqual(expected, actual);
        }
    }

    [TestMethod]
    public void Durable_temp_parser_rejects_malformed_and_unknown_names()
    {
        string valid = Guid.NewGuid().ToString("N");

        string[] invalid =
        [
            "",
            "   ",
            "file.tmp",
            ".prompthelper-tmp-.tmp",
            ".prompthelper-tmp-library.tmp",
            ".prompthelper-tmp-library-.tmp",
            ".prompthelper-tmp-library-not-a-guid.tmp",
            ".prompthelper-tmp-library-123.tmp",
            $".prompthelper-tmp-unknown-{valid}.tmp",
            $".prompthelper-tmp-library-{valid}.bak"
        ];

        foreach (string name in invalid)
        {
            Assert.IsFalse(
                DurableTempReconciler.TryParseDurableTemp(name, out _),
                name);
        }
    }

    [TestMethod]
    public void Legacy_data_root_temp_parser_accepts_all_historical_formats()
    {
        string id = Guid.NewGuid().ToString("N");

        AssertLegacyDataRoot(
            $".library.json.{id}.tmp",
            "legacy library metadata temp");
        AssertLegacyDataRoot(
            $".LIBRARY.BACKUP.JSON.{id}.TMP",
            "legacy library backup temp");
        AssertLegacyDataRoot(
            $".initializing.marker.{id}.tmp",
            "legacy initializing marker temp");
    }

    [TestMethod]
    public void Legacy_data_root_temp_parser_rejects_malformed_names()
    {
        string[] invalid =
        [
            "",
            "   ",
            "library.json.tmp",
            ".library.json.tmp",
            ".library.json.not-guid.tmp",
            ".library.backup.json.123.tmp",
            ".initializing.marker.not-guid.tmp",
            ".other." + Guid.NewGuid().ToString("N") + ".tmp",
            ".library.json." + Guid.NewGuid().ToString("N") + ".bak"
        ];

        foreach (string name in invalid)
        {
            Assert.IsFalse(
                DurableTempReconciler.TryParseLegacyDataRootTemp(
                    name,
                    out string description),
                name);
            Assert.AreEqual(string.Empty, description);
        }
    }

    [TestMethod]
    public void Legacy_prompt_temp_parser_accepts_and_rejects_expected_formats()
    {
        string prompt = Guid.NewGuid().ToString("N");
        string temp = Guid.NewGuid().ToString("N");

        Assert.IsTrue(
            DurableTempReconciler.TryParseLegacyPromptTemp(
                $".{prompt}.md.{temp}.tmp"));
        Assert.IsTrue(
            DurableTempReconciler.TryParseLegacyPromptTemp(
                $".{prompt}.MD.{temp}.TMP"));

        string[] invalid =
        [
            "",
            "   ",
            $"{prompt}.md.{temp}.tmp",
            $".{prompt}.txt.{temp}.tmp",
            $".{prompt}.md.bad.tmp",
            $".bad.md.{temp}.tmp",
            $".{prompt}.md.{temp}.bak",
            $".{prompt}.md.{temp}.extra.tmp"
        ];

        foreach (string name in invalid)
        {
            Assert.IsFalse(
                DurableTempReconciler.TryParseLegacyPromptTemp(name),
                name);
        }
    }

    [TestMethod]
    public void Strict_file_authority_reports_present_missing_and_missing_parent()
    {
        using var dir = new TestDirectory();
        string file = Path.Combine(dir.Root, "file.bin");
        byte[] bytes = [1, 2, 3, 4];
        File.WriteAllBytes(file, bytes);

        Assert.AreEqual(
            StrictFilePresence.Present,
            StrictFileAuthority.GetPresence(file));
        CollectionAssert.AreEqual(
            bytes,
            StrictFileAuthority.ReadOptionalBytes(file));

        string missing = Path.Combine(dir.Root, "missing.bin");
        Assert.AreEqual(
            StrictFilePresence.Missing,
            StrictFileAuthority.GetPresence(missing));
        Assert.IsNull(StrictFileAuthority.ReadOptionalBytes(missing));

        string missingParent = Path.Combine(
            dir.Root,
            "missing-parent",
            "file.bin");
        Assert.AreEqual(
            StrictFilePresence.Missing,
            StrictFileAuthority.GetPresence(missingParent));
        Assert.IsNull(
            StrictFileAuthority.ReadOptionalBytes(missingParent));
    }

    [TestMethod]
    public void Strict_file_authority_delete_is_idempotent_for_present_and_missing_paths()
    {
        using var dir = new TestDirectory();
        string file = Path.Combine(dir.Root, "delete.bin");
        File.WriteAllText(file, "delete");

        StrictFileAuthority.DeleteIfPresentStrict(file);
        Assert.IsFalse(File.Exists(file));

        StrictFileAuthority.DeleteIfPresentStrict(file);
        StrictFileAuthority.DeleteIfPresentStrict(
            Path.Combine(dir.Root, "missing-parent", "file.bin"));
    }

    [TestMethod]
    public void Strict_file_authority_validates_path_arguments()
    {
        foreach (string? path in new string?[] { null, "", "   " })
        {
            Assert.Throws<ArgumentException>(() =>
                StrictFileAuthority.GetPresence(path!));
            Assert.Throws<ArgumentException>(() =>
                StrictFileAuthority.ReadOptionalBytes(path!));
            Assert.Throws<ArgumentException>(() =>
                StrictFileAuthority.DeleteIfPresentStrict(path!));
        }
    }

    private static void AssertLegacyDataRoot(
        string name,
        string expectedDescription)
    {
        Assert.IsTrue(
            DurableTempReconciler.TryParseLegacyDataRootTemp(
                name,
                out string description));
        Assert.AreEqual(expectedDescription, description);
    }
}
