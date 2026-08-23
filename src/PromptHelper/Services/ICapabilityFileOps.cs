using System;
using System.IO;
using System.Security.Cryptography;

namespace PromptHelper.Services;

/// <summary>
/// One capability-probe object whose creation handle remains the authority for every mutation.
/// A pathname is never enough authority to rename or destroy a probe (CRUU19-001).
/// </summary>
internal interface IOwnedCapabilityProbe : IDisposable
{
    string IdentityToken { get; }
    void Write(ReadOnlySpan<byte> bytes);
    void FlushDurable();
    void RenameNoOverwriteRetainingOwnership(string targetPath);
    void DeleteExact();
}

internal interface ICapabilityFileOps
{
    IOwnedCapabilityProbe CreateOwnedProbe(
        string physicalRoot,
        string path,
        ReadOnlySpan<byte> expectedContent,
        bool recordDurableOwnership);

    void RetireSettledOwnership(string physicalRoot);
    bool FileExists(string path);
    bool DirectoryExists(string path);
}

internal sealed class DefaultCapabilityFileOps : ICapabilityFileOps
{
    private readonly IOwnedArtifactJournal _ownedArtifacts;
    private readonly StrictPathAuthority _authority = new();

    internal DefaultCapabilityFileOps(IOwnedArtifactJournal? ownedArtifacts = null)
    {
        _ownedArtifacts = ownedArtifacts ?? new WindowsOwnedArtifactJournal();
    }

    public IOwnedCapabilityProbe CreateOwnedProbe(
        string physicalRoot,
        string path,
        ReadOnlySpan<byte> expectedContent,
        bool recordDurableOwnership)
    {
        ProductionRuntimeEvidence.Hit("DefaultCapabilityFileOps.CreateOwnedProbe");

        var probe = new OwnedCapabilityProbe(
            WindowsOwnedDurableStage.CreateNewUnderRoot(path, physicalRoot),
            physicalRoot,
            path,
            recordDurableOwnership ? _ownedArtifacts : null);

        if (!recordDurableOwnership)
        {
            return probe;
        }

        try
        {
            probe.RecordLocation(
                path,
                expectedContent.Length,
                Convert.ToHexStringLower(SHA256.HashData(expectedContent)));
            return probe;
        }
        catch (Exception recordFailure)
        {
            try
            {
                probe.DeleteExact();
            }
            catch (Exception cleanupFailure)
            {
                probe.Dispose();
                throw new IOException(
                    $"Capability-probe ownership could not be recorded and exact cleanup failed for '{path}'.",
                    new AggregateException(recordFailure, cleanupFailure));
            }

            probe.Dispose();
            throw;
        }
    }

    public void RetireSettledOwnership(string physicalRoot)
    {
        OwnedArtifactReconciler.Result result =
            OwnedArtifactReconciler.Reconcile(physicalRoot, _ownedArtifacts);
        if (result.HasFatal)
        {
            throw new InvalidDataException(
                "Capability-probe ownership reconciliation failed: " +
                string.Join("; ", result.Outcomes));
        }
    }

    public bool FileExists(string path) => _authority.Probe(path).Kind == StrictPathKind.File;
    public bool DirectoryExists(string path) => _authority.Probe(path).Kind == StrictPathKind.Directory;

    private sealed class OwnedCapabilityProbe : IOwnedCapabilityProbe
    {
        private readonly WindowsOwnedDurableStage _stage;
        private readonly string _physicalRoot;
        private readonly IOwnedArtifactJournal? _journal;
        private readonly Guid _operationId = Guid.NewGuid();
        private long _expectedLength;
        private string? _expectedSha256Hex;

        public OwnedCapabilityProbe(
            WindowsOwnedDurableStage stage,
            string physicalRoot,
            string initialPath,
            IOwnedArtifactJournal? journal)
        {
            _stage = stage;
            _physicalRoot = Path.GetFullPath(physicalRoot);
            CurrentPath = Path.GetFullPath(initialPath);
            _journal = journal;
        }

        private string CurrentPath { get; set; }
        public string IdentityToken => _stage.Identity.ToToken();

        public void Write(ReadOnlySpan<byte> bytes) => _stage.Write(bytes);
        public void FlushDurable() => _stage.FlushDurable();

        public void RenameNoOverwriteRetainingOwnership(string targetPath)
        {
            _stage.RenameNoOverwriteRetainingOwnership(targetPath);
            CurrentPath = Path.GetFullPath(targetPath);
            if (_journal is not null)
            {
                RecordLocation(CurrentPath, _expectedLength, _expectedSha256Hex!);
            }
        }

        public void DeleteExact() => _stage.DeleteExact();
        public void Dispose() => _stage.Dispose();

        public void RecordLocation(string path, long expectedLength, string expectedSha256Hex)
        {
            _expectedLength = expectedLength;
            _expectedSha256Hex = expectedSha256Hex;
            _journal?.Record(
                _physicalRoot,
                new OwnedArtifactRecord(
                    _operationId,
                    OwnedArtifactKind.Stage,
                    OwnedArtifactPhase.Claimed,
                    Path.GetRelativePath(_physicalRoot, Path.GetFullPath(path)),
                    _stage.Identity,
                    CandidateSha256Hex: expectedSha256Hex,
                    CandidateLength: expectedLength));
        }
    }
}
