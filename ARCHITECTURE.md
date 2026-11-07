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
| `CorsixNet.Net` | Lockstep multiplayer: message protocol, TCP transport, server and client logic | Sim |
| `CorsixNet.Server` | Headless dedicated server | Content, Net |
| `CorsixNet.Client` | MonoGame (DesktopGL) front end: isometric renderer, sprite-based UI, audio, input | Content, Formats, Net, Sim |
| `CorsixNet.Analyzers` | Roslyn analyzer enforcing the determinism rules below | — |

Planned, added when needed: `CorsixNet.Tools` (converters and other offline tools).

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
2. The server stamps each command with the tick it executes on and a sequence number, and
   broadcasts it. Every client executes the same commands on the same ticks.
3. Every N ticks clients report a **hash of the simulation state**. A mismatch is a desync.
4. Joining a running game, and recovering from a desync, means transferring a save.
5. A replay is an initial save plus the command log.

UI state never enters the simulation.

### Implementation (M4)

`CorsixNet.Net` implements this with the **server always ahead**:

- `LockstepServer` runs its own copy of the simulation. When a client's
  `CommandRequestMessage` arrives, the server schedules it (current tick plus
  `CommandDelay`, default 0) and broadcasts a `ScheduledCommandMessage`. After executing a
  tick, it broadcasts `AdvanceMessage(tick)`.
- `LockstepClient` may only run ticks below the last allowed tick. The connection is ordered,
  so every command of a tick arrives before permission to run that tick. Clients never wait
  for missing commands and never roll back.
- **Joining** sends `Hello` with the protocol version. The server replies with `Welcome`:
  the player number, the full `Simulation.Save` output and the allowed tick. Players get the
  lowest free number below the map's player count; a full game rejects the client.
- **Desyncs.** Every 16 ticks the server records its state hash, and clients send theirs.
  A mismatch is logged and broadcast as `DesyncMessage`. The state hash excludes pending
  commands and the sequence counter: they are inputs, and a client behind the server may
  already hold commands for future ticks.
- **Transport** is TCP with length-prefixed frames (`TcpConnection`). Lockstep needs
  reliable ordered delivery anyway, and TCP provides it without dependencies. A UDP
  transport (LiteNetLib) or Steam can be added behind `IConnection` if latency demands it.
  `InMemoryConnection` runs the same code in tests, through the real codec.
- `ServerRunner` drives a server on its own thread at the tick rate. Game time stands still
  while nobody is connected. The client can host in-process (`--host`), or the headless
  `CorsixNet.Server` can host.
- Clients keep running when their window loses focus; only input is ignored.

## Simulation core

`Simulation` holds the complete game state: tick counter, `SimRandom`, the `LevelMap`,
entities and pending commands.

- **Tick.** One tick is one game "hour", as in CorsixTH. At CorsixTH's Normal speed a tick
  lasts 54 ms of real time (about 18.5 ticks per second). The client decides how many ticks
  to run. The simulation knows nothing about real time.
- **Step order.** Commands scheduled for the current tick run first, in sequence order. Then
  every entity updates in id order. Entity ids are assigned sequentially and never reused.
- **Commands** (`Sim.Commands`) are the only input. A `ScheduledCommand` carries the tick,
  a sequence number and the player. For now the commands are debug ones: spawn a patient and
  walk to a tile.
- **Pathfinding** (`Sim.Pathfinding.Pathfinder`) is a faithful port of CorsixTH's A*,
  including the order neighbours are tried and the heap behaviour. Ties between equally
  short paths are therefore broken the same way.
- **Humanoids** walk one tile per 8 ticks, as in CorsixTH. During a step, `Tile` is the tile
  being left; it changes when the step completes. When the map changes under a planned path,
  the humanoid plans again.

Presentation details are derived in the client, not stored in the simulation. Examples are
pixel offsets during a step and which tile a walking humanoid is drawn with. Animation timing
is too: CorsixTH restarts an animation only when it changes, so the 6-frame walk cycle runs on
across 8-tick steps.

## Content definitions and rooms

`Sim.Definitions.GameContent` holds the definitions the rules refer to by id. For now that
is room types (`RoomDefinition`: minimum size, floor sprite, wall set) and the sprites of
the hospital's outer walls. The simulation receives it at construction and on load. It is
not part of the saved state, so every machine in a game must use the same content; checking
this when joining is still to do. `Content.BaseGameContent` describes the original game, in
code for now, taken from CorsixTH's `rooms/*.lua` and `walls/*.lua`.

