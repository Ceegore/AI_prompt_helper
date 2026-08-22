using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PromptHelper.Tests;

/// <summary>The complete outcome of a child process run. Never partially populated.</summary>
internal sealed record ProcessRunResult(
    bool Exited,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool WasKilled)
{
    public string CombinedOutput => StandardOutput + StandardError;
}

/// <summary>
/// Runs a child process with an absolute time bound that actually holds.
/// </summary>
/// <remarks>
/// <para>Two failure modes have to be closed together, and closing only one of them produces a
/// helper that still hangs forever (CRUU15-010):</para>
/// <list type="number">
/// <item><b>Pipe-buffer deadlock.</b> Reading one stream to completion before the other lets a
/// child that fills the unread pipe block on write while the test blocks on read. Both streams
/// are drained concurrently from the moment the process starts.</item>
/// <item><b>Timeout that cannot terminate.</b> <c>WaitForExit(timeout)</c> returning false does
/// not stop the child, and its pipes stay open, so a subsequent blocking read on the drain
/// tasks waits forever — the timeout expires and the test hangs anyway. On timeout the process
/// tree is killed, and the drains are then awaited under a second, bounded wait so even a
/// surviving grandchild holding the pipe open cannot stall the return.</item>
/// </list>
/// <para>Every child-process test in this suite goes through here rather than open-coding
/// <c>Process.Start</c>, so neither failure mode can be reintroduced one helper at a time.</para>
/// </remarks>
internal static class ProcessTestRunner
{
    /// <summary>How long to wait for pipe drains to finish after the process tree is killed.</summary>
    private const int PostKillDrainMilliseconds = 10_000;

    public static ProcessRunResult Run(ProcessStartInfo startInfo, int timeoutMilliseconds = 60_000)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{startInfo.FileName}'.");

        // Start draining before anything blocks, so a child that floods either pipe keeps
        // making progress.
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        bool exited = process.WaitForExit(timeoutMilliseconds);
        bool killed = false;

        if (!exited)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                killed = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone, or unkillable; the bounded drain below still returns.
            }

            // Give the child a moment to be reaped so its handles close and the drains finish.
            process.WaitForExit(PostKillDrainMilliseconds);
        }

        string stdout = DrainWithinBound(stdoutTask);
        string stderr = DrainWithinBound(stderrTask);

        int exitCode = -1;
        try
        {
            if (process.HasExited)
            {
                exitCode = process.ExitCode;
            }
        }
        catch (InvalidOperationException)
        {
            // Leave the sentinel exit code; Exited already reports the truth.
        }

        return new ProcessRunResult(exited, exitCode, stdout, stderr, killed);
    }

    /// <summary>
    /// Waits for a drain task, but never unboundedly: a descendant that inherited the pipe can
    /// keep it open after the direct child is dead, and blocking on that is exactly the hang
    /// this class exists to prevent.
    /// </summary>
    private static string DrainWithinBound(Task<string> drain)
    {
        try
        {
            return drain.Wait(PostKillDrainMilliseconds) ? drain.Result : string.Empty;
        }
        catch (AggregateException)
        {
            return string.Empty;
        }
        catch (ObjectDisposedException)
        {
            return string.Empty;
        }
    }
}
