using System;
using System.Collections.Generic;

namespace WaxIPTV.Models
{
    /// <summary>
    /// Represents a single entry in an M3U playlist.  A channel has an identifier, display name,
    /// optional group and logo, the stream URL and an optional XMLTV ID used to link to EPG data.
    /// </summary>
    public record Channel(
        string Id,
        string Name,
        string? Group,
        string? Logo,
        string StreamUrl,
        string? TvgId = null,
        string? TvgName = null,
        Dictionary<string, string>? Headers = null
    );
}