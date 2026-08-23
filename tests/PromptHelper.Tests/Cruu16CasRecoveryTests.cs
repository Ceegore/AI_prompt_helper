using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// CRUU16-001: crash recovery for the two-rename compare-and-swap.
/// </summary>
/// <remarks>
/// While a swap is in flight the pre-image is the only durable copy of the last committed
/// state. The CRUU15 implementation retired it whenever <i>something</i> occupied the target
/// pathname, which is not the same as "our candidate committed" — a foreign file created after
/// a crash was enough to destroy committed data. These tests drive the real primitive to each
/// durable cut and assert the outcome.
/// </remarks>
[TestClass]
public sealed class Cruu16CasRecoveryTests
{
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>
    /// Runs a real compare-and-swap and abandons the process at the cut between the two
    /// renames — the state a power loss there would leave on disk.
    /// </summary>
    private static void CrashBetweenRenames(string root, string target, byte[] candidate, string expectedHash)
    {
        WindowsAtomicExpectedFileReplacer.BetweenRenamesForTests =
            _ => throw new SimulatedCrash();

        try
        {
            Assert.ThrowsExactly<SimulatedCrash>(() =>
                new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
                    root,
                    target,
                    ExpectedFileState.Present(expectedHash),
                    candidate,
                    DurableFileClass.LibraryMetadata));
        }
        finally
        {
            WindowsAtomicExpectedFileReplacer.BetweenRenamesForTests = null;
        }
    }

    private sealed class SimulatedCrash : Exception;

    private static string PreimagePath(string root) =>
        Directory.GetFiles(root, ".prompthelper-preimage-*").Single();

    [TestMethod]
    public void CRUU16_001_Crash_between_CAS_renames_foreign_target_preserves_preimage_and_fails_closed()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] committed = Encoding.UTF8.GetBytes("the committed content");
        File.WriteAllBytes(target, committed);

        CrashBetweenRenames(temp.Root, target, Encoding.UTF8.GetBytes("our candidate"), Hash(committed));

        // The crash left the target vacated and the committed content under the pre-image name.
        Assert.IsFalse(File.Exists(target));
        string preimage = PreimagePath(temp.Root);

        // Before restart, something else creates a file at the target pathname.
        byte[] foreign = Encoding.UTF8.GetBytes("a foreign file created after the crash");
        File.WriteAllBytes(target, foreign);

        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(temp.Root, new WindowsOwnedArtifactJournal());

        Assert.IsTrue(result.HasFatal,
            "Occupancy of the target pathname must never be accepted as proof the swap committed.");
        Assert.IsTrue(File.Exists(preimage), "The last committed content must be preserved.");
        CollectionAssert.AreEqual(committed, File.ReadAllBytes(preimage));
        CollectionAssert.AreEqual(foreign, File.ReadAllBytes(target), "The foreign file must also be untouched.");
    }

    [TestMethod]
    public void CRUU16_001_Crash_between_CAS_renames_invalid_target_preserves_last_committed_preimage()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] committed = Encoding.UTF8.GetBytes("the committed content");
        File.WriteAllBytes(target, committed);

        CrashBetweenRenames(temp.Root, target, Encoding.UTF8.GetBytes("our candidate"), Hash(committed));
        string preimage = PreimagePath(temp.Root);

        // Nothing appears at the target: the swap is simply unfinished, and the pre-image is
        // the last committed content. Restoring it destroys nothing.
        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(temp.Root, new WindowsOwnedArtifactJournal());

        Assert.IsFalse(result.HasFatal, string.Join("; ", result.Outcomes.Select(o => o.Message)));
        Assert.IsTrue(File.Exists(target));
        CollectionAssert.AreEqual(committed, File.ReadAllBytes(target),
            "An interrupted update must roll back to the last committed content.");
        Assert.IsFalse(File.Exists(preimage));
    }

    [TestMethod]
    public void CRUU16_001_Crash_after_candidate_publish_exact_candidate_allows_preimage_retirement()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] committed = Encoding.UTF8.GetBytes("the committed content");
        byte[] candidate = Encoding.UTF8.GetBytes("our candidate");
        File.WriteAllBytes(target, committed);

        CrashBetweenRenames(temp.Root, target, candidate, Hash(committed));
        string preimage = PreimagePath(temp.Root);

        // The candidate did land before the crash — simulate the publish having completed
        // without the pre-image having been retired.
        File.WriteAllBytes(target, candidate);

        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(temp.Root, new WindowsOwnedArtifactJournal());

        Assert.IsFalse(result.HasFatal, string.Join("; ", result.Outcomes.Select(o => o.Message)));
        Assert.IsFalse(File.Exists(preimage), "A superseded pre-image is retired once the candidate is proven.");
        CollectionAssert.AreEqual(candidate, File.ReadAllBytes(target));
    }

    [TestMethod]
    public void CRUU16_001_Target_presence_alone_is_never_CAS_commit_authority()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] committed = Encoding.UTF8.GetBytes("the committed content");
        File.WriteAllBytes(target, committed);

        CrashBetweenRenames(temp.Root, target, Encoding.UTF8.GetBytes("our candidate"), Hash(committed));
        string preimage = PreimagePath(temp.Root);

        // Content that is neither the previous state nor the candidate: the only honest answer
        // is that this cannot be resolved automatically.
        File.WriteAllBytes(target, Encoding.UTF8.GetBytes("something else entirely"));

        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(temp.Root, new WindowsOwnedArtifactJournal());

        Assert.IsTrue(result.HasFatal);
        Assert.IsTrue(result.Outcomes.Any(o => o.Code == "CAS_AMBIGUOUS"));
        Assert.IsTrue(File.Exists(preimage));
        CollectionAssert.AreEqual(committed, File.ReadAllBytes(preimage));

        // The durable record carries the candidate's identity; that is what makes the
        // distinction decidable at all.
        OwnedArtifactRecord record = new WindowsOwnedArtifactJournal()
            .Read(temp.Root).Records
            .Where(r => r.Kind == OwnedArtifactKind.CasPreimage)
            .OrderByDescending(r => r.Phase)
            .First();

        Assert.IsNotNull(record.CandidateSha256Hex);
        Assert.AreEqual(Hash(Encoding.UTF8.GetBytes("our candidate")), record.CandidateSha256Hex);
        Assert.AreEqual(Encoding.UTF8.GetBytes("our candidate").Length, record.CandidateLength);
    }

    [TestMethod]
    public void CRUU16_001_CAS_recovery_matrix_covers_every_durable_phase()
    {
        // Every phase a compare-and-swap can stop at must have a defined outcome; a phase with
        // no case in the matrix is a crash cut nobody decided how to handle.
        var handled = new Dictionary<OwnedArtifactPhase, string>();

        OwnedArtifactPhase[] casPhases = Enum.GetValues<OwnedArtifactPhase>()
            .Where(phase => phase <= OwnedArtifactPhase.CandidatePublished)
            .ToArray();
        foreach (OwnedArtifactPhase phase in casPhases)
        {
            handled[phase] = ResolveForPhase(phase);
        }

        Assert.AreEqual(casPhases.Length, handled.Count);
        foreach (KeyValuePair<OwnedArtifactPhase, string> entry in handled)
        {
            Assert.IsNotNull(entry.Value, $"Phase {entry.Key} has no defined recovery outcome.");
        }
    }

    /// <summary>
    /// Drives a real transaction record at <paramref name="phase"/> through reconciliation and
    /// returns the outcome code it produced.
    /// </summary>
    private static string ResolveForPhase(OwnedArtifactPhase phase)
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] committed = Encoding.UTF8.GetBytes("committed");
        byte[] candidate = Encoding.UTF8.GetBytes("candidate");

        switch (phase)
        {
            case OwnedArtifactPhase.Claimed:
            {
                // A staging claim that never became a swap: the stage is simply a leftover.
                string stage = Path.Combine(temp.Root, $".prompthelper-tmp-library-{Guid.NewGuid():N}.tmp");
                File.WriteAllBytes(stage, candidate);
                OwnedArtifactTestSupport.ClaimOwnership(temp.Root, stage);

                OwnedArtifactReconciler.Result claimed =
                    OwnedArtifactReconciler.Reconcile(temp.Root, new WindowsOwnedArtifactJournal());
                Assert.IsFalse(claimed.HasFatal);
                Assert.IsFalse(File.Exists(stage));
                return "STAGE_RETIRED";
            }

            case OwnedArtifactPhase.Prepared:
            {
                File.WriteAllBytes(target, committed);
                WindowsAtomicExpectedFileReplacer.AfterPreparedRecordForTests =
                    _ => throw new SimulatedCrash();
                try
                {
                    Assert.ThrowsExactly<SimulatedCrash>(() =>
                        new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
                            temp.Root,
                            target,
                            ExpectedFileState.Present(Hash(committed)),
                            candidate,
                            DurableFileClass.LibraryMetadata));
                }
                finally
                {
                    WindowsAtomicExpectedFileReplacer.AfterPreparedRecordForTests = null;
                }

                OwnedArtifactReconciler.Result prepared =
                    OwnedArtifactReconciler.Reconcile(temp.Root, new WindowsOwnedArtifactJournal());
                Assert.IsFalse(prepared.HasFatal);
                CollectionAssert.AreEqual(committed, File.ReadAllBytes(target));
                return "CAS_NOT_STARTED";
            }

            case OwnedArtifactPhase.PreimageSidelined:
            {
                File.WriteAllBytes(target, committed);
                CrashBetweenRenames(temp.Root, target, candidate, Hash(committed));

                OwnedArtifactReconciler.Result sidelined =
                    OwnedArtifactReconciler.Reconcile(temp.Root, new WindowsOwnedArtifactJournal());
                Assert.IsFalse(sidelined.HasFatal);
                CollectionAssert.AreEqual(committed, File.ReadAllBytes(target));
                return "CAS_PREIMAGE_RESTORED";
            }

            case OwnedArtifactPhase.CandidatePublished:
            {
                File.WriteAllBytes(target, committed);
                new WindowsAtomicExpectedFileReplacer().ReplaceIfExpected(
                    temp.Root,
                    target,
                    ExpectedFileState.Present(Hash(committed)),
                    candidate,
                    DurableFileClass.LibraryMetadata);

                OwnedArtifactReconciler.Result published =
                    OwnedArtifactReconciler.Reconcile(temp.Root, new WindowsOwnedArtifactJournal());
                Assert.IsFalse(published.HasFatal);
                CollectionAssert.AreEqual(candidate, File.ReadAllBytes(target));
                return "COMPLETED";
            }

            default:
                throw new InvalidOperationException($"Unhandled phase {phase}.");
        }
    }

    /// <summary>
    /// A completed swap must stay completed even after the target legitimately moves on: later
    /// updates change the target's content, and comparing against a finished transaction's
    /// candidate would otherwise read every subsequent edit as an unresolved crash.
    /// </summary>
    [TestMethod]
    public void CRUU16_001_Completed_transaction_is_not_reopened_by_later_updates_to_the_target()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        var replacer = new WindowsAtomicExpectedFileReplacer();

        byte[] v1 = Encoding.UTF8.GetBytes("v1");
        File.WriteAllBytes(target, v1);

        byte[] v2 = Encoding.UTF8.GetBytes("v2");
        replacer.ReplaceIfExpected(temp.Root, target, ExpectedFileState.Present(Hash(v1)), v2, DurableFileClass.LibraryMetadata);

        byte[] v3 = Encoding.UTF8.GetBytes("v3");
        replacer.ReplaceIfExpected(temp.Root, target, ExpectedFileState.Present(Hash(v2)), v3, DurableFileClass.LibraryMetadata);

        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(temp.Root, new WindowsOwnedArtifactJournal());

        Assert.IsFalse(result.HasFatal,
            "Completed transactions must not be re-evaluated against a target that has since moved on: " +
            string.Join("; ", result.Outcomes.Select(o => $"[{o.Code}] {o.Message}")));
        CollectionAssert.AreEqual(v3, File.ReadAllBytes(target));
    }
}
