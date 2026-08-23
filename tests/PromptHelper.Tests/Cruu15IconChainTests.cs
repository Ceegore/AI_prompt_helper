using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PromptHelper.Tests;

/// <summary>
/// CRUU15-011: the icon chain is reproducible end to end — approved SVG, through a pinned
/// renderer, to approved pixels, to the committed ICO, to every icon group in the executable —
/// and the strict version of that check is mandatory on the release path rather than opt-in.
/// </summary>
[TestClass]
public sealed class Cruu15IconChainTests
{
    private static string GeneratorPackageDir =>
        Path.Combine(RepositoryTestPaths.Root, "tools", "icon-generator");

    private static string SvgPath => RepositoryTestPaths.RequireFile(
        "src", "PromptHelper", "Assets", "PromptHelperLogo.svg");

    private static string IcoPath => RepositoryTestPaths.RequireFile(
        "src", "PromptHelper", "Assets", "PromptHelper.ico");

    private static string ManifestPath => RepositoryTestPaths.RequireFile(
        "src", "PromptHelper", "Assets", "PromptHelperIcon.approved.json");

    [TestMethod]
    public void CRUU15_011_Fresh_checkout_can_run_pinned_canonical_icon_generator()
    {
        string packageJsonPath = RepositoryTestPaths.RequireFile("tools", "icon-generator", "package.json");
        RepositoryTestPaths.RequireFile("tools", "icon-generator", "package-lock.json");

        using JsonDocument package = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        string version = package.RootElement
            .GetProperty("dependencies")
            .GetProperty("sharp")
            .GetString()!;

        // An exact version, not a range: "^0.34.4" would let a different renderer produce
        // different pixels from the same approved vector source, which is precisely what the
        // approval manifest exists to detect and what a floating pin would make routine.
        Assert.IsFalse(version.StartsWith('^') || version.StartsWith('~') || version.Contains('*'),
            $"The renderer must be pinned to an exact version, found '{version}'.");
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"),
            $"The renderer pin must be a concrete version, found '{version}'.");

        // The generator resolves the renderer only from the pinned package, so an ambient
        // global install cannot silently satisfy it.
        string generatorSource = File.ReadAllText(
            RepositoryTestPaths.RequireFile("tools", "GenerateAppIconNative.js"));
        StringAssert.Contains(generatorSource, "PINNED_MODULES");
        StringAssert.Contains(generatorSource, "icon-generator");
        Assert.IsFalse(generatorSource.Contains("require(\"sharp\")", StringComparison.Ordinal),
            "The generator must not fall back to ambient module resolution for the renderer.");

