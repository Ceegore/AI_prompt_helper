using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// CRUU16-008: the coverage map is structurally complete, but structure is not evidence. A
/// finding whose only sentinel reads source text, or exercises a helper the product does not
/// call, is a finding nobody is actually verifying.
/// </summary>
[TestClass]
public sealed class Cruu16EvidenceQualityTests
{
    /// <summary>What a test actually does, inferred from its body.</summary>
    private enum EvidenceKind
    {
        /// <summary>Constructs or calls production types: the behaviour itself is executed.</summary>
        ProductionBehavior,

        /// <summary>Only reads repository files and asserts on their text.</summary>
        SourceTextOnly,

        /// <summary>Neither — reflection over shapes, pure helpers, and so on.</summary>
        Structural
    }

    private static readonly Lazy<IReadOnlySet<string>> ProductionTypeNames = new(() =>
        typeof(AppPaths).Assembly
            .GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("PromptHelper", StringComparison.Ordinal))
            .Select(t => t.Name)
            .Where(n => n.Length > 4 && !n.Contains('<'))
            .ToHashSet(StringComparer.Ordinal));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> TestBodies = new(LoadTestBodies);

    /// <summary>
    /// Crudely but reliably splits each test file into method bodies. Exact parsing is not
    /// needed: the question is only whether a body mentions production types at all.
    /// </summary>
    private static IReadOnlyDictionary<string, string> LoadTestBodies()
    {
        var bodies = new Dictionary<string, string>(StringComparer.Ordinal);
        string dir = Path.Combine(RepositoryTestPaths.Root, "tests", "PromptHelper.Tests");

        foreach (string file in Directory.GetFiles(dir, "*.cs"))
        {
            string text = File.ReadAllText(file);
            MatchCollection matches = Regex.Matches(text, @"public\s+(?:void|async\s+Task)\s+([A-Za-z0-9_]+)\s*\(");

            for (int i = 0; i < matches.Count; i++)
            {
                int start = matches[i].Index;
                int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
                bodies[matches[i].Groups[1].Value] = text[start..end];
            }
        }

        return bodies;
    }

    private static EvidenceKind Classify(string testName)
    {
        if (!TestBodies.Value.TryGetValue(testName, out string? body))
        {
            return EvidenceKind.Structural;
        }

        return ClassifyBody(body);
    }

    private static EvidenceKind ClassifyBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        bool touchesProduction = ProductionTypeNames.Value.Any(
            name => Regex.IsMatch(body, $@"\b{Regex.Escape(name)}\b"));

        // Executing the repository's own release/verification tooling is production behaviour
        // too. Findings about the evidence gate itself have no PromptHelper.Services surface to
        // touch; what they must do is run the real script, which these tests do.
        bool executesTooling =
            body.Contains("RunPowerShell(", StringComparison.Ordinal) ||
            body.Contains("RunCoverageGateAgainst(", StringComparison.Ordinal) ||
            (body.Contains("ProcessTestRunner.Run", StringComparison.Ordinal) &&
             (body.Contains(".ps1", StringComparison.Ordinal) ||
              body.Contains("ScriptPath", StringComparison.Ordinal)));

        bool readsRepositoryText =
            body.Contains("RepositoryTestPaths.RequireFile", StringComparison.Ordinal) ||
            body.Contains("File.ReadAllText", StringComparison.Ordinal);

        // A type token inside typeof/nameof or a reflection lookup proves only that a symbol
        // exists. Count production behaviour only when the body also constructs a production
        // type or calls a production static method. Instance calls are then reached through an
        // object constructed in the same test body. This intentionally rejects the CRUU16-005
        // false positive that reflected over Rollback's surrounding types without executing it.
        bool constructsProduction = ProductionTypeNames.Value.Any(name =>
            Regex.IsMatch(body, $@"\bnew\s+(?:[A-Za-z0-9_.]+\.)?{Regex.Escape(name)}\s*(?:\(|\{{)"));
        bool callsProductionStatic = ProductionTypeNames.Value.Any(name =>
            Regex.IsMatch(
                body,
                $@"\b{Regex.Escape(name)}\s*\.\s*(?!(?:Assembly|GetMethod|GetProperty|GetField|GetNestedTypes)\b)[A-Za-z_][A-Za-z0-9_]*\s*\("));
        var productionReceivers = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in ProductionTypeNames.Value)
        {
            foreach (Match match in Regex.Matches(
                         body,
                         $@"\b(?:[A-Za-z0-9_.]+\.)?{Regex.Escape(name)}\s+([A-Za-z_][A-Za-z0-9_]*)\b"))
            {
                productionReceivers.Add(match.Groups[1].Value);
            }

            foreach (Match match in Regex.Matches(
                         body,
                         $@"\bvar\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+(?:[A-Za-z0-9_.]+\.)?{Regex.Escape(name)}\s*\("))
            {
                productionReceivers.Add(match.Groups[1].Value);
            }
        }

        bool callsProductionInstance = productionReceivers.Any(receiver =>
            Regex.IsMatch(
                body,
                $@"\b{Regex.Escape(receiver)}\s*\.\s*(?!(?:GetMethod|GetProperty|GetField|GetNestedTypes|GetType)\b)[A-Za-z_][A-Za-z0-9_]*\s*\("));

        bool reflectionOrMentionOnly = touchesProduction &&
            !constructsProduction &&
            !callsProductionStatic &&
            !callsProductionInstance;

        if (executesTooling || (touchesProduction && !reflectionOrMentionOnly))
        {
            return EvidenceKind.ProductionBehavior;
        }

        return readsRepositoryText ? EvidenceKind.SourceTextOnly : EvidenceKind.Structural;
    }

    [TestMethod]
    public void CRUU17_008_Reflection_only_test_is_not_classified_ProductionBehavior()
    {
        string body = "typeof(DataFolderMigrationService).GetNestedTypes().Single().GetMethod(\"Rollback\");";
        Assert.AreEqual(EvidenceKind.Structural, ClassifyBody(body));
    }

    [TestMethod]
    public void CRUU17_008_Type_name_mention_alone_is_not_production_execution()
    {
        Assert.AreEqual(
            EvidenceKind.Structural,
            ClassifyBody("string mentioned = nameof(DataFolderMigrationService);"));
    }

    [TestMethod]
    public void CRUU17_008_Source_or_reflection_only_sentinel_cannot_satisfy_high_risk_acceptance()
    {
        Assert.AreNotEqual(
            EvidenceKind.ProductionBehavior,
            ClassifyBody("File.ReadAllText(path); typeof(IVerifiedArtifactDeleter).GetMethod(\"VerifyAndDelete\");"));
    }

    [TestMethod]
    public void CRUU18_007_Nameof_production_type_plus_fake_instance_call_is_not_ProductionBehavior()
    {
        Assert.AreNotEqual(
            EvidenceKind.ProductionBehavior,
            ClassifyBody("string marker = nameof(DataFolderMigrationService); fake.DoWork();"));
    }

    [TestMethod]
    public void CRUU18_007_Reflection_Invoke_without_mapped_production_hit_is_not_ProductionBehavior()
    {
        Assert.AreNotEqual(
            EvidenceKind.ProductionBehavior,
            ClassifyBody(
                "var method = typeof(MigrationRecoveryService).GetMethod(\"RecoverForRetry\"); " +
                "method!.Invoke(null, Array.Empty<object>());"));
    }

    /// <summary>
    /// The findings the audit reports themselves mark CRITICAL or HIGH. Derived from the
    /// reports rather than from a list in this repository, for the same reason the coverage
    /// gate is: a severity nobody can quietly downgrade.
    /// </summary>
    private static IReadOnlyDictionary<string, string> LoadHighRiskFindings()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        FindingCoverageMap map = FindingCoverageMap.Load();

        foreach (KeyValuePair<string, string> report in map.GatedReports)
        {
            string text = File.ReadAllText(Path.Combine(RepositoryTestPaths.Root, report.Value));

            foreach (Match match in Regex.Matches(
                         text,
                         $@"({report.Key}-\d{{3}})\s*[|—\-]{{1,3}}\s*\**\s*(CRITICAL|HIGH)\b",
                         RegexOptions.IgnoreCase))
            {
                result[match.Groups[1].Value] = match.Groups[2].Value.ToUpperInvariant();
            }
        }

        return result;
    }

    [TestMethod]
    public void CRUU16_008_High_risk_finding_requires_at_least_one_production_behavior_test()
    {
        FindingCoverageMap map = FindingCoverageMap.Load();
        IReadOnlyDictionary<string, string> highRisk = LoadHighRiskFindings();

        Assert.IsTrue(highRisk.Count >= 20,
            $"The audit reports should mark a substantial number of findings CRITICAL/HIGH; found {highRisk.Count}.");

        var offenders = new List<string>();

        foreach (KeyValuePair<string, string> finding in highRisk)
        {
            if (!map.Findings.TryGetValue(finding.Key, out IReadOnlyList<string>? tests))
            {
                continue; // Completeness is the coverage gate's job, not this one's.
            }

            if (!tests.Any(t => Classify(t) == EvidenceKind.ProductionBehavior))
            {
                offenders.Add($"{finding.Key} ({finding.Value})");
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "High-risk findings whose sentinels never execute production code: " + string.Join(", ", offenders));
    }

    [TestMethod]
    public void CRUU16_008_Source_text_only_test_cannot_be_sole_evidence_for_high_risk_finding()
    {
        FindingCoverageMap map = FindingCoverageMap.Load();
        IReadOnlyDictionary<string, string> highRisk = LoadHighRiskFindings();

        var offenders = new List<string>();

        foreach (string finding in highRisk.Keys)
        {
            if (!map.Findings.TryGetValue(finding, out IReadOnlyList<string>? tests) || tests.Count == 0)
            {
                continue;
            }

            if (tests.All(t => Classify(t) == EvidenceKind.SourceTextOnly))
            {
                offenders.Add(finding);
            }
        }

        Assert.AreEqual(0, offenders.Count,
            "Findings evidenced only by reading source text: " + string.Join(", ", offenders));
    }

    [TestMethod]
    public void CRUU16_008_Helper_only_test_cannot_be_sole_evidence_for_production_wiring_finding()
    {
        // A wiring finding asks whether the product uses a primitive, so its evidence has to
        // run the product. CRUU15-012's original sentinel exercised the helper directly, which
        // stayed green while three real writers still called the unbound factory.
        FindingCoverageMap map = FindingCoverageMap.Load();

        Assert.IsTrue(map.Findings.TryGetValue("CRUU16-007", out IReadOnlyList<string>? wiring));
        Assert.IsTrue(
            wiring!.Any(t => t.Contains("uses_root_bound_stage", StringComparison.Ordinal)),
            "The wiring finding must be evidenced by tests that drive real writers.");

        // And the structural gate those tests rely on must actually be enforced somewhere.
        string servicesDir = Path.Combine(RepositoryTestPaths.Root, "src", "PromptHelper", "Services");
        var unbound = Directory.GetFiles(servicesDir, "*.cs")
            .Where(f => Path.GetFileName(f) != "WindowsOwnedDurableStage.cs")
            .Where(f => File.ReadAllText(f).Contains("WindowsOwnedDurableStage.CreateNew(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.AreEqual(0, unbound.Count,
            "Unbound stage creation survives in: " + string.Join(", ", unbound));
    }

    [TestMethod]
    public void CRUU16_008_CRUU12_017_executes_real_transition_baseline_capture()
    {
        FindingCoverageMap map = FindingCoverageMap.Load();
        Assert.IsTrue(map.Findings.TryGetValue("CRUU12-017", out IReadOnlyList<string>? tests));

        Assert.IsTrue(
            tests!.Any(t => Classify(t) == EvidenceKind.ProductionBehavior),
            "The baseline finding must be evidenced by executing the transition that captures it, " +
            "not by hand-constructing a baseline object.");
    }

    [TestMethod]
    public void CRUU16_008_CRUU15_012_executes_every_real_stage_creator()
    {
        // Every production persistence path, driven end to end, leaving no unbound stage.
        using var temp = new TestDirectory();

        new WindowsDurableAtomicFileWriter().ReplaceDurable(
            Path.Combine(temp.Root, "library.json"),
            System.Text.Encoding.UTF8.GetBytes("a"),
            DurableFileClass.LibraryMetadata);

        new WindowsDurableSettingsFileWriter().WriteDurable(Path.Combine(temp.Root, "settings.json"), "{}");

        new AtomicTextWriter().Write(Path.Combine(temp.Root, "plain.txt"), "b");

        IMigrationFileOps migrationOps = new DefaultMigrationFileOps();
        using (IOwnedFileStage stage = migrationOps.CreateOwnedStage(temp.Root, Path.Combine(temp.Root, "m.tmp")))
        {
            stage.Write(System.Text.Encoding.UTF8.GetBytes("c"));
            stage.FlushDurable();
            stage.PromoteNoOverwriteExact(Path.Combine(temp.Root, "m.out"));
        }

        IMigrationManifestFileOps manifestOps = new DefaultMigrationManifestFileOps();
        using (IOwnedFileStage stage = manifestOps.CreateOwnedStage(temp.Root, Path.Combine(temp.Root, "n.tmp")))
        {
            stage.Write(System.Text.Encoding.UTF8.GetBytes("d"));
            stage.FlushDurable();
            stage.PromoteNoOverwriteExact(Path.Combine(temp.Root, "n.out"));
        }

        Assert.AreEqual("a", File.ReadAllText(Path.Combine(temp.Root, "library.json")));
        Assert.AreEqual("{}", File.ReadAllText(Path.Combine(temp.Root, "settings.json")));
        Assert.AreEqual("b", File.ReadAllText(Path.Combine(temp.Root, "plain.txt")));
        Assert.AreEqual("c", File.ReadAllText(Path.Combine(temp.Root, "m.out")));
        Assert.AreEqual("d", File.ReadAllText(Path.Combine(temp.Root, "n.out")));

        Assert.AreEqual(0, Directory.GetFiles(temp.Root, "*.tmp").Length,
            "Every stage must reach a terminal state.");

        // And none of them reaches for the unbound factory.
        string servicesDir = Path.Combine(RepositoryTestPaths.Root, "src", "PromptHelper", "Services");
        var unbound = Directory.GetFiles(servicesDir, "*.cs")
            .Where(f => Path.GetFileName(f) != "WindowsOwnedDurableStage.cs")
            .Where(f => File.ReadAllText(f).Contains("WindowsOwnedDurableStage.CreateNew(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.AreEqual(0, unbound.Count, "Unbound stage creation survives in: " + string.Join(", ", unbound));
    }
}
