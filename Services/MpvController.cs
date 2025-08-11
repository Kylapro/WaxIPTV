using System;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace WaxIPTV.Services
{
    /// <summary>
    /// Launches and controls an external mpv player using its JSON IPC interface.  The player
    /// is started with a named pipe for IPC.  Commands such as pause, volume and quit
    /// are sent over the pipe encoded as JSON arrays.
    /// </summary>
    public sealed class MpvController : IPlayerControl
    {
        private readonly string _mpvPath;
        private readonly string _pipeName = $"mpv-ipc-{Guid.NewGuid():N}";
        private Process? _proc;
        private NamedPipeClientStream? _pipe;

        /// <summary>
        /// Constructs a new controller for the given mpv executable path.
        /// </summary>
        public MpvController(string mpvPath)
        {
            _mpvPath = mpvPath;
        }

        /// <summary>
        /// Starts mpv with the specified stream URL and optional window title.  This will
        /// wait for the IPC pipe to become available and set an initial volume of 100%.
        /// </summary>
        public async Task StartAsync(string url, string? title = null, CancellationToken ct = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _mpvPath,
                Arguments = Quote(url) + " --no-terminal --force-window=yes " +
                            "--title=" + Quote(title ?? "IPTV") + " " +
                            "--input-ipc-server=\\\\.\\pipe\\" + _pipeName,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            _proc = Process.Start(psi) ?? throw new Exception("Failed to start mpv");
            // Wait for pipe to appear and connect
            _pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            var sw = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    await _pipe.ConnectAsync(200, ct);
                    break;
                }
                catch
                {
                    if (sw.Elapsed >= TimeSpan.FromSeconds(10))
                        throw new TimeoutException("Timeout waiting for mpv IPC pipe");
                }
            }
            // Set initial volume to 100%
            await SendAsync(new { command = new object[] { "set_property", "volume", 100 } }, ct);
        }

        /// <summary>
        /// Pauses or resumes playback.
        /// </summary>
        public Task PauseAsync(bool pause, CancellationToken ct = default)
        {
            return SendAsync(new { command = new object[] { "set_property", "pause", pause } }, ct);
        }

        /// <summary>
        /// Sends a quit command to mpv.
        /// </summary>
        public Task QuitAsync(CancellationToken ct = default)
        {
            return SendAsync(new { command = new object[] { "quit" } }, ct);
        }

        /// <summary>
        /// Serialises and sends a JSON payload over the IPC pipe.
        /// </summary>
        private async Task SendAsync(object payload, CancellationToken ct)
        {
            if (_pipe is null || !_pipe.IsConnected)
                return;
            var json = System.Text.Json.JsonSerializer.Serialize(payload) + "\n";
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await _pipe.WriteAsync(bytes, 0, bytes.Length, ct);
            await _pipe.FlushAsync(ct);
        }

        /// <summary>
        /// Quotes a command-line argument by surrounding it in double quotes and escaping
        /// internal quotes.
        /// </summary>
        private static string Quote(string s)
        {
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            try
            {
                await QuitAsync();
            }
            catch { /* ignore */ }
            _pipe?.Dispose();
            if (_proc is { HasExited: false })
            {
                _proc.Kill(true);
            }
            _proc?.Dispose();
        }
    }
}