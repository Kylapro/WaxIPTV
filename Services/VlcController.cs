using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WaxIPTV.Services
{
    /// <summary>
    /// Controls a VLC instance via its remote control (RC) interface.  VLC is
    /// launched with the RC interface bound to a randomly chosen TCP port, and
    /// commands are sent as plain text lines over the socket.  This provides a
    /// simple fallback when mpv is unavailable, though mpv is preferred for its
    /// richer JSON IPC.
    /// </summary>
    public sealed class VlcController : IPlayerControl
    {
        private readonly string _vlcPath;
        private readonly int _port = Random.Shared.Next(42100, 42999);
        private Process? _proc;
        private TcpClient? _client;
        private StreamWriter? _out;

        /// <summary>
        /// Constructs a new VLC controller for the given executable path.
        /// </summary>
        public VlcController(string vlcPath)
        {
            _vlcPath = vlcPath;
        }

        /// <inheritdoc />
        public async Task StartAsync(string url, string? title = null, CancellationToken ct = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _vlcPath,
                Arguments = Quote(url) + " --intf dummy --extraintf rc --rc-quiet " +
                            "--rc-host 127.0.0.1:" + _port + " --no-video-title-show",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            _proc = Process.Start(psi) ?? throw new Exception("Failed to start VLC");
            // Attempt to connect to the RC socket.  VLC may take a moment to open it.
            for (int i = 0; i < 30; i++)
            {
                try
                {
                    _client = new TcpClient();
                    await _client.ConnectAsync(IPAddress.Loopback, _port, ct);
                    var stream = _client.GetStream();
                    _out = new StreamWriter(stream) { AutoFlush = true };
                    break;
                }
                catch
                {
                    await Task.Delay(200, ct);
                }
            }
        }

        /// <inheritdoc />
        public Task PauseAsync(bool pause, CancellationToken ct = default)
        {
            var cmd = pause ? "pause" : "play";
            return SendAsync(cmd);
        }

        /// <summary>
        /// Stops playback.  This command instructs VLC to halt the current
        /// stream but keeps the process alive.  To fully exit the VLC
        /// process, call <see cref="QuitAsync"/>.
        /// </summary>
        public Task StopAsync()
        {
            return SendAsync("stop");
        }

        /// <summary>
        /// Quits the VLC process gracefully.  This method satisfies the
        /// <see cref="IPlayerControl"/> contract by sending the "quit" command
        /// via the RC interface.  Note that VLC terminates immediately upon
        /// receiving this command, so subsequent commands will fail until
        /// a new instance is started.
        /// </summary>
        public Task QuitAsync(CancellationToken ct = default)
        {
            return SendAsync("quit");
        }

        /// <summary>
        /// Sets the volume percentage (0-100).
        /// </summary>
        public Task SetVolumeAsync(int pct)
        {
            return SendAsync($"volume {pct}");
        }

        private async Task SendAsync(string cmd)
        {
            if (_out == null) return;
            await _out.WriteLineAsync(cmd);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_out != null)
                {
                    await _out.WriteLineAsync("quit");
                }
            }
            catch
            {
                // ignore exceptions on quit
            }
            _out?.Dispose();
            _client?.Dispose();
            if (_proc is { HasExited: false })
            {
                _proc.Kill(true);
            }
            _proc?.Dispose();
        }

        private static string Quote(string s)
        {
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }
    }
}