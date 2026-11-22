using System.IO;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// The host's welcome: the guest's player ID, plus what the guest checks before playing - the host's
    /// Simitone version (both must run the same game logic) and its object files (see <see cref="CoopContent"/>).
    /// </summary>
    public static class CoopCompat
    {
        private const int MaxManifestSize = 32 * 1024 * 1024;

        public static byte[] PackWelcome(uint playerID, string version, byte[] packedManifest)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(playerID);
                    writer.Write(version ?? "");
                    writer.Write(packedManifest.Length);
                    writer.Write(packedManifest);
                }
                return stream.ToArray();
            }
        }

        public static void ParseWelcome(byte[] data, out uint playerID, out string version, out CoopManifest manifest)
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                playerID = reader.ReadUInt32();
                version = reader.ReadString();
                var length = reader.ReadInt32();
                if (length < 0 || length > MaxManifestSize) throw new InvalidDataException("Bad manifest size " + length);
                var packed = reader.ReadBytes(length);
                if (packed.Length != length) throw new EndOfStreamException();
                manifest = CoopManifest.Unpack(packed);
            }
        }

        /// <summary>
        /// Returns why this game cannot play on that host, or null if the versions match.
        /// </summary>
        public static string VersionProblem(string hostVersion, string myVersion)
        {
            if (hostVersion == myVersion) return null;
            return "The host is running Simitone " + hostVersion + ", and this is Simitone " + myVersion +
                ". Both players need the same version.";
        }
    }
}
