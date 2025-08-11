using System.Text.Json.Serialization;

namespace WaxIPTV.Models
{
    /// <summary>
    /// Strongly typed representation of configuration settings loaded from a JSON file.
    /// Each property corresponds to a field in <c>settings.json</c>.
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Which external player to use. Valid values are "mpv" or "vlc".
        /// Defaults to "mpv" if unspecified.
        /// </summary>
        [JsonPropertyName("player")]
        public string Player { get; set; } = "mpv";

        /// <summary>
        /// Fully qualified path to the <c>mpv.exe</c> executable.
        /// When null or empty, the application will attempt to auto-detect mpv on first run.
        /// </summary>
        [JsonPropertyName("mpvPath")]
        public string? MpvPath { get; set; }

        /// <summary>
        /// Fully qualified path to the <c>vlc.exe</c> executable.
        /// When null or empty, the application will attempt to auto-detect VLC on first run.
        /// </summary>
        [JsonPropertyName("vlcPath")]
        public string? VlcPath { get; set; }

        /// <summary>
        /// URL pointing at the M3U playlist that contains available channels.
        /// The playlist will be downloaded and parsed on startup.
        /// </summary>
        [JsonPropertyName("playlistUrl")]
        public string? PlaylistUrl { get; set; }

        /// <summary>
        /// URL to an XMLTV EPG feed.  EPG data will be refreshed on a schedule.
        /// </summary>
        [JsonPropertyName("xmltvUrl")]
        public string? XmltvUrl { get; set; }

        /// <summary>
        /// Number of hours between automatic EPG refresh operations.
        /// Defaults to 12 hours.
        /// </summary>
        [JsonPropertyName("epgRefreshHours")]
        public int EpgRefreshHours { get; set; } = 12;
    }
}