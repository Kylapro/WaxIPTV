using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using WaxIPTV.Models;

namespace WaxIPTV.Services
{
    /// <summary>
    /// Utilities for parsing XMLTV data (EPG).  This implementation uses a streaming
    /// <see cref="XmlReader"/> to process large feeds efficiently and normalises
    /// timestamp formats into UTC.  It tolerates common variations such as
    /// "YYYYMMDDHHMMSS -0600", "YYYYMMDDHHMMSS-0600", suffix "Z" for UTC and
    /// missing seconds.  All programme times returned are converted to UTC.
    /// </summary>
    public static class Xmltv
    {
        /// <summary>
        /// Parses an XMLTV document and returns a dictionary of channel identifiers
        /// to display names along with a list of programme entries.  Parsing is
        /// performed using a streaming reader to minimise memory consumption.
        /// Programmes that lack a channel identifier or valid start/stop times are
        /// ignored so inaccurate EPG data is not returned.
        /// </summary>
        /// <param name="xml">Raw XMLTV content.</param>
        /// <returns>A tuple containing channel names and programmes.</returns>
        public static (Dictionary<string, string> ChannelNames, List<Programme> Programmes) Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return (new Dictionary<string, string>(), new List<Programme>());

            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var programmes = new List<Programme>();

            using var sr = new StringReader(xml);
            var settings = new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                IgnoreComments = true,
                DtdProcessing = DtdProcessing.Ignore
            };
            using var xr = XmlReader.Create(sr, settings);

