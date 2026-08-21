using System;

namespace PromptHelper.Services;

internal sealed record DataRootRuntimeContext(
    string ActivePhysicalRoot,
    string BootstrapLexicalRoot,
    string BootstrapPhysicalRoot)
{
    public static DataRootRuntimeContext Create(
        string activePhysicalRoot,
        string bootstrapLexicalRoot,
        IPhysicalPathResolver? resolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activePhysicalRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapLexicalRoot);

        IPhysicalPathResolver activeResolver = resolver ?? new WindowsPhysicalPathResolver();

        string bootstrapPhysical = DataRootTopologyValidator.ResolvePhysicalOrThrow(
            activeResolver,
            bootstrapLexicalRoot,
            "bootstrap settings folder");

        return new DataRootRuntimeContext(
            PathIdentity.NormalizeForComparison(activePhysicalRoot),
            bootstrapLexicalRoot,
            PathIdentity.NormalizeForComparison(bootstrapPhysical));
    }
}
