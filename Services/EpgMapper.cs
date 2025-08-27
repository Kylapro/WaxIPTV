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
            return MapProgrammesInBatches(programmes, channels, channelNames, int.MaxValue, overrides, null);
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
            Dictionary<string, string>? overrides = null,
            IProgress<int>? progress = null,
            Dictionary<string, List<Programme>>? existing = null)
        {
            using var scope = AppLog.BeginScope("EpgMap");
            overrides ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var channelsByTvgId = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in channels.Where(c => !string.IsNullOrEmpty(c.TvgId)))
            {
                if (!channelsByTvgId.TryAdd(c.TvgId!, c))
                {
                    AppLog.Logger.Warning("Duplicate TVG ID {TvgId} encountered; keeping first occurrence", c.TvgId);
                }
            }
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
            var channelIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < channels.Count; i++)
            {
                var c = channels[i];
                if (!channelIndex.TryAdd(c.Id, i))
                {
                    AppLog.Logger.Warning("Duplicate channel ID {ChannelId} encountered; keeping first occurrence", c.Id);
                }
            }

            var result = existing ?? new Dictionary<string, List<Programme>>();
            int processed = 0;

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

                processed += chunk.Length;
                progress?.Report(processed);
            }

            foreach (var list in result.Values)
            {
                list.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
                CleanOverlaps(list);
            }

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
        /// Cleans a list of programmes by trimming or removing overlaps and
        /// merging adjacent entries that share the same title and description.
        /// Assumes the list is already sorted by start time.
        /// </summary>
        private static void CleanOverlaps(List<Programme> programmes)
        {
            if (programmes.Count < 2)
                return;

            int i = 1;
            while (i < programmes.Count)
            {
                var prev = programmes[i - 1];
                var curr = programmes[i];

                if (curr.StartUtc < prev.EndUtc)
                {
                    if (curr.EndUtc <= prev.EndUtc)
                    {
                        programmes.RemoveAt(i);
                        continue;
                    }
                    curr = curr with { StartUtc = prev.EndUtc };
                    programmes[i] = curr;
                }

                if (prev.EndUtc == curr.StartUtc &&
                    string.Equals(prev.Title, curr.Title, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(prev.Desc, curr.Desc, StringComparison.OrdinalIgnoreCase))
                {
                    var merged = prev with { EndUtc = curr.EndUtc };
                    programmes[i - 1] = merged;
                    programmes.RemoveAt(i);
                    continue;
                }

                i++;
            }
        }
    }
}