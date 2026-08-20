namespace PromptHelper.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string? DataRootPath { get; set; }
}
