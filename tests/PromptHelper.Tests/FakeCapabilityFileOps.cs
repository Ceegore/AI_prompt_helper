using System;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class FakeCapabilityFileOps : ICapabilityFileOps
{
    private readonly ICapabilityFileOps _inner = new DefaultCapabilityFileOps();

    public Action<string, string, string?>? OnReplace { get; set; }
    public Action<string>? OnDeleteOwnedProbe { get; set; }
    public Func<string, bool>? OnFileExists { get; set; }
    public Func<string, bool>? OnDirectoryExists { get; set; }

    public IOwnedCapabilityProbe CreateOwnedProbe(
        string physicalRoot,
        string path,
        ReadOnlySpan<byte> expectedContent,
        bool recordDurableOwnership)
    {
        IOwnedCapabilityProbe inner = _inner.CreateOwnedProbe(
            physicalRoot,
            path,
            expectedContent,
            recordDurableOwnership);
        return new FakeOwnedProbe(inner, path, OnReplace, OnDeleteOwnedProbe);
    }

    public void RetireSettledOwnership(string physicalRoot) =>
        _inner.RetireSettledOwnership(physicalRoot);

    public bool FileExists(string path) =>
        OnFileExists?.Invoke(path) ?? _inner.FileExists(path);

    public bool DirectoryExists(string path) =>
        OnDirectoryExists?.Invoke(path) ?? _inner.DirectoryExists(path);

    private sealed class FakeOwnedProbe : IOwnedCapabilityProbe
    {
        private readonly IOwnedCapabilityProbe _inner;
        private readonly Action<string, string, string?>? _onReplace;
        private readonly Action<string>? _onDelete;
        private string _currentPath;

        public FakeOwnedProbe(
            IOwnedCapabilityProbe inner,
            string initialPath,
            Action<string, string, string?>? onReplace,
            Action<string>? onDelete)
        {
            _inner = inner;
            _currentPath = initialPath;
            _onReplace = onReplace;
            _onDelete = onDelete;
        }

        public string IdentityToken => _inner.IdentityToken;

        public void Write(ReadOnlySpan<byte> bytes) => _inner.Write(bytes);
        public void FlushDurable() => _inner.FlushDurable();

        public void RenameNoOverwriteRetainingOwnership(string targetPath)
        {
            _onReplace?.Invoke(_currentPath, targetPath, null);
            _inner.RenameNoOverwriteRetainingOwnership(targetPath);
            _currentPath = targetPath;
        }

        public void DeleteExact()
        {
            _onDelete?.Invoke(_currentPath);
            _inner.DeleteExact();
        }

        public void Dispose() => _inner.Dispose();
    }
}
