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
        string allowedRecoveryPath,
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
        string allowedRecoveryPath,
        ReadOnlySpan<byte> expectedContent,
        bool recordDurableOwnership)
    {
        ProductionRuntimeEvidence.Hit("DefaultCapabilityFileOps.CreateOwnedProbe");

        string fullRoot = PathIdentity.NormalizeForComparison(physicalRoot);
        string fullAllowedRecoveryPath = PathIdentity.NormalizeForComparison(allowedRecoveryPath);
        if (!PathIdentity.IsStrictDescendant(fullAllowedRecoveryPath, fullRoot))
        {
            throw new InvalidDataException(
                $"Capability-probe recovery path must remain inside its physical root. " +
                $"Root='{fullRoot}', RecoveryPath='{fullAllowedRecoveryPath}'.");
        }

        WindowsOwnedDurableStage stage =
            WindowsOwnedDurableStage.CreateCrashAtomicBootstrapUnderRoot(path, physicalRoot);
        var probe = new OwnedCapabilityProbe(
            stage,
            physicalRoot,
            path,
            fullAllowedRecoveryPath,
            expectedContent.Length,
            Convert.ToHexStringLower(SHA256.HashData(expectedContent)),
            recordDurableOwnership ? _ownedArtifacts : null);

        ProductionCrashCut.Hit("DefaultCapabilityFileOps.AfterCreateBeforeFirstClaim");

        if (!recordDurableOwnership)
        {
            return probe;
        }

        try
        {
            probe.RecordPhase(OwnedArtifactPhase.ProbeCreatedClaimed);
            stage.PersistAfterDurableClaim();
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
        private readonly string _initialRelativePath;
        private readonly string _allowedRecoveryRelativePath;
        private readonly long _expectedLength;
        private readonly string _expectedSha256Hex;

        public OwnedCapabilityProbe(
            WindowsOwnedDurableStage stage,
            string physicalRoot,
            string initialPath,
            string allowedRecoveryPath,
            long expectedLength,
            string expectedSha256Hex,
            IOwnedArtifactJournal? journal)
        {
            _stage = stage;
            _physicalRoot = Path.GetFullPath(physicalRoot);
            CurrentPath = Path.GetFullPath(initialPath);
            _initialRelativePath = Path.GetRelativePath(_physicalRoot, CurrentPath);
            _allowedRecoveryRelativePath = Path.GetRelativePath(
                _physicalRoot,
                Path.GetFullPath(allowedRecoveryPath));
            _expectedLength = expectedLength;
            _expectedSha256Hex = expectedSha256Hex;
            _journal = journal;
        }

        private string CurrentPath { get; set; }
        public string IdentityToken => _stage.Identity.ToToken();

        public void Write(ReadOnlySpan<byte> bytes) => _stage.Write(bytes);
        public void FlushDurable()
        {
            _stage.FlushDurable();
            RecordPhase(OwnedArtifactPhase.ProbeContentDurable);
        }

        public void RenameNoOverwriteRetainingOwnership(string targetPath)
        {
            string fullTarget = Path.GetFullPath(targetPath);
            if (!string.Equals(
                    Path.GetRelativePath(_physicalRoot, fullTarget),
                    _allowedRecoveryRelativePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Probe rename target '{targetPath}' was not durably predeclared.");
            }

            RecordPhase(OwnedArtifactPhase.ProbeRenamePrepared);
            _stage.RenameNoOverwriteRetainingOwnership(targetPath);
            CurrentPath = fullTarget;
            string cut = Path.GetFileName(fullTarget).Contains("displaced", StringComparison.OrdinalIgnoreCase)
                ? "DefaultCapabilityFileOps.AfterRenameToDisplacedBeforeRecord"
                : "DefaultCapabilityFileOps.AfterRenameToCurrentBeforeRecord";
            ProductionCrashCut.Hit(cut);
            RecordPhase(OwnedArtifactPhase.ProbeRenamed);
        }

        public void DeleteExact() => _stage.DeleteExact();
        public void Dispose() => _stage.Dispose();

        public void RecordPhase(OwnedArtifactPhase phase)
        {
            _journal?.Record(
                _physicalRoot,
                new OwnedArtifactRecord(
                    _operationId,
                    OwnedArtifactKind.CapabilityProbe,
                    phase,
                    _initialRelativePath,
                    _stage.Identity,
                    RestoreRelativePath: _allowedRecoveryRelativePath,
                    CandidateSha256Hex: _expectedSha256Hex,
                    CandidateLength: _expectedLength));
        }
    }
}
