using System.Diagnostics;
using System.IO;

namespace PromptHelper.Tests;

/// <summary>
/// Resolves a PowerShell host for tests that execute the repository's .ps1 tooling.
/// </summary>
/// <remarks>
/// <c>powershell.exe</c> is not interchangeable with <c>pwsh</c>: the Windows PowerShell on
/// GitHub's hosted runners does not expose <c>Import-PowerShellDataFile</c>, so tests that
/// hardcoded it passed locally and failed in CI for reasons that had nothing to do with the
/// behaviour under test. PowerShell 7 is preferred where present, with Windows PowerShell as
/// the fallback.
/// </remarks>
internal static class PowerShellHost
{
    private static readonly string ExecutableName = ResolveExecutable();

    public static ProcessStartInfo CreateStartInfo(params string[] arguments)
    {
        var psi = new ProcessStartInfo(ExecutableName);
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");

        foreach (string argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        return psi;
    }

    private static string ResolveExecutable()
    {
        string? pathVariable = System.Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVariable))
        {
            foreach (string directory in pathVariable.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                try
                {
                    if (File.Exists(Path.Combine(directory, "pwsh.exe")))
                    {
                        return "pwsh.exe";
                    }
                }
                catch (System.ArgumentException)
                {
                    // A malformed PATH entry is not a reason to fail; keep looking.
                }
            }
        }

        return "powershell.exe";
    }
}
