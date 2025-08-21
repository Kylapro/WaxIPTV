namespace WaxIPTV.Models
{
    /// <summary>
    /// Controls how EPG programmes are matched to playlist channels.
    /// </summary>
    public enum EpgMatchMode
    {
        /// <summary>
        /// Only programmes whose channel IDs explicitly match the channel's
        /// tvg-id or configured aliases are mapped.
        /// </summary>
        StrictIdsOnly,
        /// <summary>
        /// First attempt strict ID mapping. If a channel receives no
        /// programmes, fall back to matching by exact display name.
        /// </summary>
        IdsThenExactName
    }
}
