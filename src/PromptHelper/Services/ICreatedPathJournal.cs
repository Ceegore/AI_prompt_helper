namespace PromptHelper.Services;

internal interface ICreatedPathJournal
{
    void TrackCreatedFile(string path);
    void TrackCreatedDirectory(string path);
}
