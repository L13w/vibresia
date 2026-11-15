# Vibresia for Windows

An independent Windows port of [Vibresia](../README.md): visual worlds that move with whatever your PC is playing. It is not an official Minith Labs release. The original macOS app and its vibescapes are by Syd Lawrence / Minith Labs, MIT licensed (see `../LICENSE`).

The port runs the macOS app's own GLSL shaders, unchanged, through OpenGL 3.3, and reacts to whatever the PC is playing (WASAPI loopback), the microphone, or a built-in demo tone. Nothing is recorded or sent anywhere: audio is analysed in memory.

## Running it

Download the zip from the fork's releases, unzip it, and run `Vibresia.exe`. Windows SmartScreen will warn about an unsigned app: choose **More info → Run anyway**.

It opens in a window, as the Mac app does. Until something is listening, the Mac's intro asks what to listen to (and says why, if a source stopped working). The header across the top has the Mac's drop-downs: the vibescape, the colour palette, and what to listen to (system audio, microphone or the demo tone, which plays aloud). A **Settings** drop-down holds what the Mac keeps in its menu bar: full screen, keeping the display awake, the CRT effect (a Windows addition), and the custom vibescapes and log folders. The vibescape drop-down ends with **VIEW ALL**, the Mac's panel of every vibescape: switch off the ones you don't want in the list, drag them into your own order, or click a picture to switch to it. The header and the meter on the right slide away when the pointer leaves the window. The **♥ from Minith Labs** button in the bottom-right corner opens the credits, with **Start at login** and **Float above other apps**.

| Key | Does |
|---|---|
| 1 … 9, 0, then - [ ] ; ' \ , . / ` and ! @ # $ % ^ & * ( ) | Pick a vibescape by its place in the list (switched-on ones only), as on the Mac; the drop-down shows each one's key |
| F11 | Full screen / windowed |
| ← / → | Previous / next vibescape |
| D | Switch between this PC's sound and the demo tone |
| Esc | Close an open drop-down, the credits or the View All panel; otherwise quit |
| F12 | Open the log folder (send `vibresia.log` when reporting a problem) |

Vibescapes are drawn at 1× (your display's size before Windows scaling) and stretched to the screen, as the Mac draws them, with the Mac's CRT overlay and beat flash on top; turning off Windows' *Animation effects* turns off the flash.

The last vibescape, audio source and palette, whether to keep the display awake (off unless you turn it on), the CRT effect (on unless you turn it off), the list's order and which vibescapes are switched off, and floating above other apps, are remembered in `%APPDATA%\Vibresia\settings.json`. As on the Mac, a microphone used last time isn't switched on again until you pick it. Start at login is Windows' own startup entry (you'll also see it in Task Manager's Startup apps); each time Vibresia opens it points that entry at the copy you opened, so a new build unzipped to a new folder takes over by itself. `--fullscreen` starts full screen; `--palette N` (0–4) starts on another palette for that run.

## Your own vibescapes

Put vibescape `.json` files (the macOS app's format, so a file copied from a Mac works) in `%APPDATA%\Vibresia\Vibescapes\`. **Settings → Open Custom Vibescapes Folder** opens it. Opening the vibescape drop-down or the View All panel rescans the folder, so there is no need to restart. Switching one off in the panel is remembered in settings.json; your files are never changed. One that doesn't compile shows why (with the line in your code), and the previous vibescape keeps playing.

Not yet the same as the Mac:

- Missing: Auto Scan, The Band and Machine; song identification, genre and track position; MIDI input; the editor, sharing, the gallery and update checks; in the credits, the coffee button and the Proudly Shareware share link (left out by choice: this isn't an official build), Open with Music or Spotify, Identify by listening, analytics, and the privacy and terms links (they describe the Mac app).
- Different on Windows: a **Settings** drop-down stands in for the Mac's menu bar; the window keeps Windows' title bar; full screen is a borderless window one pixel taller than the monitor (an exactly-fitting one blanked a 4K screen when the pointer crossed to another display); keeping the display awake is an option the Mac doesn't have (off by default); the palette is remembered between launches (the Mac starts on NEON each time); **Settings → CRT Effect** turns off the scanlines and vignette for a brighter picture, like the Mac's exported videos (the Mac window always draws them); the intro says why a source failed (the Mac's just comes back); the list's order and switches live in settings.json rather than in your vibescape files, and Kaleidoscope (not Tunnel, which this port doesn't have) is the one that can't be switched off; in the View All panel your own vibescapes can't be edited, copied or deleted (use the folder); with no speakers the demo tone runs silently.
- Port additions: ← / →, D, F12, and Esc to quit.
- Custom vibescapes: a file with an empty `id` is skipped (the Mac loads it); the footer credit and the panel's author names aren't links.

The pointer and keys reach the vibescapes as on the Mac.

## Building (works on Linux or Windows)

Needs the .NET 10 SDK.

```
dotnet build
dotnet publish src/Vibresia.App -c Release -f net10.0-windows -r win-x64 --self-contained -p:PublishSingleFile=true -o out
```

`out/` then holds `Vibresia.exe`, `glfw3.dll` and `cimgui.dll`. Ship all three.

The `net10.0` target (no `-windows`) builds without audio capture and uses a silent demo tone; it is what runs on Linux.

## Tests

```
dotnet test                       # everything (needs a display or xvfb-run for the GL tests)
dotnet test --filter Category!=GL # no GPU or display needed
```

On a headless Linux machine: `xvfb-run -a dotnet test`. This needs Mesa (`libgl1-mesa-dri libegl1 libglx-mesa0`), `xvfb` and `libxinerama1`.

## Shaders

`shaders/` is generated from the macOS sources and is never edited by hand. After pulling upstream changes, run:

```
dotnet run --project tools/Vibresia.ShaderExtract
```

A test fails whenever `shaders/` and the macOS sources disagree.

## Layout

- `src/Vibresia.Core`: audio analysis, tempo, motion clocks, demo tone, uniforms, the vibescape catalog and custom loader, palettes, settings, logging, and the interface's testable rules (layout tiers, when the chrome hides, meter bars, the source pill). No GL and no Windows APIs.
- `src/Vibresia.Audio.Windows`: capture of the PC's playback and the microphone, and the audible demo tone (NAudio).
- `src/Vibresia.Render`: OpenGL window, renderer and the Mac's interface (Silk.NET, ImGui).
- `src/Vibresia.App`: the executable.
- `tools/Vibresia.ShaderExtract`: pulls GLSL out of the Swift sources.
