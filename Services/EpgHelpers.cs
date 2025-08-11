using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;
using System.Text;
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

        /// <summary>
        /// Converts raw bytes representing an XMLTV document (optionally gzipped)
        /// into a UTF-8 string. If the source path or URL ends with <c>.gz</c>, the
        /// bytes are decompressed using <see cref="GZipStream"/> before decoding.
        /// </summary>
        /// <param name="bytes">Raw bytes downloaded or read from disk.</param>
        /// <param name="source">Original source path or URL to determine if gzip is used.</param>
        /// <returns>UTF-8 string of the XMLTV data.</returns>
        public static string ConvertEpgBytesToString(byte[] bytes, string source)
        {
            try
            {
                if (!string.IsNullOrEmpty(source) && source.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    using var input = new MemoryStream(bytes);
                    using var gz = new GZipStream(input, CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    gz.CopyTo(output);
                    return Encoding.UTF8.GetString(output.ToArray());
                }
                else
                {
                    return Encoding.UTF8.GetString(bytes);
                }
            }
            catch
            {
                // Fallback to plain UTF-8 if decompression fails
                return Encoding.UTF8.GetString(bytes);
            }
        }
    }
}