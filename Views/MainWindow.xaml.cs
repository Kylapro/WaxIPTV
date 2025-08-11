using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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

        public MainWindow()
        {
            InitializeComponent();
            _settingsService = new SettingsService();
            Loaded += MainWindow_Loaded;
            // Register selection changed handler for the internal ListBox
            ChannelListViewControl.ChannelList.SelectionChanged += ChannelList_SelectionChanged;
        }

        /// <summary>
        /// Handles the Loaded event for the window.  Loads the application
        /// settings, initialises the external player controller and parses
        /// the playlist and EPG data if available.
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Load or detect settings
            _settingsService.Load();
            var settings = _settingsService.Settings;

            // Initialise the chosen player controller (mpv by default)
            if (string.Equals(settings.Player, "vlc", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(settings.VlcPath))
                    _player = new VlcController(settings.VlcPath);
            }
            else
            {
                if (!string.IsNullOrEmpty(settings.MpvPath))
                    _player = new MpvController(settings.MpvPath);
            }
            // Disable playback buttons when no player is available
            if (_player == null)
            {
                PlayButton.IsEnabled = PauseButton.IsEnabled = StopButton.IsEnabled = false;
            }

            // Attempt to load channels from the playlist file specified in settings
            LoadChannels(settings);
            ChannelListViewControl.ChannelList.ItemsSource = _channels;

            // Attempt to load EPG data
            LoadEpg(settings);
        }

        /// <summary>
        /// Loads the M3U playlist from the location specified in settings.  If no
        /// file exists or parsing fails, populates a few placeholder channels.
        /// </summary>
        private void LoadChannels(AppSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.PlaylistUrl) && File.Exists(settings.PlaylistUrl))
            {
                try
                {
                    var m3uContent = File.ReadAllText(settings.PlaylistUrl);
                    _channels = M3UParser.Parse(m3uContent);
                }
                catch
                {
                    _channels = new List<Channel>();
                }
            }
            if (_channels.Count == 0)
            {
                // Provide placeholders when no channels loaded
                _channels = new List<Channel>
                {
                    new Channel("ch1", "Channel 1", null, null, "", null),
                    new Channel("ch2", "Channel 2", null, null, "", null),
                    new Channel("ch3", "Channel 3", null, null, "", null)
                };
            }
        }

        /// <summary>
        /// Loads the XMLTV EPG from the location specified in settings.  The
        /// programmes dictionary is keyed by channel ID.  This method will
        /// refresh the data only if the refresh interval has elapsed.
        /// </summary>
        private void LoadEpg(AppSettings settings)
        {
            if (_epgLoadedAt + TimeSpan.FromHours(settings.EpgRefreshHours) > DateTimeOffset.UtcNow)
                return;
            if (!string.IsNullOrWhiteSpace(settings.XmltvUrl) && File.Exists(settings.XmltvUrl))
            {
                try
                {
                    var xml = File.ReadAllText(settings.XmltvUrl);
                    var (channelNames, programmes) = Xmltv.Parse(xml);
                    // Sort programmes by start time before mapping
                    var ordered = programmes.OrderBy(p => p.StartUtc).ToList();
                    _programmes = EpgMapper.MapProgrammes(ordered, _channels, channelNames);
                    _epgLoadedAt = DateTimeOffset.UtcNow;
                }
                catch
                {
                    _programmes.Clear();
                }
            }
        }

        /// <summary>
        /// Handles a selection change in the channel list.  Updates the
        /// displayed channel name and computes the now/next programmes.
        /// </summary>
        private void ChannelList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChannelListViewControl.ChannelList.SelectedItem is not Channel selected)
            {
                SelectedChannelTitle.Text = string.Empty;
                EpgNowNextPanel.UpdateProgrammes(null, null);
                return;
            }
            SelectedChannelTitle.Text = selected.Name;
            if (_programmes.TryGetValue(selected.Id, out var progs))
            {
                var (now, next) = EpgHelpers.GetNowNext(progs, DateTimeOffset.UtcNow);
                EpgNowNextPanel.UpdateProgrammes(now, next);
            }
            else
            {
                EpgNowNextPanel.UpdateProgrammes(null, null);
            }
        }

        /// <summary>
        /// Starts playback of the selected channel by invoking the external
        /// player controller.  If no stream URL is defined, no action is taken.
        /// </summary>
        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_player == null)
                return;
            if (ChannelListViewControl.ChannelList.SelectedItem is not Channel selected)
                return;
            var url = selected.StreamUrl;
            if (string.IsNullOrWhiteSpace(url))
                return;
            try
            {
                await _player.StartAsync(url, selected.Name);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start playback: {ex.Message}", "Playback Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Pauses playback via the active player controller.
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
        /// Stops or quits playback.  For mpv the player process is quit,
        /// whereas VLC is instructed to stop playback.
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
        }
    }
}