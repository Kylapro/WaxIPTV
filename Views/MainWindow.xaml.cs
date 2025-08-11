using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading.Tasks;
using WaxIPTV.Models;
using WaxIPTV.Services;

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

            // Asynchronously load channels and EPG
            await LoadChannelsAsync(settings.PlaylistUrl);
            ChannelList.SetItems(_channels);
            // Populate the group filter and apply default filtering.  This must be
            // executed on the UI thread; we are already on it because this method
            // runs after InitializeComponent.  The method will set the
            // ComboBox items and apply filtering based on the current search term and
            // selected group (initially "All").
            PopulateGroupFilterAndApply();
            await LoadEpgAsync(settings.XmltvUrl, settings.EpgRefreshHours);

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
            // Ensure we have the latest settings from disk
            _settingsService.Load();
            var s = _settingsService.Settings;

            // Cancel any ongoing EPG refresh loop
            _epgRefreshCts?.Cancel();

            // Load channels
            await LoadChannelsAsync(s.PlaylistUrl);
            ChannelList.SetItems(_channels);
            PopulateGroupFilterAndApply();

            // Load EPG with force to ignore TTL and refresh interval
            await LoadEpgAsync(s.XmltvUrl, s.EpgRefreshHours, force: true);
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
            var channels = new List<Channel>();
            if (!string.IsNullOrWhiteSpace(source))
            {
                try
                {
                    string content;
                    if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        using var client = new System.Net.Http.HttpClient();
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
                        channels = M3UParser.Parse(content);
                }
                catch
                {
                    channels = new List<Channel>();
                }
            }
            if (channels.Count == 0)
            {
                // Provide placeholders when no channels loaded
                channels = new List<Channel>
                {
                    new Channel("ch1", "Channel 1", null, null, "", null),
                    new Channel("ch2", "Channel 2", null, null, "", null),
                    new Channel("ch3", "Channel 3", null, null, "", null)
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
            // If not forcing and the last load was within the refresh interval, skip reloading
            if (!force && _epgLoadedAt + TimeSpan.FromHours(refreshHours) > DateTimeOffset.UtcNow)
                return;

            var programmesDict = new Dictionary<string, List<Programme>>();
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
                    }
                    catch
                    {
                        // ignore errors reading cache
                    }
                }
            }

            // 2) Otherwise attempt to download or read from the configured source
            if (xml == null && !string.IsNullOrWhiteSpace(source))
            {
                try
                {
                    string fetched;
                    if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    {
                        using var client = new System.Net.Http.HttpClient();
                        fetched = await client.GetStringAsync(uri);
                    }
                    else if (File.Exists(source))
                    {
                        fetched = await File.ReadAllTextAsync(source);
                    }
                    else
                    {
                        fetched = string.Empty;
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
                catch
                {
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
                catch
                {
                    xml = null;
                }
            }

            // 4) Parse the XML if available
            if (!string.IsNullOrEmpty(xml))
            {
                try
                {
                    var (channelNames, programmes) = Xmltv.Parse(xml);
                    var ordered = programmes.OrderBy(p => p.StartUtc).ToList();
                    programmesDict = EpgMapper.MapProgrammes(ordered, _channels, channelNames);
                    // Trim programmes beyond 7 days to limit memory usage
                    var cutoff = DateTimeOffset.UtcNow.AddDays(7);
                    foreach (var kv in programmesDict)
                    {
                        kv.Value.RemoveAll(p => p.EndUtc > cutoff);
                    }
                    _epgLoadedAt = DateTimeOffset.UtcNow;
                }
                catch
                {
                    programmesDict.Clear();
                }
            }

            _programmes = programmesDict;
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
                SelectedChannelTitle.Text = string.Empty;
                NowNext.UpdateProgrammes(null, null);
                return;
            }
            SelectedChannelTitle.Text = selected.Name;
            if (_programmes.TryGetValue(selected.Id, out var progs))
            {
                var (now, next) = EpgHelpers.GetNowNext(progs, DateTimeOffset.UtcNow);
                NowNext.UpdateProgrammes(now, next);
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
            }
            else
            {
                NowNext.UpdateProgrammes(null, null);
            }
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
            if (_player == null)
                return;
            if (string.IsNullOrWhiteSpace(ch.StreamUrl))
                return;
            try
            {
                // Use LoadAsync for mpv if it's already running
                if (_player is MpvController mpv && mpv.IsRunning && _playerStarted)
                {
                    await mpv.LoadAsync(ch.StreamUrl);
                }
                else
                {
                    await _player.StartAsync(ch.StreamUrl, ch.Name);
                    _playerStarted = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start playback: {ex.Message}", "Playback Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Invoked when a channel is activated by double click from the
        /// ChannelList user control.  Delegates to PlayChannelAsync.
        /// </summary>
        private async void ChannelList_ChannelActivated(object? sender, Channel ch)
        {
            await PlayChannelAsync(ch);
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
    }
}