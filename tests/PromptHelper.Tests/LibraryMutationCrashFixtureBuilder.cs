using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Tests;

internal sealed class LibraryMutationCrashFixtureBuilder
{
    private readonly string _root;
    private readonly AppPaths _paths;

    public LibraryMutationCrashFixtureBuilder(string root)
    {
        _root = root;
        _paths = new AppPaths(root);
        _paths.EnsureDataDirectories();
    }

    public LibraryMutationCrashFixtureBuilder WithPrimary(LibraryDocument document)
    {
        string json = JsonSerializer.Serialize(document, LibraryRepository.JsonOptions);
        File.WriteAllBytes(_paths.LibraryPath, StrictUtf8Text.Encode(json));
        return this;
    }

    public LibraryMutationCrashFixtureBuilder WithBackup(LibraryDocument document)
    {
        string json = JsonSerializer.Serialize(document, LibraryRepository.JsonOptions);
        File.WriteAllBytes(_paths.LibraryBackupPath, StrictUtf8Text.Encode(json));
        return this;
    }

    public LibraryMutationCrashFixtureBuilder WithBody(Guid id, byte[] body)
    {
        File.WriteAllBytes(_paths.GetPromptPath(id), body);
        return this;
    }

    public LibraryMutationCrashFixtureBuilder WithRecoveryBody(Guid operationId, Guid promptId, byte[] body)
    {
        File.WriteAllBytes(_paths.GetMutationRecoveryBodyPath(operationId, promptId), body);
        return this;
    }

    public LibraryMutationCrashFixtureBuilder WithJournal(LibraryMutationJournal journal)
    {
        byte[] bytes = LibraryMutationJournalRepository.SerializeValidate(journal);
        File.WriteAllBytes(_paths.LibraryMutationJournalPath, bytes);
        return this;
    }
}
