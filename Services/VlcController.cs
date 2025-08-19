using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using WaxIPTV.Services.Logging;

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
        public async Task StartAsync(string url, string? title = null, Dictionary<string, string>? headers = null, CancellationToken ct = default)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _vlcPath,
                Arguments = Quote(url) + " --intf dummy --extraintf rc --rc-quiet " +
                            "--rc-host 127.0.0.1:" + _port + " --no-video-title-show",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (headers != null)
            {
                foreach (var kv in headers)
                {
                    var opt = kv.Key.ToLowerInvariant() switch
                    {
                        "user-agent" => "--http-user-agent",
                        "referer" => "--http-referrer",
                        "cookie" => "--http-cookie",
                        _ => null
                    };
                    if (opt != null)
                        psi.Arguments += " " + opt + "=" + Quote(kv.Value);
                }
            }
            AppLog.Logger.Information("Starting VLC process {Path}", AppLog.Safe(_vlcPath));
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
                    AppLog.Logger.Information("Connected to VLC RC on port {Port}", _port);
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
            AppLog.Logger.Information("VLC command {Cmd}", cmd);
            return SendAsync(cmd);
        }

        /// <summary>
        /// Stops playback.  This command instructs VLC to halt the current
        /// stream but keeps the process alive.  To fully exit the VLC
        /// process, call <see cref="QuitAsync"/>.
        /// </summary>
        public Task StopAsync()
        {
            AppLog.Logger.Information("VLC stop");
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
            AppLog.Logger.Information("VLC quit");
            return SendAsync("quit");
        }

        /// <summary>
        /// Sets the volume percentage (0-100).
        /// </summary>
        public Task SetVolumeAsync(int pct)
        {
            AppLog.Logger.Information("VLC volume {Pct}", pct);
            return SendAsync($"volume {pct}");
        }

        private async Task SendAsync(string cmd)
        {
            if (_out == null) return;
            await _out.WriteLineAsync(cmd);
            AppLog.Logger.Debug("VLC cmd {Cmd}", AppLog.Safe(cmd));
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