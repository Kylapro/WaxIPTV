using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace WaxIPTV.EpgGuide;

public sealed class GuideViewModel : INotifyPropertyChanged
{
    public ObservableCollection<ChannelRow> Channels { get; } = new();

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

    public void LoadFrom(EpgSnapshot snapshot)
    {
        Channels.Clear();
        foreach (var ch in snapshot.Channels)
        {
            var row = new ChannelRow { TvgId = ch.TvgId, Number = ch.Number, Name = ch.Name, LogoPath = ch.LogoPath };
            foreach (var p in snapshot.Programs.Where(p => p.ChannelTvgId == ch.TvgId).OrderBy(p => p.StartUtc))
                row.Programs.Add(p);
            Channels.Add(row);
        }
        var now = DateTime.UtcNow;
        VisibleStartUtc = new DateTime(now.Year, now.Month, now.Day, now.Minute < 30 ? now.Hour : now.Hour, now.Minute < 30 ? 0 : 30, 0, DateTimeKind.Utc).AddMinutes(-30);
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
