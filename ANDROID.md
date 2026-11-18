# Simitone on Android

`Client/Simitone/Simitone.Android` builds Simitone for Android phones (arm64, Android 7.0 or later, OpenGL ES 3).
It has been tested on a Google Pixel 10 Pro running Android 17.

## Playing

1. Copy The Sims (the folder that holds `GameData`) to the phone's **Download** folder, named `sims`.
   Any folder in Download works, or one folder inside it, such as `Download/The Sims Legacy Collection`.
2. Install the APK, open Simitone, and allow **All files access** when asked. Simitone needs it because the game
   files and saves are in Download.
3. On the first run after an install or update, Simitone unpacks its own content (fonts, UI, shaders) to the app's
   storage. This takes a few seconds.

Saves, `config.ini` and `simitone.log` go in **Download/Simitone**. A copy of the log is also kept in
`Android/media/com.sleepsong.simitone`.

### Settings

Open the options dialog from the **...** button on the neighbourhood screen, or from **Options > Settings** on a lot.
On phones the Graphics tab shows:

- **3D Mode**: the 3D view, or the classic 2D one. Restart Simitone to apply.
- **Show FPS**: a frame rate counter at the top of the screen.
- **2D Drawing**: *Fast* draws the 2D view's depth in one pass, on GPUs that support framebuffer fetch (see below).
  *Compatible* uses FreeSO's original two-pass method. Restart Simitone to apply.

The log says which 2D method is in use: `2D depth: one pass (framebuffer fetch)`, or `two passes (reason)`.

## Android Auto (while parked)

On phones with Android 15 or later, Android Auto runs games on the car's screen while the car is parked. Simitone is
declared as one, so it appears in the car's app launcher.

- **First run.** Set Simitone up on the phone first: allow **All files access** and put the game in Download, as above.
  Android Auto can't open the settings page on the car's screen. If access is missing there, Simitone says to allow it
  on the phone.
- **Sideloaded installs.** Android Auto only runs apps from Google Play (or ONE store) in a real car, unless
  **Unknown sources** is on in Android Auto's developer settings. To show those settings, open Android Auto's settings
  on the phone and tap **Version** ten times.
- **Moving between screens.** Opening Simitone from the car's launcher while it's running on the phone moves the game
  to the car's screen, and opening it on the phone moves it back. The game keeps running and the UI is scaled for the
  screen it's on, as on phones: at least ~560 px tall, so 1x on an 800x480 car screen and about 1.9x on a 1080p one.
  When the game leaves the car's screen, time is paused.
- **When the car moves.** Android Auto closes Simitone when the car starts moving. So whenever Simitone is paused on the
  car's screen, it first pauses time and saves the household, as **Options > Save** does. This happens when the car
  starts moving, when you switch to another app on the car's screen, or when you leave Simitone with Android Auto's
  controls. It isn't saved while visiting another lot (downtown, on vacation), where The Sims doesn't save either, or
  while a lot is loading. Sound stops while Simitone is paused (MonoGame pauses its audio device).
- **Controls.** Use the car's touch screen. Swipe from the top or bottom edge for Android Auto's back and exit controls.

The log says when Simitone is on the car's screen (`moved to display ...`, `car: ...`), including each save and how long
it took.

Android Auto support needs no extra packages. It hasn't been tested on a car or the Desktop Head Unit yet (see below).
Still to do:

- After Android Auto closes Simitone, opening it again starts at the neighbourhood, with the household saved. Google's
  guidelines ask for it to go back to the lot.
- Game controllers. If they're supported later, add `<uses-feature android:name="android.hardware.gamepad"
  android:required="false"/>` to the manifest.

### What makes it work

- `SimitoneActivity` has an extra `MAIN` intent filter with the `android.intent.category.CAR_LAUNCHER` category.
  Together with `android:appCategory="game"` on `<application>` in `AndroidManifest.xml`, this lists Simitone in
  Android Auto's launcher. Games are the only parked app category Android Auto supports.
- The activity handles changes of display, density, screen size, touch screen and navigation itself, so moving between
  the phone's screen and the car's doesn't make it again. The engine's global state allows one game per run.
- `CarScreen.cs` tells which display Simitone is on (not `Display.DEFAULT_DISPLAY` means the car's), works out the UI
  scale, applies it with `SimitoneGame.SetUIScale`, and keeps the game to landscape-left on the car's screen. It also
  does the pause and save when Simitone is paused there. That runs before MonoGame stops the game loop in `OnPause`,
  and waits up to 4 s for the game.
- There's no `distractionOptimized` metadata on any activity, as parked apps mustn't have it. Simitone posts no
  notifications.

### Testing with the Desktop Head Unit

Google's [Desktop Head Unit](https://developer.android.com/training/cars/testing/dhu) (DHU, the **Android Auto Desktop
Head Unit Emulator** in the Android SDK's tools) shows Android Auto on a computer. Turn on Android Auto's developer mode
(see above), choose **Start head unit server** in its menu, connect the phone over USB, then:

```sh
adb forward tcp:5277 tcp:5277
cd $ANDROID_SDK/extras/google/auto
./desktop-head-unit                  # or: ./desktop-head-unit -c some-config.ini
```

Check the screen sizes Google asks parked apps to handle, with DHU configuration files
([Android Auto parked apps](https://developer.android.com/training/cars/parked/auto#test-screen-sizes)): 800x480
(the default), wide 1920x1080 with `marginheight = 596`, and portrait 1920x1080 with `marginwidth = 878`. In the DHU's
terminal, `restrict all` (or Shift+U) acts as the car starting to move, and `restrict none` (U) as it stopping.

Google reviews Play Store games against the
[car app quality guidelines](https://developer.android.com/docs/quality-guidelines/car-app-quality?category=game).
Simitone isn't on the Play Store, but they're a useful checklist.

## Building

You need:

- the .NET 10 SDK with the `android` workload (`dotnet workload install android`)
- the Android SDK (platform 36 and build-tools)
- JDK 21

Then build from the project folder:

```sh
cd Client/Simitone/Simitone.Android
dotnet publish -c Release -f net10.0-android \
  -p:AndroidSdkDirectory=/path/to/android-sdk -p:JavaSdkDirectory=/path/to/jdk-21
