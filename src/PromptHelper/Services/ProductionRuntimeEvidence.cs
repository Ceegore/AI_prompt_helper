using System;

namespace PromptHelper.Services;

/// <summary>
/// Test-only runtime hit sink for binding high-risk regression sentinels to the production
/// method they claim to exercise. It is a no-op unless the test assembly installs a sink.
/// </summary>
internal static class ProductionRuntimeEvidence
{
    internal static Action<string>? SinkForTests { get; set; }

    public static void Hit(string symbol) => SinkForTests?.Invoke(symbol);
}
