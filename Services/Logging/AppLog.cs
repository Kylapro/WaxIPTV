using System;
using System.Diagnostics;
using Serilog;
using Serilog.Events;
using Serilog.Core;
using WaxIPTV.Models;

namespace WaxIPTV.Services.Logging
{
    /// <summary>
    /// Central logging helper built on Serilog.  Configured on application start
    /// and used throughout the app via the <see cref="Logger"/> property.
    /// Provides helpers for simple operation scopes and timed blocks.
    /// </summary>
    public static class AppLog
    {
        /// <summary>Serilog logger instance.</summary>
        public static Logger Logger { get; private set; } = new LoggerConfiguration().CreateLogger();
        public static bool IncludeSensitive { get; private set; }

        /// <summary>
        /// Initialises the logger based on application settings.
        /// </summary>
        public static void Init(AppSettings settings)
        {
            var level = LogEventLevel.Information;
            try
            {
                level = Enum.Parse<LogEventLevel>(settings.LogLevel, true);
            }
            catch { }

            var logDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WaxIPTV", "logs");
            System.IO.Directory.CreateDirectory(logDir);
            var filePath = System.IO.Path.Combine(logDir, "log-.txt");

            IncludeSensitive = settings.LogIncludeSensitive;

            Logger = new LoggerConfiguration()
                .MinimumLevel.Is(level)
                .WriteTo.File(
                    filePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: settings.LogRetainedDays,
                    fileSizeLimitBytes: settings.LogMaxFileBytes)
                .CreateLogger();
            Log.Logger = Logger;
        }

        /// <summary>
        /// Begins a logical operation scope by pushing the name onto the logging context.
        /// </summary>
        public static IDisposable BeginScope(string name)
        {
            return Serilog.Context.LogContext.PushProperty("Scope", name);
        }

        /// <summary>
        /// Returns the value if sensitive logging is enabled; otherwise returns "[redacted]".
        /// </summary>
        public static object Safe(object? value) => IncludeSensitive ? value ?? string.Empty : "[redacted]";

        private sealed class TimingScope : IDisposable
        {
            private readonly string _name;
            private readonly Stopwatch _sw;
            public TimingScope(string name)
            {
                _name = name;
                _sw = Stopwatch.StartNew();
                Logger.Information("Start {Operation}", _name);
            }
            public void Dispose()
            {
                _sw.Stop();
                Logger.Information("End {Operation} in {Elapsed}ms", _name, _sw.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Measures the duration of an operation and logs the elapsed time on disposal.
        /// </summary>
        public static IDisposable TimeOperation(string name) => new TimingScope(name);
    }
}
