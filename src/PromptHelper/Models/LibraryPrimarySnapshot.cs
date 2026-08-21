using System;

namespace PromptHelper.Models;

public sealed record LibraryPrimarySnapshot(
    byte[] RawBytes,
    LibraryDocument Document,
    byte[] CanonicalBytes,
    string RawSha256Hex,
    string CanonicalSha256Hex);
