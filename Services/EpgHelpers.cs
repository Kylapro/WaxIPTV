using System;
using System.Collections.Generic;
using System.Linq;
using WaxIPTV.Models;

namespace WaxIPTV.Services
{
    /// <summary>
    /// Provides helper methods for EPG operations such as computing the current and next
    /// programme for a given channel.  All calculations assume times are stored in UTC
    /// internally and should be converted to local time when displayed.
    /// </summary>
    public static class EpgHelpers
    {
        /// <summary>
        /// Finds the programme currently airing and the one immediately after, given a list
        /// of programmes for a single channel.  Programmes should be sorted by start time.
        /// </summary>
        /// <param name="programmes">Programmes for a specific channel, sorted by start.</param>
        /// <param name="nowUtc">The current time in UTC.</param>
        /// <returns>A tuple containing the current programme (or null) and the next programme (or null).</returns>
        public static (Programme? Now, Programme? Next) GetNowNext(List<Programme> programmes, DateTimeOffset nowUtc)
        {
            // Return early if there are no programmes
            if (programmes == null || programmes.Count == 0)
                return (null, null);

            // Perform a binary search to find the last programme that starts before or at nowUtc.
            int lo = 0;
            int hi = programmes.Count - 1;
            int idx = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var p = programmes[mid];
                if (p.StartUtc <= nowUtc)
                {
                    idx = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            Programme? now = null;
            if (idx >= 0 && programmes[idx].StartUtc <= nowUtc && nowUtc < programmes[idx].EndUtc)
            {
                now = programmes[idx];
            }
            // The next programme is the one immediately after the current
            int nextIndex = idx + 1;
            Programme? next = (nextIndex >= 0 && nextIndex < programmes.Count) ? programmes[nextIndex] : null;
            return (now, next);
        }

        /// <summary>
        /// Sorts the supplied programme list and removes or trims overlapping
        /// entries. Adjacent programmes with the same title are merged. The
        /// method returns the number of overlaps that were trimmed or removed.
        /// </summary>
        /// <param name="programmes">Programme list for a single channel.</param>
        /// <returns>The number of overlaps that were resolved.</returns>
        public static int CleanOverlaps(List<Programme> programmes)
        {
            if (programmes == null || programmes.Count <= 1)
                return 0;
            programmes.Sort((a, b) => a.StartUtc.CompareTo(b.StartUtc));
            var cleaned = new List<Programme>(programmes.Count);
            Programme? prev = programmes[0];
            int overlaps = 0;
            for (int i = 1; i < programmes.Count; i++)
            {
                var cur = programmes[i];
                if (cur.StartUtc < prev.EndUtc)
                {
                    overlaps++;
                    if (string.Equals(cur.Title, prev.Title, StringComparison.OrdinalIgnoreCase))
                    {
                        // merge by extending the previous end time
                        if (cur.EndUtc > prev.EndUtc)
                            prev = prev with { EndUtc = cur.EndUtc };
                        continue;
                    }
                    if (cur.EndUtc <= prev.EndUtc)
                    {
                        // completely overlapped; drop
                        continue;
                    }
                    // partially overlap; trim start
                    cur = cur with { StartUtc = prev.EndUtc };
                }
                cleaned.Add(prev);
                prev = cur;
            }
            cleaned.Add(prev);
            programmes.Clear();
            programmes.AddRange(cleaned);
            return overlaps;
        }
    }
}