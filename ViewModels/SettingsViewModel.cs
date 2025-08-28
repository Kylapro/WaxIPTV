using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaxIPTV.Models;
using WaxIPTV.Services;
using WaxIPTV.Theming;
using WaxIPTV.Views;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Text;

namespace WaxIPTV.ViewModels
{
    /// <summary>
    /// View model for the settings window.  In addition to managing
    /// player and source settings, it provides a user‑friendly theme
    /// editor with live preview, presets, and raw JSON editing.  Theme
    /// properties are synchronised with a backing JSON structure and
    /// applied on demand or automatically when AutoApplyTheme is true.
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly SettingsService _service;
        private readonly AppSettings _settings;

        // ----- Player / sources -----
        [ObservableProperty]
        private string player = "mpv";
        [ObservableProperty]
        private string? mpvPath;
        [ObservableProperty]
        private string? vlcPath;
        [ObservableProperty]
        private string? playlistUrl;
        [ObservableProperty]
        private string? xmltvUrl;
        [ObservableProperty]
        private int epgRefreshHours = 12;

        // ----- Friendly Theme fields -----
        [ObservableProperty]
        private string bgHex = "#FFFFFF";
        [ObservableProperty]
        private string textHex = "#0B0B0F";
        [ObservableProperty]
        private string accentHex = "#5B8CFF";
        [ObservableProperty]
        private FontFamily? selectedFontFamily = new FontFamily("Segoe UI");
        [ObservableProperty]
        private double sizeBase = 14;
        [ObservableProperty]
        private double radius = 8;
        [ObservableProperty]
        private bool autoApplyTheme = true;

        // ----- EPG download status -----
        // Indicates whether an EPG download is currently in progress.  When
        // true, the UI can show a progress bar or busy indicator; when
        // false, the indicator should be hidden.
        [ObservableProperty]
        private bool isEpgDownloading;

        // Holds the current progress percentage (0–100) of the EPG
        // download.  This value is updated only when the total
        // content length is known.  If the length is unknown, the UI
        // may choose to show an indeterminate progress bar based on
        // IsEpgDownloading instead.
        [ObservableProperty]
        private int epgDownloadProgress;

        // Background colour for drop‑down controls (ComboBoxes).  This allows
        // users to customise the background of selection boxes separately
        // from the overall surface colour.  Defaults to a light or dark
        // surface depending on the initial theme.
        [ObservableProperty]
        private string dropdownHex = "#F5F6FA";

        // Advanced JSON (kept for power users)
        [ObservableProperty]
        private string themeJson = "{}";
        [ObservableProperty]
        private bool showAdvancedJson;

        // Determines whether the application should match the Windows light
        // or dark theme automatically.  When enabled, the theme file is
        // chosen based on the current system setting.
        [ObservableProperty]
        private bool useSystemTheme = true;

        public SettingsViewModel(SettingsService service, AppSettings settings)
        {
            _service = service;
            _settings = settings;

            // Seed from current settings + theme.json if present
            player      = settings.Player ?? "mpv";
            mpvPath     = settings.MpvPath;
            vlcPath     = settings.VlcPath;
            playlistUrl = settings.PlaylistUrl;
            xmltvUrl    = settings.XmltvUrl;
            epgRefreshHours = settings.EpgRefreshHours;
            useSystemTheme = settings.UseSystemTheme;

            var themeFile = ThemeManager.CurrentThemePath;
            if (File.Exists(themeFile))
            {
                themeJson = File.ReadAllText(themeFile);
                TryLoadFromJson(themeJson);
            }
            else
            {
                // initial JSON from defaults
                UpdateThemeJson();
            }
        }

        /// <summary>
        /// Returns all system fonts sorted by name.  Used to populate
        /// the font family ComboBox in the theme editor.
        /// </summary>
        public IEnumerable<FontFamily> SystemFonts =>
            Fonts.SystemFontFamilies.OrderBy(f => f.Source);

        // ----- Browse commands -----
        [RelayCommand]
        private void BrowseMpv()
        {
            var dlg = new OpenFileDialog { Filter = "mpv|mpv.exe|Executable|*.exe|All|*.*" };
            if (dlg.ShowDialog() == true) MpvPath = dlg.FileName;
        }

