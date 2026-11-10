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
| Diagnosis rooms: the cardiogram's slowed-down monitoring (its time is kept); the VIP's visit to the ward; in psychiatry the king complex change of clothes at the screen and the specialists' dispatch bonus | `cardiogram.lua`, `ward.lua`, `psych.lua` |
| Treatment rooms: fracture clinic, hair restoration, slack tongue, DNA fixer, electrolysis, jelly vat | `rooms/fracture_clinic.lua`, `hair_restoration.lua`, `slack_tongue.lua`, `dna_fixer.lua`, `electrolysis.lua`, `jelly_vat.lua` |
| Facilities: staff room, training room | `rooms/staff_room.lua`, `toilets.lua`, `training.lua`, `research.lua` |
| Autopsies in the research department (the research, the room and its researchers are ported) | `rooms/research.lua`, `objects/autopsy.lua` |
| Editing a finished room back to the blueprint (resize, move the door), destroyed rooms after an explosion (editing objects and taking a room down for half its cost are done) | `dialogs/edit_room.lua`, `room.lua` (`tryToEdit`, `crashRoom`) |
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
- Object state: plants being watered, bins filling (`plant.lua`, `bin.lua`), litter being swept and the
  "need a handyman" advice. Plants drooping (`PlantState`), radiators heating tiles, litter (`Litter`, its ranks)
  and patients dropping rubbish (`Littering`) are done.
- Optional room objects: the "Choose Items" window (counts and prices) and buying them after the furniture step,
  selling objects. Extra objects placed while furnishing are bought and sold back.
- Object-specific use: benches (sitting while queueing), staff-room
  sofa/TV/video game/pool table, training projector and lecture chairs, research desk/computer/autopsy.
- The crash trolley drawn split over two tiles (Partial: drawn from its own tile).
- Special entities: helicopter (emergencies), gates to hell (death) (`objects/helicopter.lua`, `gates_to_hell.lua`).
  Rat holes and rats are done (`Rats`, `Rat`), except clicking a rat (X shoots the one by the cursor), the kill
  cursor and sounds, the debug rat, and rat holes closing when a room is edited or removed.

## 3. Staff and their behaviour

**Has:** profiles (class, skill in thousandths, abilities, wage, looks), candidates for hire
refreshed monthly, `HireStaffCommand` with placement check, monthly wages; receptionist at a desk,
doctor in GP and general diagnosis, nurse in the pharmacy.

- Hire window with faces (names and descriptions are done, `StaffTexts`); picking up and placing staff
  (two steps, cancel puts candidate back) (`dialogs/hire_staff.lua`, `place_staff.lua`, `staff_profile.lua`).
- Staff management screen: list, salaries, raise, fire, train (`fullscreen/staff_management.lua`). Firing and
  answering a raise request are commands only (`FireStaffCommand`, `AnswerRaiseCommand`).
- Staff window: its pick up button, sending home (`dialogs/staff_dialog.lua`). Moving a member of staff is done as a
  command (`MoveStaffCommand`, `StaffMoving`; M and M again in the client).
- Calls dispatcher: rooms, machines, litter and plants request staff by priority and distance;
  partial: a room without staff calls the free member of staff with the best `getPriorityForRoom` score (path,
  waiting patients, emergencies, fatigue), unless another waiting room scores better for them; there is no call
  queue object, specialists' bonus, calling staff out of other rooms (`staff_allowed_to_move`) or dispatching of
  handymen and vaccinations (`calls_dispatcher.lua`, `humanoid_actions/answer_call.lua`, `call_checkpoint.lua`).
- Handyman: sweeping litter and vomit, watering plants, repairing machines, task priorities
  (`entities/humanoids/staff/handyman.lua`, `humanoid_actions/sweep_floor.lua`).
