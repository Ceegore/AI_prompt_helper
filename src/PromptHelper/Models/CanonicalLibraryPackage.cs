using System;
using System.Security.Cryptography;
using System.Text.Json;
using PromptHelper.Services;

namespace PromptHelper.Models;

public sealed record CanonicalLibraryPackage
{
    public LibraryDocument Document { get; }
    public byte[] CanonicalBytes { get; }
    public string Sha256Hex { get; }

    internal CanonicalLibraryPackage(LibraryDocument document, byte[] canonicalBytes, string sha256Hex)
    {
        Document = document;
        CanonicalBytes = canonicalBytes;
        Sha256Hex = sha256Hex;
    }

    public static CanonicalLibraryPackage Create(LibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        LibraryValidator.Validate(document);
        LibraryDocument clone = LibraryDocumentCloner.Clone(document);
        string json = JsonSerializer.Serialize(clone, LibraryRepository.JsonOptions);
        byte[] bytes = StrictUtf8Text.Encode(json);
        return new CanonicalLibraryPackage(
            clone,
            bytes,
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
}
