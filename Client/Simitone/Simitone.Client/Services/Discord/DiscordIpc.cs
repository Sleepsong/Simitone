using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Simitone.Client.Services.Discord
{
    /// <summary>Message types on Discord's local IPC connection.</summary>
    public enum DiscordOp
    {
        Handshake = 0,
        Frame = 1,
        Close = 2,
        Ping = 3,
        Pong = 4
    }

    /// <summary>
    /// Discord's IPC framing: a little-endian opcode and payload length (both 32-bit), then the payload, UTF-8 JSON.
    /// </summary>
    public static class DiscordFrame
    {
        /// <summary>Largest payload accepted from Discord; anything bigger means the stream is out of step.</summary>
        public const int MaxPayload = 64 * 1024;

        public static byte[] Encode(DiscordOp op, string json)
        {
            var payload = Encoding.UTF8.GetBytes(json);
            var frame = new byte[8 + payload.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), (int)op);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), payload.Length);
            payload.CopyTo(frame, 8);
            return frame;
        }

        public static async Task WriteAsync(Stream stream, DiscordOp op, string json, CancellationToken ct)
        {
            var frame = Encode(op, json);
            await stream.WriteAsync(frame, 0, frame.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Reads one frame. Throws EndOfStreamException if Discord closed the connection.</summary>
        public static async Task<(DiscordOp op, string json)> ReadAsync(Stream stream, CancellationToken ct)
        {
            var header = new byte[8];
            await ReadExactly(stream, header, ct).ConfigureAwait(false);
            var op = (DiscordOp)BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
            var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
            if (length < 0 || length > MaxPayload) throw new InvalidDataException("Discord frame of " + length + " bytes");
            var payload = new byte[length];
            await ReadExactly(stream, payload, ct).ConfigureAwait(false);
            return (op, Encoding.UTF8.GetString(payload));
        }

        private static async Task ReadExactly(Stream stream, byte[] buffer, CancellationToken ct)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer, read, buffer.Length - read, ct).ConfigureAwait(false);
                if (n == 0) throw new EndOfStreamException("Discord closed the connection");
                read += n;
            }
        }
    }

    /// <summary>Where the Discord app listens on this machine.</summary>
    public static class DiscordPipes
    {
        //the Discord app as a Flatpak or Snap, and Vesktop, put their socket in their own folder under XDG_RUNTIME_DIR
        private static readonly string[] Subfolders =
        {
            "", "app/com.discordapp.Discord", "app/com.discordapp.DiscordCanary", "app/dev.vencord.Vesktop",
            "snap.discord", "snap.discord-canary"
        };

        /// <summary>
        /// Unix socket paths to try, in order: discord-ipc-0..9 in XDG_RUNTIME_DIR (and the sandboxed clients'
        /// folders in it), then TMPDIR, TMP, TEMP and /tmp.
        /// </summary>
        public static List<string> UnixCandidates(Func<string, string> env)
        {
            var roots = new List<string>();
            foreach (var name in new[] { "XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP" })
            {
                var value = env(name);
                if (!string.IsNullOrEmpty(value)) roots.Add(value);
            }
            roots.Add("/tmp");
            var paths = new List<string>();
            foreach (var root in roots.Distinct())
            {
                foreach (var sub in Subfolders)
                {
                    var folder = (sub.Length == 0) ? root : Path.Combine(root, sub);
                    for (int i = 0; i < 10; i++) paths.Add(Path.Combine(folder, "discord-ipc-" + i));
                }
            }
            return paths.Distinct().ToList();
        }

        /// <summary>Connects to the first Discord that answers, or returns null if none is running.</summary>
        public static async Task<Stream> ConnectAsync(CancellationToken ct)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                for (int i = 0; i < 10; i++)
                {
                    var pipe = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                    try
                    {
                        await pipe.ConnectAsync(200, ct).ConfigureAwait(false);
                        return pipe;
                    }
                    catch (Exception e) when (!(e is OperationCanceledException))
                    {
                        pipe.Dispose();
                    }
                }
                return null;
            }
            foreach (var path in UnixCandidates(Environment.GetEnvironmentVariable))
            {
                if (!File.Exists(path)) continue; //sockets show up as files
                var stream = await ConnectUnixAsync(path, ct).ConfigureAwait(false);
                if (stream != null) return stream;
            }
            return null;
        }

        public static async Task<Stream> ConnectUnixAsync(string path, CancellationToken ct)
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                socket.Dispose();
                return null;
            }
        }
    }

    /// <summary>What to show on the profile. Null fields are left out.</summary>
    public class PresenceActivity : IEquatable<PresenceActivity>
    {
        public string Details;
        public string State;
        /// <summary>Unix seconds the "elapsed" timer counts from.</summary>
        public long? Start;
        public string LargeImage;
        public string LargeText;

        public JsonObject ToJson()
        {
            var activity = new JsonObject();
            if (!string.IsNullOrEmpty(Details)) activity["details"] = Fit(Details);
            if (!string.IsNullOrEmpty(State)) activity["state"] = Fit(State);
            if (Start != null) activity["timestamps"] = new JsonObject { ["start"] = Start.Value };
            if (!string.IsNullOrEmpty(LargeImage))
            {
                var assets = new JsonObject { ["large_image"] = LargeImage };
                if (!string.IsNullOrEmpty(LargeText)) assets["large_text"] = Fit(LargeText);
                activity["assets"] = assets;
            }
            return activity;
        }

        /// <summary>Discord takes 2 to 128 characters in these fields.</summary>
        private static string Fit(string text)
        {
            text = text.Trim();
            if (text.Length > 128) text = text.Substring(0, 127) + "…";
            return (text.Length < 2) ? text.PadRight(2) : text;
        }

        public bool Equals(PresenceActivity other) => other != null && Details == other.Details && State == other.State
            && Start == other.Start && LargeImage == other.LargeImage && LargeText == other.LargeText;
        public override bool Equals(object obj) => Equals(obj as PresenceActivity);
        public override int GetHashCode() => HashCode.Combine(Details, State, Start, LargeImage, LargeText);
    }

    /// <summary>
    /// One connection to the Discord app: the handshake, then SET_ACTIVITY commands. Discord answers each command,
    /// and may ping; <see cref="ReadAsync"/> takes those, and the caller should keep reading so they don't back up.
    /// </summary>
    public sealed class DiscordRpcClient : IDisposable
    {
        private readonly string ClientId;
        private readonly Func<CancellationToken, Task<Stream>> Connect;
        private Stream Stream;

        public bool Connected => Stream != null;

        /// <param name="connect">How to reach Discord; by default <see cref="DiscordPipes.ConnectAsync"/>.</param>
        public DiscordRpcClient(string clientId, Func<CancellationToken, Task<Stream>> connect = null)
        {
            ClientId = clientId;
            Connect = connect ?? DiscordPipes.ConnectAsync;
        }

        /// <summary>Connects and shakes hands. False if Discord isn't running or refused (e.g. an unknown application ID).</summary>
        public async Task<bool> ConnectAsync(CancellationToken ct)
        {
            Close();
            var stream = await Connect(ct).ConfigureAwait(false);
            if (stream == null) return false;
            try
            {
                var hello = new JsonObject { ["v"] = 1, ["client_id"] = ClientId };
                await DiscordFrame.WriteAsync(stream, DiscordOp.Handshake, hello.ToJsonString(), ct).ConfigureAwait(false);
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    var (op, json) = await DiscordFrame.ReadAsync(stream, timeout.Token).ConfigureAwait(false);
                    if (op != DiscordOp.Frame || ReadString(json, "evt") != "READY")
                    {
                        LastError = (op == DiscordOp.Close) ? "Discord refused: " + ReadString(json, "message") : "unexpected reply";
                        stream.Dispose();
                        return false;
                    }
                }
                Stream = stream;
                return true;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                LastError = e.Message;
                stream.Dispose();
                return false;
            }
        }

        /// <summary>Why the last connection failed, for the log.</summary>
        public string LastError { get; private set; }

        /// <summary>Shows <paramref name="activity"/>, or clears the presence if it's null.</summary>
        public Task SetActivityAsync(PresenceActivity activity, CancellationToken ct)
        {
            var args = new JsonObject
            {
                ["pid"] = Environment.ProcessId,
                ["activity"] = activity?.ToJson() //null clears it
            };
            var command = new JsonObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = args,
                ["nonce"] = Guid.NewGuid().ToString()
            };
            return Send(DiscordOp.Frame, command.ToJsonString(), ct);
        }

        public Task<(DiscordOp op, string json)> ReadAsync(CancellationToken ct)
        {
            if (Stream == null) throw new InvalidOperationException("not connected");
            return DiscordFrame.ReadAsync(Stream, ct);
        }

        public Task Send(DiscordOp op, string json, CancellationToken ct)
        {
            if (Stream == null) throw new InvalidOperationException("not connected");
            return DiscordFrame.WriteAsync(Stream, op, json, ct);
        }

        public void Close()
        {
            Stream?.Dispose();
            Stream = null;
        }

        public void Dispose() => Close();

        /// <summary>A top-level string property of a JSON object, or null.</summary>
        public static string ReadString(string json, string name)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    return (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var value)
                        && value.ValueKind == JsonValueKind.String) ? value.GetString() : null;
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