- Going to the staff room and resting there, the raise request dialog (`staff_rise.lua`; the requests, the
  policy's automatic raise and quitting are in `StaffEmployment`). Happiness is
  done for warmth, pay, nearby objects, a room's windows and space and the highest salary (`Feelings`), fatigue
  for working and waiting for a staff room (`Tiredness`); resting, being on call, the no-rest cheat and the mood
  icons are not.
- Staff room idle actions (`meander.lua`, `use_staffroom.lua`, `seek_staffroom.lua`). Doctors, nurses, receptionists
  and handymen without a job meander (`Meandering`), so do the pharmacy's nurse and the general diagnosis doctor
  between patients, handymen within their parcel (`SetHandymanParcelCommand`; no staff window yet).
- Training: training room, consultant teaching, skill growth, gaining abilities (`rooms/training.lua`).
- Surgeons, psychiatrists, researchers actually being required by rooms.
- Multiple desks per receptionist logic. (Hiring a receptionist straight onto a desk is done.)
- Tiredness animations (yawn, check watch, tap foot) (`humanoid_actions/yawn.lua`, `check_watch.lua`, `tap_foot.lua`).
  Staff speed (juniors, consultants, tiredness, the no-rest cheat) is done (`Tiredness.UpdateSpeed`).
- Picking staff up with the hand cursor (the grab cursor, the staff drawn under it, `pickup.wav`); the move itself is
  done (`MoveStaffCommand`), but not while the member of staff is treating a patient with an object.

## 4. Patients, diseases, epidemics, VIPs, emergencies, disasters

**Has:** arrivals by the `popn` schedule, disease chosen from the level's diseases, reception queue,
GP visits and diagnosis progress, waiting/going home by default policies, payment (insurance two months
later), treatment in the pharmacy with cure or death; all diseases and pseudo diseases in `DiseaseCatalog`.

- Looks of visual diseases: Partial, only bloaty head; the other humanoid types (slack tongue, invisible,
  transparent, alien, chewbacca/hairyitis, elvis/king complex, etc.) are missing from the sim and client
  (`entities/humanoids/patient.lua`, `diseases/*.lua`).
- Diagnosis by the other diagnosis rooms (`diagnosis/diag_*.lua`); treatment of all diseases except
  those cured in the pharmacy.
- Patient needs: boredom, leaving angry, complaining (warmth and its
  effect on happiness, prices changing happiness, health dropping, patients giving up, and thirst and the need for
  the toilet growing (`Needs`), thirsty patients buying a can from the drinks machine, vomiting and peeing on the floor, and going to the toilets, are done) (`entities/humanoids/patient.lua`, `humanoid_actions/seek_toilets.lua`, `pee.lua`, `vomit.lua`).
- Health and death: dying in place with the death animation, grim reaper and gates to hell, the dying mood
  icons and the health chart (`PatientBehaviour.Health` drops and patients die, but simply vanish) (`humanoid_actions/die.lua`, `entities/humanoids/grim_reaper.lua`).
- Queue features: benches and the queue window itself (`queue.lua`, `dialogs/queue_dialog.lua`). Priorities for VIPs,
  the inspector and emergency patients, rooms taking patients from other rooms' queues (`TryToFindNearbyPatients`), and
  the window's size buttons, moving patients in the queue and sending a patient to another room of the same type as
  commands (`ChangeQueueSizeCommand`, `MoveInQueueCommand`, `SendPatientToRoomCommand`) are done.
- Patient window: health, happiness, thirst, warmth, diagnosis progress, the health graph and treatment history
  drawn (`dialogs/patient.lua`); what they show is kept (`PatientBehaviour.HealthHistory`, `TreatmentHistory`, and
  the sad mood `IsSad`). Its send home and guess cure buttons are done as commands (`SendPatientHomeCommand`,
  `GuessPatientCureCommand`; Q and Shift+Q in the client).
- The fax window for patients asking what to do, its texts (what to build or hire) and the research choice, and the
  policy window (`fullscreen/hospital_policy.lua`, `fax.lua`, `player_hospital.lua`). The policies themselves
  (`HospitalPolicies`, `SetPolicyCommand`) and the questions with their send home, wait and guess cure answers
  (`PatientBehaviour.Question`, `AnswerPatientCommand`; K, Shift+K and Ctrl+K in the client) are done.
- Emergencies: the end-of-emergency fax and announcements, the helicopter's sound and depth sorting (`world.lua`,
  `objects/helicopter.lua`). The planned and random emergencies, the fax offer, helicopter, watch, bonus and
  reputation are done (`Emergencies`).
- Epidemics: clicking patients to vaccinate them (J picks every infected patient for now) and the vaccination
  cursor, vaccinating patients on benches, the infected mood icons, the result fax, the adviser's warnings, the
  announcements and the inspector's, and showing the infected by cheat (`epidemic.lua`,
  `entities/humanoids/inspector.lua`). Contagious patients, the spreading, the declare or cover-up fax (I / Shift+I),
  the cover-up watch, vaccination by nurses (`VaccinateCommand`), the inspector and the fines, compensation and
  evacuation are done (`Epidemics`, `InspectorBehaviour`).
