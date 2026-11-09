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
  to run: a local game follows CorsixTH's game speeds (`GameClock` in the client, from Slowest,
  one tick per 56 frames of 18 ms, to Speed up, eight ticks per frame). A network game runs at
  the server's Normal speed for now. The simulation knows nothing about real time.
- **Calendar.** `Simulation.Date` (`Sim.Calendar.GameDate`, a port of CorsixTH's `date.lua`) follows
  from `Simulation.Hours`, the tick plus the hours the end-of-month and end-of-year cheats skipped
  (`SkippedHours`, saved): days of 50 hours, months of the usual lengths, no leap years, the game
  starting at hour 0 of 1 January of year 1. Transactions are stamped with that hour; the tick itself
  only counts steps, so commands and timers in ticks are not affected by a jump.
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
- **Room doors.** A step across a room door's edge is CorsixTH's `navigateDoor` (`walk.lua`): the
  humanoid stands on the door's tile while its door animation (`GameContent.FindDoorAnimations`,
  `walk_anims` DoorL/DoorE) plays, the door itself hidden, and comes out on the far side when it
  ends. Going north or west uses the leaving animation, east or south the entering one, mirrored
  for west and south. One humanoid goes through a door at a time; others wait on their side.
  Humanoids without door animations (receptionist, undressed patients) walk through plainly.
  Door queues, knocking and swing doors are not ported yet.
- **Wandering.** `MeanderCommand` ports `meander.lua`: the humanoid walks to one of the 24 nearest
  idle tiles (`Pathfinder.TryFindIdleTile`), whose search does not go through doors, so wandering
  people keep out of rooms. The demo's walkers use it.

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

Before walls go up, the site is cleared of people, as in CorsixTH's `UIEditRoom` (`clearArea`).
When the player goes from the blueprint to its door, `MarkBlueprintCommand` starts the player's
`ConstructionSite`: the blueprint's tiles get `TileFlags.AvoidTile` and everyone on them, or
walking into them from the wall tiles around, is told to leave (`Humanoid.LeaveArea`): people in a
queue are sent to their places again, anyone else walks to a nearby idle tile and, if they were on
their way somewhere, goes on there afterwards. Avoided tiles cost 128 in the pathfinder, are never
idle tiles, and someone about to walk onto one from outside plans their way again. Going back to the
blueprint sends `UnmarkBlueprintCommand`. `BuildRoomCommand` completes the site's plan: the room is
built at once if those told to leave are gone and nobody else is in the way, otherwise the site
looks again after an hour and then every ten hours, and builds the room (buying its objects) once
everyone it watches has left. Like CorsixTH, it does not look for people who walked in meanwhile.

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
- **shareable:** another object's use position may overlap the tile;
- **only side:** a side object (radiator, fire extinguisher, bin) stands against the side of the
  tile it faces and leaves the tile free. The bin facing east stands against the west side, as in
  CorsixTH.

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
- only one object per tile, and none on a tile someone stands on to use an object (unless that
  tile is shareable) or on a door's walkway (CorsixTH's `isTileExclusivelyPassable`);
- a side object needs its side buildable;
- every use position in the room must stay reachable from the door, checked by a flood fill on
  a provisional placement;
- outside rooms, the object must not cut the corridor: the passable tiles around its solid part
  must still reach each other with it in place (`wouldNonSideObjectBreakPathfindingIfSpawnedAt`),
  and a side object on an edge without a wall must leave another way from one side of the edge to
  the other.

