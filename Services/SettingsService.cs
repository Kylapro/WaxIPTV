using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using WaxIPTV.Models;
using Microsoft.Win32;
using WaxIPTV.Services.Logging;

namespace WaxIPTV.Services
{
    /// <summary>
    /// Service responsible for loading, saving and detecting application settings.
    /// Settings are persisted to a JSON file on disk.  If the file does not exist,
    /// the service attempts to locate external player executables and populates
    /// the corresponding fields with detected values.
    /// </summary>
    public class SettingsService
    {
        private readonly string _settingsFilePath;

        /// <summary>
        /// Gets the current settings.  After invoking <see cref="Load"/>, this instance
        /// will be populated either from disk or with auto-detected defaults.
        /// </summary>
        public AppSettings Settings { get; private set; }

        /// <summary>
        /// Constructs a new settings service.
        /// </summary>
        /// <param name="settingsFilePath">Optional path to the settings file.  If omitted,
        /// a file named <c>settings.json</c> in the application base directory is used.</param>
        public SettingsService(string? settingsFilePath = null)
        {
            _settingsFilePath = settingsFilePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            Settings = new AppSettings();
        }

        /// <summary>
        /// Loads settings from disk.  If the file does not exist, attempts to detect
        /// mpv and VLC installation paths and leaves other fields at their defaults.
        /// </summary>
        public void Load()
        {
            if (File.Exists(_settingsFilePath))
            {
                AppLog.Logger.Information("Loading settings from {Path}", AppLog.Safe(_settingsFilePath));
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null)
                {
                    Settings = loaded;
                }
            }
            else
            {
                AppLog.Logger.Information("Settings file not found, attempting to auto-detect players");
                // Attempt to detect player executables when no settings file exists
                Settings.MpvPath = FindMpvPath();
                Settings.VlcPath = FindVlcPath();
            }
        }

        /// <summary>
        /// Persists the provided settings to disk.  The resulting JSON is formatted
        /// with indentation for readability.  When invoked with a null parameter, this
        /// method uses the current <see cref="Settings"/> instance.
        /// </summary>
        public void Save(AppSettings? settings = null)
        {
            // If a specific settings instance is provided, update the internal Settings
            if (settings != null)
            {
                Settings = settings;
            }
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            AppLog.Logger.Information("Saving settings to {Path}", AppLog.Safe(_settingsFilePath));
            File.WriteAllText(_settingsFilePath, json);
        }

        /// <summary>
        /// Attempts to locate a VLC installation by querying the Windows registry.  If found,
        /// returns the path to <c>vlc.exe</c>; otherwise returns <see langword="null"/>.
        /// </summary>
        public static string? FindVlcPath()
        {
            // Query both 64-bit and 32-bit registry nodes
            var dir = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\VideoLAN\VLC", "InstallDir", null) as string
                   ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\VideoLAN\VLC", "InstallDir", null) as string;
            if (dir == null)
                return null;
            var path = Path.Combine(dir, "vlc.exe");
            var exists = File.Exists(path) ? path : null;
            AppLog.Logger.Information("Detected VLC at {Path}", AppLog.Safe(exists));
            return exists;
        }

        /// <summary>
        /// Attempts to locate an installed copy of mpv.  This method will search the system PATH
        /// via the <c>where</c> command and then fall back to a common installation directory.
        /// Returns the path to <c>mpv.exe</c> if found, or <see langword="null"/> otherwise.
        /// </summary>
        public static string? FindMpvPath()
        {
            // Try to use the "where" command to locate mpv.exe
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "where",
                        Arguments = "mpv.exe",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(2000);
                // The first line of output should be the full path to mpv.exe, if present
                var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var candidate = lines.FirstOrDefault();
                if (candidate != null && File.Exists(candidate))
                {
                    AppLog.Logger.Information("Detected mpv at {Path}", AppLog.Safe(candidate));
                    return candidate;
                }
            }
            catch (Exception ex)
            {
                AppLog.Logger.Warning(ex, "Failed to locate mpv via where command");
                // ignore and fall back to common path
            }

            // Fallback to common installation path
            var common = @"C:\\Program Files\\mpv\\mpv.exe";
            var exists = File.Exists(common) ? common : null;
            AppLog.Logger.Information("Fallback mpv detection returned {Path}", AppLog.Safe(exists));
            return exists;
        }
    }
}