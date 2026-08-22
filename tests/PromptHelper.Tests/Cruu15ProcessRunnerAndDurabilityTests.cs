using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

/// <summary>
/// CRUU15-010 (a child-process helper whose timeout actually terminates) and CRUU15-012 (the
/// durable-promotion contract, including the rename-metadata barrier).
/// </summary>
[TestClass]
public sealed class Cruu15ProcessRunnerAndDurabilityTests
{
    // ==========================================
    // CRUU15-010: process runner timeout semantics
    // ==========================================

    [TestMethod]
    public void CRUU15_010_Process_runner_large_stdout_and_stderr_does_not_deadlock()
    {
        // Both pipes are filled well past any plausible OS buffer. A helper that drains one
        // stream to completion before starting the other blocks here forever.
        var psi = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList =
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-Command",
                "$line = 'x' * 512; 1..2000 | ForEach-Object { [Console]::Out.WriteLine($line); [Console]::Error.WriteLine($line) }"
            }
        };

        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 120_000);

        Assert.IsTrue(run.Exited, "The child exits on its own; a deadlock would have hit the timeout.");
        Assert.IsFalse(run.WasKilled);
        Assert.AreEqual(0, run.ExitCode);
        Assert.IsTrue(run.StandardOutput.Length > 512 * 1000, $"stdout was truncated: {run.StandardOutput.Length} bytes.");
        Assert.IsTrue(run.StandardError.Length > 512 * 1000, $"stderr was truncated: {run.StandardError.Length} bytes.");
    }

    [TestMethod]
    public void CRUU15_010_Process_runner_hung_child_is_killed_at_timeout()
    {
        var psi = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList =
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-Command", "while ($true) { Start-Sleep -Seconds 30 }"
            }
        };

        var stopwatch = Stopwatch.StartNew();
        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 3_000);
        stopwatch.Stop();

        Assert.IsFalse(run.Exited, "The child never exits on its own.");
        Assert.IsTrue(run.WasKilled, "A child that outlives its timeout must be killed, not merely reported.");
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(60),
            $"The runner must return promptly after its timeout; it took {stopwatch.Elapsed}.");
    }

    [TestMethod]
    public void CRUU15_010_Process_runner_hung_child_with_open_pipes_still_returns()
    {
        // The child writes some output, then hangs forever with both pipes still open. This is
        // the case where the CRUU14 helper deadlocked: WaitForExit(timeout) returned false and
        // the subsequent blocking read on the drain tasks never completed.
        var psi = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList =
            {
                "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-Command",
                "[Console]::Out.WriteLine('before the hang'); [Console]::Error.WriteLine('stderr too'); while ($true) { Start-Sleep -Seconds 30 }"
            }
        };

        var stopwatch = Stopwatch.StartNew();
        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 5_000);
        stopwatch.Stop();

        Assert.IsFalse(run.Exited);
        Assert.IsTrue(run.WasKilled);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(60),
            $"The runner must return promptly after its timeout; it took {stopwatch.Elapsed}.");
        StringAssert.Contains(run.StandardOutput, "before the hang",
            "Output produced before the hang must still be reported.");
    }

    [TestMethod]
    public void CRUU15_010_Process_runner_kills_descendant_process_tree()
    {
        using var temp = new TestDirectory();
        string markerPath = Path.Combine(temp.Root, "descendant-alive.txt");

        // The direct child spawns a grandchild that keeps touching a marker file, then hangs.
        // Killing only the direct child would leave the grandchild running.
        string script =
            "$p = Start-Process powershell.exe -PassThru -WindowStyle Hidden -ArgumentList " +
            "'-NoProfile','-NonInteractive','-Command'," +
            $"\"while (`$true) {{ Set-Content -Path '{markerPath}' -Value (Get-Date).Ticks; Start-Sleep -Milliseconds 100 }}\"; " +
            "[Console]::Out.WriteLine($p.Id); while ($true) { Start-Sleep -Seconds 30 }";

        var psi = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }
        };

        ProcessRunResult run = ProcessTestRunner.Run(psi, timeoutMilliseconds: 5_000);

        Assert.IsFalse(run.Exited);
        Assert.IsTrue(run.WasKilled);

        // Give any surviving grandchild a chance to prove it is still running.
        Assert.IsTrue(SpinUntilMarkerStopsChanging(markerPath),
            "A descendant of the timed-out child was still running after the kill.");
    }

    private static bool SpinUntilMarkerStopsChanging(string markerPath)
    {
        // The grandchild rewrites the marker every 100 ms. Two consecutive one-second samples
        // with identical content mean nothing is writing it any more.
        for (int attempt = 0; attempt < 20; attempt++)
        {
            string first = ReadOrEmpty(markerPath);
            System.Threading.Thread.Sleep(1000);
            string second = ReadOrEmpty(markerPath);

            if (first == second)
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadOrEmpty(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (IOException)
        {
            return Guid.NewGuid().ToString();
        }
    }

    // ==========================================
    // CRUU15-012: durable promotion contract
    // ==========================================

    [TestMethod]
    [TestCategory("WindowsFilesystemIntegration")]
    public void CRUU15_012_Durable_stage_asserts_physical_root_before_promotion()
    {
        using var inside = new TestDirectory();
        using var outside = new TestDirectory();

        // A stage created outside the bound root is refused, and the refusal destroys only the
        // object this call created.
        string escaping = Path.Combine(outside.Root, $".prompthelper-tmp-library-{Guid.NewGuid():N}.tmp");
        Assert.ThrowsExactly<InvalidDataException>(
            () => WindowsOwnedDurableStage.CreateNewUnderRoot(escaping, inside.Root));
        Assert.IsFalse(File.Exists(escaping), "A refused stage must not be left behind.");

        // A stage inside the root passes the same check and promotes normally.
        string legitimate = Path.Combine(inside.Root, $".prompthelper-tmp-library-{Guid.NewGuid():N}.tmp");
        string target = Path.Combine(inside.Root, "library.json");
        using (var stage = WindowsOwnedDurableStage.CreateNewUnderRoot(legitimate, inside.Root))
        {
            stage.Write(Encoding.UTF8.GetBytes("content"));
            stage.FlushDurable();
            stage.PromoteReplaceExact(target);
        }

        Assert.AreEqual("content", File.ReadAllText(target));
    }

    [TestMethod]
    public void CRUU15_012_Durable_promotion_uses_documented_rename_metadata_write_through_contract()
    {
        // The rename that publishes a staged file is issued on the staging handle, so the
        // handle itself has to carry the write-through flag: FlushFileBuffers happens before
        // the rename and cannot cover the rename's own metadata. The previous path-based
        // implementation obtained the same guarantee from MOVEFILE_WRITE_THROUGH; when
        // promotion became handle-bound, that guarantee had to move with it (CRUU15-012).
        const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;

        FieldInfo? flagField = typeof(WindowsOwnedDurableStage)
            .GetField("FILE_FLAG_WRITE_THROUGH", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(flagField, "The owned stage must declare the write-through flag it opens with.");
        Assert.AreEqual(FILE_FLAG_WRITE_THROUGH, (uint)flagField!.GetRawConstantValue()!);

        // The contract is documented on the type, with the Win32 references that justify it.
        string source = File.ReadAllText(RepositoryTestPaths.RequireFile(
            "src", "PromptHelper", "Services", "WindowsOwnedDurableStage.cs"));

        StringAssert.Contains(source, "FILE_ATTRIBUTE_NORMAL | FILE_FLAG_WRITE_THROUGH",
            "The staging handle must actually be opened write-through.");
        StringAssert.Contains(source, "Durability contract (CRUU15-012)");
        StringAssert.Contains(source, "nf-fileapi-createfilew");
    }

    [TestMethod]
    public void CRUU15_012_ReplaceDurable_does_not_report_success_before_required_post_rename_barrier()
    {
        using var temp = new TestDirectory();
        string target = Path.Combine(temp.Root, "library.json");
        byte[] content = Encoding.UTF8.GetBytes("durably promoted content");

        var writer = new WindowsDurableAtomicFileWriter();
        writer.ReplaceDurable(target, content, DurableFileClass.LibraryMetadata);

        // Success means: the content is published under the final name, the staging object is
        // gone, and both the data flush and the write-through rename have already happened by
        // the time control returns.
        CollectionAssert.AreEqual(content, File.ReadAllBytes(target));
        Assert.AreEqual(0, Directory.GetFiles(temp.Root, ".prompthelper-tmp-*").Length);

        string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(target)));
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(content)), hash);

        // Replacing again is equally terminal: no staging residue accumulates.
        writer.ReplaceDurable(target, Encoding.UTF8.GetBytes("second"), DurableFileClass.LibraryMetadata);
        Assert.AreEqual("second", File.ReadAllText(target));
        Assert.AreEqual(0, Directory.GetFiles(temp.Root, ".prompthelper-tmp-*").Length);
    }
}
