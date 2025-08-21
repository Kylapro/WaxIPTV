using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using WaxIPTV.Models;

namespace WaxIPTV.EpgGuide;

public sealed class GuideViewModel : INotifyPropertyChanged
{
    public ObservableCollection<ChannelRow> Channels { get; } = new();
    public ObservableCollection<ChannelRow> FilteredChannels { get; } = new();

    public ObservableCollection<string> Groups { get; } = new();

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); ApplyFilters(); }
    }

    private string? _selectedGroup;
    public string? SelectedGroup
    {
        get => _selectedGroup;
        set { _selectedGroup = value; OnPropertyChanged(); ApplyFilters(); }
    }

    private DateTime _visibleStartUtc = DateTime.UtcNow.AddMinutes(-15);
    public DateTime VisibleStartUtc { get => _visibleStartUtc; set { _visibleStartUtc = value; OnPropertyChanged(); OnPropertyChanged(nameof(VisibleEndUtc)); OnPropertyChanged(nameof(TimelinePixelWidth)); } }

    private TimeSpan _visibleDuration = TimeSpan.FromHours(3);
    public TimeSpan VisibleDuration { get => _visibleDuration; set { _visibleDuration = value; OnPropertyChanged(); OnPropertyChanged(nameof(VisibleEndUtc)); OnPropertyChanged(nameof(TimelinePixelWidth)); } }

    public DateTime VisibleEndUtc => VisibleStartUtc + VisibleDuration;

    private double _pxPerMinute = 4.0; // ~240 px/hour
    public double PxPerMinute { get => _pxPerMinute; set { _pxPerMinute = value; OnPropertyChanged(); OnPropertyChanged(nameof(TimelinePixelWidth)); } }
    public double TimelinePixelWidth => Math.Max(1, VisibleDuration.TotalMinutes * PxPerMinute);

    private ProgramBlock? _selectedProgram;
    public ProgramBlock? SelectedProgram { get => _selectedProgram; set { _selectedProgram = value; OnPropertyChanged(); } }

    public ICommand JumpNowCommand     { get; }
    public ICommand PageEarlierCommand { get; }
    public ICommand PageLaterCommand   { get; }
    public ICommand DayEarlierCommand  { get; }
    public ICommand DayLaterCommand    { get; }

    public GuideViewModel()
    {
        JumpNowCommand     = new Relay(() => VisibleStartUtc = DateTime.UtcNow.AddMinutes(-15));
        PageEarlierCommand = new Relay(() => VisibleStartUtc = VisibleStartUtc.AddHours(-1));
        PageLaterCommand   = new Relay(() => VisibleStartUtc = VisibleStartUtc.AddHours(+1));
        DayEarlierCommand  = new Relay(() => VisibleStartUtc = VisibleStartUtc.AddDays(-1));
        DayLaterCommand    = new Relay(() => VisibleStartUtc = VisibleStartUtc.AddDays(+1));
    }

    public async Task LoadIncrementalAsync(IList<Channel> channels, Dictionary<string, List<Programme>> programmes)
    {
        Channels.Clear();
        FilteredChannels.Clear();

        Groups.Clear();
        Groups.Add("All");

        int i = 0;
        foreach (var ch in channels)
        {
            var row = new ChannelRow
            {
                TvgId = ch.Id,
                Number = (i + 1).ToString(),
                Name = ch.Name,
                Group = ch.Group,
                LogoPath = ch.Logo
            };

            if (programmes.TryGetValue(ch.Id, out var list))
            {
                foreach (var p in list)
                {
                    row.Programs.Add(new ProgramBlock
                    {
                        ChannelTvgId = ch.Id,
                        Title = p.Title,
                        Synopsis = p.Desc,
                        StartUtc = p.StartUtc.UtcDateTime,
                        EndUtc = p.EndUtc.UtcDateTime,
                        IsLive = false,
                        IsNew = false
                    });
                }
            }

            Channels.Add(row);
            FilteredChannels.Add(row);

            if (!string.IsNullOrWhiteSpace(ch.Group) && !Groups.Any(g => string.Equals(g, ch.Group, StringComparison.OrdinalIgnoreCase)))
                Groups.Add(ch.Group!);

            i++;
            if (i % 20 == 0)
                await Task.Yield();
        }

        SelectedGroup = "All";
        ApplyFilters();
        var now = DateTime.UtcNow;
        VisibleStartUtc = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute < 30 ? 0 : 30, 0, DateTimeKind.Utc).AddMinutes(-30);
    }

    public async Task LoadFromAsync(EpgSnapshot snapshot)
    {
        Channels.Clear();
        FilteredChannels.Clear();

        var programsByChannel = snapshot.Programs
            .GroupBy(p => p.ChannelTvgId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.StartUtc));

        Groups.Clear();
        Groups.Add("All");

        int i = 0;
        foreach (var ch in snapshot.Channels)
        {
            var row = new ChannelRow
            {
                TvgId = ch.TvgId,
                Number = ch.Number,
                Name = ch.Name,
                Group = ch.Group,
                LogoPath = ch.LogoPath
            };

            if (programsByChannel.TryGetValue(ch.TvgId, out var list))
            {
                foreach (var p in list)
                    row.Programs.Add(p);
            }

            Channels.Add(row);
            FilteredChannels.Add(row);

            if (!string.IsNullOrWhiteSpace(ch.Group) && !Groups.Any(g => string.Equals(g, ch.Group, StringComparison.OrdinalIgnoreCase)))
                Groups.Add(ch.Group!);

            i++;
            if (i % 20 == 0)
                await Task.Yield();
        }

        SelectedGroup = "All";
        ApplyFilters();
        var now = DateTime.UtcNow;
        VisibleStartUtc = new DateTime(now.Year, now.Month, now.Day, now.Minute < 30 ? now.Hour : now.Hour, now.Minute < 30 ? 0 : 30, 0, DateTimeKind.Utc).AddMinutes(-30);
    }

    private void ApplyFilters()
    {
        IEnumerable<ChannelRow> query = Channels;

        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query.Where(c => c.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(SelectedGroup) && SelectedGroup != "All")
            query = query.Where(c => string.Equals(c.Group, SelectedGroup, StringComparison.OrdinalIgnoreCase));

        query = query.OrderBy(c => c.Group).ThenBy(c => c.Name);

        FilteredChannels.Clear();
        foreach (var ch in query)
            FilteredChannels.Add(ch);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

file sealed class Relay : ICommand
{
    private readonly Action _a; public Relay(Action a) => _a = a;
    public bool CanExecute(object? p) => true;
    public void Execute(object? p) => _a();
    public event EventHandler? CanExecuteChanged;
}
