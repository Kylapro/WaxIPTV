using System.Windows;
using WaxIPTV.Models;
using WaxIPTV.Services;
using WaxIPTV.Views;

namespace WaxIPTV
{
    /// <summary>
    /// Application entry point.  Handles startup to show the settings
    /// dialog prior to launching the main window.  If the user cancels
    /// the settings dialog, the application shuts down.
    /// </summary>
    public partial class App : Application
    {
        private readonly SettingsService _settingsService = new();

        /// <summary>
        /// Handles application startup.  Loads existing settings and
        /// presents the settings window.  If the user saves changes
        /// successfully, the main window is shown.
        /// </summary>
        private void OnStartup(object sender, StartupEventArgs e)
        {
            // Load settings from disk; this populates the Settings property
            _settingsService.Load();
            var settings = _settingsService.Settings;

            // Always show the main window first.  The channel list will
            // appear immediately.  If critical information is missing
            // (player path or playlist), we will prompt for settings.
            var main = new MainWindow();
            main.Show();

            // Determine whether essential settings are missing.  If so,
            // present the settings dialog to the user.  After saving,
            // request the main window to reload the playlist and EPG.
            bool missingPlayer =
                (string.Equals(settings.Player, "mpv", System.StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(settings.MpvPath)) ||
                (string.Equals(settings.Player, "vlc", System.StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(settings.VlcPath));

            bool missingPlaylist = string.IsNullOrWhiteSpace(settings.PlaylistUrl);

            if (missingPlayer || missingPlaylist)
            {
                var dlg = new SettingsWindow(_settingsService, settings);
                if (dlg.ShowDialog() == true)
                {
                    // User saved settings; instruct main window to reload
                    main.ReloadFromSettings();
                }
                else
                {
                    // User cancelled; continue running with whatever settings are available
                }
            }

            // Setup the theme watcher so that external edits to theme.json
            // are applied automatically.  This should occur after the main
            // window is created so that Application.Current is available.
            SetupThemeWatcher();
        }

        /// <summary>
        /// Sets up a file watcher for the theme JSON.  Whenever the
        /// file changes, the new contents are read and applied to the
        /// application resource dictionary.  Errors while applying
        /// the theme are ignored to avoid crashing the app if the
        /// JSON becomes invalid.
        /// </summary>
        private void SetupThemeWatcher()
        {
            try
            {
                var themePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "theme.json");
                var directory = System.IO.Path.GetDirectoryName(themePath);
                var filename = System.IO.Path.GetFileName(themePath);
                if (directory == null || filename == null)
                    return;
                var watcher = new System.IO.FileSystemWatcher(directory, filename)
                {
                    NotifyFilter = System.IO.NotifyFilters.LastWrite | System.IO.NotifyFilters.Size
                };
                watcher.Changed += (s, e) =>
                {
                    // Use dispatcher to update UI resources on the main thread
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var json = System.IO.File.ReadAllText(themePath);
                            WaxIPTV.Theming.ThemeLoader.ApplyThemeJson(json, Application.Current.Resources);
                        }
                        catch
                        {
                            // Ignore errors during theme reload
                        }
                    });
                };
                watcher.EnableRaisingEvents = true;
            }
            catch
            {
                // If the watcher can't be created (e.g. due to permissions), silently fail
            }
        }
    }
}
