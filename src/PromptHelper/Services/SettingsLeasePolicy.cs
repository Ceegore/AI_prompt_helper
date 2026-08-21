using System;

namespace PromptHelper.Services;

public sealed record SettingsLeasePolicy(
    TimeSpan Timeout,
    TimeSpan RetryDelay)
{
    public static readonly SettingsLeasePolicy Default = new(
        Timeout: TimeSpan.FromMilliseconds(2000),
        RetryDelay: TimeSpan.FromMilliseconds(25));

    public static readonly SettingsLeasePolicy FastTest = new(
        Timeout: TimeSpan.FromMilliseconds(50),
        RetryDelay: TimeSpan.FromMilliseconds(5));
}
