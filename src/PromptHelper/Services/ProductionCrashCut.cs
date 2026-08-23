using System;

namespace PromptHelper.Services;

/// <summary>
/// Test-only observation points for deterministic subprocess hard-crash verification. The
/// production application never installs a sink, so these calls are ordinary no-ops outside
/// the test process.
/// </summary>
internal static class ProductionCrashCut
{
    internal static Action<string>? SinkForTests { get; set; }

    public static void Hit(string cut) => SinkForTests?.Invoke(cut);

    public static bool IsArmed(string cut) => SinkForTests is not null &&
        string.Equals(Environment.GetEnvironmentVariable("PROMPTHELPER_CRASH_CUT"), cut,
            StringComparison.Ordinal);
}
