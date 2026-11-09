# Handoff notes (2026-11-08)

Context for continuing the work in a new Claude Code session on another computer. Read
`CLAUDE.md` and `ARCHITECTURE.md` first. This note covers what they don't: where the work
stands, how the user likes to work, and what was learnt about the original game. Delete it once
the new session has absorbed it.

## How the user works

- The user writes in Russian; answer in Russian. Code, docs and commit messages are in English.
  The user is fluent in C#.
- Commit and push only when the user asks ("коммить", "пушь"). Commit messages end with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Edit and inspect code through the **Rider MCP** (`mcp__rider__apply_patch`, `read_file`,
  `search_text`, `get_file_problems`, `reformat_file`, `rename_refactoring`, ...), not Python or
  sed scripts. The Python scripts wrote CRLF on Windows and broke `dotnet format`. Use Bash for
  git, `dotnet build/test/format` and running the client. Rider suggests removing trailing
  commas; ignore that, the repository style (enforced by `dotnet format`) keeps them.
- Downloads need the user's explicit yes. Don't spend money or make irreversible moves in the
  user's own copy of the original game.
- Faithfulness to the original matters. When unsure how the original behaves, look at CorsixTH
  (reference checkout `C:\repos\github\CorsixTH`, MIT) and say what it does. The user corrects
  wrong guesses about the original, so record their observations.

## Environment

- Original game (EA version) on the first computer: `D:\Games\Theme Hospital`, on the second
  `C:\Games\Theme Hospital`; data in `data\Game` (DATA, QDATA, LEVELS, ...). While DOSBox runs the
  original, `RncTests.DecompressesEveryCompressedGameFile` fails: it reads every file of the install
  and `DOSBOX\stderr.txt` is locked. `ThemeHospitalInstall.Locate()` finds it, or set
  `CORSIXNET_TH_PATH`. Real-data tests skip without it.
