# Porting gaps: CorsixTH features not yet in CorsixNet

State as of 2026-11-08: CorsixNet `main` at `ff588c1` (PR #7, room doors and wandering, merged),
compared with the CorsixTH checkout at `1a1460f` (`C:\repos\github\CorsixTH`). Paths in the
right column are CorsixTH's, under `CorsixTH/Lua/` unless they say otherwise.

Each section starts with a **Has** line (what CorsixNet already does), then lists what is missing.
"Partial" items say briefly what is left. Update this file when a gap closes.

## 1. Rooms

**Has:** building in the original's steps (blueprint, door, windows, furniture, confirm), inactive
rooms, cancel; three working room types: GP's office, general diagnosis, pharmacy.

| Missing | CorsixTH source |
|---|---|
| Diagnosis rooms: X-ray, scanner, ultrascan, blood machine, cardiogram, psychiatry (also a treatment room), ward (also treatment) | `rooms/x_ray_room.lua`, `scanner_room.lua`, `ultrascan.lua`, `blood_machine_room.lua`, `cardiogram.lua`, `psych.lua`, `ward.lua` |
| Treatment rooms: operating theatre, inflation, fracture clinic, hair restoration, slack tongue, DNA fixer, electrolysis, jelly vat, decontamination | `rooms/operating_theatre.lua`, `inflation.lua`, `fracture_clinic.lua`, `hair_restoration.lua`, `slack_tongue.lua`, `dna_fixer.lua`, `electrolysis.lua`, `jelly_vat.lua`, `decontamination.lua` |
| Facilities: staff room, toilets, training room, research department | `rooms/staff_room.lua`, `toilets.lua`, `training.lua`, `research.lua` |
| Room availability by level (`rooms[n]` / `towns` settings) and by research (rooms to be discovered) | `world.lua` (`available_rooms`), `research_department.lua` |
| Editing a finished room (back to blueprint: resize, move the door; add, move or remove objects), deleting a room | `dialogs/edit_room.lua`, `room.lua` (`tryToEdit`, `crashRoom`) |
| Room closing (staff/patients leave while editing), "room is crashed" state after an explosion | `room.lua` |
| Room-specific rules: doors that can't be used while busy (swing doors of the operating theatre, ward), rooms that need two staff (operating theatre), max patients per room (ward beds) | `rooms/*.lua`, `objects/doors/*` |
| Room information windows (staff in room, queue length, room "health") | `dialogs/queue_dialog.lua`, `dialogs/information.lua` |

## 2. Objects

**Has:** footprints and placement rules (`ObjectPlacer`), pick up, use animations (single and
two-person), corridor objects (reception desk, bench, drinks machine, fire extinguisher, radiator,
bin, plant; side objects; the corridor path check), bought one at a time with F and Tab; objects
of the three rooms (desk, cabinet, chair, pharmacy cabinet, screen, crash trolley). Map-placed
objects from the level file.

- Furnish corridor window with counts and prices (`dialogs/furnish_corridor.lua`); the bench drawn
  split over three tiles as CorsixTH does (drawn from its own tile for now).
- Side objects inside a room's area when the room is built, and moving or selling corridor objects.
- Every object of the missing rooms (`objects/*.lua`), including the 15 machines in `objects/machines/`.
- Machines: strength, wear per use, breakdown, smoking, explosion (room crash), repair by handymen,
  replacement and upgrade (`entities/machine.lua`, `dialogs/machine_dialog.lua`, `resizables/machine_menu.lua`).
- Object state: plants wilting and being watered, bins filling, radiators heating tiles, litter
  (`objects/litter.lua`, `plant.lua`, `bin.lua`).
- Optional room objects: buying extra items in the furniture step and later (room's "Choose Items"
  list with counts and prices), selling objects.
- Object-specific use: benches (sitting while queueing), drinks machine (thirst), toilets, staff-room
  sofa/TV/video game/pool table, training projector and lecture chairs, research desk/computer/autopsy.
- The crash trolley drawn split over two tiles (Partial: drawn from its own tile).
- Special entities: helicopter (emergencies), gates to hell (death), rat holes and rats
  (`objects/helicopter.lua`, `gates_to_hell.lua`, `rathole.lua`, `entities/rat.lua`).

## 3. Staff and their behaviour

**Has:** profiles (class, skill in thousandths, abilities, wage, looks), candidates for hire
refreshed monthly, `HireStaffCommand` with placement check, monthly wages; receptionist at a desk,
doctor in GP and general diagnosis, nurse in the pharmacy.

- Hire window with faces, names and descriptions from the language files; picking up and placing staff
  (two steps, cancel puts candidate back) (`dialogs/hire_staff.lua`, `place_staff.lua`, `staff_profile.lua`).
- Staff management screen: list, salaries, raise, fire, train (`fullscreen/staff_management.lua`).
- Staff window: picking up and moving a member of staff, sending home (`dialogs/staff_dialog.lua`, `humanoid_actions/pickup.lua`).
- Calls dispatcher: rooms, machines, litter and plants request staff by priority and distance;
  partial: today a room just sends for the nearest free member of staff (`calls_dispatcher.lua`, `humanoid_actions/answer_call.lua`, `call_checkpoint.lua`).
- Handyman: sweeping litter and vomit, watering plants, repairing machines, task priorities
  (`entities/humanoids/staff/handyman.lua`, `humanoid_actions/sweep_floor.lua`).
- Fatigue, happiness, going to the staff room, wage demands and raises (`staff_rise.lua`), quitting.
  Partial: service quality uses fatigue 0 and happiness 1.
- Staff meandering when idle (Partial: wanderers in `--demo` use `MeanderCommand`; staff waiting in a
  room stand still), staff room idle actions (`use_staffroom.lua`, `seek_staffroom.lua`).
- Training: training room, consultant teaching, skill growth, gaining abilities (`rooms/training.lua`).
- Surgeons, psychiatrists, researchers actually being required by rooms.
- Receptionist hired straight onto a desk; multiple desks per receptionist logic.
- Staff speed, tiredness animations (yawn, check watch, tap foot) (`humanoid_actions/yawn.lua`, `check_watch.lua`, `tap_foot.lua`).
- Picking staff up with the hand cursor and dropping them in another room.

## 4. Patients, diseases, epidemics, VIPs, emergencies, disasters

**Has:** arrivals by the `popn` schedule, disease chosen from the level's diseases, reception queue,
GP visits and diagnosis progress, waiting/going home by default policies, payment (insurance two months
later), treatment in the pharmacy with cure or death; all diseases and pseudo diseases in `DiseaseCatalog`.

- Looks of visual diseases: Partial, only bloaty head; the other humanoid types (slack tongue, invisible,
  transparent, alien, chewbacca/hairyitis, elvis/king complex, etc.) are missing from the sim and client
  (`entities/humanoids/patient.lua`, `diseases/*.lua`).
- Diagnosis by the other diagnosis rooms (`diagnosis/diag_*.lua`); treatment of all diseases except
  those cured in the pharmacy.
- Patient needs: health dropping over time, warmth, thirst, toilets, happiness, boredom, leaving angry,
  complaining (`entities/humanoids/patient.lua`, `humanoid_actions/seek_toilets.lua`, `pee.lua`, `vomit.lua`).
- Health and death: dying in place with the death animation, grim reaper and gates to hell, patients
  getting worse (`humanoid_actions/die.lua`, `entities/humanoids/grim_reaper.lua`).
- Queue features: priorities (emergencies, VIP), benches, queue window to reorder or send home,
  patients rerouting to shorter queues (`queue.lua`, `dialogs/queue_dialog.lua`).
- Patient window: health, happiness, thirst, warmth, diagnosis progress, send home, guess cure
  (`dialogs/patient.lua`).
- Faxes asking what to do with a patient (guess cure, send home, wait), patient policy thresholds
  (`fullscreen/hospital_policy.lua`, `fax.lua`). Partial: default policy values are applied.
- Emergencies: helicopter, timed group of patients with one disease, bonus or penalty
  (`world.lua` `nextEmergency`, `objects/helicopter.lua`).
- Epidemics: contagious diseases spreading between patients, inspector, vaccination by nurses, cover-up
  fax and fine (`epidemic.lua`, `humanoid_actions/vaccinate.lua`, `entities/humanoids/inspector.lua`).
- VIP visits and their verdict, affecting reputation (`entities/humanoids/vip.lua`, `humanoid_actions/vip_go_to_next_room.lua`).
- Earthquakes damaging machines (`earthquake.lua`).
- Rats, litter and vomit, hospital cleanliness (`entities/rat.lua`, `objects/litter.lua`).
- Patient AI in multi-hospital games: choosing between hospitals by reputation; today every patient goes
  to the first hospital.
- Falling, shaking a fist, on-ground and get-up animations (`humanoid_actions/falling.lua`, `shake_fist.lua`, `on_ground.lua`, `get_up.lua`).
- Floating dollar signs when a patient pays.

## 5. Economy, bank, loans, prices

**Has:** balance and transaction log per player, level costs of rooms and objects, room purchase
rules, wages, patient payments with inflation and insurers, drug cost.

- Loans, interest, overdraft interest, credit limit, bank manager screen (`fullscreen/bank_manager.lua`, `hospital.lua`).
- Bank statement window showing the transaction log (data exists, no UI).
- Casebook: per-disease price percentage, concentrate research, drug ratings (`fullscreen/drug_casebook.lua`).
  Partial: price percent is fixed at 100 %.
- Reputation: changes from VIPs, emergencies, autopsies, room explosions, the year-end award and staff kicked out;
  the price's effect on patients' happiness (`hospital.lua`). Cures, deaths, patients sent home or put off by the
  price, prices far from what patients expect, and the price rise above 500 are done.
- Annual report screen (statistics, rankings, trophy and award plaques); trophies and awards for drinks sold, VIPs,
  rats, plants, staff and patient happiness, cleanliness, emergencies, waiting times, machines and research; the
  level 3 salary rise. The hospital's value, the year's counts and the other trophies and awards are done.
- Buying land: plot purchase from the town map, plot prices (`fullscreen/town_map.lua`).
- Heating costs and heating level (`fullscreen/town_map.lua`, radiators).
- Research spending and its effect on drug ratings and new equipment (`research_department.lua`, `fullscreen/research_policy.lua`).
- Graphs of money, visitors, cures, deaths, reputation over time (`fullscreen/graphs.lua`).

## 6. Interface, windows, faxes, adviser

**Has:** isometric map view, camera scroll and zoom, overlays (`--overlay`), date and speed box,
balance in the top line, status line, build room tool and its blueprint drawing, sprite viewer,
MMPX smoothing; everything else is debug keys.

- Bottom panel with its buttons, message queue and money/date display (`dialogs/bottom_panel.lua`).
- Top menu bar (file, options, display, charts, debug) (`dialogs/menu.lua`).
- Build room window with room list and prices, furnish corridor window, room edit panel
  (items, confirm, pick-up, buy) (`dialogs/build_room.lua`, `furnish_corridor.lua`, `edit_room.lua`, `place_objects.lua`).
- Hire, staff, patient, machine, queue, watch (countdown) windows (`dialogs/*.lua`).
- Fullscreen dialogs: annual report and awards/hall of shame, bank manager, casebook, fax, graphs,
  policy, progress report, research policy, staff management, town map (`dialogs/fullscreen/*.lua`).
- Faxes and message icons in the bottom panel (`dialogs/message.lua`, `fullscreen/fax.lua`).
- Adviser with speech, tips, adviser history (`dialogs/adviser.lua`, `resizables/adviser_history.lua`, `announcer.lua`).
- Information dialogs, confirm dialogs, tip of the day (`dialogs/information.lua`, `confirm_dialog.lua`, `resizables/tip_of_the_day.lua`).
- Cursors per action (build, pick up, hand, queue) from the original's sprites; tooltips; object and
  humanoid highlighting on hover; hover info.
- Strings and languages: reading the original's `LANG-*.DAT` and CorsixTH's language files, translations (`strings.lua`, `languages/`).
- Subtitles for the announcer (`dialogs/subtitles.lua`).
- Hotkeys and their assignment dialog (`resizables/hotkey_assign.lua`).

## 7. Levels, goals, campaign

**Has:** loading original `LEVEL.Lx` maps and `.SAM` level settings (start cash, prices, staff
rules, `popn`, disease frequencies and availability), switching levels with Page Up/Down.

- Win and lose criteria (`#win_criteria`, `#lose_criteria`: reputation, balance, cured %, value, deaths),
  progress report, level completion and the letter to the next level (`endconditions.lua`, `fullscreen/progress_report.lua`).
- Remaining level settings: emergencies (`#emergency_control`), epidemics, VIP and earthquake schedules,
  `computer[n]` AI players, interest rates and overdraft, research settings, room availability, `#gbv`
  values beyond the ones read now.
- Original campaign (12 levels in order, difficulty choice), CorsixTH campaigns (`.campaign`),
  custom levels (`.level` + `.map`) (`app.lua`, `Campaigns/`, `Levels/`).
- Free build mode and sandbox options (Partial: `LevelRules.Free` exists for missing level files).
- New game dialog, difficulty choice, main menu (`resizables/new_game.lua`, `main_menu.lua`).
- AI hospitals competing for patients (`hospitals/ai_hospital.lua`).
- Intro and level-end movies (`movie_player.lua`).

## 8. Saves

**Has:** explicit versioned save format (version 9) of the whole simulation, used for network joins
and state hashing; tests for save/load round trips.

- Save and load from the UI, save slots, autosave (monthly), quicksave/quickload, file browser
  (`resizables/file_browser.lua`, `file_browsers/`, `app.lua`).
- Loading CorsixTH saves (likely never: CorsixTH persists the Lua heap) and original `SAVE*.SAV`.
- Replays (initial save plus command log) as a feature in the client.

## 9. Sound and music

**Has:** nothing.

- Readers for the original's sound archives (`SOUND-0.DAT` / `.TAB`) and XMI/MIDI music (`audio.lua`, `Src/th_sound.cpp`).
- Positional sound effects for objects, doors, footsteps, machines; announcer speech; adviser voice.
- Music playback and the jukebox (`dialogs/jukebox.lua`), MP3 replacement music, volumes (`resizables/sound_setting.lua`).

## 10. Network

**Has:** lockstep server and client over TCP, joining with a save, desync detection by state hash,
in-process host (`--host`) and headless server.

CorsixTH has no network game, so there is nothing to port here; the original's network game is the
model. Missing relative to the plan in `ARCHITECTURE.md`: land auctions, player-owned plots of other
players on screen, chat, lobby, network game speed control and pause, desync recovery, content check on
join.

## 11. Settings

**Has:** command-line options only (`--level`, `--map`, `--zoom`, `--speed`, `--upscale`, ...).

- Options dialog: resolution, fullscreen, language, audio, scroll speed, edge scrolling, zoom, mouse
  capture, adviser on/off, ... (`resizables/options.lua`, `graphics_settings.lua`, `customise.lua`).
- Config file and its location (`config_finder.lua`), game data folder selection (`resizables/folder_settings.lua`, `directory_browser.lua`).
- Hotkeys file.
- Update check (`resizables/update.lua`).

## 12. Map editor and tools

**Has:** sprite viewer (`--sprite-viewer`), map overlays for debugging.

- Map editor: painting floor, walls, parcels, spawn points (`resizables/map_editor.lua`).
- Cheats dialog (`cheats.lua`, `resizables/cheats_dialog.lua`).
- Lua console equivalent / debug console (`resizables/lua_console.lua`), debug patient spawning
  (Partial: debug keys H, Ctrl+1-4).
- AnimView (separate tool in the CorsixTH repo); partly covered by the sprite viewer.

## 13. Rendering

**Has:** map layers, walls with shadows, windows, doors, objects and humanoids with layers and
mirroring, blueprint drawing, bitmap fonts, palette remaps.

- Wall transparency (walls cut away), animated map elements.
- Screen shake during earthquakes, smoke from broken machines, explosion animations.
- Video playback (Smacker intro/win/lose movies).
