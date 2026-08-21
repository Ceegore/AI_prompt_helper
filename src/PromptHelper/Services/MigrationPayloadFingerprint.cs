using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PromptHelper.Services;

internal static class MigrationPayloadFingerprint
{
    public static string Compute(IEnumerable<MigrationPayloadFile> files)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (MigrationPayloadFile file in files
                     .OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            Append(hash, file.RelativePath);
            hash.AppendData([0]);

            Append(hash, ((int)file.Role).ToString(CultureInfo.InvariantCulture));
            hash.AppendData([0]);

            Append(hash, file.Length.ToString(CultureInfo.InvariantCulture));
            hash.AppendData([0]);

            hash.AppendData(file.Sha256);
            hash.AppendData([0]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static string ComputeFromManifestArtifacts(IEnumerable<MigrationManifestArtifact> artifacts)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (MigrationManifestArtifact artifact in artifacts
                     .OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.RelativePath, StringComparer.Ordinal))
        {
            Append(hash, artifact.RelativePath);
            hash.AppendData([0]);

            Append(hash, ((int)artifact.Role).ToString(CultureInfo.InvariantCulture));
            hash.AppendData([0]);

            Append(hash, artifact.Length.ToString(CultureInfo.InvariantCulture));
            hash.AppendData([0]);

            byte[] shaBytes = Convert.FromHexString(artifact.Sha256Hex);
            hash.AppendData(shaBytes);
            hash.AppendData([0]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string text)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(text));
    }
}