Placing marks the footprint as CorsixTH's `occupyTilesByObjectFootprintAt` does. A side object
only takes its side; on an edge without a wall it also closes the edge to walking, kept in the
tile flags `BlockedNorth` and `BlockedWest` so that recomputing travel flags keeps it closed. Room doors
keep the tiles on both sides clear. Picking an object up recomputes the room's tiles from the
saved originals and the remaining objects. The client previews with the same check and draws
objects with their idle animations; a facing without its own animation mirrors its mirror facing
(north and west, east and south), as CorsixTH's `orient_mirror`. The corridor objects are the
reception desk, bench, drinks machine, fire extinguisher, radiator, bin and plant; using them
(benches, drinks, watering plants) is not ported yet. A plant (`RoomObject.Plant`, `PlantState`, from
`plant.lua`) droops a state every 64 days, sooner where it is warm (each day counts 1 plus the tile's temperature),
from healthy to dead in five states; the client draws the idle frame plus the state. Nobody waters it yet.
Litter (`Litter`, an entity of its own, from `objects/litter.lua`) lies on a tile, at most one piece per tile: a
higher ranked kind (puke 4, dead rat 3, pee 2, rubbish 1, soot 99) replaces a lower one, otherwise the new piece is
not dropped (`World:newLitter`). Patients drop rubbish (`Littering`, from `Patient:setTile`): on their first floor
tile of the building they get a countdown of 20 to 100 tiles, each step at the hospital counts it down, and at 0, on a
free floor tile of their hospital outside rooms, they drop a can, banana, paper or bottle (one time in 20 if a bin is
within 8 steps by CorsixTH's object search) and start again at 30 to 150. Placing an object or building a room clears
the cleanable litter from its tiles. Nobody sweeps it up yet.
Rats (`Rats`, `Rat`, from `hospital.lua` and `entities/rat.lua`) come with litter. Each day a hospital wants a rat
hole (`Hospital.Ratholes`) for each half percent of its corridor tiles with litter and tries up to 12 random tiles for
one more in a corridor wall; then, unless it has a rat per 200 tiles, a rat comes with a chance of 5 % plus half the
littered fraction (a clean hospital first gets the two holes a run needs). The rat comes out of a random hole (north
holes play a leaving animation), runs a tile every 4 ticks along the corridors to another hole and goes in.
`ShootRatCommand` shoots it (X in the client, for the rat by the cursor) and leaves a dead rat. Building over a hole
closes it. Only holes in north and west walls are drawn.

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
  hospital, in a room only if its door can be reached, and never in a room for receptionists, who may
  instead be put straight onto a reception desk without a receptionist, which they then staff. Hiring
  pays the first month's salary (`TransactionKind.HireStaff`). The new member of staff is a
  `Humanoid` with the profile and the employing player. In CorsixTH choosing and placing are two
  steps and cancelling puts the candidate back at the end of the list; here they are one command.

## Patients and their visit

What humanoids do beyond walking lives in `Sim.People`. CorsixTH strings humanoid actions together in
an action queue of Lua closures; here each kind of humanoid has a small state machine
(`HumanoidBehaviour`: `PatientBehaviour`, `ReceptionistBehaviour`, `DoctorBehaviour`, `NurseBehaviour`), which is easy to
save explicitly. A behaviour runs on every tick in which its humanoid is not walking.

- **Arrivals** (`PatientArrivals`, from `world.lua`). The spawn rate starts at the level's
  `popn[0].Change` patients a month and changes at the end of each month by the `popn` entry for that
  month. The month's patients are spread over its days with Poisson-distributed gaps (`math.p_random`,
  done with `decimal` here to stay deterministic); at the end of each day the next day's patients get
  random hours. A patient only arrives at a hospital with a staffed reception desk, at one of up to 8
  spawn points per map edge from which the hospital can be reached; the first one by the shortest way
  to a desk. As in CorsixTH (`World:chooseHospitalForPatient`) the hospitals take turns by
  `Hospital.SpawnAttempts`, a hospital not yet open passing its turn, and patients come only once the first
  hospital has opened. The level's computer opponents (`computer[n].Playing`, CorsixTH's `AIHospital`) are
  `Simulation.Competitors`: not on the map and only names so far, they take their turns after the players'
  hospitals and count the patients sent to them as visitors (`CompetitorHospital`), so that with three of
  them a player gets a quarter of the patients, as in CorsixTH. Each `Hospital` runs CorsixTH's opening
  watch (`UIWatch` "initial_opening"), 13 segments of 385 hours, and opens an hour after it has run
  down, 5006 hours (about 100 days) into the game. `OpenHospitalCommand` opens it early, as the watch's button does; the client's O
  key stands in for that button and the date box shows the days left. `--demo` opens at once.