Building a room is a command, `BuildRoomCommand(RoomPlan)`. A plan is a room type, an area
and a door placement on the area's perimeter. `RoomBuilder.Check` applies CorsixTH's rules:

- at least the minimum size;
- only buildable hospital tiles, on a parcel the player owns, outside other rooms and with no
  walls inside the area;
- a door on the perimeter, not cut into an outer wall or a wall outside the room, and opening
  onto a passable hospital tile.

The client's build tool calls the same check for its preview. Building follows CorsixTH's
`UIEditRoom:_finishRoomFoundation`:

- inside walls on the north and west sides, outside walls on the south and east sides;
- outer walls and walls of neighbouring rooms stay;
- the room's floor and id go on its tiles;
- a `RoomDoor` entity opens the wall at the door;
- travel flags and shadows are recomputed (`LevelMap.UpdatePathfinding`, `UpdateShadows`).

## Saves

Saves are explicit, versioned serialization of simulation state. CorsixTH saves by
persisting the entire Lua heap, which ties saves to code structure. We avoid that.
The same serializer feeds network joins and state hashing.

`Simulation.Save` and `Simulation.Load` write and read the whole state through
`SimWriter`/`SimReader`: a fixed little-endian layout with a magic number and format version.
`ComputeStateHash` streams the same bytes into a 64-bit FNV-1a hash without buffering them.
Tests check that a saved and reloaded simulation continues to produce identical hashes.

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

Objects placed by map files are only entrance doors in practice. The original levels contain
nothing else, and CorsixTH ignores other types. `LoadedLevelMap.StartSimulation` places
them. As in CorsixTH, parcel N starts out owned by player N (if that player exists).
Objects on parcels without an owner wait in `Simulation.DelayedMapObjects` until
`SetParcelOwner` gives the parcel to someone.

An `EntranceDoor` is half of a sliding door pair. The right half counts people stepping
onto its tile and the tile in front of it, and passes the count to the left half. A step
reports the move when it starts, as in CorsixTH's walk action. Which frame a door shows while
sliding is tracked by the client.

Not yet handled: the original divider walls between parcels for multiplayer, and the UI
layer.

**Optional sprite smoothing.** `Formats.PixelArtScaler` ports MMPX, a pixel art
magnification filter. It smooths diagonals but keeps straight edges, corners and the original
palette, because it only copies existing pixels. The client can store sprites enlarged 2x or
4x (`SpriteAtlas.Scale`, key U, `--upscale`). It then draws the map into a correspondingly
larger buffer. World coordinates stay in original pixels.

Floor tiles cannot be filtered one by one: the filter rounds off a tile's edge into the
transparency around it, which leaves seams between tiles. `ScaleTile` therefore surrounds a
floor tile with copies of itself along the isometric lattice before filtering. It then cuts
the result back to the tile's outline, enlarged pixel for pixel, so neighbouring tiles fit
exactly as the originals do. The original pixels remain the default, since the project is a
faithful reimplementation.

Animations come from `Formats.AnimationSet` (`VSTART-1`, `VFRA-1`, `VLIST-1`, `VELE-1` with
the `VSPR-0` sprites). A frame is a list of sprite elements. Elements belong to layers, and
the humanoid's chosen variant per layer (head, clothes and so on) selects which ones are
drawn. Entities are drawn in the second map pass, after the west wall of the tile they are
attached to.

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
   ported from CorsixTH; humanoids walking; entrance doors from map files. *(done)*
5. **M4** Two-client lockstep prototype. This validates the architecture before content grows. *(done)*
6. **M5** Build mode and a first complete room (GP's office), reception → GP patient flow.
   *(in progress: room building done; objects, staff and patient flow to come)*
7. **M6+** Content breadth: diseases, staff, finances, events, campaign, AI hospitals, auctions,
   music and video.

## Attribution and clean-room policy

CorsixTH is MIT-licensed. Code and logic ported from it keep its copyright notice.
CorsixTH is the reference for game behaviour. Following CorsixTH's own policy, we do
not disassemble or decompile the original game binaries.
