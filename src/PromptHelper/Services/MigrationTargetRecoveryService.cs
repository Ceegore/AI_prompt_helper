using System;
using System.IO;

namespace PromptHelper.Services;

internal static class MigrationTargetRecoveryService
{
    public static void ResolveInterruptedTarget(
        string targetRoot,
        MigrationManifestRepository? manifestRepo = null,
        IMigrationFileOps? fileOps = null,
        string? bootstrapRoot = null)
    {
        var service = new MigrationRecoveryService(manifestRepo, fileOps);
        var context = new MigrationRecoveryContext(targetRoot, bootstrapRoot);
        var result = service.RecoverForRetry(context);
        if (!result.Success)
        {
            if (result.Error is InvalidDataException ide)
            {
                throw ide;
            }
            throw new InvalidDataException(result.ErrorMessage ?? $"Failed to recover migration target '{targetRoot}'.", result.Error);
        }
    }
}
