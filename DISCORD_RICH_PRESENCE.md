# Discord Rich Presence

Shows what you're doing in Simitone on your Discord profile, under "Playing Simitone":

| Where | Top line | Second line |
|---|---|---|
| Starting | Starting up | |
| Neighbourhood | In the neighbourhood | Old Town, Downtown, Vacation Island, Studio Town, Magic Town |
| A town's map, travelling from a lot | Travelling | Vacation Island, Studio Town, Magic Town... |
| Create a Family | Create a Family | Choosing a family, Editing a family, Making a Sim |
| A family's lot | Playing the Goth family | Live mode at 5 Sim Lane (Buy mode, Build mode, Options) |
| A lot without a family | the lot's name | the mode |

The "elapsed" time counts from when you arrived where you are: switching mode keeps it, going to another lot or back to the neighbourhood restarts it.

It's part of Simitone only; the FreeSO engine isn't changed.

## Turning it on

It's on by default, as Simitone's Discord application (ID `1566559628077047818`, built in). The Discord app has to be running on the same computer. To turn it off, set `enabled=false` in `discord.ini` in the user folder (`~/.local/share/Simitone/` on Linux; Simitone writes it on first run). `client_id=` can point it at a different application.

## Art assets

In the [Discord Developer Portal](https://discord.com/developers/applications), on the Simitone application. All are in `art/discord/`, made from Simitone's own icons (the app icon and the mode and neighbourhood buttons):

| Where | File | Name to give it |
|---|---|---|
| General Information → App Icon | `app_icon.png` (1024×1024) | — |
| Rich Presence → Art Assets | `simitone.png` (1024×1024) | `simitone` (the large picture) |
| Rich Presence → Art Assets | `buy.png`, `build.png`, `options.png` (512×512) | `buy`, `build`, `options` (the small picture in those modes) |
| Rich Presence → Art Assets | `cas.png` | `cas` (Create a Family) |
| Rich Presence → Art Assets | `downtown.png`, `vacation.png`, `studio.png`, `magic.png` | `downtown`, `vacation`, `studio`, `magic` (those neighbourhoods) |

The asset name is what Simitone asks for, so it has to match exactly (Discord fills it in from the file name). New assets can take a few minutes to show up. In buy, build and options the small picture is the mode; otherwise it's the town (in a town's neighbourhood, on its map while travelling, and in live mode on its lots); in the main neighbourhood and live mode on its lots there's none. If the small ones aren't uploaded, set `small_images=false`.

The pictures are scaled up from the 256×256 app icon and the 96–128 px buttons, because Discord asks for at least 512×512; it only ever shows them at 30–120 px, where they look sharp. The small ones sit on a white disc, since Discord crops small pictures to a circle.

## How it works

- `Services/Discord/DiscordIpc.cs`: Discord's local IPC protocol, without a library. Frames are a little-endian opcode and length, then UTF-8 JSON; a handshake (`{"v":1,"client_id":…}`) answered by `READY`, then `SET_ACTIVITY` commands carrying the process ID and the activity. On Windows it connects to the named pipes `discord-ipc-0` to `9`; elsewhere to the Unix sockets of the same names in `XDG_RUNTIME_DIR` (and the folders the Flatpak and Snap builds of Discord, and Vesktop, use inside it), then `TMPDIR`, `TMP`, `TEMP` and `/tmp`.
- `Services/Discord/PresenceSource.cs`: reads the current screen on the game thread (about once a second).
- `Services/Discord/PresenceText.cs`: turns that into the two lines and the start time. Plain data in and out.
- `Services/Discord/DiscordPresence.cs`: `discord.ini`, and `DiscordLink`, the background task that owns the connection. It connects when Discord is running (retrying every 15 s if not), sends the activity only when it changes and at most every 4 s (Discord allows 5 updates per 20 s), answers Discord's pings, and keeps reading its replies so they don't back up. The game thread never waits on Discord. Notes go to `discord.log` next to `discord.ini`.

## Tests

The controller harness covers it: framing, the socket search order, the text for each place, the timer, `discord.ini` parsing, and, against a fake Discord on a real Unix socket, the handshake, `SET_ACTIVITY` (with the process ID), a refused application ID, no Discord running, pings, the rate limit, and reconnecting after Discord goes away.
