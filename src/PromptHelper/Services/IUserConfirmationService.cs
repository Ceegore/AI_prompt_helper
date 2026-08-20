using System.Windows;

namespace PromptHelper.Services;

public interface IUserConfirmationService
{
    bool ConfirmExistingLibrarySwitch(string targetPath, string? warning);
}

public sealed class WpfUserConfirmationService : IUserConfirmationService
{
    private readonly Window? _owner;

    public WpfUserConfirmationService(Window? owner = null)
    {
        _owner = owner;
    }

    public bool ConfirmExistingLibrarySwitch(string targetPath, string? warning)
    {
        string message = "Existing Prompt Helper library found\n\n" +
                         "The selected folder already contains a Prompt Helper library.\n\n" +
                         "Your CURRENT library will NOT be copied, merged, or overwritten.\n" +
                         $"After restart, Prompt Helper will open the library that already exists at:\n\n{targetPath}\n\n" +
                         "If you intended to move the current library, cancel and choose an empty folder instead.\n\n" +
                         "Switch to the existing library anyway?";

        if (!string.IsNullOrEmpty(warning))
        {
            message = $"{warning}\n\n" + message;
        }

        MessageBoxResult result = _owner != null
            ? MessageBox.Show(_owner, message, "Switch to Existing Library", MessageBoxButton.OKCancel, MessageBoxImage.Warning)
            : MessageBox.Show(message, "Switch to Existing Library", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        return result == MessageBoxResult.OK;
    }
}
