using System;

namespace WaxIPTV.Models
{
    /// <summary>
    /// Represents a single programme entry in an XMLTV feed.  Each programme is tied
    /// to a channel by its XMLTV channel identifier, and includes start and end
    /// times in UTC as well as a title and an optional description.
    /// </summary>
    public record Programme(
        string ChannelId,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        string Title,
        string? Desc
    );
}