- **Reception desk** (`ReceptionDesks`, from `objects/reception_desk.lua`). A corridor object, bought
  when it is placed (`PlaceObjectCommand` charges corridor objects; the hospital needs the price in the
  bank). A desk without staff sends for the nearest receptionist without a desk; they walk to the
  desk's secondary use position and step onto its tile. Patients queue at the use position. The first
  in line is seen once they have stood there 4 hours plus up to a day, less the more skilled the
  receptionist (`4 + 50 * (1 - skill)`).
- **Queues** (`HumanoidQueue`, from `queue.lua` and the queue action). The n-th in line stands on the
  n-th tile of `Pathfinder.TryFindIdleTile`, a port of CorsixTH's `find_idle_tile`, which yields a line
  of neighbouring tiles that keeps off door walkways (`TileFlags.DoNotIdle`), and faces the one ahead.
  No priorities and no benches yet.
- **Seeking a desk or a room** scores each candidate by path distance plus ten tiles per person queueing
  or on the way (a room with its staff in gets 50 tiles off, a full queue 1000 on). A patient joins a
  desk's queue once within 6 tiles of it; further away they walk towards it until they are.
- **The GP's office** (`RoomVisits`, `DoctorBehaviour`, from `room.lua` and `rooms/gp.lua`). An active
  room without staff sends for the nearest free doctor (CorsixTH's call dispatcher is not ported). The
  doctor goes back and forth between the cabinet and the desk; at the desk they stay for
  `random(3 + 5 * (1 - skill), 8 + 10 * (1 - skill))` rounds of the desk animation (consultants
  longer). Patients queue at the door and go in one at a time once the doctor is in. A patient sits on
  the chair; each time the doctor's desk time runs out, they conclude with probability
  `0.7 + 0.3 * skill`, at the latest after three failures, and the patient leaves (see below).
- **Diseases** (`Sim.Diseases`, from `diseases/*.lua`, `diagnosis/*.lua` and `world.lua`). `DiseaseCatalog`
  holds every disease and every diagnosis room's pseudo disease (`diag_gp`, `diag_x_ray`, ...), which is
  what a patient pays for before they are diagnosed. `DiseaseRules`, part of `LevelRules`, comes from the
  level files: each disease's `expertise[n]` price, `Known` and `MaxDiagDiff`, how common it is
  (`visuals`, `non_visuals`; 0 leaves it out) and from which month it comes (`visuals_available`,
  `non_visuals_available`, `gbv.HoldVisualMonths`, `gbv.HoldVisualPeepCount`). A new patient gets one of the
  level's diseases at random (`World:getNewPatientDisease`), looks as it makes them (only bloaty heads differ
  so far; the other visual diseases need humanoid types the client cannot draw yet) and pays through one of
  three insurance companies one time in four.
- **Diagnosis** (`PatientCare`, from `patient.lua` and `rooms/gp.lua`). Progress is in thousandths, worked
  out with decimals: each GP visit adds `0.4 * (1 - MaxDiagDiff)` plus `(attention / d + 0.4) * skill / d`
  times `1.5 − fatigue`, `d` a random 1 to 3, very skilled doctors up to five times as much.
  At the hospital's `stop_procedure` policy (1.0 by default; progress goes no further) the patient is diagnosed, the hospital knows the disease, and
  they look for its treatment rooms; otherwise the GP sends them to one of the disease's diagnosis rooms.
  Only the GP's office, general diagnosis and the pharmacy work so far (`RoomVisits.IsSupported`), so other diagnosis rooms are never
  found: as in CorsixTH's `seek_room.lua`, a patient below the `send_home` policy (0.1) goes home, one below
  `guess_cure` (0.9) or with a disease the hospital does not know waits, and one above has the disease guessed. Waiting
  patients step off to a free spot and give up after 60 days; when a room of the kind they wait for starts
  working, they go to it (`RoomVisits.OnRoomActivated`). CorsixTH's faxes asking the player what to do are not
  there yet. A doctor of middling skill and little attention to detail gets level one's patients only about
  three quarters of the way, so without general diagnosis many of them wait.
