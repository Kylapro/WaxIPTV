using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WaxIPTV.Models;

namespace WaxIPTV.Services
{
    /// <summary>
    /// Maps programmes from an XMLTV feed to channels defined in an M3U playlist.
    /// The mapping occurs by matching the programme's channel identifier to the
    /// channel's TVG ID first, then by comparing normalised display names, with
    /// an optional manual overrides dictionary to handle special cases.
    /// </summary>
    public static class EpgMapper
    {
        /// <summary>
        /// Builds a lookup from channel identifier to a list of programmes scheduled
        /// on that channel.  Only programmes that match a channel in the playlist
        /// are included.
        /// </summary>
        /// <param name="programmes">List of programme entries parsed from XMLTV.</param>
        /// <param name="channels">List of channels parsed from an M3U playlist.</param>
        /// <param name="channelNames">Dictionary mapping XMLTV channel IDs to display names.</param>
        /// <param name="overrides">Optional manual overrides mapping normalised channel names to TVG IDs.</param>
        /// <returns>Dictionary mapping the internal channel ID to its list of programmes.</returns>
        public static Dictionary<string, List<Programme>> MapProgrammes(
            List<Programme> programmes,
            List<Channel> channels,
            Dictionary<string, string> channelNames,
            Dictionary<string, string>? overrides = null)
        {
            overrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Build a lookup of channels by TVG ID and by normalised name
            var channelsByTvgId = channels
                .Where(c => !string.IsNullOrEmpty(c.TvgId))
                .ToDictionary(c => c.TvgId!, c => c, StringComparer.OrdinalIgnoreCase);
            var channelsByNormName = channels
                .ToDictionary(c => NormalizeName(c.Name), c => c, StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, List<Programme>>();

            foreach (var prog in programmes)
            {
                Channel? match = null;
                // Try match by TVG ID first
                if (!string.IsNullOrEmpty(prog.ChannelId) && channelsByTvgId.TryGetValue(prog.ChannelId, out var byId))
                {
                    match = byId;
                }
                else
                {
                    // Attempt manual override: map normalised XMLTV display name to a TVG ID
                    if (channelNames.TryGetValue(prog.ChannelId, out var displayName))
                    {
                        var norm = NormalizeName(displayName);
                        if (overrides.TryGetValue(norm, out var overrideTvgId))
                        {
                            if (channelsByTvgId.TryGetValue(overrideTvgId, out var overrideChannel))
                                match = overrideChannel;
                        }
                        if (match == null)
                        {
                            // Match by normalised name
                            if (channelsByNormName.TryGetValue(norm, out var byName))
                                match = byName;
                        }
                    }
                }
                if (match != null)
                {
                    var id = match.Id;
                    if (!result.TryGetValue(id, out var list))
                    {
                        list = new List<Programme>();
                        result[id] = list;
                    }
                    list.Add(prog);
                }
            }
            return result;
        }

        /// <summary>
        /// Normalises a channel name for comparison by converting to lowercase,
        /// removing punctuation and whitespace, and stripping common suffixes like
        /// "hd", "uhd" and "4k".
        /// </summary>
        /// <param name="name">Channel display name to normalise.</param>
        /// <returns>A normalised channel name.</returns>
        public static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            // Lowercase and remove non-alphanumeric characters
            var sb = new StringBuilder();
            foreach (var ch in name.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(ch);
            }
            var norm = sb.ToString();
            // Remove common suffixes
            foreach (var suffix in new[] { "uhd", "hd", "4k" })
            {
                if (norm.EndsWith(suffix))
                {
                    norm = norm.Substring(0, norm.Length - suffix.Length);
                    break;
                }
            }
            return norm;
        }
    }
}