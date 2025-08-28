using System;
using System.IO;
using System.Windows;
using WaxIPTV.Models;

namespace WaxIPTV.Theming
{
    /// <summary>
    /// Centralises theme loading logic.  Chooses the appropriate theme file
    /// based on application settings and the current Windows theme and applies
    /// it to a resource dictionary.
    /// </summary>
    public static class ThemeManager
    {
        /// <summary>
        /// Gets the path to the theme file currently applied to the
        /// application.  This path is used by the file watcher and settings
        /// editor to load and persist theme changes.
        /// </summary>
        public static string CurrentThemePath { get; private set; } = Path.Combine(AppContext.BaseDirectory, "theme.json");

        /// <summary>
        /// Applies the theme specified by <paramref name="settings"/> to the
        /// provided resource dictionary.  The chosen theme file path is stored
        /// in <see cref="CurrentThemePath"/>.
        /// </summary>
        public static void ApplyTheme(AppSettings settings, ResourceDictionary resources)
        {
            var path = GetThemePath(settings);
            CurrentThemePath = path;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                ThemeLoader.ApplyThemeJson(json, resources);
            }
        }

        /// <summary>
        /// Determines which theme JSON file should be used given the
        /// application settings and current system theme.
        /// </summary>
        public static string GetThemePath(AppSettings settings)
        {
            var baseDir = AppContext.BaseDirectory;
            var light = settings.LightThemePath ?? "theme.json";
            var dark = settings.DarkThemePath ?? "theme.dark.json";

            string selected = light;
            if (settings.UseSystemTheme)
            {
                selected = SystemThemeService.IsLightTheme() ? light : dark;
            }

            return Path.Combine(baseDir, selected);
        }
    }
}
