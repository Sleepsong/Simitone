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

On devices with a second screen, such as the AYN Thor, the lot's controls go on the second screen and the
Graphics tab also shows **Second Screen**. See [DUAL_SCREEN.md](DUAL_SCREEN.md).

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
