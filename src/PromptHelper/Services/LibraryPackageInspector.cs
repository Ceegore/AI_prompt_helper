using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using PromptHelper.Models;

namespace PromptHelper.Services;

internal sealed record HealthyLibraryPackage(
    LibraryDocument Document,
    IReadOnlyDictionary<Guid, PromptBodySnapshot> Bodies);

internal abstract record LibraryPackageState
{
    public sealed record Healthy(
        HealthyLibraryPackage Package)
        : LibraryPackageState
    {
        public LibraryDocument Document => Package.Document;
        public IReadOnlyDictionary<Guid, PromptBodySnapshot> Bodies => Package.Bodies;
    }

    public sealed record MetadataInvalid(Exception Error)
        : LibraryPackageState;

    public sealed record BodyMissing(
        LibraryDocument Document,
        Guid PromptId,
        string Path)
        : LibraryPackageState;

    public sealed record BodyUnreadable(
        LibraryDocument Document,
        Guid PromptId,
        string Path,
        Exception Error)
        : LibraryPackageState;
}

internal sealed record PromptBodySnapshot(
    Guid PromptId,
    long Length,
    byte[] Sha256);

internal sealed class LibraryPackageInspector
{
    private readonly AppPaths _paths;

    public LibraryPackageInspector(AppPaths paths)
    {
        _paths =
            paths ??
            throw new ArgumentNullException(nameof(paths));
    }

    public LibraryPackageState Inspect(
        LibraryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        LibraryValidator.Validate(document);

        var bodies =
            new Dictionary<Guid, PromptBodySnapshot>();

        foreach (PromptRecord prompt in document.Prompts)
        {
            string path =
                _paths.GetPromptPath(prompt.Id);

            byte[] bytes;

            try
            {
                bytes = File.ReadAllBytes(path);
                // Strict UTF-8 validation
                StrictUtf8Text.Decode(bytes, $"prompt body '{prompt.Id}'");
            }
            catch (FileNotFoundException)
            {
                return new LibraryPackageState.BodyMissing(
                    LibraryDocumentCloner.Clone(document),
                    prompt.Id,
                    path);
            }
            catch (DirectoryNotFoundException)
            {
                return new LibraryPackageState.BodyMissing(
                    LibraryDocumentCloner.Clone(document),
                    prompt.Id,
                    path);
            }
            catch (Exception ex) when (
                ex is IOException or
                UnauthorizedAccessException or
                SecurityException or
                InvalidDataException)
            {
                return new LibraryPackageState.BodyUnreadable(
                    LibraryDocumentCloner.Clone(document),
                    prompt.Id,
                    path,
                    ex);
            }

            bodies[prompt.Id] =
                new PromptBodySnapshot(
                    prompt.Id,
                    bytes.LongLength,
                    SHA256.HashData(bytes));
        }

        var healthyPackage = new HealthyLibraryPackage(
            LibraryDocumentCloner.Clone(document),
            bodies);

        return new LibraryPackageState.Healthy(healthyPackage);
    }
}