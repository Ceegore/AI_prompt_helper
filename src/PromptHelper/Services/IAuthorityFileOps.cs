using System;
using System.IO;

namespace PromptHelper.Services;

internal enum StrictFilePresence
{
    Missing,
    Present
}

internal interface IAuthorityFileOps
{
    StrictFilePresence GetPresenceStrict(string path);
    byte[]? ReadOptionalBytesStrict(string path);
    void DeleteIfPresentStrict(string path);
}

internal sealed class DefaultAuthorityFileOps : IAuthorityFileOps
{
    public StrictFilePresence GetPresenceStrict(string path) =>
        StrictFileAuthority.GetPresence(path);

    public byte[]? ReadOptionalBytesStrict(string path) =>
        StrictFileAuthority.ReadOptionalBytes(path);

    public void DeleteIfPresentStrict(string path) =>
        StrictFileAuthority.DeleteIfPresentStrict(path);
}