- VIP visits: the faxes asking and reporting, the arrival announcement and judging machines
  (`entities/humanoids/vip.lua`). The visit, the rating (with the litter seen), its grant and reputation and
  the year's trophy are done (`VipVisits`, `VipBehaviour`; V and Shift+V answer the invitation).
- Earthquakes damaging machines and knocking patients over (`earthquake.lua`); planning, the warning and main
  quakes and the screen shake are done (`Earthquakes`).
- Vomit and pee, soot from explosions, hospital cleanliness (`humanoid_actions/vomit.lua`, `pee.lua`, `room.lua`);
  patients dropping rubbish, people minding litter and rats are done (`Littering`, `Feelings`, `Rats`).
- Patient AI in multi-hospital games: choosing between hospitals by reputation and price (`gbv.AllocDelay`,
  `AllocRand`, `AllocIndRep`), which CorsixTH has commented out; today the hospitals take turns, as in CorsixTH.
- Falling, shaking a fist, on-ground and get-up animations (`humanoid_actions/falling.lua`, `shake_fist.lua`, `on_ground.lua`, `get_up.lua`).
- Floating dollar signs are done for treatments and cans from the drinks machine.

## 5. Economy, bank, loans, prices

**Has:** balance and transaction log per player, level costs of rooms and objects, room purchase
rules, wages, patient payments with inflation and insurers, drug cost.

- The bank manager and bank statement screens as the original draws them (`fullscreen/bank_manager.lua`,
  `bank_statement.lua`). Loans, their limit, interest and overdraft interest are done, and F1 and F2 show them as text
  windows with buttons (`BankManagerWindow`, `BankStatementWindow`).
- The casebook as the original draws it (`fullscreen/drug_casebook.lua`); F5 is a text window with prices to set and
  research to concentrate (`CasebookWindow`).
- Reputation: changes from autopsies, room explosions, the year-end award and staff kicked out;
  the price's effect on patients' happiness (`hospital.lua`). Cures, deaths, patients sent home or put off by the
  price, prices far from what patients expect, and the price rise above 500 are done.
- Annual report screen (statistics, rankings, trophy and award plaques); trophies and awards for drinks sold,
  rats, plants, staff and patient happiness, cleanliness, emergencies, waiting times, machines and research; the
  salary ranking. The hospital's value, the year's counts, the player's salary (`Hospital.PlayerSalary`, with the
  level 3 rise and the offer on winning) and the other trophies and awards are done.
- Town map screen as the original draws it (`fullscreen/town_map.lua`); F4 is a text window with counts, heating and
  parcels to buy (`TownMapWindow`). Parcels nobody owns showing as grass, divider walls, buying parcels
  (N in the client), their prices, the heating level (T in the client), the heating bill and the boiler breaking
  down are done, and so is the temperature of tiles (`th_map.cpp`'s `updateTemperatures`, `TileTemperatures`, the
  O overlay), and people and plants feel it, and the adviser warns when the boiler breaks.
- The research policy screen as the original draws it (`fullscreen/research_policy.lua`); F6 sets the shares in a
  text window (`ResearchWindow`).
- The graphs screen with the original's sprites (`fullscreen/graphs.lua`); F8 draws the lines of the monthly figures
  (`Hospital.Statistics`, `GraphWindow`).

## 6. Interface, windows, faxes, adviser

**Has:** isometric map view, camera scroll and zoom, overlays (`--overlay`), date and speed box,
balance in the top line, status line, build room tool and its blueprint drawing, sprite viewer,
MMPX smoothing, and windows over the map (`Client/Ui`): text stand-ins for CorsixTH's dialogs, with buttons that
send commands. Every window below exists in that form; what is missing is the original's look (panels, sprites,
fonts, faces) and the details listed.

- Bottom panel as the original draws it, with message icons (`dialogs/bottom_panel.lua`); the text panel has the
  balance, the dialogs' buttons, the faxes' count and the adviser's line.
