# CorsixNet architecture

CorsixNet is a reimplementation of Bullfrog's Theme Hospital (1997) in C# on .NET,
modelled on [CorsixTH](https://github.com/CorsixTH/CorsixTH). It plays with the
graphics, sounds and levels of an original copy of the game, which the player supplies.

## Goals

- **Faithful reimplementation.** Behaviour matches the original game. Original assets
  are read at runtime from the player's copy and never distributed.
- **Multiplayer.** Several hospitals share one town, as in the original network game
  (player-owned plots, land auctions). This requires a deterministic simulation from the start.
- **Mods.** Content is data. Code mods use a stable API.
- **Compatibility** with CorsixTH custom levels and campaigns (`.level` / `.map`)
  and the original level files.

## Projects

| Project | Role | Depends on |
|---|---|---|
| `CorsixNet.Formats` | Readers for original data files: install detection, RNC, palettes, sprite sheets, fonts, maps, sounds, music, video, strings | — |
| `CorsixNet.Sim` | Deterministic simulation: ticks, RNG, entities, commands, saving, state hashing. No engine dependencies | — |
| `CorsixNet.Content` | Builds simulation state from game data: level maps now, content definitions and mods later | Formats, Sim |
| `CorsixNet.Client` | MonoGame (DesktopGL) front end: isometric renderer, sprite-based UI, audio, input | Content, Formats, Sim |
| `CorsixNet.Analyzers` | Roslyn analyzer enforcing the determinism rules below | — |

Planned, added when their milestone starts: `CorsixNet.Net` (lockstep protocol and transport), `CorsixNet.Server`
(headless dedicated server), `CorsixNet.Tools` (sprite viewer, converters).

The simulation never references the client. The client reads simulation state and
submits commands. This split is what makes headless servers, replays, automated tests
and lockstep multiplayer possible.

## Determinism

Every machine in a game runs the same simulation from the same inputs and must reach
bit-identical state. Projects that set `<CorsixNetDeterministic>true</CorsixNetDeterministic>`
get the analyzer, which turns violations into build errors:

| Rule | Forbids | Use instead |
|---|---|---|
| `CNET0001` | `System.Random`, `DateTime`, `Stopwatch`, `Environment.TickCount`, `Guid.NewGuid`, `HashCode`, threads, tasks, `async`, string or identity `GetHashCode()` | `SimRandom`, simulation ticks, single-threaded tick processing |
| `CNET0002` | `float`, `double`, `Half` | Integers or a fixed-point type; `decimal` is allowed |
| `CNET0003` | Iterating `Dictionary`, `HashSet` and other hash-based collections (`foreach`, LINQ, passing as `IEnumerable`) | Lists ordered by entity ID, `SortedDictionary`, or sort before iterating. Lookups, `Count`, `Any`, `Contains` and `OrderBy` are fine |

Further rules that the analyzer does not check yet:

- No mutable static state in the simulation.
- Sorting needs a total order: break ties by entity ID.
- Entity IDs are assigned sequentially by the simulation.

CI runs the test suite on x64 Windows, Linux and macOS (ARM64). Cross-machine state
hash comparison is added together with the first simulation scenarios.

## Multiplayer model

Deterministic lockstep, as in OpenTTD and Factorio:

1. Clients send **commands** only (build room, hire staff, change a price, pause, change speed).
2. The server stamps each command with the tick it executes on (a few ticks ahead) and
   broadcasts it. Every client executes the same commands on the same ticks.
3. Every N ticks clients exchange a **hash of the simulation state**. A mismatch is a
   desync and produces a diagnostic dump.
4. Joining a running game, and recovering from a desync, means transferring a save.
5. A replay is an initial save plus the command log.

Transport: reliable ordered UDP (LiteNetLib). A Steam transport can be added later.
UI state never enters the simulation.

## Saves

Saves are explicit, versioned serialization of simulation state. CorsixTH saves by
persisting the entire Lua heap, which ties saves to code structure. We avoid that.
The same serializer feeds network joins and state hashing.

## Content and mods

1. **Data.** Rooms, objects, diseases, staff and balance values are definitions loaded
   from data files. The base game is itself the `core` content pack. Nothing gameplay-
   specific is hard-coded in lists.
2. **Levels and campaigns.** Original level files plus CorsixTH `.level` / `.map`.
3. **Code.** C# assemblies loaded through `AssemblyLoadContext`, extending the
   simulation through explicit hooks rather than runtime patching. Mods are expected to
   enable the determinism analyzer. In multiplayer, all players must have the same mod
   set (checked by hash at join). Code mods are trusted code. There is no sandbox, and
   the UI says so.

## Maps and rendering

A level map flows through three layers:

1. `Formats.MapFile` parses the binary map: the original `LEVELS/LEVEL.L*` files and
   CorsixTH `.map` files. It does not interpret the data.
2. `Content.LevelMapLoader` turns it into a `Sim.Maps.LevelMap`, following CorsixTH's
   rules. Block codes become `VBLK-0` sprite numbers. Walls block travel. Pond foliage
   moves to the wall layer. Build flags, parcels, objects, cameras and heliports are read.
3. `LevelMap` is simulation state: floors, walls and tile flags. It also computes wall
   shadows (`UpdateShadows`). Building rooms will change it.

The client's `MapRenderer` draws the map in CorsixTH's order. First every floor and floor
shadow. Then, scanline by scanline, north walls right to left and west walls left to right.
The map is drawn 1:1 into an off-screen target, which is then scaled to the window. This
keeps fractional zoom free of seams between tiles.

Not yet handled: objects placed by the map file (doors, benches; they are collected in
`LoadedLevelMap.Objects`), the original divider walls between parcels for multiplayer, and
the UI layer.

## Original data

`ThemeHospitalInstall` finds the game automatically (common install folders, the EA/Origin
`data\Game` layout, GOG) or uses the `CORSIXNET_TH_PATH` environment variable. File names
are resolved case-insensitively. Tests that need the data skip themselves when it is absent.

Format readers in `CorsixNet.Formats`:

| Type | Reads |
|---|---|
| `Rnc` | RNC method 1 compression, which covers most data files. `ThemeHospitalInstall.ReadDataFile` decompresses transparently |
| `Palette` | 256-colour palettes (`MPALETTE.DAT`, `QDATA/*.PAL`). 6-bit VGA components; magenta and index 255 are transparent |
| `SpriteSheet` | `.TAB` + `.DAT` sprite sheets in the simple (world) or complex (UI) chunk encoding, decoded to `IndexedImage` |
| `IndexedImage.FromRaw` | Uncompressed 640x480 backgrounds (`QDATA/*01V.DAT`) |
| `BitmapFont`, `Cp437` | Bitmap fonts: sprite `i` draws character `31 + i` in the game's code page |

Images stay as palette indices so that the renderer can apply palette effects.
The client converts them to textures.

The sprite viewer (`CorsixNet --sprite-viewer`) browses every sheet and background with
its palette. `CorsixNet --sheet DATA/VBLK-0` opens a given item.

## Roadmap

1. **M0** Solution, CI, determinism analyzer, install detection, `SimRandom`. *(done)*
2. **M1** Format readers (RNC, palettes, sprite sheets, fonts) and a sprite viewer. *(done)*
3. **M2** Level loading, isometric renderer, camera and zoom. *(done)*
4. **M3** Simulation core: tick loop, entities, commands, save/load, state hash. Pathfinding
   ported from CorsixTH; humanoids walking.
5. **M4** Two-client lockstep prototype. This validates the architecture before content grows.
6. **M5** Build mode and a first complete room (GP's office), reception → GP patient flow.
7. **M6+** Content breadth: diseases, staff, finances, events, campaign, AI hospitals, auctions,
   music and video.

## Attribution and clean-room policy

CorsixTH is MIT-licensed. Code and logic ported from it keep its copyright notice.
CorsixTH is the reference for game behaviour. Following CorsixTH's own policy, we do
not disassemble or decompile the original game binaries.
