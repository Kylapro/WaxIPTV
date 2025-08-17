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

        // Cancellation token source used to cancel any in-progress guide row
        // construction when the user changes the search term or selected group.
        private System.Threading.CancellationTokenSource? _buildRowsCts;

        // Precomputed guide rows for all channels.  Each GuideRow contains
        // the list of programme slots for the current time window.  Use
        // ObservableCollection so that UI updates automatically when rows
        // are added incrementally from a background thread.  A second
        // collection holds only the rows that match the current filter.
        private readonly System.Collections.ObjectModel.ObservableCollection<GuideRow> _allGuideRows = new();
        private readonly System.Collections.ObjectModel.ObservableCollection<GuideRow> _filteredRows = new();

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

            // Set up the filtered collection as the ItemsSource for both lists so that
            // rows added later will appear automatically.  This is required prior
            // to starting the asynchronous build.  Draw the timeline header and
            // then begin constructing rows according to the current filters.
            ChannelNamesList.ItemsSource = _filteredRows;
            TimelineItemsControl.ItemsSource = _filteredRows;
            StartBuildRows();

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
            // Rebuild rows for the selected group
            StartBuildRows();
        }

        /// <summary>
        /// Handles search text changes.  Updates the search term and
        /// rebuilds the guide rows.
        /// </summary>
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTerm = SearchBox.Text?.Trim() ?? string.Empty;
            // Rebuild rows for the new search term
            StartBuildRows();
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
            // Rebuild the filtered view based on the current search and group
            // criteria.  Clear the existing filtered collection and add back
            // rows from the master collection that match the predicate.
            _filteredRows.Clear();
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
            foreach (var row in filtered)
            {
                _filteredRows.Add(row);
            }
            // Redraw the timeline header to ensure the time labels reflect
            // the current time window when the filter changes.
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
        /// Determines whether a given guide row matches the current
        /// search term and selected group.  This helper is used when
        /// building guide rows asynchronously so that only matching
        /// rows are displayed as they are generated.
        /// </summary>
        /// <param name="row">The guide row to test.</param>
        /// <returns>True if the row satisfies the current filters; otherwise false.</returns>
        private bool RowMatchesFilter(GuideRow row)
        {
            // Apply search filter: match on channel name, tvg-id or group
            if (!string.IsNullOrEmpty(_searchTerm))
            {
                var term = _searchTerm;
                bool nameMatch = !string.IsNullOrEmpty(row.ChannelName) && row.ChannelName.Contains(term, StringComparison.OrdinalIgnoreCase);
                bool idMatch = !string.IsNullOrEmpty(row.Channel.TvgId) && row.Channel.TvgId.Contains(term, StringComparison.OrdinalIgnoreCase);
                bool groupMatch = !string.IsNullOrEmpty(row.Channel.Group) && row.Channel.Group.Contains(term, StringComparison.OrdinalIgnoreCase);
                if (!(nameMatch || idMatch || groupMatch))
                {
                    return false;
                }
            }
            // Apply group filter: skip if not in selected group (other than "All")
            if (!string.IsNullOrWhiteSpace(_selectedGroup) && !string.Equals(_selectedGroup, "All", StringComparison.OrdinalIgnoreCase))
            {
                var group = _selectedGroup;
                if (!string.Equals(row.Channel.Group, group, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Determines whether a channel matches the current search term and
        /// group filter without constructing the full guide row.  This is
        /// used before building programme slots so that only channels that
        /// pass the filter are processed.  This helps improve startup
        /// performance because we avoid unnecessary work for channels in
        /// other groups or that don't match the search.
        /// </summary>
        /// <param name="ch">The channel to test.</param>
        /// <returns>True if the channel satisfies the current filters; otherwise false.</returns>
        private bool ChannelMatchesFilter(Channel ch)
        {
            // Apply search filter: match on channel name, tvg-id or group
            if (!string.IsNullOrEmpty(_searchTerm))
            {
                var term = _searchTerm;
                bool nameMatch = !string.IsNullOrEmpty(ch.Name) && ch.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
                bool idMatch = !string.IsNullOrEmpty(ch.TvgId) && ch.TvgId.Contains(term, StringComparison.OrdinalIgnoreCase);
                bool groupMatch = !string.IsNullOrEmpty(ch.Group) && ch.Group.Contains(term, StringComparison.OrdinalIgnoreCase);
                if (!(nameMatch || idMatch || groupMatch))
                {
                    return false;
                }
            }
            // Apply group filter
            if (!string.IsNullOrWhiteSpace(_selectedGroup) && !string.Equals(_selectedGroup, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(ch.Group, _selectedGroup, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Builds a guide row for a single channel.  This method mirrors
        /// the logic of <see cref="BuildGuideRows(List{Channel})"/> but for
        /// a single channel.  It calculates the programme slots within
        /// the current time window and loads the channel icon.  This
        /// method may be called from a background thread; however it
        /// freezes any image resources so that they can be safely used on
        /// the UI thread.
        /// </summary>
        /// <param name="ch">The channel to build a row for.</param>
        /// <returns>A populated <see cref="GuideRow"/> for the channel.</returns>
        private GuideRow BuildGuideRow(Channel ch)
        {
            var row = new GuideRow
            {
                Channel = ch,
                ChannelName = ch.Name,
                ChannelIcon = LoadIcon(ch.Logo),
                ProgrammeSlots = new List<ProgrammeSlot>()
            };
            // Determine time window in local time
            var windowStartLocal = DateTimeOffset.Now;
            var windowEndLocal = windowStartLocal + _windowDuration;
            var totalMinutes = _windowDuration.TotalMinutes;
            var pixelsPerMinute = _timelineWidth / totalMinutes;
            // Populate programme slots
            if (_programmes.TryGetValue(ch.Id, out var progs))
            {
                foreach (var prog in progs)
                {
                    var progStart = prog.StartUtc.ToLocalTime();
                    var progEnd = prog.EndUtc.ToLocalTime();
                    if (progEnd <= windowStartLocal || progStart >= windowEndLocal)
                        continue;
                    var segStart = progStart < windowStartLocal ? windowStartLocal : progStart;
                    var segEnd = progEnd > windowEndLocal ? windowEndLocal : progEnd;
                    var offsetMinutes = (segStart - windowStartLocal).TotalMinutes;
                    var durationMinutes = (segEnd - segStart).TotalMinutes;
                    var offsetPx = offsetMinutes * pixelsPerMinute;
                    var widthPx = Math.Max(durationMinutes * pixelsPerMinute, 4);
                    var slot = new ProgrammeSlot
                    {
                        Channel = ch,
                        Title = prog.Title,
                        Start = prog.StartUtc,
                        End = prog.EndUtc,
                        Offset = offsetPx,
                        Width = widthPx,
                        ToolTip = $"{prog.Title}\\n{prog.StartUtc.ToLocalTime():HH:mm} - {prog.EndUtc.ToLocalTime():HH:mm}"
                    };
                    row.ProgrammeSlots.Add(slot);
                }
            }
            return row;
        }

        /// <summary>
        /// Starts (or restarts) asynchronous construction of guide rows
        /// according to the current search term and selected group.  Any
        /// ongoing build operation is cancelled.  The guide rows and
        /// filtered collections are cleared, and new rows are added one
        /// at a time on the UI thread.  The operation yields periodically
        /// to maintain UI responsiveness.  Only channels that match the
        /// current filters are processed.
        /// </summary>
        private void StartBuildRows()
        {
            // Cancel any in-progress build
            _buildRowsCts?.Cancel();
            _buildRowsCts = new System.Threading.CancellationTokenSource();
            var token = _buildRowsCts.Token;
            // Clear existing rows and draw header on UI thread
            _allGuideRows.Clear();
            _filteredRows.Clear();
            DrawTimelineHeader();
            // Kick off background task
            _ = Task.Run(async () =>
            {
                int processed = 0;
                foreach (var ch in _allChannels)
                {
                    if (token.IsCancellationRequested)
                        break;
                    if (!ChannelMatchesFilter(ch))
                        continue;
                    var row = BuildGuideRow(ch);
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _allGuideRows.Add(row);
                        _filteredRows.Add(row);
                    });
                    processed++;
                    if (processed % 20 == 0)
                    {
                        try
                        {
                            await Task.Delay(1, token);
                        }
                        catch (TaskCanceledException)
                        {
                            break;
                        }
                    }
                }
            });
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
                // Support base64 data URIs
                if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var commaIndex = url.IndexOf(',');
                    if (commaIndex > 0)
                    {
                        var base64Data = url[(commaIndex + 1)..];
                        var bytes = Convert.FromBase64String(base64Data);
                        using var ms = new System.IO.MemoryStream(bytes);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = ms;
                        bitmap.DecodePixelWidth = 32;
                        bitmap.DecodePixelHeight = 32;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        _iconCache[url] = bitmap;
                        return bitmap;
                    }
                }
                // Handle local file paths
                if (System.IO.File.Exists(url))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(url, UriKind.Absolute);
                    bitmap.DecodePixelWidth = 32;
                    bitmap.DecodePixelHeight = 32;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    _iconCache[url] = bitmap;
                    return bitmap;
                }
                // Replace spaces with %20 for HTTP/HTTPS URIs
                var candidate = url.Contains(' ') ? url.Replace(" ", "%20") : url;
                if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
                {
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
            }
            catch
            {
                // ignored
            }
            // Cache the failure as null to avoid repeated attempts
            _iconCache[url] = null;
            return null;
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