        [RelayCommand]
        private void BrowseVlc()
        {
            var dlg = new OpenFileDialog { Filter = "vlc|vlc.exe|Executable|*.exe|All|*.*" };
            if (dlg.ShowDialog() == true) VlcPath = dlg.FileName;
        }

        [RelayCommand]
        private void BrowsePlaylist()
        {
            var dlg = new OpenFileDialog { Filter = "M3U/M3U8|*.m3u;*.m3u8|All|*.*" };
            if (dlg.ShowDialog() == true) PlaylistUrl = dlg.FileName;
        }

        [RelayCommand]
        private void BrowseEpg()
        {
            var dlg = new OpenFileDialog { Filter = "XML/XMLTV|*.xml;*.xml.gz|All|*.*" };
            if (dlg.ShowDialog() == true) XmltvUrl = dlg.FileName;
        }

        // ----- Theme commands -----
        [RelayCommand]
        private void ApplyTheme()
        {
            UpdateThemeJson();
            try
            {
                ThemeLoader.ApplyThemeJson(themeJson, Application.Current.Resources);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Theme JSON invalid:\n" + ex.Message, "Theme Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void ResetTheme()
        {
            BgHex = "#0B0B0F";
            TextHex = "#FFFFFF";
            AccentHex = "#5B8CFF";
            DropdownHex = "#13131A";
            SelectedFontFamily = new FontFamily("Segoe UI");
            SizeBase = 14;
            Radius = 8;
            UpdateThemeJson();
            if (AutoApplyTheme) ApplyTheme();
        }

        [RelayCommand]
        private void ChooseAccent(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            AccentHex = hex;
        }

        [RelayCommand]
        private void ChooseBg(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            BgHex = hex;
        }

        [RelayCommand]
        private void ChooseText(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            TextHex = hex;
        }

        [RelayCommand]
        private void ChooseDropdown(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return;
            DropdownHex = hex;
        }

        [RelayCommand]
        private void ApplyPreset(string? preset)
        {
            if (string.Equals(preset, "light", StringComparison.OrdinalIgnoreCase))
            {
                BgHex = "#FFFFFF";
                TextHex = "#0B0B0F";
                AccentHex = "#3D6DFF";
                DropdownHex = "#F5F6FA";
            }
            else // dark is default
            {
                BgHex = "#0B0B0F";
                TextHex = "#FFFFFF";
                AccentHex = "#5B8CFF";
                DropdownHex = "#13131A";
            }
            UpdateThemeJson();
            if (AutoApplyTheme) ApplyTheme();
        }

        // ----- Reactive theme updates -----
        partial void OnBgHexChanged(string value)       => MaybeLiveApply();
        partial void OnTextHexChanged(string value)     => MaybeLiveApply();
        partial void OnAccentHexChanged(string value)   => MaybeLiveApply();
        partial void OnDropdownHexChanged(string value) => MaybeLiveApply();
        partial void OnSelectedFontFamilyChanged(FontFamily? value) => MaybeLiveApply();
        partial void OnSizeBaseChanged(double value)    => MaybeLiveApply();
        partial void OnRadiusChanged(double value)      => MaybeLiveApply();

        private void MaybeLiveApply()
        {
            UpdateThemeJson();
            if (AutoApplyTheme) ApplyTheme();
        }

        private void UpdateThemeJson()
        {
            var model = new
            {
                colors = new
                {
                    bg       = NormalizeHex(BgHex),
                    text     = NormalizeHex(TextHex),
                    accent   = NormalizeHex(AccentHex),
                    dropdown = NormalizeHex(DropdownHex)
                },
                typography = new { fontFamily = SelectedFontFamily?.Source ?? "Segoe UI", sizeBase = (int)Math.Round(SizeBase) },
                shape = new { radius = (int)Math.Round(Radius) }
            };
            themeJson = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
            OnPropertyChanged(nameof(ThemeJson));
        }

        private void TryLoadFromJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("colors", out var colors))
                {
                    if (colors.TryGetProperty("bg", out var bg)) BgHex = bg.GetString() ?? BgHex;
                    if (colors.TryGetProperty("text", out var tx)) TextHex = tx.GetString() ?? TextHex;
                    if (colors.TryGetProperty("accent", out var ac)) AccentHex = ac.GetString() ?? AccentHex;
                    if (colors.TryGetProperty("dropdown", out var dd)) DropdownHex = dd.GetString() ?? DropdownHex;
                }
                if (root.TryGetProperty("typography", out var typo))
                {
                    if (typo.TryGetProperty("fontFamily", out var ff))
                        SelectedFontFamily = new FontFamily(ff.GetString() ?? "Segoe UI");
                    if (typo.TryGetProperty("sizeBase", out var sz)) SizeBase = sz.GetDouble();
                }
                if (root.TryGetProperty("shape", out var shape))
                {
                    if (shape.TryGetProperty("radius", out var r)) Radius = r.GetDouble();
                }
            }
            catch
            {
                // ignore parse errors; keep defaults
            }
        }

        private static string NormalizeHex(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "#000000";
            s = s.Trim();
            if (!s.StartsWith("#")) s = "#" + s;
            if (s.Length == 4) // #RGB -> expand
            {
                var r = s[1]; var g = s[2]; var b = s[3];
                s = $"#{r}{r}{g}{g}{b}{b}";
            }
            return s.ToUpperInvariant();
        }

        // ----- Save/Close commands (unchanged except theme handling) -----
        [RelayCommand]
        private async Task SaveAndClose(Window window)
        {
            // Basic validation: require a playlist, but EPG can be left blank
            if (string.IsNullOrWhiteSpace(playlistUrl))
            {
                MessageBox.Show("Please set a Playlist URL or file.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Validate player paths only if that player is selected
            if (player.Equals("mpv", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(mpvPath))
            {
                MessageBox.Show("Please set the path to mpv.exe.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (player.Equals("vlc", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(vlcPath))
            {
                MessageBox.Show("Please set the path to vlc.exe.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Persist new settings
            _settings.Player      = player;
            _settings.MpvPath     = mpvPath;
            _settings.VlcPath     = vlcPath;
            _settings.PlaylistUrl = playlistUrl;
            _settings.XmltvUrl    = xmltvUrl;
            _settings.EpgRefreshHours = epgRefreshHours;
            _settings.UseSystemTheme = useSystemTheme;

            // Save settings and theme
            _service.Save(_settings);
            UpdateThemeJson();
            File.WriteAllText(ThemeManager.CurrentThemePath, themeJson);

            // Apply the theme immediately and update the current theme path
            ThemeManager.ApplyTheme(_settings, Application.Current.Resources);

            try
            {
                if (window.IsVisible)
                {
                    window.DialogResult = true;
                }
            }
            catch (InvalidOperationException)
            {
                // Window was not shown as a dialog; ignore failure to set DialogResult.
            }
            window.Close();
        }

        [RelayCommand]
        private void Cancel(Window window)
        {
            try
            {
                if (window.IsVisible)
                {
                    window.DialogResult = false;
                }
            }
            catch (InvalidOperationException)
            {
                // Window was not shown as a dialog; ignore failure to set DialogResult.
            }
            window.Close();
        }

        /// <summary>
        /// Returns the available player options for the ComboBox.
        /// </summary>
        public string[] PlayerOptions => new[] { "mpv", "vlc" };

        // ----- EPG download/refresh commands -----

        /// <summary>
        /// Returns the full path to the EPG cache file.  The file is stored
        /// under the user's LocalApplicationData\WaxIPTV folder and will be
        /// created on demand.  This helper is shared between the settings
        /// view model and the main window to ensure a single cache file is
        /// used throughout the application.
        /// </summary>
        private static string GetEpgCachePath()
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WaxIPTV");
            System.IO.Directory.CreateDirectory(dir);
            return System.IO.Path.Combine(dir, "epg-cache.xml");
        }

        /// <summary>
        /// Converts a byte array representing an EPG XML or gzipped XML into a
        /// string.  If the source path ends with ".gz", the data is
        /// decompressed using a GZipStream before decoding as UTF‑8.  Otherwise
        /// the bytes are decoded directly.  This helper centralises the
        /// decompression logic used by DownloadEpg.
        /// </summary>
        /// <param name="bytes">Raw bytes downloaded or read from disk.</param>
        /// <param name="source">The original URL or file path used to identify
        /// whether the data is gzipped.</param>
        /// <returns>The UTF‑8 string representation of the EPG XML.</returns>
        private static string ConvertEpgBytesToString(byte[] bytes, string source)
        {
            try
            {
                if (!string.IsNullOrEmpty(source) && source.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    using var input = new MemoryStream(bytes);
                    using var gz = new GZipStream(input, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    gz.CopyTo(output);
                    return Encoding.UTF8.GetString(output.ToArray());
                }
                else
                {
                    return Encoding.UTF8.GetString(bytes);
                }
            }
            catch
            {
                // Fallback to plain UTF‑8 if decompression fails
                return Encoding.UTF8.GetString(bytes);
            }
        }

        /// <summary>
        /// Downloads the EPG XML from the configured URL or local path and
        /// writes it to the cache file.  If the source is invalid or the
        /// download fails, an error message is shown.  On success, the
        /// path of the cached file is displayed to the user.
        /// </summary>
        [RelayCommand]
        private async Task DownloadEpg()
        {
            // Show that a download is in progress and reset progress to 0
            IsEpgDownloading = true;
            EpgDownloadProgress = 0;

            if (string.IsNullOrWhiteSpace(XmltvUrl))
            {
                MessageBox.Show("Set an EPG (XMLTV) URL or file first.", "EPG", MessageBoxButton.OK, MessageBoxImage.Information);
                IsEpgDownloading = false;
                return;
            }

            try
            {
                string xml;
                // Handle HTTP sources with progress reporting
                if (Uri.TryCreate(XmltvUrl, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    using var http = new HttpClient();
                    try
                    {
                        // Initiate a streaming download to allow progress updates
                        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();
                        long? contentLength = response.Content.Headers.ContentLength;
                        using var stream = await response.Content.ReadAsStreamAsync();
                        using var ms = new System.IO.MemoryStream();
                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int read;
                        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                        {
                            ms.Write(buffer, 0, read);
                            totalRead += read;
                            if (contentLength.HasValue && contentLength.Value > 0)
                            {
                                var percent = (int)(totalRead * 100 / contentLength.Value);
                                // Avoid reporting >100 due to rounding
                                if (percent > 100) percent = 100;
                                EpgDownloadProgress = percent;
                            }
                        }
                        // Convert to string, decompress if necessary
                        xml = ConvertEpgBytesToString(ms.ToArray(), XmltvUrl);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to download EPG: {ex.Message}", ex);
                    }
                }
                // Handle local files (and optionally .gz) with progress set to 100 immediately
                else if (System.IO.File.Exists(XmltvUrl))
                {
                    byte[] bytes = await System.IO.File.ReadAllBytesAsync(XmltvUrl);
                    EpgDownloadProgress = 100;
                    xml = ConvertEpgBytesToString(bytes, XmltvUrl);
                }
                else
                {
                    MessageBox.Show("EPG source not found.", "EPG", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var cachePath = GetEpgCachePath();
                await System.IO.File.WriteAllTextAsync(cachePath, xml);
                MessageBox.Show($"EPG cached to:\n{cachePath}", "EPG", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to download EPG:\n" + ex.Message, "EPG", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Hide progress indicator when complete
                IsEpgDownloading = false;
                EpgDownloadProgress = 0;
            }
        }

        /// <summary>
        /// Forces a refresh of only the playlist using the current settings.  This
        /// command reloads the channel list without touching the cached EPG or
        /// triggering a new EPG download.  It is used when the user clicks
        /// the "Refresh playlist" button in the settings dialog.  If the
        /// main window is not available, the method does nothing.
        /// </summary>
        [RelayCommand]
        private async Task RefreshPlaylist()
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                await mw.RefreshPlaylistFromSettingsAsync();
            }
        }

        /// <summary>
        /// Forces a refresh of only the EPG using the current settings.  This reloads the guide
        /// data without reloading the playlist.  If the main window is not available, the method
        /// does nothing.
        /// </summary>
        [RelayCommand]
        private async Task RefreshEpg()
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                await mw.RefreshEpgFromSettingsAsync();
            }
        }
    }
}