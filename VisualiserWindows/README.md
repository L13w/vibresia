# Vibresia for Windows

Visual worlds that move with whatever your PC is playing: neon tunnels, silk ribbons, a dancing llama. Play some music and Vibresia turns it into a live scene that pulses with the bass and sparkles with the treble.

This is a community-built Windows version (beta) of [Vibresia](../README.md), the free macOS app by Syd Lawrence / Minith Labs. It runs the Mac app's own vibescapes, unchanged, and copies its look and controls. It is MIT licensed, like the original (see [`../LICENSE`](../LICENSE)). Windows version by Llew Roberts ([inertialnavigation.com](https://inertialnavigation.com)).

Nothing is recorded or sent anywhere: the sound is analysed in memory, and the app makes no network connections.

## What you need

- Windows 10 or 11, 64-bit.
- A graphics card that supports OpenGL 3.3. Almost any card or built-in graphics from the last ten years does. If yours doesn't, Vibresia says so and suggests updating the graphics driver.

## Install

1. Download the latest Windows build (a `windows-v…` pre-release, `Vibresia-Windows-x64.zip`) from the [releases page](https://github.com/sydlawrencedev/vibresia/releases). Until the first one is published there, test builds are on [the port's own releases page](https://github.com/L13w/vibresia/releases).
2. Unzip it anywhere and run `Vibresia.exe`. There is no installer, and nothing else to install.
3. The app isn't signed yet, so Windows SmartScreen may warn the first time: choose **More info → Run anyway**.

To remove it, delete the folder. If you want your settings and logs gone too, delete `%APPDATA%\Vibresia` and `%LOCALAPPDATA%\Vibresia`.

## Using it

When it starts, Vibresia asks what to listen to:

- **Share System Audio**: whatever the PC is playing: Spotify, YouTube, games.
- **Use Microphone**: whatever the mic hears.
- **Use Demo Tone**: a built-in beat, played aloud, for trying it with nothing else playing.

Next time it starts with the same choice, except the microphone, which always waits to be asked for.

Across the top are four drop-downs:

- **The vibescape.** Its last row, **VIEW ALL**, opens every vibescape with a picture of each. Switch off the ones you don't want in the list, drag them into your own order, or click one to switch to it.
- **The colour palette.** Five of them, from NEON to MONO.
- **What it's listening to.**
- **Settings:** Full Screen, Keep the Display Awake, CRT Effect (the scanlines and vignette; off gives a brighter picture), Float Above Other Apps, Start at Login, and the folders for your own vibescapes and the log.

The top bar and the level meter slide away when the pointer leaves the window, and come back when it returns. The vibescape's name stays in the bottom-left corner. The **♥ from Minith Labs** button in the bottom-right corner shows the credits.

| Key | Does |
|---|---|
| 1 … 9, 0, then - [ ] ; ' \ , . / ` and ! @ # $ % ^ & * ( ) | Pick a vibescape by its place in the list. The drop-down shows each one's key. |
| ← / → | Previous / next vibescape |
| F11 | Full screen / windowed |
| D | Switch between the PC's sound and the demo tone |
| Esc | Close whatever is open; otherwise quit |
| F12 | Open the log folder |

Turning off Windows' *Animation effects* (Settings → Accessibility → Visual effects) turns off the flash on each beat. `--fullscreen` starts it full screen, and `--palette N` (0–4) starts on another palette for that run.

## Settings and files

- `%APPDATA%\Vibresia\settings.json` holds the choices Vibresia remembers: the vibescape, the audio source, the palette, the list's order and switches, Keep the Display Awake, CRT Effect and Float Above Other Apps. Full Screen isn't remembered, and Start at Login lives in Windows' startup list.
- `%APPDATA%\Vibresia\Vibescapes\` is for your own vibescapes.
- `%LOCALAPPDATA%\Vibresia\Logs\` holds `vibresia.log` (F12 opens it).

Start at Login uses Windows' own startup list, so it also appears in Task Manager under Startup apps. Each time Vibresia opens, it points that entry at the copy you opened, so a newer version unzipped into a different folder takes over by itself.

## Your own vibescapes

A vibescape is a small shader in a `.json` file, in the same format as the Mac app's, so a file copied from a Mac works here. Put the files in `%APPDATA%\Vibresia\Vibescapes\` (**Settings → Open Custom Vibescapes Folder** opens it). Opening the vibescape list rescans the folder, so there is no need to restart.

If one doesn't compile, Vibresia says why, with the line number in your code, and keeps showing the previous vibescape. Switching one off in VIEW ALL is remembered in `settings.json`; your files are never changed. The main [README](../README.md#custom-vibescapes-and-the-local-mcp-server) explains how to write one.

## Problems

Press **F12** to open the log folder, and attach `vibresia.log` to [an issue](https://github.com/sydlawrencedev/vibresia/issues), saying it's about the Windows version. The log records the graphics card, the audio device, and anything that went wrong. It holds no audio and nothing about what was playing.

## How it differs from the Mac app

- **Not here yet:** Auto Scan, The Band and Machine; song identification, genre and track position; MIDI input; the vibescape editor, sharing, the gallery and update checks.
- **Left out on purpose:** in the credits, the coffee button and the share link (a community port shouldn't ask for money on Minith Labs' behalf); Open with Music or Spotify, analytics, and the privacy and terms links (they describe the Mac app).
- **Different on Windows:**
  - A **Settings** drop-down stands in for the Mac's menu bar, and also holds Start at Login and Float Above Other Apps, which the Mac keeps in its credits.
  - The window keeps Windows' title bar.
  - Full screen is a borderless window one pixel taller than the monitor, because one that fits exactly blanked a 4K screen when the pointer moved to another display.
  - Additions: the palette is remembered between launches, Keep the Display Awake and CRT Effect are options, the intro says why a source stopped working, and the keys ← / →, D, F12 and Esc (quit) are new.
  - The list's order and switches live in `settings.json` rather than in your vibescape files. Kaleidoscope is the one vibescape that can't be switched off (on the Mac it's Tunnel, which isn't in this version yet).
  - In VIEW ALL, your own vibescapes can't be edited, copied or deleted from the app; use the folder.
  - With no speakers the demo tone runs silently.
  - A custom vibescape with an empty `id` is skipped (the Mac loads it), and author names aren't links.

## For developers

The port is C# on .NET 10: Silk.NET for the window, OpenGL 3.3 and input; NAudio for Windows audio; and Dear ImGui's draw lists for the interface. The vibescapes are the Mac app's own GLSL shaders, extracted from its Swift sources, so they look the same and stay in step with it.

### Building (Linux or Windows)

Needs the .NET 10 SDK.

```
dotnet build
dotnet publish src/Vibresia.App -c Release -f net10.0-windows -r win-x64 --self-contained -p:PublishSingleFile=true -o out
```

`out/` then holds `Vibresia.exe`, `glfw3.dll` and `cimgui.dll`. Ship all three. The `net10.0` target (no `-windows`) builds without Windows audio and uses a silent demo tone; it is what runs on Linux, and what the tests use.

### Tests

```
dotnet test                       # everything (needs a display, or xvfb-run, for the GL tests)
dotnet test --filter Category!=GL # no GPU or display needed
```

On a headless Linux machine: `xvfb-run -a dotnet test`. This needs Mesa (`libgl1-mesa-dri libegl1 libglx-mesa0`), `xvfb` and `libxinerama1`.

### Shaders

`shaders/` is generated from the macOS sources and is never edited by hand. After pulling changes to the Mac app, run:

```
dotnet run --project tools/Vibresia.ShaderExtract
```

A test fails whenever `shaders/` and the macOS sources disagree. The pictures in VIEW ALL are the website's (`../site/media/modes`), built into the exe.

### Layout

- `src/Vibresia.Core`: audio analysis, tempo, motion clocks, the demo tone, uniforms, the vibescape catalog and loader, palettes, settings, logging, and the interface's rules (layout, when the chrome hides, the meter, the intro, the list's order and switches). No GL and no Windows APIs, so it is all testable on Linux.
- `src/Vibresia.Audio.Windows`: capturing the PC's playback and the microphone, and playing the demo tone (NAudio).
- `src/Vibresia.Render`: the OpenGL window, the renderer, and the interface (Silk.NET, ImGui).
- `src/Vibresia.App`: the executable, which wires the pieces together.
- `tools/Vibresia.ShaderExtract`: pulls the GLSL out of the Swift sources.