- **General diagnosis** (`PatientBehaviour.GeneralDiagnosisStep`, from `rooms/general_diag.lua` and
  `use_screen.lua`). The room sends for a free doctor, who waits at the crash trolley's secondary use position
  (CorsixTH's doctor meanders meanwhile). The patient undresses behind the screen (the screen's animation shows them;
  they become a stripped patient, `Humanoid.Type` changes, and their clothes hang on the screen,
  `RoomObject.HangingClothes`), lies down on the trolley (the trolley's multi-use with the doctor, who is hidden; one
  of three animation variants), walks back and dresses. Then the room is done with them: they get further in the
  diagnosis, pay for it and, if they agree to the GP's price, go back to the GP, whose next visit often completes
  the diagnosis. Without the doctor they dress, leave and look for the room again. CorsixTH draws the trolley split
  over two tiles; here it is drawn from its own tile, so during the examination its top can show over the wall.
- **The pharmacy** (`NurseBehaviour`, from `rooms/pharmacy.lua`). The room sends for the nearest free nurse
  (each supported room type names the kind of staff it needs and the object a patient is seen at). She waits
  at the cabinet's use position (CorsixTH's nurse meanders about the room meanwhile). The patient walks to the
  cabinet's secondary use position and faces her; she then uses the cabinet with them (see "Using objects")
  with a random flask colour on layer 3, and the room is done with the patient.
