using System;
using System.Collections.ObjectModel;

namespace WaxIPTV.EpgGuide;

public sealed class ProgramBlock
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string ChannelTvgId { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Synopsis { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public bool IsLive { get; init; }
    public bool IsNew { get; init; }
    public TimeSpan Duration => EndUtc - StartUtc;
}

public sealed class ChannelRow
{
    public string TvgId { get; init; } = "";
    public string Number { get; init; } = "";
    public string Name { get; init; } = "";
    public string? LogoPath { get; init; }
    public ObservableCollection<ProgramBlock> Programs { get; } = new();
}

public sealed class ChannelMeta
{
    public required string TvgId { get; init; }
    public required string Number { get; init; }
    public required string Name { get; init; }
    public string? LogoPath { get; init; }
}

public sealed class EpgSnapshot
{
    public required ChannelMeta[] Channels { get; init; }
    public required ProgramBlock[] Programs { get; init; }
}
