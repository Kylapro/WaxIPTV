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
        /// Optional mapping of application channel identifiers to additional
        /// XMLTV channel IDs that should be considered equivalent when
        /// loading EPG data. Each entry maps a playlist channel's internal ID
        /// to one or more XMLTV ids that should also be accepted when
        /// associating programmes with that channel.
        /// </summary>
        [JsonPropertyName("epgIdAliases")]
        public Dictionary<string, string[]>? EpgIdAliases { get; set; }

        /// <summary>
        /// Determines how programmes from the XMLTV feed are matched to
        /// playlist channels. The default <see cref="EpgMatchMode.StrictIdsOnly"/>
        /// maps programmes solely by tvg-id and aliases. The
        /// <see cref="EpgMatchMode.IdsThenExactName"/> mode falls back to an
        /// exact display-name match only if a channel receives no programmes.
        /// </summary>
        [JsonPropertyName("epgMatchMode")]
        public EpgMatchMode EpgMatchMode { get; set; } = EpgMatchMode.StrictIdsOnly;

        /// <summary>
        /// Optional global EPG time shift in minutes.  Some providers supply
        /// XMLTV data in a different timezone than the viewer.  Specify a
        /// positive or negative number of minutes here to adjust all
        /// programme start and end times accordingly.  Defaults to zero,
        /// meaning no shift is applied.
        /// </summary>
        [JsonPropertyName("epgShiftMinutes")]
        public int EpgShiftMinutes { get; set; } = 0;

        /// <summary>
        /// Minimum log level to record. Valid values correspond to Serilog levels
        /// such as "Information", "Debug" or "Verbose".
        /// </summary>
        [JsonPropertyName("logLevel")]
        public string LogLevel { get; set; } = "Information";

        /// <summary>
        /// When true, potentially sensitive data such as URLs and file paths will be
        /// included in log entries. Defaults to false to avoid leaking information.
        /// </summary>
        [JsonPropertyName("logIncludeSensitive")]
        public bool LogIncludeSensitive { get; set; } = false;

        /// <summary>
        /// Number of days to retain rolling log files before deletion.
        /// </summary>
        [JsonPropertyName("logRetainedDays")]
        public int LogRetainedDays { get; set; } = 7;

        /// <summary>
        /// Maximum size in bytes for an individual log file before it rolls.
        /// </summary>
        [JsonPropertyName("logMaxFileBytes")]
        public long LogMaxFileBytes { get; set; } = 5 * 1024 * 1024; // 5 MB
    }
}