- **Treatment** (`PatientCare.DealtWith`, `TreatDisease`, from `room.lua` and `patient.lua`). After a treatment
  room the patient goes on to the disease's next treatment room, or after the last one is treated: they pay what
  they agreed to at the GP, and the cure works if `effectiveness × diagnosis progress`, moved by up to a tenth of
  what is left to 100 (at least 20) by the staff's service quality (`0.7 × skill + 0.2 × (1 − fatigue) + 0.1 ×
  happiness`), reaches a random 1 to 100. A cured patient goes home and counts
  in the hospital's `Cured` and the disease's `Recoveries`; otherwise the patient dies (`Deaths`, `Fatalities`) and,
  as dying is not animated yet, vanishes outside the room. Either way the hospital pays for the drug.
- **Payment** (`Hospital`, from `hospital.lua`). Before each room the patient agrees to pay its current price
  (always at or below the standard price, with chance `e^(-4 * overprice)` above it) and pays it when the
  staff is done: into the bank as a `Treatment` transaction, or, if insured, owed by the insurance company,
  which pays two months later at a month's end. The price is the level's `StartPrice` (a pseudo disease's
  own price) with 4.5 % inflation a year, times the player's percentage in the casebook (50–200 %, set with
  `SetTreatmentPriceCommand` for a known disease or diagnosis; building a diagnosis room makes its diagnosis
  known, as CorsixTH's `World:markRoomAsBuilt` does), and with a reputation above 500 times reputation / 500.
  A disease cured in the pharmacy has the level's drug rating as its cure effectiveness and costs the drug's
  price (`gbv.StartRating` and `gbv.StartCost`, both 100) for each cure (`TransactionKind.DrugCost`, paid to one
  of five drug companies at random); research improving them is not there yet.
- **Reputation** (`Hospital`, from `hospital.lua`). The hospital starts at the level's `StartRep` (500) and each
  non-pseudo disease at 500. A cure adds 1, a death takes 4, a patient sent home or giving up waiting 3
  (`Kicked`, also counted in the disease's `TurnedAway`), one refusing a price 2 (`OverPriced`); both count in
  `NotCured`. The disease's reputation always changes; the hospital's always for a move towards 500 and
  otherwise with a chance that falls to 0 at 0 and 1000. A patient paying themselves now and then finds a price
  far below or above what they expect from the reputation and the cure's effectiveness (+1 or −2).
- **Value and year end** (`Hospital`, from `hospital.lua` and `annual_report.lua`). The hospital is worth its
  parcel's tiles at `gbv.LandCostPerTile` (25) and 20,000 at the start; what is paid for rooms and objects adds
  to it and what objects are sold for comes off it. At each year end the year is judged as CorsixTH's annual
  report does, against the level's `awards_trophies` settings (`AwardRules`, defaults from base_config.lua):
  trophies (`TransactionKind.TrophyBonus`), awards and fines (`AwardBonus`, which may be negative) and
  reputation for the hospital's value; then the year's counts start afresh and prices rise. The results stay
  in `Hospital.LastYearAwards` until the annual report screen exists.
- **Loans** (`Hospital`, from `hospital.lua` and `bank_manager.lua`). A hospital borrows and pays back in steps
  of 5,000 (`BorrowCommand`, `RepayCommand`) up to a third of its value, rounded down to 5,000, plus 10,000.
  Each day runs up the loan's interest at the level's `InterestRate` (hundredths of a percent a year, 365 days)
  and, below zero, the overdraft's at that plus `OverdraftDiff`; both are paid at the month's end after the
  wages, rounded, unless they round to nothing. A hospital with a loan does not win.
- **Land** (`Simulation.PurchaseParcel`, from `hospital.lua` and `th_map.cpp`). A parcel nobody owns may be
  bought when it touches the outside or the buyer's land by passable tiles of the map as the level began; it
  costs its tiles at `gbv.LandCostPerTile` and adds that to the hospital's value (`PurchaseParcelCommand`).
- **Heating** (`Hospital`, from `hospital.lua` and `town_map.lua`). The radiators' heat is set in tenths from 1 to
  10 (`SetRadiatorHeatCommand`, 5 at the start). Each day runs up heat × 10 × radiators × 7.5 over the month, paid
  at its end after the wages (`TransactionKind.Heating`). Every 200 days or so (medium difficulty) comes a chance
  of a disaster: an open hospital with radiators and fewer than one handyman per 8 of them may have its boiler
  break, stuck at full or no heat, until 10–30 repairs are done (1 to 3 a day, the more handymen the more).
- **Warmth and happiness** (`Humanoid.Warmth`, `Humanoid.Happiness`, `Feelings`, from `humanoid.lua` and
  `staff.lua`). Everyone starts at warmth 0.29 and happiness 1. At the end of each day after the hospitals, a
  person's warmth moves a quarter towards their tile's temperature; below 0.22 (11 °C) or above 0.36 (18 °C)
  happiness drops by a tenth of the gap, otherwise it rises by 0.005. Staff also gain or lose 5 % of how far their
  wage is from the fair wage, and a little for a plant, extinguisher, bin, bookcase, skeleton or TV within two
  tiles in the same room, and in an active room a thousandth per step of `round(ln windows)` (doubled in a staff
  room; only the external wall's windows count, as in CorsixTH) and of `round(ln area / smallest area)`.
  Receptionists and staff on `payroll.MaxSalary` (2000) are always happy. A patient paying
  loses half the price distortion. Happiness feeds the treatment's service quality and the happiness goals.
- **Health** (`PatientBehaviour.Health`, from `patient.lua`). A patient comes with health from 0.80 to 1.00 and
  loses 0.004 each day after warmth and happiness; a cure sets it to 1. While it is from 0.18 to below 0.22, each
  day costs 0.0002 happiness and one time in 30 the patient gives up and goes home (counted as sent home); CorsixTH
  lets a patient in a room other than their cure room give up too, here only those outside rooms do. Below 0.01 it
  becomes 0, and the next day at 0 the patient is set to die: once out of rooms and doorways they die, count as a
  death of their disease and vanish. Outside rooms, patients also like an extinguisher within two tiles (0.0002),
  a plant by how healthy it is (−0.0003 + 0.0001 × state), and lose 0.00002 a day for standing (no benches yet).
  Patients outside rooms and staff lose 0.0002 a day for each piece of rubbish within two tiles in the same room,
  0.0004 for other litter.
- **Thirst and the toilet** (`Needs`, from `patient.lua`). Patients come with thirst and need for the toilet each up
  to 0.2 (none with alien DNA). After the day's health, except for emergency patients, thirst grows by 0.004 to
  0.008 plus a fiftieth of warmth, and the need for the toilet by 0.002 to 0.008 (0.008 to 0.026 with gut rot or the
  squits). Above 0.7 thirst costs 0.002 happiness a day and shows the thirsty mood; in a corridor the patient looks
  for a drinks machine within 8 steps and, without one or three times in ten, looks again in two to four days.
  Buying the drink, peeing and the toilets are not there yet.
- **Policies** (`Hospital.Policies`, from `hospital.lua` and `hospital_policy.lua`). Each hospital has CorsixTH's
  policy window settings: `send_home` 0.1, `guess_cure` 0.9, `stop_procedure` 1.0 (up to 2.0), `goto_staffroom`
  from the level's `gbv.Tired`, staff allowed to move and granting raises. `SetPolicyCommand` sets one, in
  thousandths for the sliders, kept in the sliders' ranges (sending home never above guessing). The window itself
  is not there yet; staff moving has nothing to act on yet.
- **Tiredness** (`Humanoid.Fatigue`, `Tiredness`, from `staff.lua`). Every hour a member of staff in a room
  (other than a staff room), or a handyman anywhere, gains 0.00009 fatigue and loses 0.00002 happiness, so a doctor
  who never leaves their office reaches the level's `gbv.Tired` (600 thousandths, the hospital's `goto_staffroom`
  policy) in about 133 days. From then on, with no staff room in the hospital, they wait for one and lose 0.001
  happiness an hour; very tired (`gbv.VeryTired`, 700) costs 0.0002 an hour more. At the day's end tired staff
  who are not resting lose 0.02, and 0.01 more from fatigue 0.5; resting in a staff room brings 0.006. Fatigue
  lowers the treatment's service quality and the GP's diagnosis bonus, and a consultant stays at the desk only
  while a random draw is at least their fatigue. Going to the staff room and resting there are not ported yet.
- **Firing and raises** (`StaffEmployment`, from `staff.lua`). `FireStaffCommand` pays a month's wage as
  severance, costs 3 reputation (as a kicked patient) and sends the member of staff out through a spawn point
  (`LeavingBehaviour`); wages, feelings and the end conditions no longer count them. A nurse or doctor in the middle
  of a two-person use finishes it first. Staff below 0.1 happiness ask for a raise every 200 hours with a 1 in 5
  chance (the larger of 10 % and half-way to the fair wage, up to the maximum salary); unanswered for 750–800 hours,
  they get it if the hospital's policy grants raises and quit otherwise. `AnswerRaiseCommand` grants (happiness up
  0.99) or refuses (they are fired). The raise dialog is not ported.
- **VIPs** (`VipVisits`, `VipBehaviour`, from `world.lua`, `vip.lua` and the VIP faxes). Each hospital's next VIP
  comes some 120–180 days on (a triangular draw around the level's `gbv.MayorLaunch`, 150), if the hospital has a
  room and a staffed desk; otherwise the visit is put off. The VIP then asks to visit: `AnswerVipCommand` invites or
  refuses them, and after 20 days the fax invites them by itself. A VIP refused more than twice may come anyway, one
  time in two. The VIP walks at half speed, goes ahead of patients at reception, stands there for the rest of the
  day, then looks in from the door at the first room and, after it, at each with a chance of one in two (always at a
  ward or research department), 100 hours each. On leaving they rate the hospital from 12 less up to 5 by litter
  (each piece within 8 steps counted once a day; more than 10 rates up), staff tiredness, the patients' health,
  warmth, happiness, deaths, cures, standing and queues, the doctors' grades and the rooms' extinguishers, bins and
  plants. Off the map a rating of 7 or better brings a grant of 200–4,000 and 20–50 reputation; a worse one 15 down
  to −25 reputation. When every VIP of the year left pleased, the year end gives a trophy in reputation.
- **Emergencies** (`Disasters/Emergencies.cs`, from `world.lua`'s `nextEmergency` and `createEmergency`,
  `objects/helicopter.lua` and `fax.lua`). The level's `#emergency_control` plans emergencies by month; on the day
  one comes, if the hospital is open and has a heliport, a staffed desk and the disease discovered, a fax offers it
  (refused by itself after 16 days). An accepted emergency brings a helicopter that drops its patients, diagnosed,
  at the heliport; they go ahead of others in queues. A watch of 52 days runs; when it ends, or every patient is
  cured or dead, the remaining ones die unless cured, and enough cures bring the bonus and 15 reputation, too few
  cost 20 reputation. Each hospital has its own schedule.
