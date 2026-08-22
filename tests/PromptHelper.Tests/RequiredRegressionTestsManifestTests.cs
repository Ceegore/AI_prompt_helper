using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
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

        var psi = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList =
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-Command",
                $"(Import-PowerShellDataFile -Path '{psd1Path}').Required | ConvertTo-Json -Compress"
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process proc = Process.Start(psi)!;
        // Both streams must be drained concurrently with waiting for exit — see the identical
        // note in IconAssetTests; reading one stream to completion before the other risks a
        // pipe-buffer deadlock if the child writes enough to fill the other one first.
        System.Threading.Tasks.Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
        System.Threading.Tasks.Task<string> stderrTask = proc.StandardError.ReadToEndAsync();
        bool exited = proc.WaitForExit(30000);
        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        Assert.IsTrue(exited, "Import-PowerShellDataFile timed out.");
        Assert.AreEqual(0, proc.ExitCode, $"Failed to parse RequiredRegressionTests.psd1.\nSTDERR:\n{stderr}");

        string json = stdout.Trim();
        // A single-element array serializes as a bare string with -Compress; normalize to an array.
        if (!json.StartsWith('['))
        {
            json = $"[{json}]";
        }

        return JsonSerializer.Deserialize<string[]>(json) ?? [];
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
