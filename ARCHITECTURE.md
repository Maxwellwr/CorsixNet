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
- **Calendar.** `Simulation.Date` (`Sim.Calendar.GameDate`, a port of CorsixTH's `date.lua`) follows
  from the tick: days of 50 hours, months of the usual lengths, no leap years, the game starting at
  hour 0 of 1 January of year 1. It needs no saved state of its own.
- **Step order.** Commands scheduled for the current tick run first, in sequence order. If the
  tick is the last hour of a day, the end of the day follows, as in CorsixTH's `World:onTick`: at
  the end of a month every hospital pays its wages, then the candidates for hire are replaced. Then
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

Building a room is a command, `BuildRoomCommand(RoomPlan)`. A plan is a room type, an area,
a door placement on the area's perimeter and optional windows. `RoomBuilder.Check` applies
CorsixTH's rules:

- at least the minimum size;
- only buildable hospital tiles, on a parcel the player owns, outside other rooms and with no
  walls inside the area;
- a door on the perimeter, not cut into an outer wall or a wall outside the room, and opening
  onto a passable hospital tile;
- windows on the perimeter where no wall stands yet, not on the door;
- no door or window on a corner tile of the room. `RoomBuilder.WallNear` picks the wall under
  the cursor the way CorsixTH's `screenToWall` does, stepping off corners.

The client's build tool calls the same check for its preview. Building follows CorsixTH's
`UIEditRoom:_finishRoomFoundation`:

- inside walls on the north and west sides, outside walls on the south and east sides;
- outer walls and walls of neighbouring rooms stay;
- the room's floor and id go on its tiles;
- window sprites replace the walls where windows go; a neighbour's window stays a window;
- a `RoomDoor` entity opens the wall at the door;
- travel flags and shadows are recomputed (`LevelMap.UpdatePathfinding`, `UpdateShadows`).

A new room is inactive, like a CorsixTH room while its edit dialog is open. Its owner furnishes
it with `PlaceObjectCommand` and `PickUpObjectCommand`, then sends `ConfirmRoomCommand`, which
activates the room once all its needed objects stand in it. `CancelRoomCommand` takes an
inactive room down as CorsixTH's `removeRoom` does: objects and door go, walls come down
(outer walls stay, and a wall shared with a neighbouring room becomes that room's wall again),
and the tiles get back the floor and flags the room saved when it was built.

The client's `BuildRoomTool` follows the original's steps: blueprint (resize by dragging an
edge, move by dragging the middle), door, windows, then furniture. The blueprint, door and
windows stay in the client; the room only reaches the simulation when the player is done with
them. `RoomBlueprint` and `BlueprintDrawing` draw the blueprint as the original does, with
the game's own sprites: blue floor tiles (red where a tile cannot be built on) and a blue
wireframe wall, into which the door and windows are drawn and marked on the floor. The red
parts are the blue sprites recoloured through table 6 of `QData/Ghost1.dat` (`PaletteRemaps`),
CorsixTH's alternative palette for them. While building, the tile under the cursor has a red
outline. In the furniture step the needed objects come to the cursor one at a time. An object's
anchor tile is always the tile under the cursor, so it follows the cursor tile by tile, and it is
drawn grey where it cannot go. Commands take a few ticks to arrive, so the tool keeps track of the ones it
has sent until the simulation shows their effect.

## Objects

`ObjectDefinition` gives each object type a footprint per facing direction, taken from
CorsixTH's `objects/*.lua`. The footprint is a list of tiles relative to the placement tile,
and each tile has flags:

- **complete cell:** the object fills the tile, so all four sides must be free;
- **use position:** the tile stays passable, and someone stands there to use the object;
- **need side:** the object needs that side of the tile free;
- **shareable:** another object's use position may overlap the tile.

The render attach offset says which tile's drawing pass draws the object, which decides what it
overlaps. The animation itself is still drawn from the placement tile; CorsixTH does the same by
shifting the animation back by the attach tile's screen offset.

`PlaceObjectCommand` places an object if `ObjectPlacer.Check` allows it:

- the object must fit its place: a room under construction whose type lists it, or a corridor
  for corridor objects;
- every footprint tile must be in that room, on the player's land, and buildable (passable for
  use positions);
- the sides the footprint covers must still be buildable;
- a use position must not overlap another object's use position or a door's walkway;
- every use position in the room must stay reachable from the door, checked by a flood fill on
  a provisional placement.

Placing marks the footprint as CorsixTH's `occupyTilesByObjectFootprintAt` does. Room doors
keep the tiles on both sides clear. Picking an object up recomputes the room's tiles from the
saved originals and the remaining objects. The client previews with the same check and draws
objects with their idle animations, mirroring east and north for south and west.

## Money

Each player has a `Hospital` with a balance and a log of its latest transactions, as in
CorsixTH's `hospital.lua`. What things cost depends on the level, so the prices travel with the
saved state as `LevelRules`: starting cash, each room type's own cost and each object type's
cost. `LevelRulesLoader` reads them from the original's level files, as CorsixTH does: the
difficulty's base file (`FULL00.SAM`, medium difficulty), then the level's own file
(`FULL01.SAM` for `LEVEL.L1`). The format (`#towns[1].StartCash.IllRate 40000 2`) is read by
`LevelConfig`. Rooms find their price through their number in those files (`LevelConfigId`, 7
for the GP's office), objects through their object type. Without level files everything is free.

`RoomPurchases` charges for rooms as the original does:

- a room can only be started with its whole cost (room plus needed objects) in the bank;
  `BuildRoomCommand` checks this when it executes;
- the needed objects are bought when the room is built, the moment they come to the cursor;
- confirming the room pays for the room itself;
- taking down an unfinished room sells its objects back at full price.

Otherwise the balance may go below zero.

## Staff

Staff follow CorsixTH's `staff_profile.lua`. A `StaffProfile` has a class (`StaffClass`: nurse,
doctor, handyman, receptionist, numbered as the level files' `staff[n]`), a skill, a doctor's
abilities (psychiatrist, surgeon, researcher; junior and consultant follow from the skill), a wage
and looks (face parts for the staff window, animation layer 5 for a doctor's skin colour). CorsixTH
keeps skill as a number from 0 to 1; here it is in thousandths, like the thresholds in the level
files, so the simulation needs no floating point. Names and descriptions come from the language
files and are left to the client.

The staff settings travel in `LevelRules.Staff` (`StaffRules`): minimum salaries, the salary
additions per ability (`gbv.SalaryAdd[3..8]`), `gbv.SalaryAbilityDivisor`, the junior and
consultant thresholds, and the `staff_levels` entries, which say from which month how many people of
each class are for hire and how often a doctor has each ability. An entry may leave values out; they
come from the latest earlier entry. `LevelRulesLoader` reads them on top of CorsixTH's
base_config.lua defaults. Without level files (`LevelRules.Free`) staff work for nothing, as in
CorsixTH's free build mode.

- A doctor's abilities are rolled "one in n" from the entry for the month; a junior's skill is put
  below the junior threshold, a consultant's above the consultant threshold, everyone else's between.
- The wage is the class's minimum salary plus skill divided by `SalaryAbilityDivisor`, plus for a
  doctor the addition of each ability (or the plain doctor's addition), never below the minimum.
- The candidates for hire (`Simulation.AvailableStaff`) are made when the game starts, as CorsixTH's
  `makeAvailableStaff(0)`, and made anew at the end of every month for the month that ends
  (`GameDate.MonthOfGame`, 1 at the end of January), as `World:onEndMonth` does. As in CorsixTH,
  all hospitals hire from the same list.
- At the end of every month each hospital pays the wages of all its staff in one transaction
  (`TransactionKind.Wages`), as `Hospital:onEndMonth` does; nothing is logged when there is no
  wage to pay.
- `HireStaffCommand(class, index, tile)` hires a candidate if the hospital has a month's salary in
  the bank and `StaffHiring.CheckPlacement` accepts the tile: a passable tile in the player's
  hospital, in a room only if its door can be reached, and never in a room for receptionists. Hiring
  pays the first month's salary (`TransactionKind.HireStaff`). The new member of staff is a
  `Humanoid` with the profile and the employing player. In CorsixTH choosing and placing are two
  steps and cancelling puts the candidate back at the end of the list; here they are one command.

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
| `LevelConfig` | Level settings (`LEVELS/*.SAM`, CorsixTH `.level` files) as flat keys such as `towns[1].StartCash` |
| `PaletteRemaps` | Colour remap tables (`QDATA/GHOST*.DAT`), for example the red room blueprint |

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
   *(in progress: room building in the original's steps (blueprint, door, windows, furniture,
   confirm), paying for rooms and hiring staff done; reception desk and patient flow to come)*
7. **M6+** Content breadth: diseases, staff, finances, events, campaign, AI hospitals, auctions,
   music and video.

## Attribution and clean-room policy

CorsixTH is MIT-licensed. Code and logic ported from it keep its copyright notice.
CorsixTH is the reference for game behaviour. Following CorsixTH's own policy, we do
not disassemble or decompile the original game binaries.