- **Epidemics** (`Disasters/Epidemics.cs`, `People/InspectorBehaviour.cs`, from `epidemic.lua`, `hospital.lua`'s
  `determineIfContagious` and `addToEpidemic`, and `entities/humanoids/inspector.lua`). A patient who comes with a
  contagious disease is contagious one time in the level's `ContRate` for it, once the game is past
  `ReduceContMonths` and `ReduceContPeepCount` patients came; up to `EpidemicConcurrentLimit` epidemics run at once.
  Every hour an infected patient may infect each patient on the four tiles next to them, in the same room or both
  in corridors, each patient being tried once, so that the epidemic's infections stay under `spread_factor / 200`
  of its tries. An epidemic stays hidden until one of its patients is diagnosed, then a fax asks whether to
  declare it (a fine, at least 2,000, and a hundredth of it in reputation) or cover it up (the default after 480
  hours). A cover-up runs a 100-day watch; when it ends, an uncured patient leaves the grounds or nobody is
  infected, the health inspector comes and reports at reception (or at once without a desk): nobody infected
  brings compensation, otherwise a fine and reputation, and from `EpidemicEvacMinimum` still infected an evacuation
  and a third of the reputation. During the cover-up the player picks infected patients to vaccinate
  (`VaccinateCommand`): one standing still out of rooms calls a free nurse out of rooms within 5 tiles, the nearest
  by path, who walks to a free tile next to them, faces them for 5 hours and vaccinates them for `VacCost`.
  Vaccinated patients pass nothing on but still count as infected.
