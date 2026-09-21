using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[TestClass]
public sealed class RecoveryBaselineVerifierTests
{
    private sealed class FakeAuthorityFileOps : IAuthorityFileOps
    {
        public Dictionary<string, StrictFilePresence> Presence { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public StrictFilePresence GetPresenceStrict(string path) =>
            Presence.TryGetValue(Path.GetFullPath(path), out StrictFilePresence value)
                ? value
                : StrictFilePresence.Missing;

        public byte[]? ReadOptionalBytesStrict(string path) =>
            throw new NotSupportedException();

        public void DeleteIfPresentStrict(string path) =>
            throw new NotSupportedException();
    }

    [TestMethod]
    public void Clean_inventory_is_accepted()
    {
        using var dir = new TestDirectory();
        var ops = new FakeAuthorityFileOps();

        RecoveryBaselineVerifier.AssertRestored(
            dir.Root,
            Inventory(
                finals: ["library.json"],
                temps: ["library.json.tmp"],
                controls: [".prompthelper-migration.json"]),
            ops);
    }

    [TestMethod]
    public void Unknown_entries_fail_closed_before_artifact_checks()
    {
        using var dir = new TestDirectory();
        var ops = new FakeAuthorityFileOps();

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            RecoveryBaselineVerifier.AssertRestored(
                dir.Root,
                Inventory(unknown: ["foreign.bin", "other.tmp"]),
                ops));

        StringAssert.Contains(ex.Message, "unknown entries");
        StringAssert.Contains(ex.Message, "foreign.bin");
    }

    [TestMethod]
    public void Remaining_final_artifact_fails_closed()
    {
        using var dir = new TestDirectory();
        var ops = new FakeAuthorityFileOps();
        string relative = "library.json";
        ops.Presence[Path.Combine(dir.Root, relative)] = StrictFilePresence.Present;

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            RecoveryBaselineVerifier.AssertRestored(
                dir.Root,
                Inventory(finals: [relative]),
                ops));

        StringAssert.Contains(ex.Message, relative);
    }

    [TestMethod]
    public void Remaining_payload_temp_fails_closed()
    {
        using var dir = new TestDirectory();
        var ops = new FakeAuthorityFileOps();
        string relative = "prompts/item.tmp";
        string full = Path.Combine(
            dir.Root,
            relative.Replace('/', Path.DirectorySeparatorChar));
        ops.Presence[full] = StrictFilePresence.Present;

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            RecoveryBaselineVerifier.AssertRestored(
                dir.Root,
                Inventory(temps: [relative]),
                ops));

        StringAssert.Contains(ex.Message, relative);
    }

    [TestMethod]
    public void Remaining_declared_control_fails_closed()
    {
        using var dir = new TestDirectory();
        var ops = new FakeAuthorityFileOps();
        string relative = "recovery/control.json";
        string full = Path.Combine(
            dir.Root,
            relative.Replace('/', Path.DirectorySeparatorChar));
        ops.Presence[full] = StrictFilePresence.Present;

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            RecoveryBaselineVerifier.AssertRestored(
                dir.Root,
                Inventory(controls: [relative]),
                ops));

        StringAssert.Contains(ex.Message, relative);
    }

    [TestMethod]
    public void Arguments_are_validated()
    {
        using var dir = new TestDirectory();
        var inventory = Inventory();
        var ops = new FakeAuthorityFileOps();

        Assert.Throws<ArgumentException>(() =>
            RecoveryBaselineVerifier.AssertRestored(" ", inventory, ops));
        Assert.Throws<ArgumentNullException>(() =>
            RecoveryBaselineVerifier.AssertRestored(dir.Root, null!, ops));
        Assert.Throws<ArgumentNullException>(() =>
            RecoveryBaselineVerifier.AssertRestored(dir.Root, inventory, null!));
    }

    private static MigrationTargetInventory Inventory(
        IReadOnlyList<string>? finals = null,
        IReadOnlyList<string>? temps = null,
        IReadOnlyList<string>? controls = null,
        IReadOnlyList<string>? unknown = null) =>
        new(
            finals ?? [],
            temps ?? [],
            controls ?? [],
            [],
            [],
            [],
            unknown ?? []);
}
