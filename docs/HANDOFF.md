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
- Live checks: `CorsixNet.exe --script checks.txt` plays the mouse and keyboard from a text file and saves
  screenshots of the whole window (commands in `Client/Input/InputScript.cs`: wait, key, click, drag, wheel,
  shot, quit). It needs no focus and leaves the real mouse alone, so it works while the user is at the computer.
- Earlier live checks were done by starting the client and injecting mouse and keyboard input from a
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
  object"): the pharmacy, built from the room list (B); a nurse goes to its cabinet and gives diagnosed patients their drink
  (the cabinet's multi-use animations, patient drawn into the nurse's); they pay, are cured or die, and the
  drug is paid for. Patients waiting for a room go to one built later. The status line counts cures and
  deaths; `--demo` builds a pharmacy and hires the best candidates; `--speed` sets the starting speed.
  Save format version 7. Live-checked with `--demo --speed AndThenSomeMore`. Keys injected from a Remote Control session reach the window
  through SendInput or PostMessage, not keybd_event.
- General diagnosis (see ARCHITECTURE.md, "General diagnosis"): built from the room list (B); patients undress behind
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
  fax, PR #124), epidemics (I / Shift+I cover up or declare, PR #125) and vaccinating their patients (J,
  PR #126), answering waiting patients' faxes (K / Shift+K / Ctrl+K, PR #127), computer opponents that
  take their turns for patients and count them as visitors (PR #128), patients dropping litter and
  people minding it (PR #129), rat holes and rats coming with litter, shot with X (PR #130), the
  player's salary and the salary offered on winning (PR #131), the VIP rating counting the litter they
  see (PR #132), patients' thirst and need for the toilet growing (PR #133), the end of the month and
  year cheats (PR #134), the fax code cheats (PR #135), the patient window's send home and guess cure as
  commands, Q and Shift+Q (PR #136), the queue window's size and reordering as commands (PR #137),
  sending a patient queueing for a room to another room of the same type (PR #138), levels with random
  emergencies (PR #139), the campaign's difficulty and `--difficulty` (PR #140), idle staff meandering
  (PR #141), handymen meandering within their parcel (PR #142), staff speed: slow juniors and tired
  staff, fast consultants (PR #143), the pharmacy's nurse meandering between patients (PR #144), the
  general diagnosis doctor meandering until the patient has undressed (PR #145), floating dollar signs
  over paying patients (PR #146), the level's starting staff and CorsixTH's extra settings for campaign
  levels 5, 7, 8, 11 and 12 (PR #147), CorsixTH custom levels with `--map file.level` (PR #148), reading
  CorsixTH campaign files with `--campaign` (PR #149), reading the original's strings, `LANG-n.DAT` (PR
  #150), staff names and descriptions from them, shown for the member of staff under the cursor (PR
  #151), disease and VIP names in the status line (PR #152), rooms taking patients from the back of
  other rooms' queues (PR #153), the settings file `config.txt` in CorsixTH's format (PR #154), free
  build mode (PR #155), the sound archive reader (PR #156), the music list and XMI to MIDI conversion
  (PR #157), moving staff: M picks up the member of staff at the cursor, M again puts them down (PR
  #158). Sound effects in the client: SoundEffects plays SOUND-n.DAT through MonoGame for the client's
  own actions (building, placing objects, hiring and moving staff, parcels, rats, sending patients
  home), with play_sounds and sound_volume in config.txt; sounds the simulation causes are not played
  yet (PR #159). Scrolling as CorsixTH: keys and screen edges in 18 ms ticks with scroll_speed,
  shift_scroll_speed and the growing multiplier, the wheel zooming with momentum through zoom_speed, and
  prevent_edge_scrolling (PR #160). Camera hotkeys: = and - zoom (Shift five times as far), 0 original
  size, Alt+digit remembers the view and Ctrl+0, Ctrl+5-9 recall it, as Ctrl+1-4 still hire (PR #161).
  The patient window's data: the health graph's last 20 days, the treatment history (rooms, diagnosis,
  cure) and the sad mood (PR #162). No game data in the cloud: real-data tests were skipped there and
  the client was not run, so these want a look on MSI. Save format version 50.
- Second overnight cloud batch, 2026-11-09/10 (no game data in the cloud: nothing was run live, so every
  window wants a look on MSI). Windows over the map (`Client/Ui`, see ARCHITECTURE.md, "Windows"): text
  stand-ins for CorsixTH's dialogs with clickable buttons that send commands. F1 bank manager (loans,
  insurance owed), F2 bank statement, F5 casebook (prices, research concentration), F6 research shares,
  F8 graph figures, F9 hospital policy. Shift+F3 staff management (F3 stays the level list): staff by
  class, bonus (a tenth of the wage), a tenth more salary, firing after a confirmation, raise answers;
  clicking a member of staff opens their window (pick up, fire, a handyman's parcel).
  Clicking a patient opens the patient window (health, needs, history; send home, guess cure,
  vaccinate), clicking a room or a reception desk its queue window (size, front/back, another room of
  the kind); C opens the faxes (VIP, emergency, epidemic, patients asking), which open by themselves
  when a new fax comes. The old keys (V, E, I, K, Q) still work.
  Y opens the hire window (candidates by class; Hire puts them on the cursor, a click places them,
  right click or Esc cancels; Ctrl+1-4 still hire at once), F4 the town map (counts, heating, parcels
  with Buy), and the watch opens by itself while a countdown runs (opening, emergency, epidemic) with
  "Open now".
  F7 progress report (the level's goals and where the hospital stands), F11 the cheats window (F11 no
  longer runs all research at once; it is a button there), the annual report at each year end, and
  the level's end (won: salary offer and Next level; lost: why, and Restart).
  The bottom panel (balance; Build, Corridor, Hire, Faxes and the dialogs as buttons) is always there;
  Corridor opens the furnish corridor window (prices, how many, Buy puts one on the cursor); while
  furnishing a room the edit panel lists what is still needed, sells extra objects (Buy) and has
  Confirm, Pick up and Take down.
  Esc with no window open opens the menu (CorsixTH's menu bar) instead of quitting: save (to a new
  file or over one, after asking), load (manual saves, quick save, autosaves), quick save/load,
  restart, quit, the options (sounds, volume, screen shake, edge scrolling, scroll speeds, autosave;
  Apply saves config.txt and uses them at once), the levels list, the charts and cheats.
  F8 draws the graphs (graphs.lua): a coloured line per statistic, each scaled to its own range,
  buttons to hide lines, months/years/4-year steps, and Figures for the table.
  The adviser (adviser.lua, player_hospital.lua) speaks over the bottom panel: a queue said in turn
  (OK/Next, Quiet), the history of the last 20 (Messages button), the adviser_disabled option, and
  advice on set days from month 5 (money, staff room, radiators, toilets, heating, plants, thirst),
  the reception at month ends of year 1, the first cure/death, the first raise request, the boiler,
  earthquakes, new machines and VIPs walking in. Texts come from the game's strings (S[11], S[28],
  S[21], S[62]) with English stand-ins. Not yet: benches, queues, litter, machines, level progress.
  Dialogs (information.lua, confirm_dialog.lua, tip_of_the_day.lua): a tip of the day with the first
  game, a room's description from S[57] the first time one is built (room_information_dialogs), and
  Restart and Quit in the menu ask first.
  Rooms call staff by the dispatcher's score (path, queue x5, emergency, fatigue x40, -50 outside a
  room) instead of the nearest, and leave staff to a waiting room that scores better for them.
  Edit room (bottom panel, then click a room): the room stops taking patients, its queue goes to
  other rooms, its staff leave once no patient is inside, then its objects can be moved, bought or
  sold; Confirm costs nothing again, Take down asks and gives half its cost back (Room.IsPaid,
  IsClosing; save format 59). Not yet: going back to the blueprint to resize or move the door.
  Replays: a local game records its start and every command (LocalSession.Recording); Menu, Save
  replay writes Replays/*.cnreplay in the saves folder, Watch replay plays one back (ReplaySession,
  the player's commands ignored) and the status line says whether it matched the recorded hash.
  Hotkeys come from hotkeys.txt beside config.txt (CorsixTH's action names, written with the defaults
  when missing): the dialogs, Ctrl+H the adviser's history, Shift+A the adviser on or off (saved),
  pause, speed up and quick save/load. The camera, speeds and stand-in keys stay fixed.
  Menu, New game (new_game.lua): the original campaign from level 1 at Junior, Doctor or Consultant
  (the difficulties whose level files are installed); the levels list follows that campaign.
  The bottom panel's buttons have tooltips (CorsixTH's toolbar texts and the hotkeys) above the cursor.

- 2026-11-10 on MSI: every window of the second overnight batch was looked at live with `--script` on the game
  data and works; the tip and the watch now open where CorsixTH puts them (PR #199). The inflation room
  (rooms/inflation.lua, inflator.lua; frame counts read from VSTART-1/VFRA-1) treats bloaty heads: the doctor
  waits at the machine's other end, the patient's head is popped and pumped up (464, 478, 482, 496 once, 576)
  and comes back to normal size (layer 0 less 10) before the cure. Checked live from a save made by a test.
  The psychiatric room (rooms/psych.lua; couch, comfortable chair, bookcase, skeleton) diagnoses and treats with a
  psychiatrist only (`RoomVisits.IsQualifiedFor`): between patients they read at the bookcase and meander 2 to 8
  rounds; a session is 16 to 72 rounds of the chair counted while the patient lies on the couch, with trips to the
  bookcase. Save format 60. Objects against a room's front wall draw their user over the wall (seen with the
  chair); a drawing-order matter for later.
  The ward (rooms/ward.lua, bed.lua; save format 61) takes as many patients as beds and calls as many nurses as desks
  (`RoomVisits.UpdateWard`, kept out of the single-patient `Room.PatientId` as the toilets are). Nurses meander 4 to
  10 rounds and write at a free desk 7 to 14 rounds; patients lie in a free bed until 150 to 600 of work is done, each
  round taking off `WardHealingAmount` (nurses in the room x (0.5 + service quality) / ln(1 + patients) / (0.8 +
  difficulty / 10)). Swing doors (swing_door_left/right.lua; `RoomDefinition.SwingDoors`, save format 62): the right door
  is walked through with the humanoids' swing animations, the left one beside it and the wall beyond get the swing
  wall pieces; the left door swings at once, the right one once the walker is through, and nobody else goes through
  until it is done. Not yet: the VIP's visit to the ward.
  The blood machine (blood_machine_room.lua; save format 63): the doctor works the machine at its use position with
  the patient hidden in its animations at the other end; `MultiObjectUse.InUseRounds` plays the in-use phase again
  as a prolonged use (2 to 4 more times here).
  Decontamination (decontamination.lua, shower.lua, console.lua): the X-ray's flow with the console as the doctor's
  station (`RoomVisits.DoctorConsole`); the shower lasts math.random() * 3 - skill (short without loops under 1). From 2026-11-10 the user lets threads merge their own green PRs.

Needs MSI (game data or a live look; the cloud has neither):

- Every window and the adviser from the second overnight batch: nothing was seen on screen.
- Rooms with machines or their own animations, which need frame counts from VSTART-1/VFRA-1:
  scanner, cardiogram, operating theatre, fracture
  clinic, hair restoration, slack tongue, DNA fixer, electrolysis, jelly vat, staff
  room, training room.
- Machine wear, breakdowns and handymen repairing them (#33): the repair animations are needed, and
  explosions without repairs would only do harm.
- Benches (sitting animations), the death animation, staff yawning and checking their watch, staff
  using the staff room.

Not done yet:

- M5: hire window (the original's staff list with faces), staff meandering, benches, queue
  priorities, staff called through a dispatcher.
- Patients: diagnosis rooms other than general diagnosis, the crash trolley split over two tiles as
  CorsixTH draws it, treatment rooms other than the pharmacy (inflation for bloaty heads,
  psychiatry for TV personalities), faxes asking what to do with a patient, the casebook window
  (prices), looks of visual diseases (new humanoid types in the client), health,
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
