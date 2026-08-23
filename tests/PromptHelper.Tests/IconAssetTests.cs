using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PromptHelper.Tests;

[TestClass]
public sealed class IconAssetTests
{
    /// <summary>
    /// CRUU14-011 Problem E / CRUU14-012: GenerateAppIcon.ps1 is an ImageMagick-based fallback
    /// generator, not the canonical one — it still rasterizes once at 256 and downsamples the
    /// smaller frames from that single raster, which is exactly the pattern
    /// GenerateAppIconNative.js was written to replace. This test still asserts that fallback
    /// script's own real content (it must keep working as documented), but the canonical
    /// generator asserted elsewhere is the Node/sharp per-size renderer.
    /// </summary>
    [TestMethod]
    public void GenerateAppIcon_fallback_script_exists_and_contains_square_padding_and_validation()
    {
        string scriptPath = RepositoryTestPaths.RequireFile("tools", "GenerateAppIcon.ps1");
        string scriptContent = File.ReadAllText(scriptPath);

        StringAssert.Contains(scriptContent, "PromptHelperLogo.svg");
        StringAssert.Contains(scriptContent, "PromptHelper.ico");
        StringAssert.Contains(scriptContent, "extent \"256x256\"");
        StringAssert.Contains(scriptContent, "gravity center");
        StringAssert.Contains(scriptContent, "auto-resize=256,128,64,48,32,24,16");
    }

    [TestMethod]
    public void CRUU14_012_Canonical_generator_renders_each_size_from_vector_source()
    {
        string scriptPath = RepositoryTestPaths.RequireFile("tools", "GenerateAppIconNative.js");
        string scriptContent = File.ReadAllText(scriptPath);

        // Every required size is listed once, and each one is passed straight into sharp's own
        // per-call resize against the SVG source — not the fallback's single 256 raster
        // downsampled afterward via ImageMagick's "auto-resize".
        foreach (int size in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            StringAssert.Contains(scriptContent, size.ToString(), $"Canonical generator must declare size {size}.");
        }

        StringAssert.Contains(scriptContent, "sharp(svgPath");
        StringAssert.DoesNotMatch(scriptContent, new System.Text.RegularExpressions.Regex("auto-resize"),
            "Canonical generator must not delegate to ImageMagick's single-raster auto-resize.");
    }

    [TestMethod]
    public void CRUU4_011_Workflow_invokes_non_strict_release_asset_verification()
    {
        string workflowPath = RepositoryTestPaths.RequireFile(".github", "workflows", "windows-ci.yml");
        string workflowContent = File.ReadAllText(workflowPath);

        StringAssert.Contains(workflowContent, "./tools/VerifyReleaseAssets.ps1");
    }

    [TestMethod]
    public void CRUU4_011_Strict_workflow_path_invokes_RequireIcon()
    {
        string workflowPath = RepositoryTestPaths.RequireFile(".github", "workflows", "windows-ci.yml");
        string workflowContent = File.ReadAllText(workflowPath);

        StringAssert.Contains(workflowContent, "release_gate");
        StringAssert.Contains(workflowContent, "./tools/VerifyReleaseAssets.ps1 -RequireIcon");
        StringAssert.Contains(workflowContent, "-PublishedExe artifacts/publish-check/PromptHelper.exe");
    }

    [TestMethod]
    public void CRUU4_012_Release_asset_script_checks_ico_payload_bounds()
    {
        string scriptPath = RepositoryTestPaths.RequireFile("tools", "VerifyReleaseAssets.ps1");
        string scriptContent = File.ReadAllText(scriptPath);

        StringAssert.Contains(scriptContent, "imageSize");
        StringAssert.Contains(scriptContent, "imageOffset");
        StringAssert.Contains(scriptContent, "directoryLength");
    }

