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

        /// <summary>
        /// Optional mapping of playlist channel names to XMLTV channel identifiers.  When provided,
        /// these aliases override automatic name matching and fuzzy matching in the EPG mapper.
        /// The key should be the normalised channel name (as displayed in the UI) and the value
        /// should be the XMLTV channel ID from the EPG feed.  Entries are compared in a
        /// case-insensitive manner.  If an alias is specified for a channel, the EPG will
        /// always use the mapped ID regardless of other matches.
        /// </summary>
        [JsonPropertyName("epgIdAliases")]
        public Dictionary<string, string>? EpgIdAliases { get; set; }

        /// <summary>
        /// Optional global EPG time shift in minutes.  Some providers supply
        /// XMLTV data in a different timezone than the viewer.  Specify a
        /// positive or negative number of minutes here to adjust all
        /// programme start and end times accordingly.  Defaults to zero,
        /// meaning no shift is applied.
        /// </summary>
        [JsonPropertyName("epgShiftMinutes")]
        public int EpgShiftMinutes { get; set; } = 0;
    }
}