- CorsixTH checkout: `C:\repos\github\CorsixTH`.
- Commands: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`.
- Live checks were done by starting the client and injecting mouse and keyboard input from a
  PowerShell script (user32 `SetCursorPos`, `mouse_event`, `keybd_event`, `PrintWindow` for
  screenshots). Wait about 6 seconds after start before sending input, or the steps land before
  the level has loaded. With the default window on `LEVEL.L1`, a GP's office goes in with:
  B, drag from window (908,187) to (908,315), Enter, click (972,283) for the door, Enter.

## Where the work stands

Milestone M5 (build mode and a first complete room, then reception → GP patient flow).

Done and committed:

- Room building in the original's steps: blueprint (drag, resize by edge, move by middle) →
  door → windows → furniture → confirm. Esc steps back; in the furniture step it takes the room
  down. No doors or windows on corner tiles.
- Rooms are built inactive; `PlaceObject`, `PickUpObject`, `ConfirmRoom`, `CancelRoom` commands.
- The blueprint is drawn with the original's sprites (blue wireframe walls, door and window
  sprites, floor marks; red parts recoloured through `QData/Ghost1.dat` table 6).
- Money: `LevelConfig` reads `LEVELS/*.SAM`, `LevelRulesLoader` builds `LevelRules`
  (start cash, room and object costs), `Hospital` per player with balance and transaction log,
  `RoomPurchases` (whole cost needed to start; objects paid at build, room at confirm, objects
  sold back on cancel). Balance in the client's top line. Save format version 3.
- Build UI: red outline on the tile under the cursor while building; the object being placed is
  attached to the cursor by its anchor tile and follows it tile by tile.
- Staff hiring (see ARCHITECTURE.md, "Staff"): `StaffProfile` (skill in thousandths), `StaffRules`
  from the level files, candidates for hire at game start, `HireStaffCommand` with CorsixTH's
  placement check, one month's salary paid on hiring, staff types and walk animations in the client.
  Debug keys Ctrl+1-4 hire the first doctor, nurse, handyman or receptionist onto the hovered tile;
  `--demo` hires one of each. Save format version 4.
- Game calendar (`GameDate`, from the tick, as CorsixTH's date.lua): monthly wages and new
  candidates for hire at the end of each month. Game speeds as CorsixTH's (keys 1-5, Space/P,
  Z held), local games only; date and speed in a box at the top right.
- Patient flow (see ARCHITECTURE.md, "Patients and their visit"): reception desk bought with F, a
  receptionist goes to it; patients arrive by the level's `popn` schedule, queue at the desk, go to
  the GP's office, queue at its door, sit on the chair until the doctor (cabinet-desk cycle) is done,
  then go home. Use animations of desk, cabinet and chair. Save format version 5.
- Diseases, diagnosis and payment (see ARCHITECTURE.md, "Diseases", "Diagnosis", "Payment"): patients
  come with one of the level's diseases, the GP diagnoses them and they pay for the consultation (or
  their insurance pays two months later); diagnosed patients wait for a treatment room, the others for a
  diagnosis room or go home, as CorsixTH's default policies say. The demo's patient chair keeps off the
  front walls. Save format version 6.
- Pharmacy and treatment (see ARCHITECTURE.md, "The pharmacy", "Treatment", "Two people using an
  object"): Shift+B builds a pharmacy, a nurse goes to its cabinet and gives diagnosed patients their drink
  (the cabinet's multi-use animations, patient drawn into the nurse's); they pay, are cured or die, and the
  drug is paid for. Patients waiting for a room go to one built later. The status line counts cures and
  deaths; `--demo` builds a pharmacy and hires the best candidates; `--speed` sets the starting speed.
  Save format version 7. Live-checked with `--demo --speed AndThenSomeMore`: keyboard input injected from a
  Remote Control session does not reach the window, hence `--speed`.
- General diagnosis (see ARCHITECTURE.md, "General diagnosis"): Ctrl+B builds it; patients undress behind
  the screen, the doctor examines them on the crash trolley, they dress, pay and go back to the GP. With a
  weak GP on level one it takes cures from 0 to 8 in five months. `--demo` builds it after the pharmacy
  and hires a second doctor. Save format version 8.
- Room doors (see ARCHITECTURE.md, "Room doors", "Wandering"): people go through a room door with its
  opening animation, one at a time; the demo's wanderers meander as CorsixTH's and keep out of rooms.
  Save format version 9.
- Overnight cloud batch on 2026-11-08 (PRs #97–#108, each listing its oddities): clearing a new
  blueprint, corridor objects, the opening watch (Shift+O), reputation, the year end's trophies and
  awards and the hospital's value, winning and losing, loans and interest (L / Shift+L), buying land
  (N), room availability by level, setting prices in the casebook (by command only), heating and boiler
  breakdowns (T / Shift+T), patients sent to the hospitals in turn; then the monthly statistics for the
  graphs (PR #109) and the bank statement and graphs figures as text on F2 and F8 (PR #110), hiring a
  receptionist straight onto a desk (PR #111), the menu cheats as a command (PR #112), earthquakes from
  the level's `quake_control` with screen shake (PR #113), tile temperatures (PR #114), warmth and
  happiness of people (PR #115), plants drooping (PR #116), rooms' windows and space cheering staff (PR
  #117), staff tiring at work (PR #118), patients' health running down until they give up or die (PR
  #119), the quick save (Alt+Shift+S / Alt+Shift+L), monthly autosaves, `--load` and `--continue` (PR
  #120), hospital policies by command (PR #121), firing staff and their raise requests by command (PR
  #122), VIP visits (V / Shift+V answer the invitation, PR #123), emergencies (E / Shift+E answer the
  fax, PR #124), epidemics (I / Shift+I cover up or declare, PR #125). No game data in the cloud:
  real-data tests were skipped there and the client was not run, so these want a look on MSI. Save
  format version 32.

Not done yet:

- M5: hire window (the original's staff list with faces), staff names and descriptions from the
  language files, staff meandering, benches, queue
  priorities, staff called through a dispatcher.
- Patients: diagnosis rooms other than general diagnosis, the crash trolley split over two tiles as
  CorsixTH draws it, treatment rooms other than the pharmacy (inflation for bloaty heads,
  psychiatry for TV personalities), faxes asking what to do with a patient, the casebook window
  (prices), floating dollar signs, looks of visual diseases (new humanoid types in the client), health,
  the death animation, the rest of reputation (VIPs, emergencies, awards), drug research.
- Money: the town map, the annual report and bank manager screens, bank window
  with the transaction log.
- Build UI: on-screen panel like the original (list of items with counts, confirm, pick-up and
  buy buttons); steps are keys and the status line for now. Cursor shapes per step. Buying extra
  objects.

## What we learnt about the original game

Observed by driving the original in DOSBox, plus the user's corrections:

- The room's whole cost must be in the bank to start it. Game time keeps running while building.
- Furniture step: walls are real already. The first missing item comes to the cursor on its own,
  then the next one after each placement.
- The item on the cursor is attached by one fixed cell and follows every tile step, even inside
  its own footprint (as CorsixTH's `calculateBestPlacementPosition`). An earlier guess that it
  only moves after leaving its footprint was wrong, and the user found it inconvenient.
- The item is hidden when the cursor leaves the room. Right click turns it. A click on an invalid
  spot does nothing. Invalid = drawn flat grey; there is only a faint light footprint, no red or
  green tiles.
- The tile under the cursor has a red outline while building.
- Pick-up ("claw") button: the hovered object is highlighted; picking it up puts it back on the
  cursor.
- The GP desk includes the doctor's swivel chair; it needs a free tile behind the chair, so the
  desk can't stand with the chair against a wall. The "Chair" item is the patient's chair.
- Doors and windows never go on a room's corner tiles.
- The cash-register button opens "Choose Items" (extinguisher, radiator, plant, bin) with prices.
- Year end shows award or "Hall of Shame" screens and then returns to the game.

## Shelved experiment

Neural sprite upscaling (Real-ESRGAN x4plus and general-x4v3 as ONNX) flattened the game's
dithering into a plastic look; it was put aside. MMPX smoothing (key U) is in the client.