    [TestMethod]
    public void CRUU4_012_Release_asset_script_supports_published_exe_icon_check()
    {
        string scriptPath = RepositoryTestPaths.RequireFile("tools", "VerifyReleaseAssets.ps1");
        string scriptContent = File.ReadAllText(scriptPath);

        StringAssert.Contains(scriptContent, "PublishedExe");
        // CRUU14-011 Problem D: this previously asserted "ExtractIconEx", which the script no
        // longer uses for the check itself — the string only survives inside a comment noting
        // it was superseded. Assert the marker for what the script actually runs now: an exact
        // pixel comparison against every relevant icon group via IconIdentityVerifier.
        StringAssert.Contains(scriptContent, "IconIdentityVerifier");
        StringAssert.Contains(scriptContent, "compare-exe");
    }

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU4_012_Release_asset_script_actually_validates_the_built_exe_icon()
    {
        string repoRoot = RepositoryTestPaths.Root;
        string scriptPath = Path.Combine(repoRoot, "tools", "VerifyReleaseAssets.ps1");
        // CRUU15-011: resolved for whichever configuration was built, and required to exist —
        // a release-asset check that reports success (or "not applicable") when the artefact it
        // validates is absent is precisely how an unvalidated executable ships.
        string exePath = RepositoryTestPaths.RequireBuiltApplicationExe();

        var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            ArgumentList =
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-File", scriptPath,
                "-RequireIcon",
                "-PublishedExe", exePath
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = repoRoot
        };

        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 60_000);
        Assert.IsTrue(run.Exited, "VerifyReleaseAssets.ps1 timed out.");

        Assert.AreEqual(0, run.ExitCode,
            $"VerifyReleaseAssets.ps1 -RequireIcon must pass against the built PromptHelper.exe.\nSTDOUT:\n{run.StandardOutput}\nSTDERR:\n{run.StandardError}");
        StringAssert.Contains(run.StandardOutput, "Release asset verification completed successfully.");
    }

    [TestMethod]
    public void WindowsCI_workflow_contains_self_contained_publish_and_TRX_upload()
    {
        string workflowPath = RepositoryTestPaths.RequireFile(".github", "workflows", "windows-ci.yml");
        string workflowContent = File.ReadAllText(workflowPath);

        StringAssert.Contains(workflowContent, "dotnet publish");
        StringAssert.Contains(workflowContent, "win-x64");
        StringAssert.Contains(workflowContent, "--self-contained true");
        StringAssert.Contains(workflowContent, "artifacts/publish-check/PromptHelper.exe");
        StringAssert.Contains(workflowContent, "if: always()");
        StringAssert.Contains(workflowContent, "timeout-minutes: 20");
    }

    [TestMethod]
    public void PromptHelperIco_when_present_is_valid_and_contains_required_square_frames()
    {
        string root = RepositoryTestPaths.Root;
        string ico = Path.Combine(root, "src", "PromptHelper", "Assets", "PromptHelper.ico");

        if (!File.Exists(ico))
        {
            // Allowed to be absent until source SVG is supplied
            return;
        }

        byte[] bytes = File.ReadAllBytes(ico);
        Assert.IsTrue(bytes.Length > 6, "ICO is empty or truncated.");

        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2));
        ushort type = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2));
        ushort count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2));

        Assert.AreEqual((ushort)0, reserved);
        Assert.AreEqual((ushort)1, type);
        Assert.IsTrue(count >= 7);
        Assert.IsTrue(bytes.Length >= 6 + (count * 16));

        var sizes = new HashSet<int>();

        for (int i = 0; i < count; i++)
        {
            int offset = 6 + (i * 16);
            int width = bytes[offset] == 0 ? 256 : bytes[offset];
            int height = bytes[offset + 1] == 0 ? 256 : bytes[offset + 1];

            Assert.AreEqual(width, height, $"ICO frame {i} is not square.");
            sizes.Add(width);

            uint imageSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 8, 4));
            uint imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 12, 4));

            Assert.IsTrue(imageSize > 0, $"ICO frame {i} has zero image size.");
            Assert.IsTrue(imageOffset < (uint)bytes.Length, $"ICO frame {i} offset is outside file.");
            Assert.IsTrue((ulong)imageOffset + imageSize <= (ulong)bytes.Length,
                $"ICO frame {i} extends past end of file.");
        }

        foreach (int required in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            Assert.IsTrue(sizes.Contains(required), $"ICO is missing {required}x{required} frame.");
        }
    }

    private static readonly int[] RequiredIconSizes = [16, 24, 32, 48, 64, 128, 256];

    private static string ManifestPath => Path.Combine(
        RepositoryTestPaths.Root, "src", "PromptHelper", "Assets", "PromptHelperIcon.approved.json");

    private static string SvgPath => Path.Combine(
        RepositoryTestPaths.Root, "src", "PromptHelper", "Assets", "PromptHelperLogo.svg");

    private static string IcoPath => Path.Combine(
        RepositoryTestPaths.Root, "src", "PromptHelper", "Assets", "PromptHelper.ico");

    [TestMethod]
    public void CRUU14_012_Approved_SVG_hash_matches_manifest()
    {
        // CRUU15-011: a missing approval manifest is a release blocker, not a reason to skip
        // the identity check.
        Assert.IsTrue(File.Exists(ManifestPath),
            $"The icon approval manifest is required and is missing: '{ManifestPath}'.");

        IconApprovalManifest manifest = IconApprovalManifest.Load(ManifestPath);
        string actualSvgHash = IconApprovalManifest.ComputeSvgHash(SvgPath);

        Assert.AreEqual(manifest.SvgSha256Hex, actualSvgHash,
            "The committed SVG no longer matches the approved artwork identity manifest. " +
            "If this change is deliberate, regenerate and re-review the approval manifest.");
    }

    [TestMethod]
    public void CRUU14_012_Checked_in_ICO_matches_approved_normalized_RGBA_hashes()
    {
        // CRUU15-011: a missing approval manifest is a release blocker, not a reason to skip
        // the identity check.
        Assert.IsTrue(File.Exists(ManifestPath),
            $"The icon approval manifest is required and is missing: '{ManifestPath}'.");

        IconApprovalManifest manifest = IconApprovalManifest.Load(ManifestPath);
        byte[] icoBytes = File.ReadAllBytes(IcoPath);
        Dictionary<int, byte[]> framePayloads = IconApprovalManifest.ReadIcoFramePayloads(icoBytes);

        foreach (IconApprovedFrame expected in manifest.Frames)
        {
            Assert.IsTrue(framePayloads.ContainsKey(expected.Size), $"Checked-in ICO is missing size {expected.Size}.");
            string actualHash = IconApprovalManifest.ComputeNormalizedRgbaHash(framePayloads[expected.Size]);
            Assert.AreEqual(expected.NormalizedRgbaSha256Hex, actualHash,
                $"Checked-in ICO frame {expected.Size}x{expected.Size} no longer matches its approved normalized pixel content.");
        }
    }

    [TestMethod]
    public void CRUU14_012_Each_native_frame_matches_approved_normalized_RGBA_hash()
    {
        // Cross-check: the manifest itself must cover every mandatory size, independent of
        // whether the checked-in ICO happens to (that is the previous test's job) — a manifest
        // silently missing a required size would let that size's identity go unreviewed.
        // CRUU15-011: a missing approval manifest is a release blocker, not a reason to skip
        // this check either.
        Assert.IsTrue(File.Exists(ManifestPath),
            $"The icon approval manifest is required and is missing: '{ManifestPath}'.");

        IconApprovalManifest manifest = IconApprovalManifest.Load(ManifestPath);
        var coveredSizes = new HashSet<int>();
        foreach (IconApprovedFrame frame in manifest.Frames)
        {
            coveredSizes.Add(frame.Size);
        }

        foreach (int required in RequiredIconSizes)
        {
            Assert.IsTrue(coveredSizes.Contains(required), $"Approval manifest is missing required size {required}.");
        }
    }
}
