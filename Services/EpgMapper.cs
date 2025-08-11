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
            return MapProgrammesInBatches(programmes, channels, channelNames, int.MaxValue, overrides);
        }

        /// <summary>
        /// Builds a lookup from channel identifier to a list of programmes scheduled
        /// on that channel. Programmes are processed in batches to keep memory usage
        /// low. When a channel in the playlist is missing or has an invalid TVG ID,
        /// a best-effort match is attempted using normalised names. If a match is
        /// found, the channel is mapped to the programme's TVG ID for subsequent
        /// lookups; otherwise the programme is skipped so no EPG info is shown for
        /// that channel.
        /// </summary>
        /// <param name="programmes">Enumerable of programme entries parsed from XMLTV.</param>
        /// <param name="channels">List of channels parsed from an M3U playlist.</param>
        /// <param name="channelNames">Dictionary mapping XMLTV channel IDs to display names.</param>
        /// <param name="batchSize">Number of programmes to process per batch.</param>
        /// <param name="overrides">Optional manual overrides mapping normalised channel names to TVG IDs.</param>
        /// <returns>Dictionary mapping the internal channel ID to its list of programmes.</returns>
        public static Dictionary<string, List<Programme>> MapProgrammesInBatches(
            IEnumerable<Programme> programmes,
            List<Channel> channels,
            Dictionary<string, string> channelNames,
            int batchSize,
            Dictionary<string, string>? overrides = null)
        {
            overrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var channelsByTvgId = channels
                .Where(c => !string.IsNullOrEmpty(c.TvgId))
                .ToDictionary(c => c.TvgId!, c => c, StringComparer.OrdinalIgnoreCase);
            var channelsByNormName = channels
                .ToDictionary(c => NormalizeName(c.Name), c => c, StringComparer.OrdinalIgnoreCase);
            var channelIndex = channels
                .Select((c, i) => new { c.Id, Index = i })
                .ToDictionary(x => x.Id, x => x.Index, StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, List<Programme>>();

            foreach (var chunk in programmes.Chunk(batchSize))
            {
                foreach (var prog in chunk)
                {
                    Channel? match = null;
                    if (!string.IsNullOrEmpty(prog.ChannelId) && channelsByTvgId.TryGetValue(prog.ChannelId, out var byId))
                    {
                        match = byId;
                    }
                    else if (channelNames.TryGetValue(prog.ChannelId, out var displayName))
                    {
                        var norm = NormalizeName(displayName);
                        if (overrides.TryGetValue(norm, out var overrideTvgId) &&
                            channelsByTvgId.TryGetValue(overrideTvgId, out var overrideChannel))
                        {
                            match = overrideChannel;
                        }
                        if (match == null && channelsByNormName.TryGetValue(norm, out var byName))
                        {
                            match = byName;
                            if (!string.IsNullOrEmpty(prog.ChannelId) && (!string.Equals(byName.TvgId, prog.ChannelId, StringComparison.OrdinalIgnoreCase)))
                            {
                                var updated = byName with { TvgId = prog.ChannelId };
                                channels[channelIndex[byName.Id]] = updated;
                                channelsByNormName[norm] = updated;
                                channelsByTvgId[prog.ChannelId] = updated;
                            }
                        }
                    }
                    if (match != null)
                    {
                        if (!result.TryGetValue(match.Id, out var list))
                        {
                            list = new List<Programme>();
                            result[match.Id] = list;
                        }
                        list.Add(prog);
                    }
                }
            }

            foreach (var list in result.Values)
                list.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));

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