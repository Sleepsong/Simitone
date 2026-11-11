using System;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// Co-op is experimental and has no menus yet. It is switched on with environment variables:
    ///   SIMITONE_COOP_HOST=1            host: let others join whichever house is being played (default port 37565)
    ///   SIMITONE_COOP_HOST=40000        host on this port instead
    ///   SIMITONE_COOP_JOIN=192.168.1.5  guest: join this host instead of showing the neighbourhood
    ///   SIMITONE_COOP_JOIN=host:40000   guest: join on this port
    /// Nothing changes unless one of them is set.
    /// </summary>
    public static class CoopOptions
    {
        /// <summary>The port to host on, or 0 when not hosting.</summary>
        public static readonly int HostPort;

        /// <summary>Guests may change the game speed and pause for everyone.</summary>
        public static bool GuestsControlSpeed = true;

        private static string JoinAddress;

        static CoopOptions()
        {
            var host = Environment.GetEnvironmentVariable("SIMITONE_COOP_HOST");
            if (!string.IsNullOrWhiteSpace(host))
            {
                if (!int.TryParse(host, out HostPort) || HostPort < 1024 || HostPort > 65535) HostPort = CoopProtocol.DefaultPort;
            }
            JoinAddress = Environment.GetEnvironmentVariable("SIMITONE_COOP_JOIN");
            if (string.IsNullOrWhiteSpace(JoinAddress)) JoinAddress = null;
        }

        /// <summary>
        /// The host to join instead of showing the neighbourhood, once per run.
        /// </summary>
        public static bool TakeJoin(out string address)
        {
            address = JoinAddress;
            JoinAddress = null;
            return address != null;
        }
    }
}
