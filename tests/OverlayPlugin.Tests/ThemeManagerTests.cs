using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using PlayniteOverlay;

namespace OverlayPlugin.Tests;

public class ThemeManagerTests
{
    [Theory]
    [InlineData(null, "Default")]
    [InlineData("", "Default")]
    [InlineData("   ", "Default")]
    [InlineData("Default", "Default")]
    [InlineData("default", "Default")]
    [InlineData("LIGHT", "Light")]
    [InlineData("light", "Light")]
    [InlineData("minimal", "Minimal")]
    [InlineData("Vibrant", "Vibrant")]
    [InlineData("BogusTheme", "Default")]
    public void ResolveThemeName_NormalizesToValidTheme(string? input, string expected)
    {
        Assert.Equal(expected, ThemeManager.ResolveThemeName(input));
    }

    [Fact]
    public void BuiltInThemes_ContainsDefault()
    {
        Assert.Contains(ThemeManager.DefaultTheme, ThemeManager.BuiltInThemes);
    }

    [Fact]
    public void BuiltInThemes_AreUnique()
    {
        Assert.Equal(ThemeManager.BuiltInThemes.Count, ThemeManager.BuiltInThemes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void OverlayWindowXaml_AllDynamicResourceKeys_ExistInDefaultTheme()
    {
        var missing = GetMissingKeys("Default");
        Assert.True(missing.Count == 0, $"Missing theme keys: {string.Join(", ", missing)}");
    }

    [Fact]
    public void AllThemeDictionaries_DefineSameKeySetAsDefault()
    {
        var defaultKeys = GetThemeKeys("Default");
        foreach (var theme in ThemeManager.BuiltInThemes)
        {
            var themeKeys = GetThemeKeys(theme);
            var missing = defaultKeys.Except(themeKeys, StringComparer.OrdinalIgnoreCase).ToList();
            var extra = themeKeys.Except(defaultKeys, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.True(missing.Count == 0, $"{theme} is missing keys: {string.Join(", ", missing)}");
            Assert.True(extra.Count == 0, $"{theme} has extra keys: {string.Join(", ", extra)}");
        }
    }

    [Fact]
    public void EveryBuiltInTheme_HasAResourceFile()
    {
        foreach (var theme in ThemeManager.BuiltInThemes)
        {
            var path = Path.Combine(GetRepoRoot(), "src", "OverlayPlugin", "Themes", $"{theme}.xaml");
            Assert.True(File.Exists(path), $"Missing theme file: {path}");
        }
    }

    private static HashSet<string> GetMissingKeys(string theme)
    {
        var repoRoot = GetRepoRoot();
        var overlayXaml = File.ReadAllText(Path.Combine(repoRoot, "src", "OverlayPlugin", "OverlayWindow.xaml"));
        var themeKeys = GetThemeKeys(theme);

        var usedKeys = Regex.Matches(overlayXaml, @"\{DynamicResource\s+([A-Za-z0-9_]+)\}")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return usedKeys.Where(k => !themeKeys.Contains(k, StringComparer.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> GetThemeKeys(string theme)
    {
        var repoRoot = GetRepoRoot();
        var themePath = Path.Combine(repoRoot, "src", "OverlayPlugin", "Themes", $"{theme}.xaml");
        var content = File.ReadAllText(themePath);
        return Regex.Matches(content, @"x:Key=""([A-Za-z0-9_]+)""")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "OverlayPlugin")) &&
                File.Exists(Path.Combine(dir.FullName, "src", "OverlayPlugin", "OverlayPlugin.csproj")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from " + AppContext.BaseDirectory);
    }
}
