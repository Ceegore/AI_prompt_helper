using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PromptHelper.Tests;

internal static class RepositoryTestPaths
{
    private static readonly Lazy<string> RootLazy = new(FindRoot);

    public static string Root => RootLazy.Value;

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);

        while (dir != null)
        {
            bool solution = File.Exists(Path.Combine(dir.FullName, "PromptHelper.slnx"));
            bool project = File.Exists(Path.Combine(
                dir.FullName,
                "src", "PromptHelper", "PromptHelper.csproj"));

            if (solution && project)
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        Assert.Fail(
            $"Could not locate repository root from test base directory: {AppContext.BaseDirectory}");
        throw new InvalidOperationException();
    }

    public static string RequireFile(params string[] parts)
    {
        string path = parts.Aggregate(Root, Path.Combine);
        Assert.IsTrue(File.Exists(path), $"Required repository file missing: {path}");
        return path;
    }

    /// <summary>
    /// The built PromptHelper.exe for whichever configuration produced the running test
    /// assembly, falling back to any configuration present.
    /// </summary>
    /// <remarks>
    /// These tests used to hardcode <c>bin/Debug</c>, which meant they found nothing whenever
    /// the suite ran against a Release build - and, because they then declared themselves
    /// inconclusive, reported that absence as success. That is the exact shape of the defect
    /// CRUU15-011 describes: a release-asset check that quietly passes when the asset it exists
    /// to validate is not there. Removing the opt-out is only half the fix; the other half is
    /// looking in the right place.
    /// </remarks>
    public static string RequireBuiltApplicationExe()
    {
        string binRoot = Path.Combine(Root, "src", "PromptHelper", "bin");

        // The test assembly runs from tests/PromptHelper.Tests/bin/<Config>/<tfm>/, so its own
        // location names the configuration that was just built.
        string? configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name;

        if (!string.IsNullOrEmpty(configuration))
        {
            string preferred = Path.Combine(binRoot, configuration, "net10.0-windows", "PromptHelper.exe");
            if (File.Exists(preferred))
            {
                return preferred;
            }
        }

        string[] candidates = Directory.Exists(binRoot)
            ? Directory.GetFiles(binRoot, "PromptHelper.exe", SearchOption.AllDirectories)
            : [];

        Assert.IsTrue(
            candidates.Length > 0,
            $"No built PromptHelper.exe found under '{binRoot}'. Build the main project before running the " +
            "release-asset tests; their whole purpose is to validate that executable, so an absent one is a " +
            "failure rather than a reason to skip.");

        return candidates
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .First();
    }
}
