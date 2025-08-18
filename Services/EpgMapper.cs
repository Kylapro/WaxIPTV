using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WaxIPTV.Models;
using WaxIPTV.Services.Logging;

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
            AppLog.Logger.Information("Mapping {ProgCount} programmes", programmes.Count);
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
            using var scope = AppLog.BeginScope("EpgMap");
            overrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var channelsByTvgId = channels
                .Where(c => !string.IsNullOrEmpty(c.TvgId))
                .ToDictionary(c => c.TvgId!, c => c, StringComparer.OrdinalIgnoreCase);
            // Build a lookup of channels keyed by normalised names. Some playlists include duplicate
            // names such as "Channel", "Channel HD", "Channel 4K" which normalise to the same key.
            // Group the channels by normalised name and choose the first channel that has a TVG ID
            // as the representative for that name; otherwise pick the first in the group.  This
            // prevents duplicate keys from throwing and avoids losing EPG mapping for all entries.
            // Build a lookup of channels keyed by normalised names. Some playlists include duplicate
            // names such as "Channel", "Channel HD", "Channel 4K" which normalise to the same key.
            // We collect a list of all candidates for each normalised name instead of picking one
            // representative up front. This allows us to choose the best candidate later (preferring
            // those with a TVG ID) and avoids duplicate-key exceptions.
            // Build lookups of channels keyed by normalised display names and tvg-names.  Some
            // playlists include duplicate names such as "Channel", "Channel HD", etc.  We
            // collect a list of candidates for each normalised key instead of picking one
            // representative up front.  This allows us to choose the best candidate later
            // (preferring those with a TVG ID) and avoids duplicate-key exceptions.  We
            // also build a separate lookup keyed by normalised tvg-name values (if present)
            // so that EPG mapping can match on tvg-name, similar to behaviour in other
            // players such as Televizo.
            var channelsByNormName = new Dictionary<string, List<Channel>>(StringComparer.OrdinalIgnoreCase);
            var channelsByNormTvgName = new Dictionary<string, List<Channel>>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in channels)
            {
                // Normalised display name lookup
                var key = NormalizeName(c.Name);
                if (!channelsByNormName.TryGetValue(key, out var list))
                {
                    list = new List<Channel>();
                    channelsByNormName[key] = list;
                }
                list.Add(c);

                // Normalised tvg-name lookup (if provided)
                if (!string.IsNullOrWhiteSpace(c.TvgName))
                {
                    var tkey = NormalizeName(c.TvgName!);
                    if (!channelsByNormTvgName.TryGetValue(tkey, out var tlist))
                    {
                        tlist = new List<Channel>();
                        channelsByNormTvgName[tkey] = tlist;
                    }
                    tlist.Add(c);
                }
            }
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
                        // 3) tvg-name match (normalize xml display name to match tvg-name values)
                        if (match == null && channelsByNormTvgName.TryGetValue(norm, out var tvgCandidates))
                        {
                            // Choose the best candidate: prefer those with a TVG ID, then shortest display name
                            var chosenTvg = tvgCandidates
                                .OrderByDescending(c => !string.IsNullOrEmpty(c.TvgId))
                                .ThenBy(c => c.Name.Length)
                                .FirstOrDefault();
                            match = chosenTvg;
                            // Update the channel's TVG ID if it differs from the programme's channel ID to improve future lookups
                            if (match != null && !string.IsNullOrEmpty(prog.ChannelId) && !string.Equals(match.TvgId, prog.ChannelId, StringComparison.OrdinalIgnoreCase))
                            {
                                var updated = match with { TvgId = prog.ChannelId };
                                channels[channelIndex[match.Id]] = updated;
                                // update the candidate list
                                var idxCandidate = tvgCandidates.IndexOf(match);
                                if (idxCandidate >= 0) tvgCandidates[idxCandidate] = updated;
                                channelsByTvgId[prog.ChannelId] = updated;
                                match = updated;
                            }
                        }

                        // 4) fallback: normalised display name match on channel name
                        if (match == null && channelsByNormName.TryGetValue(norm, out var candidates))
                        {
                            // Choose the best candidate: prefer those with a TVG ID, then shortest name
                            var chosen = candidates
                                .OrderByDescending(c => !string.IsNullOrEmpty(c.TvgId))
                                .ThenBy(c => c.Name.Length)
                                .FirstOrDefault();
                            match = chosen;
                            // Update the channel's TVG ID if it differs from the programme's channel ID to improve future lookups
                            if (match != null && !string.IsNullOrEmpty(prog.ChannelId) && !string.Equals(match.TvgId, prog.ChannelId, StringComparison.OrdinalIgnoreCase))
                            {
                                var updated = match with { TvgId = prog.ChannelId };
                                channels[channelIndex[match.Id]] = updated;
                                // update the candidate list
                                var idxCandidate = candidates.IndexOf(match);
                                if (idxCandidate >= 0) candidates[idxCandidate] = updated;
                                channelsByTvgId[prog.ChannelId] = updated;
                                match = updated;
                            }
                        }

                        // 5) If still no match, attempt a fuzzy match based on Levenshtein similarity
                        if (match == null)
                        {
                            var fuzzy = FindBestChannel(displayName, channelsByNormName);
                            if (fuzzy != null)
                            {
                                match = fuzzy;
                                // As above, update the TVG ID to the XMLTV channel id for consistency
                                var normF = NormalizeName(displayName);
                                if (!string.IsNullOrEmpty(prog.ChannelId) && !string.Equals(fuzzy.TvgId, prog.ChannelId, StringComparison.OrdinalIgnoreCase))
                                {
                                    var updated = fuzzy with { TvgId = prog.ChannelId };
                                    channels[channelIndex[fuzzy.Id]] = updated;
                                    // update in channelsByNormName list for this normalised key
                                    if (channelsByNormName.TryGetValue(normF, out var listF))
                                    {
                                        var idxF = listF.IndexOf(fuzzy);
                                        if (idxF >= 0) listF[idxF] = updated;
                                    }
                                    // update in tvg-name dictionary if tvg-name present
                                    if (channelsByNormTvgName.TryGetValue(normF, out var listT))
                                    {
                                        var idxT = listT.IndexOf(fuzzy);
                                        if (idxT >= 0) listT[idxT] = updated;
                                    }
                                    channelsByTvgId[prog.ChannelId] = updated;
                                    match = updated;
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

            AppLog.Logger.Information("Mapped programmes for {ChannelCount} channels", result.Count);
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
            // Remove stacked suffixes such as "...uhdhd4k" repeatedly until no more suffix is present.
            var suffixes = new[] { "uhd", "fhd", "hd", "sd", "4k", "tv", "channel", "network", "us", "usa", "uk", "ca", "de" };
            bool stripped;
            do
            {
                stripped = false;
                foreach (var suffix in suffixes)
                {
                    if (norm.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        norm = norm.Substring(0, norm.Length - suffix.Length);
                        stripped = true;
                        break;
                    }
                }
            } while (stripped);
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
        private static Channel? FindBestChannel(string displayName, Dictionary<string, List<Channel>> channelsByNormName)
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
                    // Pick the best candidate from the list: prefer those with a TVG ID and shorter names
                    var candidates = kv.Value;
                    var chosen = candidates
                        .OrderByDescending(c => !string.IsNullOrEmpty(c.TvgId))
                        .ThenBy(c => c.Name.Length)
                        .FirstOrDefault();
                    best = chosen;
                }
            }
            // Require a reasonable threshold to avoid incorrect matches
            return bestScore >= 0.4 ? best : null;
        }
    }
}