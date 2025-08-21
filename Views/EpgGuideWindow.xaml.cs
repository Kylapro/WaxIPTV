using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WaxIPTV.Models;

namespace WaxIPTV.Views
{
    /// <summary>
    /// Window displaying an EPG timeline for all channels.  A search box and
    /// group selector allow filtering.  Selecting a programme shows details in
    /// the header with a Play button to start playback via a callback provided
    /// by the main window.
    /// </summary>
    public partial class EpgGuideWindow : Window
    {
        private readonly List<Channel> _channels;
        private readonly Dictionary<string, List<Programme>> _programmes;
        private readonly Func<Channel, Task>? _playCallback;
        private readonly DateTimeOffset _startUtc;
        private readonly DateTimeOffset _endUtc;

        private string _searchTerm = string.Empty;
        private string? _selectedGroup;
        private Channel? _selectedChannel;
        private Programme? _selectedProgramme;

        public EpgGuideWindow(List<Channel> channels,
                              Dictionary<string, List<Programme>> programmes,
                              Func<Channel, Task>? playCallback = null)
        {
            InitializeComponent();
            _channels = channels;
            _programmes = programmes;
            _playCallback = playCallback;
            _startUtc = DateTimeOffset.UtcNow;
            _endUtc = _startUtc.AddHours(12);

            PopulateGroupFilter();
            BuildTimelineHeader();
            ApplyFilter();
        }

        private void PopulateGroupFilter()
        {
            var groups = _channels
                .Select(c => string.IsNullOrWhiteSpace(c.Group) ? "Ungrouped" : c.Group!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g)
                .ToList();
            groups.Insert(0, "All");
            GroupFilter.ItemsSource = groups;
            GroupFilter.SelectedIndex = 0;
        }

        private void BuildTimelineHeader()
        {
            var items = new List<TimelineHeaderItem>();
            var localStart = _startUtc.ToLocalTime();
            for (int i = 0; i <= 12; i++)
            {
                var t = localStart.AddHours(i);
                items.Add(new TimelineHeaderItem
                {
                    Left = i * 60,
                    Label = t.ToString("HH:mm")
                });
            }
            TimelineHeader.ItemsSource = items;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTerm = SearchBox.Text ?? string.Empty;
            ApplyFilter();
        }

        private void GroupFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedGroup = GroupFilter.SelectedItem as string;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            IEnumerable<Channel> filtered = _channels;

            if (!string.IsNullOrEmpty(_searchTerm))
            {
                var term = _searchTerm;
                filtered = filtered.Where(ch =>
                    (!string.IsNullOrEmpty(ch.Name) && ch.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(ch.TvgId) && ch.TvgId.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(ch.Group) && ch.Group.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }

            if (!string.IsNullOrWhiteSpace(_selectedGroup) && !string.Equals(_selectedGroup, "All", StringComparison.OrdinalIgnoreCase))
            {
                var sel = _selectedGroup;
                filtered = filtered.Where(ch => string.Equals(ch.Group ?? "Ungrouped", sel, StringComparison.OrdinalIgnoreCase));
            }

            var rows = filtered
                .Select(ch => new ChannelEpgRow
                {
                    Channel = ch,
                    Blocks = BuildBlocks(ch)
                })
                .ToList();

            ChannelItems.ItemsSource = rows;
        }

        private List<EpgBlock> BuildBlocks(Channel ch)
        {
            var blocks = new List<EpgBlock>();
            if (!_programmes.TryGetValue(ch.Id, out var list))
                return blocks;
            foreach (var prog in list)
            {
                if (prog.EndUtc <= _startUtc || prog.StartUtc >= _endUtc)
                    continue;
                var start = prog.StartUtc < _startUtc ? _startUtc : prog.StartUtc;
                var end = prog.EndUtc > _endUtc ? _endUtc : prog.EndUtc;
                var left = (start - _startUtc).TotalMinutes;
                var width = (end - start).TotalMinutes;
                blocks.Add(new EpgBlock { Channel = ch, Programme = prog, Left = left, Width = width });
            }
            return blocks;
        }

        private void ProgrammeBlock_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is EpgBlock block)
            {
                _selectedProgramme = block.Programme;
                _selectedChannel = block.Channel;
                UpdateDetails();
            }
        }

        private async void PlayProgrammeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedChannel != null && _playCallback != null)
            {
                await _playCallback(_selectedChannel);
            }
        }

        private void UpdateDetails()
        {
            if (_selectedProgramme != null && _selectedChannel != null)
            {
                DetailTitle.Text = _selectedProgramme.Title + " (" + _selectedChannel.Name + ")";
                var localStart = _selectedProgramme.StartUtc.ToLocalTime();
                var localEnd = _selectedProgramme.EndUtc.ToLocalTime();
                DetailTime.Text = $"{localStart:HH:mm} - {localEnd:HH:mm}";
                DetailDesc.Text = _selectedProgramme.Desc ?? string.Empty;
                PlayProgrammeButton.Visibility = Visibility.Visible;
            }
            else
            {
                DetailTitle.Text = string.Empty;
                DetailTime.Text = string.Empty;
                DetailDesc.Text = string.Empty;
                PlayProgrammeButton.Visibility = Visibility.Collapsed;
            }
        }

        private sealed class ChannelEpgRow
        {
            public required Channel Channel { get; init; }
            public required List<EpgBlock> Blocks { get; init; }
        }

        private sealed class EpgBlock
        {
            public required Channel Channel { get; init; }
            public required Programme Programme { get; init; }
            public double Left { get; init; }
            public double Width { get; init; }
        }

        private sealed class TimelineHeaderItem
        {
            public double Left { get; init; }
            public required string Label { get; init; }
        }
    }
}
