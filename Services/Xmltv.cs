using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using WaxIPTV.Models;

namespace WaxIPTV.Services
{
    /// <summary>
    /// Utilities for parsing XMLTV data (EPG).  Provides methods to extract channel names
    /// and programme entries from an XMLTV document.  All times are converted to UTC.
    /// </summary>
    public static class Xmltv
    {
        /// <summary>
        /// Parses an XMLTV document and returns a collection of channel display names
        /// and a list of programme entries.  The channel dictionary maps XMLTV channel
        /// identifiers to their display names (as given by the first &lt;display-name&gt;
        /// element under each &lt;channel&gt; node).
        /// </summary>
        /// <param name="xml">Raw XMLTV content.</param>
        /// <returns>A tuple containing the channel name map and the list of programmes.</returns>
        public static (Dictionary<string, string> channelNames, List<Programme> programmes) Parse(string xml)
        {
            var xdoc = XDocument.Parse(xml);
            var root = xdoc.Root ?? throw new ArgumentException("Invalid XMLTV document");
            // Extract channel id → name mapping
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ch in root.Elements("channel"))
            {
                var id = (string?)ch.Attribute("id") ?? string.Empty;
                var name = (string?)ch.Element("display-name") ?? id;
                if (!string.IsNullOrEmpty(id))
                    names[id] = name;
            }
            // Extract programmes
            var progs = new List<Programme>();
            foreach (var p in root.Elements("programme"))
            {
                var channelId = (string?)p.Attribute("channel") ?? string.Empty;
                var startAttr = (string?)p.Attribute("start") ?? string.Empty;
                var stopAttr = (string?)p.Attribute("stop") ?? string.Empty;
                var start = ParseTime(startAttr);
                var stop = ParseTime(stopAttr);
                var title = (string?)p.Element("title") ?? string.Empty;
                var desc = (string?)p.Element("desc");
                progs.Add(new Programme(channelId, start, stop, title, desc));
            }
            return (names, progs);
        }

        /// <summary>
        /// Parses XMLTV time strings into a UTC DateTimeOffset.  Handles formats such as
        /// "YYYYMMDD HHMMSS Z" and "YYYYMMDDTHHMMSSZ", with or without separators.  The
        /// resulting time is converted to UTC.
        /// </summary>
        /// <param name="s">A time string in XMLTV format.</param>
        /// <returns>The corresponding UTC DateTimeOffset.</returns>
        public static DateTimeOffset ParseTime(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return DateTimeOffset.MinValue;
            // Normalize: remove spaces and ensure timezone offset is present
            s = s.Replace(" ", string.Empty).Replace("Z", "+0000");
            // Expected: yyyyMMddHHmmss±HHmm (14+5 = 19 chars)
            // Some files may use local time without seconds: yyyyMMddHHmm±HHmm
            // We'll parse date and time components manually.
            try
            {
                // Extract date/time digits
                var digits = s.Substring(0, 14);
                var y = int.Parse(digits.AsSpan(0, 4));
                var mo = int.Parse(digits.AsSpan(4, 2));
                var d = int.Parse(digits.AsSpan(6, 2));
                var h = int.Parse(digits.AsSpan(8, 2));
                var mi = int.Parse(digits.AsSpan(10, 2));
                var se = int.Parse(digits.AsSpan(12, 2));
                // Extract offset
                var offSign = s[14] == '-' ? -1 : 1;
                var offH = int.Parse(s.AsSpan(15, 2));
                var offM = int.Parse(s.AsSpan(17, 2));
                var offset = new TimeSpan(offSign * offH, offSign * offM, 0);
                var dto = new DateTimeOffset(y, mo, d, h, mi, se, offset);
                return dto.ToUniversalTime();
            }
            catch
            {
                // Fallback: try parse with built-in parser
                if (DateTimeOffset.TryParseExact(s, new[] {
                    "yyyyMMddHHmmsszzz", "yyyyMMddHHmmzzz",
                    "yyyyMMdd'T'HHmmsszzz", "yyyyMMdd'T'HHmmzzz" },
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    return parsed.ToUniversalTime();
                }
                return DateTimeOffset.MinValue;
            }
        }
    }
}