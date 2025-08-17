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
            // Build a lookup of channels keyed by normalised names. Some playlists include duplicate
            // names such as "Channel", "Channel HD", "Channel 4K" which normalise to the same key.
            // Group the channels by normalised name and choose the first channel that has a TVG ID
            // as the representative for that name; otherwise pick the first in the group.  This
            // prevents duplicate keys from throwing and avoids losing EPG mapping for all entries.
            var channelsByNormName = channels
                .GroupBy(c => NormalizeName(c.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.FirstOrDefault(x => !string.IsNullOrEmpty(x.TvgId)) ?? g.First(),
                    StringComparer.OrdinalIgnoreCase);
            var channelIndex = channels
                .Select((c, i) => new { c.Id, Index = i })
                .ToDictionary(x => x.Id, x => x.Index, StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, List<Programme>>();

            foreach (var chunk in programmes.Chunk(batchSize))
            {
                foreach (var prog in chunk)
                {
                    Channel? match = null;
                    // Skip programmes with no channel ID to avoid KeyNotFound exceptions
                    if (string.IsNullOrWhiteSpace(prog.ChannelId))
                        continue;
                    if (channelsByTvgId.TryGetValue(prog.ChannelId, out var byId))
                    {
                        match = byId;
                    }
                    else if (channelNames.TryGetValue(prog.ChannelId, out var displayName))
                    {
                        // First attempt an exact normalised match using overrides and known names
                        var norm = NormalizeName(displayName);
                        if (overrides.TryGetValue(norm, out var overrideTvgId) &&
                            channelsByTvgId.TryGetValue(overrideTvgId, out var overrideChannel))
                        {
                            match = overrideChannel;
                        }
                        if (match == null && channelsByNormName.TryGetValue(norm, out var byName))
                        {
                            match = byName;
                            // Update the channel's TVG ID if it differs from the programme's channel ID to improve future lookups
                            if (!string.IsNullOrEmpty(prog.ChannelId) && (!string.Equals(byName.TvgId, prog.ChannelId, StringComparison.OrdinalIgnoreCase)))
                            {
                                var updated = byName with { TvgId = prog.ChannelId };
                                channels[channelIndex[byName.Id]] = updated;
                                channelsByNormName[norm] = updated;
                                channelsByTvgId[prog.ChannelId] = updated;
                            }
                        }
                        // If still no match, attempt a fuzzy match based on Levenshtein similarity
                        if (match == null)
                        {
                            var fuzzy = FindBestChannel(displayName, channelsByNormName);
                            if (fuzzy != null)
                            {
                                match = fuzzy;
                                // As above, update the TVG ID to the XMLTV channel id for consistency
                                var normF = NormalizeName(displayName);
                                if (!string.IsNullOrEmpty(prog.ChannelId) && (!string.Equals(fuzzy.TvgId, prog.ChannelId, StringComparison.OrdinalIgnoreCase)))
                                {
                                    var updated = fuzzy with { TvgId = prog.ChannelId };
                                    channels[channelIndex[fuzzy.Id]] = updated;
                                    channelsByNormName[normF] = updated;
                                    channelsByTvgId[prog.ChannelId] = updated;
                                }
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
            // Return empty string for null or whitespace names
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            // Convert to lowercase and replace ampersands with "and" to unify variations like "A&E" and "A and E"
            var lower = name.ToLowerInvariant().Replace("&", "and");
            // Remove any character that is not a letter or digit to collapse punctuation and spaces
            var sb = new StringBuilder(lower.Length);
            foreach (var ch in lower)
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(ch);
            }
            var norm = sb.ToString();
            // Remove one common suffix if present.  This helps match variations like "channelhd", "networkusa", etc.
            var suffixes = new[] { "uhd", "hd", "fhd", "4k", "sd", "tv", "channel", "network", "us", "usa", "uk", "ca", "de" };
            foreach (var suffix in suffixes)
            {
                if (norm.EndsWith(suffix, StringComparison.Ordinal))
                {
                    norm = norm.Substring(0, norm.Length - suffix.Length);
                    break;
                }
            }
            return norm;
        }

        /// <summary>
        /// Computes the Levenshtein distance between two strings.  This value
        /// represents the number of single-character edits (insertions,
        /// deletions or substitutions) required to transform one string into
        /// the other.  The implementation is iterative to avoid recursion
        /// overhead and handles empty strings gracefully.
        /// </summary>
        private static int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;
            var m = s.Length;
            var n = t.Length;
            var d = new int[m + 1, n + 1];
            for (int i = 0; i <= m; i++) d[i, 0] = i;
            for (int j = 0; j <= n; j++) d[0, j] = j;
            for (int i = 1; i <= m; i++)
            {
                var si = s[i - 1];
                for (int j = 1; j <= n; j++)
                {
                    var cost = si == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1,    // deletion
                                 d[i, j - 1] + 1),    // insertion
                        d[i - 1, j - 1] + cost        // substitution
                    );
                }
            }
            return d[m, n];
        }

        /// <summary>
        /// Computes a similarity score between two strings using the
        /// Levenshtein distance.  The score is 1.0 for identical strings
        /// and approaches 0.0 as they diverge.  It is calculated as
        /// 1 - (distance / maxLength).  When both strings are empty,
        /// the score is 1.0.  When only one is empty, the score is 0.0.
        /// </summary>
        private static double Similarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1.0;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;
            var dist = LevenshteinDistance(a, b);
            var maxLen = Math.Max(a.Length, b.Length);
            return 1.0 - (double)dist / maxLen;
        }

        /// <summary>
        /// Attempts to find the channel from the playlist whose normalised name
        /// best matches the given XMLTV display name.  A simple similarity
        /// metric based on the Levenshtein distance is used.  If the best
        /// match has a score below 0.4, null is returned to avoid spurious
        /// associations.  This fuzzy matching is only used when no exact
        /// match is found via TVG ID or normalised names.
        /// </summary>
        /// <param name="displayName">The XMLTV channel display name.</param>
        /// <param name="channelsByNormName">Lookup of normalised playlist channel names to channels.</param>
        /// <returns>A channel if a sufficiently similar match exists; otherwise null.</returns>
        private static Channel? FindBestChannel(string displayName, Dictionary<string, Channel> channelsByNormName)
        {
            if (string.IsNullOrWhiteSpace(displayName) || channelsByNormName.Count == 0)
                return null;
            var normDisplay = NormalizeName(displayName);
            double bestScore = 0.0;
            Channel? best = null;
            foreach (var kv in channelsByNormName)
            {
                var score = Similarity(normDisplay, kv.Key);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = kv.Value;
                }
            }
            // Require a reasonable threshold to avoid incorrect matches
            return bestScore >= 0.4 ? best : null;
        }
    }
}