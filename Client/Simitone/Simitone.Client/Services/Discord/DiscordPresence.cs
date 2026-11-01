using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FSO.Common;

namespace Simitone.Client.Services.Discord
{
    /// <summary>
    /// Discord Rich Presence: shows what the player is doing in Simitone on their Discord profile. On unless
    /// discord.ini (in the user folder, written on first run) turns it off; it shows as Simitone's Discord application.
    /// The game thread only hands over the wanted activity (<see cref="Tick"/>); a background task owns the connection,
    /// so a slow or absent Discord never stalls the game. It reconnects when Discord starts later, sends only changes,
    /// and no more often than Discord allows (5 updates per 20 s). Notes go to discord.log next to it.
    /// </summary>
    public static class DiscordPresence
    {
        private const double CheckInterval = 1.0; //seconds between looks at the game

        /// <summary>Simitone's Discord application (what "Playing Simitone" and the pictures come from).</summary>
        public const string SimitoneApplicationId = "1566559628077047818";

        public static string SettingsPath => Path.Combine(FSOEnvironment.UserDir ?? "", "discord.ini");
        public static string LogPath => Path.Combine(FSOEnvironment.UserDir ?? "", "discord.log");

        private static bool Started;
        private static PresenceText Text;
        private static PresenceActivity Wanted; //latest from the game thread
        private static double NextCheck;
        private static CancellationTokenSource Stop;

        /// <summary>Reads discord.ini (writing the template if there's none) and starts if it's enabled.</summary>
        public static void Start()
        {
            if (Started) return;
            Started = true;
            try
            {
                var settings = ReadSettings();
                //on unless discord.ini turns it off
                if (settings.ContainsKey("enabled") && !IsOn(settings, "enabled")) return;
                if (!settings.TryGetValue("client_id", out var clientId) || string.IsNullOrWhiteSpace(clientId))
                    clientId = SimitoneApplicationId;
                Text = new PresenceText();
                if (settings.TryGetValue("large_image", out var image) && image.Length > 0) Text.LargeImage = image;
                if (settings.ContainsKey("place_images")) Text.PlaceImages = IsOn(settings, "place_images");
                else if (settings.ContainsKey("small_images")) Text.PlaceImages = IsOn(settings, "small_images"); //its old name
                Stop = new CancellationTokenSource();
                File.WriteAllText(LogPath, $"Simitone Discord log, {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
                var link = new DiscordLink(new DiscordRpcClient(clientId.Trim())) { Log = Log };
                Task.Run(() => link.RunAsync(() => Volatile.Read(ref Wanted), Stop.Token));
            }
            catch (Exception e)
            {
                Log("Couldn't start: " + e.Message);
            }
        }

        /// <summary>Called every frame from the game thread: looks at the game about once a second.</summary>
        public static void Tick(double now)
        {
            if (Text == null || now < NextCheck) return;
            NextCheck = now + CheckInterval;
            try
            {
                var info = PresenceSource.Read();
                Volatile.Write(ref Wanted, Text.Build(info, DateTimeOffset.UtcNow));
            }
            catch
            {
                //a screen in an odd state for a moment: try again next time
            }
        }

        public static void Shutdown()
        {
            Stop?.Cancel();
        }

        private static Dictionary<string, string> ReadSettings()
        {
            var path = SettingsPath;
            if (!File.Exists(path))
            {
                File.WriteAllText(path, Template);
                return new Dictionary<string, string>();
            }
            return ParseSettings(File.ReadAllLines(path));
        }

        /// <summary>key=value lines; # starts a comment. Keys are lower-cased.</summary>
        public static Dictionary<string, string> ParseSettings(IEnumerable<string> lines)
        {
            var result = new Dictionary<string, string>();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                result[line.Substring(0, eq).Trim().ToLowerInvariant()] = line.Substring(eq + 1).Trim();
            }
            return result;
        }

        public static bool IsOn(Dictionary<string, string> settings, string key)
        {
            return settings.TryGetValue(key, out var value)
                && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
        }

        public const string Template =
            "# Discord Rich Presence: shows what you're doing in Simitone on your Discord profile\n" +
            "# (\"Playing Simitone\", the neighbourhood or the family you're playing, and for how long).\n" +
            "# It needs the Discord app running on this computer. Set enabled=false to turn it off.\n" +
            "enabled=true\n" +
            "# Leave empty for Simitone's own Discord application, or put another application's ID here.\n" +
            "client_id=\n" +
            "# The pictures: art assets uploaded to the application. place_images shows where you are as the\n" +
            "# large picture (buy, build, options, cas, downtown, vacation, studio, magic), with Simitone's\n" +
            "# (simitone_small) in its corner; off, or where there's no place picture, it's large_image alone.\n" +
            "large_image=simitone\n" +
            "place_images=true\n";

        private static void Log(string line)
        {
            try
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            }
            catch
            {
                //nowhere better to say it
            }
        }
    }

    /// <summary>
    /// Keeps one Discord connection showing the wanted activity: connects (and reconnects, every
    /// <see cref="RetryInterval"/> seconds, while Discord is away), sends the activity when it changes but at most every
    /// <see cref="MinSendInterval"/> seconds, answers pings, and keeps reading so Discord's replies don't back up.
    /// </summary>
    public class DiscordLink
    {
        public double MinSendInterval = 4.0; //Discord allows 5 updates per 20 s
        public double RetryInterval = 15.0;
        public Action<string> Log = _ => { };

        private readonly DiscordRpcClient Client;

        public DiscordLink(DiscordRpcClient client)
        {
            Client = client;
        }

        public async Task RunAsync(Func<PresenceActivity> wanted, CancellationToken stop)
        {
            PresenceActivity sent = null;
            var lastSend = DateTime.MinValue;
            var loggedMissing = false;
            Task<(DiscordOp op, string json)> reading = null;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    if (!Client.Connected)
                    {
                        reading = null;
                        sent = null;
                        if (await Client.ConnectAsync(stop))
                        {
                            Log("Connected to Discord.");
                            loggedMissing = false;
                        }
                        else
                        {
                            if (!loggedMissing) Log("Discord isn't running, or didn't answer" +
                                (Client.LastError != null ? " (" + Client.LastError + ")" : "") + $"; trying again every {RetryInterval} s.");
                            loggedMissing = true;
                            await Task.Delay(TimeSpan.FromSeconds(RetryInterval), stop);
                            continue;
                        }
                    }

                    reading ??= Client.ReadAsync(stop);
                    if (reading.IsCompleted)
                    {
                        var (op, json) = await reading;
                        reading = null;
                        if (op == DiscordOp.Ping) await Client.Send(DiscordOp.Pong, json, stop);
                        else if (op == DiscordOp.Close) throw new IOException("Discord closed the connection: " + DiscordRpcClient.ReadString(json, "message"));
                        else if (DiscordRpcClient.ReadString(json, "evt") == "ERROR") Log("Discord said: " + json);
                        continue;
                    }

                    var next = wanted();
                    if (next != null && !next.Equals(sent) && (DateTime.UtcNow - lastSend).TotalSeconds >= MinSendInterval)
                    {
                        await Client.SetActivityAsync(next, stop);
                        sent = next;
                        lastSend = DateTime.UtcNow;
                    }
                    await Task.WhenAny(reading, Task.Delay(100, stop));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Log("Lost Discord: " + e.Message);
                    Client.Close();
                    try { await Task.Delay(TimeSpan.FromSeconds(RetryInterval), stop); }
                    catch (OperationCanceledException) { break; }
                }
            }
            Client.Dispose();
        }
    }
}
