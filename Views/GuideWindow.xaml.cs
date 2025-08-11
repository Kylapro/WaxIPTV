using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaxIPTV.Models;

namespace WaxIPTV.Views
{
    /// <summary>
    /// Interaction logic for GuideWindow.xaml.  This window presents an
    /// electronic programme guide (EPG) styled after a traditional cable
    /// guide.  Channels are listed vertically with their icons and names,
    /// while the right portion shows programme blocks across a fixed
    /// time window.  A filter bar allows narrowing the list by group
    /// and searching by name.  Clicking a programme emits a
    /// ChannelSelected event so that the caller can start playback.
    /// </summary>
    public partial class GuideWindow : Window
    {
        // Incoming data from the main window
        private readonly List<Channel> _allChannels;
        private readonly Dictionary<string, List<Programme>> _programmes;

        // Cache for channel icons keyed by the logo URL.  This avoids
        // re-downloading images when filters are applied multiple times.
        private readonly Dictionary<string, ImageSource?> _iconCache = new();

        // Filter state
        private string _searchTerm = string.Empty;
        private string? _selectedGroup = "All";

        // Timeline settings
        private readonly TimeSpan _windowDuration = TimeSpan.FromHours(3);
        private readonly double _timelineWidth = 1800; // pixels representing the full window duration

        // Event triggered when a channel is selected from the guide
        public event EventHandler<Channel>? ChannelSelected;

        // ScrollViewer of the channel names list (retrieved after loaded)
        private ScrollViewer? _namesScrollViewer;

        // Precomputed guide rows for all channels.  Each GuideRow contains
        // the list of programme slots for the current time window.
        private List<GuideRow> _allGuideRows = new();

        public GuideWindow(List<Channel> channels, Dictionary<string, List<Programme>> programmes)
        {
            InitializeComponent();
            _allChannels = channels;
            _programmes = programmes;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Populate group filter ComboBox
            var groups = _allChannels
                .Select(c => c.Group)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var items = new List<string> { "All" };
            items.AddRange(groups);
            GroupFilter.ItemsSource = items;
            GroupFilter.SelectedIndex = 0;
            _selectedGroup = "All";

            // Build initial guide rows
            // Precompute all guide rows once; we'll filter these instead of
            // rebuilding programme slots on every search or group change.  The
            // time window is anchored at the moment of loading.
            _allGuideRows = BuildGuideRows(_allChannels);

            ApplyFilterAndBuildGuide();

            // Capture the scroll viewer for the names list so we can sync vertical scrolling
            _namesScrollViewer = FindVisualChild<ScrollViewer>(ChannelNamesList);
        }

        /// <summary>
        /// Handles group selection changes.  Updates the selected group and
        /// rebuilds the guide rows.
        /// </summary>
        private void GroupFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedGroup = GroupFilter.SelectedItem as string;
            ApplyFilterAndBuildGuide();
        }

