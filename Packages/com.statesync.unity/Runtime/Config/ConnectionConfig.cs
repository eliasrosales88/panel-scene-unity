using System;

namespace StateSync.Config
{
    /// <summary>
    /// Resolves connection role and port. Sources, in priority order:
    /// 1. Test/Editor overrides set via <see cref="OverrideIsHost"/> and <see cref="OverridePort"/>.
    /// 2. Command-line args (<c>--ws-mode=host|follower</c>, <c>--ws-port=N</c>).
    /// 3. Defaults: host on port 5050.
    /// </summary>
    public static class ConnectionConfig
    {
        const string ModeArgPrefix = "--ws-mode=";
        const string PortArgPrefix = "--ws-port=";
        public const int DefaultPort = 5050;
        public const bool DefaultIsHost = true;

        static readonly object _lock = new object();
        static bool _parsed;
        static bool? _cliIsHost;
        static int? _cliPort;

        public static bool? OverrideIsHost { get; set; }
        public static int? OverridePort { get; set; }

        public static bool IsHost
        {
            get
            {
                if (OverrideIsHost.HasValue) return OverrideIsHost.Value;
                EnsureParsed();
                return _cliIsHost ?? DefaultIsHost;
            }
        }

        public static int WsPort
        {
            get
            {
                if (OverridePort.HasValue) return OverridePort.Value;
                EnsureParsed();
                return _cliPort ?? DefaultPort;
            }
        }

        /// <summary>
        /// Parses the provided argument array as if it were the process CLI.
        /// Useful for tests; production callers do not need to call this.
        /// </summary>
        public static void ParseFrom(string[] args)
        {
            lock (_lock)
            {
                ParseInternal(args);
                _parsed = true;
            }
        }

        /// <summary>
        /// Clears parsed and override state. Test-only.
        /// </summary>
        public static void ResetForTests()
        {
            lock (_lock)
            {
                _cliIsHost = null;
                _cliPort = null;
                _parsed = false;
                OverrideIsHost = null;
                OverridePort = null;
            }
        }

        static void EnsureParsed()
        {
            if (_parsed) return;
            lock (_lock)
            {
                if (_parsed) return;
                ParseInternal(Environment.GetCommandLineArgs());
                _parsed = true;
            }
        }

        static void ParseInternal(string[] args)
        {
            _cliIsHost = null;
            _cliPort = null;
            if (args == null) return;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == null) continue;

                if (a.StartsWith(ModeArgPrefix, StringComparison.Ordinal))
                {
                    string v = a.Substring(ModeArgPrefix.Length);
                    if (string.Equals(v, "host", StringComparison.OrdinalIgnoreCase)) _cliIsHost = true;
                    else if (string.Equals(v, "follower", StringComparison.OrdinalIgnoreCase)) _cliIsHost = false;
                }
                else if (a.StartsWith(PortArgPrefix, StringComparison.Ordinal))
                {
                    string v = a.Substring(PortArgPrefix.Length);
                    if (int.TryParse(v, out int port) && port > 0 && port <= 65535)
                        _cliPort = port;
                }
            }
        }
    }
}
