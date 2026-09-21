namespace PromptHelper.Models;

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string? DataRootPath { get; set; }
    public bool UseDarkMode { get; set; }
}
