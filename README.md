# CorsixNet

A reimplementation of Theme Hospital in C# / .NET, modelled on
[CorsixTH](https://github.com/CorsixTH/CorsixTH), with deterministic lockstep
multiplayer and mod support as design goals. It needs the data files from an
original copy of the game (CD, GOG or EA).

Early development: see [ARCHITECTURE.md](ARCHITECTURE.md) for the design and roadmap.

## Building

Requires the .NET 10 SDK.

```bash
dotnet build
dotnet test
```

Tests that read original game data are skipped unless the game is found. Detection
checks common install folders. Set `CORSIXNET_TH_PATH` to point at a copy explicitly.

## Running

```bash
dotnet run --project src/CorsixNet.Client
```

Optionally pass the game folder as an argument. The client opens the map view on the
first original level:

| Key | Action |
|---|---|
| Arrows / WASD, right or middle drag | Pan |
| Mouse wheel | Zoom |
| PgUp / PgDn | Previous / next level |
| O | Cycle overlay (passable, hospital, parcels) |
| Home | Reset camera |
| H | Spawn a patient at the cursor |
| G | Spawn ten patients in the hospital |
| Left click | Select a patient, or send the selected one walking |
| R | Toggle random wandering |
| Space | Pause or resume the simulation |
| Esc | Quit |

Options: `--level LEVEL.L5` starts on a given level, and `--map path/to/file.map` shows
a map file, such as a CorsixTH custom level. `--zoom 0.5` and `--overlay Parcels` set the
initial view. `--demo` starts with twenty patients wandering around the hospital.

`--sprite-viewer` browses every sprite sheet and background in the game data instead:

| Key | Action |
|---|---|
| Left / Right | Previous / next sheet |
| Mouse wheel, Up / Down, PgUp / PgDn | Scroll |
| + / - | Zoom |
| P / Shift+P | Cycle palette |
| C | Toggle chunk encoding |
| Esc | Quit |

`--sheet QDATA/BANK02V` opens the viewer on a given sheet.

## License

[MIT](LICENSE). Parts are ported from CorsixTH; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Theme Hospital is a trademark of Electronic Arts; CorsixNet is not
affiliated with or endorsed by EA or Bullfrog. Original game data is not included.
