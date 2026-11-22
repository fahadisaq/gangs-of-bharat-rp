using System;
using System.Collections.Generic;
using System.Globalization;

namespace GangsOfBharat.Server
{
    /// <summary>
    /// Server settings. Sources, lowest to highest priority: defaults, environment variables
    /// (GOB_PORT, GOB_MAX_PLAYERS, ...), command-line arguments (--port 7777, ...).
    /// </summary>
    public sealed class ServerConfig
    {
        public string BindAddress = "0.0.0.0";
        public int Port = 7777;
        public int MaxPlayers = 1000;
        public int TickRate = 20;
        public double ClientTimeoutSeconds = 10.0;

        public float SpawnX = 4096f;
        public float SpawnZ = 4096f;
        /// <summary>New players appear at a random point within this radius of the spawn point.</summary>
        public float SpawnRadius = 10f;
        public int ParkedVehicles = 200;
        public int WorldSeed = 1234;

        public int MaxEntitiesPerSnapshot = 100;
        public float InterestRadius = 300f;
        public int SnapshotThreads = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));

        public float LocalChatRadius = 30f;
        public float ProximityVoiceRadius = 40f;
        public double GlobalSlowModeSeconds = 10.0;
        /// <summary>Chat flood limit: this many messages per window per player.</summary>
        public int ChatBurst = 5;
        public double ChatWindowSeconds = 5.0;
        /// <summary>Opus 20 ms frames = 50 per second; allow a little jitter.</summary>
        public int VoiceFramesPerSecond = 60;
        /// <summary>Ad chat price per message in rupees (balancing placeholder).</summary>
        public long AdCostRupees = 500;
        /// <summary>SMS price per message from phone balance (balancing placeholder).</summary>
        public long SmsCostRupees = 2;
        /// <summary>Story start: the player arrives by train with 500 rupees.</summary>
        public long StartingWalletRupees = 500;
        public long StartingPhoneBalanceRupees = 50;

        /// <summary>
        /// Accept "dev:" session tokens that set faction, crew, VIP and admin flags. For local
        /// testing and bots only; never enable on a public server.
        /// </summary>
        public bool AllowDevTokens;
        public bool LogChatToConsole = true;
        /// <summary>Seconds between stats log lines (0 = off).</summary>
        public int StatsIntervalSeconds = 10;

        public double TickIntervalMs
        {
            get { return 1000.0 / TickRate; }
        }

        public static ServerConfig Load(string[] args, Func<string, string> getEnv)
        {
            ServerConfig c = new ServerConfig();
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in KnownKeys)
            {
                string env = getEnv("GOB_" + key.Replace('-', '_').ToUpperInvariant());
                if (!string.IsNullOrEmpty(env))
                {
                    values[key] = env;
                }
            }
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (!a.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Unexpected argument: " + a);
                }
                string key = a.Substring(2);
                string value = "true";
                int eq = key.IndexOf('=');
                if (eq >= 0)
                {
                    value = key.Substring(eq + 1);
                    key = key.Substring(0, eq);
                }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = args[++i];
                }
                if (Array.IndexOf(KnownKeys, key.ToLowerInvariant()) < 0)
                {
                    throw new ArgumentException("Unknown option: --" + key);
                }
                values[key] = value;
            }
            foreach (KeyValuePair<string, string> kv in values)
            {
                c.Apply(kv.Key.ToLowerInvariant(), kv.Value);
            }
            c.Validate();
            return c;
        }

        public static readonly string[] KnownKeys = new[]
        {
            "bind", "port", "max-players", "tick-rate", "timeout", "spawn-x", "spawn-z", "spawn-radius",
            "vehicles", "seed", "max-entities", "interest-radius", "snapshot-threads", "local-chat-radius",
            "voice-radius", "slow-mode", "ad-cost", "sms-cost", "allow-dev-tokens", "log-chat", "stats-interval",
        };

        private void Apply(string key, string value)
        {
            switch (key)
            {
                case "bind": BindAddress = value; break;
                case "port": Port = ParseInt(key, value); break;
                case "max-players": MaxPlayers = ParseInt(key, value); break;
                case "tick-rate": TickRate = ParseInt(key, value); break;
                case "timeout": ClientTimeoutSeconds = ParseDouble(key, value); break;
                case "spawn-x": SpawnX = (float)ParseDouble(key, value); break;
                case "spawn-z": SpawnZ = (float)ParseDouble(key, value); break;
                case "spawn-radius": SpawnRadius = (float)ParseDouble(key, value); break;
                case "vehicles": ParkedVehicles = ParseInt(key, value); break;
                case "seed": WorldSeed = ParseInt(key, value); break;
                case "max-entities": MaxEntitiesPerSnapshot = ParseInt(key, value); break;
                case "interest-radius": InterestRadius = (float)ParseDouble(key, value); break;
                case "snapshot-threads": SnapshotThreads = ParseInt(key, value); break;
                case "local-chat-radius": LocalChatRadius = (float)ParseDouble(key, value); break;
                case "voice-radius": ProximityVoiceRadius = (float)ParseDouble(key, value); break;
                case "slow-mode": GlobalSlowModeSeconds = ParseDouble(key, value); break;
                case "ad-cost": AdCostRupees = ParseLong(key, value); break;
                case "sms-cost": SmsCostRupees = ParseLong(key, value); break;
                case "allow-dev-tokens": AllowDevTokens = ParseBool(key, value); break;
                case "log-chat": LogChatToConsole = ParseBool(key, value); break;
                case "stats-interval": StatsIntervalSeconds = ParseInt(key, value); break;
            }
        }

        public void Validate()
        {
            if (Port < 0 || Port > 65535)
            {
                throw new ArgumentException("port must be 0-65535");
            }
            if (MaxPlayers < 1 || MaxPlayers > 4000)
            {
                throw new ArgumentException("max-players must be 1-4000");
            }
            if (TickRate < 1 || TickRate > 60)
            {
                throw new ArgumentException("tick-rate must be 1-60");
            }
            if (MaxEntitiesPerSnapshot < 0 || MaxEntitiesPerSnapshot > 1000)
            {
                throw new ArgumentException("max-entities must be 0-1000");
            }
            if (SnapshotThreads < 1 || SnapshotThreads > 64)
            {
                throw new ArgumentException("snapshot-threads must be 1-64");
            }
            if (ParkedVehicles < 0 || ParkedVehicles > 5000)
            {
                throw new ArgumentException("vehicles must be 0-5000");
            }
            if (AdCostRupees < 0 || SmsCostRupees < 0)
            {
                throw new ArgumentException("costs must be >= 0");
            }
        }

        private static int ParseInt(string key, string value)
        {
            int v;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
            {
                throw new ArgumentException("--" + key + " expects an integer");
            }
            return v;
        }

        private static long ParseLong(string key, string value)
        {
            long v;
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
            {
                throw new ArgumentException("--" + key + " expects an integer");
            }
            return v;
        }

        private static double ParseDouble(string key, string value)
        {
            double v;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                throw new ArgumentException("--" + key + " expects a number");
            }
            return v;
        }

        private static bool ParseBool(string key, string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    return false;
                default:
                    throw new ArgumentException("--" + key + " expects true/false");
            }
        }
    }
}
