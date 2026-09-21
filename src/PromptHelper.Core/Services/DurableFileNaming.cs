namespace PromptHelper.Services;

internal static class DurableFileNaming
{
    public static string GetClassTag(DurableFileClass fileClass) => fileClass switch
    {
        DurableFileClass.Settings => "settings",
        DurableFileClass.LibraryMetadata => "library",
        DurableFileClass.PromptBody => "prompt",
        DurableFileClass.RecoveryArtifact => "recovery",
        DurableFileClass.InitializationControl => "init",
        DurableFileClass.MigrationControl => "migration",
        DurableFileClass.MutationControl => "mutation",
        _ => throw new ArgumentOutOfRangeException(nameof(fileClass), fileClass, "Unknown durable file class.")
    };
}
