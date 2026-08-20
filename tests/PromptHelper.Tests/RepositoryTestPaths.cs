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
}
