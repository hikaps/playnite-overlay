using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PlayniteOverlay;

/// <summary>
/// Resolves and applies overlay theme ResourceDictionaries.
/// Themes are compiled XAML dictionaries in the OverlayPlugin assembly
/// (Themes/{Name}.xaml) exposing a fixed set of semantic brush keys.
/// </summary>
internal static class ThemeManager
{
    public const string DefaultTheme = "Default";

    /// <summary>
    /// Built-in theme names, in display order.
    /// </summary>
    public static readonly IReadOnlyList<string> BuiltInThemes = new[]
    {
        DefaultTheme,
        "Light",
        "Minimal",
        "Vibrant"
    };

    /// <summary>
    /// Resolves a user-supplied theme name to a valid built-in theme.
    /// Case-insensitive; null/empty/unknown names fall back to <see cref="DefaultTheme"/>.
    /// </summary>
    public static string ResolveThemeName(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            foreach (var theme in BuiltInThemes)
            {
                if (string.Equals(theme, name, StringComparison.OrdinalIgnoreCase))
                {
                    return theme;
                }
            }
        }

        return DefaultTheme;
    }

    /// <summary>
    /// Loads the compiled ResourceDictionary for the given theme name.
    /// Unknown names resolve to the default theme.
    /// </summary>
    public static ResourceDictionary LoadTheme(string? name)
    {
        var themeName = ResolveThemeName(name);
        var uri = new Uri($"/OverlayPlugin;component/Themes/{themeName}.xaml", UriKind.Relative);
        return new ResourceDictionary { Source = uri };
    }

    /// <summary>
    /// Merges the theme dictionary into the window's resources so
    /// {DynamicResource} references resolve and update live.
    /// </summary>
    public static void ApplyTo(Window window, string? themeName)
    {
        if (window == null)
        {
            throw new ArgumentNullException(nameof(window));
        }

        window.Resources.MergedDictionaries.Add(LoadTheme(themeName));
    }

    /// <summary>
    /// Resolves a theme brush from a FrameworkElement's resource chain.
    /// Throws if the theme dictionary is not applied.
    /// </summary>
    public static Brush GetBrush(FrameworkElement element, string key)
    {
        return (Brush)element.FindResource(key);
    }
}
