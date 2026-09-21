using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using PromptHelper.Models;
using PromptHelper.Services;

namespace PromptHelper.Desktop;

public sealed partial class App : Application
{
    public override void Initialize() =>
        AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                LinuxNativeFileSystemGuard.EnsureSupported();

                var settingsStore = new LinuxDesktopSettingsStore();
                AppSettings settings = settingsStore.Load();

                var lockProvider = new LinuxAppInstanceLockProvider();
                IAppInstanceLease? lease = lockProvider.TryAcquire(
                    Path.Combine(DefaultDataRoot.Path, ".app.lock"));

                if (lease is null)
                {
                    desktop.MainWindow = new StartupErrorWindow(
                        "Prompt Helper is already running for this user.");
                }
                else
                {
                    string root =
                        settingsStore.ResolveEffectiveDataRoot(settings);
                    var library = new LinuxPromptLibraryStore(root);
                    library.Initialize();

                    ApplyTheme(settings.UseDarkMode);

                    desktop.MainWindow = new MainWindow(
                        settingsStore,
                        settings,
                        library,
                        lease);
                }
            }
            catch (Exception ex)
            {
                desktop.MainWindow = new StartupErrorWindow(
                    "Prompt Helper could not start safely.\n\n" + ex.Message);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void ApplyTheme(bool useDarkMode)
    {
        RequestedThemeVariant = useDarkMode
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
    }
}

internal static class LinuxNativeFileSystemGuard
{
    public static void EnsureSupported()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "This desktop host is the Linux build of Prompt Helper.");
        }
    }
}