- **Tile temperatures** (`Simulation.Temperatures`, from `th_map.cpp`'s `update_temperatures`). Each tile has a
  temperature from 0 to 65535 for 0–50 °C, 8192 at the start. Every hour after the entities, each tile moves a
  quarter towards its neighbours' average (a neighbour across a wall counts a quarter, unless both tiles are alike
  inside and in a room), then outside tiles 1 % towards the month's air, radiator tiles half-way to the radiators'
  heat (`0.25 + heat × 0.03`, by the parcel owner's setting) and other hospital tiles lose 0.1 %. Nothing feels it
  yet; the client's O overlay shows it in the town map's colours.
- **Statistics** (`Hospital.Statistics`, from `hospital.lua`). At the end of each month, after the bills and
  insurance, the month's money in and out, its wages, the balance, the visitors, cures and deaths so far and the
  reputation are recorded for the graphs; the first entry is the start of the game.
- **Cheats** (`Cheats`, from `cheats.lua`). `CheatCommand` performs the menu cheats the simulation has the parts
  for: money (10,000, `TransactionKind.Cheat`), a patient, winning or losing the level, prices up or down by 50
  points, forgetting the deaths and the reputation to 1,000, an earthquake now and earthquakes off or on, and
  ending the month or year (the calendar jumps to its last hour, and a planned emergency in the skipped days is
  replanned). Each that works marks `Hospital.Cheated`, except losing.
- **Earthquakes** (`Simulation.Earthquakes`, from `earthquake.lua`). The level's `quake_control` entries are
  planned in turn: a random day of a random month of the game between their months. On that day a 25-hour
  warning quake comes, 600 hours later the main one, which does a round of damage every 16 hours until its
  severity is used up; then the next is planned. `Earthquakes.Shake` says how hard the client shakes the view
  (gently in the warning and the first and last two rounds). The rounds do nothing yet: there are no machines to
  wear and no falling animations.
- **Room availability** (`LevelRules.IsRoomAvailable`, `Hospital.IsRoomDiscovered`, from `world.lua` and
  `hospital.lua`). A level has a room type when every object it needs has `objects[n].AvailableForLevel`; the
  hospital may build it from the start when they all have `StartAvail` too, otherwise once research discovers it.
  An object the level does not name counts as there from the start. `BuildRoomCommand` builds only discovered rooms.
- **Winning and losing** (`EndConditions`, from `endconditions.lua`). The level's `win_criteria` and
  `lose_criteria` become goals in groups; a hospital meeting every goal of a win group wins, one meeting every goal
  of a lose group loses. Wins are looked for at the end of March, June and September and at the year end, losses
  only at the year end, after its trophies and awards. The outcome stays in `Hospital.Outcome`; the simulation
  goes on. `ProgressReport` gives what CorsixTH's progress report shows.
- **The player's salary** (`Hospital.PlayerSalary`, from `hospital.lua`). It starts at 10,000 and rises each month,
  after the interest, by a tenth of `gbv.ScoreMaxInc` (300) plus the reputation above 500 per death plus one, at
  least a sixth and at most all of `ScoreMaxInc`, rounded up; at the end of the third year of level 3 by 8,000 to
  20,000 more. Winning sets `SalaryOffer`, the salary plus a bonus of 4 to 9 % unless the hospital cheated.
- **Using objects** (`ObjectUse`, from `use_object.lua`). The object's usage animations
  (`ObjectDefinition.UsageNorth` and `UsageEast`, mirrored for south and west) say how long each phase
  lasts: one tick per frame, with the lengths of the original's animations in `BaseGameContent` (a
  real-data test checks them). A use is prolonged, repeating the in-use animation until interrupted,
  when the object has begin, in-use and finish animations. Chairs are walked onto first. While an object
  is in use, the humanoid's `ActionAnimation` says what the client draws, from which tile, and that the
  object itself is hidden, as CorsixTH's `makeInvisible`.
