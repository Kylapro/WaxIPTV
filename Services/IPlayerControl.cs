using System;
using System.Threading;
using System.Threading.Tasks;

namespace WaxIPTV.Services
{
    /// <summary>
    /// Defines the basic control operations for an external media player.  Implementations
    /// should launch the player with a given URL, allow pausing and stopping, and support
    /// proper disposal of unmanaged resources.
    /// </summary>
    public interface IPlayerControl : IAsyncDisposable
    {
        /// <summary>
        /// Launches the player with the given stream URL and optional window title.
        /// </summary>
        Task StartAsync(string url, string? title = null, CancellationToken ct = default);

        /// <summary>
        /// Pauses or resumes playback.
        /// </summary>
        Task PauseAsync(bool pause, CancellationToken ct = default);

        /// <summary>
        /// Requests the player to quit gracefully.
        /// </summary>
        Task QuitAsync(CancellationToken ct = default);
    }
}