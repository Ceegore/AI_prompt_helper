using System;
using System.Security.Cryptography;

namespace PromptHelper.Services;

internal enum MutationContentState
{
    Missing,
    Old,
    New,
    Other
}

internal static class MutationContentClassifier
{
    public static MutationContentState ClassifyBytes(
        byte[]? bytes,
        long? oldLength,
        string? oldSha,
        long? newLength,
        string? newSha)
    {
        if (bytes is null)
        {
            return MutationContentState.Missing;
        }

        if (oldLength.HasValue &&
            oldSha is not null &&
            bytes.LongLength == oldLength.Value &&
            string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                oldSha,
                StringComparison.OrdinalIgnoreCase))
        {
            return MutationContentState.Old;
        }

        if (newLength.HasValue &&
            newSha is not null &&
            bytes.LongLength == newLength.Value &&
            string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(bytes)),
                newSha,
                StringComparison.OrdinalIgnoreCase))
        {
            return MutationContentState.New;
        }

        return MutationContentState.Other;
    }
}
