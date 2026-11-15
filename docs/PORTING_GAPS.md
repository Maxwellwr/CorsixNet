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
| Diagnosis rooms: the cardiogram's slowed-down monitoring (its time is kept); in psychiatry the king complex change of clothes at the screen and the specialists' dispatch bonus | `cardiogram.lua`, `ward.lua`, `psych.lua` |
| Facilities: (the staff room and training room are done) | `rooms/staff_room.lua`, `toilets.lua`, `training.lua`, `research.lua` |
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
- (Done: every room of the original with its objects and machines, `BaseGameContent`, `RoomVisits`.)
- Machines: the gear icon over a machine a handyman was called to is drawn. The machines list (`MachineMenuWindow`, F10), the repair announcement (`RepairAnnouncer`, as sound effects) and the
  invulnerable and repair-all cheats are done. Strength, wear per use,
  smoking, explosion (room crash), repair by handymen and the machine window's call and replace (`MachineWindow`,
  `CallHandymanCommand`, `ReplaceMachineCommand`) are done (`Machines`, `HandymanBehaviour`).
- Optional room objects: the "Choose Items" window (counts and prices) and buying them after the furniture step,
  selling objects. Extra objects placed while furnishing are bought and sold back. A new room can come with the player's
  average room's extras (`enable_avg_contents`, `BuildRoomTool.ComputeAverageContents`).
