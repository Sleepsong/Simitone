using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using Simitone.Client.Network.Coop;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// The dialogs behind the co-op button on the neighbourhood screen: host a game or join one, and once in a
    /// session, see how it stands and end it. (The SIMITONE_COOP_HOST and SIMITONE_COOP_JOIN environment
    /// variables do the same at start-up; see CoopOptions.)
    /// </summary>
    public static class UICoopMenu
    {
        private static UIMobileAlert Open;

        public static void Show(TS1GameScreen screen)
        {
            if (Open != null) return;
            if (CoopManager.Host != null) ShowHosting();
            else if (CoopManager.IsGuest) ShowGuest();
            else ShowStart(screen);
        }

        private static void Present(UIAlertOptions options, string text = null)
        {
            options.Width = Math.Max(options.Width, 325 + (int)((options.Message ?? "").Length / 3.5f));
            Open = new UIMobileAlert(options);
            if (text != null) Open.ResponseText = text;
            UIScreen.GlobalShowDialog(Open, true);
        }

        private static void Close()
        {
            Open?.Close();
            Open = null;
        }

        private static UIAlertButton Button(UIAlertButtonType type, string text, Action action)
        {
            return new UIAlertButton(type, (btn) =>
            {
                Close();
                action?.Invoke();
            }, text);
        }

        private static void ShowStart(TS1GameScreen screen)
        {
            Present(new UIAlertOptions()
            {
                Title = "Co-op",
                Message = "Play this neighbourhood together with a friend.\n\n" +
                    "Host: your friend joins your game, and plays your neighbourhood and families with you.\n" +
                    "Join: you play in a friend's game. Yours is left as it is, and comes back when you leave.",
                Buttons = new UIAlertButton[]
                {
                    Button(UIAlertButtonType.Yes, "Host", StartHosting),
                    Button(UIAlertButtonType.No, "Join", () => AskAddress(screen)),
                    Button(UIAlertButtonType.Cancel, "Cancel", null)
                }
            });
        }

        private static void StartHosting()
        {
            var port = (CoopOptions.HostPort > 0) ? CoopOptions.HostPort : CoopProtocol.DefaultPort;
            if (CoopManager.StartHosting(port)) ShowHosting();
            else
            {
                Present(new UIAlertOptions()
                {
                    Title = "Co-op",
                    Message = "The game could not start hosting on port " + port + ". Another program, or another copy of the game, may be using it.",
                    Buttons = new UIAlertButton[] { Button(UIAlertButtonType.OK, "OK", null) }
                });
            }
        }

        private static void ShowHosting()
        {
            var host = CoopManager.Host;
            if (host == null) return;
            var addresses = LocalAddresses();
            var message = "You are hosting, on port " + host.Port + ". " +
                ((host.GuestCount == 0) ? "Nobody has joined yet." : (host.GuestCount == 1) ? "One guest is playing." : host.GuestCount + " guests are playing.") + "\n\n" +
                "A friend on your home network presses the co-op button, chooses Join and types " +
                ((addresses.Count == 0) ? "this computer's address" : (addresses.Count == 1) ? addresses[0] : "one of " + string.Join(", ", addresses)) + ".\n" +
                "Over the internet they need your public address instead, and your router has to pass port " + host.Port + " on to this computer.";
            Present(new UIAlertOptions()
            {
                Title = "Co-op",
                Message = message,
                Buttons = new UIAlertButton[]
                {
                    Button(UIAlertButtonType.OK, "Keep Hosting", null),
                    Button(UIAlertButtonType.Cancel, "Stop Hosting", CoopManager.StopHosting)
                }
            });
        }

        private static void ShowGuest()
        {
            Present(new UIAlertOptions()
            {
                Title = "Co-op",
                Message = "You are playing in a friend's game. Leaving takes you back to your own neighbourhood, as you left it.",
                Buttons = new UIAlertButton[]
                {
                    Button(UIAlertButtonType.OK, "Stay", null),
                    Button(UIAlertButtonType.Cancel, "Leave", CoopManager.Leave)
                }
            });
        }

        private static void AskAddress(TS1GameScreen screen)
        {
            UIMobileAlert asking = null;
            Present(new UIAlertOptions()
            {
                Title = "Join a Game",
                Message = "Type the address of the computer that is hosting, for example 192.168.1.20. The host's co-op button shows it.",
                TextEntry = true,
                MaxChars = 100,
                Buttons = new UIAlertButton[]
                {
                    new UIAlertButton(UIAlertButtonType.OK, (btn) =>
                    {
                        var address = (asking?.ResponseText ?? "").Trim();
                        Close();
                        if (address == "") return;
                        RememberAddress(address);
                        //only from the neighbourhood, which is where the button is: but the dialog can outlast it
                        var current = GameFacade.Screens.CurrentUIScreen as TS1GameScreen;
                        if (current != null && !current.InLot) CoopManager.Join(address, current);
                    }, "Join"),
                    Button(UIAlertButtonType.Cancel, "Cancel", null)
                }
            }, LastAddress());
            asking = Open;
        }

        /// <summary>
        /// This computer's addresses on the networks it is connected to, for the host to pass on.
        /// </summary>
        private static List<string> LocalAddresses()
        {
            var result = new List<string>();
            try
            {
                foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var address in network.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var text = address.Address.ToString();
                        if (!text.StartsWith("169.254.") && !result.Contains(text)) result.Add(text);
                    }
                }
            }
            catch (Exception) { } //then the dialog just says "this computer's address"
            if (result.Count > 3) result.RemoveRange(3, result.Count - 3);
            return result;
        }

        private static string AddressFile { get { return Path.Combine(CoopContentScanner.CacheRoot, "lastjoin.txt"); } }

        private static string LastAddress()
        {
            try
            {
                if (File.Exists(AddressFile)) return File.ReadAllText(AddressFile).Trim();
            }
            catch (Exception) { }
            return "";
        }

        private static void RememberAddress(string address)
        {
            try
            {
                Directory.CreateDirectory(CoopContentScanner.CacheRoot);
                File.WriteAllText(AddressFile, address);
            }
            catch (Exception) { }
        }
    }
}
