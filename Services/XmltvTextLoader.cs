using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WaxIPTV.Services
{
    /// <summary>
    /// Provides helper methods for loading XMLTV guides from either local files
    /// or HTTP/HTTPS URLs.  This class reads raw bytes, detects gzip by magic
    /// numbers and inflates when necessary, then decodes the bytes into a
    /// string, respecting byte order marks (BOMs) or the encoding declared in
    /// the XML prolog.  It also ensures HTTP responses are decompressed at
    /// the transport layer for content-encoded gzip/deflate/brotli.
    /// </summary>
    public static class XmltvTextLoader
    {
        // A singleton HttpClient configured to automatically decompress HTTP
        // responses that include a Content-Encoding header.  Reusing a single
        // HttpClient instance avoids port exhaustion and improves performance.
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                // Enable automatic decompression of HTTP transfer encodings.
                AutomaticDecompression = DecompressionMethods.GZip |
                                         DecompressionMethods.Deflate |
                                         DecompressionMethods.Brotli
            };
            return new HttpClient(handler, disposeHandler: true);
        }

        /// <summary>
        /// Loads the contents of the specified source into a string.  The
        /// source may be a local file path or an HTTP/HTTPS URL.  If the
        /// downloaded bytes are gzip-compressed (either the payload itself
        /// or via HTTP transport), the bytes are decompressed before being
        /// decoded into a string.  The method attempts to detect the
        /// encoding using a BOM, falling back to the encoding specified in
        /// the XML declaration, and defaults to UTF‑8 when no encoding is
        /// specified.
        /// </summary>
        /// <param name="source">A local file path or HTTP/HTTPS URL.</param>
        /// <param name="ct">Optional cancellation token.</param>
        /// <returns>The decoded XML string, or an empty string if the source
        /// could not be read.</returns>
        public static async Task<string> LoadAsync(string source, CancellationToken ct = default)
        {
            byte[] data;

            // Determine whether to treat the source as a URL or a local file.
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                // Download the raw bytes.  The HttpClient's handler will
                // automatically decompress any HTTP content-encoding.
                data = await Http.GetByteArrayAsync(uri, ct).ConfigureAwait(false);
            }
            else
            {
                if (!File.Exists(source))
                    return string.Empty;

                data = await File.ReadAllBytesAsync(source, ct).ConfigureAwait(false);
            }

            // If the payload starts with the gzip magic numbers, inflate it.
            if (IsGzip(data))
            {
                using var input = new MemoryStream(data, writable: false);
                using var gz = new GZipStream(input, CompressionMode.Decompress);
                using var inflated = new MemoryStream();
                await gz.CopyToAsync(inflated, ct).ConfigureAwait(false);
                data = inflated.ToArray();
            }

            // Decode using BOM, XML prolog encoding, or UTF‑8 as a fallback.
            var encoding = DetectXmlEncoding(data);
            return encoding.GetString(data);
        }

        /// <summary>
        /// Returns true if the provided byte span begins with the two-byte
        /// gzip magic number (0x1F 0x8B).
        /// </summary>
        private static bool IsGzip(ReadOnlySpan<byte> bytes) =>
            bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B;

        /// <summary>
        /// Attempts to detect the encoding of an XML document.  The method
        /// checks for a UTF‑8 BOM, UTF‑16 BOMs, and examines the XML
        /// declaration for an explicit encoding attribute.  If no encoding
        /// is found, UTF‑8 without a BOM is returned.
        /// </summary>
        private static Encoding DetectXmlEncoding(ReadOnlySpan<byte> bytes)
        {
            // Check for common BOMs (UTF‑8, UTF‑16 LE/BE).
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode; // UTF‑16 LE
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode; // UTF‑16 BE

            // Probe the first kilobyte for an XML declaration specifying encoding.
            var probeLength = Math.Min(bytes.Length, 1024);
            var snippet = Encoding.ASCII.GetString(bytes.Slice(0, probeLength).ToArray());
            // Use a standard string (not verbatim) so that backslashes and quotes are properly escaped. When
            // constructing the regex pattern below, the backslash (\) characters are doubled so that the
            // compiled pattern includes a single backslash for the regex engine. Likewise, the double
            // quotes in the character class are escaped with \" so that the C# string parser does not
            // terminate the string prematurely. The resulting pattern matches an XML encoding attribute
            // like encoding="UTF-8" or encoding='ISO-8859-1', capturing the value in a named group "enc".
            var match = Regex.Match(snippet, "encoding\\s*=\\s*['\\\"](?<enc>[^'\\\"]+)['\\\"]", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var encName = match.Groups["enc"].Value;
                try
                {
                    return Encoding.GetEncoding(encName);
                }
                catch
                {
                    // Fall through to UTF‑8 if unknown encoding.
                }
            }

            // Default to UTF‑8 if no BOM or encoding declaration is present.
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
    }
}