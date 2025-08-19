using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Text;
using WaxIPTV.Models;
using WaxIPTV.Services;
using WaxIPTV.Services.Logging;

namespace WaxIPTV.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml.  This class wires up basic
    /// functionality to load settings, populate the channel list, load
    /// EPG data and handle playback controls via external players.  It
    /// intentionally avoids heavy MVVM patterns to provide a clear
    /// starting point for further development.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly SettingsService _settingsService;
        private IPlayerControl? _player;
        private List<Channel> _channels = new();
        private Dictionary<string, List<Programme>> _programmes = new();
        private DateTimeOffset _epgLoadedAt = DateTimeOffset.MinValue;
        // Timer used to refresh the Now/Next display on a fixed schedule.
        private System.Windows.Threading.DispatcherTimer? _nowNextTimer;
        // Background task cancellation token for EPG refresh (optional)
        private System.Threading.CancellationTokenSource? _epgRefreshCts;
        // Tracks whether the external player has already been started.  Used
        // to determine if we should use LoadAsync on mpv for subsequent
        // zaps instead of spawning a new process.
        private bool _playerStarted;

        // Fields used for filtering channels by group and search term.  The
        // default group is "All" which represents no specific filter.
        private string _searchTerm = string.Empty;
        private string? _selectedGroup = "All";

        // Remember the last playlist and EPG keys so we can detect when
        // the user changes inputs.  A key is the trimmed, lower-case URL or file path.
        private string? _lastPlaylistKey;
        private string? _lastEpgKey;

        /// <summary>
        /// Normalizes a playlist or EPG source string into a lowercase key.
        /// Returns an empty string for null or whitespace.
        /// </summary>
        private static string KeyFor(string? s)
        {
            return string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim().ToLowerInvariant();
        }

        public MainWindow()
        {
            InitializeComponent();
            _settingsService = new SettingsService();
            Loaded += async (_, __) => await LoadFromSettingsAsync();
            // Register a handler for selection changes on the ListBox within
            // ChannelList.  This updates the Now/Next view and channel title
            // whenever the user clicks a different item.  We cannot set
            // SelectionChanged directly in XAML on the user control because
            // the ListBox is defined in the control template.
            ChannelList.ChannelActivated += ChannelList_ChannelActivated;
            // Subscribe to the user control's SelectionChanged event rather than
            // accessing the internal ListBox directly.
            ChannelList.SelectionChanged += ChannelList_SelectionChanged;
        }

        /// <summary>
        /// Returns the full path to the EPG cache file.  The file is stored
        /// under the user's LocalApplicationData\WaxIPTV folder and will be
        /// created on demand.  This helper is used to locate the cache
        /// when loading and saving the EPG.
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
        /// UTF-8 string. If the source ends with ".gz", the bytes are
        /// decompressed using <see cref="GZipStream"/> before decoding. If
        /// decompression fails, the bytes are decoded directly. This mirrors the
        /// logic used in the settings view model for downloading EPG data.
        /// </summary>
        /// <param name="bytes">Raw bytes downloaded or read from disk.</param>
        /// <param name="source">Original URL or file path to determine if gzipped.</param>
        /// <returns>Decoded XML string.</returns>
        private static string ConvertEpgBytesToString(byte[] bytes, string source)
        {
            // Convert raw bytes representing an XMLTV document into a UTF‑8 string.  If the
            // data is gzip‑compressed, decompress it before decoding.  Detection is
            // performed both on the file extension (".gz") and on the gzip magic
            // numbers (0x1F, 0x8B).  Any errors during decompression fall back to
            // returning the raw bytes as UTF‑8.
            try
            {
                bool extIsGz = !string.IsNullOrEmpty(source) && source.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);
                bool magicIsGz = bytes.Length > 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;
                if (extIsGz || magicIsGz)
                {
                    using var ms = new MemoryStream(bytes);
                    using var gz = new GZipStream(ms, CompressionMode.Decompress);
                    using var sr = new StreamReader(gz, Encoding.UTF8);
                    return sr.ReadToEnd();
                }
                // Not gzipped; decode directly.  Encoding.GetString will handle a BOM if present.
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                // Fallback: return the raw bytes as UTF‑8 if anything goes wrong.
                return Encoding.UTF8.GetString(bytes);
            }
        }

        /// <summary>
        /// Reloads the channel list and EPG using the latest settings.
        /// This convenience method is called from App.xaml.cs when
        /// the settings dialog is shown during startup.  It simply
        /// delegates to the asynchronous LoadFromSettingsAsync method.
        /// </summary>
        public async void ReloadFromSettings()
        {
            await LoadFromSettingsAsync();
        }

        /// <summary>
        /// Reloads channels and EPG data from the current settings.  This
        /// method can be invoked after the user saves changes in the
        /// settings dialog.  It clears the existing collections and
        /// repopulates them from the latest configuration.  Because it
        /// performs file I/O, it is implemented asynchronously.
        /// </summary>
        public async System.Threading.Tasks.Task LoadFromSettingsAsync()
        {
            AppLog.Logger.Information("Loading configuration and data");
            // Cancel any pending EPG refresh loop before reloading
            _epgRefreshCts?.Cancel();

            // Load settings from disk
            _settingsService.Load();
            var settings = _settingsService.Settings;

            // Initialise or update the external player controller based
            // on the selected player type.  Delay instantiation until
            // first playback if the path is missing.  The player will be
            // created in PlayChannelAsync if null.
            _player = null;
            _playerStarted = false;
            if (string.Equals(settings.Player, "vlc", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(settings.VlcPath))
                    _player = new VlcController(settings.VlcPath);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(settings.MpvPath))
                    _player = new MpvController(settings.MpvPath);
            }
            // Disable playback buttons when no player is available
            bool hasPlayer = _player != null;
            PlayButton.IsEnabled = PauseButton.IsEnabled = StopButton.IsEnabled = hasPlayer;

            // Remember the current playlist and EPG keys so that future calls can detect changes.
            _lastPlaylistKey = KeyFor(settings.PlaylistUrl);
            _lastEpgKey = KeyFor(settings.XmltvUrl);

            // Asynchronously load channels
            await LoadChannelsAsync(settings.PlaylistUrl);
            AppLog.Logger.Information("Loaded {Count} channels", _channels.Count);
            ChannelList.SetItems(_channels);
            // Populate the group filter and apply default filtering.  This must be
            // executed on the UI thread; we are already on it because this method
            // runs after InitializeComponent.  The method will set the
            // ComboBox items and apply filtering based on the current search term and
            // selected group (initially "All").
            PopulateGroupFilterAndApply();

            // Load the EPG with force so that the new playlist and EPG are fully mapped.  This
            // initial load should bypass any refresh window and always parse and map the
            // programmes.  Subsequent reloads will be controlled by the EPG refresh loop or
            // user actions.
            await LoadEpgAsync(settings.XmltvUrl, settings.EpgRefreshHours, force: true);
            AppLog.Logger.Information("EPG loaded with {ProgrammeCount} programmes", _programmes.Sum(kv => kv.Value.Count));

            // Update the UI for the first selection if any
            if (_channels.Count > 0)
            {
                // Select the first channel by default
                ChannelList.SelectFirst();
            }
            else
            {
                SelectedChannelTitle.Text = string.Empty;
                NowNext.UpdateProgrammes(null, null);
            }

            // Start or restart the Now/Next timer
            _nowNextTimer?.Stop();
            _nowNextTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _nowNextTimer.Tick += (_, __) => UpdateNowNextForSelected();
            _nowNextTimer.Start();

            // Launch background EPG refresh loop
            _epgRefreshCts = new System.Threading.CancellationTokenSource();
            var token = _epgRefreshCts.Token;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(TimeSpan.FromHours(settings.EpgRefreshHours), token);
                        if (token.IsCancellationRequested) break;
                        // Reload EPG on the UI thread so we can update Now/Next
                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            await LoadEpgAsync(settings.XmltvUrl, settings.EpgRefreshHours);
                            UpdateNowNextForSelected();
                        });
                    }
                    catch (System.OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // Ignore other errors and continue
                    }
                }
            }, token);
        }

        /// <summary>
        /// Forces a refresh of the playlist and EPG using the current settings.
        /// This method is invoked from the settings view model when the user
        /// clicks the Refresh button.  It reloads the channels and then
        /// reloads the EPG bypassing the refresh interval and cache TTL.
        /// After reloading, the channel list and Now/Next panel are updated.
        /// </summary>
        public async System.Threading.Tasks.Task RefreshFromSettingsAsync()
        {
            AppLog.Logger.Information("Refreshing data from settings");
            // Ensure we have the latest settings from disk
            _settingsService.Load();
            var s = _settingsService.Settings;

            // Update the last playlist and EPG keys so that subsequent calls to
            // LoadEpgAsync can detect if these inputs change.  Using KeyFor
            // normalises the URL or file path to lower‑case.  Setting these
            // ahead of loading ensures that changes in the settings file will
            // trigger a forced reload when necessary.
            _lastPlaylistKey = KeyFor(s.PlaylistUrl);
            _lastEpgKey = KeyFor(s.XmltvUrl);

            // Cancel any ongoing EPG refresh loop
            _epgRefreshCts?.Cancel();

            // Load channels
            await LoadChannelsAsync(s.PlaylistUrl);
            AppLog.Logger.Information("Reloaded {Count} channels", _channels.Count);
            ChannelList.SetItems(_channels);
            PopulateGroupFilterAndApply();

            // Load EPG with force to ignore TTL and refresh interval
            await LoadEpgAsync(s.XmltvUrl, s.EpgRefreshHours, force: true);
            AppLog.Logger.Information("Refreshed EPG with {Programmes} programmes", _programmes.Sum(kv => kv.Value.Count));
            UpdateNowNextForSelected();

            // Restart the Now/Next timer with the new refresh interval
            _nowNextTimer?.Stop();
            _nowNextTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _nowNextTimer.Tick += (_, __) => UpdateNowNextForSelected();
            _nowNextTimer.Start();

            // Restart the background EPG refresh loop
            _epgRefreshCts = new System.Threading.CancellationTokenSource();
            var token = _epgRefreshCts.Token;
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(TimeSpan.FromHours(s.EpgRefreshHours), token);
                        if (token.IsCancellationRequested) break;
                        await Application.Current.Dispatcher.InvokeAsync(async () =>
                        {
                            await LoadEpgAsync(s.XmltvUrl, s.EpgRefreshHours);
                            UpdateNowNextForSelected();
                        });
                    }
                    catch (System.OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // ignore other errors
                    }
                }
            }, token);
        }

        /// <summary>
        /// Reloads the playlist from the current settings without touching the EPG.  This
        /// helper is invoked from the settings view model when the user chooses to
        /// refresh only the playlist.  It updates the channel list and applies
        /// filtering, but leaves the existing EPG mapping intact.  After
        /// reloading, the first channel is selected (if available) and the
        /// Now/Next panel is updated for the selected channel based on the
        /// previously loaded EPG.
        /// </summary>
        public async System.Threading.Tasks.Task RefreshPlaylistFromSettingsAsync()
        {
            // Ensure latest settings are loaded
            _settingsService.Load();
            var s = _settingsService.Settings;

            // Update the last playlist key so that if a new playlist file or URL
            // is selected, the EPG loader can detect a change and reload the
            // guide even if the EPG URL remains the same.  Do not update
            // _lastEpgKey here since we are not reloading the EPG.
            _lastPlaylistKey = KeyFor(s.PlaylistUrl);
            // Load channels only
            await LoadChannelsAsync(s.PlaylistUrl);
            ChannelList.SetItems(_channels);
            PopulateGroupFilterAndApply();
            // Select first channel or clear selection
            if (_channels.Count > 0)
            {
                ChannelList.SelectFirst();
                UpdateNowNextForSelected();
            }
            else
            {
                SelectedChannelTitle.Text = string.Empty;
                NowNext.UpdateProgrammes(null, null);
            }
        }

        /// <summary>
        /// Loads the M3U playlist from a file path or URL.  For local
        /// files, File.ReadAllText is used.  For HTTP/HTTPS sources,
        /// HttpClient downloads the content.  On any failure, the
        /// channel list is reset to a few placeholders.
        /// </summary>
        /// <param name="source">Path or URL of the playlist.</param>
        private async System.Threading.Tasks.Task LoadChannelsAsync(string? source)
        {
            using var scope = AppLog.BeginScope("LoadChannels");
            var channels = new List<Channel>();
            if (!string.IsNullOrWhiteSpace(source))
            {
                try
                {
                    string content;
                    if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        using var client = new System.Net.Http.HttpClient()
                        {
                            Timeout = TimeSpan.FromSeconds(15)
                        };
                        // Download playlist content with a reasonable timeout.  If the request
                        // times out or fails, an exception will be caught below and the
                        // placeholder channels will be used instead.
                        content = await client.GetStringAsync(uri);
                    }
                    else if (File.Exists(source))
                    {
                        content = await File.ReadAllTextAsync(source);
                    }
                    else
                    {
                        content = string.Empty;
                    }
                    if (!string.IsNullOrEmpty(content))
                    {
                        AppLog.Logger.Information("Parsing playlist from {Source}", AppLog.Safe(source));
                        channels = M3UParser.Parse(content);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Logger.Error(ex, "Failed to load playlist from {Source}", AppLog.Safe(source));
                    channels = new List<Channel>();
                }
            }
            if (channels.Count == 0)
            {
                // Provide placeholders when no channels loaded
                channels = new List<Channel>
                {
                    new Channel("ch1", "Channel 1", null, null, "", null, null, null),
                    new Channel("ch2", "Channel 2", null, null, "", null, null, null),
                    new Channel("ch3", "Channel 3", null, null, "", null, null, null)
                };
            }
            _channels = channels;
        }

        /// <summary>
        /// Loads and parses the XMLTV EPG data from a file or URL.  The
        /// programmes are keyed by channel ID.  Programmes beyond
        /// seven days from now are pruned to minimise memory usage.  The
        /// method returns when parsing is complete.
        /// </summary>
        private async System.Threading.Tasks.Task LoadEpgAsync(string? source, int refreshHours, bool force = false)
        {
            using var scope = AppLog.BeginScope("LoadEpg");
            // If the EPG source has changed since the last load, always force a reload.  We
            // compare the normalized key of the incoming source against the last used
            // key.  If they differ (including the initial load where _lastEpgKey may be
            // null), update the stored key and set force=true so that the EPG is
            // re-downloaded and re-mapped regardless of the refresh window.
            var currentEpgKey = KeyFor(source);
            if (_lastEpgKey != null && currentEpgKey != _lastEpgKey)
            {
                _lastEpgKey = currentEpgKey;
                force = true;
            }
            else if (_lastEpgKey == null)
            {
                _lastEpgKey = currentEpgKey;
            }

            // If not forcing and the last load was within the refresh interval, skip reloading
            if (!force && _epgLoadedAt + TimeSpan.FromHours(refreshHours) > DateTimeOffset.UtcNow)
            {
                AppLog.Logger.Information("Skipping EPG reload; within refresh window");
                return;
            }

            // Show the main window EPG loading indicator at the beginning of a new load.  Use
            // Dispatcher to ensure UI updates occur on the correct thread.
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (MainEpgLoadingPanel != null)
                    {
                        MainEpgLoadingPanel.Visibility = Visibility.Visible;
                        MainEpgLoadingLabel.Text = "Loading EPG...";
                        MainEpgProgressBar.IsIndeterminate = true;
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Logger.Warning(ex, "Failed to update EPG loading UI");
                // Ignore UI update errors; loading will continue without a progress indicator.
            }

            var programmesDict = new Dictionary<string, List<Programme>>();
            _programmes = programmesDict;
            var cachePath = GetEpgCachePath();
            string? xml = null;

            // 1) If not forcing and a fresh cache file exists, use it
            if (!force && File.Exists(cachePath))
            {
                var freshCutoff = DateTime.UtcNow - TimeSpan.FromHours(refreshHours);
                if (File.GetLastWriteTimeUtc(cachePath) >= freshCutoff)
                {
                    try
                    {
                        xml = await File.ReadAllTextAsync(cachePath);
                        AppLog.Logger.Information("Loaded EPG from cache");
                    }
                    catch (Exception ex)
                    {
                        AppLog.Logger.Warning(ex, "Failed reading EPG cache");
                    }
                }
            }

            // 2) Otherwise attempt to download or read from the configured source.
            //    Use XmltvTextLoader to handle both remote URLs and local files.
            if (xml == null && !string.IsNullOrWhiteSpace(source))
            {
                try
                {
                    string fetched = string.Empty;

                    // Only attempt to load if the source is a valid URL or existing file.
                    if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        // Use a cancellation token to impose a timeout on EPG downloads
                        try
                        {
                            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                            fetched = await XmltvTextLoader.LoadAsync(source, cts.Token);
                        }
                        catch (System.OperationCanceledException)
                        {
                            // Timeout reached; leave fetched empty to trigger fallback
                            fetched = string.Empty;
                        }
                    }
                    else if (File.Exists(source))
                    {
                        // Local files are read synchronously without a timeout
                        fetched = await XmltvTextLoader.LoadAsync(source);
                    }

                    if (!string.IsNullOrEmpty(fetched))
                    {
                        xml = fetched;
                        // write/update the cache
                        try
                        {
                            await File.WriteAllTextAsync(cachePath, fetched);
                        }
                        catch
                        {
                            // ignore cache write errors
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Logger.Error(ex, "Failed to fetch EPG from {Source}", AppLog.Safe(source));
                    // ignore fetch errors and fall back to cache
                }
            }

            // 3) If still no XML, try reading stale cache as fallback
            if (xml == null && File.Exists(cachePath))
            {
                try
                {
                    xml = await File.ReadAllTextAsync(cachePath);
                }
                catch (Exception ex)
                {
                    xml = null;
                    AppLog.Logger.Warning(ex, "Failed to read stale EPG cache");
                }
            }

            // 4) Parse the XML if available
            if (!string.IsNullOrEmpty(xml))
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        AppLog.Logger.Information("Parsing EPG XML");
                    var channelNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    int totalProgrammes = 0;
                    int shiftMinutes = 0;
                    try
                    {
                        shiftMinutes = _settingsService.Settings.EpgShiftMinutes;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Logger.Warning(ex, "Failed applying EPG time shift");
                    }

                    var programmeStream = Xmltv.StreamProgrammes(xml, channelNames).Select(p =>
                    {
                        totalProgrammes++;
                        return shiftMinutes != 0
                            ? p with
                            {
                                StartUtc = p.StartUtc.AddMinutes(shiftMinutes),
                                EndUtc = p.EndUtc.AddMinutes(shiftMinutes)
                            }
                            : p;
                    });

                    // Pass any manual EPG ID aliases from settings to the mapper.  These aliases
                    // allow users to map playlist channel names to specific XMLTV IDs when
                    // automatic or fuzzy matching fails.  The aliases dictionary is copied to
                    // ensure case-insensitive keys.
                    Dictionary<string, string>? overrides = null;
                    try
                    {
                        var aliasMap = _settingsService.Settings.EpgIdAliases;
                        if (aliasMap != null && aliasMap.Count > 0)
                        {
                            overrides = new Dictionary<string, string>(aliasMap, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                    catch (Exception ex)
                    {
                        // ignore alias loading errors and fall back to null
                        AppLog.Logger.Warning(ex, "Error processing EPG ID aliases");
                        overrides = null;
                    }

                    // Map the programmes to channels using the batched mapper. The stream
                    // enumerates lazily, keeping memory usage low when handling large EPGs.
                    var progress = new Progress<int>(count =>
                    {
                        try
                        {
                            Dispatcher.Invoke(() =>
                            {
                                if (MainEpgLoadingLabel != null)
                                    MainEpgLoadingLabel.Text = $"Loading EPG... ({count} programmes)";
                                // Update now/next display as data becomes available
                        UpdateNowNextForSelected();
                            });
                        }
                        catch
                        {
                            // ignore UI update errors
                        }
                    });
                    programmesDict = EpgMapper.MapProgrammesInBatches(programmeStream, _channels, channelNames, 200, overrides, progress, programmesDict);
                    AppLog.Logger.Information("Mapping {ProgCount} programmes", totalProgrammes);
                    // Trim programmes beyond 7 days to limit memory usage
                    var cutoff = DateTimeOffset.UtcNow.AddDays(7);
                    foreach (var kv in programmesDict)
                    {
                        kv.Value.RemoveAll(p => p.EndUtc > cutoff);
                    }
                    _epgLoadedAt = DateTimeOffset.UtcNow;

                    // -------------------------------------------------------------------
                    // After mapping, record diagnostics and update counters.  The counters
                    // show how many channels and programmes were parsed and mapped.  The
                    // diagnostics file lists any playlist channels that were not mapped,
                    // along with suggestions for epgIdAliases to help the user fix
                    // mismatches between playlist names/ids and the XMLTV guide.
                    try
                    {
                        // Compute counts
                        int epgChannelNames = channelNames.Count;
                        int totalProgrammesCount = totalProgrammes;
                        int mappedChannelsCount = programmesDict.Count;
                        int mappedProgrammesCount = 0;
                        foreach (var list in programmesDict.Values)
                        {
                            mappedProgrammesCount += list.Count;
                        }

                        // Update the in-app counters on the UI thread
                        Dispatcher.Invoke(() =>
                        {
                            if (EpgCountersText != null)
                            {
                                EpgCountersText.Text = $"EPG: channels in XMLTV={epgChannelNames}, programmes={totalProgrammesCount}, mapped channels={mappedChannelsCount}, mapped programmes={mappedProgrammesCount}";
                            }
                        });

                        // Build diagnostics file listing unmatched channels and alias suggestions
                        var allPlaylist = _channels ?? new List<Channel>();
                        var mappedSet = new HashSet<string>(programmesDict.Keys);
                        var missing = allPlaylist.Where(ch => !mappedSet.Contains(ch.Id)).ToList();

                        // Write diagnostics file
                        var diagPath = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "WaxIPTV", "epg-diagnostics.txt");
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(diagPath)!);
                        using (var sw = new System.IO.StreamWriter(diagPath, false, System.Text.Encoding.UTF8))
                        {
                            sw.WriteLine($"[EPG Diagnostics] {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
                            sw.WriteLine($"Playlist channels: {allPlaylist.Count}");
                            sw.WriteLine($"Mapped channels:   {mappedSet.Count}");
                            sw.WriteLine($"Unmatched (playlist but no EPG): {missing.Count}");
                            sw.WriteLine();
                            // Build lookup from display-name to XMLTV id
                            var epgNameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var kv in channelNames)
                            {
                                // kv.Key = xmltv channel id, kv.Value = display name
                                epgNameToId[kv.Value] = kv.Key;
                            }
                            string Normalize(string s)
                            {
                                if (string.IsNullOrWhiteSpace(s)) return string.Empty;
                                var sb = new System.Text.StringBuilder();
                                foreach (var ch in s.ToLowerInvariant())
                                    if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                                var norm = sb.ToString();
                                string[] suffixes = { "uhd", "fhd", "hd", "sd", "4k" };
                                bool stripped;
                                do
                                {
                                    stripped = false;
                                    foreach (var sfx in suffixes)
                                    {
                                        if (norm.EndsWith(sfx, StringComparison.Ordinal))
                                        {
                                            norm = norm.Substring(0, norm.Length - sfx.Length);
                                            stripped = true;
                                            break;
                                        }
                                    }
                                } while (stripped);
                                return norm;
                            }
                            // Write suggestions
                            foreach (var ch in missing)
                            {
                                var norm = Normalize(ch.TvgName ?? ch.Name);
                                if (!string.IsNullOrEmpty(ch.TvgId))
                                {
                                    sw.WriteLine($"- {ch.Name}  (has tvg-id=\"{ch.TvgId}\") → XMLTV id must equal that value");
                                }
                                else if (epgNameToId.TryGetValue(ch.TvgName ?? ch.Name, out var xmltvId))
                                {
                                    sw.WriteLine($"- {ch.Name}  suggestion: epgIdAliases[\"{norm}\"] = \"{xmltvId}\"");
                                }
                                else
                                {
                                    // Fuzzy match: first display-name normalizing to same string
                                    var fuzzy = epgNameToId.FirstOrDefault(p => Normalize(p.Key) == norm);
                                    if (!string.IsNullOrWhiteSpace(fuzzy.Key))
                                    {
                                        sw.WriteLine($"- {ch.Name}  suggestion: epgIdAliases[\"{norm}\"] = \"{fuzzy.Value}\"  // matches display-name \"{fuzzy.Key}\"");
                                    }
                                    else
                                    {
                                        sw.WriteLine($"- {ch.Name}  (no obvious XMLTV match) — check playlist tvg-id/tvg-name vs XMLTV <display-name>");
                                    }
                                }
                            }
                            sw.WriteLine();
                            sw.WriteLine("Edit settings.json: add epgIdAliases entries like:");
                            sw.WriteLine("{");
                            sw.WriteLine("  \"epgIdAliases\": {");
                            sw.WriteLine("    \"normalizedplaylistname\": \"xmltv-channel-id\"");
                            sw.WriteLine("  }");
                            sw.WriteLine("}");
                        }
                    }
                    catch
                    {
                        // ignore diagnostics errors
                    }
                }
                catch (Exception ex)
                {
                    programmesDict.Clear();
                    AppLog.Logger.Error(ex, "Failed to parse or map EPG");
                }
                });
            }

            _programmes = programmesDict;
            AppLog.Logger.Information("EPG load complete with {Programmes} programmes", programmesDict.Sum(kv => kv.Value.Count));

            // Hide the main window EPG loading indicator when loading completes.  Also update
            // the status text to indicate completion.  Use Dispatcher to marshal back to
            // the UI thread.
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (MainEpgLoadingPanel != null)
                    {
                        MainEpgProgressBar.IsIndeterminate = false;
                        MainEpgLoadingLabel.Text = "EPG loaded";
                        MainEpgLoadingPanel.Visibility = Visibility.Collapsed;
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Logger.Warning(ex, "Failed to update EPG UI after load");
                // ignore UI update errors
            }
        }

        /// <summary>
        /// Handles selection changes in the channel list.  When a new
        /// channel is selected, update the channel title and Now/Next
        /// programme display immediately.  This method is wired to
        /// ChannelList.ChannelList.SelectionChanged.
        /// </summary>
        private void ChannelList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ChannelList.GetSelected() is not Channel selected)
            {
                AppLog.Logger.Information("Channel selection cleared");
                SelectedChannelTitle.Text = string.Empty;
                NowNext.UpdateProgrammes(null, null);
                return;
            }
            AppLog.Logger.Information("Channel selected {Name}", selected.Name);
            SelectedChannelTitle.Text = selected.Name;
            if (_programmes.TryGetValue(selected.Id, out var progs))
            {
                var (now, next) = EpgHelpers.GetNowNext(progs, DateTimeOffset.UtcNow);
                NowNext.UpdateProgrammes(now, next);
                AppLog.Logger.Information("Now playing {Now} next {Next}", now?.Title, next?.Title);
            }
            else
            {
                NowNext.UpdateProgrammes(null, null);
            }
        }

        /// <summary>
        /// Updates the Now/Next display for the currently selected channel.  A
        /// helper to refresh the view on a timer or after EPG reload.
        /// </summary>
        private void UpdateNowNextForSelected()
        {
            if (ChannelList.GetSelected() is not Channel selected)
                return;
            if (_programmes.TryGetValue(selected.Id, out var progs))
            {
                var (now, next) = EpgHelpers.GetNowNext(progs, DateTimeOffset.UtcNow);
                NowNext.UpdateProgrammes(now, next);
                AppLog.Logger.Information("Updated Now/Next for {Channel}: {Now} -> {Next}", selected.Name, now?.Title, next?.Title);
            }
            else
            {
                NowNext.UpdateProgrammes(null, null);
                AppLog.Logger.Information("No EPG data for {Channel}", selected.Name);
            }
        }

        /// <summary>
        /// Cleans up timers, cancels background tasks and disposes the player when
        /// the window is closed.  This prevents orphaned background threads or
        /// external player processes from lingering after the UI is closed.
        /// </summary>
        protected override async void OnClosed(EventArgs e)
        {
            AppLog.Logger.Information("Main window closing");
            // Stop the periodic Now/Next timer
            try
            {
                _nowNextTimer?.Stop();
            }
            catch (Exception ex)
            {
                AppLog.Logger.Warning(ex, "Error stopping timer");
            }
            // Cancel the EPG refresh loop
            try
            {
                _epgRefreshCts?.Cancel();
            }
            catch (Exception ex)
            {
                AppLog.Logger.Warning(ex, "Error cancelling EPG refresh");
            }
            // Shut down and dispose the external player
            if (_player != null)
            {
                try
                {
                    await _player.QuitAsync();
                }
                catch (Exception ex)
                {
                    AppLog.Logger.Warning(ex, "Error quitting player");
                }
                try
                {
                    await _player.DisposeAsync();
                }
                catch (Exception ex)
                {
                    AppLog.Logger.Warning(ex, "Error disposing player");
                }
            }
            base.OnClosed(e);
        }

        /// <summary>
        /// Starts playback of the selected channel.  Uses the active
        /// player controller to either start a new process or load
        /// the new URL into an existing mpv instance.  On error, a
        /// message box is displayed.  If no player is configured,
        /// the method returns immediately.
        /// </summary>
        private async System.Threading.Tasks.Task PlayChannelAsync(Channel ch)
        {
            AppLog.Logger.Information("Play request for {Channel}", ch.Name);
            // Lazy‑create a player controller on demand.  This allows users to
            // fix a missing or invalid player path in Settings and immediately
            // start playback without restarting the application.  If no player
            // can be created, disable playback buttons and show a message.
            if (_player == null)
            {
                var s = _settingsService.Settings;
                // Try VLC first if selected
                if (string.Equals(s.Player, "vlc", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(s.VlcPath))
                {
                    _player = new VlcController(s.VlcPath);
                }
                else if (!string.IsNullOrWhiteSpace(s.MpvPath))
                {
                    _player = new MpvController(s.MpvPath);
                }
                // Update button state based on whether a player was created
                bool hasPlayer = _player != null;
                PlayButton.IsEnabled = PauseButton.IsEnabled = StopButton.IsEnabled = hasPlayer;
                if (!hasPlayer)
                {
                    MessageBox.Show("No player configured. Open Settings and set mpv.exe or vlc.exe.",
                                    "Playback",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                    return;
                }
            }

            // Ensure we have a valid stream URL
            if (string.IsNullOrWhiteSpace(ch.StreamUrl))
                return;

            try
            {
                // Use LoadAsync for mpv if it's already running and we previously started it
                if (_player is MpvController mpv && mpv.IsRunning && _playerStarted)
                {
                    AppLog.Logger.Information("Using existing mpv instance");
                    await mpv.LoadAsync(ch.StreamUrl, ch.Headers);
                }
                else
                {
                    AppLog.Logger.Information("Starting player process");
                    await _player.StartAsync(ch.StreamUrl, ch.Name, ch.Headers);
                    _playerStarted = true;
                }
            }
            catch (Exception ex)
            {
                AppLog.Logger.Error(ex, "Failed to start playback");
                MessageBox.Show($"Failed to start playback: {ex.Message}", "Playback Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Invoked when a channel is activated by double click from the
        /// ChannelList user control.  Delegates to PlayChannelAsync.
        /// </summary>
        private async void ChannelList_ChannelActivated(object? sender, ChannelEventArgs e)
        {
            // When a channel is double‑clicked in the list view, invoke playback
            // using the encapsulated Channel from the event args.
            AppLog.Logger.Information("Channel double-clicked {Channel}", e.Channel.Name);
            await PlayChannelAsync(e.Channel);
        }

        /// <summary>
        /// Play button handler.  Invokes PlayChannelAsync on the
        /// currently selected channel.
        /// </summary>
        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = ChannelList.GetSelected();
            if (selected != null)
            {
                await PlayChannelAsync(selected);
            }
        }

        /// <summary>
        /// Pause button handler.  Toggles pause on the active player.
        /// </summary>
        private async void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_player != null)
            {
                try
                {
                    await _player.PauseAsync(true);
                }
                catch
                {
                    // Ignore errors on pause
                }
            }
        }

        /// <summary>
        /// Stop button handler.  Quits mpv or stops VLC playback and
        /// clears the player state so the next Play will start anew.
        /// </summary>
        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_player != null)
            {
                try
                {
                    switch (_player)
                    {
                        case MpvController mpv:
                            await mpv.QuitAsync();
                            break;
                        case VlcController vlc:
                            await vlc.StopAsync();
                            break;
                    }
                }
                catch
                {
                    // Ignore errors on stop
                }
            }
            _playerStarted = false;
        }

        /// <summary>
        /// Populates the group filter ComboBox and applies the default filter.  This
        /// method should be called after channels are loaded.  It extracts all
        /// distinct group names from the loaded channel list, adds an "All"
        /// option to represent no group filtering, and sets the selected
        /// group to "All" before applying the filter.
        /// </summary>
        private void PopulateGroupFilterAndApply()
        {
            // Build a sorted list of unique groups ignoring case
            var groups = _channels
                .Select(c => c.Group)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Insert the default "All" option at the beginning
            var items = new List<string> { "All" };
            items.AddRange(groups);

            GroupFilter.ItemsSource = items;
            GroupFilter.SelectedIndex = 0;
            _selectedGroup = "All";
            ApplyFilter();
        }

        /// <summary>
        /// Handles selection changes in the group filter ComboBox.  Updates
        /// the selected group and reapplies the channel filter.
        /// </summary>
        private void GroupFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedGroup = GroupFilter.SelectedItem as string;
            ApplyFilter();
        }

        /// <summary>
        /// Handles text changes in the search box.  Updates the search term
        /// and reapplies the channel filter on each keystroke.
        /// </summary>
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTerm = SearchBox.Text?.Trim() ?? string.Empty;
            ApplyFilter();
        }

        /// <summary>
        /// Clears the search box when the clear button is clicked.  Setting
        /// the Text property triggers the TextChanged event which in turn
        /// updates the filter.
        /// </summary>
        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
        }

        /// <summary>
        /// Applies the search and group filters to the channel list.  If the
        /// search term is empty, it does not filter on text.  If the selected
        /// group is "All" or empty, it does not filter on group.  After
        /// filtering, the channel list is updated via ChannelList.SetItems.
        /// If no channel is selected, the first item is automatically
        /// selected to ensure the Now/Next panel remains in sync.
        /// </summary>
        private void ApplyFilter()
        {
            IEnumerable<Channel> filtered = _channels;

            // Filter by search term across name, tvg-id and group (case-insensitive)
            if (!string.IsNullOrEmpty(_searchTerm))
            {
                var term = _searchTerm;
                filtered = filtered.Where(ch =>
                    (!string.IsNullOrEmpty(ch.Name) && ch.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(ch.TvgId) && ch.TvgId.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(ch.Group) && ch.Group.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }

            // Filter by selected group if not "All"
            if (!string.IsNullOrWhiteSpace(_selectedGroup) && !string.Equals(_selectedGroup, "All", StringComparison.OrdinalIgnoreCase))
            {
                var selGroup = _selectedGroup;
                filtered = filtered.Where(ch => string.Equals(ch.Group, selGroup, StringComparison.OrdinalIgnoreCase));
            }

            // Update channel list view
            ChannelList.SetItems(filtered.ToList());
            if (ChannelList.GetSelected() == null)
            {
                ChannelList.SelectFirst();
            }
        }

        /// <summary>
        /// Handler for the Settings menu item.  Displays the settings
        /// dialog; if the user saves changes, reloads the channels and EPG.
        /// </summary>
        private void SettingsMenu_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(_settingsService, _settingsService.Settings);
            if (dlg.ShowDialog() == true)
            {
                // After saving settings, reload channels and EPG
                _ = LoadFromSettingsAsync();
            }
        }

        /// <summary>
        /// Handler for the EPG Guide menu item.  Opens a new guide window
        /// displaying the programme guide for all channels.  Passes the
        /// current channels and programmes to the guide and subscribes
        /// to its ChannelSelected event so that clicking a programme
        /// triggers playback via PlayChannelAsync.
        /// </summary>
        private async void GuideMenu_Click(object sender, RoutedEventArgs e)
        {
            if (_channels == null || _channels.Count == 0)
            {
                MessageBox.Show("No channels are loaded yet.", "EPG Guide", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // Before opening the guide, ensure we only use cached EPG data.  We
            // load from the cache file directly by passing a null source to
            // LoadEpgAsync.  The force flag bypasses the refresh interval and
            // TTL checks so that the cache is always read.  If the cache
            // does not exist or cannot be parsed, the programmes dictionary
            // will be empty, resulting in an empty guide until the user
            // downloads a new EPG via the settings dialog.
            try
            {
                _settingsService.Load();
                var s = _settingsService.Settings;
                await LoadEpgAsync(null, s.EpgRefreshHours, force: true);
            }
            catch
            {
                // Ignore errors reading cache; the guide will simply show no programmes
            }

            var guide = new GuideWindow(_channels, _programmes);
            guide.Owner = this;
            guide.ChannelSelected += async (_, ch) =>
            {
                await PlayChannelAsync(ch);
            };
            guide.Show();
        }

        /// <summary>
        /// Handler for the Exit menu item.  Closes the main window,
        /// shutting down the application.
        /// </summary>
        private void ExitMenu_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Handler for the Refresh menu item.  Forces reload of the playlist and
        /// EPG using current settings.  This bypasses the normal refresh
        /// interval and always downloads or reads from cache immediately.
        /// </summary>
        private async void RefreshMenu_Click(object sender, RoutedEventArgs e)
        {
            await RefreshFromSettingsAsync();
        }

        /// <summary>
        /// Handler for the "Reload EPG (force)" menu item.  Forces the EPG to be
        /// reloaded from the current settings without reloading the playlist.
        /// This bypasses the refresh interval and cache time‑to‑live, and it
        /// resets the EPG loaded time so subsequent automatic refreshes occur on
        /// the configured interval.  The channel list remains unchanged.
        /// </summary>
        private async void ReloadEpgForce_Click(object sender, RoutedEventArgs e)
        {
            // Ensure we have the latest settings from disk in case the user
            // recently changed the EPG URL or refresh interval.  We do not
            // reload the playlist here because the user may want to keep the
            // current list intact.  Forcing a reload ensures that any
            // modifications to the XMLTV source take effect immediately.
            _settingsService.Load();
            var s = _settingsService.Settings;

            // Force load the EPG ignoring TTL and refresh interval.  By
            // specifying force=true we also bypass the cache and always
            // download/decompress the latest guide.  After loading, update
            // Now/Next for the currently selected channel.
            await LoadEpgAsync(s.XmltvUrl, s.EpgRefreshHours, force: true);
            UpdateNowNextForSelected();
        }
    }
}