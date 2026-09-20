using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PromptHelper.Services;

public sealed class WpfThemeService : IThemeService
{
    private static readonly IReadOnlyDictionary<string, Color> LightPalette =
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["AppBackgroundBrush"] = Color.FromRgb(0xF4, 0xF6, 0xFA),
            ["SurfaceBrush"] = Colors.White,
            ["TextPrimaryBrush"] = Color.FromRgb(0x11, 0x18, 0x27),
            ["TextSecondaryBrush"] = Color.FromRgb(0x59, 0x61, 0x70),
            ["SubtleTextBrush"] = Color.FromRgb(0x5E, 0x66, 0x75),
            ["BorderBrush"] = Color.FromRgb(0xE5, 0xE7, 0xEB),
            ["BorderHoverBrush"] = Color.FromRgb(0xD1, 0xD5, 0xDB),
            ["AccentBrush"] = Color.FromRgb(0x4F, 0x46, 0xE5),
            ["AccentHoverBrush"] = Color.FromRgb(0x43, 0x38, 0xCA),
            ["AccentPressedBrush"] = Color.FromRgb(0x37, 0x30, 0xA3),
            ["AccentForegroundBrush"] = Colors.White,
            ["AccentLightBrush"] = Color.FromRgb(0xEE, 0xF2, 0xFF),
            ["SecondaryHoverBrush"] = Color.FromRgb(0xF9, 0xFA, 0xFB),
            ["DangerBrush"] = Color.FromRgb(0xDC, 0x26, 0x26),
            ["DangerLightBrush"] = Color.FromRgb(0xFE, 0xF2, 0xF2),
            ["DangerBorderBrush"] = Color.FromRgb(0xFE, 0xCA, 0xCA)
        };

    private static readonly IReadOnlyDictionary<string, Color> DarkPalette =
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["AppBackgroundBrush"] = Color.FromRgb(0x0B, 0x10, 0x20),
            ["SurfaceBrush"] = Color.FromRgb(0x11, 0x18, 0x27),
            ["TextPrimaryBrush"] = Color.FromRgb(0xF9, 0xFA, 0xFB),
            ["TextSecondaryBrush"] = Color.FromRgb(0xCB, 0xD5, 0xE1),
            ["SubtleTextBrush"] = Color.FromRgb(0xB8, 0xC2, 0xD1),
            ["BorderBrush"] = Color.FromRgb(0x33, 0x41, 0x55),
            ["BorderHoverBrush"] = Color.FromRgb(0x47, 0x55, 0x69),
            ["AccentBrush"] = Color.FromRgb(0x81, 0x8C, 0xF8),
            ["AccentHoverBrush"] = Color.FromRgb(0xA5, 0xB4, 0xFC),
            ["AccentPressedBrush"] = Color.FromRgb(0xC7, 0xD2, 0xFE),
            ["AccentForegroundBrush"] = Color.FromRgb(0x11, 0x18, 0x27),
            ["AccentLightBrush"] = Color.FromRgb(0x1E, 0x1B, 0x4B),
            ["SecondaryHoverBrush"] = Color.FromRgb(0x1F, 0x29, 0x37),
            ["DangerBrush"] = Color.FromRgb(0xF8, 0x71, 0x71),
            ["DangerLightBrush"] = Color.FromRgb(0x45, 0x1A, 0x1A),
            ["DangerBorderBrush"] = Color.FromRgb(0x7F, 0x1D, 0x1D)
        };

    private readonly Application _application;

    public WpfThemeService(Application application)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
    }

    public bool PreferredDarkMode { get; private set; }

    public void ApplyPreferredTheme(bool useDarkMode)
    {
        PreferredDarkMode = useDarkMode;
        ApplyEffectivePalette();
    }

    public void RefreshSystemContrast() => ApplyEffectivePalette();

    private void ApplyEffectivePalette()
    {
        _application.Dispatcher.VerifyAccess();

        IReadOnlyDictionary<string, Color> palette = SystemParameters.HighContrast
            ? CreateHighContrastPalette()
            : PreferredDarkMode ? DarkPalette : LightPalette;

        foreach ((string key, Color color) in palette)
        {
            // Replace the resource instead of mutating a possibly frozen brush. All palette
            // consumers use DynamicResource, so open windows update immediately and safely.
            _application.Resources[key] = new SolidColorBrush(color);
        }
    }

    private static IReadOnlyDictionary<string, Color> CreateHighContrastPalette() =>
        new Dictionary<string, Color>(StringComparer.Ordinal)
        {
            ["AppBackgroundBrush"] = SystemColors.WindowColor,
            ["SurfaceBrush"] = SystemColors.WindowColor,
            ["TextPrimaryBrush"] = SystemColors.WindowTextColor,
            ["TextSecondaryBrush"] = SystemColors.WindowTextColor,
            ["SubtleTextBrush"] = SystemColors.WindowTextColor,
            ["BorderBrush"] = SystemColors.WindowTextColor,
            ["BorderHoverBrush"] = SystemColors.HighlightColor,
            ["AccentBrush"] = SystemColors.HighlightColor,
            ["AccentHoverBrush"] = SystemColors.HotTrackColor,
            ["AccentPressedBrush"] = SystemColors.HighlightColor,
            ["AccentForegroundBrush"] = SystemColors.HighlightTextColor,
            ["AccentLightBrush"] = SystemColors.WindowColor,
            ["SecondaryHoverBrush"] = SystemColors.ControlColor,
            ["DangerBrush"] = SystemColors.WindowTextColor,
            ["DangerLightBrush"] = SystemColors.WindowColor,
            ["DangerBorderBrush"] = SystemColors.WindowTextColor
        };
}
