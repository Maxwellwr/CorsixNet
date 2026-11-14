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

- The original's look (2026-11-14, PRs #308–#341): the bottom panel (`OriginalBottomPanel`), the adviser's figure and
  balloon (`AdviserFigure`), and every dialog the original draws from its own graphics now draws itself that way when
  the graphics are there, falling back to its table of text otherwise (tests, demo data): bank manager, casebook,
  policy, research, progress report, graphs, town map, annual report, fax (fullscreen, 640×480 centred); build room,
  room edit and its buy window, furnish corridor, hire, staff, patient, queue, machine, watch, information and confirm.
  The pattern: a `*Screen` class per dialog, `UiWindow.IsFullscreen(host)` (with `FullscreenSize`,
  `FullscreenPosition`, `CapturesMouse`), `OriginalGraphics` (pictures, sheets and fonts from QData or Data with their
  palettes, the language's Unicode face for Russian), and `IWindowHost` helpers (`CloseUp` for view circles,
  `DrawIdle`, `DrawAnimation`, `TileAtScreen`, `PlayerName`). CorsixTH's own resizable windows (tip of the day,
  options, menus) use its `aux_ui` bitmaps, which the original lacks, so they stay our text windows.
  Live checks: `--load <save> --script` with `key F1`… and `button <label>`; panel buttons are found by name.
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
  class, bonus (a tenth of the wage), a tenth more salary, firing after a confirmation, raise answers
  (in the original's look, `StaffManagementScreen`, where the graphics are there);
  clicking a member of staff opens their window (pick up, fire, a handyman's parcel).
  Clicking a patient opens the patient window (health, needs, history; send home, guess cure,
  vaccinate), clicking a room or a reception desk its queue window (size, front/back, another room of
  the kind); C opens the faxes (VIP, emergency, epidemic, patients asking), which open by themselves
  when a new fax comes. The old keys (V, E, I, K, Q) still work.
  Y opens the hire window (candidates by class; Hire puts them on the cursor, standing on the tile below it
  and grey where they cannot go, as UIPlaceStaff draws them; a click places them,
  right click or Esc cancels; Ctrl+1-4 still hire at once), F4 the town map (counts, heating, parcels
  with Buy), and the watch opens by itself while a countdown runs (opening, emergency, epidemic) with
  "Open now".
  F7 progress report (the level's goals and where the hospital stands), F11 the cheats window (F11 no
  longer runs all research at once; it is a button there), the annual report at each year end, and
  the level's end (won: salary offer and Next level; lost: why, and Restart).
  The bottom panel is the original's (bank, balance, reputation, date, Build, Corridor, Edit, Hire; over it
  the dialogs' row; waiting faxes as icons above its left end, a click opens the fax window);
  Corridor opens the furnish corridor window (prices, how many, Buy puts one on the cursor); while
  furnishing a room the edit panel lists what is still needed, sells extra objects (Buy) and has
  Confirm, Pick up and Take down.
  Esc with no window open opens the menu (CorsixTH's menu bar) instead of quitting: save (to a new
  file or over one, after asking), load (manual saves, quick save, autosaves), quick save/load,
  restart, quit, the options (sounds, volume, screen shake, edge scrolling, scroll speeds, autosave;
  the language among the installed LANG-n.DAT, whose texts and speech switch at once; Apply saves
  config.txt and uses them at once), the levels list, the charts and cheats.
  F8 draws the graphs (graphs.lua): a coloured line per statistic, each scaled to its own range,
  buttons to hide lines, months/years/4-year steps, and Figures for the table.
  The adviser (adviser.lua, player_hospital.lua) pops up over the bottom panel (animations 438,
  460, 440) with his speech in a balloon (Panel02V, Font50V): a queue said in turn (left click on him for the next,
  right click sends him down), the history of the last 20 (Messages button), the adviser_disabled option, and
  advice on set days from month 5 (money, staff room, radiators, toilets, heating, plants, thirst),
  the reception at month ends of year 1, the first cure/death, the first raise request, the boiler,
  earthquakes, new machines and VIPs walking in. Texts come from the game's strings (S[11], S[28],
  S[21], S[62]) with English stand-ins. Benches, queues, litter, machines and level progress came later (see below);
  2026-11-13 added the price judgements and patients not paying (`Hospital.PriceAdvices`, `NotPaying`; save format 90),
  the epidemic's hurry up and serious warnings, and drug and machine improvements. Not done: falling_1-6 (said only
  when the player knocks a patient over, which this port cannot do) and research's new_available (an object discovered other than by research, which this port has no way of; drug_fully_researched
  was added the same day).
  Dialogs (information.lua, confirm_dialog.lua, tip_of_the_day.lua): a tip of the day with the first
  game, a room's description from S[57] the first time one is built (room_information_dialogs), and
  Restart and Quit in the menu ask first.
  Rooms call staff by the dispatcher's score (path, queue x5, emergency, fatigue x40, -50 outside a
  room, -100000 for rooms needing a researcher, psychiatrist or surgeon) instead of the nearest, and leave staff
  to a waiting room that scores better for them. With staff_allowed_to_move (the default), a room with patients
  queueing calls staff idle in a room nobody waits for (2026-11-12; only then, as rooms here poll for staff).
  Ctrl+C over a finished room puts a copy of it on the cursor (our addition): the same type, size and door, moved by
  its top left corner; a click builds it and its objects go where they stood in the original if they fit (not its
  windows), then it is furnished and confirmed as any new room; Esc or a right click cancels.
  Edit room (bottom panel, then click a room): the room stops taking patients, its queue goes to
  other rooms, its staff leave once no patient is inside, then its objects can be moved, bought or
  sold; Confirm costs nothing again (Room.IsPaid, IsClosing; save format 59). Esc while editing goes
  back to the door, as CorsixTH's returnToDoorPhase: the room comes down, its objects are sold at full
  price, it stays paid for (Hospital.Rebuild, RoomRebuild, save format 76), and it can be resized or
  get its door moved; built again, its needed objects are bought again and a machine placed again
  keeps its wear. Esc on its blueprint asks (delete_room) and gives half its cost back (abortRoom).
  Deviation: CorsixTH buys every object back on the list, here extra objects are bought again from
  the edit panel.
  Replays: a local game records its start and every command (LocalSession.Recording); Menu, Save
  replay writes Replays/*.cnreplay in the saves folder, Watch replay plays one back (ReplaySession,
  the player's commands ignored) and the status line says whether it matched the recorded hash.
  Hotkeys come from hotkeys.txt beside config.txt (CorsixTH's action names, written with the defaults
  when missing): the dialogs, Ctrl+H the adviser's history, Shift+A the adviser on or off (saved),
  pause, speed up, quick save/load, the speeds 1-5, scrolling (arrows; WASD stays too), zoom (=, -,
  Shift for five times, 0), Alt/Ctrl+digit views and B the room list. Speed 6 and the stand-in keys stay fixed.
  Options, Hotkeys, Assign opens the hotkey assignment dialog (hotkey_assign.lua; HotkeyAssignWindow): panes of
  the actions above, click a key then press the new one (Esc/Return alone refused, a key in use swaps), Accept
  saves hotkeys.txt and uses the keys at once. While it waits for a key the game sees no keyboard (IKeyCaptureWindow).
  Map editor model (map_editor.lua, th_map.cpp level_map::save; 2026-11-12): Content/Levels/MapEditor.cs edits a
  LevelMap loaded with LevelMapLoader.Load(file, editing: true) (no parcel owners): PaintFloor (outside/road/hospital
  flags), walls, DeleteWalls, SetParcel, camera/heliport, Copy/Paste, ToMapFile/Save. MapFile.Create/ToBytes write
  the format. The editor's window and cursor (2026-11-12, MSI, seen live): the main menu's Map Editor or `--map-editor`
  opens a blank map paused (a Simulation on the editor's LevelMap without map objects; MapViewScreen.StartMapEditor) with
  MapEditorWindow: the nine sprite pages (MapEditorPages, layoutButtons) drawn with the map's block sprites on its canvas
  (UiWindow.CanvasClicked), paste, delete wall, cameras and heliports 1-4 (players beyond the map's count greyed),
  parcels 0-9 (the parcels overlay shows), Save map (Levels folder, "Map N.map", playable from Single Scenario), Load
  map, New map, Quit. MapEditorTool is UIMapEditor's cursor: left drag paints (drag capabilities: floor area, north wall
  along x, west wall along y), right press during a left drag paints in blocks, right drag copies and goes to paste,
  moving back to the start cancels; red outlines as fillCursorArea; entrance doors drawn from the editor's placements
  (MapEditor.PlaceEntranceDoor, checkObjectSpace). In the editor the right button no longer pans (middle does); Esc
  reopens the window. `--script` `drag x1 y1 x2 y2 right` drags with the right button. The player count (LevelMap.SetPlayerCount,
  PR #263) and the custom parcel number box (MapEditorWindow as IKeyCaptureWindow) are done; CorsixTH's editor and map loading
  know no objects but entrance doors (Simulation.CreateMapObject ignores the rest), so there is nothing more to place.
  Seated patients get up for a drink and are vaccinated on the bench (PR #266, format 84); they are not sick while seated,
  as atFullyEmptyTile.
  Patients knock on a room's door before going in (walk.lua's KnockDoorAction; PatientSpotAction.Knock, 2026-11-12): rooms
  with required staff, doors that do not swing, nobody queueing or inside; knock_east animations are one frame in the
  original. The crash trolley is drawn split over two tiles (ObjectDrawing.Split, AnimationRenderer.SetColumnClip,
  thdf_crop), its examination too. The waiting patients' watch/yawn/foot animations were checked against the game data.
  Benches (2026-11-12, MSI, seen live; save format 83): HumanoidQueue.Arrange sits patients on free corridor benches as
  action_queue_on_change_position (keep a bench within 4 tiles of the goal, look within 10 standing, 2/3 of the distance
  sitting; reception desks and toilets keep 3 standing, aliens stand), PatientBehaviour.BenchId/GoToBench/LeaveBench
  (bench.lua's use animations, prolonged; called in, sent home or the bench gone, they get up first), a new bench makes
  queues look again, sitting cheers, the VIP and the adviser count sitters (_adviseBenches on day 12 from the sitting
  ratio every second day). The bench facing north is drawn split over three tiles.
  The tutorial (game_ui.lua's tutorial_phases; Ui/Tutorial.cs, 2026-11-12): the new game dialog's Tutorial switch starts it on
  the first level; it follows the interface each frame (TutorialView from MapViewScreen.TutorialView) instead of CorsixTH's
  dialog callbacks, says each phase's S[11] text kept up (Adviser.SayAndStay) and lights the button to press
  (Tutorial.Lit). Phases this UI has no window for are skipped (order/accept, next/previous candidate, room categories).
  The whole tutorial was played live with --script (Esc menu, New game, Tutorial On, Start; then the desk, a
  receptionist, the GP's office from the room list with blueprint, door, windows, objects, Confirm and the help box, a
  doctor) and ends with the introduction texts; TutorialTests cover the phases too. The end shows
  introduction texts 15-17 (OriginalTexts.IntroductionText). As CorsixTH's ui.start_tutorial, a game started with the
  tutorial never gets reception_need_receptionist or gps_office_need_doctor (AdviserChecks.StartedWithTutorial), and its
  opening watch is held (HoldOpeningWatchCommand, Hospital.IsOpeningWatchHeld, save format 85) until the last chapter
  starts it afresh; a game saved during the tutorial starts its watch afresh when loaded (2026-11-12, cloud).
  Surgeons change back into doctors at the screen when they leave (PR #264, format 82), also when they go to rest
  (2026-11-12: `DoctorBehaviour.ChangeBackBeforeRest`, then `Tiredness.WantStaffRoom`; format 89); the death animations, grim reaper
  and earthquake falls had been merged only into stacked branches and came to main with PR #262 (format 81).
  Handyman priorities (staff_dialog.lua changeHandymanAttributes; save format 78): the staff window's Litter/Plants/
  Machines More buttons (IncreaseHandymanPriorityCommand), per-handyman thousandths in HandymanBehaviour; the first
  change makes the adviser say handyman_adjust (Hospital.HandymanPrioritiesChanged).
  Tooltips (#74): OriginalTooltips.Get(host, index, fallback) reads section 32 (original_strings.lua's tooltip.*);
  most text windows' buttons have them now, including the fax, cheats, adviser history and level end windows
  (TooltipTests lists the windows checked). The progress report's goals carry tooltip.status (ProgressReportWindow.Tooltip;
  section 64 when loaded).
  More advice (#72; save format 79): long queues on day 10 (getRandomBusyRoom: queues_too_long, or for a doctor's room
  queue_too_long_send_doctor/need_doctors), plants_thirsty once a plant, people_did_it_on_the_floor
  (Hospital.DidItOnFloor), fax_received (first fax in a campaign level; client-side flag, not saved),
  information.emergency at helicopter phase 60, epidemic_health_inspector, reception_need_receptionist / no_desk_5 on
  buying a desk. Bench advice and the tutorial were done since; crazy doctors done 2026-11-13 (`Tiredness.CheckCrazy`, `Humanoid.IsCrazy`, save format 91: a very
  tired doctor goes crazy one day in 300, looks wild (layer 5 + 4) until rested in the staff room; doctor_crazy_overwork). msgMultiReceptionDesks (reception_bottleneck, queue_too_long_at_reception, another_desk) runs when a
  patient sets off for a desk or joins its queue (AdviserChecks.MultiReceptionDesks).
  Fax keypad (fax.lua UIFax:validate, cheats.lua processCheatCode): digits, Cancel, Correct and Validate in the fax
  window; 24328 opens the cheats window, 112 is accepted, the four toggle codes (FaxCodes.ToggleCheat, CorsixTH's
  n^5.00001 formula) send CheatCommand; fax_yes/fax_no sounds.
  VIP report fax (makeVipEndFax; save format 80): VipVisits.Report keeps the name, rating and grant for 20 days and
  the fax window shows it; the remarks are section 44, 196-210, with our stand-ins without the game's strings.
  Emergency result fax (makeEmergencyEndFax; save format 80): HospitalEmergencies.Report (saved, total, bonus there
  was, earned) for 25 days, section 44 strings 40 and 41.
  Handyman advice (2026-11-12): the first litter without handymen (need_handyman_litter, Hospital.Littered, save
  format 77), a machine placed without one (need_handyman_machines), and repair calls (machines_falling_apart when a
  machine goes straight below 4 uses left, machinery_damaged2 below 6 or from the machine window with no handyman).
  CorsixTH's translations (#75; 2026-11-12, cloud): CorsixTH/Lua/languages/*.lua are bundled in
  Content/Languages/CorsixTH (copied to Languages/ beside the game); `LuaChunk` parses them and `CorsixLanguages`
  evaluates them as strings.lua (merging assignments, Inherit, original_strings' S[s][i] names). `OriginalTexts.String`,
  disease texts, VIP names, introduction texts and room descriptions come from the translation where it has them, so
  english.lua's corrections apply too. The options list CorsixTH's languages drawn with the game's font (cp437),
  and since 2026-11-14 (cloud) also Russian (Font "mik") and the Unicode-font languages, drawn with a TrueType font
  (unicode_font, else CorsixTHUnicode.ttf/GoNotoKurrent-Regular.ttf beside the game, else a system font with the
  language's letters; see ARCHITECTURE.md, "Settings"). Not looked at live yet: wants a look on MSI with Russian. CorsixTH's own adviser
  messages (no_desk_*, another_desk, *_needs_desk_*, not_enough_lecture_chairs) carry their names (`Advice.Name`), and
  the main menu and the new game window take main_menu.*, new_game_window.* and their tooltips (`CorsixText.Get`). Staff
  names, descriptions and professions come from the translation's humanoid_name_starts/_ends, staff_descriptions and
  staff_class (whole-section assignments such as `= S[9]` and `[38] = nil` are followed). Not yet: the other windows'
  English stand-ins.
  Main menu (main_menu.lua; 2026-11-12): with no --level/--map/--campaign/--load/--continue/--demo/--script/
  network option the game opens on it, the first level paused behind: Campaign, Custom Campaign and Single Scenario
  (the .campaign/.level files under config.txt's campaigns/levels, by default Campaigns/Levels beside the saves
  folder; CustomGames), Continue Game (newest save, autosaves too), Load Game, Settings, Exit (asks).
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
  bookcase. Save format 60. Users of objects against a room's front wall are covered by it again since the port of
  level_map::draw's redraw (checked live 2026-11-12 with the chair and the couch; a few pixels of a hand at the corner
  remain, as in CorsixTH's algorithm).
  The ward (rooms/ward.lua, bed.lua; save format 61) takes as many patients as beds and calls as many nurses as desks
  (`RoomVisits.UpdateWard`, kept out of the single-patient `Room.PatientId` as the toilets are). Nurses meander 4 to
  10 rounds and write at a free desk 7 to 14 rounds; patients lie in a free bed until 150 to 600 of work is done, each
  round taking off `WardHealingAmount` (nurses in the room x (0.5 + service quality) / ln(1 + patients) / (0.8 +
  difficulty / 10)). Swing doors (swing_door_left/right.lua; `RoomDefinition.SwingDoors`, save format 62): the right door
  is walked through with the humanoids' swing animations, the left one beside it and the wall beyond get the swing
  wall pieces; the left door swings at once, the right one once the walker is through, and nobody else goes through
  until it is done. The VIP's visit to the ward: the ward is a must-visit room, nothing more in CorsixTH (checked
  2026-11-12; `evaluateRoom` now also counts machines and a patient in the research department, as CorsixTH).
  The blood machine (blood_machine_room.lua; save format 63): the doctor works the machine at its use position with
  the patient hidden in its animations at the other end; `MultiObjectUse.InUseRounds` plays the in-use phase again
  as a prolonged use (2 to 4 more times here).
  Decontamination (decontamination.lua, shower.lua, console.lua): the X-ray's flow with the console as the doctor's
  station (`RoomVisits.DoctorConsole`); the shower lasts math.random() * 3 - skill (short without loops under 1).
  Stripped patients come in CorsixTH's three kinds (`StrippedMalePatient2/3`, ...; PR #206). The cardiogram
  (cardiogram.lua, cardio.lua) runs through general diagnosis's flow (`RoomVisits.UndressesFirst`): undress, the
  machine with the doctor (54 in-use rounds, the time of CorsixTH's slowed-down loop), dress. Two-person uses are now
  drawn from the object's tile in its attach tile's pass, as single uses (it doubled the cardiogram).
  The scanner (scanner_room.lua, scanner.lua; save format 64): undress, the scanner's five begin and five finish
  animations (`UseAnimations.More`, `ObjectUsePhase.Begin3`..`Finish5`; it has no in-use one) while the doctor works
  the console, dress.
  The operating theatre (operating_theatre.lua; surgeon_screen, op_sink1/2, x_ray_viewer, operating_table/_b; save
  format 65): two surgeons (doctors with the surgeon ability) change into scrubs at the screen (the outfits' count picks
  the animation), wait meandering; the patient changes into a gown (new types Surgeon, GownedMale/FemalePatient), the
  surgeons wash at the sink and take their places; the second surgeon operates from the far side (operating_table_b's
  animations, 2 to 5 rounds) while the first's use of the table with the patient loops its one-frame in-use animation
  for as long; then the patient changes back and is cured. The surgeons changing back at the screen as they leave or go to rest, the X-ray viewer lighting up and the screen's
  outfit idles were done later (PRs #264, #283 and the X-ray viewer); the "needs a ward/surgeon" advice is done with the rooms' requirements below. From 2026-11-10 the user lets threads merge their own green PRs.

Next, in this order (2026-11-10):

1. Done (PR #211): the staff room. `RestingBehaviour` takes over a tired doctor, nurse or handyman (Tiredness.
   CheckIfNeedRest, once their room's patient has gone; ward nurses at once) and gives them back their usual behaviour,
   free for any room, when rested. Done 2026-11-12 (cloud): researchers and doctors in training go back to their room
   once rested if it has room for them (`RestingBehaviour.ReturnRoomId`, CorsixTH's `last_room`;
   `RoomVisits.ReturnToLastRoom`), staff on call (a handyman with a job, a nurse going to vaccinate, staff walking to
   a room) neither rest nor stop tiring in the staff room, and very tired staff show the tired mood until rested
   (`Humanoid.IsTiredMoodShown`; text in the staff window). Save format 74. Mood icons drawn (2026-11-12, MSI):
   `HumanoidMoods` picks the highest-priority active mood of humanoid.lua's table and `HumanoidDrawing` draws it 96 px
   up (setMoodInfo): dead, cured, exit, sad_money (`PatientBehaviour.HomeMood`, save format 88), emergency, the
   cover-up's epidemic statuses, dying1-5 by health, sad2, patient_wait (no room), poo, thirsty; staff exit, pay_rise,
   tired; the icon follows the frame markers CorsixTH sets on animations (`MoodMarkers`, generated from the Lua's set{Patient,Staff}Marker calls). staff_wait (from the queue to the door), the GP's idea3/reflexion
   (until the patient is out) are drawn too. The queue mood over the patients queueing at the room (or door) under the cursor and cold/hot over the humanoid under it,
   with HLIGHTP2.wav (GameUI:onCursorWorldPositionChange), are done too (2026-11-13). Left out:
   meander.lua's idle staff going back to any `last_room` that needs staff (rooms here call staff themselves).
2. Looks of visual diseases: Slack Male/Female done (PR #212: slack tongues and bald heads, with the GP chair, drinks
   machine, loo, X-ray, scanner and blood machine; a slack woman borrows a standard woman's animations,
   `ObjectDefinition.LooksLike`). Pregnant patients are women only; a "pregnant" man seen once could not be found again (2026-11-13: every way a patient
   gets a disease checked, and four months with the ward and theatre showed only standard and gowned women). Chewbacca done
   with electrolysis (PR #215). Alternate Male done with the fracture clinic (PR #216; women are off, CorsixTH's disable_fractured_bones_females default). Transparent and Invisible done (2026-11-12, MSI, seen live: TransparentMale/FemalePatient, InvisiblePatient with their walking,
   doors, knocking, vomiting, peeing, dying, the GP's chair, bench, loo, drinks machine, X-ray (invisible), scanner and autopsy;
   the pharmacy's drink morphs them into standard patients, MultiUsePhase.SecondaryMorph, NurseBehaviour.BecomeStandard; the
   invisible man's cured_layers are picked at the cure). Elvis done (2026-11-12, MSI, seen live: ElvisPatient with walking,
   doors, knocking, vomiting, peeing, dying, GP chair, bench, loo, drinks machine, X-ray, scanner (dressed), autopsy and the
   couch; psych.lua's king complex change: at the end of the session PatientBehaviour.StartKingChange sends him to the
   screen (KingChangeStep, anim 946, use_screen.lua's layers 2/random/2) and he is dealt with after it; save format 87).
   Next: Alien (walk_anims in entities/humanoid.lua) with their animations in every object they use on the way. Then the
   rooms that need them: slack tongue, hair restoration, electrolysis, fracture clinic, DNA fixer, jelly vat.
3. Machine wear and repairs done (save format 68): `MachineState` on every machine `RoomObject` (strength from the
   research, times used), `Machines.Used` at the end of each use by anyone but a handyman (ObjectUse/MultiObjectUse
   report no Done when it explodes), `Machines.Crash` (crashRoom: everyone inside dies, other objects go at the end of
   the tick via `RoomObject.IsDestroyed`, soot on the floor, value and 50 reputation lost, `Room.IsCrashed`), handymen
   repair below 6 uses left (priority 2 below 4): walk to the handyman position, meander until the room's patient has
   gone or is leaving, then `GoingToRepair` stops the room letting anyone in until the repair (begin/in-use for at least
   20 frames/finish) ends. Client: smoke plumes below 4 uses, the crashed animation. The machine window
   (clicking a machine; save format 73) calls a handyman (`CallHandymanCommand`, `MachineState.IsRepairCalled`: an
   urgent task, the room lets no patient in until the repair) and replaces the machine at today's price
   (`ReplaceMachineCommand`, after a confirmation). Done 2026-11-12 (cloud): the machines list
   (`MachineMenuWindow`, F10 and a Machines button; machine_menu.lua's columns and sorts as text), the invulnerable
   machines and repair all machines cheats (`ToggleCheats.InvulnerableMachines`, `Machines.RepairAll`; CorsixTH's repair
   all still cuts strength through `strength_reducible or true`, a bug not copied), the repair announcement
   (`RepairAnnouncer`: machwarn.wav and the room's maint0xx.wav when a machine starts smoking, played as effects). The gear
   icon (4564) turns over a machine with a repair called (PR #271); num_explosions is `Hospital.Explosions` (save format 86;
   CorsixTH never reads it). A crashed room cannot be edited, as in CorsixTH (game_ui.lua skips crashed rooms,
   `EditRoomCommand` refuses them); with config.txt's
   remove_destroyed_rooms a click on it in edit mode offers to clear it away for twice its cost less 40 % of its
   needed objects (`RemoveDestroyedRoomCommand`, 2026-11-12). Wall soot is outranked by the
   floor soot on the same tiles, as in CorsixTH. Jelly vat done (save format 69): the doctor uses the jelly moulder with the patient like the blood machine
   (`RoomVisits.DoctorUsesMachine`), the patient visible outside phases −3..4 (`MultiObjectUse` invisible phase span)
   and moved to `FinishSecondaryUsePosition` facing `FinishSecondaryFacing` at the in-use phase. The wobble is drawn
   (AnimationEffect.Jelly), and the patient waits for the doctor facing the moulder (2026-11-12: west of one facing north,
   otherwise north, as JellyVatRoom:commandEnteringPatient's IdleAction). Training room done (save format 70): the player puts doctors in (`RoomVisits.SettleInTraining`, only when
   placed): a consultant lectures at the projector and goes to a skeleton or bookcase now and then, replacing any
   consultant already lecturing; other doctors sit on free lecture chairs or leave. `Training.OnEndDay` is trainSkills:
   skill (with millionths kept in `StaffProfile`) and specialism progress (`SpecialismProgress`, `TrainSpecialism`) from
   `TrainingRules` (gbv.TrainingRate, AbilityThreshold, TrainingValue); a student who becomes a consultant leaves. Not
   yet: the promotion advice and "not enough lecture chairs" advice (both since done). Learning on the job done
   2026-11-12 (`Training.LearnOnTheJob`: 0.000003 skill an hour using an object outside the training room, staff room
   and toilets); doctors going back to the room after resting done the same night. No wage change on promotion: CorsixTH
   has none (`parseSkillLevel` only sets the grade; the fair wage grows, so raise requests follow). Autopsies done (save format 71): the fax's Research choice
   (`PatientAnswer.Research`, `PatientBehaviour.CanGoToResearch`) sends a patient asking for a treatment room to the
   research department (`RoomSeek.Research`; it now has a door queue); the room's first researcher leaves research for
   the autopsy machine and uses it with the patient (a doctor-driven machine like the blood machine); `Autopsied`: the
   last treatment room's first undiscovered object gets AutopsyRschPercent, the autopsy may come out (1 in 3, 2 in 3
   with unhappy staff) for AutopsyRepHitPercent of the reputation, and the patient is gone without counting as a death.
   The advice when an autopsy comes out done 2026-11-12 (`Hospital.AutopsiesDiscovered`, S[11][123]; save format 75); CorsixTH
   has no idea mood for autopsied patients (only debug patients use idea1), so none here. Faces done: `StaffFaces` draws drawFace's hair, face and chin from
   QDATA/FACE01V (65x1350, main palette; greyscale like the original) in the hire window (click a name to pick whose
   portrait shows) and the staff window. The list is done. DNA fixer done (save format 72): aliens
   (`HumanoidType.AlienMalePatient`/`AlienFemalePatient`, `PatientLook.Alien`) walk with their own animations, use a
   standard patient's at every object (`ObjectDefinition.LooksLike`) and at normal doors, and have swing door ones
   (3628/3626); they come only by emergency (`OnlyEmergency`, already there). The room (`RoomVisits.DnaFixerRoom`)
   takes only a researcher (`IsQualifiedFor`), who works the console while the patient, made the standard patient
   they turn into, uses the fixer once. Morphs: `UseAnimations.Morph` (`UseMorph`: into, rounds, mirrored, object
   visible) and `FlipFinish`; `Humanoid.MorphAnimation` is saved; the client draws CorsixTH's draw_morph (a line
   rising from the bottom of both animations; `AnimationRenderer.Extent`, `DrawFrameRows`, `SpriteAtlas.DrawRows`).
   Seen live with `--load` + `--script`. Early list done (2026-11-12, MSI): `ObjectOrientation.EarlyList` (dna fixer and
   autopsy north, bed north and east, lecture chair north, X-ray viewer, cast remover and slicer east) and their users are
   drawn with the tile's north wall (`MapRenderer`'s `drawEarlyEntities`, level_map::draw's oEarlyEntities), so a patient
   at the north fixer's use position stands in front of its base. Transparent walls done 2026-11-13 (issue #94): X held or Shift+X toggled draws every wall at half alpha
   (GameUI:setWallsTransparent, ingame_setTransparent/ingame_toggleTransparent hotkeys); our own addition: while an object or
   a member of staff is being put down, the walls up to three tiles in front of that tile are half transparent too; rats are shot by clicking them
   now, as Rat:onClick, since X is CorsixTH's transparency key. North room doors, swing doors included, are in the early list too
   (door.lua's Door:setAnimation; 2026-11-13).

Rooms' requirements (2026-11-11): when a room of the player's becomes active, `AdviserChecks` says what its
`roomFinished` says in CorsixTH: a doctor for the GP, a nurse for the pharmacy or ward, a psychiatrist, a consultant
for training, a researcher for research, a ward and two surgeons for the operating theatre (S[11][79..88], [105]).
CorsixTH's DNA fixer says nothing, so neither does this.
Staff put down in a room (2026-11-12): `AdviserChecks.StaffPlacement` says what `Room:onHumanoidEnter` and the staff
classes' `adviseWrongPersonForThisRoom` say: only researchers (research, DNA fixer), surgeons, psychiatrists, nurses
or doctors work there, doctors or nurses cannot work in a room (S[11][124..130]), and a researcher or ward nurse put
into a room full of them needs a desk of their own (CorsixTH's warnings.researcher_needs_desk_*, nurse_needs_desk_*).

Dying (2026-11-11, save format 74): a dead patient (health run out out of a room, or a failed treatment once out of
the room) takes one step aside, then falls east or south at random and goes to heaven: rise, wings, hands, and flying up
4 pixels a tick for 30 ticks (die.lua's `action_die_tick`; `PatientPhase.Dying`, `DyingStage`, `GameContent.DeathAnimations`,
aliens included; stripped and gowned patients die as the standard man or woman). The trip to hell (save format 75,
`GrimReaperBehaviour`, `HumanoidType.GrimReaper`, `BehaviourKind.GrimReaper`): 65 % of the men (bloaty heads excepted) lie
as a standard man while the grim reaper rises near the gates to hell (drawn from his behaviour, not a placed object),
walks to his place and waits; the patient gets up, walks in and falls; he swings, leaves and the hole closes. The hole
blocks its tile (not passable or buildable, its sides not buildable, as gates_to_hell.lua's footprint) until it closes,
and is only opened where it leaves a way round (die.lua's 4-step check; 2026-11-12, cloud). Its lava sounds play while it is open (2026-11-13, `GatesToHellSounds`:
LAVA00*.WAV with showGatesToHell's silences by tick rate).

How the room ports were done (PRs #200-#209): read the room and object .lua, dump frame counts with a throwaway
Formats test (`AnimationSet.Load(RealGameData.Require())`, `Length(FirstFrame(id))`), add the definitions to
BaseGameContent and the idles (and slave parts) to ObjectDrawing, fit the flow into RoomVisits/PatientBehaviour/
DoctorBehaviour (machine flow, general diagnosis flow, multi-staff rooms like the ward), write a Content test with a
small hospital (PatientFlowTests helpers; a disease-only DiseaseRules; the super doctor cheat for specialists), then a
throwaway test saves that state and `--load` + `--script` shots show it live.

Needs MSI (game data or a live look; the cloud has neither):

- Every window and the adviser from the second overnight batch: nothing was seen on screen.
- Rooms with machines or their own animations, which need frame counts from VSTART-1/VFRA-1:
  jelly vat, staff room, training room.
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
