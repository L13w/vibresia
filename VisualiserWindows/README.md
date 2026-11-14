# Vibresia for Windows

An independent Windows port of [Vibresia](../README.md): visual worlds that move with whatever your PC is playing. It is not an official Minith Labs release. The original macOS app and its vibescapes are by Syd Lawrence / Minith Labs, MIT licensed (see `../LICENSE`).

The port runs the macOS app's own GLSL shaders, unchanged, through OpenGL 3.3, and reacts to whatever the PC is playing (WASAPI loopback). Nothing is recorded or sent anywhere: audio is analysed in memory.

## Running it

Download the zip from the fork's releases, unzip it, and run `Vibresia.exe`. Windows SmartScreen will warn about an unsigned app: choose **More info → Run anyway**.

| Key | Does |
|---|---|
| ← / → | Previous / next vibescape |
| 1 … 9, 0 | Pick one of the first ten vibescapes in the list, as on the Mac |
| Tab | Open the vibescape list (↑/↓ and Enter, or click; Tab or Esc closes it) |
| D | Switch between this PC's sound and the built-in demo tone |
| Esc | Quit |
| F12 | Open the log folder (send `vibresia.log` when reporting a problem) |

Vibescapes are drawn at 1× (your display's size before Windows scaling) and stretched to the screen, as the Mac draws them, with the Mac's CRT overlay and beat flash on top; turning off Windows' *Animation effects* turns off the flash. The display is kept awake while Vibresia runs.

Start it with `--windowed` to get a window instead of full screen. The last vibescape and audio source are remembered in `%APPDATA%\Vibresia\settings.json`.

## Your own vibescapes

Put vibescape `.json` files (the macOS app's format, so a file copied from a Mac works) in `%APPDATA%\Vibresia\Vibescapes\`. The vibescape list has an **Open custom folder** button that opens it. Opening the list rescans the folder, so there is no need to restart. One that doesn't compile shows why (with the line in your code), and the previous vibescape keeps playing.

Not yet the same as the Mac: no Auto Scan, The Band or Machine; no song identification, genre or track position; no MIDI input; no palette picker (everything uses the Mac's default NEON grade); no editor or sharing; the list is a simple one rather than the Mac's menu and panel. The pointer and keys reach the vibescapes as on the Mac.

## Building (works on Linux or Windows)

Needs the .NET 10 SDK.

```
dotnet build
dotnet publish src/Vibresia.App -c Release -f net10.0-windows -r win-x64 --self-contained -p:PublishSingleFile=true -o out
```

`out/` then holds `Vibresia.exe`, `glfw3.dll` and `cimgui.dll`. Ship all three.

The `net10.0` target (no `-windows`) builds without loopback capture and uses the demo tone; it is what runs on Linux.

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

- `src/Vibresia.Core`: audio analysis, tempo, motion clocks, demo tone, uniforms, the vibescape catalog and custom loader, settings, logging. No GL and no Windows APIs.
- `src/Vibresia.Audio.Windows`: loopback capture of the PC's playback (NAudio).
- `src/Vibresia.Render`: OpenGL window, renderer and status overlay (Silk.NET, ImGui).
- `src/Vibresia.App`: the executable.
- `tools/Vibresia.ShaderExtract`: pulls GLSL out of the Swift sources.
