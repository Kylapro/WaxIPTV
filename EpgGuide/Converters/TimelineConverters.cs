using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace WaxIPTV.EpgGuide;

public sealed class TimeToXConverter : IMultiValueConverter
{
    // values: StartUtc, VisibleStartUtc, PxPerMinute
    public object Convert(object[] v, Type t, object p, CultureInfo c)
        => v[0] is DateTime s && v[1] is DateTime vis && v[2] is double ppm
           ? Math.Max(0, (s - vis).TotalMinutes * ppm) : 0d;
    public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class DurationToWidthConverter : IMultiValueConverter
{
    // values: StartUtc, EndUtc, PxPerMinute
    public object Convert(object[] v, Type t, object p, CultureInfo c)
        => v[0] is DateTime s && v[1] is DateTime e && v[2] is double ppm
           ? Math.Max(1, (e - s).TotalMinutes * ppm) : 1d;
    public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class VisibleProgramsConverter : IMultiValueConverter
{
    // values: IEnumerable<ProgramBlock>, VisibleStartUtc, VisibleEndUtc
    public object Convert(object[] v, Type t, object p, CultureInfo c)
    {
        var list = v[0] as IEnumerable<ProgramBlock> ?? Array.Empty<ProgramBlock>();
        if (v[1] is DateTime start && v[2] is DateTime end)
            return list.Where(pr => pr.EndUtc > start && pr.StartUtc < end).ToArray();
        return list.ToArray();
    }
    public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}