        EnsurePinnedRendererInstalled();
        Assert.IsTrue(Directory.Exists(Path.Combine(GeneratorPackageDir, "node_modules", "sharp")),
            "The pinned renderer must be installable from the checked-in lockfile alone.");
    }

    [TestMethod]
    public void CRUU15_011_Generated_native_frames_match_approved_RGBA_hashes()
    {
        EnsurePinnedRendererInstalled();

        using var temp = new TestDirectory();
        string generatedIco = Path.Combine(temp.Root, "generated.ico");

        var psi = new ProcessStartInfo("node")
        {
            WorkingDirectory = RepositoryTestPaths.Root,
            ArgumentList =
            {
                Path.Combine(RepositoryTestPaths.Root, "tools", "GenerateAppIconNative.js"),
                SvgPath,
                generatedIco
            }
        };

        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 300_000);
        Assert.IsTrue(run.Exited, "The canonical icon generator timed out.");
        Assert.AreEqual(0, run.ExitCode, $"The canonical icon generator failed.\n{run.CombinedOutput}");
        Assert.IsTrue(File.Exists(generatedIco), "The generator produced no ICO.");

        IconApprovalManifest approved = IconApprovalManifest.Load(ManifestPath);
        Assert.AreEqual(approved.SvgSha256Hex, IconApprovalManifest.ComputeSvgHash(SvgPath),
            "The approval manifest is not bound to the SVG that was just rendered.");

        Dictionary<int, byte[]> generated =
            IconApprovalManifest.ReadIcoFramePayloads(File.ReadAllBytes(generatedIco));

        // The comparison is on normalized pixels, not PNG bytes: two encoders can emit
        // different containers for identical images, and it is the image that was approved.
        foreach (IconApprovedFrame frame in approved.Frames)
        {
            Assert.IsTrue(generated.ContainsKey(frame.Size),
                $"The generator did not render the approved {frame.Size}px frame.");
            Assert.AreEqual(
                frame.NormalizedRgbaSha256Hex,
                IconApprovalManifest.ComputeNormalizedRgbaHash(generated[frame.Size]),
                $"Rendering the approved SVG produced different {frame.Size}px pixels than the approval manifest records. " +
                "Either the renderer pin drifted or the artwork changed without re-approval.");
        }

        // And the checked-in ICO is the same approved image, so the committed artefact is
        // reproducible from source rather than merely self-consistent.
        Dictionary<int, byte[]> committed =
            IconApprovalManifest.ReadIcoFramePayloads(File.ReadAllBytes(IcoPath));

        foreach (IconApprovedFrame frame in approved.Frames)
        {
            Assert.IsTrue(committed.ContainsKey(frame.Size));
            Assert.AreEqual(
                IconApprovalManifest.ComputeNormalizedRgbaHash(generated[frame.Size]),
                IconApprovalManifest.ComputeNormalizedRgbaHash(committed[frame.Size]),
                $"The committed ICO's {frame.Size}px frame is not what the pinned generator produces.");
        }
    }

    [TestMethod]
    public void CRUU15_011_Deleting_required_ICO_fails_required_icon_test()
    {
        // The strict release check must fail when the artefact it validates is absent. A check
        // that reports success (or skips) for a missing icon is how an icon-less build ships.
        using var temp = new TestDirectory();
        string repo = Path.Combine(temp.Root, "repo");
        Directory.CreateDirectory(Path.Combine(repo, "tools"));
        Directory.CreateDirectory(Path.Combine(repo, "src", "PromptHelper", "Assets"));

        File.Copy(
            RepositoryTestPaths.RequireFile("tools", "VerifyReleaseAssets.ps1"),
            Path.Combine(repo, "tools", "VerifyReleaseAssets.ps1"));

        // Everything except the ICO is present.
        File.Copy(SvgPath, Path.Combine(repo, "src", "PromptHelper", "Assets", "PromptHelperLogo.svg"));
        File.Copy(ManifestPath, Path.Combine(repo, "src", "PromptHelper", "Assets", "PromptHelperIcon.approved.json"));

        ProcessStartInfo psi = PowerShellHost.CreateStartInfo(
            "-File",
            Path.Combine(repo, "tools", "VerifyReleaseAssets.ps1"),
            "-RequireIcon");

        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 120_000);

        Assert.IsTrue(run.Exited, "VerifyReleaseAssets.ps1 timed out.");
        Assert.AreNotEqual(0, run.ExitCode,
            $"A missing required ICO must fail the strict release check.\n{run.CombinedOutput}");
    }

    [TestMethod]
    public void CRUU15_011_Release_tag_path_requires_strict_icon_verification()
    {
        string releaseWorkflow = File.ReadAllText(
            RepositoryTestPaths.RequireFile(".github", "workflows", "release.yml"));

        // Triggered by an actual release, not by someone remembering to tick a box.
        StringAssert.Contains(releaseWorkflow, "tags:");
        Assert.IsFalse(releaseWorkflow.Contains("release_gate", StringComparison.Ordinal),
            "The release path must not depend on an opt-in workflow_dispatch input.");

        // And it runs the whole strict chain, including against the published executable.
        StringAssert.Contains(releaseWorkflow, "-RequireIcon");
        StringAssert.Contains(releaseWorkflow, "-PublishedExe");
        StringAssert.Contains(releaseWorkflow, "VerifyIconGeneration.ps1");
        StringAssert.Contains(releaseWorkflow, "VerifyFindingCoverage.ps1");

        // The ordinary CI path runs the generation check too, so drift is caught on every push
        // rather than only at release time.
        string ci = File.ReadAllText(
            RepositoryTestPaths.RequireFile(".github", "workflows", "windows-ci.yml"));
        StringAssert.Contains(ci, "VerifyIconGeneration.ps1");
        StringAssert.Contains(ci, "VerifyFindingCoverage.ps1");
    }

    [TestMethod]
    public void CRUU15_011_All_EXE_icon_groups_have_no_unapproved_required_frame_content()
    {
        string exePath = RepositoryTestPaths.RequireBuiltApplicationExe();

        // The comparison runs through the real release tool, which enumerates every
        // RT_GROUP_ICON in the executable rather than stopping at the first one — an
        // unapproved frame in a later group is exactly what a first-group-only reader misses.
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepositoryTestPaths.Root,
            ArgumentList =
            {
                "run",
                "--project",
                Path.Combine(RepositoryTestPaths.Root, "tools", "IconIdentityVerifier", "IconIdentityVerifier.csproj"),
                "--",
                "compare-exe",
                IcoPath,
                exePath
            }
        };

        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 300_000);

        Assert.IsTrue(run.Exited, "The icon identity verifier timed out.");
        Assert.AreEqual(0, run.ExitCode,
            "Every icon group in the executable must match the approved frames. " + run.CombinedOutput);

        // It must actually have found groups to compare; a verifier that silently examined
        // nothing would also exit zero.
        StringAssert.Contains(run.StandardOutput, "icon group");
        Assert.IsFalse(run.StandardOutput.Contains("Found 0 icon group(s)", StringComparison.Ordinal),
            "The executable carries no icon groups at all.");

        // And the approved manifest is the same authority the ICO was compared against.
        IconApprovalManifest approved = IconApprovalManifest.Load(ManifestPath);
        Dictionary<int, byte[]> committed =
            IconApprovalManifest.ReadIcoFramePayloads(File.ReadAllBytes(IcoPath));

        foreach (IconApprovedFrame frame in approved.Frames)
        {
            Assert.IsTrue(committed.ContainsKey(frame.Size));
            Assert.AreEqual(
                frame.NormalizedRgbaSha256Hex,
                IconApprovalManifest.ComputeNormalizedRgbaHash(committed[frame.Size]));
        }
    }

    /// <summary>
    /// Installs the pinned renderer from the checked-in lockfile if it is not present. This is
    /// what "a fresh checkout can run the canonical generator" means, so the test performs it
    /// rather than assuming a pre-provisioned machine.
    /// </summary>
    private static void EnsurePinnedRendererInstalled()
    {
        if (Directory.Exists(Path.Combine(GeneratorPackageDir, "node_modules", "sharp")))
        {
            return;
        }

        var psi = new ProcessStartInfo("npm.cmd")
        {
            WorkingDirectory = GeneratorPackageDir,
            ArgumentList = { "ci", "--no-audit", "--no-fund" }
        };

        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 420_000);

        Assert.IsTrue(run.Exited, "npm ci timed out installing the pinned icon renderer.");
        Assert.AreEqual(0, run.ExitCode,
            $"npm ci failed for tools/icon-generator; the pinned toolchain must install from its lockfile.\n{run.CombinedOutput}");
    }
}
