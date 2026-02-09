using System;
using System.Windows;
using PaqetTunnel.Models;

namespace PaqetTunnel.Services;

/// <summary>
/// Manages application themes with runtime switching.
/// Themes are loaded from Themes/ directory as ResourceDictionaries.
/// </summary>
public static class ThemeManager
{
    public static readonly string[] AvailableThemes =
    {
        "dark", "light", "nord", "cyberpunk", "sakura",
        "ocean", "sunset", "dracula", "monokai", "rose"
    };

    private static string _currentTheme = "dark";

    public static string CurrentTheme => _currentTheme;

    /// <summary>Apply a theme by name. Swaps the theme ResourceDictionary at runtime.</summary>
    public static void Apply(string theme)
    {
        theme = theme?.ToLowerInvariant() ?? "dark";
        if (Array.IndexOf(AvailableThemes, theme) < 0)
            theme = "dark";

        var uri = new Uri($"Themes/{Capitalize(theme)}.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        var app = Application.Current;
        if (app == null) return;

        // Replace the first merged dictionary (theme) while keeping others
        var merged = app.Resources.MergedDictionaries;
        if (merged.Count > 0)
            merged[0] = dict;
        else
            merged.Add(dict);

        _currentTheme = theme;
        Logger.Info($"Theme applied: {theme}");
    }

    /// <summary>Load theme from settings.</summary>
    public static void LoadFromSettings(AppSettings settings)
    {
        Apply(settings.Theme ?? "dark");
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
