# Discord Rich Presence

Shows what you're doing in Simitone on your Discord profile, under "Playing Simitone":

| Where | Top line | Second line |
|---|---|---|
| Starting | Starting up | |
| Neighbourhood | In the neighbourhood | Old Town, Downtown, Vacation Island, Studio Town, Magic Town |
| Create a Family | Create a Family | Choosing a family, Editing a family, Making a Sim |
| A family's lot | Playing the Goth family | Live mode at 5 Sim Lane (Buy mode, Build mode, Options) |
| A lot without a family | the lot's name | the mode |

The "elapsed" time counts from when you arrived where you are: switching mode keeps it, going to another lot or back to the neighbourhood restarts it.

It's part of Simitone only; the FreeSO engine isn't changed.

## Turning it on

It's off until you set it up, because it needs a Discord application to show up as:

1. At <https://discord.com/developers/applications>, make a **New Application** named **Simitone** (the name is what Discord shows after "Playing").
2. Copy its **Application ID** (a long number).
3. Optionally, under **Rich Presence → Art Assets**, upload an image named `simitone`: the large picture on the profile.
4. Start Simitone once; it writes `discord.ini` in the user folder (`~/.local/share/Simitone/` on Linux). Set `enabled=true` and `client_id=` the ID, and restart Simitone.

The Discord app has to be running on the same computer. If the ID is baked into Simitone later, step 1 to 3 go away and `discord.ini` is only the on/off switch.

## How it works

- `Services/Discord/DiscordIpc.cs`: Discord's local IPC protocol, without a library. Frames are a little-endian opcode and length, then UTF-8 JSON; a handshake (`{"v":1,"client_id":…}`) answered by `READY`, then `SET_ACTIVITY` commands carrying the process ID and the activity. On Windows it connects to the named pipes `discord-ipc-0` to `9`; elsewhere to the Unix sockets of the same names in `XDG_RUNTIME_DIR` (and the folders the Flatpak and Snap builds of Discord, and Vesktop, use inside it), then `TMPDIR`, `TMP`, `TEMP` and `/tmp`.
- `Services/Discord/PresenceSource.cs`: reads the current screen on the game thread (about once a second).
- `Services/Discord/PresenceText.cs`: turns that into the two lines and the start time. Plain data in and out.
- `Services/Discord/DiscordPresence.cs`: `discord.ini`, and `DiscordLink`, the background task that owns the connection. It connects when Discord is running (retrying every 15 s if not), sends the activity only when it changes and at most every 4 s (Discord allows 5 updates per 20 s), answers Discord's pings, and keeps reading its replies so they don't back up. The game thread never waits on Discord. Notes go to `discord.log` next to `discord.ini`.

## Tests

The controller harness covers it: framing, the socket search order, the text for each place, the timer, `discord.ini` parsing, and, against a fake Discord on a real Unix socket, the handshake, `SET_ACTIVITY` (with the process ID), a refused application ID, no Discord running, pings, the rate limit, and reconnecting after Discord goes away.
