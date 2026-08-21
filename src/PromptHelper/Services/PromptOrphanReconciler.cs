using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PromptHelper.Models;

namespace PromptHelper.Services;

internal sealed record OrphanReconciliationAuthority(
    LibraryDocument Primary,
    LibraryDocument Backup);

internal sealed record OrphanReconciliationResult(
    IReadOnlyList<string> Deleted,
    IReadOnlyList<string> Preserved,
    string? Warning);

internal sealed class PromptOrphanReconciler
{
    private readonly AppPaths _paths;
    private readonly PromptRepository _prompts;
    private readonly LibraryMutationJournalRepository _journalRepo;

    public PromptOrphanReconciler(
        AppPaths paths,
        PromptRepository prompts,
        LibraryMutationJournalRepository journalRepo)
    {
        _paths = paths;
        _prompts = prompts;
        _journalRepo = journalRepo;
    }

    public OrphanReconciliationResult Reconcile(
        OrphanReconciliationAuthority authority)
    {
        if (_journalRepo.TryReadStrict() is not null)
        {
            return new OrphanReconciliationResult(
                [],
                [],
                "Orphan cleanup deferred while a library mutation journal exists.");
        }

        var protectedIds = new HashSet<Guid>(
            authority.Primary.Prompts.Select(p => p.Id));

        protectedIds.UnionWith(
            authority.Backup.Prompts.Select(p => p.Id));

        var deleted = new List<string>();
        var preserved = new List<string>();

        foreach (string path in _prompts.EnumeratePromptFilesStrict())
        {
            string stem = Path.GetFileNameWithoutExtension(path);

            if (!Guid.TryParseExact(stem, "N", out Guid id))
            {
                preserved.Add(path);
                continue;
            }

            if (protectedIds.Contains(id))
            {
                preserved.Add(path);
                continue;
            }

            // App-lifetime tree lease is already held.
            try
            {
                File.Delete(path);
                deleted.Add(path);
            }
            catch
            {
                preserved.Add(path);
            }
        }

        return new OrphanReconciliationResult(deleted, preserved, null);
    }
}