- (Done: benches, the staff room's sofa, video game and pool table, the training room's projector and lecture chairs,
  the research room's desks, computer and autopsy.)
- The crash trolley drawn split over two tiles (Partial: drawn from its own tile).
- Special entities: helicopter (emergencies), gates to hell (death) (`objects/helicopter.lua`, `gates_to_hell.lua`).
  Rat holes and rats are done (`Rats`, `Rat`), except the kill
  cursor and sounds, the debug rat, (rat holes in a room's walls close when it is edited or crashes, `Rats.RemoveRatholesAroundRoom`).

## 3. Staff and their behaviour

**Has:** profiles (class, skill in thousandths, abilities, wage, looks), candidates for hire
refreshed monthly, `HireStaffCommand` with placement check, monthly wages; receptionist at a desk,
doctor in GP and general diagnosis, nurse in the pharmacy.

- Hire window: the original's panel (names, descriptions and portraits are done, `StaffTexts`, `StaffFaces`); picking up and placing staff
  (two steps, cancel puts candidate back) (`dialogs/hire_staff.lua`, `place_staff.lua`, `staff_profile.lua`).
- Staff management screen: done in the original's look (`StaffManagementScreen`: tabs, pages, ordering by a heading,
  bonus, pay rise, sack, the view, the arrow keys). A raise request comes as a "strike" icon on the bottom panel
  that opens `StaffRiseWindow` (`staff_rise.lua`: Req12V, grant or refuse, Esc does not close it, the game pauses).
- Keys in windows: a window on top in its original look takes the keys it handles (`UiWindow.TakesKey`): the arrows in
  staff management and the casebook, Enter in the confirmation, information, hire and buy objects windows, H (`ingame_patient_gohome`) in the patient
  window. The place objects rotate key is done (`ingame_rotateobject`); its sell key waits for picking up corridor objects.
- Staff window: its pick up button, sending home (`dialogs/staff_dialog.lua`). Moving a member of staff is done as a
  command (`MoveStaffCommand`, `StaffMoving`; M and M again in the client).
- Calls dispatcher: rooms, machines, litter and plants request staff by priority and distance;
  partial: a room without staff calls the free member of staff with the best `getPriorityForRoom` score (path,
  waiting patients, emergencies, fatigue, specialist rooms first), unless another waiting room scores better for them;
  with `staff_allowed_to_move`, staff idle in a room nobody waits for are called to a room with patients waiting.
  There is no call queue object or dispatching of handymen and vaccinations (`calls_dispatcher.lua`, `humanoid_actions/answer_call.lua`, `call_checkpoint.lua`).
- Going to the staff room and resting there (the raise requests, the policy's automatic raise and quitting are in
  `StaffEmployment`, the request dialog in `StaffRiseWindow`). Happiness is
  done for warmth, pay, nearby objects, a room's windows and space and the highest salary (`Feelings`), fatigue
  for working and waiting for a staff room (`Tiredness`); resting (`RestingBehaviour`), being on call in the staff
  room, the no-rest cheat, the tired mood (`Humanoid.IsTiredMoodShown`, with its icon over the head) and researchers and trainees going back to their room after resting are done.
- Staff room idle actions (`meander.lua`, `use_staffroom.lua`, `seek_staffroom.lua`). Doctors, nurses, receptionists
  and handymen without a job meander (`Meandering`), so do the pharmacy's nurse and the general diagnosis doctor
  between patients, handymen within their parcel (`SetHandymanParcelCommand`; no staff window yet).
- Training: learning on the job (`Training.LearnOnTheJob`, `doctor.lua`'s `tick`), the room, teaching, skill growth, gaining abilities and the advice (promotions, a consultant needed, too few lecture chairs; `AdviserChecks`) are done (`Training`).
- (Done: surgeons, psychiatrists and researchers required by their rooms, `RoomVisits.IsQualifiedFor`.)
- Multiple desks per receptionist logic. (Hiring a receptionist straight onto a desk is done.)
- Patients waiting look at the watch, yawn and tap a foot with 30, 20 and 10 days left, and those with sleeping illness yawn one day in ten (`PatientSpotAction`; `humanoid_actions/yawn.lua`, `check_watch.lua`, `tap_foot.lua`). In CorsixTH these are patients' animations only.
  Staff speed (juniors, consultants, tiredness, the no-rest cheat) is done (`Tiredness.UpdateSpeed`).
- Picking staff up with the hand cursor (the grab cursor, the staff drawn under it, `pickup.wav`); the move itself is
  done (`MoveStaffCommand`), but not while the member of staff is treating a patient with an object.

## 4. Patients, diseases, epidemics, VIPs, emergencies, disasters

**Has:** arrivals by the `popn` schedule, disease chosen from the level's diseases, reception queue,
GP visits and diagnosis progress, waiting/going home by default policies, payment (insurance two months
later), treatment in the pharmacy with cure or death; all diseases and pseudo diseases in `DiseaseCatalog`.

- (Done: the looks of the visual diseases, `PatientLook` and its humanoid types.)
- (Done: diagnosis in every diagnosis room and treatment in every treatment room, `RoomVisits`.)
- Patient needs: a patient fed up near death in a room that does not cure them goes home once their visit is over,
  where CorsixTH sends them at once (`PatientBehaviour.IsFedUp`; `entities/humanoids/patient.lua`). The rest
  (warmth, thirst, toilets, happiness, prices, waiting, vomiting) is done (`Needs`, `Feelings`).
- Health and death: the dying mood icons and the death sounds; the gates to hell blocking their tile and CorsixTH's check
  that they leave a way round. Health running down, the step aside, the fall, the trip to heaven and, for 65 % of the men,
  the trip to hell with the grim reaper and the gates to hell are done (`PatientBehaviour.UpdateDying`, `DeathAnimations`,
  `GrimReaperBehaviour`); those killed when a room crashes simply vanish, as in CorsixTH.
- Queue features: benches and the queue window are done. Priorities for VIPs,
  the inspector and emergency patients, rooms taking patients from other rooms' queues (`TryToFindNearbyPatients`), and
  the window's size buttons, moving patients in the queue and sending a patient to another room of the same type as
  commands (`ChangeQueueSizeCommand`, `MoveInQueueCommand`, `SendPatientToRoomCommand`) are done.
- Patient window: health, happiness, thirst, warmth, diagnosis progress, the health graph and treatment history
  drawn (`dialogs/patient.lua`); what they show is kept (`PatientBehaviour.HealthHistory`, `TreatmentHistory`, and
  the sad mood `IsSad`). Its send home and guess cure buttons are done as commands (`SendPatientHomeCommand`,
  `GuessPatientCureCommand`; Q and Shift+Q in the client).
- The fax window for patients asking what to do (its texts, what to build or hire, are the original's: `NoTreatmentRoomLines`, `NoDiagnosisRoomLines`), and the
  policy window (`fullscreen/hospital_policy.lua`, `fax.lua`, `player_hospital.lua`). The policies themselves
  (`HospitalPolicies`, `SetPolicyCommand`) and the questions with their send home, wait and guess cure answers
  (`PatientBehaviour.Question`, `AnswerPatientCommand`; K, Shift+K and Ctrl+K in the client) are done.
- Emergencies: the helicopter's depth sorting (`objects/helicopter.lua`); CorsixTH has no end-of-emergency announcement, and
  the helicopter's sound is its frames' (`FrameSounds`). The planned and random emergencies, the fax offer, helicopter, watch, bonus,
  reputation and the result on the fax window (`HospitalEmergencies.Report`, `makeEmergencyEndFax`) are done
  (`Emergencies`).
- Epidemics: vaccinating patients on benches (`epidemic.lua`). The infected mood icons, the adviser's warnings, the
  announcements and the inspector's, and showing the infected by cheat (`CheatKind.ShowInfected`) are done. Contagious patients, the spreading, the declare or cover-up fax (I / Shift+I),
  the cover-up watch, vaccination by nurses (`VaccinateCommand`), the inspector and the fines, compensation and
  evacuation and the result fax (`HospitalEpidemics.Report`) are done (`Epidemics`, `InspectorBehaviour`). The fax
  telling of a newly discovered disease is done too (`Hospital.DiseaseReports`, `adviseDiscoverDisease`).
- VIP visits: the original's look of
  the asking and reporting faxes. The visit, the rating (with the litter seen), its grant and reputation, the year's
  trophy, the invitation and the report on the fax window (`VipVisits.Report`, `FaxWindow.VipReportLines`,
  `makeVipEndFax`; our English stand-ins for the fifteen remarks without the game's strings) are done (`VipVisits`,
  `VipBehaviour`; V and Shift+V answer the invitation).
- Earthquakes: planning, the warning and main quakes, the screen shake, the rounds of damage wearing machines and knocking
  patients over are done (`Earthquakes`, `Machines.EarthquakeImpact`, `PatientBehaviour.Fall`).
- Vomit and pee, soot from explosions, hospital cleanliness (`humanoid_actions/vomit.lua`, `pee.lua`, `room.lua`);
  patients dropping rubbish, people minding litter and rats are done (`Littering`, `Feelings`, `Rats`).
- Patient AI in multi-hospital games: choosing between hospitals by reputation and price (`gbv.AllocDelay`,
  `AllocRand`, `AllocIndRep`), which CorsixTH has commented out; today the hospitals take turns, as in CorsixTH.
- Floating dollar signs are done for treatments and cans from the drinks machine.

## 5. Economy, bank, loans, prices

**Has:** balance and transaction log per player, level costs of rooms and objects, room purchase
rules, wages, patient payments with inflation and insurers, drug cost.

- Bank manager: drawn as the original's (`BankManagerScreen` over Bank01V and Stat01V: the face's animation, loans,
  insurance with its graph picture, rates, the statement page, tooltips, the dollar and pie chart cursors from
  QData/SPointer). The three insurance companies are picked as CorsixTH does (`Hospital.ChooseInsurers`). F2 opens it on the statement page, and F1 and F2
  turn its pages or close it (`dialogBankCommon`). Fullscreen dialogs now draw themselves where `UiWindow.IsFullscreen` says so (`OriginalGraphics`).
- Casebook: drawn as the original's (`CasebookScreen` over DrugN01V: the list with diagnoses first, the figures, the
  charge as a percentage for a moment after a change, the cure's kind and whether it can be cured, research, repeat
  buttons with Shift and Ctrl, the wheel, clicks on the list, tooltips, the cure requirements' rooms to research and
  build and staff to hire).
- Reputation: changes from the year-end award and staff kicked out;
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

- Bottom panel: drawn as the original's (`Ui/OriginalBottomPanel`: Panel02V buttons with the hover row, balance,
  date, reputation meter, fax door, dynamic info and pause text, fax icons by kind). The fax icons slide out of the door,
  the emergency's siren turns and a right click dismisses a fax with one choice (`FaxDrawer`). The dynamic info's lines and its diagnosis and tiredness bars are drawn (`DynamicInfoLines`). Missing: CorsixTH's
  machine menu button (aux_ui sprites); Machines and the adviser's messages are on their hotkeys only.