```

The APK is `bin/Release/net10.0-android/publish/com.sleepsong.simitone-Signed.apk`, signed with the .NET debug
key. Build from that folder: its `Directory.Build.rsp` passes `-p:SimitoneAndroid=true` to every project in the
build. Without it, the engine libraries would hand MonoGame DesktopGL to the app (see `Directory.Build.targets`).

### NuGet packages

The Android project adds these packages. Everything else comes from the existing Simitone and FreeSO projects.

| Package | Version | Why |
| --- | --- | --- |
| MonoGame.Framework.Android | 3.8.4 | MonoGame for Android: GLES, input, audio |
| SixLabors.ImageSharp | 3.1.11 | Decodes images on Android, where System.Drawing isn't available |

The SDK adds Microsoft.NET.ILLink.Tasks 10.0.14 for trimming. The .NET Android workload also brings in its own
packs: Microsoft.Android.Sdk, the Mono runtime, and the Mono AOT compiler.

Packages that come in through the engine and Simitone projects:

- Common.Logging 3.3.1 and Common.Logging.Core 3.3.1
- CommunityToolkit.HighPerformance 8.2.2
- DeltaQ 2.1.0, with DeltaQ.BsDiff, DeltaQ.SuffixSorting.Abstractions, DeltaQ.SuffixSorting.LibDivSufSort,
  DeltaQ.SuffixSorting.SAIS and DeltaQ.Utility.Memory (all 2.1.0)
- FreeSO.HandEvaluator 1.2.0
- Microsoft.Win32.SystemEvents 9.0.9
- Mina 2.0.12
- MonoGame.Library.OpenAL 1.23.1.10
- MP3Sharp 1.0.5
- Newtonsoft.Json 13.0.4
- Ninject 3.3.6
- SharpZipLib 1.4.2
- System.Drawing.Common 9.0.9
- System.Formats.Nrbf 9.0.0
- System.Resources.Extensions 9.0.0
- xxHashSharp 1.0.0

MonoGame.Framework.DesktopGL is used to compile the engine libraries, but it isn't packaged in the app.

### What the build does

- **Content.** The project gathers the same `Content` folder the desktop build has, from Simitone.Client,
  tso.content, FSO.Content.TSO and tso.client. It stages that folder in `obj/` and packages it as
  `assets/Content` with a `files.txt` list. On the way, the phone shaders' pixel stages are switched from medium to
  high precision, because depth and lighting band at medium precision on phone GPUs.
- **Ahead-of-time compilation.** The app is AOT-compiled for faster start-up and loading. Only the .NET libraries
  are trimmed. MonoGame and the engine are kept whole, because they find content readers and settings by name.
- **One-pass 2D shader.** `Content/iOS/Effects/2DWorldBatchFetch.xnb` is made from FreeSO's phone 2D world shader
  with `tools/make_fetch.py` (see below).

## Phone-specific changes

- **2D software depth.** Phones can't write gl_FragDepth, so FreeSO draws the 2D view's depth to a separate image.
  - Render targets that share a depth image now also share their depth-stencil buffer, which the stencil pass needs.
  - Sims are drawn through the same depth path.
  - On GPUs with `GL_EXT_shader_framebuffer_fetch`, colour and depth are drawn in a single pass, to two draw
    buffers. This replaces a colour pass, a stencil and a depth pass for every sprite group. At start-up the app
    test-compiles the shaders (`2DWorldBatchFetch.glsl.txt`), and if they fail it falls back to two passes.
- **Fewer static redraws.**
  - Objects hidden by the cutaway stay in the dynamic layer instead of swapping layers every few seconds.
  - Outdoor light changes are throttled in 2D.
  - Large batches of moved objects keep their layer.
- **Touch.**
  - Taps and held objects account for zoom.
  - Pie menus open where you tapped.
  - A tooltip explains when an object has nothing to do or a spot can't be reached.
  - Create-a-Sim: head and body swipes work at any screen density, and the 2D view is scaled up and framed for
    phones.
- **Content.** Bodies without a texture (they showed grey) are left out of Create-a-Sim. Sims in special rooms are lit.
- **Performance log.** Every 5 seconds the log gets a line with the frame rate, slow frames, garbage collections,
  static redraws and their reasons. Spikes over 100 ms are logged as well.

## Regenerating the one-pass shader

Run this after a build, so the staged, high-precision `2DWorldBatchiOS.xnb` exists:

```sh
cd Client/Simitone/Simitone.Android
python3 tools/make_fetch.py obj/Release/net10.0-android/simitone-content/Content/iOS/Effects/2DWorldBatchiOS.xnb \
  Content/iOS/Effects/2DWorldBatchFetch.xnb Content/iOS/Effects/2DWorldBatchFetch.glsl.txt
python3 tools/gles_check.py Content/iOS/Effects/2DWorldBatchFetch.glsl.txt   # compile and link on Mesa
python3 tools/fetch_semantics.py                                             # draw buffer / fetch behaviour test
```

`gles_check.py` and `fetch_semantics.py` need Mesa's surfaceless EGL with GLES 3.2: `libEGL.so.1` and
`libGLESv2.so.2`.
