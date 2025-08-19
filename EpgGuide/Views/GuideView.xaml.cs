using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace WaxIPTV.EpgGuide;

public partial class GuideView : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    public GuideViewModel VM => (GuideViewModel)DataContext;

    public GuideView()
    {
        InitializeComponent();
        DataContext = new GuideViewModel();
        Loaded += (_, __) =>
        {
            RebuildHourStrip();
            _timer.Tick += (_, __2) => RebuildHourStrip();
            _timer.Start();
            VM.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(VM.VisibleStartUtc) or nameof(VM.VisibleDuration) or nameof(VM.PxPerMinute))
                    RebuildHourStrip();
            };
        };
    }

    private void RebuildHourStrip()
    {
        var hours = new List<DateTime>();
        var start = new DateTime(VM.VisibleStartUtc.Year, VM.VisibleStartUtc.Month, VM.VisibleStartUtc.Day, VM.VisibleStartUtc.Hour, 0, 0, DateTimeKind.Utc);
        var t = start;
        while (t <= VM.VisibleEndUtc.AddHours(1)) { hours.Add(t); t = t.AddHours(1); }
        HourStrip.ItemsSource = hours;

        // each hour == 60 minutes * PxPerMinute
        foreach (var cp in HourStrip.Items.Cast<object>().Select(HourStrip.ItemContainerGenerator.ContainerFromItem).OfType<ContentPresenter>())
            cp.Width = 60 * VM.PxPerMinute;

        TimelineScroller.ScrollToHorizontalOffset(GridScroller.HorizontalOffset);
    }

    private void OnGridScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0)
            TimelineScroller.ScrollToHorizontalOffset(e.HorizontalOffset);
        if (e.VerticalChange != 0)
            ChannelsScroller.ScrollToVerticalOffset(e.VerticalOffset);
    }

    private void OnProgramClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.CommandParameter is ProgramBlock p) VM.SelectedProgram = p;
    }

    private ProgramBlock? Current => VM.SelectedProgram;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Current is null) return;
        switch (e.Key)
        {
            case Key.Left:  MoveHorizontal(-1); e.Handled = true; break;
            case Key.Right: MoveHorizontal(+1); e.Handled = true; break;
            case Key.Up:    MoveVertical(-1);   e.Handled = true; break;
            case Key.Down:  MoveVertical(+1);   e.Handled = true; break;
            case Key.Home:  VM.JumpNowCommand.Execute(null); e.Handled = true; break;
            case Key.PageUp:   VM.PageEarlierCommand.Execute(null); e.Handled = true; break;
            case Key.PageDown: VM.PageLaterCommand.Execute(null);   e.Handled = true; break;
        }
    }

    private void MoveHorizontal(int dir)
    {
        var row = VM.Channels.FirstOrDefault(c => c.TvgId == Current!.ChannelTvgId);
        if (row is null) return;
        var i = row.Programs.IndexOf(Current!);
        var target = dir < 0 ? (i > 0 ? row.Programs[i - 1] : row.Programs.FirstOrDefault())
                             : (i < row.Programs.Count - 1 ? row.Programs[i + 1] : row.Programs.LastOrDefault());
        if (target != null) VM.SelectedProgram = target;
    }

    private void MoveVertical(int dir)
    {
        var ci = VM.Channels.ToList().FindIndex(c => c.TvgId == Current!.ChannelTvgId);
        var ni = ci + dir; if (ni < 0 || ni >= VM.Channels.Count) return;
        var pivot = Current!.StartUtc + TimeSpan.FromSeconds(Math.Max(1, Current!.Duration.TotalSeconds / 2));
        var neighbor = VM.Channels[ni].Programs.FirstOrDefault(p => p.StartUtc < pivot && p.EndUtc > pivot)
                    ?? VM.Channels[ni].Programs.OrderBy(p => Math.Abs((p.StartUtc - pivot).Ticks)).FirstOrDefault();
        if (neighbor != null) VM.SelectedProgram = neighbor;
    }
}