- **Two people using an object** (`MultiObjectUse`, from `multi_use_object.lua`). `ObjectDefinition.MultiUsage`
  gives, per facing direction and pair of humanoid types, the begin phases, the in-use phase and the finish
  phases; a direction without its own mirrors the opposite one. Each phase plays the user's animation and the
  other one's secondary animation, or hides the other one (`Humanoid.IsHidden`) when the user's animation shows
  them too, and lasts as long as the longer of the two. The user takes on the other one's layers 0 to 4 for the
  use, or a patient using it with a member of staff takes on their layer 5. Where CorsixTH lists animations for the phases, one variant is picked for the whole use. Prolonged use and the morphs of invisible patients are not ported yet.

The client buys a reception desk with F (a stand-in for the original's corridor furnishing window;
Tab and Shift+Tab go through the other corridor objects),
draws use animations, and puts a receptionist facing away from the viewer in front of the desk on its
tile, otherwise behind it. B builds a GP's office, Shift+B a pharmacy and Ctrl+B general diagnosis until the build window exists; the
status line shows how many patients were cured and died. `--demo` hires the most skilled candidate of each kind,
places a desk once its GP's office works and builds and furnishes a pharmacy, then general diagnosis with a second doctor. `--speed` starts a local game at
another speed, which helps watching patients come and go.

## Saves

Saves are explicit, versioned serialization of simulation state. CorsixTH saves by
persisting the entire Lua heap, which ties saves to code structure. We avoid that.
The same serializer feeds network joins and state hashing.

`Simulation.Save` and `Simulation.Load` write and read the whole state through
`SimWriter`/`SimReader`: a fixed little-endian layout with a magic number and format version.
`ComputeStateHash` streams the same bytes into a 64-bit FNV-1a hash without buffering them.
Tests check that a saved and reloaded simulation continues to produce identical hashes.

Save files (`SaveGameStore` in `CorsixNet.Content`, after CorsixTH's `app.lua` and `world.lua`) add a short header
(`CNETSAVE`, a header version and the level's name) before the simulation's own save, and are written to a
temporary file first. They live in `%AppData%/CorsixNet/Saves` (or `CORSIXNET_SAVES`): the quick save
`quicksave.qs` (Alt+Shift+S, Alt+Shift+L; loading without one makes one) and, for local games, an autosave as each
month's last day begins, `Autosaves/Autosave-mm-dd.sav`, so a year keeps one per month. `--load FILE` and
`--continue` (the newest autosave) start the client on a saved game. Network games are not saved.

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
   confirm), paying for rooms, hiring staff, the calendar and wages, the reception desk and patients
   arriving, queueing and seeing the GP done; diseases and diagnosis to come)*
7. **M6+** Content breadth: diseases, staff, finances, events, campaign, AI hospitals, auctions,
   music and video.

## Attribution and clean-room policy

CorsixTH is MIT-licensed. Code and logic ported from it keep its copyright notice.
CorsixTH is the reference for game behaviour. Following CorsixTH's own policy, we do
not disassemble or decompile the original game binaries.