- Top menu bar: drawn as the original's (`OriginalMenuBar`: appears near the top, PullDV, file, options (adviser, transparent walls and game speed as check items), charts and debug menus, the cheats menu once unlocked, the clock); CorsixTH's music, warmth colour and wage options, its debug items and submenus are not in it. Esc still opens the menu window too.
- Build room window with room list and prices (B is a list for now), furnish corridor window, room edit panel
  (`dialogs/build_room.lua`, `furnish_corridor.lua`, `edit_room.lua`, `place_objects.lua`); the corridor window and the
  edit panel are text windows. A finished room is resized or gets its door moved by going back to its blueprint (Esc while
  editing, `ReturnToBlueprintCommand`).
- The fax (`FaxScreen` over Fax01V: the first fax waiting with its choices, the keypad; the emergency's, epidemic's and
  patients' texts are still our English stand-ins), the watch (`WatchScreen`, Watch01V; its epidemic button toggles vaccination mode), the build room, room edit (with its buy window), furnish corridor, hire, staff, patient, queue and machine windows are drawn as the original's (the queue's right-click popup sends a patient back to reception or home, `QueuePopupWindow`; `HireScreen` Req11V, `StaffScreen` Req01V,
  `PatientScreen` Req02V, the view circles' close-up of the map, `IWindowHost.CloseUp`); queue and watch windows are text windows, and so is the machine window (`dialogs/*.lua`).
- Fullscreen dialogs (`dialogs/fullscreen/*.lua`) other than the bank manager, the casebook, the policy, research, progress report, graphs, town map and annual report screens are text windows (the progress report's one warning a month is picked from the month on the client, not drawn by the simulation) (the policy's wage requests switch stays in its table only); the hall of fame/shame page is a TODO in CorsixTH too.
- Faxes are a text window opened by a new fax (`fullscreen/fax.lua`); the message icons are not (`dialogs/message.lua`).
- Adviser: done, the figure too (`AdviserFigure`: getting up, talking with the balloon, waiting, getting down,
  clicks on him). The announcer's queue is done (`Audio/Announcer`: priorities,
  expiry, the staffed-desk rule, random announcements, `play_announcements`, `announcement_volume`); the queue, history and much
  of the advice are done (`Adviser`, `AdviserChecks`, `AdviserHistoryWindow`).
- Information and confirm dialogs are drawn as the original's (`InformationScreen` PulldV, `ConfirmScreen` Req04V); the tip of the day is a text window (`dialogs/information.lua`,
  `confirm_dialog.lua`, `resizables/tip_of_the_day.lua`).
