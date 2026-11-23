using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace GangsOfBharat.Server
{
    public static class Program
    {
        private const string Usage =
            "GangsOfBharat.Server - authoritative UDP game server\n" +
            "\n" +
            "Options (or environment variables GOB_<OPTION>, e.g. GOB_PORT=7777):\n" +
            "  --port N              UDP port (default 7777)\n" +
            "  --bind ADDR           bind address (default 0.0.0.0)\n" +
            "  --max-players N       player cap (default 1000)\n" +
            "  --tick-rate N         simulation rate in Hz (default 20)\n" +
            "  --timeout S           client timeout in seconds (default 10)\n" +
            "  --vehicles N          parked vehicles (default 200)\n" +
            "  --max-entities N      entities per client snapshot (default 100)\n" +
            "  --interest-radius M   snapshot radius in metres (default 300)\n" +
            "  --snapshot-threads N  snapshot worker threads\n" +
            "  --spawn-x/--spawn-z/--spawn-radius   spawn area\n" +
            "  --local-chat-radius M (default 30)   --voice-radius M (default 40)\n" +
            "  --slow-mode S         global chat slow mode (default 10)\n" +
            "  --ad-cost R / --sms-cost R           chat prices in rupees\n" +
            "  --allow-dev-tokens    accept dev: tokens (local testing only)\n" +
            "  --log-chat true|false  --stats-interval S\n";

        public static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--help") >= 0 || Array.IndexOf(args, "-h") >= 0)
            {
                Console.WriteLine(Usage);
                return 0;
            }
            ServerConfig config;
            try
            {
                config = ServerConfig.Load(args, Environment.GetEnvironmentVariable);
            }
            catch (ArgumentException e)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(Usage);
                return 2;
            }

            ServerLog log = new ServerLog();
            using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
            using (GameServer server = new GameServer(config, log))
            {
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    stop.Set();
                };
                using (PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
                {
                    ctx.Cancel = true;
                    stop.Set();
                }))
                {
                    server.Start();
                    stop.Wait();
                    server.Stop();
                }
            }
            return 0;
        }
    }
}
