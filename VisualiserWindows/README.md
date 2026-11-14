# Vibresia for Windows

An independent Windows port of [Vibresia](../README.md): visual worlds that move with whatever your PC is playing. It is not an official Minith Labs release. The original macOS app and its vibescapes are by Syd Lawrence / Minith Labs, MIT licensed (see `../LICENSE`).

The port runs the macOS app's own GLSL shaders, unchanged, through OpenGL 3.3.

## Running it

Download the zip from the fork's releases, unzip it, and run `Vibresia.exe`. Windows SmartScreen will warn about an unsigned app: choose **More info → Run anyway**.

| Key | Does |
|---|---|
| Esc | Quit |
| F12 | Open the log folder (send `vibresia.log` when reporting a problem) |

Start it with `--windowed` to get a window instead of full screen.

## Building (works on Linux or Windows)

Needs the .NET 10 SDK.

```
dotnet build
dotnet publish src/Vibresia.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out
```

`out/` then holds `Vibresia.exe` and `glfw3.dll`. Ship both.

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

- `src/Vibresia.Core`: uniforms, logging, version. No GL and no Windows APIs.
- `src/Vibresia.Render`: OpenGL window and renderer (Silk.NET).
- `src/Vibresia.App`: the executable.
- `tools/Vibresia.ShaderExtract`: pulls GLSL out of the Swift sources.