- Cursors: the original's (DATA/MPointer, `CursorDrawing`) are drawn for the map's hover cases (patient, staff, queue at a
  door or desk, machine repair, rat, infected patient), editing a room, waiting while it empties, carrying staff and a pressed button; the blueprint's move, resize and arrow cursors are done (`BuildRoomTool.Cursor`), and the bank's SPointer ones (`UiWindow.FullscreenCursor`); people and placed objects are picked by their sprites' pixels (`HumanoidDrawing.HumanoidAt`,
  `ObjectDrawing.ObjectAt`; machines and reception desks use it), doors still by tile. Tooltips for the annual report's
  figures and for objects and people on the map (windows' cells can carry one, `WindowCell.Tooltip`, drawn above the
  cursor; the bottom panel, staff, patient, queue, machine, bank, town map, graphs, research, policy, hire, casebook,
  information, confirm, tip of the day, new game, fax, cheats, adviser history and level end buttons have CorsixTH's `tooltip.*` texts, from the original's strings when
  loaded, `OriginalTooltips`); object and humanoid highlighting on hover. Hover info done as the second status line (`DynamicInfo`: the
  bottom panel's dynamic info of the patient, member of staff, VIP, inspector or machine under the cursor, S[59] texts).
- Strings and languages: CorsixTH's language files and their mapping of the original strings, translations, and
  showing them in windows (`strings.lua`, `languages/`). Reading the original's `LANG-*.DAT` is done (`StringTable`), and
  so are staff names, descriptions and professions (`StaffTexts`; the client's tile line shows them under the cursor),
  disease texts and VIP names (`OriginalTexts`, in the status line).
- (Done: subtitles for the announcer, `enable_announcer_subtitles`, `Subtitles`.)

## 7. Levels, goals, campaign

**Has:** loading original `LEVEL.Lx` maps and `.SAM` level settings (start cash, prices, staff
rules, `popn`, disease frequencies and availability, win and lose criteria), switching levels with Page Up/Down.
Hospitals win and lose by the level's criteria (`endconditions.lua`), and the progress report's content is computed.

- Win and lose screens: the lose movie and CorsixTH's return to the main menu on losing (`world.lua`). The winning letter on the fax (one of the original's letters, the translation's when it has them, with the salary and the next town; on to the next level or stay) is done (`FaxWindow.WinLetterLines`); losing shows the original's message (`information.level_lost`: why, and the limit passed) over the level end window.
- Playing a campaign through, the original's (12 levels in order) or a CorsixTH one (the fax offering the next level,
  `world.lua`'s winning letter, `fax.lua`), choosing a custom level or campaign in a menu, briefings shown (`app.lua`,
  `Campaigns/`, `Levels/`). Loading a custom level (`.level` + `.map`, with overlays) and reading a campaign file are
  done (`LevelMapLoader.LoadCustom`, `LoadCampaign`, `--map`, `--campaign`). The difficulty and what it changes (`LevelRules.Difficulty`, `--difficulty`)
  are done, the ward's healing too.
- Free build mode (Done in the simulation: `LevelRules.WithFreeBuild`, `free_build_mode` in `config.txt` for `--map`
  levels outside a campaign, `--free-build` on the server; the progress report's free build text and the VIP and
  emergency faxes' free build remarks are shown). Sandbox options
  beyond it are not done.
- Main menu (`resizables/main_menu.lua`): done as a window over the paused first level when the command line names
  nothing to play (`MainMenuWindow`: Campaign, Custom Campaign, Single Scenario with Free Build, Continue Game, Load
  Game, Settings, Exit; `CustomGames`, `levels`/`campaigns` in `config.txt`). Not yet: the map editor, the menu's own
  background, the tutorial and the player name's text box in the new game dialog (`resizables/new_game.lua`).
- AI hospitals (Partial: `computer[n]` opponents take their turns for patients and count them as
  visitors, as CorsixTH's `AIHospital` does; like CorsixTH's, they build nothing and play no part in
  the league, awards or end conditions; `hospitals/ai_hospital.lua`).
- Intro and level-end movies (`movie_player.lua`).

## 8. Saves

**Has:** explicit versioned save format (version 9) of the whole simulation, used for network joins
and state hashing; tests for save/load round trips.

- The file browser's look (`resizables/file_browser.lua`, `file_browsers/`): the save and load windows are text windows (`SaveLoadWindow`: the saves newest first, a new save under a typed name (ASCII letters for now) or the date, overwriting after a question). The quick save, monthly autosaves, `--load`, `--continue` and `autosave_frequency` are done (`SaveGameStore`).
- Loading CorsixTH saves (likely never: CorsixTH persists the Lua heap) and original `SAVE*.SAV`.
- Replays: local games are recorded (initial save plus command log) and can be saved and watched from the menu,
  checked against the recorded state hash; network games too, from the joining client's state and the server's
  commands (`LockstepClient.Recording`), and wound back and forward while watching (`ReplaySession.Seek`, [ and ]).

## 9. Sound and music

**Has:** the sound archive reader (`SoundArchive`, `ThemeHospitalInstall.LoadSounds`: `SOUND/DATA/SOUND-n.DAT` with
the English fallback, names, WAV data, durations, wildcard names; `Src/th_sound.cpp`, `audio.lua`); the music list
and XMI to MIDI conversion (`ThemeHospitalInstall.MusicTracks`, `Xmi.ToMidi`; `audio.lua`'s `Audio:init`,
`Src/xmi2mid.cpp`). The client plays sound effects (`SoundEffects` over MonoGame, `SoundChoice` for wildcards and
the distance falloff of `sound_player::play_at`; `play_sounds` and `sound_volume` in `config.txt`) for the actions it
has: the build tool's door and window clicks, placing and turning objects, hiring and picking up and putting down
staff, buying a parcel, shooting a rat, sending a patient home (positional).

- The animations' own sounds (VFRA's frame sounds with CorsixTH's replacements: machines, doors, objects, getting up,
  vomiting, the grim reaper's swipe and more) play where the frame is drawn (`FrameSounds`, 2026-11-13). Still:
  sound effects the simulation causes (2026-11-14: rooms calling for staff, the epidemic's start and end, cheats, the
  background ispot sounds, the cheer and boo, vaccin.wav and staff's leave sounds play, `HospitalAnnouncements`; the entrance doors and earthquakes, `WorldSounds`): footsteps, patients' coughs and other idle sounds;
  the interface's hover sounds (buttons play selectx.wav and the close box no4.wav, `WindowLayer`, 2026-11-14); adviser voice; the settings window's
  switches for the announcer and its volume (config.txt's keys are read).
- Music playback and the jukebox (`dialogs/jukebox.lua`), MP3 replacement music, volumes (`resizables/sound_setting.lua`).

## 10. Network

**Has:** lockstep server and client over TCP, joining with a save, desync detection by state hash,
in-process host (`--host`) and headless server.

CorsixTH has no network game, so there is nothing to port here; the original's network game is the
model. Missing relative to the plan in `ARCHITECTURE.md`: land auctions, player-owned plots of other
players on screen, a lobby to choose the level in. (Done: waiting for the players in a game started paused (`--speed
pause` with `--host` or on the server), the players' list (`PlayersMessage`) on the status line; the chat, `ChatWindow`, Return in a network game; the content check on join, `ContentFingerprint`; the game speed and pause, set by any
player for everyone on the server's clock, `LockstepServer.SetSpeed`; desync recovery, the server's state sent again
to the client that diverged, `ResyncMessage`.)

## 11. Settings

**Has:** command-line options only (`--level`, `--map`, `--zoom`, `--speed`, `--upscale`, ...).

- Options dialog: resolution, fullscreen, CorsixTH's own translations (the installed original languages can be
  chosen), audio, scroll speed, edge scrolling, zoom, mouse
  capture, adviser on/off, ... (`resizables/options.lua`, `graphics_settings.lua`, `customise.lua`). Of `customise.lua`'s
  switches, removing destroyed rooms, the average contents and the insurance's blue banknote are in the options
  window (`OptionsWindow`).
- The rest of the config file (`config_finder.lua`): announcements, music, movies, `right_mouse_scrolling` (the
  right and middle buttons both drag), `capture_mouse`, the options dialog's game rules, `config.path.txt`; game
  data folder selection (`resizables/folder_settings.lua`, `directory_browser.lua`). `config.txt` with the language,
  window, screen shake, autosave frequency, game folder, player name, sound effects and scrolling
  (`prevent_edge_scrolling`, `scrolling_momentum`, `zoom_speed`, `scroll_speed`, `shift_scroll_speed`) is done
  (`GameConfig`).
- Hotkeys file (`hotkeys.txt`): read, and written with the defaults when missing, for the dialogs (F1–F9, Shift+F3,
  Ctrl+H, Y, C, F11), pause, speed up, quick save/load, the adviser toggle, the speeds, scrolling, zoom, remembered
  views and the room list (`Hotkeys`, `HotkeyInput`), and changed in the hotkey assignment dialog from the options
  (`HotkeyAssignWindow`). Still on fixed keys: Esc and this game's stand-in keys;
  The sound and announcements toggles (Alt+S, Alt+A, also in the menu bar's options) are done.
  The screenshot key (Ctrl+S, `global_screenshot`, into Screenshots beside the settings; the `screenshots` setting is not read) is done.
  Alt+Enter switches fullscreen and keeps it in the settings (`global_fullscreen_toggle`, `UI:fullscreenHotkey`).
  CorsixTH's other actions (music toggle, logs, furnish corridor, edit room,
  sell the picked up item) are not read. The rotate object key (`ingame_rotateobject`, space) turns the object being
  placed, as a right click does; outside placing, space still pauses.
- Update check (`resizables/update.lua`).

## 12. Map editor and tools

**Has:** sprite viewer (`--sprite-viewer`), map overlays for debugging.

- Map editor: its window, the sprite pages and the cursor drawn over the map, and the main menu's button
  (`resizables/map_editor.lua`). The editing itself is done (`MapEditor`): the blank map, painting floor and wall
  sprites with the floor's flags, deleting walls, parcels, the camera and heliport tiles, copying and pasting areas,
  and saving the map as a map file (`MapFile.ToBytes`, `level_map::save` in `th_map.cpp`); not yet the player
  count and objects bigger than a tile making room for each other (`checkObjectSpace`).
- Cheats dialog (`resizables/cheats_dialog.lua`) and the fax code that opens it; the cheats for research,
  showing the infected and machines, and the fax codes themselves (`cheats.lua`). Money, a patient, winning, losing,
  prices, the death count, the reputation, the earthquake, the VIP, the emergency, the epidemic, the epidemics
  toggle, the end of the month and year cheats and the fax code toggles (Roujin's challenge, no rest, queue jump,
  super doctors) are done (`CheatCommand`). The fax's keypad is done (`FaxWindow`, `UIFax:validate`): 24328 opens the
  cheats window with the adviser's line, the four toggle codes switch their cheat with its announcement, and a wrong code
  buzzes; 24328 also puts the cheats menu on the menu bar until the game is left (`app.cheats_menu_unlocked`,
  `UIMenuBar:unlockCheats`). F11 opens the cheats always, where CorsixTH needs its `debug` setting.
- Lua console equivalent / debug console (`resizables/lua_console.lua`), debug patient spawning
  (Partial: debug keys H, Ctrl+1-4).
- AnimView (separate tool in the CorsixTH repo); partly covered by the sprite viewer.

## 13. Rendering

**Has:** map layers, walls with shadows, windows, doors, objects and humanoids with layers and
mirroring, blueprint drawing, bitmap fonts, palette remaps.

- (Done: transparent walls, X held or Shift+X toggled; machines' smoke and their crashed look after an explosion, which
  is all CorsixTH draws for one (Machine:explodeMachine); screen shake during earthquakes. CorsixTH's level_map draws
  the map's blocks unanimated, so there are no animated map elements to port.)
- Video playback (Smacker intro/win/lose movies).
