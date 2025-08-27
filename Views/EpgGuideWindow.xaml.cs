using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WaxIPTV.Models;

namespace WaxIPTV.Views
{
    /// <summary>
    /// Interaction logic for EpgGuideWindow.xaml.  This window displays a
    /// six‑hour timeline for each channel using programme data loaded
    /// by the main window.  Users can filter channels via a search
    /// box and group selector.  Selecting a programme shows its
    /// details in the information panel and exposes a play button.
    /// Double‑clicking a channel row or programme segment will
    /// immediately start playback via the main window.
    /// </summary>
    public partial class EpgGuideWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private List<Channel> _channels;
        private Dictionary<string, List<Programme>> _programmes;
        private string _searchTerm = string.Empty;
        private string _selectedGroup = "All";
        // Width assigned to represent six hours of programmes.  The
        // timeline uses this base value to compute the width of each
        // segment relative to its duration (minutes).  You can adjust
        // this constant to increase or decrease the overall scale of
        // the timeline.  Six hours = 360 minutes.
        private const double TimelineBaseWidth = 720.0;
        private readonly double _minuteWidth;
        private readonly Brush _programmeBrush;
        private readonly Brush _blankBrush;

        // Cancellation source used to abort in‑flight timeline builds when
        // filters change or a new EPG load arrives.  Each call to
        // BuildTimelines will cancel the previous asynchronous builder so
        // that only the most recent request populates the UI.  Without
        // cancellation, rapid search or group changes could result in
        // multiple overlapping background tasks mutating the ItemsSource.
        private System.Threading.CancellationTokenSource? _buildCts;

        /// <summary>
        /// Model representing a programme or blank timeslot on the
        /// timeline.  Programmes have a title, start and end times and
        /// are drawn using the accent brush.  Blank slots fill gaps
        /// between programmes and are drawn using the divider brush.
        /// </summary>
        private class SegmentModel
        {
            public Channel Channel { get; init; } = null!;
            public Programme? Programme { get; init; }
            public double Width { get; init; }
            public string Title => Programme?.Title ?? string.Empty;
            public Brush Background { get; init; } = Brushes.Transparent;
        }

        /// <summary>
        /// Model representing a single channel row in the guide.  It
        /// encapsulates the channel metadata and the collection of
        /// segments comprising its six‑hour timeline.  The timeline
        /// updates when filtering or when the EPG is refreshed.
        /// </summary>
        private class ChannelTimelineModel
        {
            public Channel Channel { get; init; } = null!;
            public ObservableCollection<SegmentModel> Segments { get; init; } = new();
        }

        /// <summary>
        /// Constructs a new EPG guide window.  The guide uses the
        /// supplied channel list and programme mapping to build
        /// timelines.  A reference to the main window is required so
        /// that playback can be delegated and the guide can refresh
        /// when new EPG data is loaded.
        /// </summary>
        /// <param name="channels">A snapshot of the currently loaded channels.</param>
        /// <param name="programmes">A mapping of channel IDs to their list of programmes.</param>
        /// <param name="mainWindow">The owning main window.</param>
        public EpgGuideWindow(List<Channel> channels,
                              Dictionary<string, List<Programme>> programmes,
                              MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _channels = channels;
            _programmes = programmes;
            // Compute the width of each minute based on the timeline base width
            _minuteWidth = TimelineBaseWidth / 360.0;
            // Resolve brushes from the current theme to match the main UI
            _programmeBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.DarkBlue;
            _blankBrush = TryFindResource("DividerBrush") as Brush ?? Brushes.LightGray;
            Loaded += OnLoaded;
            // Subscribe to the main window’s EPG loaded event so we can refresh
            _mainWindow.EpgLoaded += MainWindow_EpgLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Populate the group filter from the channel snapshot
            PopulateGroupFilter();
            // Build the initial timeline
            BuildTimelines();
        }

        /// <summary>
        /// Rebuilds the group filter ComboBox.  Adds the default
        /// "All" option and sorts the remaining groups alphabetically.
        /// </summary>
        private void PopulateGroupFilter()
        {
            var groups = _channels
                .Select(c => c.Group)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var items = new List<string> { "All" };
            items.AddRange(groups);
            GroupFilter.ItemsSource = items;
            GroupFilter.SelectedIndex = 0;
        }

        /// <summary>
        /// Invoked whenever the EPG is reloaded in the main window.  The
        /// guide responds by refreshing its internal programme mapping and
        /// rebuilding the timeline.  This method runs on the UI thread.
        /// </summary>
        private void MainWindow_EpgLoaded()
        {
            try
            {
                // Update our references to the latest collections
                _channels = _mainWindow.Channels.ToList();
                _programmes = _mainWindow.Programmes.ToDictionary(kv => kv.Key, kv => kv.Value);
                PopulateGroupFilter();
                BuildTimelines();
            }
            catch
            {
                // If anything goes wrong while updating, ignore to avoid crashing
            }
        }

