using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WaxIPTV.Models;

namespace WaxIPTV.Services
{
    /// <summary>
    /// Provides a simple parser for M3U/M3U8 playlists.  It extracts the metadata
    /// associated with each channel (such as tvg-id, logo and group-title) and
    /// produces a list of <see cref="Channel"/> records.  Lines starting with
    /// <c>#EXTINF</c> define metadata for the following stream URL; any other
    /// non-comment line is treated as the stream URL itself.
    /// </summary>
    public static class M3UParser
    {
        // Regular expression used to extract key="value" pairs from the EXTINF line
        // Use a verbatim string (prefixed with @) for the regex pattern.  When using verbatim
        // strings, double quotes must be doubled to escape them.  This pattern captures
        // key="value" pairs such as tvg-id="channel-id" and tvg-logo="logo.png".
        private static readonly Regex Attr = new(@"(\w+(?:-\w+)*)=""([^""]*)""", RegexOptions.Compiled);

        /// <summary>
        /// Parses a raw M3U playlist and returns a list of channels.  The parser
        /// tolerates Windows and Unix newline conventions and ignores blank lines
        /// and lines starting with '#'.
        /// </summary>
        /// <param name="m3u">The contents of an M3U or M3U8 file.</param>
        /// <returns>A list of channels extracted from the playlist.</returns>
        public static List<Channel> Parse(string m3u)
        {
            var channels = new List<Channel>();
            Dictionary<string, string>? meta = null;
            string? title = null;

            foreach (var raw in m3u.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var line = raw.Trim();
                if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                {
                    // Start a new metadata block
                    meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Match m in Attr.Matches(line))
                        meta[m.Groups[1].Value] = m.Groups[2].Value;

                    // Extract the display name following the comma
                    var comma = line.IndexOf(',');
                    title = comma >= 0 ? line[(comma + 1)..].Trim() : "Channel";
                }
                else if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#") && meta is not null)
                {
                    // We have a stream URL and corresponding metadata
                    meta.TryGetValue("tvg-id", out var tvgId);
                    // Some playlists use 'logo' instead of 'tvg-logo'.  Fall back accordingly.
                    meta.TryGetValue("tvg-logo", out var logo);
                    if (string.IsNullOrWhiteSpace(logo) && meta.TryGetValue("logo", out var logoAlt))
                    {
                        logo = logoAlt;
                    }
                    meta.TryGetValue("group-title", out var group);
                    // Display name precedence: explicit title from EXTINF, then tvg-name, then tvg-id
                    meta.TryGetValue("tvg-name", out var tvgName);
                    var name = title ?? tvgName ?? tvgId ?? "Channel";
                    // Use the tvg-id if present as the id; otherwise derive from name
                    var id = (tvgId ?? tvgName ?? name).ToLowerInvariant().Replace(' ', '-');
                    // Pass the optional tvg-name through to the Channel record.  This
                    // allows downstream EPG mapping to match on tvg-name when tvg-id
                    // is absent or does not correspond to the XMLTV feed.
                    channels.Add(new Channel(id, name, group, logo, line, tvgId, tvgName));
                    // Reset for the next entry
                    meta = null;
                    title = null;
                }
            }
            return channels;
        }
    }
}