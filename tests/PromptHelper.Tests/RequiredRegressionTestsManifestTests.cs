using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PromptHelper.Tests;

/// <summary>
/// CRUU14-011: the required-sentinel manifest (tools/RequiredRegressionTests.psd1) is itself
/// tested — a required name that no longer corresponds to an actual [TestMethod] (renamed,
/// deleted, or typo'd) would otherwise let the exact-name evidence gate in CI silently stop
/// covering that behavior while still reporting green, since VerifyTestEvidence.ps1 only
/// checks the TRX that was actually produced by whatever tests happened to run.
/// </summary>
[TestClass]
public sealed class RequiredRegressionTestsManifestTests
{
    private static IReadOnlyList<string> LoadRequiredNames()
    {
        string psd1Path = RepositoryTestPaths.RequireFile("tools", "RequiredRegressionTests.psd1");

        // Parsed in-process rather than by shelling out to Import-PowerShellDataFile: that
        // cmdlet is missing from the Windows PowerShell on GitHub's hosted runners, so this
        // test used to fail in CI for a reason unrelated to the manifest's contents. The
        // manifest's grammar is a list of single-quoted identifiers, which needs no shell.
        string manifestText = File.ReadAllText(psd1Path);

        var names = new List<string>();
        foreach (Match match in Regex.Matches(manifestText, "'([A-Za-z0-9_]+)'"))
        {
            names.Add(match.Groups[1].Value);
        }

        Assert.IsTrue(names.Count > 0,
            $"No sentinel names could be parsed from '{psd1Path}'.");

        return names;
    }

    private static HashSet<string> LoadAllTestMethodNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Type type in Assembly.GetExecutingAssembly().GetTypes())
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.GetCustomAttributes(typeof(TestMethodAttribute), inherit: true).Length > 0)
                {
                    names.Add(method.Name);
                }
            }
        }

        return names;
    }

    [TestMethod]
    public void CRUU14_011_Required_manifest_contains_all_CRUU12_CRUU13_CRUU14_sentinels()
    {
        IReadOnlyList<string> required = LoadRequiredNames();
        Assert.IsTrue(required.Count > 0, "Required-sentinel manifest must not be empty.");

        HashSet<string> actualTestNames = LoadAllTestMethodNames();

        List<string> missing = required
            .Where(name => !actualTestNames.Contains(name))
            .ToList();

        Assert.IsTrue(missing.Count == 0,
            "The required-sentinel manifest names a test method that no longer exists in the " +
            "compiled test assembly (renamed, deleted, or typo'd): " + string.Join(", ", missing));
    }
}
