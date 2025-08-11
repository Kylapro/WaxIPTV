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
            if (programmes == null || programmes.Count == 0)
                return (null, null);
            Programme? now = null;
            Programme? next = null;
            foreach (var prog in programmes)
            {
                if (prog.StartUtc <= nowUtc && nowUtc < prog.EndUtc)
                {
                    now = prog;
                }
                else if (prog.StartUtc > nowUtc)
                {
                    next = prog;
                    break;
                }
            }
            return (now, next);
        }
    }
}