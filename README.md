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
| U | Cycle sprite smoothing: original, 2x, 4x |
| Home | Reset camera |
| B | Build a GP's office (see below) |
| H | Spawn a patient at the cursor |
| G | Spawn ten patients in the hospital |
| Left click | Select a patient, or send the selected one walking |
| R | Toggle random wandering |
| Space | Pause or resume the simulation |
| Esc | Quit |

Building a room goes through the original game's steps:

1. Drag out the blueprint. Drag its edges to resize it or its middle to move it, then press Enter.
2. Click a wall to place the door. Doors and windows never go on a corner tile.
3. Click walls to add windows (click again to remove one), then press Enter.
4. The room is built and the furniture it needs comes to the cursor one piece at a time. Click
   to place, right click to turn. P picks up a placed object to move it.
5. Press Enter to confirm the room once everything is in it.

Esc steps back. In the furniture step it takes the room down again and returns to the door.

Options: `--level LEVEL.L5` starts on a given level, and `--map path/to/file.map` shows
a map file, such as a CorsixTH custom level. `--zoom 0.5` and `--overlay Parcels` set the
initial view. `--upscale 2` (or 4) starts with smoothed sprites. `--capture shot.png` saves
the map view to a PNG after a few seconds and quits, which works even when the screen is locked. `--demo` builds and furnishes a GP's office near the start and adds twenty patients, half of them outside,
wandering into and around the hospital.

### Multiplayer

Host a game on the two-player map `LEVEL.L20` and join it yourself:

```bash
dotnet run --project src/CorsixNet.Client -- --host --demo
```

Join from a second window:

```bash
dotnet run --project src/CorsixNet.Client -- --connect 127.0.0.1
```

`--host PORT` and `--connect HOST:PORT` choose another port (default 7777). `--name` sets
the player name. Hosting accepts only local connections unless you add `--public`. A
headless server is available too:

```bash
dotnet run --project src/CorsixNet.Server -- --level LEVEL.L20
```

Each player's camera starts at their own hospital. The status bar shows the tick, the player
number, and any desync the server detects.

### Sprite viewer

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
