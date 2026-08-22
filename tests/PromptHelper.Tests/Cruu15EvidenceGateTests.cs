using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PromptHelper.Tests;

/// <summary>
/// CRUU15-009: the regression evidence gate proves that every audited finding is covered
/// behaviorally, and the TRX verifier is proved by execution rather than by reading its source.
/// </summary>
[TestClass]
public sealed class Cruu15EvidenceGateTests
{
    private static string EvidenceScriptPath =>
        RepositoryTestPaths.RequireFile("tools", "VerifyTestEvidence.ps1");

    private static string CoverageScriptPath =>
        RepositoryTestPaths.RequireFile("tools", "VerifyFindingCoverage.ps1");

    private static ProcessRunResult RunPowerShell(params string[] arguments)
    {
        var psi = new ProcessStartInfo("powershell.exe");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        return ProcessTestRunner.Run(psi, timeoutMilliseconds: 120_000);
    }

    private static string WriteTrx(string directory, string resultsXml, int total, int passed, int failed)
    {
        string path = Path.Combine(directory, $"fixture-{Guid.NewGuid():N}.trx");
        File.WriteAllText(path,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun id="1" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <ResultSummary outcome="Completed">
                <Counters total="{total}" passed="{passed}" failed="{failed}" error="0" timeout="0" aborted="0" />
              </ResultSummary>
              <Results>
            {resultsXml}
              </Results>
            </TestRun>
            """);
        return path;
    }

    // ==========================================
    // Completeness of the coverage authority
    // ==========================================

    [TestMethod]
    public void CRUU15_009_Canonical_finding_coverage_map_contains_every_CRUU12_through_CRUU15_required_ID()
    {
        FindingCoverageMap map = FindingCoverageMap.Load();

        IReadOnlySet<string> required = map.ReadRequiredFindingIdsFromReports();
        Assert.IsTrue(required.Count >= 77,
            $"The audit reports should name at least the 77 CRUU12-CRUU15 findings; found {required.Count}.");

        List<string> uncovered = required
            .Where(id => !map.Findings.TryGetValue(id, out IReadOnlyList<string>? tests) || tests.Count == 0)
            .ToList();

        Assert.AreEqual(0, uncovered.Count,
            "Findings with no behavioral regression coverage: " + string.Join(", ", uncovered));

        // Every mapped test must be a real, compiled test method: a map that names tests which
        // do not exist proves nothing either.
        HashSet<string> compiled = LoadAllTestMethodNames();
        List<string> phantom = map.Findings
            .SelectMany(entry => entry.Value)
            .Distinct()
            .Where(name => !compiled.Contains(name))
            .ToList();

        Assert.AreEqual(0, phantom.Count,
            "The coverage map names tests that do not exist: " + string.Join(", ", phantom));
    }

    [TestMethod]
    public void CRUU15_009_Removing_required_ID_from_manifest_fails_coverage_gate()
    {
        // Deleting a finding from the coverage map must fail the gate. The old manifest could
        // not detect this: it only checked that names still present in it resolved to tests.
        using var temp = new TestDirectory();
        string workingMap = Path.Combine(temp.Root, "FindingCoverageMap.json");

        string original = File.ReadAllText(FindingCoverageMap.MapPath);
        FindingCoverageMap map = FindingCoverageMap.Parse(original);
        string victim = map.ReadRequiredFindingIdsFromReports().First(id => id.StartsWith("CRUU15-", StringComparison.Ordinal));

        // Remove exactly one required finding from a copy of the map.
        var reduced = map.Findings
            .Where(entry => entry.Key != victim)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        WriteMapCopy(workingMap, map.GatedReports, reduced);

        ProcessRunResult intact = RunPowerShell("-File", CoverageScriptPath);
        Assert.IsTrue(intact.Exited);
        Assert.AreEqual(0, intact.ExitCode, $"The real coverage map must pass the gate.\n{intact.CombinedOutput}");

        ProcessRunResult reducedRun = RunCoverageGateAgainst(workingMap);
        Assert.IsTrue(reducedRun.Exited);
        Assert.AreNotEqual(0, reducedRun.ExitCode,
            "Removing a required finding from the coverage map must fail the gate.");
        StringAssert.Contains(reducedRun.CombinedOutput, victim);
    }

    /// <summary>
    /// Runs the real gate script against a substituted coverage map, by copying the script and
    /// the reports into a scratch repository layout. The script under test is byte-identical to
    /// the one CI runs.
    /// </summary>
    private static ProcessRunResult RunCoverageGateAgainst(string mapPath)
    {
        string scratchRoot = Path.GetDirectoryName(mapPath)!;
        string toolsDir = Path.Combine(scratchRoot, "repo", "tools");
        Directory.CreateDirectory(toolsDir);

        File.Copy(CoverageScriptPath, Path.Combine(toolsDir, "VerifyFindingCoverage.ps1"), overwrite: true);
        File.Copy(mapPath, Path.Combine(toolsDir, "FindingCoverageMap.json"), overwrite: true);
        File.Copy(
            RepositoryTestPaths.RequireFile("tools", "RequiredRegressionTests.psd1"),
            Path.Combine(toolsDir, "RequiredRegressionTests.psd1"),
            overwrite: true);

        FindingCoverageMap map = FindingCoverageMap.Load();
        foreach (KeyValuePair<string, string> report in map.GatedReports)
        {
            File.Copy(
                Path.Combine(RepositoryTestPaths.Root, report.Value),
                Path.Combine(scratchRoot, "repo", report.Value),
                overwrite: true);
        }

        return RunPowerShell("-File", Path.Combine(toolsDir, "VerifyFindingCoverage.ps1"));
    }

    private static void WriteMapCopy(
        string path,
        IReadOnlyDictionary<string, string> reports,
        IReadOnlyDictionary<string, IReadOnlyList<string>> findings)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("{");
        builder.AppendLine("  \"schemaVersion\": 1,");
        builder.AppendLine("  \"gatedReports\": {");
        builder.AppendLine(string.Join(",\n", reports.Select(r => $"    \"{r.Key}\": \"{r.Value}\"")));
        builder.AppendLine("  },");
        builder.AppendLine("  \"findings\": {");
        builder.AppendLine(string.Join(",\n", findings.Select(f =>
            $"    \"{f.Key}\": [{string.Join(", ", f.Value.Select(t => $"\"{t}\""))}]")));
        builder.AppendLine("  }");
        builder.AppendLine("}");

        File.WriteAllText(path, builder.ToString());
    }

    // ==========================================
    // The TRX verifier, proved by execution
    // ==========================================

    [TestMethod]
    public void CRUU15_009_Substring_only_TRX_fixture_is_rejected_by_executing_VerifyTestEvidence()
    {
        using var temp = new TestDirectory();

        // A result whose name merely *contains* the required name must not satisfy it.
        string trx = WriteTrx(
            temp.Root,
            """    <UnitTestResult testName="Prefix_CRUU15_009_Required_Sentinel_Suffix" outcome="Passed" />""",
            total: 1, passed: 1, failed: 0);

        ProcessRunResult run = RunPowerShell(
            "-File", EvidenceScriptPath,
            "-TrxPath", trx,
            "-RequiredTests", "CRUU15_009_Required_Sentinel");

        Assert.IsTrue(run.Exited, "VerifyTestEvidence.ps1 timed out.");
        Assert.AreNotEqual(0, run.ExitCode,
            $"A substring-only match must be rejected.\n{run.CombinedOutput}");
    }

    [TestMethod]
    public void CRUU15_009_Missing_required_TRX_fixture_is_rejected_by_executing_VerifyTestEvidence()
    {
        using var temp = new TestDirectory();

        string trx = WriteTrx(
            temp.Root,
            """    <UnitTestResult testName="SomeUnrelatedTest" outcome="Passed" />""",
            total: 1, passed: 1, failed: 0);

        ProcessRunResult run = RunPowerShell(
            "-File", EvidenceScriptPath,
            "-TrxPath", trx,
            "-RequiredTests", "CRUU15_009_Required_Sentinel");

        Assert.IsTrue(run.Exited);
        Assert.AreNotEqual(0, run.ExitCode,
            $"A required test that never executed must be rejected.\n{run.CombinedOutput}");
    }

    [TestMethod]
    public void CRUU15_009_Failed_required_TRX_fixture_is_rejected_by_executing_VerifyTestEvidence()
    {
        using var temp = new TestDirectory();

        string trx = WriteTrx(
            temp.Root,
            """    <UnitTestResult testName="CRUU15_009_Required_Sentinel" outcome="Failed" />""",
            total: 1, passed: 0, failed: 1);

        ProcessRunResult run = RunPowerShell(
            "-File", EvidenceScriptPath,
            "-TrxPath", trx,
            "-RequiredTests", "CRUU15_009_Required_Sentinel");

        Assert.IsTrue(run.Exited);
        Assert.AreNotEqual(0, run.ExitCode,
            $"A required test that failed must be rejected.\n{run.CombinedOutput}");
    }

    [TestMethod]
    public void CRUU15_009_Inconclusive_required_TRX_fixture_is_rejected()
    {
        using var temp = new TestDirectory();

        // The counters say the run was clean; only the per-result outcome reveals that the
        // required sentinel never actually asserted anything.
        string trx = WriteTrx(
            temp.Root,
            """    <UnitTestResult testName="CRUU15_009_Required_Sentinel" outcome="Inconclusive" />""",
            total: 1, passed: 1, failed: 0);

        ProcessRunResult run = RunPowerShell(
            "-File", EvidenceScriptPath,
            "-TrxPath", trx,
            "-RequiredTests", "CRUU15_009_Required_Sentinel");

        Assert.IsTrue(run.Exited);
        Assert.AreNotEqual(0, run.ExitCode,
            $"An inconclusive required test is not evidence of anything.\n{run.CombinedOutput}");
    }

    /// <summary>
    /// No required sentinel may be skipped or inconclusive in this suite's own source: an
    /// acceptance test that opts out under conditions it does not control is not acceptance.
    /// </summary>
    [TestMethod]
    public void CRUU15_009_No_required_sentinel_opts_out_with_Inconclusive()
    {
        FindingCoverageMap map = FindingCoverageMap.Load();
        var requiredNames = map.Findings.SelectMany(entry => entry.Value).ToHashSet(StringComparer.Ordinal);

        var offenders = new List<string>();

        // Assembled at runtime so this scanner's own source line does not match itself.
        string optOutCall = "Assert.Incon" + "clusive(";

        foreach (string file in Directory.GetFiles(
                     Path.Combine(RepositoryTestPaths.Root, "tests", "PromptHelper.Tests"),
                     "*.cs"))
        {
            string[] lines = File.ReadAllLines(file);
            string? currentTest = null;

            foreach (string line in lines)
            {
                System.Text.RegularExpressions.Match declaration =
                    System.Text.RegularExpressions.Regex.Match(line, @"public\s+(?:void|async\s+Task)\s+([A-Za-z0-9_]+)\s*\(");
                if (declaration.Success)
                {
                    currentTest = declaration.Groups[1].Value;
                }

                if (currentTest is not null &&
                    requiredNames.Contains(currentTest) &&
                    line.Contains(optOutCall, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{currentTest}");
                }
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "Required sentinels must never opt out: " + string.Join(", ", offenders));
    }

    private static HashSet<string> LoadAllTestMethodNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (Type type in typeof(Cruu15EvidenceGateTests).Assembly.GetTypes())
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttributes(typeof(TestMethodAttribute), inherit: true).Length > 0)
                {
                    names.Add(method.Name);
                }
            }
        }

        return names;
    }
}