- Top menu bar (`dialogs/menu.lua`); Esc opens a menu window with the file items, options, charts and debug.
- Build room window with room list and prices (B is a list for now), furnish corridor window, room edit panel
  (`dialogs/build_room.lua`, `furnish_corridor.lua`, `edit_room.lua`, `place_objects.lua`); the corridor window and the
  edit panel are text windows. Resizing a finished room or moving its door is not done.
- Hire, staff, patient, queue and watch windows are text windows; the machine window is missing (`dialogs/*.lua`).
- Fullscreen dialogs (`dialogs/fullscreen/*.lua`) are text windows; the hall of shame is missing.
- Faxes are a text window opened by a new fax (`fullscreen/fax.lua`); the message icons are not (`dialogs/message.lua`).
- Adviser: the speaking figure and the announcer (`dialogs/adviser.lua`, `announcer.lua`); the queue, history and much
  of the advice are done (`Adviser`, `AdviserChecks`, `AdviserHistoryWindow`).
- Information, confirm and tip of the day dialogs are done as text windows (`dialogs/information.lua`,
  `confirm_dialog.lua`, `resizables/tip_of_the_day.lua`).
- Cursors per action (build, pick up, hand, queue) from the original's sprites; tooltips beyond the bottom panel's
  (windows' cells can carry one, `WindowCell.Tooltip`, drawn above the cursor; the bottom panel's buttons have
  CorsixTH's `tooltip.toolbar` texts with the hotkeys file's keys); object and humanoid highlighting on hover; hover
  info.
- Strings and languages: CorsixTH's language files and their mapping of the original strings, translations, and
  showing them in windows (`strings.lua`, `languages/`). Reading the original's `LANG-*.DAT` is done (`StringTable`), and
  so are staff names, descriptions and professions (`StaffTexts`; the client's tile line shows them under the cursor),
  disease texts and VIP names (`OriginalTexts`, in the status line).
- Subtitles for the announcer (`dialogs/subtitles.lua`).
- The hotkey assignment dialog (`resizables/hotkey_assign.lua`); the keys themselves come from `hotkeys.txt` (`Hotkeys`).

## 7. Levels, goals, campaign

**Has:** loading original `LEVEL.Lx` maps and `.SAM` level settings (start cash, prices, staff
rules, `popn`, disease frequencies and availability, win and lose criteria), switching levels with Page Up/Down.
Hospitals win and lose by the level's criteria (`endconditions.lua`), and the progress report's content is computed.

- Win and lose screens and faxes: the letter offering the next level, declining it, the lose movie
  (`world.lua`), and the progress report dialog and its advice (`fullscreen/progress_report.lua`).
- Remaining level settings:
  research settings, `#gbv`
  values beyond the ones read now.
- Playing a campaign through, the original's (12 levels in order) or a CorsixTH one (the fax offering the next level,
  `world.lua`'s winning letter, `fax.lua`), choosing a custom level or campaign in a menu, briefings shown (`app.lua`,
  `Campaigns/`, `Levels/`). Loading a custom level (`.level` + `.map`, with overlays) and reading a campaign file are
  done (`LevelMapLoader.LoadCustom`, `LoadCampaign`, `--map`, `--campaign`). The difficulty and what it changes (`LevelRules.Difficulty`, `--difficulty`)
  are done, except the ward's use of it, as there is no ward yet.
- Free build mode (Done in the simulation: `LevelRules.WithFreeBuild`, `free_build_mode` in `config.txt` for `--map`
  levels outside a campaign, `--free-build` on the server; the client shows no free build texts yet: the progress
  report's and the VIP and emergency faxes' remarks, `progress_report.lua`, `player_hospital.lua`). Sandbox options
  beyond it are not done.
