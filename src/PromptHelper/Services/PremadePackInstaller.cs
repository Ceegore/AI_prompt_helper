using PromptHelper.Models;
using System.IO;

namespace PromptHelper.Services;

internal sealed record PremadePackInstallResult(
    LibraryDocument Document,
    bool Installed,
    string? Warning = null);

/// <summary>
/// Installs each bundled premade pack exactly once per library. Prompt bodies are created
/// before the single metadata commit. If the process stops in between, the exact bundled
/// bodies are recognized on the next launch and the install resumes without overwriting data.
/// </summary>
internal sealed class PremadePackInstaller
{
    private readonly LibraryRepository _libraryRepo;
    private readonly PromptRepository _promptRepo;
    private readonly LibraryPackageInspector _inspector;

    public PremadePackInstaller(
        LibraryRepository libraryRepo,
        PromptRepository promptRepo,
        LibraryPackageInspector inspector)
    {
        _libraryRepo = libraryRepo ?? throw new ArgumentNullException(nameof(libraryRepo));
        _promptRepo = promptRepo ?? throw new ArgumentNullException(nameof(promptRepo));
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
    }

    public PremadePackInstallResult InstallIfNeeded(LibraryDocument current)
    {
        ArgumentNullException.ThrowIfNull(current);
        LibraryValidator.Validate(current);

        if (current.PremadePackVersion >= PremadePromptCatalog.CurrentPackVersion)
        {
            return new PremadePackInstallResult(
                LibraryDocumentCloner.Clone(current),
                Installed: false);
        }

        if (_inspector.Inspect(current) is not LibraryPackageState.Healthy)
        {
            return new PremadePackInstallResult(
                LibraryDocumentCloner.Clone(current),
                Installed: false,
                Warning: "The Premades pack was not installed because the current library has an unavailable prompt body. Restore or remove the unavailable prompt, then restart Prompt Helper.");
        }

        LibraryPrimarySnapshot disk = _libraryRepo.CapturePrimarySnapshot();
        byte[] expectedCurrent = _libraryRepo.SerializeCanonicalBytes(current);
        if (!disk.CanonicalBytes.AsSpan().SequenceEqual(expectedCurrent))
        {
            throw new InvalidOperationException(
                "The library changed outside the current Prompt Helper state. Reload before installing Premades.");
        }

        DefaultLibraryPackage pack = PremadePromptCatalog.CreatePackage();
        LibraryDocument candidate = LibraryDocumentCloner.Clone(current);
        var categoryMap = new Dictionary<Guid, Guid>();

        foreach (PremadeCategoryDefinition definition in PremadePromptCatalog.Categories)
        {
            Guid? actualParentId = definition.ParentId.HasValue
                ? categoryMap[definition.ParentId.Value]
                : null;

            CategoryRecord? sameId = candidate.Categories.SingleOrDefault(x => x.Id == definition.Id);
            if (sameId != null)
            {
                // Stable IDs identify categories from an older pack. Preserve any rename,
                // move, or sort-order customization the user made after installation.
                categoryMap.Add(definition.Id, sameId.Id);
                continue;
            }

            // A user may already have created a category named Premades (or part of the
            // mirrored hierarchy). Reuse that category instead of renaming or duplicating it.
            CategoryRecord? sameLocationAndName = candidate.Categories.SingleOrDefault(x =>
                x.ParentId == actualParentId &&
                string.Equals(x.Name, definition.Name, StringComparison.OrdinalIgnoreCase));

            if (sameLocationAndName != null)
            {
                categoryMap.Add(definition.Id, sameLocationAndName.Id);
                continue;
            }

            var category = new CategoryRecord
            {
                Id = definition.Id,
                ParentId = actualParentId,
                Name = definition.Name,
                SortOrder = definition.SortOrder
            };
            candidate.Categories.Add(category);
            categoryMap.Add(definition.Id, category.Id);
        }

        foreach (PremadePromptDefinition definition in PremadePromptCatalog.Prompts)
        {
            Guid actualCategoryId = categoryMap[definition.CategoryId];
            PromptRecord? existing = candidate.Prompts.SingleOrDefault(x => x.Id == definition.Id);

            if (existing != null)
            {
                // Stable IDs identify prompts from an older pack. Never compare or replace
                // their metadata or body: users own their copy after installation.
                continue;
            }

            string expectedContent = pack.PromptContents[definition.Id];
            if (_promptRepo.Exists(definition.Id))
            {
                string actualContent = _promptRepo.Read(definition.Id);
                if (!string.Equals(actualContent, expectedContent, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Cannot install Premades because prompt file {definition.Id:N}.md already exists with different content.");
                }
            }
            else
            {
                _promptRepo.Create(definition.Id, expectedContent);
            }

            candidate.Prompts.Add(new PromptRecord
            {
                Id = definition.Id,
                CategoryId = actualCategoryId,
                Title = definition.Title,
                SortOrder = definition.SortOrder
            });
        }

        candidate.PremadePackVersion = PremadePromptCatalog.CurrentPackVersion;
        LibraryValidator.Validate(candidate);

        CanonicalLibraryPackage package = _libraryRepo.CreateCanonicalPackage(candidate);
        CommitResult commit = _libraryRepo.CommitIfPrimaryUnchanged(package, disk.RawSha256Hex);

        return new PremadePackInstallResult(
            LibraryDocumentCloner.Clone(candidate),
            Installed: true,
            Warning: commit.Warning);
    }
}