            while (xr.Read())
            {
                // Only process start elements
                if (xr.NodeType != XmlNodeType.Element)
                    continue;

                // Handle <channel> nodes
                if (xr.Name.Equals("channel", StringComparison.OrdinalIgnoreCase))
                {
                    var id = xr.GetAttribute("id") ?? string.Empty;
                    if (!string.IsNullOrEmpty(id))
                    {
                        string? displayName = null;
                        // Create a subtree reader to process child elements without
                        // advancing the main reader beyond this node
                        using var sub = xr.ReadSubtree();
                        sub.Read();
                        while (sub.Read())
                        {
                            if (sub.NodeType == XmlNodeType.Element && sub.Name.Equals("display-name", StringComparison.OrdinalIgnoreCase))
                            {
                                displayName = sub.ReadElementContentAsString();
                                break;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(displayName))
                            names[id] = displayName!;
                    }
                }
                // Handle <programme> nodes
                else if (xr.Name.Equals("programme", StringComparison.OrdinalIgnoreCase))
                {
                    var channelId = xr.GetAttribute("channel") ?? string.Empty;
                    var startRaw = xr.GetAttribute("start") ?? string.Empty;
                    var stopRaw = xr.GetAttribute("stop") ?? string.Empty;
                    var start = ParseXmltvTime(startRaw);
                    var stop = ParseXmltvTime(stopRaw);
                    string? title = null;
                    string? desc = null;
                    using var sub = xr.ReadSubtree();
                    sub.Read();
                    while (sub.Read())
                    {
                        if (sub.NodeType != XmlNodeType.Element)
                            continue;
                        if (sub.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
                        {
                            title = sub.ReadElementContentAsString();
                        }
                        else if (sub.Name.Equals("desc", StringComparison.OrdinalIgnoreCase))
                        {
                            desc = sub.ReadElementContentAsString();
                        }
                    }
                    // Only include the programme if it has a channel, title and valid times
                    if (!string.IsNullOrWhiteSpace(channelId) &&
                        !string.IsNullOrWhiteSpace(title) &&
                        start != DateTimeOffset.MinValue &&
                        stop != DateTimeOffset.MinValue &&
                        stop > start)
                    {
                        // Use empty strings for missing fields to avoid nulls; description remains nullable
                        programmes.Add(new Programme(channelId, start, stop, title!.Trim(), desc));
                    }
                }
            }

            // Ensure programmes are sorted by start time for downstream logic such as now/next
            programmes.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
            return (names, programmes);
        }

        /// <summary>
        /// Streams programmes from an XMLTV document while populating a dictionary
        /// of channel identifiers to display names.  This method yields programmes
        /// sequentially, allowing callers to process them in small groups and
        /// thereby reduce memory pressure.  Programmes missing a channel ID or a
        /// valid time range are skipped.
        /// </summary>
        /// <param name="xml">Raw XMLTV content.</param>
        /// <param name="channelNames">Dictionary to receive channel id/display name pairs.</param>
        /// <param name="allowedChannelIds">Optional set of channel IDs to include. When provided,
        /// only programmes whose channel attribute is contained in this set are yielded.</param>
        /// <returns>An enumeration of <see cref="Programme"/> items.</returns>
        public static IEnumerable<Programme> StreamProgrammes(string xml, Dictionary<string, string> channelNames, HashSet<string>? allowedChannelIds = null)
        {
            if (string.IsNullOrWhiteSpace(xml))
                yield break;

            using var sr = new StringReader(xml);
            var settings = new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                IgnoreComments = true,
                DtdProcessing = DtdProcessing.Ignore
            };
            using var xr = XmlReader.Create(sr, settings);

            while (xr.Read())
            {
                if (xr.NodeType != XmlNodeType.Element)
                    continue;

                if (xr.Name.Equals("channel", StringComparison.OrdinalIgnoreCase))
                {
                    var id = xr.GetAttribute("id") ?? string.Empty;
                    if (!string.IsNullOrEmpty(id))
                    {
                        string? displayName = null;
                        using var sub = xr.ReadSubtree();
                        sub.Read();
                        while (sub.Read())
                        {
                            if (sub.NodeType == XmlNodeType.Element &&
                                sub.Name.Equals("display-name", StringComparison.OrdinalIgnoreCase))
                            {
                                displayName = sub.ReadElementContentAsString();
                                break;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(displayName))
                            channelNames[id] = displayName!;
                    }
                }
                else if (xr.Name.Equals("programme", StringComparison.OrdinalIgnoreCase))
                {
                    var channelId = xr.GetAttribute("channel") ?? string.Empty;
                    if (allowedChannelIds != null && !allowedChannelIds.Contains(channelId))
                        continue;
                    var startRaw = xr.GetAttribute("start") ?? string.Empty;
                    var stopRaw = xr.GetAttribute("stop") ?? string.Empty;
                    var start = ParseXmltvTime(startRaw);
                    var stop = ParseXmltvTime(stopRaw);
                    string? title = null;
                    string? desc = null;
                    using var sub = xr.ReadSubtree();
                    sub.Read();
                    while (sub.Read())
                    {
                        if (sub.NodeType != XmlNodeType.Element)
                            continue;
                        if (sub.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
                            title = sub.ReadElementContentAsString();
                        else if (sub.Name.Equals("desc", StringComparison.OrdinalIgnoreCase))
                            desc = sub.ReadElementContentAsString();
                    }
                    if (!string.IsNullOrWhiteSpace(channelId) &&
                        !string.IsNullOrWhiteSpace(title) &&
                        start != DateTimeOffset.MinValue &&
                        stop != DateTimeOffset.MinValue &&
                        stop > start)
                    {
                        yield return new Programme(channelId, start, stop, title!.Trim(), desc);
                    }
                }
            }
        }

        /// <summary>
        /// Parses an XMLTV timestamp into a UTC <see cref="DateTimeOffset"/>.  Supports
        /// formats like "YYYYMMDDHHMMSS -0600", "YYYYMMDDHHMMSS-0600", suffix "Z",
        /// and truncated times without seconds ("YYYYMMDDHHMM").  Returns UTC time.
        /// </summary>
        /// <param name="raw">The raw timestamp string.</param>
        /// <returns>Parsed UTC timestamp; <see cref="DateTimeOffset.MinValue"/> if parsing fails.</returns>
        private static DateTimeOffset ParseXmltvTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return DateTimeOffset.MinValue;

            raw = raw.Trim();

            // Many XMLTV feeds now provide ISO 8601 timestamps such as
            // "2024-05-03T20:00:00Z" or "2024-05-03T20:00:00+02:00".  These
            // formats are not handled by the original custom parsing logic
            // below, so attempt a standard DateTimeOffset parse first.  If
            // successful we normalise to UTC and return immediately.
            if (DateTimeOffset.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var isoDto))
            {
                return isoDto;
            }

            // Separate date/time part and timezone part
            string datePart = raw;
            string tzPart = string.Empty;

            // Case 1: time and offset separated by space
            var spaceIndex = raw.IndexOf(' ');
            if (spaceIndex > 0)
            {
                datePart = raw[..spaceIndex];
                tzPart = raw[(spaceIndex + 1)..];
            }
            else
            {
                // Case 2: offset appended without space
                if (raw.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
                {
                    datePart = raw[..^1];
                    tzPart = "Z";
                }
                else if (raw.Length >= 19 && (raw[^5] == '+' || raw[^5] == '-'))
                {
                    // "yyyyMMddHHmmss±HHmm"
                    datePart = raw[..^5];
                    tzPart = raw[^5..];
                }
            }

            // Normalise timezone: e.g., "-0600" -> "-06:00"
            if (tzPart.Length == 5 && (tzPart[0] == '+' || tzPart[0] == '-'))
                tzPart = tzPart.Insert(3, ":");

            // Determine format; some feeds may omit seconds
            string format;
            switch (datePart.Length)
            {
                case 14: // yyyyMMddHHmmss
                    format = "yyyyMMddHHmmss";
                    break;
                case 12: // yyyyMMddHHmm
                    format = "yyyyMMddHHmm";
                    break;
                default:
                    format = "yyyyMMddHHmmss";
                    break;
            }

            try
            {
                // If timezone part is "Z", treat datePart as UTC
                if (tzPart.Equals("Z", StringComparison.OrdinalIgnoreCase))
                {
                    var dto = DateTimeOffset.ParseExact(datePart, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                    return dto.ToUniversalTime();
                }
                // If timezone part exists, parse with offset
                if (!string.IsNullOrEmpty(tzPart))
                {
                    var dto = DateTimeOffset.ParseExact($"{datePart} {tzPart}", $"{format} zzz", CultureInfo.InvariantCulture, DateTimeStyles.None);
                    return dto.ToUniversalTime();
                }
                // Fallback: assume UTC if no timezone provided
                var assumed = DateTimeOffset.ParseExact(datePart, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
                return assumed.ToUniversalTime();
            }
            catch
            {
                return DateTimeOffset.MinValue;
            }
        }
    }
}