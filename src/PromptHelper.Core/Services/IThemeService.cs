namespace PromptHelper.Services;

public interface IThemeService
{
    bool PreferredDarkMode { get; }

    void ApplyPreferredTheme(bool useDarkMode);

    void RefreshSystemContrast();
}