        /// <summary>
        /// Handles search text changes.  Updates the search term and
        /// rebuilds the guide rows.
        /// </summary>
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTerm = SearchBox.Text?.Trim() ?? string.Empty;
            ApplyFilterAndBuildGuide();
        }

        /// <summary>
        /// Clears the search box when the clear button is clicked.
        /// </summary>
        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
        }

        /// <summary>
        /// Handles selection change in the channel names list.  When a channel
        /// is selected from the left column, highlight the corresponding row
        /// in the timeline.  This method is reserved for future use; currently
        /// no row highlighting is implemented.
        /// </summary>
        private void ChannelNamesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // No-op for now.  Could be used to scroll timeline to the selected row.
        }

        /// <summary>
        /// Synchronise vertical scrolling between the timeline and the channel
        /// names list.  When the timeline scrolls vertically, adjust the
        /// vertical offset of the names list accordingly.
        /// </summary>
        private void TimelineScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange != 0 && _namesScrollViewer != null)
            {
                _namesScrollViewer.ScrollToVerticalOffset(e.VerticalOffset);
            }
        }

        /// <summary>
        /// Called when a programme block is clicked.  Determines the
        /// associated channel and raises the ChannelSelected event.
        /// </summary>
        private void ProgrammeSlot_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is ProgrammeSlot slot)
            {
                var ch = slot.Channel;
                ChannelSelected?.Invoke(this, ch);
            }
        }

        /// <summary>
        /// Handles clicks on a channel row in the left list.  When the user
        /// clicks a channel name (or its icon), raise the ChannelSelected
        /// event so that the caller can start playback immediately.  This
        /// allows selecting a channel even when there are no programme
        /// blocks in the timeline (e.g., when the EPG is empty).
        /// </summary>
        private void ChannelRow_Click(object sender, MouseButtonEventArgs e)
        {
            // The DataContext of the stack panel is the GuideRow for the channel
            if (sender is FrameworkElement element && element.DataContext is GuideRow row)
            {
                var ch = row.Channel;
                ChannelSelected?.Invoke(this, ch);
            }
        }

        /// <summary>
        /// Applies the current search and group filters to the channel list,
        /// then rebuilds the guide rows and header.
        /// </summary>
        private void ApplyFilterAndBuildGuide()
        {
            // Filter precomputed guide rows instead of rebuilding slots each time
            IEnumerable<GuideRow> filtered = _allGuideRows;
            // Filter by search term across name, tvg-id and group (case-insensitive)
            if (!string.IsNullOrEmpty(_searchTerm))
            {
                var term = _searchTerm;
                filtered = filtered.Where(row =>
                    (!string.IsNullOrEmpty(row.ChannelName) && row.ChannelName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(row.Channel.TvgId) && row.Channel.TvgId.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(row.Channel.Group) && row.Channel.Group.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }
            // Filter by group
            if (!string.IsNullOrWhiteSpace(_selectedGroup) && !string.Equals(_selectedGroup, "All", StringComparison.OrdinalIgnoreCase))
            {
                var group = _selectedGroup;
                filtered = filtered.Where(row => string.Equals(row.Channel.Group, group, StringComparison.OrdinalIgnoreCase));
            }
            var guideRows = filtered.ToList();

            // Bind to UI
            ChannelNamesList.ItemsSource = guideRows;
            TimelineItemsControl.ItemsSource = guideRows;

            // Draw header labels based on current time window
            DrawTimelineHeader();
        }

        /// <summary>
        /// Builds guide rows for a given list of channels.  For each channel,
        /// the method creates a GuideRow containing the channel metadata
        /// and a list of ProgrammeSlot objects representing programmes
        /// within the time window.  Programmes outside the window are
        /// clipped or omitted.  The width and offset of each slot is
        /// computed based on the timeline width and duration.
        /// </summary>
        private List<GuideRow> BuildGuideRows(List<Channel> channels)
        {
            var rows = new List<GuideRow>(channels.Count);
            // Determine the start and end of the window in UTC.  Programme times
            // are stored in UTC, but we convert to local time for display
            var windowStartLocal = DateTimeOffset.Now;
            var windowEndLocal = windowStartLocal + _windowDuration;
            var totalMinutes = _windowDuration.TotalMinutes;
            var pixelsPerMinute = _timelineWidth / totalMinutes;
            foreach (var ch in channels)
            {
                var row = new GuideRow
                {
                    Channel = ch,
                    ChannelName = ch.Name,
                    ChannelIcon = LoadIcon(ch.Logo),
                    ProgrammeSlots = new List<ProgrammeSlot>()
                };
                if (_programmes.TryGetValue(ch.Id, out var progs))
                {
                    foreach (var prog in progs)
                    {
                        // Convert to local time for display
                        var progStart = prog.StartUtc.ToLocalTime();
                        var progEnd = prog.EndUtc.ToLocalTime();
                        // Skip if outside the window
                        if (progEnd <= windowStartLocal || progStart >= windowEndLocal)
                            continue;
                        var segStart = progStart < windowStartLocal ? windowStartLocal : progStart;
                        var segEnd = progEnd > windowEndLocal ? windowEndLocal : progEnd;
                        var offsetMinutes = (segStart - windowStartLocal).TotalMinutes;
                        var durationMinutes = (segEnd - segStart).TotalMinutes;
                        var offsetPx = offsetMinutes * pixelsPerMinute;
                        var widthPx = Math.Max(durationMinutes * pixelsPerMinute, 4); // ensure minimum width
                        var slot = new ProgrammeSlot
                        {
                            Channel = ch,
                            Title = prog.Title,
                            Start = prog.StartUtc,
                            End = prog.EndUtc,
                            Offset = offsetPx,
                            Width = widthPx,
                            ToolTip = $"{prog.Title}\n{prog.StartUtc.ToLocalTime():HH:mm} - {prog.EndUtc.ToLocalTime():HH:mm}"
                        };
                        row.ProgrammeSlots.Add(slot);
                    }
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Draws the timeline header labels across the header canvas.  Uses
        /// 30-minute intervals spanning the same window duration as the
        /// programme rows.  Clears any existing children before drawing.
        /// </summary>
        private void DrawTimelineHeader()
        {
            TimelineHeaderCanvas.Children.Clear();
            TimelineHeaderCanvas.Width = _timelineWidth;
            var windowStartLocal = DateTimeOffset.Now;
            var intervalMinutes = 30;
            var totalIntervals = (int)(_windowDuration.TotalMinutes / intervalMinutes);
            var pixelsPerMinute = _timelineWidth / _windowDuration.TotalMinutes;
            for (int i = 0; i <= totalIntervals; i++)
            {
                var time = windowStartLocal.AddMinutes(i * intervalMinutes);
                var label = new TextBlock
                {
                    Text = time.ToString("HH:mm"),
                    Foreground = (Brush)Application.Current.FindResource("TextBrush"),
                    FontSize = 12
                };
                Canvas.SetLeft(label, i * intervalMinutes * pixelsPerMinute + 2);
                Canvas.SetTop(label, 0);
                TimelineHeaderCanvas.Children.Add(label);
                // Optionally draw a separator line
                var line = new Border
                {
                    Background = (Brush)Application.Current.FindResource("DividerBrush"),
                    Width = 1,
                    Height = TimelineHeaderCanvas.Height
                };
                Canvas.SetLeft(line, i * intervalMinutes * pixelsPerMinute);
                Canvas.SetTop(line, 0);
                TimelineHeaderCanvas.Children.Add(line);
            }
        }

        /// <summary>
        /// Helper to load an icon from a URL.  Returns null if loading
        /// fails or the URL is empty.  Icons are cached in memory via
        /// BitmapImage.  This method catches all exceptions and returns
        /// null on failure to avoid impacting the UI thread.
        /// </summary>
        private ImageSource? LoadIcon(string? url)
        {
            // Return null if no URL is provided
            if (string.IsNullOrWhiteSpace(url))
                return null;
            // Check cache first to avoid reloading
            if (_iconCache.TryGetValue(url, out var cached))
            {
                return cached;
            }
            try
            {
                var uri = new Uri(url);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.DecodePixelWidth = 32;
                bitmap.DecodePixelHeight = 32;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                bitmap.Freeze();
                _iconCache[url] = bitmap;
                return bitmap;
            }
            catch
            {
                // Cache the failure as null to avoid repeated attempts
                _iconCache[url] = null;
                return null;
            }
        }

        /// <summary>
        /// Walks the visual tree to find a child element of a specified type.
        /// Used to locate the internal ScrollViewer of the ListBox.
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            if (obj == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is T found)
                    return found;
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        /// <summary>
        /// Represents a row in the guide containing a channel and its
        /// programme blocks within the time window.
        /// </summary>
        private class GuideRow
        {
            public Channel Channel { get; set; } = null!;
            public string ChannelName { get; set; } = string.Empty;
            public ImageSource? ChannelIcon { get; set; }
            public List<ProgrammeSlot> ProgrammeSlots { get; set; } = new();
        }

        /// <summary>
        /// Represents a single programme slot on the guide.  Holds the
        /// original programme start and end times as well as the
        /// calculated pixel offset and width for display.  The Channel
        /// property points back to the channel to facilitate playback.
        /// </summary>
        private class ProgrammeSlot
        {
            public Channel Channel { get; set; } = null!;
            public string Title { get; set; } = string.Empty;
            public DateTimeOffset Start { get; set; }
            public DateTimeOffset End { get; set; }
            public double Offset { get; set; }
            public double Width { get; set; }
            public string ToolTip { get; set; } = string.Empty;
            public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "No title" : Title;
        }
    }
}