- Main menu (`resizables/main_menu.lua`): the game still starts straight into the first level. The new game dialog
  (Menu, New game: player name, the installed difficulties, Start from the campaign's first level, `NewGameWindow`) is
  done, without the tutorial and the player name's text box (`resizables/new_game.lua`).
- AI hospitals (Partial: `computer[n]` opponents take their turns for patients and count them as
  visitors, as CorsixTH's `AIHospital` does; like CorsixTH's, they build nothing and play no part in
  the league, awards or end conditions; `hospitals/ai_hospital.lua`).
- Intro and level-end movies (`movie_player.lua`).

## 8. Saves

**Has:** explicit versioned save format (version 9) of the whole simulation, used for network joins
and state hashing; tests for save/load round trips.

- Save and load windows, save slots, the file browser (the autosave frequency setting is done, `autosave_frequency`) (`resizables/file_browser.lua`, `file_browsers/`). The quick save, monthly autosaves, `--load` and
  `--continue` are done (`SaveGameStore`).
- Loading CorsixTH saves (likely never: CorsixTH persists the Lua heap) and original `SAVE*.SAV`.
- Replays: local games are recorded (initial save plus command log) and can be saved and watched from the menu,
  checked against the recorded state hash; not yet for network games, nor seeking or rewinding.

## 9. Sound and music

**Has:** the sound archive reader (`SoundArchive`, `ThemeHospitalInstall.LoadSounds`: `SOUND/DATA/SOUND-n.DAT` with
the English fallback, names, WAV data, durations, wildcard names; `Src/th_sound.cpp`, `audio.lua`); the music list
and XMI to MIDI conversion (`ThemeHospitalInstall.MusicTracks`, `Xmi.ToMidi`; `audio.lua`'s `Audio:init`,
`Src/xmi2mid.cpp`). The client plays sound effects (`SoundEffects` over MonoGame, `SoundChoice` for wildcards and
the distance falloff of `sound_player::play_at`; `play_sounds` and `sound_volume` in `config.txt`) for the actions it
has: the build tool's door and window clicks, placing and turning objects, hiring and picking up and putting down
staff, buying a parcel, shooting a rat, sending a patient home (positional).

- Sound effects the simulation causes: objects, doors (`eledoor2.wav`), footsteps, machines, the earthquake
  (`quake.wav`), cheers and boos at the year end, vaccination (`vaccin.wav`), patients' coughs and other idle sounds;
  the interface's hover and button sounds once there are windows; announcer speech; adviser voice;
  `play_announcements` and `announcement_volume`.
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
- The rest of the config file (`config_finder.lua`): announcements, music, movies, `right_mouse_scrolling` (the
  right and middle buttons both drag), `capture_mouse`, the options dialog's game rules, `config.path.txt`; game
  data folder selection (`resizables/folder_settings.lua`, `directory_browser.lua`). `config.txt` with the language,
  window, screen shake, autosave frequency, game folder, player name, sound effects and scrolling
  (`prevent_edge_scrolling`, `scrolling_momentum`, `zoom_speed`, `scroll_speed`, `shift_scroll_speed`) is done
  (`GameConfig`).
- Hotkeys file (`hotkeys.txt`): read, and written with the defaults when missing, for the dialogs (F1–F9, Shift+F3,
  Ctrl+H, Y, C, F11), pause, speed up, quick save/load and the adviser toggle (`Hotkeys`, `HotkeyInput`). Still on
  fixed keys: the camera (zoom, original size, remembering and recalling views, CorsixTH's default keys), scrolling,
  the speeds, Esc, and this game's stand-in keys; CorsixTH's other actions (sound, music, announcements toggles,
  screenshots, logs) are not read.
- Update check (`resizables/update.lua`).

## 12. Map editor and tools

**Has:** sprite viewer (`--sprite-viewer`), map overlays for debugging.

- Map editor: painting floor, walls, parcels, spawn points (`resizables/map_editor.lua`).
- Cheats dialog (`resizables/cheats_dialog.lua`) and the fax code that opens it; the cheats for research,
  showing the infected and machines, and the fax codes themselves (`cheats.lua`). Money, a patient, winning, losing,
  prices, the death count, the reputation, the earthquake, the VIP, the emergency, the epidemic, the epidemics
  toggle, the end of the month and year cheats and the fax code toggles (Roujin's challenge, no rest, queue jump,
  super doctors) are done (`CheatCommand`), with no key in the client.
- Lua console equivalent / debug console (`resizables/lua_console.lua`), debug patient spawning
  (Partial: debug keys H, Ctrl+1-4).
- AnimView (separate tool in the CorsixTH repo); partly covered by the sprite viewer.

## 13. Rendering

**Has:** map layers, walls with shadows, windows, doors, objects and humanoids with layers and
mirroring, blueprint drawing, bitmap fonts, palette remaps.

- Wall transparency (walls cut away), animated map elements.
- Smoke from broken machines, explosion animations. (Screen shake during earthquakes and the
  `enable_screen_shake` option are done.)
- Video playback (Smacker intro/win/lose movies).
