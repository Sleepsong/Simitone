using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Simitone.Client.Network.Coop
{
    /// <summary>
    /// A guest's side of a co-op session. Connects to a host, then feeds the host's ticks to the
    /// guest's driver and the guest's commands to the host. Polled on the game thread via <see cref="Update"/>.
    ///
    /// Before it is given the lot, the guest compares the host's object files with its own and fetches the
    /// ones it lacks or has a different copy of (see <see cref="CoopContent"/>). Fetched files are kept in
    /// <see cref="CacheDir"/>, so each is only ever sent once.
    /// </summary>
    public class CoopClient
    {
        /// <summary>The driver for the lot being played. A new one is made for each lot visit (<see cref="ResetDriver"/>).</summary>
        public CoopClientDriver Driver { get; private set; }

        /// <summary>This guest's player ID in the VM. 0 until the host has welcomed us.</summary>
        public uint PlayerID { get; private set; }

        /// <summary>The player IDs of everyone in the session (the host and all guests, including us).</summary>
        public HashSet<uint> Players { get; private set; } = new HashSet<uint>();

        /// <summary>The host accepted us and we have its objects; PlayerID is set and the lot state will follow.</summary>
        public event Action OnWelcome;
        /// <summary>
        /// Object files are being fetched from the host: bytes received so far, bytes in all, files still to come.
        /// Fired once with (0, total, n) before the first file, then after each one.
        /// </summary>
        public event Action<long, long, int> OnContentProgress;
        /// <summary>The session ended. The string says why, and is empty if we left ourselves.</summary>
        public event Action<string> OnClosed;
        /// <summary>A command the host sent to this guest alone (a serialized VMNetCommand).</summary>
        public event Action<byte[]> OnDirect;
        /// <summary>The host's neighbourhood as it is now (a packed CoopWorld), for the neighbourhood screen.</summary>
        public event Action<byte[]> OnLobby;
        /// <summary>Where the host is: the scene, the area shown, the house being viewed (0 none), the family being moved in (-1 none).</summary>
        public event Action<CoopScene, ushort, int, int> OnScene;
        /// <summary>Create-a-Family: the player who holds control, and the family as it stands (a packed CoopCasState).</summary>
        public event Action<uint, byte[]> OnCas;
        /// <summary>Something the host wants this player told.</summary>
        public event Action<string> OnNotice;
        /// <summary>A house file we asked for: its name, and the frame data with where in it the file starts and how long it is (0: keep ours).</summary>
        public event Action<string, byte[], int, int> OnHouseFile;

        private readonly ConcurrentQueue<CoopFrame> Incoming = new ConcurrentQueue<CoopFrame>();
        private volatile CoopConnection Connection;
        private bool Ended;

        /// <summary>This game's version, compared with the host's on joining.</summary>
        public string GameVersion = SimitoneVersion.Current;

        /// <summary>This game's object files, compared with the host's on joining. The welcome waits for it.</summary>
        public Task<CoopManifest> LocalContent = Task.FromResult(new CoopManifest());

        /// <summary>The folder to keep files fetched from hosts in. Without one, a guest that needs any cannot join.</summary>
        public string CacheDir;

        /// <summary>
        /// Called once all the host's files this game needs are on disk, with each file's manifest entry and
        /// where it is, to make the game use them. Throwing ends the session.
        /// </summary>
        public Action<List<KeyValuePair<CoopContentFile, string>>> ApplyContent;

        /// <summary>How many object files had to be fetched from the host (the rest were already in the cache).</summary>
        public int ContentFilesFetched { get; private set; }

        private byte[] PendingWelcome;   //held until LocalContent is ready
        private uint WelcomeID;          //our player ID once content is in place
        private CoopManifest HostContent;
        private readonly List<KeyValuePair<CoopContentFile, string>> ContentInPlace = new List<KeyValuePair<CoopContentFile, string>>();
        private readonly HashSet<int> ContentAwaited = new HashSet<int>();
        private long ContentBytesTotal, ContentBytesDone;
        private byte[] PendingWorld; //the neighbourhood for the lot state in the next Tick frame
        private string PendingEnd; //set by the driver mid-tick; the session ends on the next Update

        public CoopClient()
        {
            ResetDriver();
        }

        /// <summary>
        /// Starts a fresh driver, for the next lot the host takes us to. The session carries on.
        /// </summary>
        public void ResetDriver()
        {
            var driver = new CoopClientDriver();
            driver.OnClientCommand += (data) => Connection?.Send(CoopFrameKind.Command, data);
            driver.OnShutdown += (reason) =>
            {
                //closing a lot shuts its driver down too. That is not the end of the session: the host may
                //just be back at the neighbourhood. Only trouble reading the host's data is.
                if (driver != Driver || reason == FSO.SimAntics.NetPlay.VMCloseNetReason.LeaveLot) return;
                PendingEnd = driver.CloseString ?? "The game lost sync with the host.";
            };
            Driver = driver;
        }

        /// <summary>Create-a-Family: sends the family as we, holding control, have changed it.</summary>
        public void SendCasEdit(byte[] family)
        {
            Connection?.Send(CoopFrameKind.CasEdit, family);
        }

        /// <summary>Create-a-Family: gives control back to the host.</summary>
        public void SendCasRelease()
        {
            Connection?.Send(CoopFrameKind.CasRelease, null);
        }

        /// <summary>
        /// Asks the host for house files by name. They arrive one by one through <see cref="OnHouseFile"/>.
        /// </summary>
        public void RequestHouses(ICollection<string> names)
        {
            if (names.Count == 0) return;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(names.Count);
                    foreach (var name in names) writer.Write(name);
                }
                Connection?.Send(CoopFrameKind.HouseRequest, stream.ToArray());
            }
        }

        /// <summary>
        /// Parses "host" or "host:port" (or "[v6]:port").
        /// </summary>
        public static void ParseAddress(string address, out string host, out int port)
        {
            host = address.Trim();
            port = CoopProtocol.DefaultPort;
            var colon = host.LastIndexOf(':');
            var isBareV6 = host.IndexOf(':') != colon && !host.StartsWith("[");
            if (colon > 0 && !isBareV6 && int.TryParse(host.Substring(colon + 1), out var parsed))
            {
                port = parsed;
                host = host.Substring(0, colon);
            }
            host = host.Trim('[', ']');
        }

        public void Connect(string address, string playerName)
        {
            ParseAddress(address, out var host, out var port);
            new Thread(() =>
            {
                try
                {
                    var socket = new TcpClient();
                    socket.Connect(host, port);
                    var conn = new CoopConnection(socket, Incoming);
                    Connection = conn; //before Hello, so the reply always finds it
                    if (Ended)
                    {
                        conn.Close(); //we left while the connection was being made
                        return;
                    }
                    using (var stream = new MemoryStream())
                    {
                        using (var writer = new BinaryWriter(stream))
                        {
                            writer.Write(CoopProtocol.Version);
                            writer.Write(playerName ?? "");
                        }
                        conn.Send(CoopFrameKind.Hello, stream.ToArray());
                    }
                }
                catch (Exception e)
                {
                    Incoming.Enqueue(new CoopFrame
                    {
                        Kind = CoopFrameKind.Closed,
                        Data = Encoding.UTF8.GetBytes("Could not connect to " + host + ":" + port + ".\n\n" + e.Message)
                    });
                }
            })
            { IsBackground = true, Name = "Coop connect" }.Start();
        }

        public void RequestSpeed(int speed)
        {
            Connection?.Send(CoopFrameKind.SpeedRequest, new byte[] { (byte)speed });
        }

        /// <summary>Leaves the session. OnClosed is not fired for this.</summary>
        public void Disconnect()
        {
            Ended = true;
            Connection?.Close();
        }

        /// <summary>Ends the session over something that went wrong on this side. OnClosed is fired with the reason.</summary>
        public void Fail(string reason)
        {
            End(reason);
        }

        public void Update()
        {
            if (PendingEnd != null)
            {
                var reason = PendingEnd;
                PendingEnd = null;
                End(reason);
            }
            if (PendingWelcome != null && LocalContent.IsCompleted) HandleWelcome();

            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!Ended && Incoming.TryDequeue(out var frame))
            {
                switch (frame.Kind)
                {
                    case CoopFrameKind.Welcome:
                        if (PlayerID != 0 || PendingWelcome != null || HostContent != null) break;
                        PendingWelcome = frame.Data;
                        if (LocalContent.IsCompleted) HandleWelcome();
                        break;
                    case CoopFrameKind.ContentFile:
                        if (PlayerID == 0 && HostContent != null) HandleContentFile(frame.Data);
                        break;
                    case CoopFrameKind.Reject:
                        string reason;
                        try
                        {
                            using (var reader = new BinaryReader(new MemoryStream(frame.Data))) reason = reader.ReadString();
                        }
                        catch (Exception) { reason = "The host refused the connection."; }
                        End(reason);
                        break;
                    case CoopFrameKind.Tick:
                        if (PlayerID == 0 || frame.Data.Length < 1) break;
                        var count = frame.Data[0];
                        var headerSize = 1 + count * 2;
                        if (frame.Data.Length < headerSize) break;
                        var speeds = new short[count];
                        for (int i = 0; i < count; i++) speeds[i] = BitConverter.ToInt16(frame.Data, 1 + i * 2);
                        var ticks = new byte[frame.Data.Length - headerSize];
                        Buffer.BlockCopy(frame.Data, headerSize, ticks, 0, ticks.Length);
                        Driver.EnqueueTicks(ticks, speeds, PendingWorld);
                        PendingWorld = null;
                        break;
                    case CoopFrameKind.Players:
                        if (frame.Data.Length < 4) break;
                        var playerCount = BitConverter.ToInt32(frame.Data, 0);
                        if (playerCount < 0 || frame.Data.Length != 4 + playerCount * 4) break;
                        var players = new HashSet<uint>();
                        for (int i = 0; i < playerCount; i++) players.Add(BitConverter.ToUInt32(frame.Data, 4 + i * 4));
                        Players = players;
                        break;
                    case CoopFrameKind.World:
                        if (PlayerID != 0) PendingWorld = frame.Data;
                        break;
                    case CoopFrameKind.Lobby:
                        if (PlayerID != 0) OnLobby?.Invoke(frame.Data);
                        break;
                    case CoopFrameKind.Scene:
                        if (PlayerID != 0 && frame.Data.Length == 11)
                        {
                            OnScene?.Invoke((CoopScene)frame.Data[0], BitConverter.ToUInt16(frame.Data, 1),
                                BitConverter.ToInt32(frame.Data, 3), BitConverter.ToInt32(frame.Data, 7));
                        }
                        break;
                    case CoopFrameKind.Cas:
                        if (PlayerID != 0 && frame.Data.Length >= 4)
                        {
                            var family = new byte[frame.Data.Length - 4];
                            Buffer.BlockCopy(frame.Data, 4, family, 0, family.Length);
                            OnCas?.Invoke(BitConverter.ToUInt32(frame.Data, 0), family);
                        }
                        break;
                    case CoopFrameKind.Notice:
                        if (PlayerID == 0) break;
                        try
                        {
                            using (var reader = new BinaryReader(new MemoryStream(frame.Data))) OnNotice?.Invoke(reader.ReadString());
                        }
                        catch (Exception) { }
                        break;
                    case CoopFrameKind.HouseFile:
                        if (PlayerID == 0) break;
                        try
                        {
                            string houseName;
                            int start;
                            using (var stream = new MemoryStream(frame.Data))
                            using (var reader = new BinaryReader(stream))
                            {
                                houseName = reader.ReadString();
                                start = (int)stream.Position;
                            }
                            OnHouseFile?.Invoke(houseName, frame.Data, start, frame.Data.Length - start);
                        }
                        catch (Exception) { } //not worth ending the session over a picture
                        break;
                    case CoopFrameKind.Direct:
                        if (PlayerID != 0) OnDirect?.Invoke(frame.Data);
                        break;
                    case CoopFrameKind.Closed:
                        var text = Encoding.UTF8.GetString(frame.Data);
                        End(text == "" ? "Connection lost." : text);
                        break;
                }
                //files being fetched are checked and saved here, so leave time to draw the progress
                if ((ContentAwaited.Count > 0 || frame.Kind == CoopFrameKind.HouseFile) && timer.ElapsedMilliseconds > 12) break;
            }
        }

        private void HandleWelcome()
        {
            var data = PendingWelcome;
            PendingWelcome = null;
            if (LocalContent.IsFaulted || LocalContent.IsCanceled)
            {
                End("This game could not read its own objects to compare them with the host's.");
                return;
            }

            CoopContentPlan plan;
            try
            {
                CoopCompat.ParseWelcome(data, out WelcomeID, out var hostVersion, out HostContent);
                var problem = CoopCompat.VersionProblem(hostVersion, GameVersion);
                if (problem != null)
                {
                    End(problem);
                    return;
                }
                plan = CoopContent.Compare(HostContent, LocalContent.Result);
            }
            catch (Exception)
            {
                End("The host sent a reply this game could not read.");
                return;
            }
            if (plan.Problem != null)
            {
                End(plan.Problem);
                return;
            }

            foreach (var index in plan.Needed)
            {
                var file = HostContent.Files[index];
                var path = (CacheDir == null) ? null : CoopContent.CachePath(CacheDir, file);
                if (path != null && File.Exists(path) && new FileInfo(path).Length == file.Size)
                {
                    ContentInPlace.Add(new KeyValuePair<CoopContentFile, string>(file, path)); //fetched in an earlier session
                }
                else
                {
                    ContentAwaited.Add(index);
                    ContentBytesTotal += file.Size;
                }
            }

            if (ContentAwaited.Count == 0)
            {
                FinishJoin();
                return;
            }
            if (CacheDir == null)
            {
                End("The host has " + ContentAwaited.Count + " objects this game lacks, and there is nowhere to keep them.");
                return;
            }

            var request = new byte[4 + ContentAwaited.Count * 4];
            BitConverter.TryWriteBytes(new Span<byte>(request, 0, 4), ContentAwaited.Count);
            var at = 4;
            foreach (var index in ContentAwaited)
            {
                BitConverter.TryWriteBytes(new Span<byte>(request, at, 4), index);
                at += 4;
            }
            Connection?.Send(CoopFrameKind.ContentRequest, request);
            OnContentProgress?.Invoke(0, ContentBytesTotal, ContentAwaited.Count);
        }

        private void HandleContentFile(byte[] data)
        {
            if (data.Length < 4) return;
            var index = BitConverter.ToInt32(data, 0);
            if (!ContentAwaited.Contains(index)) return;
            var file = HostContent.Files[index];
            var length = data.Length - 4;

            //the fingerprint is also the file's place in the cache, so it has to be what the host said
            if (length != file.Size || !CoopContent.HashEquals(CoopContent.HashOf(data, 4, length), file.Hash))
            {
                End("An object from the host (" + file.Name + ") did not arrive as the host described it. " +
                    "It may have changed on the host since the session began.");
                return;
            }

            var path = CoopContent.CachePath(CacheDir, file);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var temp = path + ".part";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 4, length);
                }
                File.Move(temp, path, true);
            }
            catch (Exception e)
            {
                End("An object from the host could not be saved to " + path + ".\n\n" + e.Message);
                return;
            }

            ContentAwaited.Remove(index);
            ContentInPlace.Add(new KeyValuePair<CoopContentFile, string>(file, path));
            ContentFilesFetched++;
            ContentBytesDone += length;
            OnContentProgress?.Invoke(ContentBytesDone, ContentBytesTotal, ContentAwaited.Count);
            if (ContentAwaited.Count == 0) FinishJoin();
        }

        /// <summary>
        /// Everything the host has that we needed is on disk: use it, and tell the host we can be given the lot.
        /// </summary>
        private void FinishJoin()
        {
            try
            {
                if (ContentInPlace.Count > 0) ApplyContent?.Invoke(ContentInPlace);
            }
            catch (Exception e)
            {
                End("The host's objects could not be loaded.\n\n" + e.Message);
                return;
            }
            PlayerID = WelcomeID;
            Connection?.Send(CoopFrameKind.Ready, null);
            OnWelcome?.Invoke();
        }

        private void End(string reason)
        {
            if (Ended) return;
            Ended = true;
            Connection?.Close();
            OnClosed?.Invoke(reason);
        }
    }
}