        /// <summary>
        /// Responds to changes in the search box by updating the
        /// internal search term and rebuilding the timeline to reflect
        /// the filtered channel list.
        /// </summary>
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTerm = SearchBox.Text ?? string.Empty;
            BuildTimelines();
        }

        /// <summary>
        /// Responds to changes in the group filter by updating the
        /// selected group and rebuilding the timeline accordingly.
        /// </summary>
        private void GroupFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GroupFilter.SelectedItem is string s)
            {
                _selectedGroup = s;
                BuildTimelines();
            }
        }

        /// <summary>
        /// Closes the guide when the Close button is clicked.  Unsubscribe
        /// from the main window’s EPG loaded event to avoid leaks.
        /// </summary>
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // Unsubscribe from the event to avoid memory leaks
            _mainWindow.EpgLoaded -= MainWindow_EpgLoaded;
        }

        /// <summary>
        /// Builds the timeline rows for all channels matching the
        /// current search and group filter criteria.  The result
        /// replaces the ItemsSource of the timeline list.  This method
        /// runs synchronously; for large playlists you may wish to
        /// dispatch work to a background thread and update the UI
        /// incrementally.
        /// </summary>
        private void BuildTimelines()
        {
            // Cancel any previously running timeline build and start a new one.  This ensures
            // that only the latest search/group filter takes effect.  Without
            // cancellation, multiple asynchronous builders may compete to update
            // the ItemsSource causing flickering and wasted work.
            _buildCts?.Cancel();
            _buildCts = new System.Threading.CancellationTokenSource();
            var token = _buildCts.Token;
            _ = BuildTimelinesAsync(token);
        }

        /// <summary>
        /// Asynchronously constructs the timeline rows for all channels matching
        /// the current search and group filters.  The rows are added
        /// incrementally to the UI so that long playlists load progressively
        /// rather than blocking the UI thread.  If a new build is
        /// requested, the previous build is cancelled via the provided
        /// token.  This method must not be awaited on the UI thread; it
        /// schedules its own updates on the Dispatcher.
        /// </summary>
        private async System.Threading.Tasks.Task BuildTimelinesAsync(System.Threading.CancellationToken token)
        {
            try
            {
                // Determine the filtered channel list.  Perform the filter on a
                // background thread to avoid blocking the UI if there are
                // thousands of channels.  We do not await Task.Run directly on
                // the UI thread because we still need to schedule UI updates.
                var filtered = await System.Threading.Tasks.Task.Run(() =>
                {
                    var result = new List<Channel>();
                    string search = _searchTerm.Trim();
                    foreach (var ch in _channels)
                    {
                        if (token.IsCancellationRequested) return result;
                        // Apply group filter
                        if (_selectedGroup != "All" && !string.Equals(ch.Group, _selectedGroup, StringComparison.OrdinalIgnoreCase))
                            continue;
                        // Apply search filter
                        if (!string.IsNullOrEmpty(search) && !ch.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                            continue;
                        result.Add(ch);
                    }
                    return result;
                }, token);
                if (token.IsCancellationRequested) return;

                // Create a new observable collection for the timeline rows.  The
                // collection is created on the UI thread so that items can be
                // added safely from within Dispatcher.InvokeAsync calls.  We
                // assign it to the ItemsSource immediately so that the guide
                // clears any previous content.
                var collection = new ObservableCollection<ChannelTimelineModel>();
                await Dispatcher.InvokeAsync(() => TimelineList.ItemsSource = collection, System.Windows.Threading.DispatcherPriority.Background);

                foreach (var ch in filtered)
                {
                    if (token.IsCancellationRequested) break;
                    // Build the segment list synchronously.  This method is
                    // relatively lightweight but could be executed on a
                    // background thread if necessary.  It computes gaps and
                    // programmes within the six‑hour window.
                    var segs = BuildSegmentsForChannel(ch);
                    var model = new ChannelTimelineModel
                    {
                        Channel = ch,
                        Segments = new ObservableCollection<SegmentModel>(segs)
                    };
                    // Add the model to the UI collection on the Dispatcher.  Use
                    // Background priority to allow the UI to remain responsive.
                    await Dispatcher.InvokeAsync(() => collection.Add(model), System.Windows.Threading.DispatcherPriority.Background);
                    // Yield control to allow UI rendering between items.  Without
                    // yielding, long lists could still freeze the UI.
                    await System.Threading.Tasks.Task.Yield();
                }
            }
            catch (System.OperationCanceledException)
            {
                // Swallow cancellation exceptions; they are expected when the user
                // changes filters or the window closes.
            }
            catch
            {
                // Ignore other exceptions to avoid crashing the guide.  The
                // timeline may be partially populated.
            }
        }

        /// <summary>
        /// Constructs the six‑hour timeline segments for a given channel.  A
        /// blank segment is inserted for any gaps where no programme
        /// overlaps with the timeline window.  Programme segments are
        /// drawn using the accent brush while blank segments use the
        /// divider brush.
        /// </summary>
        private List<SegmentModel> BuildSegmentsForChannel(Channel channel)
        {
            var segments = new List<SegmentModel>();
            // Determine the time window: from now to six hours ahead
            var start = DateTimeOffset.UtcNow;
            var end = start.AddHours(6);
            // Find programmes for this channel and sort them
            List<Programme> progs;
            if (!_programmes.TryGetValue(channel.Id, out progs))
            {
                progs = new List<Programme>();
            }
            var list = progs.OrderBy(p => p.StartUtc).ToList();
            var current = start;
            foreach (var p in list)
            {
                // Skip programmes that end before the start of our window
                if (p.EndUtc <= start)
                    continue;
                // Stop if the programme starts after the end of our window
                if (p.StartUtc >= end)
                    break;
                // Insert blank segment if there is a gap between current and the programme start
                var segStart = p.StartUtc > start ? p.StartUtc : start;
                if (segStart > current)
                {
                    var blankDur = (segStart - current).TotalMinutes;
                    if (blankDur > 0)
                    {
                        segments.Add(new SegmentModel
                        {
                            Channel = channel,
                            Programme = null,
                            Width = blankDur * _minuteWidth,
                            Background = _blankBrush
                        });
                    }
                }
                // Compute overlap duration within our window
                var progStart = p.StartUtc > start ? p.StartUtc : start;
                var progEnd = p.EndUtc < end ? p.EndUtc : end;
                var duration = (progEnd - progStart).TotalMinutes;
                if (duration > 0)
                {
                    segments.Add(new SegmentModel
                    {
                        Channel = channel,
                        Programme = p,
                        Width = duration * _minuteWidth,
                        Background = _programmeBrush
                    });
                    current = progEnd;
                }
            }
            // Trailing blank segment if timeline end is after the last programme
            if (current < end)
            {
                var blankDur = (end - current).TotalMinutes;
                segments.Add(new SegmentModel
                {
                    Channel = channel,
                    Programme = null,
                    Width = blankDur * _minuteWidth,
                    Background = _blankBrush
                });
            }
            return segments;
        }

        /// <summary>
        /// Handles mouse left button down on programme segments.  If the user
        /// double‑clicks, the associated channel starts playing immediately via
        /// the main window.  A single click displays the programme
        /// information in the panel above the guide.  Clicking on a blank
        /// segment hides the panel.
        /// </summary>
        private async void Segment_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is SegmentModel seg)
            {
                // Double‑click: play the channel
                if (e.ClickCount >= 2)
                {
                    if (seg.Channel != null)
                    {
                        try
                        {
                            await _mainWindow.PlayChannelFromGuideAsync(seg.Channel);
                        }
                        catch
                        {
                            // ignore playback errors
                        }
                    }
                    return;
                }
                // Single click: show or hide programme info
                if (seg.Programme == null)
                {
                    InfoPanel.Visibility = Visibility.Collapsed;
                    InfoPlayButton.Tag = null;
                    return;
                }
                var prog = seg.Programme;
                InfoPanel.Visibility = Visibility.Visible;
                InfoTitle.Text = prog.Title;
                var localStart = prog.StartUtc.ToLocalTime().DateTime;
                var localEnd = prog.EndUtc.ToLocalTime().DateTime;
                InfoTime.Text = string.Format(CultureInfo.CurrentCulture, "{0:t} - {1:t}", localStart, localEnd);
                InfoDesc.Text = string.IsNullOrWhiteSpace(prog.Desc) ? string.Empty : prog.Desc;
                // Store the segment on the play button for later retrieval
                InfoPlayButton.Tag = seg;
            }
        }

        /// <summary>
        /// Handles mouse left button down on the channel row.  A double click
        /// will immediately play the channel via the main window.  Single
        /// clicks are ignored to avoid interfering with segment selection.
        /// </summary>
        private async void ChannelRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                if (sender is Border border && border.DataContext is ChannelTimelineModel model)
                {
                    try
                    {
                        await _mainWindow.PlayChannelFromGuideAsync(model.Channel);
                    }
                    catch
                    {
                        // ignore playback errors
                    }
                }
            }
        }

        /// <summary>
        /// Invoked when the play button on the information panel is
        /// clicked.  Starts playback of the programme’s channel via
        /// the main window.  The stored SegmentModel on the button’s
        /// Tag provides the channel information.
        /// </summary>
        private async void InfoPlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (InfoPlayButton.Tag is SegmentModel seg && seg.Channel != null)
            {
                try
                {
                    await _mainWindow.PlayChannelFromGuideAsync(seg.Channel);
                }
                catch
                {
                    // ignore errors
                }
            }
        }
    }
}