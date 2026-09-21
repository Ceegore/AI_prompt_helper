namespace PromptHelper.Services;

/// <summary>
/// Platform-tagged identity of one concrete filesystem object. The scheme defines how the
/// opaque value was obtained and compared; identities from different schemes are never equal.
/// </summary>
internal readonly record struct FileObjectIdentity
{
    public FileObjectIdentity(string scheme, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        Scheme = scheme;
        Value = value;
    }

    public string Scheme { get; }

    public string Value { get; }

    public override string ToString() => $"{Scheme}:{Value}";
}
