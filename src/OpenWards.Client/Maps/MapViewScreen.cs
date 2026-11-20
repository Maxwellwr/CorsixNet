using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using OpenWards.Client.Audio;
using OpenWards.Client.Input;
using OpenWards.Client.Movies;
using OpenWards.Client.Rendering;
using OpenWards.Client.Sessions;
using OpenWards.Client.Ui;
using OpenWards.Client.Ui.Windows;
using OpenWards.Content;
using OpenWards.Content.Audio;
using OpenWards.Content.Interface;
using OpenWards.Content.Languages;
using OpenWards.Content.Levels;
using OpenWards.Content.Saves;
using OpenWards.Content.Settings;
using OpenWards.Formats;
using OpenWards.Sim;
using OpenWards.Sim.Calendar;
using OpenWards.Sim.Commands;
using OpenWards.Sim.Definitions;
using OpenWards.Sim.Disasters;
using OpenWards.Sim.Economy;
using OpenWards.Sim.Entities;
using OpenWards.Sim.Maps;
using OpenWards.Sim.Objects;
using OpenWards.Sim.People;
using OpenWards.Sim.Rooms;
using OpenWards.Sim.Staff;

namespace OpenWards.Client.Maps;

/// <summary>A level to show: an original LEVELS file or a map file elsewhere on disk.</summary>
internal sealed record LevelSource(string Name, Func<LoadedLevelMap> Load);

/// <summary>
/// Shows the map of a game session with a movable, zoomable camera.
/// </summary>
/// <remarks>
/// Keys: arrows/WASD, screen edges or right/middle-drag pan, wheel or =/− zoom, 0 original size, Alt+digit remember
/// the view and Ctrl+digit go back to it, Home reset camera,
/// F3 list of levels to pick from (local games), O cycle overlay, U cycle sprite upscaling, Esc close the front window or open the menu (save, load, options, quit).
/// Clicking a member of staff, a patient, a room or a desk opens their window; C faxes, Y hire staff, F4 town map.
/// F1 bank manager, F2 bank statement, Shift+F3 staff management, F5 casebook, F7 progress report, F11 cheats, F6 research, F8 graphs, F9 policy, F10 machines (windows, see <see cref="WindowLayer"/>).
/// Simulation test controls: H spawn a patient at the cursor, G spawn ten in the hospital,
/// left click select a patient or send the selected one walking, R toggle random wandering,
/// Space or P pause, 1 to 5 game speed, Z held speeds up (local games), Ctrl+1 to Ctrl+4 hire staff. B builds a GP's office: blueprint, door, windows, then furniture
/// (right click turns an object, P picks one up, Enter confirms, Esc steps back). Alt+Shift+S saves a local game to
/// the quick save and Alt+Shift+L loads it; the game also saves itself each month. In a network game, commands go
/// through the server.
/// </remarks>
internal sealed class MapViewScreen : IScreen, IWindowHost
{
    /// <summary>The most interface ticks of scrolling (18 ms each) one frame catches up on.</summary>
    private const int MaxScrollTicksPerFrame = 10;

    /// <summary>UIPlaceStaff takes the tile under a point this many screen pixels below the cursor.</summary>
    private const int StaffCursorDrop = 14;

    /// <summary>A member of staff who cannot be put down where they are shown is grey (the ghost palette).</summary>
    private static readonly Color StaffBlockedTint = new Color(130, 130, 130);

    /// <summary>How far an earthquake at full strength moves the view, in map pixels (CorsixTH's <c>shake_screen_max_movement</c>).</summary>
    private const float ShakeMaxMovement = 50f;

    private static readonly Color Background = Color.Black;
    private static readonly Color HoverTint = new Color(255, 255, 255) * 0.35f;
    private static readonly byte[] NoLayers = [];
    private static readonly Color PickUpTint = new(150, 200, 255);
    private static readonly Color CellOutlineColor = new(232, 0, 0);

    /// <summary>The surgeon screen with two green outfits (2772), one green and one white (2776), or two white (2774).</summary>
    private const int SurgeonScreenGreens = 2772;
    private const int SurgeonScreenBoth = 2776;
    private const int SurgeonScreenWhites = 2774;

    /// <summary>The gear icon over a machine waiting for its repair (machine.lua, the only icon for machinery).</summary>
    private const int RepairGearIcon = 4564;

    /// <summary>The X-ray viewer's layers while lit: layer 11 shows its second variant (<c>setLayer(11, 2)</c>).</summary>
    private static readonly byte[] XRayViewerLit = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2];

    /// <summary>Colour of the status bar and the frame around the map while building, the blue of the blueprints.</summary>
    private static readonly Color BuildModeColor = new(0, 48, 144);

    /// <summary>Background of the date and speed box.</summary>
    private static readonly Color DatePanelColor = new(40, 40, 40);
    private static readonly Color MoneyPanelColor = new(0, 0, 0, 200);

    private static readonly Color RoomListHover = new(60, 90, 160, 220);

    private static readonly Color RoomListHoverBlocked = new(90, 60, 60, 220);

    private static readonly Color RoomListBlocked = new(150, 150, 150);

    private static readonly Color LevelListCurrent = new(255, 220, 120);

    /// <summary>Width of the frame drawn around the map while building.</summary>
    private const int BuildFrameWidth = 3;

    /// <summary>How long a notice such as UiText.Get("openwards.build_tool.not_enough_money", "not enough money") stays in the status line.</summary>
    private const int NoticeMilliseconds = 4000;

    /// <summary>The emergency helicopter (CorsixTH's objects/helicopter.lua).</summary>
    private const int HelicopterAnimation = 2456;

    /// <summary>Room door animations from CorsixTH's <c>objects/door.lua</c>.</summary>
    private const int RoomDoorNorth = 104;

    /// <summary>The rat hole in a north wall (objects/rathole.lua).</summary>
    private const int RatholeAnimation = 1904;

    /// <summary>The rat's animations (entities/rat.lua).</summary>
    private const int RatWalkNorth = 1908;
    private const int RatWalkEast = 1912;
    private const int RatWalkSouth = 1916;
    private const int RatWalkWest = 1920;
    private const int RatEnterNorthHole = 1924;
    private const int RatEnterWestHole = 1926;
    private const int RatLeaveHole = 1928;
    private const int RoomDoorWest = 106;

    /// <summary>Swing doors standing: the left one in a north (1996) or west (1998) wall, the right one in a north wall (2006).</summary>
    private const int SwingDoorLeftNorth = 1996;
    private const int SwingDoorLeftWest = 1998;
    private const int SwingDoorRightNorth = 2006;

    private readonly GraphicsDevice _device;
    private IReadOnlyList<LevelSource> _levels;
    private MainMenuWindow? _mainMenu;
    private readonly SpriteAtlas _blocks;
    private readonly BlueprintDrawing _blueprints;
    private readonly MapRenderer _renderer;
    private readonly TextRenderer _text;
    private UnicodeFonts _unicodeFonts;
    private readonly Dictionary<string, Task<IReadOnlyList<(string Name, string Path)>>> _fontChoices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Texture2D _diamond;
    private readonly Texture2D _cellOutline;
    private readonly Texture2D _pixel;
    private readonly Camera _camera = new();
    private readonly AnimationRenderer _animations;
    private readonly HumanoidDrawing _humanoids;
    private readonly EntranceDoorDrawing _doors;
    private readonly Dictionary<TilePosition, List<Entity>> _entitiesByTile = [];

    /// <summary>Entities drawn with their tile's north wall (CorsixTH's early list), by tile.</summary>
    private readonly Dictionary<TilePosition, List<Entity>> _earlyByTile = [];

    /// <summary>The tiles of placed objects of the early list, whose users are drawn early too.</summary>
    private readonly HashSet<TilePosition> _earlyObjectTiles = [];

    /// <summary>Objects whose second part (CorsixTH's slave object) is drawn on the tile.</summary>
    private readonly Dictionary<TilePosition, List<RoomObject>> _slavePartsByTile = [];

    /// <summary>Parts of objects drawn split over several tiles, and of their users' animations, by the tile drawing them.</summary>
    private readonly Dictionary<TilePosition, List<(Entity Entity, int Left, int Right)>> _splitPartsByTile = [];
    private readonly BuildRoomTool _buildTool = new();
    private readonly FurnishCorridorTool _furnishTool = new();

    /// <summary>The map editor's cursor while the editor is open (<c>UIMapEditor</c>), or null.</summary>
    private MapEditorTool? _editorTool;

    /// <summary>The map file the editor last loaded or saved, or null for a new map.</summary>
    private string? _editorPath;
    private readonly HashSet<int> _hiddenObjects = [];
    private readonly ObjectDrawing _objects;
    private readonly FloatingDollarDrawing _floatingDollars;

    /// <summary>The original's cursors (DATA/MPointer), or null if the sheet could not be loaded.</summary>
    private readonly CursorDrawing? _cursors;

    /// <summary>Whether the mouse was over a window at the last update.</summary>
    private bool _cursorOverWindow;

    /// <summary>Objects the demo still has to place in its GP's office, one at a time.</summary>
    private readonly Queue<string> _demoFurniture = new();
    private int _demoFurnitureExpected;
    private readonly List<TilePosition> _hospitalTiles = [];

    // Test controls choose targets at random; the choices reach the simulation as commands.
    private readonly Random _random = new();

    private IGameSession? _session;

    /// <summary>The saves folder: the quick save and the monthly autosaves.</summary>
    /// <summary>Where games are saved: the <c>savegames</c> setting, else the default Saves folder.</summary>
    private SaveGameStore _saves = new(SaveGameStore.DefaultDirectory());

    /// <summary><c>App:initSavegameDir</c>: the configured folder, else the default.</summary>
    internal static string SavesDirectory(GameConfig config) => config.Savegames is { Length: > 0 } folder ? folder : SaveGameStore.DefaultDirectory();
    private int _levelIndex;

    private string? _error;

    /// <summary>--debug-status: show the status bar with the debug lines above the map.</summary>
    private readonly bool _debugStatus;
    private MapOverlay _overlay;
    private RenderTarget2D? _target;
    private KeyboardState _previousKeys;
    private MouseState _previousMouse;
    private Point? _hoveredTile;

    /// <summary>The tile a member of staff being hired or carried goes on: 14 screen pixels below the cursor's, as
    /// <c>UIPlaceStaff:onCursorWorldPositionChange</c> takes it.</summary>
    private Point? _staffTile;
    private (int X, int Y) _hoveredFraction;
    private Point _rightPressedAt;
    private bool _buildBack;

    /// <summary>The windows open over the map (bank, casebook, research, policy, ...).</summary>
    private readonly WindowLayer _windows;

    /// <summary>A button of the room edit panel pressed, for the build tool's next step.</summary>
    private BuildButton? _buildButton;

    /// <summary>The candidate for hire on the cursor, to be placed with a click.</summary>
    private (StaffClass Class, StaffProfile Profile)? _hiring;

    /// <summary>The year when last looked, so that a new one opens the annual report.</summary>
    private int? _lastYearSeen;

    /// <summary>The level's outcome when last looked, so that winning or losing opens the level's end.</summary>
    private EndOutcome _lastOutcome;

    /// <summary>Whether a countdown ran when last looked, so that a new one opens the watch.</summary>
    private bool _watchRan;

    /// <summary>The faxes waiting when last looked, so that a new one opens the faxes window.</summary>
    private HashSet<string> _seenFaxes = new(StringComparer.Ordinal);

    /// <summary>The staff whose raise requests had an icon when last looked, so that a new one plays NewFax.wav.</summary>
    private HashSet<int> _seenRaises = [];

    /// <summary>Staff whose raise request has been answered, until the game has taken the answer.</summary>
    private readonly HashSet<int> _raisesAnswered = [];

    /// <summary>Whether a window that must pause the game (<see cref="UiWindow.MustPause"/>) paused it.</summary>
    private bool _pausedForWindow;

    /// <summary>Whether this player holds the speed-up key, so that letting go of it ends the speed-up.</summary>
    private bool _speedUpHeld;

    /// <summary>Announces machines that start needing a repair urgently (CorsixTH's <c>announceRepair</c>).</summary>
    private readonly RepairAnnouncer _repairs = new();

    private readonly HospitalAnnouncements _announcements = new(new Random());

    private readonly WorldSounds _worldSounds = new(new Random());

    private readonly Announcer _announcer = new(new Random());
    private readonly Subtitles _subtitles = new();

    private readonly GatesToHellSounds _hellSounds = new();

    private readonly FrameSounds _frameSounds = new();

    private object? _repairSession;

    /// <summary>Whether the list of levels is open (F3), a stand-in for CorsixTH's new game and custom level windows.</summary>
    private bool _levelList;

    /// <summary>Which campaign's board movies the levels played have (the original's, a custom campaign's <c>movie</c>, or none).</summary>
    private CampaignMovies _campaignMovies;

    /// <summary>The level list's rows on screen, from the last time it was drawn, for clicks.</summary>
    private readonly List<(Rectangle Row, int Index)> _levelListRows = [];

    /// <summary>Whether the list of rooms to build is open (B), a stand-in for CorsixTH's build room window.</summary>
    private bool _roomList;

    /// <summary>The list's room rows on screen, from the last time it was drawn, for clicks.</summary>
    private readonly List<(Rectangle Row, RoomDefinition Room)> _roomListRows = [];
    private string? _notice;
    private long _noticeUntil;

    /// <summary>Until when the status line reminds that other keys wait until building is over.</summary>
    private long _buildReminderUntil;
    private bool _wander;
    private bool _demoPending;

    /// <summary>The speed a local game starts at (<c>--speed</c>).</summary>
    private readonly GameSpeed _initialSpeed;
    private bool _demoDeskPending;
    private bool _demoDiagnosisPending;
    private bool _demoMachinePending;
    private bool _demoToiletsPending;
    private int? _selected;

    /// <summary>The member of staff picked up with M, to be put down with M again.</summary>
    private int? _pickedUpStaff;
    private LevelMap? _preparedMap;
    private string? _capturePath;

    /// <summary>The player's settings.</summary>
    private GameConfig _config;

    /// <summary>Where the settings are saved, or null to leave the file alone.</summary>
    private readonly string? _configPath;

    /// <summary>The keys for the dialogs and other actions, from <c>hotkeys.txt</c> next to the settings.</summary>
    private Hotkeys _hotkeys;

    /// <summary>The game install, for reloading the sounds when the options change.</summary>
    private readonly ThemeHospitalInstall _install;

    /// <summary>The game's texts in the installed language, or null if no string file loads.</summary>
    private OriginalTexts? _texts;
    private readonly Adviser _adviser = new();
    private readonly AdviserFigure _adviserFigure = new();

    /// <summary>The original's bottom panel, or null where its graphics could not be read (the text panel then).</summary>
    private readonly OriginalBottomPanel? _bottomPanel;

    /// <summary>The fullscreen dialogs' graphics, loaded as they are first shown.</summary>
    private readonly OriginalGraphics _graphics;

    /// <summary>The bottom panel's sprites (<c>Data/Panel02V</c>), for the adviser's balloon.</summary>
    private readonly SpriteAtlas? _panelSprites;

    /// <summary>The black font of the adviser's balloon (<c>QData/Font50V</c>).</summary>
    private readonly TextRenderer? _blackText;

    /// <summary>The tutorial of the first level, when the new game dialog asked for it.</summary>
    private Tutorial? _tutorial;

    /// <summary>The game the tutorial was started with.</summary>
    private IGameSession? _tutorialSession;
    private readonly Random _adviserRandom = new();
    private AdviserChecks _adviserChecks;
    private object? _adviserSession;
    private readonly HashSet<string> _roomsDescribed = new(StringComparer.Ordinal);
    /// <summary>The tip of the day shown with the main menu, which goes with it (CorsixTH app.lua, <c>loadMainMenu</c>).</summary>
    private TipOfTheDayWindow? _tipOfTheDay;
    private bool _pickingRoomToEdit;

    /// <summary>The original's sound effects, silent when they are turned off or cannot be played.</summary>
    private SoundEffects _sounds;

    /// <summary>The background music (<c>Audio</c>'s playlist), started with the game (<c>App:init</c>'s playRandomBackgroundTrack).</summary>
    private BackgroundMusic _music;

    /// <summary>The movie player (<c>App.moviePlayer</c>) and the game's movies.</summary>
    private readonly MoviePlayer _movie;

    private readonly MovieFiles _movieFiles;

    /// <summary>Whether a movie with sound paused the music, to go on with it afterwards (<c>holding_bg_music</c>).</summary>
    private bool _holdingMusic;

    /// <summary>CorsixTH's picture behind the main menu, loaded while the menu shows.</summary>
    private MenuBackground? _menuBackground;

    /// <summary>
    /// The warmth colours of the town map and the O overlay (<c>Map:setTemperatureDisplayMethod</c>): 1 red, 2 blue
    /// green red, 3 yellow orange red; <c>warmth_colors_display_default</c> to begin with.
    /// </summary>
    private int _temperatureTheme = 1;

    /// <summary>The last answer to the player's poaching already told, and the staff employed elsewhere at the last look.</summary>
    private PoachAnswer? _lastPoachSeen;

    private HashSet<int>? _staffElsewhere;

    /// <summary>The last auction's result already told, and whether the auctions have been watched since the game began.</summary>
    private AuctionResult? _lastAuctionSeen;

    private bool _auctionWatched;

    /// <summary>How long the main menu has been left alone, for the demo movie (<c>App.idle_tick</c>), in milliseconds.</summary>
    private double _idleMilliseconds;

    /// <summary>Scrolling and zooming the view as CorsixTH does, with the player's speeds.</summary>
    private MapScrolling _scrolling;

    /// <summary>Views remembered with Alt+digit, by digit (CorsixTH's <c>recallpositions</c>).</summary>
    private readonly (Vector2 Center, float Zoom)?[] _recallPositions = new (Vector2 Center, float Zoom)?[10];

    /// <summary>Time not yet scrolled for, less than an interface tick.</summary>
    private double _scrollMilliseconds;

    /// <summary>Creates the view for a local game on <paramref name="levels"/>, or for <paramref name="networkSession"/> if given.</summary>
    public MapViewScreen(
        ThemeHospitalInstall install,
        GraphicsDevice device,
        IReadOnlyList<LevelSource> levels,
        float zoom = 1f,
        MapOverlay overlay = MapOverlay.None,
        bool demo = false,
        IGameSession? networkSession = null,
        int upscale = 1,
        GameSpeed speed = GameSpeed.Normal,
        string? loadPath = null,
        GameConfig? config = null,
        string? configPath = null,
        bool mainMenu = false,
        bool mapEditor = false,
        bool debugStatus = false)
    {
        _debugStatus = debugStatus;
        _config = config ?? new GameConfig();
        _saves = new SaveGameStore(SavesDirectory(_config));
        _adviser.IsEnabled = !_config.AdviserDisabled;
        _adviserChecks = new AdviserChecks(_adviser, _adviserRandom);
        _configPath = configPath;
        _hotkeys = LoadHotkeys(configPath);
        _install = install;
        _device = device;
        _initialSpeed = speed;
        _levels = networkSession is null ? levels : [];

        var palette = install.LoadPalette("DATA", "MPALETTE.DAT");
        // Floor tiles are the 64x32 diamonds of the block sheet.
        _blocks = new SpriteAtlas(device, install.LoadSpriteSheet("DATA", "VBLK-0", complex: false), palette, upscale, sprite => sprite.Width == 64 && sprite.Height == 32);
        _renderer = new MapRenderer(_blocks);
        _text = new TextRenderer(device, new BitmapFont(install.LoadSpriteSheet("QDATA", "FONT01V", complex: false)), palette);
        _windows = new WindowLayer(text => _text.Measure(text).Width, () => _text.LineHeight) { Origin = new Point(8, StatusHeight + 8) };
        try
        {
            _bottomPanel = new OriginalBottomPanel(device, install, palette);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            // Without the panel's graphics, the text panel stands in.
            _bottomPanel = null;
            _windows.Open(new BottomPanel(BottomPanelAction));
        }

        InputScript.ButtonFinder = (text, nth) => (nth <= 1 && _bottomPanel is not null ? OriginalBottomPanel.FindButton(_device.Viewport, text) : null) ?? _windows.FindButton(text, nth);
        var sprites = install.LoadSpriteSheet("DATA", "VSPR-0", complex: false);
        _animations = new AnimationRenderer(device, AnimationSet.Load(install), sprites, palette, upscale);
        _blueprints = new BlueprintDrawing(device, install, _blocks, _animations, sprites, palette);
        _humanoids = new HumanoidDrawing(_animations) { FrameDrawn = (key, frame, x, y) => _frameSounds.Drawn(_animations.Animations, key, frame, x, y) };
        _doors = new EntranceDoorDrawing(_animations);
        _objects = new ObjectDrawing(_animations) { FrameDrawn = (key, frame, x, y) => _frameSounds.Drawn(_animations.Animations, key, frame, x, y) };
        _floatingDollars = new FloatingDollarDrawing(device, install, palette, upscale);
        try
        {
            _cursors = new CursorDrawing(device, install, palette);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            _cursors = null;
        }
        try
        {
            _panelSprites = new SpriteAtlas(device, install.LoadSpriteSheet("DATA", "PANEL02V", complex: true), palette);
            _blackText = new TextRenderer(device, new BitmapFont(install.LoadSpriteSheet("QDATA", "FONT50V", complex: false)), palette);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            _panelSprites = null;
            _blackText = null;
        }

        _graphics = new OriginalGraphics(device, install);
        Ui.StaffFaces.Current = Ui.StaffFaces.Load(device, install, palette);
        _unicodeFonts = new UnicodeFonts(_config.UnicodeFont, AppContext.BaseDirectory);
        _texts = LoadTexts(install, _config);
        UiText.Current = _texts;
        ApplyLanguageFont();
        _sounds = SoundEffects.Load(install, _config, VoiceOf(_config));
        _music = CreateMusic(_config);

        _movie = new MoviePlayer(device);
        _movieFiles = install.Movies();
        _scrolling = new MapScrolling(_config.ScrollSettings());
        _buildTool.PlaySound = name => _sounds.Play(name);
        _buildTool.Advise = line => _adviser.Say(line, Environment.TickCount64);
        _buildTool.AverageContents = _config.EnableAverageContents;
        OriginalBottomPanel.MachineMenuButton = _config.MachineMenuButton;
        _temperatureTheme = _config.WarmthColorsDisplayDefault;
        _buildTool.AbandonRequested = () => _windows.Open(new ConfirmWindow(Confirmation(5, "Are you sure you want to delete this room?"), () =>
        {
            _session?.Submit(new AbandonRebuildCommand());
            _buildTool.Stop();
        }));
        _furnishTool.PlaySound = name => _sounds.Play(name);
        _diamond = CreateDiamond(device);
        _cellOutline = CreateCellOutline(device);
        _pixel = new Texture2D(device, 1, 1);
        _pixel.SetData([Color.White]);

        _overlay = overlay;
        _demoPending = demo;
        _demoDeskPending = demo;
        _demoDiagnosisPending = demo;
        _demoMachinePending = demo;
        _demoToiletsPending = demo;
        if (demo)
        {
            foreach (var item in (string[])["desk", "cabinet", "chair", .. DemoRadiators])
            {
                _demoFurniture.Enqueue(item);
            }
        }
        if (networkSession is not null)
        {
            _session = networkSession;
        }
        else
        {
            SelectLevel(0);
            if (loadPath is not null)
            {
                LoadGame(loadPath);
            }
        }

        PrepareMapIfReady();
        _camera.ZoomAt(zoom, new Point(ViewSize.X / 2, ViewSize.Y / 2), ViewSize);
        if (mapEditor)
        {
            StartMapEditor(MapEditor.Blank(), null);
        }
        else if (mainMenu && _session is LocalSession)
        {
            OpenMainMenu();
        }

        // App:init: the intro (play_intro), then the music (callback_after_movie's playRandomBackgroundTrack).
        if (mainMenu && !mapEditor && _config.PlayIntro)
        {
            PlayMovie(_movieFiles.Intro, waitForStop: false, () => _music.PlayRandom());
        }
        else
        {
            _music.PlayRandom();
        }
    }

    public bool ExitRequested { get; private set; }

    /// <summary>
    /// Saves the next rendered map view (without the status bar) to a PNG file. The off-screen
    /// buffer is used, so this works even when the window cannot be seen, e.g. on a locked screen.
    /// </summary>
    public void RequestCapture(string path) => _capturePath = path;

    private Simulation? Simulation => _session?.Simulation;

    private LevelMap? Map => _session?.Simulation?.Map;

    /// <summary>Lists the original levels in the game's LEVELS folder, in campaign order, at a difficulty.</summary>
    public static IReadOnlyList<LevelSource> OriginalLevels(ThemeHospitalInstall install, Difficulty difficulty = Difficulty.Medium)
    {
        var folder = install.FindDirectory("LEVELS");
        if (folder is null)
        {
            return [];
        }

        return Directory.EnumerateFiles(folder, "LEVEL.L*")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Select(name => (Name: name.ToUpperInvariant(), Number: int.TryParse(name.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue))
            .OrderBy(level => level.Number)
            .ThenBy(level => level.Name, StringComparer.Ordinal)
            .Select(level => new LevelSource(level.Name, () => LevelMapLoader.LoadOriginal(install, level.Name, difficulty)))
            .ToList();
    }

    /// <summary>
    /// The difficulties whose level files are installed (<c>UINewGame</c>: medium always, easy and hard when their
    /// first level's settings file is there).
    /// </summary>
    public static IReadOnlyList<Difficulty> AvailableDifficulties(ThemeHospitalInstall install)
    {
        var folder = install.FindDirectory("LEVELS");
        var files = folder is null
            ? []
            : Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. new[] { Difficulty.Easy, Difficulty.Medium, Difficulty.Hard }
            .Where(difficulty => difficulty == Difficulty.Medium || files.Contains($"{LevelRulesLoader.FilePrefix(difficulty)}01.SAM"))];
    }

    public void Update(GameTime gameTime, bool hasFocus)
    {
        if (_mouseReleased && hasFocus && InputScript.Mouse() is var down
            && (down.LeftButton == ButtonState.Pressed || down.RightButton == ButtonState.Pressed || down.MiddleButton == ButtonState.Pressed))
        {
            // UI:onMouseDown: a click takes the cursor back.
            _mouseReleased = false;
        }

        _music.Update();
        ShowUpdateFound();
        if (_movie.IsPlaying)
        {
            UpdateMovie(hasFocus);
            return;
        }

        UpdateDemoIdle(gameTime);
        if (!hasFocus)
        {
            // No input, but the game goes on.
            AdvanceSession(gameTime);
            return;
        }

        var keys = InputScript.Keyboard();
        var mouse = InputScript.Mouse();
        var viewSize = ViewSize;
        bool Pressed(Keys key) => keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);

        // A window waiting for a key (the hotkey assignment dialog) takes the keyboard: the game sees no keys then.
        var realKeys = keys;
        if (_windows.Windows.OfType<IKeyCaptureWindow>().FirstOrDefault(window => window.IsCapturing) is { } capture)
        {
            if (HotkeyInput.Captured(keys, _previousKeys) is { } captured)
            {
                capture.Capture(captured);
            }

            keys = default;
        }

        // UI:fullscreenHotkey is global: it works in the main menu and over any window, not only on the map.
        if (HotkeyInput.Pressed(_hotkeys["global_fullscreen_toggle"], keys, Pressed))
        {
            ToggleFullscreen();
        }

        if (Pressed(Keys.Escape))
        {
            if (_hiring is not null)
            {
                _hiring = null;
            }
            else if (_pickingRoomToEdit)
            {
                _pickingRoomToEdit = false;
            }
            else if (_buildTool.IsFurnishing && _windows.Windows.LastOrDefault(window => !window.IsPinned) is RoomEditWindow)
            {
                // The edit panel is the furnishing itself (CorsixTH's UIEditRoom): closing it would only reopen it, so Esc
                // goes to the build tool, which drops an extra object from the cursor or takes the room down.
                _buildBack = true;
            }
            else if (_windows.Any)
            {
                _windows.CloseTop();
            }
            else if (_mainMenu is not null)
            {
                // esc_closes = false.
            }
            else if (_levelList)
            {
                _levelList = false;
            }
            else if (_roomList)
            {
                _roomList = false;
            }
            else if (_buildTool.IsActive || _furnishTool.IsActive)
            {
                _buildBack = true;
            }
            else if (_editorTool is not null)
            {
                OpenMapEditorWindow();
            }
            else if (IsWindowHostReady)
            {
                // CorsixTH's Esc shows the menu bar; quitting is on it.
                _windows.Open(new MenuWindow(MenuAction));
            }
            else
            {
                ExitRequested = true;
            }
        }

        // A window on top in its original look takes the keys it handles (CorsixTH's key handlers on the window): the map
        // sees none of them while it is open.
        if (IsWindowHostReady && _windows.Windows.LastOrDefault(window => !window.IsPinned) is { } top && top.IsFullscreen(this))
        {
            var taken = new List<Keys>();
            foreach (var (action, key) in WindowKeys())
            {
                if (!top.TakesKey(action))
                {
                    continue;
                }

                taken.Add(key);
                if (Pressed(key))
                {
                    top.FullscreenKey(this, action);
                }
            }

            if (taken.Count > 0)
            {
                keys = new KeyboardState(keys.GetPressedKeys().Where(key => !taken.Contains(key)).ToArray());
            }
        }

        UpdateChat(keys.IsKeyDown(Keys.Enter) && !_previousKeys.IsKeyDown(Keys.Enter));

        // In a network game F3 shows the server's levels, which the host chooses from, instead of the local list.
        if (_session is NetworkSession lobby && Pressed(Keys.F3) && !keys.IsKeyDown(Keys.LeftShift) && !keys.IsKeyDown(Keys.RightShift))
        {
            ToggleLobby(lobby);
        }
        else if (_levels.Count > 0 && Pressed(Keys.F3) && !keys.IsKeyDown(Keys.LeftShift) && !keys.IsKeyDown(Keys.RightShift))
        {
            _levelList = !_levelList;
            _roomList = false;
        }

        if (_levelList)
        {
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
            {
                var chosen = _levelListRows.FirstOrDefault(row => row.Row.Contains(mouse.X, mouse.Y));
                _levelList = false;
                if (chosen.Row != Rectangle.Empty)
                {
                    SelectLevel(chosen.Index);
                }
            }

            _previousKeys = realKeys;
            _previousMouse = mouse;
            return;
        }

        UpdateAdviser();
        if (_tutorial is { IsActive: true } tutorial && TutorialView() is { } tutorialView)
        {
            tutorial.Update(tutorialView);
        }

        // Windows over the map take the mouse while it is over them: the map sees no clicks and no wheel then.
        var realMouse = mouse;
        if (IsWindowHostReady && OriginalMenuBar.IsAvailable(this)
            && _menuBar.Update(this, MenuBarMenus(), MenuBarMeasure, mouse, _previousMouse, Environment.TickCount64))
        {
            // UIMenuBar takes the mouse while it is over the bar or an open menu.
            mouse = new MouseState(mouse.X, mouse.Y, _previousMouse.ScrollWheelValue, _previousMouse.LeftButton, mouse.MiddleButton, _previousMouse.RightButton, mouse.XButton1, mouse.XButton2);
        }

        if (ClickAdviser(mouse))
        {
            mouse = new MouseState(mouse.X, mouse.Y, mouse.ScrollWheelValue, _previousMouse.LeftButton, mouse.MiddleButton, _previousMouse.RightButton, mouse.XButton1, mouse.XButton2);
        }

        var overPanel = IsWindowHostReady && _bottomPanel is not null
            && _bottomPanel.Update(_device.Viewport, mouse, _previousMouse, PanelFaxes().Count, BottomPanelButton);
        var panelHeight = _bottomPanel is null ? 0 : OriginalBottomPanel.Height;
        var overWindow = IsWindowHostReady
            && (_windows.Update(this, mouse, _previousMouse, new Rectangle(0, StatusHeight, _device.Viewport.Width, Math.Max(1, _device.Viewport.Height - StatusHeight - panelHeight))) | overPanel);
        _cursorOverWindow = overWindow;

        // UIResearch:_playBgSound: Research.wav loops while the research screen is open in its original look.
        _sounds.SetLoop(_windows.Windows.Any(window => window is ResearchWindow && window.IsFullscreen(this)) ? "Research.wav" : null);
        if (overWindow)
        {
            mouse = new MouseState(mouse.X, mouse.Y, _previousMouse.ScrollWheelValue, _previousMouse.LeftButton, _previousMouse.MiddleButton, _previousMouse.RightButton, _previousMouse.XButton1, _previousMouse.XButton2);
        }

        if (Pressed(Keys.Home))
        {
            ResetCamera();
        }

        UpdateCameraKeys(keys, mouse, viewSize, Pressed);

        // Shift+O opens the hospital early instead.
        if (Pressed(Keys.O) && !keys.IsKeyDown(Keys.LeftShift) && !keys.IsKeyDown(Keys.RightShift))
        {
            _overlay = (MapOverlay)(((int)_overlay + 1) % Enum.GetValues<MapOverlay>().Length);
        }

        if (Pressed(Keys.U))
        {
            // Original pixels, then MMPX-smoothed sprites at 2x and 4x.
            var scale = _blocks.Scale switch { 1 => 2, 2 => 4, _ => 1 };
            _blocks.SetScale(scale);
            _animations.SetScale(scale);
            _blueprints.SetScale(scale);
            _floatingDollars.SetScale(scale);
        }

        // CorsixTH's GameUI scrolling: the arrow keys (and WASD) and the screen's edges scroll, faster with Shift and the
        // longer they do; the right or middle button drags; the wheel zooms with momentum, towards the cursor.
        // WASD only without Alt or Ctrl, which make them other keys (Alt+A, Alt+S: the announcements and sounds).
        var wasd = !keys.IsKeyDown(Keys.LeftAlt) && !keys.IsKeyDown(Keys.RightAlt) && !keys.IsKeyDown(Keys.LeftControl) && !keys.IsKeyDown(Keys.RightControl);
        var keyX = (ScrollKeyHeld("ingame_scroll_right", keys) || (wasd && keys.IsKeyDown(Keys.D)) ? 1 : 0) - (ScrollKeyHeld("ingame_scroll_left", keys) || (wasd && keys.IsKeyDown(Keys.A)) ? 1 : 0);
        var keyY = (ScrollKeyHeld("ingame_scroll_down", keys) || (wasd && keys.IsKeyDown(Keys.S)) ? 1 : 0) - (ScrollKeyHeld("ingame_scroll_up", keys) || (wasd && keys.IsKeyDown(Keys.W)) ? 1 : 0);
        var shiftHeld = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
        // In the map editor the right button copies an area (UIMapEditor); the middle one still drags the view.
        var rightPans = _editorTool is null && _config.RightMouseScrolling;
        var dragging = (rightPans && mouse.RightButton == ButtonState.Pressed) || mouse.MiddleButton == ButtonState.Pressed;
        var wasDragging = (rightPans && _previousMouse.RightButton == ButtonState.Pressed) || _previousMouse.MiddleButton == ButtonState.Pressed;
        if (dragging && wasDragging)
        {
            var (dragX, dragY) = _scrolling.Drag(mouse.X - _previousMouse.X, mouse.Y - _previousMouse.Y, _camera.Zoom);
            _camera.Center += new Vector2(dragX, dragY);
        }

        var wheel = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        if (wheel != 0)
        {
            _scrolling.Wheel(wheel / 120.0);
        }

        var (edgeX, edgeY) = _scrolling.EdgePull(mouse.X, mouse.Y, _device.Viewport.Width, _device.Viewport.Height, _config.Fullscreen);
        var zoomAnchor = new Point(mouse.X, mouse.Y - StatusHeight);
        _scrollMilliseconds += gameTime.ElapsedGameTime.TotalMilliseconds;
        for (var tick = 0; _scrollMilliseconds >= MapScrolling.TickMilliseconds; tick++)
        {
            if (tick == MaxScrollTicksPerFrame)
            {
                // After a long pause (a level loading, the window dragged), don't scroll for all of it at once.
                _scrollMilliseconds = 0;
                break;
            }

            _scrollMilliseconds -= MapScrolling.TickMilliseconds;
            var step = _scrolling.Tick(new ScrollInput(keyX, keyY, shiftHeld, edgeX, edgeY, dragging));
            _camera.Center += new Vector2((float)step.X, (float)step.Y);
            if (step.Zoom != 0 && _scrolling.Zoomed(_camera.Zoom, step.Zoom, _device.Viewport.Width) is { } zoomed)
            {
                _camera.ZoomAt((float)zoomed, zoomAnchor, viewSize);
            }
        }

        ClampCamera();

        var inView = !overWindow && mouse.Y >= StatusHeight && mouse.X >= 0 && mouse.X < viewSize.X && mouse.Y < StatusHeight + viewSize.Y;
        var world = _camera.ScreenToWorld(new Point(mouse.X, mouse.Y - StatusHeight), viewSize);
        var tile = Camera.WorldToTile(world);
        _hoveredTile = inView && Map is { } map && map.Contains(tile.X, tile.Y) ? tile : null;
        _hoveredFraction = Camera.WorldToTileFraction(world);
        var staffTile = Camera.WorldToTile(_camera.ScreenToWorld(new Point(mouse.X, mouse.Y - StatusHeight + StaffCursorDrop), viewSize));
        _staffTile = inView && Map is { } staffMap && staffMap.Contains(staffTile.X, staffTile.Y) ? staffTile : null;
        _hoveredWorld = inView ? new Point((int)Math.Floor(world.X), (int)Math.Floor(world.Y)) : null;
        UpdateHover();

        UpdateSimulation(gameTime, mouse, keys, Pressed);

        _previousKeys = realKeys;
        _previousMouse = realMouse;
    }

    /// <summary>
    /// Where an earthquake moves the view this frame (CorsixTH's <c>GameUI:draw</c> with <c>shake_screen_intensity</c>);
    /// the view keeps still while the game is paused, as CorsixTH's "pause" stage stops the shaking.
    /// </summary>
    private Point ShakeOffset()
    {
        var intensity = _config.EnableScreenShake && _session is { IsPaused: false, Simulation: { } simulation } ? simulation.Earthquakes.Shake switch
        {
            EarthquakeShake.Warning => 0.2f,
            EarthquakeShake.Small => 0.5f,
            EarthquakeShake.Large => 1f,
            _ => 0f,
        } : 0f;
        if (intensity == 0f)
        {
            return Point.Zero;
        }

        int Offset() => (int)MathF.Floor((0.5f - _random.NextSingle()) * intensity * ShakeMaxMovement * 2);
        return new Point(Offset(), Offset());
    }

    public void Draw(SpriteBatch batch)
    {
        var viewSize = ViewSize;
        var worldSize = _camera.WorldSize(viewSize);
        var topLeft = _camera.TopLeft(viewSize) + ShakeOffset();
        var scale = _blocks.Scale;
        EnsureTarget(new Point(worldSize.X * scale, worldSize.Y * scale));

        // Draw the map into an off-screen target at the sprites' scale (1:1 for original sprites),
        // then scale it to the window. Scaling the finished image avoids seams between tiles at
        // fractional zoom levels.
        _device.SetRenderTarget(_target);
        _device.Clear(Background);
        if (Map is { } map)
        {
            var view = new Rectangle(topLeft, worldSize);
            var toTarget = Matrix.CreateTranslation(-topLeft.X, -topLeft.Y, 0) * Matrix.CreateScale(scale, scale, 1);
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, transformMatrix: toTarget);
            CollectEntitiesByTile();
            _humanoids.BeginFrame();
            _objects.BeginFrame();
            _drawnDoors.Clear();
            _frameSounds.BeginFrame();
            // The object being placed is drawn in the same order as placed objects, so walls in front hide it.
            var ghost = _buildTool.Ghost ?? _furnishTool.Ghost;
            var ghostTile = ghost is { } g && Simulation is { } ghostSimulation ? ObjectDrawing.DrawTile(ghostSimulation.Content, g.ObjectType, g.Tile, g.Orientation) : (TilePosition?)null;
            var blueprint = _buildTool.Blueprint;
            // Walls in front of what is being put down let it be seen.
            _renderer.SeeThroughAt = ghostTile ?? (_hiring is not null || _pickedUpStaff is not null ? _staffTile is { } staffAt ? new TilePosition(staffAt.X, staffAt.Y) : null : null);
            _renderer.Draw(batch, map, view, (tile, anchorX, anchorY) =>
            {
                if (blueprint is not null)
                {
                    _blueprints.DrawTile(batch, blueprint, tile, anchorX, anchorY);
                }

                DrawEntities(batch, tile, anchorX, anchorY);
                DrawEditorObjects(batch, tile, anchorX, anchorY);
                if (tile == ghostTile && ghost is { } shown)
                {
                    _objects.Draw(batch, shown.ObjectType, shown.Tile, shown.Orientation, Simulation!.Tick, shown.Tint);
                }
            },
            tile => _entitiesByTile.TryGetValue(tile, out var here) && here.Any(e => e is Humanoid or Rat),
            (tile, anchorX, anchorY) => DrawEarlyEntities(batch, tile, anchorX, anchorY),
            tile => _earlyByTile.TryGetValue(tile, out var early) && early.Count > 0);
            DrawOverlay(batch, map, view);
            DrawPlacingStaff(batch);
            if (_editorTool is { } editor && (_hoveredTile is not null || editor.IsDragging))
            {
                // UIMapEditor:fillCursorArea: a red outline on each tile the cursor covers.
                foreach (var (corner, sizeX, sizeY) in editor.Outlines())
                {
                    for (var dx = 0; dx < sizeX; dx++)
                    {
                        for (var dy = 0; dy < sizeY; dy++)
                        {
                            var (cx, cy) = (corner.X + dx, corner.Y + dy);
                            batch.Draw(_cellOutline, new Vector2((32 * (cx - cy)) - 32, 16 * (cx + cy)), CellOutlineColor);
                        }
                    }
                }
            }

            _buildTool.DrawPreview(batch, _diamond);
            if (_buildTool.IsActive && _hoveredTile is { } cell)
            {
                // While building a room, the tile under the cursor has a red outline, as in CorsixTH's UIEditRoom:draw.
                // Furnishing the corridor is CorsixTH's plain UIPlaceObjects, which draws none.
                batch.Draw(_cellOutline, new Vector2((32 * (cell.X - cell.Y)) - 32, 16 * (cell.X + cell.Y)), CellOutlineColor);
            }
            batch.End();
            if (_capturePath is { } capturePath && _target is not null)
            {
                using (var file = File.Create(capturePath))
                {
                    _target.SaveAsPng(file, _target.Width, _target.Height);
                }

                Console.WriteLine($"Saved {_target.Width}x{_target.Height} capture to {capturePath}");
                _capturePath = null;
                ExitRequested = true;
            }

            RenderCloseUps(batch, map);
        }

        // animation::draw: a frame's sound plays where it was drawn.
        foreach (var (sound, x, y) in _frameSounds.TakeDue())
        {
            _sounds.PlayNumberAt(sound, (new Vector2(x, y) - _camera.TopLeft(viewSize).ToVector2()) * _camera.Zoom, viewSize);
        }

        ScreenCanvas.Bind(_device);
        _device.Clear(Background);

        var sampler = _camera.Zoom >= scale ? SamplerState.PointClamp : SamplerState.LinearClamp;
        batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, sampler);
        var destination = new Rectangle(
            0,
            StatusHeight,
            (int)MathF.Round(worldSize.X * _camera.Zoom),
            (int)MathF.Round(worldSize.Y * _camera.Zoom));
        batch.Draw(_target, destination, Color.White);
        batch.End();

        batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        var viewport = _device.Viewport;
        var building = _buildTool.IsActive || _furnishTool.IsActive;
        if (ShowsStatusBar)
        {
            batch.Draw(_pixel, new Rectangle(0, 0, viewport.Width, StatusHeight), building ? BuildModeColor : Color.Black);
        }
        if (building)
        {
            // A frame round the map, so that building can be told apart at a glance.
            var mapHeight = viewport.Height - StatusHeight;
            batch.Draw(_pixel, new Rectangle(0, StatusHeight, BuildFrameWidth, mapHeight), BuildModeColor);
            batch.Draw(_pixel, new Rectangle(viewport.Width - BuildFrameWidth, StatusHeight, BuildFrameWidth, mapHeight), BuildModeColor);
            batch.Draw(_pixel, new Rectangle(0, viewport.Height - BuildFrameWidth, viewport.Width, BuildFrameWidth), BuildModeColor);
        }

        if (ShowsStatusBar)
        {
            _text.Draw(batch, StatusLine(), new Vector2(8, 4));
            _text.Draw(batch, _error ?? TileLine(), new Vector2(8, 4 + _text.LineHeight + 2));
            DrawDatePanel(batch, viewport);
        }
        else
        {
            DrawBriefLine(batch, building);
            if (_bottomPanel is null)
            {
                // Without the original bottom panel nothing else shows the date and speed.
                DrawDatePanel(batch, viewport);
            }
        }

        _windows.Origin = new Point(8, StatusHeight + 8);
        DrawRoomList(batch);

        // UI:setMenuBackground and UI:draw: the main menu stands on CorsixTH's picture, not on the game.
        var inMainMenu = _mainMenu is not null && _windows.Windows.Contains(_mainMenu);
        if (inMainMenu)
        {
            var screen = new Point(viewport.Width, viewport.Height);
            if (_menuBackground is not null && _menuBackground.Size != MenuBackground.SizeFor(screen))
            {
                // The window was resized enough for the other size to fit better.
                _menuBackground.Dispose();
                _menuBackground = null;
            }

            _menuBackground ??= MenuBackground.Load(_device, screen);
            _menuBackground?.Draw(batch, _pixel, new Rectangle(0, 0, viewport.Width, viewport.Height), _windows.BoundsOf(_mainMenu!));
        }
        else if (_menuBackground is not null)
        {
            _menuBackground.Dispose();
            _menuBackground = null;
        }

        if (IsWindowHostReady)
        {
            var pointer = InputScript.Mouse();
            if (!inMainMenu || _menuBackground is null)
            {
                DrawBottomPanel(batch, viewport, new Point(pointer.X, pointer.Y));
                DrawAdviser(batch, viewport);
            }

            DrawSubtitles(batch);
            _windows.Draw(batch, _text, _pixel, new Point(pointer.X, pointer.Y));
            if (OriginalMenuBar.IsAvailable(this) && (!inMainMenu || _menuBackground is null))
            {
                _menuBar.Draw(batch, this, MenuBarMenus(), viewport.Width, DateTime.Now, _config.TwentyFourHourClock);
            }
            if (_bottomPanel is not null && OriginalBottomPanel.TooltipAt(viewport, new Point(pointer.X, pointer.Y), ((IWindowHost)this).HotkeyText) is { } tooltip)
            {
                OriginalBottomPanel.DrawTooltip(batch, _text, _pixel, viewport, new Point(pointer.X, pointer.Y), tooltip);
            }
        }

        DrawLevelList(batch);
        if (_movie.IsPlaying)
        {
            // App:drawFrame: a movie playing covers everything.
            _movie.Draw(batch, _pixel, new Rectangle(0, 0, viewport.Width, viewport.Height));
        }
        else if (_cursors is not null)
        {
            var at = InputScript.Mouse();
            _cursors.Draw(batch, CurrentCursor(at), new Point(at.X, at.Y));
        }

        batch.End();
    }

    /// <inheritdoc/>
    public bool DrawsCursor => _cursors is not null;

    /// <inheritdoc/>
    /// <remarks>
    /// As current games do: only in a game, so not in the main menu, a film or a full-screen dialog such as the
    /// casebook, where the map cannot be scrolled and the cursor is better free.
    /// </remarks>
    public bool CapturesMouse => _config.CaptureMouse && !_mouseReleased && _session is not null && _mainMenu is null
        && !_movie.IsPlaying && !_windows.Windows.Any(window => window.IsFullscreen(this) && MouseGrabPolicy.CoversGame(window.FullscreenSize));

    /// <summary><c>UI:releaseMouse</c> (Ctrl+F10): the cursor is let go until a mouse button is pressed (<c>onMouseDown</c>).</summary>
    private bool _mouseReleased;

    /// <summary>
    /// GameUI:onCursorWorldPositionChange's cursor: the epidemic's over an infected patient during a cover-up, else the
    /// hover cursor of what is under it (a patient, staff, a door or reception desk's queue, a machine to repair, a rat),
    /// else the pressed one while a button is down; editing a room or carrying staff have theirs.
    /// </summary>
    private GameCursor CurrentCursor(MouseState mouse)
    {
        if (_cursorOverWindow && _windows.CursorAt(new Point(mouse.X, mouse.Y)) is { } windowCursor)
        {
            return windowCursor;
        }

        if (_pickingRoomToEdit)
        {
            return GameCursor.EditRoom;
        }

        if (_pickedUpStaff is not null)
        {
            return GameCursor.Grab;
        }

        if (Simulation is { } waiting && _buildTool.IsWaitingForRoom(waiting))
        {
            return GameCursor.Sleep;
        }

        var down = mouse.LeftButton == ButtonState.Pressed || mouse.RightButton == ButtonState.Pressed;
        if (!_cursorOverWindow && _buildTool.IsActive
            && _buildTool.Cursor(_hoveredTile is { } buildTile ? new TilePosition(buildTile.X, buildTile.Y) : null) is { } buildCursor)
        {
            return buildCursor;
        }
        if (_cursorOverWindow || Simulation is not { } simulation || _session is null || _hoveredTile is not { } hovered
            || _buildTool.IsActive || _furnishTool.IsActive)
        {
            return down ? GameCursor.Clicked : GameCursor.Default;
        }

        var tile = new TilePosition(hovered.X, hovered.Y);
        if (_vaccinationMode && CoverUpRunning() is not null
            && !(simulation.FindEntity(_hoveredHumanoid) is Humanoid { Behaviour: PatientBehaviour { IsInfected: true } }))
        {
            // In vaccination mode the epidemic's hover cursor shows over everything else.
            return GameCursor.EpidemicHover;
        }

        if (simulation.FindEntity(_hoveredHumanoid) is Humanoid humanoid)
        {
            if (humanoid.Behaviour is PatientBehaviour { IsInfected: true } && humanoid.HospitalPlayer < simulation.Hospitals.Count
                && simulation.Hospitals[humanoid.HospitalPlayer].Epidemics.Current is { IsCoverUp: true, IsWatchRunning: true })
            {
                return GameCursor.Epidemic;
            }

            if (humanoid.Behaviour is PatientBehaviour)
            {
                return GameCursor.Patient;
            }

            if (humanoid.Profile is not null && !humanoid.IsFired)
            {
                return GameCursor.Staff;
            }

            return GameCursor.Default;
        }

        if (simulation.Entities.OfType<Rat>().Any(rat => !rat.IsGone && RatDrawing(rat).Tile == tile))
        {
            return GameCursor.KillRatHover;
        }

        if (RatNear(simulation, _session.Player, hovered) is not null)
        {
            return GameCursor.KillRat;
        }

        if (MachineUnderCursor(simulation, tile) is { } machine && simulation.FindEntity(machine.RoomId) is Room { IsActive: true })
        {
            return GameCursor.Repair;
        }

        if (DeskUnderCursor(simulation, tile) is not null
            || simulation.Entities.OfType<RoomDoor>().Any(door => door.Tile == tile))
        {
            return GameCursor.Queue;
        }

        return down ? GameCursor.Clicked : GameCursor.Default;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _target?.Dispose();
        _blueprints.Dispose();
        _floatingDollars.Dispose();
        _cursors?.Dispose();
        _panelSprites?.Dispose();
        _bottomPanel?.Dispose();
        _graphics.Dispose();
        foreach (var closeUp in _closeUps.Values)
        {
            closeUp.Dispose();
        }
        _blackText?.Dispose();
        _animations.Dispose();
        _blocks.Dispose();
        _text.Dispose();
        _unicodeFonts.Dispose();
        _diamond.Dispose();
        _cellOutline.Dispose();
        _pixel.Dispose();
        _sounds.Dispose();
        _music.Dispose();
        _movie.Dispose();
        _menuBackground?.Dispose();
    }

    /// <summary>Where on the map view the middle of a tile is, for sounds heard there.</summary>
    private Vector2 ScreenOf(TilePosition tile)
    {
        var topLeft = _camera.TopLeft(ViewSize);
        return (Camera.TileCenter(tile.X, tile.Y) - topLeft.ToVector2()) * _camera.Zoom;
    }

    /// <summary>
    /// The status bar's height: two lines of text, taller with a TrueType font. The bar is a developer's aid, shown
    /// only with --debug-status and in the map editor; otherwise the map starts at the top of the screen.
    /// </summary>
    private int StatusHeight => ShowsStatusBar ? Math.Max(36, (2 * _text.LineHeight) + 10) : 0;

    /// <summary>Whether the black status bar with the debug lines is shown (--debug-status, or the map editor).</summary>
    private bool ShowsStatusBar => _debugStatus || _editorTool is not null;

    private Point ViewSize => new(_device.Viewport.Width, Math.Max(1, _device.Viewport.Height - StatusHeight));

    private void SelectLevel(int index)
    {
        if (_levels.Count == 0)
        {
            _error = UiText.Get("openwards.status.no_levels", "No levels found.");
            return;
        }

        CloseMainMenu();
        _levelIndex = index;
        _lastYearSeen = null;
        _lastOutcome = EndOutcome.None;
        _session?.Dispose();
        _session = null;
        try
        {
            var level = _levels[index].Load();
            var local = new LocalSession(_levels[index].Name, level.StartSimulation(seed: 1), _saves, _config.AutosaveFrequency);
            local.SetSpeed(_initialSpeed);
            _session = local;
            _error = null;
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            _error = UiText.Format("openwards.status.cannot_load_level", "Cannot load %1%: %2%", _levels[index].Name, e.Message);
        }

        PrepareMapIfReady();
    }

    /// <summary>
    /// <c>App:quickSave</c> and <c>App:quickLoad</c> (Alt+Shift+S and Alt+Shift+L): only for local games, and not while
    /// building. Loading without a quick save makes one, as CorsixTH does.
    /// </summary>
    /// <returns>Whether another game was loaded.</returns>
    private bool UpdateQuickSave(KeyboardState keys, Func<Keys, bool> pressed)
    {
        if (_session is not LocalSession local)
        {
            return false;
        }

        var save = HotkeyInput.Pressed(_hotkeys["ingame_quickSave"], keys, pressed);
        var load = HotkeyInput.Pressed(_hotkeys["ingame_quickLoad"], keys, pressed);
        if (!save && !load)
        {
            return false;
        }

        return QuickSave(load);
    }

    /// <summary>Saves to the quick save, or loads it (making one if there is none).</summary>
    /// <returns>Whether another game was loaded.</returns>
    private bool QuickSave(bool load)
    {
        if (_session is not LocalSession local)
        {
            return false;
        }

        if (_buildTool.IsActive || _furnishTool.IsActive)
        {
            ShowNotice(UiText.Get("openwards.status.finish_building_first", "Finish building before saving or loading"));
            return false;
        }

        if (load && File.Exists(_saves.QuickSavePath))
        {
            return LoadGame(_saves.QuickSavePath);
        }

        try
        {
            SaveGameStore.Save(_saves.QuickSavePath, local.Name, local.Simulation);
            ShowNotice(load ? UiText.Get("openwards.status.no_quick_save", "There was no quick save to load, so one has been made") : UiText.Get("openwards.status.quick_saved", "Quick saved"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNotice(UiText.Format("openwards.status.cannot_save", "Cannot save: %1%", e.Message));
        }

        return false;
    }

    /// <summary>Replaces the local game with a saved one, keeping the game speed.</summary>
    /// <returns>Whether the game was loaded.</returns>
    private bool LoadGame(string path)
    {
        SavedGame saved;
        try
        {
            saved = File.Exists(path) && new FileInfo(path).Length == OriginalSaveGame.Length
                ? ImportOriginalSave(path)
                : SaveGameStore.Load(path, BaseGameContent.Create());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
            ShowNotice(UiText.Format("openwards.status.cannot_load", "Cannot load %1%: %2%", Path.GetFileName(path), e.Message));
            return false;
        }

        var speed = _mainMenu is null && _session is { CanChangeSpeed: true } previous ? previous.Speed : _initialSpeed;
        CloseMainMenu();
        _session?.Dispose();
        var local = new LocalSession(saved.LevelName, saved.Simulation, _saves, _config.AutosaveFrequency);
        local.SetSpeed(speed == GameSpeed.SpeedUp ? GameSpeed.Normal : speed);
        _session = local;
        _error = null;
        var index = _levels.ToList().FindIndex(level => string.Equals(level.Name, saved.LevelName, StringComparison.Ordinal));
        if (index >= 0)
        {
            _levelIndex = index;
        }

        // The demo's steps belong to the game it was started on.
        _demoPending = _demoDeskPending = _demoDiagnosisPending = _demoMachinePending = _demoToiletsPending = false;
        _demoFurniture.Clear();
        PrepareMapIfReady();
        ShowNotice(UiText.Format("openwards.status.loaded", "Loaded %1%", Path.GetFileName(path)));
        return true;
    }

    /// <summary>
    /// A save of the original game, rebuilt on its level (<see cref="OriginalSaveImporter"/>); what could not be rebuilt
    /// is told in a notice.
    /// </summary>
    private SavedGame ImportOriginalSave(string path)
    {
        var save = OriginalSaveGame.Parse(File.ReadAllBytes(path));
        var (simulation, report) = OriginalSaveImporter.Start(_install, save, seed: (ulong)Environment.TickCount64);
        if (report.Problems.Count > 0)
        {
            ShowNotice(UiText.Format("openwards.status.import_problems", "Could not rebuild from the save: %1%", report.Problems.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        return new SavedGame($"LEVEL.L{save.Level}", simulation);
    }

    /// <summary>
    /// Sets up everything that depends on the map once it is available: immediately for local
    /// games, after joining for network games.
    /// </summary>
    private void PrepareMapIfReady()
    {
        if (Map is not { } map || ReferenceEquals(map, _preparedMap))
        {
            return;
        }

        _preparedMap = map;
        _selected = null;
        _humanoids.Reset();
        _doors.Reset();
        _hospitalTiles.Clear();
        for (var y = 0; y < map.Height; y++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (map[x, y].Has(TileFlags.Passable | TileFlags.Hospital))
                {
                    _hospitalTiles.Add(new TilePosition(x, y));
                }
            }
        }

        ResetCamera();
        if (_demoPending)
        {
            _demoPending = false;
            StartDemo(map);
        }
    }

    /// <summary>
    /// Spawns twenty patients, half inside and half outside near the camera, hires one of each kind of
    /// staff, and lets them wander.
    /// </summary>
    private void StartDemo(LevelMap map)
    {
        if (_session is null || _hospitalTiles.Count == 0)
        {
            return;
        }

        // The demo does not wait a hundred days for the hospital to open.
        _session.Submit(new OpenHospitalCommand());

        var start = map.GetCameraTile(Math.Min(_session.Player, map.PlayerCount - 1));
        var outside = new List<TilePosition>();
        for (var y = Math.Max(0, start.Y - 12); y <= Math.Min(map.Height - 1, start.Y + 12); y++)
        {
            for (var x = Math.Max(0, start.X - 12); x <= Math.Min(map.Width - 1, start.X + 12); x++)
            {
                var tile = map[x, y];
                if (tile.Has(TileFlags.Passable) && !tile.Has(TileFlags.Hospital))
                {
                    outside.Add(new TilePosition(x, y));
                }
            }
        }

        // Build a GP's office first, so that spawned patients cannot appear inside its walls.
        var roomPlan = Simulation?.Content.FindRoom("gp") is { } gp ? FindRoomSpot(map, gp, start) : null;
        if (roomPlan is { } plan)
        {
            _session.Submit(new BuildRoomCommand(plan));
        }

        for (var i = 0; i < 20; i++)
        {
            var pool = i % 2 == 0 || outside.Count == 0 ? _hospitalTiles : outside;
            _session.Submit(new SpawnHumanoidCommand(pool[_random.Next(pool.Count)]));
        }

        // Hire one of each kind of staff and place them near the camera, so that they can be seen.
        // Not inside the room, which is built first: receptionists may not go into rooms.
        var nearby = _hospitalTiles
            .Where(tile => roomPlan is not { } built || !new TileRect(built.Area.X - 1, built.Area.Y - 1, built.Area.Width + 2, built.Area.Height + 2).Contains(tile))
            .OrderBy(tile => Math.Abs(tile.X - start.X) + Math.Abs(tile.Y - start.Y))
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .Take(40)
            .ToList();
        // The most skilled candidate of each kind, so that the GP diagnoses patients well enough to send them on to
        // the pharmacy while there are no diagnosis rooms.
        foreach (var staffClass in StaffClasses.HireOrder)
        {
            var candidates = Simulation?.AvailableStaff(staffClass) ?? [];
            var best = candidates.Count == 0 ? 0 : candidates.Select((profile, index) => (profile.Skill, index)).Max().index;
            _session.Submit(new HireStaffCommand(staffClass, best, nearby[_random.Next(nearby.Count)]));
        }

        _wander = true;
    }

    /// <summary>
    /// Places the demo's furniture in its unfinished room (the GP's office, then the pharmacy), one object at a
    /// time: each placement is checked against the state that includes the previous object. The room is confirmed
    /// with its last object.
    /// </summary>
    private void FurnishDemoRoom()
    {
        if (_session is null || Simulation is not { } simulation || _demoFurniture.Count == 0)
        {
            return;
        }

        var objectId = _demoFurniture.Peek();
        var room = simulation.Entities.OfType<Room>()
            .FirstOrDefault(r => !r.IsActive && simulation.Content.FindRoom(r.RoomType)?.Allows(objectId) == true);
        if (room is null || simulation.Entities.OfType<RoomObject>().Count() < _demoFurnitureExpected)
        {
            return;
        }

        _demoFurniture.Dequeue();
        var confirm = !_demoFurniture.TryPeek(out var next) || simulation.Content.FindRoom(room.RoomType)?.Allows(next) != true;
        foreach (var tile in DemoFurnitureTiles(room.Area, objectId))
        {
            foreach (var orientation in DemoOrientations(objectId))
            {
                if (ObjectPlacer.Check(simulation, objectId, tile, orientation, _session.Player) == ObjectPlacementProblem.None)
                {
                    _session.Submit(new PlaceObjectCommand(objectId, tile, orientation));
                    _demoFurnitureExpected++;
                    if (confirm)
                    {
                        // Commands of one tick run in order, so the room is complete when this runs.
                        _session.Submit(new ConfirmRoomCommand(room.Id));
                    }

                    return;
                }
            }
        }
    }

    /// <summary>
    /// Two radiators for each demo room: without them its staff grow cold and unhappy in the first winter, ask for a raise
    /// and are fired when nobody answers (Humanoid:tickDay, Staff:checkIfWaitedTooLongForRaise); two keep a room of four
    /// by four tiles comfortable at the default heat.
    /// </summary>
    private static readonly string[] DemoRadiators = ["radiator", "radiator"];

    private void EnqueueDemoRadiators()
    {
        foreach (var radiator in DemoRadiators)
        {
            _demoFurniture.Enqueue(radiator);
        }
    }

    /// <summary>The orientations the demo tries for an object; radiators only have sprites facing north and east.</summary>
    private static Direction[] DemoOrientations(string objectId) => string.Equals(objectId, "radiator", StringComparison.Ordinal)
        ? [Direction.North, Direction.East]
        : [Direction.North, Direction.East, Direction.South, Direction.West];

    /// <summary>
    /// The tiles of the demo room in the order they are tried for an object: row by row from the back. The
    /// patient's chair keeps off the south and east walls, which are drawn in front of it and would hide the
    /// patient sitting on it.
    /// </summary>
    private static IEnumerable<TilePosition> DemoFurnitureTiles(TileRect area, string objectId)
    {
        var tiles = new List<TilePosition>();
        for (var y = area.Y; y < area.Bottom; y++)
        {
            for (var x = area.X; x < area.Right; x++)
            {
                tiles.Add(new TilePosition(x, y));
            }
        }

        if (!string.Equals(objectId, "chair", StringComparison.Ordinal))
        {
            return tiles;
        }

        return tiles.OrderBy(tile => (tile.Y == area.Bottom - 1 ? 1 : 0) + (tile.X == area.Right - 1 ? 1 : 0));
    }

    /// <summary>
    /// Once the demo's GP's office works, buys a reception desk near the camera, so that patients start to
    /// arrive and the hired receptionist goes to it.
    /// </summary>
    private void PlaceDemoReceptionDesk()
    {
        if (!_demoDeskPending || _session is null || Simulation is not { } simulation
            || !simulation.Entities.OfType<Room>().Any(room => room.IsActive))
        {
            return;
        }

        _demoDeskPending = false;
        var start = simulation.Map.GetCameraTile(Math.Min(_session.Player, simulation.Map.PlayerCount - 1));

        // A pharmacy next, for the patients the GP diagnoses with a disease cured by a drink. The desk keeps clear of it.
        var keepClear = new TileRect(0, 0, 0, 0);
        if (simulation.Content.FindRoom("pharmacy") is { } pharmacy && FindRoomSpot(simulation.Map, pharmacy, start) is { } pharmacyPlan)
        {
            _session.Submit(new BuildRoomCommand(pharmacyPlan));
            _demoFurniture.Enqueue("pharmacy_cabinet");
            EnqueueDemoRadiators();
            var area = pharmacyPlan.Area;
            keepClear = new TileRect(area.X - 2, area.Y - 2, area.Width + 4, area.Height + 4);
        }

        foreach (var tile in _hospitalTiles.Where(t => !keepClear.Contains(t)).OrderBy(t => Math.Abs(t.X - start.X) + Math.Abs(t.Y - start.Y)).ThenBy(t => t.Y).ThenBy(t => t.X))
        {
            foreach (var orientation in new[] { Direction.North, Direction.East, Direction.South, Direction.West })
            {
                if (ObjectPlacer.Check(simulation, ReceptionDesks.ObjectId, tile, orientation, _session.Player) == ObjectPlacementProblem.None)
                {
                    _session.Submit(new PlaceObjectCommand(ReceptionDesks.ObjectId, tile, orientation));
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Once the demo's general diagnosis works, builds toilets near the camera with two toilets and a sink, for the
    /// patients who need them.
    /// </summary>
    private void BuildDemoToilets()
    {
        if (!_demoToiletsPending || _session is null || Simulation is not { } simulation
            || !simulation.Entities.OfType<Room>().Any(room => room is { IsActive: true, RoomType: "general_diag" }))
        {
            return;
        }

        _demoToiletsPending = false;
        var start = simulation.Map.GetCameraTile(Math.Min(_session.Player, simulation.Map.PlayerCount - 1));
        if (simulation.Content.FindRoom("toilets") is not { } room || FindRoomSpot(simulation.Map, room, start) is not { } plan)
        {
            return;
        }

        _session.Submit(new BuildRoomCommand(plan));
        foreach (var item in new[] { "loo", "loo", "sink" })
        {
            _demoFurniture.Enqueue(item);
        }

        EnqueueDemoRadiators();
    }

    /// <summary>
    /// Once the demo's reception desk stands, buys a drinks machine a few tiles from it, for the patients who grow thirsty
    /// while they wait.
    /// </summary>
    private void PlaceDemoDrinksMachine()
    {
        if (!_demoMachinePending || _session is null || Simulation is not { } simulation
            || simulation.Entities.OfType<RoomObject>().FirstOrDefault(o => o.Desk is not null) is not { } desk)
        {
            return;
        }

        _demoMachinePending = false;
        foreach (var tile in _hospitalTiles.Where(t => Math.Abs(t.X - desk.Tile.X) + Math.Abs(t.Y - desk.Tile.Y) >= 3)
            .OrderBy(t => Math.Abs(t.X - desk.Tile.X) + Math.Abs(t.Y - desk.Tile.Y)).ThenBy(t => t.Y).ThenBy(t => t.X))
        {
            foreach (var orientation in new[] { Direction.South, Direction.East, Direction.North, Direction.West })
            {
                if (ObjectPlacer.Check(simulation, "drinks_machine", tile, orientation, _session.Player) == ObjectPlacementProblem.None)
                {
                    _session.Submit(new PlaceObjectCommand("drinks_machine", tile, orientation));
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Once the demo's pharmacy works, builds general diagnosis near the camera and hires a second doctor for it, so
    /// that patients the GP cannot diagnose at once get examined and come back.
    /// </summary>
    private void BuildDemoGeneralDiagnosis()
    {
        if (!_demoDiagnosisPending || _session is null || Simulation is not { } simulation
            || !simulation.Entities.OfType<Room>().Any(room => room is { IsActive: true, RoomType: "pharmacy" }))
        {
            return;
        }

        _demoDiagnosisPending = false;
        var start = simulation.Map.GetCameraTile(Math.Min(_session.Player, simulation.Map.PlayerCount - 1));
        if (simulation.Content.FindRoom("general_diag") is not { } room || FindRoomSpot(simulation.Map, room, start) is not { } plan)
        {
            return;
        }

        _session.Submit(new BuildRoomCommand(plan));
        _demoFurniture.Enqueue("screen");
        _demoFurniture.Enqueue("crash_trolley");
        EnqueueDemoRadiators();
        var area = new TileRect(plan.Area.X - 1, plan.Area.Y - 1, plan.Area.Width + 2, plan.Area.Height + 2);
        var tile = _hospitalTiles.Where(t => !area.Contains(t) && simulation.Map[t.X, t.Y].Has(TileFlags.Passable) && simulation.Map[t.X, t.Y].RoomId == 0)
            .OrderBy(t => Math.Abs(t.X - start.X) + Math.Abs(t.Y - start.Y)).ThenBy(t => t.Y).ThenBy(t => t.X).FirstOrDefault();
        var doctors = simulation.AvailableStaff(StaffClass.Doctor);
        var best = doctors.Count == 0 ? 0 : doctors.Select((profile, index) => (profile.Skill, index)).Max().index;
        _session.Submit(new HireStaffCommand(StaffClass.Doctor, best, tile));
    }

    /// <summary>Finds the valid room plan closest to <paramref name="near"/>, trying a few sizes and every door position.</summary>
    private RoomPlan? FindRoomSpot(LevelMap map, RoomDefinition room, TilePosition near)
    {
        var size = room.MinimumSize + 1;
        for (var radius = 0; radius < 20; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                    {
                        continue;
                    }

                    var area = new TileRect(near.X + dx, near.Y + dy, size, size);
                    if (RoomBuilder.CheckArea(map, room, area, _session!.Player) != RoomPlanProblem.None)
                    {
                        continue;
                    }

                    for (var i = 0; i < size; i++)
                    {
                        DoorPlacement[] doors =
                        [
                            new(new TilePosition(area.X + i, area.Bottom), EdgeOrientation.North),
                            new(new TilePosition(area.Right, area.Y + i), EdgeOrientation.West),
                            new(new TilePosition(area.X + i, area.Y), EdgeOrientation.North),
                            new(new TilePosition(area.X, area.Y + i), EdgeOrientation.West),
                        ];
                        foreach (var door in doors)
                        {
                            var plan = new RoomPlan(room.Id, area, door);
                            if (RoomBuilder.Check(map, Simulation!.Content, plan, _session.Player) == RoomPlanProblem.None)
                            {
                                return plan;
                            }
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// CorsixTH's camera hotkeys: = and − zoom in and out towards the cursor (five times as far with Shift,
    /// <c>World:adjustZoom</c>), 0 goes back to the original size (<c>World:resetZoom</c>), Alt+digit remembers the view
    /// and Ctrl+digit goes back to it (<c>GameUI:setMapRecallPosition</c>, <c>recallMapPosition</c>). Until the hire window
    /// exists, Ctrl+1 to Ctrl+4 hire staff instead, so those keys do not recall. All of them are hotkeys
    /// (<c>ingame_zoom_in</c>, <c>ingame_zoom_in_more</c>, <c>ingame_zoom_out</c>, <c>ingame_zoom_out_more</c>,
    /// <c>ingame_reset_zoom</c>, <c>ingame_storePosition_N</c>, <c>ingame_recallPosition_N</c>).
    /// </summary>
    private void UpdateCameraKeys(KeyboardState keys, MouseState mouse, Point viewSize, Func<Keys, bool> pressed)
    {
        bool hotkey(string action) => HotkeyInput.Pressed(_hotkeys[action], keys, pressed);
        var zoomDelta = (hotkey("ingame_zoom_in") ? 1 : 0) + (hotkey("ingame_zoom_in_more") ? 5 : 0)
            - (hotkey("ingame_zoom_out") ? 1 : 0) - (hotkey("ingame_zoom_out_more") ? 5 : 0);
        if (zoomDelta != 0 && _scrolling.Zoomed(_camera.Zoom, zoomDelta, _device.Viewport.Width) is { } zoomed)
        {
            _camera.ZoomAt((float)zoomed, new Point(mouse.X, mouse.Y - StatusHeight), viewSize);
        }

        if (hotkey("ingame_reset_zoom"))
        {
            _camera.ZoomAt(1f, new Point(viewSize.X / 2, viewSize.Y / 2), viewSize);
        }

        for (var digit = 0; digit <= 9; digit++)
        {
            var suffix = digit.ToString(CultureInfo.InvariantCulture);
            if (hotkey("ingame_storePosition_" + suffix))
            {
                _recallPositions[digit] = (_camera.Center, _camera.Zoom);
            }
            else if (hotkey("ingame_recallPosition_" + suffix) && !IsDebugHireKey(_hotkeys["ingame_recallPosition_" + suffix])
                && _recallPositions[digit] is var (center, zoom))
            {
                _camera.ZoomAt(zoom, new Point(viewSize.X / 2, viewSize.Y / 2), viewSize);
                _camera.Center = center;
            }
        }
    }

    /// <summary>Ctrl+1 to Ctrl+4, which hire staff for now.</summary>
    private static bool IsDebugHireKey(Hotkey? key) =>
        key is { Ctrl: true, Shift: false, Alt: false, Key: "1" or "2" or "3" or "4" };

    /// <summary>The key actions a window can take (<see cref="UiWindow.TakesKey"/>) and the key for each.</summary>
    private IEnumerable<(string Action, Keys Key)> WindowKeys()
    {
        foreach (var action in new[] { "ingame_scroll_up", "ingame_scroll_down", "ingame_scroll_left", "ingame_scroll_right", "ingame_patient_gohome" })
        {
            if (_hotkeys[action] is { } key && HotkeyInput.ToKeys(key.Key) is { } down)
            {
                yield return (action, down);
            }
        }

        yield return ("global_confirm", Keys.Enter);
    }

    /// <summary>An <c>ingame_scroll_*</c> key held; Shift (<c>ingame_scroll_shift</c>) only makes it faster, so modifiers do not matter.</summary>
    private bool ScrollKeyHeld(string action, KeyboardState keys) =>
        _hotkeys[action] is { } key && HotkeyInput.ToKeys(key.Key) is { } down && keys.IsKeyDown(down);

    private void ResetCamera()
    {
        if (Map is { } map && _session is not null)
        {
            // Each player starts looking at their own hospital.
            var start = map.GetCameraTile(Math.Min(_session.Player, map.PlayerCount - 1));
            _camera.Center = Camera.TileCenter(start.X, start.Y);
        }
    }

    /// <summary>The debug menu's limit camera (<c>limit_to_visible_diamond</c>, on to begin with).</summary>
    private bool _limitCamera = true;

    /// <summary>
    /// With the camera limited, its middle stays over the map's diamond (<c>GameUI:limitCamera</c>); without, only within
    /// the map's bounding box.
    /// </summary>
    private void ClampCamera()
    {
        if (Map is not { } map)
        {
            return;
        }

        if (_limitCamera)
        {
            var tileX = Math.Clamp(((_camera.Center.X / 32f) + (_camera.Center.Y / 16f)) / 2f, 0f, map.Width);
            var tileY = Math.Clamp(((_camera.Center.Y / 16f) - (_camera.Center.X / 32f)) / 2f, 0f, map.Height);
            _camera.Center = new Vector2(32f * (tileX - tileY), 16f * (tileX + tileY));
            return;
        }

        _camera.Center = new Vector2(
            Math.Clamp(_camera.Center.X, -32f * map.Height, 32f * map.Width),
            Math.Clamp(_camera.Center.Y, 0f, 16f * (map.Width + map.Height)));
    }

    /// <summary>
    /// <c>UIMakeDebugPatient:buttonClicked</c>: a diagnosed patient with the disease, on the passable tile nearest the
    /// view's middle.
    /// </summary>
    private void MakeDebugPatient(Sim.Diseases.DiseaseDefinition disease)
    {
        if (Simulation is not { } simulation || _session is null)
        {
            return;
        }

        var middle = Camera.WorldToTile(_camera.Center);
        for (var radius = 0; radius < 12; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var x = middle.X + dx;
                    var y = middle.Y + dy;
                    if ((Math.Abs(dx) == radius || Math.Abs(dy) == radius) && simulation.Map.Contains(x, y)
                        && simulation.Map[x, y].Has(TileFlags.Passable) && simulation.Map[x, y].Has(TileFlags.Hospital))
                    {
                        _session.Submit(new MakeDebugPatientCommand(disease.Id, new TilePosition(x, y)));
                        return;
                    }
                }
            }
        }
    }

    private void EnsureTarget(Point size)
    {
        if (_target is not null && _target.Width == size.X && _target.Height == size.Y)
        {
            return;
        }

        _target?.Dispose();
        _target = new RenderTarget2D(_device, size.X, size.Y);
    }

    /// <summary>Runs the session and the input-free test controls (wandering, demo furnishing).</summary>
    private void AdvanceSession(GameTime gameTime)
    {
        if (_session is null)
        {
            return;
        }

        _session.Update(gameTime.ElapsedGameTime);
        PrepareMapIfReady();
        FurnishDemoRoom();
        PlaceDemoReceptionDesk();
        BuildDemoGeneralDiagnosis();
        PlaceDemoDrinksMachine();
        BuildDemoToilets();
        if (Simulation is not { } simulation || !_wander || _hospitalTiles.Count == 0 || _session.IsPaused)
        {
            return;
        }

        foreach (var humanoid in simulation.Entities.OfType<Humanoid>())
        {
            // People with something to do (patients on a visit, staff at work) are left to it.
            if (humanoid.Behaviour is null && humanoid.Destination is null && humanoid.Id != _selected && _random.Next(60) == 0)
            {
                _session.Submit(new MeanderCommand(humanoid.Id));
            }
        }
    }

    /// <summary>
    /// CorsixTH's speed keys: 1 to 5 pick a speed, P or Space pause and unpause, Z held down speeds the
    /// game up and going back to the previous speed when let go. The speed also changes while building,
    /// as game time runs on in the original.
    /// </summary>
    private void UpdateSpeed(KeyboardState keys, Func<Keys, bool> pressed)
    {
        // The level behind the main menu stays paused.
        if (_session is not { CanChangeSpeed: true } session || _mainMenu is not null)
        {
            return;
        }

        var control = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);
        var alt = keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt);
        (string Action, GameSpeed Speed)[] speedKeys =
        [
            ("ingame_gamespeed_slowest", GameSpeed.Slowest), ("ingame_gamespeed_slower", GameSpeed.Slower),
            ("ingame_gamespeed_normal", GameSpeed.Normal), ("ingame_gamespeed_max", GameSpeed.MaxSpeed),
            ("ingame_gamespeed_thensome", GameSpeed.AndThenSomeMore),
        ];
        foreach (var (action, speed) in speedKeys)
        {
            if (HotkeyInput.Pressed(_hotkeys[action], keys, pressed))
            {
                session.SetSpeed(speed);
            }
        }

        // The insane speed has no hotkey in CorsixTH (only its menu): 6 here.
        if (!control && !alt && pressed(Keys.D6))
        {
            session.SetSpeed(GameSpeed.Insane);
        }

        // While building, P is the pick-up key; while placing an object the rotate object key (space) turns it.
        var rotating = IsPlacingObject && HotkeyInput.Pressed(_hotkeys["ingame_rotateobject"], keys, pressed);
        if ((pressed(Keys.Space) && !rotating) || (HotkeyInput.Pressed(_hotkeys["ingame_pause"], keys, pressed) && !_buildTool.IsActive && !rotating))
        {
            if (session.IsPaused)
            {
                session.ResumeSpeed();
            }
            else
            {
                session.SetSpeed(GameSpeed.Pause);
            }
        }

        // In a network game another player may hold it: only letting go of one's own key ends it.
        if (HotkeyInput.Held(_hotkeys["ingame_gamespeed_speedup"], keys))
        {
            session.SetSpeed(GameSpeed.SpeedUp);
            _speedUpHeld = true;
        }
        else if (_speedUpHeld)
        {
            _speedUpHeld = false;
            if (session.Speed == GameSpeed.SpeedUp)
            {
                session.ResumeSpeed();
            }
        }
    }

    /// <summary>Watching a replay, [ winds it back a month and ] on a month; with Shift, a year.</summary>
    private void UpdateReplaySeek(KeyboardState keys, Func<Keys, bool> pressed)
    {
        if (_session is not ReplaySession replay)
        {
            return;
        }

        var shift = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
        var step = (shift ? 12 : 1) * ReplaySeekMonth;
        if (pressed(Keys.OemOpenBrackets))
        {
            replay.Seek(replay.Simulation.Tick - step);
        }
        else if (pressed(Keys.OemCloseBrackets))
        {
            replay.Seek(replay.Simulation.Tick + step);
        }
    }

    /// <summary>The ticks a replay seek moves by: thirty game days.</summary>
    private const long ReplaySeekMonth = 30 * GameDate.HoursPerDay;

    private void UpdateSimulation(GameTime gameTime, MouseState mouse, KeyboardState keys, Func<Keys, bool> pressed)
    {
        AdvanceSession(gameTime);
        if (_session is null || Simulation is not { } simulation)
        {
            return;
        }

        if (_editorTool is { } editorTool)
        {
            UpdateMapEditor(editorTool, mouse);
            return;
        }

        UpdateSpeed(keys, pressed);
        UpdateReplaySeek(keys, pressed);

        if (pressed(Keys.R) && !keys.IsKeyDown(Keys.LeftShift) && !keys.IsKeyDown(Keys.RightShift))
        {
            _wander = !_wander;
        }


        // Until the watch window exists, Shift+O opens the hospital early, as clicking the watch's button does.
        if (pressed(Keys.O) && (keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift)) && _session.Player < simulation.Hospitals.Count && !simulation.Hospitals[_session.Player].IsOpen)
        {
            _session.Submit(new OpenHospitalCommand());
        }

        // Until the build room window exists, B opens a list of the rooms to build.
        var wasBuilding = _buildTool.IsActive;
        var shift = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift);
        var ctrl = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);

        var alt = keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt);
        if (UpdateQuickSave(keys, pressed))
        {
            // Another game now: this frame's input was meant for the old one.
            return;
        }

        // Until the bank manager exists: L borrows 5,000, Shift+L pays 5,000 back; with Ctrl as much as allowed.
        if (pressed(Keys.L) && !alt)
        {
            _session.Submit(shift ? new RepayCommand(ctrl) : new BorrowCommand(ctrl));
        }

        // Until faxes exist, E accepts the emergency offered and Shift+E refuses it.
        if (pressed(Keys.E) && simulation.Hospitals[_session.Player].Emergencies.Current is { IsAccepted: false })
        {
            _session.Submit(new AnswerEmergencyCommand(!shift));
        }

        // Until faxes exist, I covers up the epidemic the hospital was told of and Shift+I declares it.
        if (pressed(Keys.I) && simulation.Hospitals[_session.Player].Epidemics.IsFaxPending)
        {
            _session.Submit(new AnswerEpidemicCommand(!shift));
        }

        // Until faxes exist, K answers the first waiting patient's fax: wait; Shift+K sends them home, Ctrl+K guesses the cure.
        if (pressed(Keys.K) && FirstQuestion(simulation, _session.Player) is { } asking)
        {
            _session.Submit(new AnswerPatientCommand(asking.Id, ctrl ? PatientAnswer.GuessCure : shift ? PatientAnswer.SendHome : PatientAnswer.Wait));
        }

        // GameUI's ingame_setTransparent (X, while held) and ingame_toggleTransparent (Shift+X): walls half transparent.
        if (HotkeyInput.Pressed(_hotkeys["ingame_toggleTransparent"], keys, pressed))
        {
            _wallsTransparentToggled = !_wallsTransparentToggled;
        }

        _renderer.TransparentWalls = _wallsTransparentToggled || HotkeyInput.Held(_hotkeys["ingame_setTransparent"], keys);

        // Until staff can be grabbed with the hand cursor, M picks up the player's member of staff nearest the cursor
        // (within a tile) and M again puts them down at the cursor; on the same tile, nothing happens.
        if (pressed(Keys.M) && (_pickedUpStaff is null ? _hoveredTile : _staffTile) is { } handPoint)
        {
            var hand = new TilePosition(handPoint.X, handPoint.Y);
            if (_pickedUpStaff is { } carried)
            {
                PutDownCarried(simulation, carried, hand, StaffPlacing.Click(candidateGone: false, left: true, right: false, _session.IsPaused));
            }
            else
            {
                _pickedUpStaff = simulation.Entities.OfType<Humanoid>()
                    .Where(h => h.Profile is not null && h.Employer == _session.Player && !h.IsDespawning && !h.IsFired)
                    .Where(h => Math.Abs(h.Tile.X - hand.X) <= 1 && Math.Abs(h.Tile.Y - hand.Y) <= 1)
                    .OrderBy(h => Math.Abs(h.Tile.X - hand.X) + Math.Abs(h.Tile.Y - hand.Y))
                    .Select(h => (int?)h.Id)
                    .FirstOrDefault();
                if (_pickedUpStaff is not null)
                {
                    _sounds.Play("pickup.wav"); // the pickup action
                    ShowNotice(UiText.Get("openwards.status.picked_up", "Picked up: click to put them down, right click lets them go"));
                }
            }
        }

        // Until patients can be clicked, Q sends the player's patient nearest the cursor home (within a tile), and
        // Shift+Q guesses their cure, as the patient window's buttons do.
        if (pressed(Keys.Q) && _hoveredTile is { } spot)
        {
            var chosen = simulation.Entities.OfType<Humanoid>()
                .Where(h => h.Behaviour is PatientBehaviour && h.HospitalPlayer == _session.Player && !h.IsDespawning)
                .Where(h => Math.Abs(h.Tile.X - spot.X) <= 1 && Math.Abs(h.Tile.Y - spot.Y) <= 1)
                .OrderBy(h => Math.Abs(h.Tile.X - spot.X) + Math.Abs(h.Tile.Y - spot.Y))
                .FirstOrDefault();
            if (chosen is not null)
            {
                _session.Submit(shift ? new GuessPatientCureCommand(chosen.Id) : new SendPatientHomeCommand(chosen.Id));
                if (!shift)
                {
                    _sounds.PlayAt("sack.wav", ScreenOf(chosen.Tile), ViewSize); // UIPatient:goHome
                }
            }
        }

        // Until faxes exist, V invites the VIP who asks to visit and Shift+V refuses them.
        if (pressed(Keys.V) && simulation.Hospitals[_session.Player].Vips.IsInvitationPending)
        {
            _session.Submit(new AnswerVipCommand(!shift));
        }

        UpdateWindowKeys(keys, pressed);

        // Until the town map exists, T and Shift+T turn the heating up and down.

        if (pressed(Keys.T))
        {
            var hospital = simulation.Hospitals[_session.Player];
            if (hospital.IsHeatingBroken)
            {
                ShowNotice(UiText.Get("openwards.status.boiler_broken", "The boiler is broken: the heating cannot be set until it is repaired"));
            }
            else
            {
                var heat = Math.Clamp(hospital.RadiatorHeat + (shift ? -1 : 1), Hospital.MinRadiatorHeat, Hospital.MaxRadiatorHeat);
                _session.Submit(new SetRadiatorHeatCommand(heat));
                ShowNotice(UiText.Format("openwards.status.heating", "Heating: %1% %", heat * 10));
            }
        }

        // Until the town map exists, N buys the parcel under the cursor.
        if (pressed(Keys.N) && _hoveredTile is { } landAt)
        {
            var parcel = simulation.Map[landAt.X, landAt.Y].Parcel;
            var price = simulation.ParcelPrice(parcel);
            // UITownMap's purchase: the till rings, or the wrong-choice buzz.
            if (!simulation.IsParcelPurchasable(parcel, _session.Player))
            {
                ShowNotice(UiText.Format("openwards.status.parcel_not_for_sale", "Parcel %1% is not for sale", parcel));
                _sounds.Play("Wrong2.wav");
            }
            else if (price > simulation.Hospitals[_session.Player].Balance)
            {
                ShowNotice(UiText.Format("openwards.town_map.cannot_afford", "Not enough money for parcel %1%: it costs %2%", parcel, MoneyText.Format(price)));
                _sounds.Play("Wrong2.wav");
            }
            else
            {
                _session.Submit(new PurchaseParcelCommand(parcel));
                _sounds.Play("cashreg.wav");
            }
        }

        // The room list (B): a click on a room starts it; a click elsewhere, B or Esc closes the list.
        if (_roomList)
        {
            if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
            {
                var chosen = _roomListRows.FirstOrDefault(row => row.Row.Contains(mouse.X, mouse.Y));
                _roomList = false;
                if (chosen.Room is { } picked)
                {
                    TryStartRoom(simulation, picked);
                }

                return;
            }

            if (HotkeyInput.Pressed(_hotkeys["ingame_panel_buildRoom"], keys, pressed))
            {
                _roomList = false;
                return;
            }
        }
        else if (HotkeyInput.Pressed(_hotkeys["ingame_panel_buildRoom"], keys, pressed) && !_buildTool.IsActive && !_furnishTool.IsActive)
        {
            ToggleRoomList();
            return;
        }

        var back = _buildBack;
        _buildBack = false;
        if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
        {
            _rightPressedAt = new Point(mouse.X, mouse.Y);
        }

        if (pressed(Keys.F) && !_buildTool.IsActive && !_furnishTool.IsActive
            && simulation.Content.FindObject(ReceptionDesks.ObjectId) is { } receptionDesk)
        {
            _furnishTool.Start(receptionDesk);
        }
        else if (pressed(Keys.Tab) && _furnishTool.IsActive)
        {
            // Until the furnish corridor window exists, Tab (Shift+Tab) goes through the corridor objects.
            _furnishTool.Cycle(simulation.Content, backwards: shift);
        }

        if (UpdateHiring(simulation, mouse) || UpdateCarrying(simulation, mouse))
        {
            return;
        }

        if (_buildTool.IsFurnishing && !_windows.Windows.Any(window => window is RoomEditWindow))
        {
            _windows.Open(new RoomEditWindow(_buildTool, button => _buildButton = button));
        }

        var rightClickedMap = !_buildTool.IsActive && !_furnishTool.IsActive
            && mouse.RightButton == ButtonState.Released && _previousMouse.RightButton == ButtonState.Pressed
            && Math.Abs(mouse.X - _rightPressedAt.X) + Math.Abs(mouse.Y - _rightPressedAt.Y) < 6;

        // Staff:onClick (CorsixTH/Lua/entities/humanoids/staff.lua): a right click (not a drag, which pans) on one of our
        // members of staff picks them up (setPickup) without opening their window. UI:ableToClickEntity: not while the
        // cover-up's vaccination mode is on, and not while a room to edit is being chosen.
        if (rightClickedMap && !_vaccinationMode && !_pickingRoomToEdit
            && _hoveredTile is { } staffUnder && HumanoidUnderCursor(simulation, new TilePosition(staffUnder.X, staffUnder.Y)) is { } staffHere
            && staffHere.Profile is not null && staffHere.Employer == _session.Player && !staffHere.IsFired && !staffHere.IsDespawning)
        {
            ((IWindowHost)this).PickUpStaff(staffHere.Id);
            return;
        }

        // Object:onClick: a right click (not a drag, which pans) on a corridor object of ours picks it up.
        if (rightClickedMap
            && _hoveredTile is { } under && GrabbableObjectAt(simulation, new TilePosition(under.X, under.Y)) is { } grabbed
            && simulation.Content.FindObject(grabbed.ObjectType) is { } grabbedDefinition)
        {
            _session.Submit(new PickUpCorridorObjectCommand(grabbed.Id));
            _furnishTool.StartMove(grabbedDefinition, grabbed);
            return;
        }

        // An object carried with nothing on the cursor (a game loaded meanwhile, or a put down refused): back on the cursor.
        if (!_buildTool.IsActive && !_furnishTool.IsActive && _session.Player < simulation.Hospitals.Count
            && simulation.Hospitals[_session.Player].Carried is { } carriedObject
            && simulation.Content.FindObject(carriedObject.ObjectType) is { } carriedDefinition)
        {
            _furnishTool.StartCarrying(carriedDefinition, carriedObject.Orientation);
        }

        if (_buildTool.IsActive || _furnishTool.IsActive)
        {
            // While building, the mouse belongs to the build tool; a right click (not a drag, which pans) turns objects.
            var leftDown = mouse.LeftButton == ButtonState.Pressed;
            var leftWasDown = _previousMouse.LeftButton == ButtonState.Pressed;
            var rightReleased = mouse.RightButton == ButtonState.Released && _previousMouse.RightButton == ButtonState.Pressed;
            var rightClicked = rightReleased && Math.Abs(mouse.X - _rightPressedAt.X) + Math.Abs(mouse.Y - _rightPressedAt.Y) < 6;
            var input = new BuildInput(
                _hoveredTile is { } point ? new TilePosition(point.X, point.Y) : null,
                _hoveredFraction.X,
                _hoveredFraction.Y,
                leftDown,
                leftDown && !leftWasDown,
                !leftDown && leftWasDown,
                rightClicked,
                pressed(Keys.Enter) || TakeButton(BuildButton.Confirm),
                back || TakeButton(BuildButton.Back),
                pressed(Keys.P) || TakeButton(BuildButton.PickUp),
                IsPlacingObject && HotkeyInput.Pressed(_hotkeys["ingame_rotateobject"], keys, pressed),
                _furnishTool.IsActive && !_buildTool.IsActive && HotkeyInput.Pressed(_hotkeys["ingame_sellPickedUpItem"], keys, pressed));
            var tool = _buildTool.IsActive ? _buildTool.Update(simulation, _session.Player, input) : _furnishTool.Update(simulation, _session.Player, input);
            if (tool is { } command)
            {
                _session.Submit(command);
            }

            if (wasBuilding && (pressed(Keys.B) || pressed(Keys.H) || pressed(Keys.G)))
            {
                _buildReminderUntil = Environment.TickCount64 + NoticeMilliseconds;
            }

            return;
        }

        if (pressed(Keys.H) && !keys.IsKeyDown(Keys.LeftControl) && !keys.IsKeyDown(Keys.RightControl) && _hoveredTile is { } spawnAt)
        {
            _session.Submit(new SpawnHumanoidCommand(new TilePosition(spawnAt.X, spawnAt.Y)));
        }

        // Debug hiring until the hire window exists: Ctrl+1 to Ctrl+4 hire the first doctor, nurse,
        // handyman or receptionist on the list and place them on the tile under the cursor. The plain
        // number keys set the game speed, as in CorsixTH.
        var control = keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);

        // Ctrl+C over a finished room of the hospital puts a copy of it on the cursor (our addition, BuildRoomTool.StartCopy).
        if (control && pressed(Keys.C) && !_buildTool.IsActive && !_furnishTool.IsActive && _hoveredTile is { } copyAt
            && simulation.FindEntity(simulation.Map[copyAt.X, copyAt.Y].RoomId) is Room { IsActive: true } copied
            && RoomBuilder.IsOwnedBy(simulation.Map, copied, _session.Player))
        {
            _windows.CloseAll();
            _buildTool.StartCopy(simulation, copied);
        }

        (Keys Key, StaffClass Class)[] hireKeys =
        [
            (Keys.D1, StaffClass.Doctor), (Keys.D2, StaffClass.Nurse), (Keys.D3, StaffClass.Handyman), (Keys.D4, StaffClass.Receptionist),
        ];
        foreach (var (key, staffClass) in hireKeys)
        {
            if (control && pressed(key) && _hoveredTile is { } hireAt)
            {
                var tile = new TilePosition(hireAt.X, hireAt.Y);
                var problem = StaffHiring.Check(simulation, staffClass, 0, tile, _session.Player);
                if (problem == StaffHireProblem.None)
                {
                    _session.Submit(new HireStaffCommand(staffClass, 0, tile));
                    _sounds.Play("plac_st2.wav"); // UIPlaceStaff:close
                }
                else
                {
                    ShowNotice(UiText.Format("openwards.status.cannot_hire", "Cannot hire a %1%: %2%", staffClass, problem));
                }
            }
        }

        if (pressed(Keys.G) && _hospitalTiles.Count > 0)
        {
            for (var i = 0; i < 10; i++)
            {
                _session.Submit(new SpawnHumanoidCommand(_hospitalTiles[_random.Next(_hospitalTiles.Count)]));
            }
        }

        var clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        if (_pickingRoomToEdit)
        {
            PickRoomToEdit(simulation, clicked, mouse);
            return;
        }

        if (_vaccinationMode && CoverUpRunning() is null)
        {
            // turnOffVaccinationMode: the cover-up is over.
            _vaccinationMode = false;
        }

        if (_vaccinationMode)
        {
            // GameUI:onMouseUp in vaccination mode: a left click only picks infected patients, a right click ends the mode.
            if (clicked && _hoveredTile is { } pick && HumanoidUnderCursor(simulation, new TilePosition(pick.X, pick.Y)) is { } patientHere && IsVaccinationTarget(patientHere))
            {
                _session.Submit(new VaccinateCommand(patientHere.Id));
                _sounds.Play("vaccin.wav");
            }

            if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
            {
                _vaccinationMode = false;
            }

            return;
        }

        if (clicked && _hoveredTile is { } aim && RatNear(simulation, _session.Player, aim) is { } shot)
        {
            // Rat:onClick: a click on one of the player's rats (within a tile of the cursor) shoots it.
            _session.Submit(new ShootRatCommand(shot.Id));
            _sounds.Play("shotgun.wav");
            _sounds.Play("deadrat2.wav");
        }
        else if (clicked && _hoveredTile is { } target)
        {
            var tile = new TilePosition(target.X, target.Y);
            var here = HumanoidUnderCursor(simulation, tile);
            var desk = DeskUnderCursor(simulation, tile);
            if (here is not null)
            {
                _selected = here.Id;
                if (here.Profile is not null && here.Employer == _session.Player && !here.IsFired)
                {
                    // A click on a member of staff opens their window (CorsixTH's UIStaff), on a patient theirs (UIPatient).
                    _windows.Open(new StaffWindow(here.Id));
                }
                else if (here.Profile is not null && here.Employer >= 0 && here.Employer != _session.Player && !here.IsFired && _session.Player < simulation.Hospitals.Count)
                {
                    // The network game's poaching: a click on another hospital's member of staff asks them to work for us.
                    _session.Submit(new PoachStaffCommand(here.Id));
                    ShowNotice(UiText.Format("openwards.poach.asked", "You ask %1% whether they would rather work for you", StaffLabels.Name(this, here)));
                }
                else if (here.Behaviour is PatientBehaviour && here.HospitalPlayer == _session.Player)
                {
                    // Patient:onClick: an infected patient during the cover-up is picked for vaccination (vaccin.wav).
                    if (IsVaccinationTarget(here))
                    {
                        _session.Submit(new VaccinateCommand(here.Id));
                        _sounds.Play("vaccin.wav");
                    }
                    else
                    {
                        _windows.Open(new PatientWindow(here.Id));
                    }
                }
            }
            else if (MachineUnderCursor(simulation, tile) is { } machine && simulation.FindEntity(machine.RoomId) is Room machineRoom && RoomBuilder.IsOwnedBy(simulation.Map, machineRoom, _session.Player))
            {
                // A click on a machine opens its window (CorsixTH's UIMachine).
                _selected = null;
                _windows.Open(new MachineWindow(machine.Id));
            }
            else if ((desk?.Id ?? QueueRoomOf(DoorRoomUnderCursor(simulation)) ?? QueueRoomAt(simulation, tile)?.Id) is { } queueOwner)
            {
                // A click on a reception desk (ReceptionDesk:onClick) or on a room's door (Door:onClick) opens its queue
                // (CorsixTH's UIQueue); a click elsewhere in the room, on its floor or an empty chair, opens nothing.
                _selected = null;
                _windows.Open(new QueueWindow(queueOwner));
            }
            else if (_selected is { } id)
            {
                _session.Submit(new WalkToCommand(id, tile));
            }
        }
    }

    /// <summary>Whether Shift+X has made the walls transparent (GameUI's toggled_transparency).</summary>
    private bool _wallsTransparentToggled;

    /// <summary>The player's rat nearest the tile under the cursor, if it is within a tile.</summary>
    private Rat? RatNear(Simulation simulation, int player, Point aim) =>
        simulation.Entities.OfType<Rat>()
            .Where(rat => !rat.IsGone && rat.Player == player)
            .Select(rat => (rat, RatDrawing(rat).Tile))
            .Where(entry => Math.Abs(entry.Tile.X - aim.X) <= 1 && Math.Abs(entry.Tile.Y - aim.Y) <= 1)
            .OrderBy(entry => Math.Abs(entry.Tile.X - aim.X) + Math.Abs(entry.Tile.Y - aim.Y))
            .Select(entry => entry.rat)
            .FirstOrDefault();

    /// <summary>The humanoid under the cursor (picked as a click picks one), for its hoverable mood.</summary>
    private int _hoveredHumanoid;

    /// <summary>The world pixel under the cursor, for picking by the sprites drawn there.</summary>
    private Point? _hoveredWorld;

    /// <summary>The placed object whose sprite is under the cursor (GameUI's hitTestObjects), or null.</summary>
    private RoomObject? ObjectUnderCursor(Simulation simulation) =>
        _hoveredWorld is { } point && _objects.ObjectAt(point) is { } id ? simulation.FindEntity(id) as RoomObject : null;

    /// <summary>The machine under the cursor: by its sprite, else on the tile under it.</summary>
    private RoomObject? MachineUnderCursor(Simulation simulation, TilePosition tile) =>
        ObjectUnderCursor(simulation) is { Machine: not null } machine ? machine : MachineWindow.At(simulation, tile);

    /// <summary>The reception desk under the cursor: by its sprite, else on the tile under it.</summary>
    private RoomObject? DeskUnderCursor(Simulation simulation, TilePosition tile) =>
        ObjectUnderCursor(simulation) is { Desk: not null } desk ? desk
        : simulation.Entities.OfType<RoomObject>().FirstOrDefault(o => o.Desk is not null && o.Tile == tile);

    /// <summary>
    /// The humanoid under the cursor: by the pixels of the sprites last drawn (GameUI's hitTestObjects), else one
    /// standing on the tile under it.
    /// </summary>
    private Humanoid? HumanoidUnderCursor(Simulation simulation, TilePosition tile) =>
        (_hoveredWorld is { } point && _humanoids.HumanoidAt(simulation, point) is { } id ? simulation.FindEntity(id) as Humanoid : null)
        ?? simulation.Entities.OfType<Humanoid>().FirstOrDefault(h => h.Tile == tile || h.StepTarget == tile);

    /// <summary>Door:onClick: the room whose door's sprite was clicked, if it is finished.</summary>
    internal static int? QueueRoomOf(Room? room) => room is { IsActive: true } ? room.Id : null;

    /// <summary>
    /// Door:onClick when the cursor is not on the door's sprite: the finished room whose door is on the tile, or on the
    /// tile across the wall from it; null anywhere else, inside rooms too.
    /// </summary>
    internal static Room? QueueRoomAt(Simulation simulation, TilePosition tile) =>
        simulation.Entities.OfType<RoomDoor>()
            .Where(door => IsDoorTile(door, tile))
            .Select(door => simulation.FindEntity(door.RoomId) as Room)
            .FirstOrDefault(room => room is { IsActive: true });

    /// <summary>Whether the tile is the door's own tile or the one across the wall from it (both swing doors' too).</summary>
    private static bool IsDoorTile(RoomDoor door, TilePosition tile)
    {
        static TilePosition Across(RoomDoor door, TilePosition side) =>
            door.Edge == EdgeOrientation.North ? side with { Y = side.Y - 1 } : side with { X = side.X - 1 };

        return tile == door.Tile || tile == Across(door, door.Tile)
            || (door.IsSwing && (tile == door.SlaveTile || tile == Across(door, door.SlaveTile)));
    }

    /// <summary>The room under the cursor or of the door under it, whose queueing patients show the queue mood.</summary>
    private int _hoveredRoom;

    /// <summary>
    /// GameUI:onCursorWorldPositionChange's queueing icons and hoverable moods: HLIGHTP2.wav when the cursor comes onto a
    /// room with a queue, or onto someone too cold or too hot.
    /// </summary>
    private void UpdateHover()
    {
        if (Simulation is not { } simulation || _hoveredTile is not { } hovered)
        {
            _hoveredHumanoid = 0;
            _hoveredRoom = 0;
            return;
        }

        var tile = new TilePosition(hovered.X, hovered.Y);
        var roomId = simulation.Map[tile.X, tile.Y].RoomId;
        if (roomId == 0)
        {
            // Over a door from the corridor: its room, picked by the door's sprite as GameUI's hitTestObjects does (the
            // door drawn in front), else by the tiles on either side of it.
            roomId = DoorRoomUnderCursor(simulation)?.Id
                ?? simulation.Entities.OfType<RoomDoor>()
                    .FirstOrDefault(door => door.Tile == tile || (door.Edge == EdgeOrientation.North ? door.Tile with { Y = door.Tile.Y - 1 } : door.Tile with { X = door.Tile.X - 1 }) == tile)?.RoomId ?? 0;
        }

        if (roomId != _hoveredRoom)
        {
            _hoveredRoom = roomId;
            if (simulation.FindEntity(roomId) is Room { DoorQueue.Count: > 0 })
            {
                _sounds.Play("HLIGHTP2.wav");
            }
        }

        var humanoid = HumanoidUnderCursor(simulation, tile);
        var id = humanoid?.Id ?? 0;
        if (id != _hoveredHumanoid)
        {
            _hoveredHumanoid = id;
            if (humanoid is not null && HumanoidMoods.HoverIcon(humanoid) is not null)
            {
                _sounds.Play("HLIGHTP2.wav");
            }
        }
    }

    private void CollectEntitiesByTile()
    {
        foreach (var list in _entitiesByTile.Values)
        {
            list.Clear();
        }

        foreach (var list in _earlyByTile.Values)
        {
            list.Clear();
        }

        _earlyObjectTiles.Clear();

        foreach (var list in _slavePartsByTile.Values)
        {
            list.Clear();
        }

        foreach (var list in _splitPartsByTile.Values)
        {
            list.Clear();
        }

        if (Simulation is not { } simulation)
        {
            return;
        }

        // An object in use is drawn as part of its user's animation (CorsixTH's makeInvisible).
        _hiddenObjects.Clear();
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { HiddenObjectId: not 0 and var hidden })
            {
                _hiddenObjects.Add(hidden);
            }

            if (entity is RoomObject early && IsEarlyObject(simulation, early))
            {
                _earlyObjectTiles.Add(early.Tile);
            }
        }

        foreach (var entity in simulation.Entities)
        {
            // A room door is hidden too while someone goes through: their door animation shows it.
            if (entity is RoomObject or RoomDoor && _hiddenObjects.Contains(entity.Id))
            {
                continue;
            }

            // Someone shown by another's animation, such as a patient at the pharmacy cabinet.
            // The pickup action: one being carried is drawn on the cursor instead.
            if (entity is Humanoid { IsHidden: true } || (entity is Humanoid && entity.Id == _pickedUpStaff)
                || (entity is RoomObject && entity.Id == _furnishTool.MovingId))
            {
                continue;
            }

            TilePosition? tile = entity switch
            {
                Humanoid humanoid => HumanoidDrawing.DrawTile(humanoid),
                EntranceDoor door => door.Tile,
                RoomDoor door => door.Tile,
                RoomObject placed => ObjectDrawing.DrawTile(simulation.Content, placed.ObjectType, placed.Tile, placed.Orientation),
                Litter { IsRemoved: false } litter => litter.Tile,
                Rat { IsGone: false } rat => RatDrawing(rat).Tile,
                _ => null,
            };
            if (tile is not { } position)
            {
                continue;
            }

            if (SplitParts(simulation, entity) is { } parts)
            {
                foreach (var (partTile, left, right) in parts)
                {
                    if (!_splitPartsByTile.TryGetValue(partTile, out var partList))
                    {
                        partList = [];
                        _splitPartsByTile[partTile] = partList;
                    }

                    partList.Add((entity, left, right));
                }

                continue;
            }

            // DrawFlags.EarlyList: an object of the early list, and whoever uses it (use_object.lua), with the north wall;
            // so is a door in a north wall (Door:setAnimation), both halves of a swing door included.
            if ((entity is RoomObject earlyObject && IsEarlyObject(simulation, earlyObject))
                || (entity is Humanoid { ActionAnimation: not null } user && _earlyObjectTiles.Contains(user.ActionAnimationTile))
                || entity is RoomDoor { Edge: EdgeOrientation.North })
            {
                AddEarly(position, entity);
                if (entity is RoomDoor { IsSwing: true } earlySwing)
                {
                    AddEarly(earlySwing.SlaveTile, entity);
                }

                continue;
            }

            if (!_entitiesByTile.TryGetValue(position, out var list))
            {
                list = [];
                _entitiesByTile[position] = list;
            }

            if (entity is RoomDoor { IsSwing: true } swingDoor)
            {
                // The left swing door is drawn in its own tile's pass.
                if (!_entitiesByTile.TryGetValue(swingDoor.SlaveTile, out var slaveList))
                {
                    slaveList = [];
                    _entitiesByTile[swingDoor.SlaveTile] = slaveList;
                }

                slaveList.Add(swingDoor);
            }

            if (entity is RoomObject master && ObjectDrawing.SlaveDrawTile(master.ObjectType, master.Tile, master.Orientation) is { } slaveTile)
            {
                if (!_slavePartsByTile.TryGetValue(slaveTile, out var slaves))
                {
                    slaves = [];
                    _slavePartsByTile[slaveTile] = slaves;
                }

                slaves.Add(master);
            }

            // The gates to hell the grim reaper opened are drawn in their own tile's pass, under the patient falling in.
            if (entity is Humanoid { Behaviour: GrimReaperBehaviour grim })
            {
                if (!_entitiesByTile.TryGetValue(grim.HoleTile, out var holeList))
                {
                    holeList = [];
                    _entitiesByTile[grim.HoleTile] = holeList;
                }

                holeList.Insert(0, entity);
            }

            // Litter lies on the floor, under whatever else is on its tile (CorsixTH's DrawingLayers.Litter).
            if (entity is Litter)
            {
                list.Insert(0, entity);
            }
            else
            {
                list.Add(entity);
            }
        }

        // A receptionist stands on the desk's own tile. Facing north or west (away from the viewer) they are
        // drawn after the desk, otherwise behind it (CorsixTH's action_staff_reception_idle_phase).
        foreach (var entity in simulation.Entities)
        {
            if (entity is Humanoid { Behaviour: ReceptionistBehaviour { Phase: ReceptionistPhase.Staffing } staffing } receptionist
                && simulation.FindEntity(staffing.DeskId) is RoomObject desk
                && _entitiesByTile.TryGetValue(receptionist.Tile, out var list) && list.Remove(desk))
            {
                var index = list.IndexOf(receptionist);
                list.Insert(receptionist.Facing is Direction.North or Direction.West ? index : index + 1, desk);
            }
        }
    }

    /// <summary>
    /// The parts of an object drawn split over several tiles (<see cref="ObjectDrawing.Split"/>), or of the animation of
    /// someone using it from its tile, which shows the object; null for anything drawn whole.
    /// </summary>
    private static IEnumerable<(TilePosition Tile, int Left, int Right)>? SplitParts(Simulation simulation, Entity entity) => entity switch
    {
        RoomObject placed when simulation.FindEntity(placed.RoomId) is not Room { IsCrashed: true } =>
            ObjectDrawing.Split(placed.ObjectType, placed.Tile, placed.Orientation),
        Humanoid { ActionAnimation: not null, HiddenObjectId: not 0 } humanoid
            when simulation.FindEntity(humanoid.HiddenObjectId) is RoomObject used && humanoid.ActionAnimationTile == used.Tile =>
            ObjectDrawing.Split(used.ObjectType, used.Tile, used.Orientation),
        _ => null,
    };

    /// <summary>The first patient of the player's hospital waiting for an answer to their fax, in entity order.</summary>
    private static Humanoid? FirstQuestion(Simulation simulation, int player) =>
        simulation.Entities.OfType<Humanoid>().FirstOrDefault(humanoid => humanoid is { Behaviour: PatientBehaviour { Question: not PatientQuestion.None }, IsDespawning: false } && humanoid.HospitalPlayer == player);

    /// <summary>
    /// The emergency helicopter (CorsixTH's objects/helicopter.lua, animation 2456) on the tile south of the heliport:
    /// hidden on its way, then 600 pixels up coming down 10 a tick, landed, and going up again.
    /// </summary>
    /// <summary>
    /// Where and how a rat is drawn (CorsixTH's <c>Rat:_walkToNextTile</c>): running east or south it is drawn from the
    /// tile it runs to, west or north from the one it runs from, slid 32 by 16 pixels a tile; coming out of or going into
    /// a hole it plays that animation at the hole.
    /// </summary>
    private static (TilePosition Tile, int Animation, int OffsetX, int OffsetY) RatDrawing(Rat rat)
    {
        switch (rat.Phase)
        {
            case RatPhase.Emerging:
                return (rat.Origin.Tile, RatLeaveHole, 0, 0);
            case RatPhase.Entering:
                return (rat.Target.Tile, rat.Target.Wall == Direction.West ? RatEnterWestHole : RatEnterNorthHole, 0, 0);
        }

        var from = rat.From;
        var to = rat.To;
        var done = Rat.TicksPerTile - Math.Clamp(rat.Timer, 0, Rat.TicksPerTile);
        var slide32 = 32 * done / Rat.TicksPerTile;
        var slide16 = 16 * done / Rat.TicksPerTile;
        if (to.X > from.X)
        {
            return (to, RatWalkEast, -32 + slide32, -16 + slide16);
        }

        if (to.X < from.X)
        {
            return (from, RatWalkWest, -slide32, -slide16);
        }

        if (to.Y > from.Y)
        {
            return (to, RatWalkSouth, 32 - slide32, -16 + slide16);
        }

        return (from, RatWalkNorth, slide32, -slide16);
    }

    private void DrawHelicopter(SpriteBatch batch, Simulation simulation, TilePosition tile, int anchorX, int anchorY)
    {
        if (_session is not { } session || simulation.Hospitals[session.Player].Emergencies.HelicopterPhase is not { } phase || phase < 0)
        {
            return;
        }

        var heliport = simulation.Map.GetHeliportTile(session.Player);
        if (tile != heliport with { Y = heliport.Y + 1 })
        {
            return;
        }

        var height = phase switch
        {
            < 60 => 600 - (10 * phase),
            < 87 => 0,
            _ => 10 * (phase - 87),
        };
        var set = _animations.Animations;
        var first = set.FirstFrame(HelicopterAnimation);
        var frame = set.Advance(first, (int)(simulation.Tick % set.Length(first)));
        _animations.DrawFrame(batch, frame, anchorX, anchorY - height, NoLayers, flipHorizontally: false);
    }

    private void DrawEntities(SpriteBatch batch, TilePosition tile, int anchorX, int anchorY)
    {
        if (Simulation is { } helicopterSimulation)
        {
            DrawHelicopter(batch, helicopterSimulation, tile, anchorX, anchorY);
        }

        if (Simulation is { } holeSimulation)
        {
            // Rat holes, in north and west walls only; a west one is the north one mirrored.
            foreach (var hospital in holeSimulation.Hospitals)
            {
                foreach (var hole in hospital.Ratholes)
                {
                    if (hole.IsVisible && hole.Tile == tile)
                    {
                        _animations.DrawFrame(batch, _animations.Animations.FirstFrame(RatholeAnimation), anchorX, anchorY, NoLayers, flipHorizontally: hole.Wall == Direction.West);
                    }
                }
            }
        }

        if (Simulation is not { } simulation)
        {
            return;
        }

        if (!_entitiesByTile.TryGetValue(tile, out var list))
        {
            DrawSplitParts(batch, simulation, tile, anchorX, anchorY);
            DrawSlaveParts(batch, tile);
            DrawFloatingDollars(batch, simulation, tile, anchorX, anchorY);
            return;
        }

        // Litter lies under everything, split objects included.
        var splitDrawn = false;
        foreach (var entity in list)
        {
            if (!splitDrawn && entity is not Litter)
            {
                DrawSplitParts(batch, simulation, tile, anchorX, anchorY);
                splitDrawn = true;
            }

            DrawEntity(batch, simulation, entity, tile, anchorX, anchorY);
        }

        if (!splitDrawn)
        {
            DrawSplitParts(batch, simulation, tile, anchorX, anchorY);
        }

        DrawSlaveParts(batch, tile);
        DrawFloatingDollars(batch, simulation, tile, anchorX, anchorY);
    }

    private void AddEarly(TilePosition tile, Entity entity)
    {
        if (!_earlyByTile.TryGetValue(tile, out var list))
        {
            list = [];
            _earlyByTile[tile] = list;
        }

        list.Add(entity);
    }

    /// <summary>Whether a placed object facing its way is of the early list (<c>early_list</c>).</summary>
    private static bool IsEarlyObject(Simulation simulation, RoomObject placed) =>
        simulation.Content.FindObject(placed.ObjectType)?.Orientations.GetValueOrDefault(placed.Orientation) is { EarlyList: true };

    /// <summary>The early entities of a tile, drawn after its north wall (level_map::draw's oEarlyEntities).</summary>
    private void DrawEarlyEntities(SpriteBatch batch, TilePosition tile, int anchorX, int anchorY)
    {
        if (Simulation is not { } simulation || !_earlyByTile.TryGetValue(tile, out var list))
        {
            return;
        }

        foreach (var entity in list)
        {
            DrawEntity(batch, simulation, entity, tile, anchorX, anchorY);
        }
    }

    /// <summary>The parts of split objects, and of their users' animations, that <paramref name="tile"/> draws, each cropped to its column.</summary>
    private void DrawSplitParts(SpriteBatch batch, Simulation simulation, TilePosition tile, int anchorX, int anchorY)
    {
        if (!_splitPartsByTile.TryGetValue(tile, out var parts))
        {
            return;
        }

        foreach (var (entity, left, right) in parts)
        {
            _animations.SetColumnClip(left, right);
            DrawEntity(batch, simulation, entity, tile, anchorX, anchorY);
            _animations.ClearColumnClip();
        }
    }

    private void DrawEntity(SpriteBatch batch, Simulation simulation, Entity entity, TilePosition tile, int anchorX, int anchorY)
    {
        switch (entity)
        {
            case EntranceDoor door:
                _doors.Draw(batch, door, anchorX, anchorY, simulation.Tick);
                break;
            case Humanoid { Behaviour: GrimReaperBehaviour grim } when tile == grim.HoleTile:
                // gates_to_hell.lua: open (1602) until the grim reaper has gone, then closing (2552).
                var hole = grim.Phase == GrimReaperPhase.Closing ? GrimReaperBehaviour.HoleClosing : GrimReaperBehaviour.HoleIdle;
                var holeFirst = _animations.Animations.FirstFrame(hole.Id);
                var holeTicks = simulation.Tick - (grim.Phase == GrimReaperPhase.Closing ? grim.PhaseStart : 0);
                _animations.DrawFrame(batch, _animations.Animations.Advance(holeFirst, (int)(holeTicks % hole.Length)), anchorX, anchorY, NoLayers, flipHorizontally: false);
                break;
            case Humanoid { Behaviour: GrimReaperBehaviour { Phase: GrimReaperPhase.Closing } }:
                break;
            case Humanoid humanoid:
                _humanoids.Draw(batch, humanoid, anchorX, anchorY, simulation.Tick, HumanoidMoods.Icon(simulation, humanoid, humanoid.Id == _hoveredHumanoid, _hoveredRoom));
                break;
            case RoomObject placed:
                var tint = placed.Id == _buildTool.HighlightedObject ? PickUpTint : Color.White;
                var machine = placed.Machine is null ? null : simulation.Content.FindObject(placed.ObjectType)?.Machine;
                if (machine is not null && simulation.FindEntity(placed.RoomId) is Room { IsCrashed: true })
                {
                    _objects.DrawCrashed(batch, machine.Crashed.Id, placed.Tile, tint);
                    break;
                }

                if (placed.ObjectType == "surgeon_screen")
                {
                    // use_screen.lua's surgical_state: the outfits hanging on the screen, green scrubs and white coats.
                    var whites = simulation.Entities.Count(e => e is Humanoid { Type: HumanoidType.Surgeon } surgeon
                        && simulation.Map[surgeon.Tile.X, surgeon.Tile.Y].RoomId == placed.RoomId);
                    var screenAnimation = whites < 2 ? (whites > 0 ? SurgeonScreenBoth : SurgeonScreenGreens) : SurgeonScreenWhites;
                    _animations.DrawFrame(batch, _animations.Animations.FirstFrame(screenAnimation), 32 * (placed.Tile.X - placed.Tile.Y), 16 * (placed.Tile.X + placed.Tile.Y), NoLayers, flipHorizontally: false, tint);
                    break;
                }

                // OperatingTheatreRoom:setXRayOn: the viewer lights up (layer 11 set to 2) while a patient is in the theatre.
                var lit = placed.ObjectType == "x_ray_viewer" && simulation.FindEntity(placed.RoomId) is Room { PatientId: not 0 };
                _objects.Draw(batch, placed.ObjectType, placed.Tile, placed.Orientation, simulation.Tick, tint, placed.HangingClothes, placed.Plant?.State, lit ? XRayViewerLit : null, placed.Id);
                if (machine is not null)
                {
                    _objects.DrawSmoke(batch, machine.Smoke.Id, placed.Tile, placed.Orientation, placed.Machine!.RemainingUses, simulation.Tick);
                    if (placed.Machine.IsRepairCalled)
                    {
                        // Machine:setRepairingStatus: the gear icon (4564) turns above the machine (setMoodInfo, 96 pixels up)
                        // until the repair is done.
                        var gearFirst = _animations.Animations.FirstFrame(RepairGearIcon);
                        var gearFrame = _animations.Animations.Advance(gearFirst, (int)(simulation.Tick % _animations.Animations.Length(gearFirst)));
                        _animations.DrawFrame(batch, gearFrame, (32 * (placed.Tile.X - placed.Tile.Y)) - 1, (16 * (placed.Tile.X + placed.Tile.Y)) - 96, NoLayers, flipHorizontally: false);
                    }
                }

                break;
            case Rat rat:
                var (_, ratAnimation, offsetX, offsetY) = RatDrawing(rat);
                var ratFirst = _animations.Animations.FirstFrame(ratAnimation);
                var ratFrame = _animations.Animations.Advance(ratFirst, (int)(simulation.Tick % _animations.Animations.Length(ratFirst)));
                _animations.DrawFrame(batch, ratFrame, anchorX + offsetX, anchorY + offsetY, NoLayers, flipHorizontally: false);
                break;
            case Litter litter:
                _animations.DrawFrame(batch, _animations.Animations.FirstFrame(litter.Animation), anchorX, anchorY, NoLayers, flipHorizontally: litter.IsMirrored);
                break;
            case RoomDoor { IsSwing: true } swingDoor:
                DrawSwingDoor(batch, simulation, swingDoor, tile, anchorX, anchorY);
                break;
            case RoomDoor roomDoor:
                var animation = roomDoor.Edge == EdgeOrientation.North ? RoomDoorNorth : RoomDoorWest;
                DrawDoorFrame(batch, roomDoor, _animations.Animations.FirstFrame(animation), anchorX, anchorY, flip: false);
                break;
        }
    }

    /// <summary>
    /// One of a pair of swing doors (objects/doors/swing_door_left.lua, swing_door_right.lua): standing, or swinging in or
    /// out, the left one from the start of the swing and the right one once the humanoid is through; mirrored in a west
    /// wall, where only the left door has its own standing animation.
    /// </summary>
    private void DrawSwingDoor(SpriteBatch batch, Simulation simulation, RoomDoor door, TilePosition tile, int anchorX, int anchorY)
    {
        var west = door.Edge == EdgeOrientation.West;
        var slave = tile == door.SlaveTile;
        var animation = slave ? (west ? SwingDoorLeftWest : SwingDoorLeftNorth) : SwingDoorRightNorth;
        var flip = west && !slave;
        if (simulation.Content.SwingDoor is { } swing && door.SwingStart >= 0)
        {
            var swingAnimation = slave
                ? (door.SwingIn ? swing.SlaveIn : swing.SlaveOut)
                : (door.SwingIn ? swing.MasterIn : swing.MasterOut);
            var elapsed = simulation.Tick - door.SwingStart - (slave ? 0 : door.SwingCrossTicks);
            if (elapsed >= 0 && elapsed < swingAnimation.Length)
            {
                var first = _animations.Animations.FirstFrame(swingAnimation.Id);
                DrawDoorFrame(batch, door, _animations.Animations.Advance(first, (int)elapsed), anchorX, anchorY, west);
                return;
            }
        }

        DrawDoorFrame(batch, door, _animations.Animations.FirstFrame(animation), anchorX, anchorY, flip);
    }

    /// <summary>Draws a frame of a room's door and remembers where, for clicks on it (GameUI's hitTestObjects).</summary>
    private void DrawDoorFrame(SpriteBatch batch, RoomDoor door, int frame, int anchorX, int anchorY, bool flip)
    {
        _animations.DrawFrame(batch, frame, anchorX, anchorY, NoLayers, flipHorizontally: flip);
        _drawnDoors.Add((door.RoomId, frame, anchorX, anchorY, flip));
    }

    /// <summary>The room doors' frames drawn this frame: the room, the frame, where and whether mirrored.</summary>
    private readonly List<(int RoomId, int Frame, int X, int Y, bool Flip)> _drawnDoors = [];

    /// <summary>The room whose door's sprite, as last drawn, is under the cursor (the one drawn last, in front), or null.</summary>
    private Room? DoorRoomUnderCursor(Simulation simulation)
    {
        if (_hoveredWorld is not { } point)
        {
            return null;
        }

        for (var i = _drawnDoors.Count - 1; i >= 0; i--)
        {
            var (roomId, frame, x, y, flip) = _drawnDoors[i];
            if (_animations.HitTest(frame, x, y, NoLayers, flip, point.X, point.Y))
            {
                return simulation.FindEntity(roomId) as Room;
            }
        }

        return null;
    }

    /// <summary>The second parts of objects drawn on <paramref name="tile"/>, such as the radiation shield's front.</summary>
    private void DrawSlaveParts(SpriteBatch batch, TilePosition tile)
    {
        if (!_slavePartsByTile.TryGetValue(tile, out var slaves))
        {
            return;
        }

        foreach (var master in slaves)
        {
            var tint = master.Id == _buildTool.HighlightedObject ? PickUpTint : Color.White;
            _objects.DrawSlave(batch, master.ObjectType, master.Tile, master.Orientation, tint);
        }
    }

    /// <summary>
    /// The local player's floating dollar signs on <paramref name="tile"/>, over everything else on it (CorsixTH's
    /// <c>DrawingLayers.FloatingDollars</c>).
    /// </summary>
    private void DrawFloatingDollars(SpriteBatch batch, Simulation simulation, TilePosition tile, int anchorX, int anchorY)
    {
        if (_session is not { } session)
        {
            return;
        }

        foreach (var sign in simulation.FloatingDollarSigns)
        {
            if (sign.Tile == tile && sign.Player == session.Player)
            {
                _floatingDollars.Draw(batch, sign, anchorX, anchorY, simulation.Tick, _config.EnableInsuranceBlueBanknote);
            }
        }
    }

    /// <summary>
    /// Where the adviser is: CorsixTH's bottom panel is 640 by 48 pixels, centred at the bottom of the screen, and the
    /// adviser's window sits at (378, -16) in it, his animation 200 pixels further right.
    /// </summary>
    private static Point AdviserOrigin(Viewport viewport) => new(((viewport.Width - 640) / 2) + 378, viewport.Height - 48 - 16);

    /// <summary>
    /// UIAdviser:draw: the adviser's animation, and while he talks the balloon (Panel02V's sprites 37 to 39, the middle
    /// one repeated to the balloon's width) with his speech in the black font.
    /// </summary>
    /// <summary>Subtitles:draw: each subtitle shown on a line of its own at the top left, 16 pixels apart.</summary>
    private void DrawSubtitles(SpriteBatch batch)
    {
        var lines = _subtitles.Shown;
        for (var i = 0; i < lines.Count; i++)
        {
            _text.Draw(batch, lines[i], new Vector2(5, StatusHeight + 5 + (i * 16)));
        }
    }

    private void DrawAdviser(SpriteBatch batch, Viewport viewport)
    {
        if (_adviserFigure.Animation is not { } animation)
        {
            return;
        }

        var origin = AdviserOrigin(viewport);
        var set = _animations.Animations;
        _animations.DrawFrame(batch, set.Advance(set.FirstFrame(animation), _adviserFigure.Frame), origin.X + 200, origin.Y, NoAdviserLayers, flipHorizontally: false);
        if (_adviserFigure.Speech is not { } speech || _panelSprites is null || _blackText is null)
        {
            return;
        }

        var width = AdviserFigure.BalloonWidth(speech);
        var left = BalloonLeft(origin, width);
        for (var dx = 0; dx <= width; dx += 16)
        {
            _panelSprites.Draw(batch, 38, origin.X + 139 - dx, origin.Y - 25, Color.White);
        }

        _panelSprites.Draw(batch, 37, left - 16, origin.Y - 25, Color.White);
        _panelSprites.Draw(batch, 39, origin.X + 155, origin.Y - 40, Color.White);
        var y = origin.Y - 20;
        foreach (var line in WrapToWidth(speech.TrimEnd('/'), width + 60, text => _blackText.Measure(text).Width))
        {
            _blackText.Draw(batch, line, new Vector2(left - 8, y), tint: _blackText.IsTrueTypeFor(line) ? Color.Black : null);
            y += _blackText.LineHeight;
        }
    }

    private static readonly byte[] NoAdviserLayers = new byte[Humanoid.LayerCount];

    /// <summary>The left edge of the balloon's repeated middle part, the last one drawn.</summary>
    private static int BalloonLeft(Point origin, int width) => origin.X + 139 - (width / 16 * 16);

    /// <summary>
    /// UIAdviser:onMouseDown: a left click on the adviser or his balloon moves on to the next message, or he gets down if
    /// there is none; a right click sends him down and the waiting messages to the history unsaid.
    /// </summary>
    /// <returns>Whether the click was on him, so nothing else gets it.</returns>
    private bool ClickAdviser(MouseState mouse)
    {
        var left = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        var right = mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released;
        if ((!left && !right) || !_adviserFigure.IsUp || _adviserFigure.Animation is not { } animation)
        {
            return false;
        }

        var origin = AdviserOrigin(_device.Viewport);
        var set = _animations.Animations;
        var onHim = _animations.HitTest(set.Advance(set.FirstFrame(animation), _adviserFigure.Frame), origin.X + 200, origin.Y, NoAdviserLayers, false, mouse.X, mouse.Y);
        if (!onHim && _adviserFigure.Speech is { } speech && _panelSprites is not null)
        {
            var balloonLeft = BalloonLeft(origin, AdviserFigure.BalloonWidth(speech)) - 16;
            var balloon = new Rectangle(balloonLeft, origin.Y - 40, origin.X + 155 + _panelSprites.WidthOf(39) - balloonLeft, 15 + _panelSprites.HeightOf(38));
            onHim = balloon.Contains(mouse.X, mouse.Y);
        }

        if (!onHim)
        {
            return false;
        }

        var now = Environment.TickCount64;
        if (right)
        {
            _adviser.DismissAll();
            _adviserFigure.Hide(now);
        }
        else if (_adviser.Queued > 0)
        {
            _adviser.Next(now);
        }
        else
        {
            _adviser.Next(now);
            _adviserFigure.Hide(now);
        }

        return true;
    }

    /// <summary>Breaks text into lines no wider than <paramref name="width"/> pixels at spaces (longer words stay whole).</summary>
    internal static IReadOnlyList<string> WrapToWidth(string text, int width, Func<string, int> measure)
    {
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var longer = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && measure(longer) > width)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = longer;
            }
        }

        if (line.Length > 0)
        {
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// UIPlaceStaff:draw: the member of staff being hired or carried stands, in the first frame of their idle animation
    /// facing east, on the tile they would be put on, grey where they cannot be.
    /// </summary>
    private void DrawPlacingStaff(SpriteBatch batch)
    {
        if (Simulation is not { } simulation || _session is null || _staffTile is not { } point)
        {
            return;
        }

        var tile = new TilePosition(point.X, point.Y);
        Humanoid shown;
        bool valid;
        if (_hiring is { } hiring)
        {
            var index = simulation.AvailableStaff(hiring.Class).ToList().IndexOf(hiring.Profile);
            if (index < 0)
            {
                return;
            }

            shown = Humanoid.CreateStaff(0, tile, hiring.Profile, _session.Player);
            valid = StaffHiring.Check(simulation, hiring.Class, index, tile, _session.Player) == StaffHireProblem.None;
        }
        else if (_pickedUpStaff is { } carried && simulation.FindEntity(carried) is Humanoid held)
        {
            shown = held;
            valid = held.Tile == tile || StaffMoving.Check(simulation, carried, tile, _session.Player) == StaffHireProblem.None;
        }
        else
        {
            return;
        }

        _humanoids.DrawPlacing(batch, shown, 32 * (tile.X - tile.Y), 16 * (tile.X + tile.Y), valid ? Color.White : StaffBlockedTint);
    }

    private void DrawOverlay(SpriteBatch batch, LevelMap map, Rectangle view)
    {
        // While building, the blueprint and the object being placed show where the cursor is, as in the original.
        // The member of staff being placed shows where they go instead.
        var hovered = _buildTool.IsActive || _furnishTool.IsActive || _hiring is not null || _pickedUpStaff is not null ? null : _hoveredTile;
        var (firstScanline, lastScanline) = MapRenderer.VisibleScanlines(map, view);
        for (var s = firstScanline; s <= lastScanline; s++)
        {
            var (firstX, lastX) = MapRenderer.VisibleColumns(map, view, s);
            for (var x = firstX; x <= lastX; x++)
            {
                var y = s - x;
                var tint = hovered == new Point(x, y) ? HoverTint : OverlayTint(map[x, y], x, y);
                if (tint != Color.Transparent)
                {
                    batch.Draw(_diamond, new Vector2((32 * (x - y)) - 32, 16 * (x + y)), tint);
                }

                if (_overlay == MapOverlay.Positions)
                {
                    // overlay("positions"): the tile's coordinates written on it.
                    var label = string.Create(CultureInfo.InvariantCulture, $"{x},{y}");
                    _text.Draw(batch, label, new Vector2((32 * (x - y)) - (_text.Measure(label).Width / 2f), (16 * (x + y)) + 8));
                }
            }
        }
    }

    private Color OverlayTint(MapTile tile, int x, int y)
    {
        return _overlay switch
        {
            MapOverlay.Temperature => tile.Has(TileFlags.Hospital) && Simulation is { } simulation
                ? TemperatureColor(simulation.Temperatures[x, y], _temperatureTheme) * 0.6f
                : Color.Transparent,
            MapOverlay.Passable => tile.Has(TileFlags.Passable) ? new Color(0, 200, 0) * 0.4f : new Color(200, 0, 0) * 0.4f,
            MapOverlay.Hospital => tile.Has(TileFlags.Buildable) ? new Color(0, 120, 255) * 0.45f
                : tile.Has(TileFlags.Hospital) ? new Color(255, 200, 0) * 0.45f
                : Color.Transparent,
            MapOverlay.Parcels => tile.Parcel == 0 ? Color.Transparent : ParcelColor(tile.Parcel) * 0.45f,
            _ => Color.Transparent,
        };
    }

    /// <summary>
    /// The town map's warmth colours (CorsixTH's th_lua_ui.cpp, <c>temperature_theme</c>): 1, red, red rising with the
    /// heat over a blue of 70; 2, blue below 11 °C, green up to 18 °C, red above; 3, yellow below 11 °C, orange to red
    /// above.
    /// </summary>
    internal static Color TemperatureColor(ushort temperature, int theme = 2)
    {
        // 4 °C and below is 0, 25 °C and above 255.
        var scaled = temperature < 5200 ? 0 : temperature > 32767 ? 255 : (temperature - 5200) / 108;
        const int MinOk = 140;
        const int MaxOk = 180;
        return theme switch
        {
            1 => new Color(scaled, 0, 70),
            3 => scaled < MinOk
                ? new Color(Channel(0, MinOk - 1, scaled, 100, 213), Channel(0, MinOk - 1, scaled, 80, 180), 0)
                : new Color(Channel(MinOk, 255, scaled, 223, 235), Channel(MinOk, 255, scaled, 184, 104), Channel(MinOk, 255, scaled, 0, 53)),
            _ => scaled < MinOk ? new Color(0, 0, Channel(0, MinOk - 1, scaled, 200, 60))
                : scaled < MaxOk ? new Color(0, Channel(MinOk, MaxOk - 1, scaled, 140, 224), 70)
                : new Color(Channel(MaxOk, 255, scaled, 224, 255), 0, 70),
        };

        // map_color_channel in th_lua_ui.h.
        static int Channel(int low, int high, int value, int start, int end) =>
            value <= low ? start : value >= high ? end : start + ((end - start) * (value - low) / (high - low));
    }

    private static Color ParcelColor(int parcel)
    {
        // Spread hues so that neighbouring parcel numbers differ visibly.
        var hue = (parcel * 0.618034f) % 1f;
        return HueToColor(hue);
    }

    private static Color HueToColor(float hue)
    {
        var h = hue * 6f;
        var x = 1f - MathF.Abs((h % 2f) - 1f);
        var (r, g, b) = (int)h switch
        {
            0 => (1f, x, 0f),
            1 => (x, 1f, 0f),
            2 => (0f, 1f, x),
            3 => (0f, x, 1f),
            4 => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };
        return new Color(r, g, b);
    }

    /// <summary>
    /// CorsixTH's dialogs' hotkeys, read from <c>hotkeys.txt</c> (see <see cref="Hotkeys"/>): by default F1 bank manager,
    /// F2 bank statement, F4 town map, F5 casebook, F6 research, F7 progress report, F8 graphs, F9 policy, F10 machines, Ctrl+H the
    /// adviser's history, Y hire, C faxes, F11 cheats, each opening its window or closing it again. F3 (staff
    /// management) stays the level list for now; Shift+F3 opens staff management.
    /// </summary>
    private void UpdateWindowKeys(KeyboardState keys, Func<Keys, bool> pressed)
    {
        // A new fax opens the faxes (C opens and closes them), as the original's message icon asks for a look.
        var faxes = FaxWindow.Pending(Simulation!, _session!.Player).ToHashSet(StringComparer.Ordinal);
        if (faxes.Any(fax => !_seenFaxes.Contains(fax)))
        {
            _windows.Open(new FaxWindow());
            _sounds.Play("NewFax.wav"); // UIBottomPanel:queueMessage
        }

        _seenFaxes = faxes;

        // UIBottomPanel:_showMessageIcon: a raise request's icon comes out of the fax door with NewFax.wav, but nothing opens.
        var raises = RaiseRequests().Select(staff => staff.Id).ToHashSet();
        if (raises.Any(id => !_seenRaises.Contains(id)))
        {
            _sounds.Play("NewFax.wav");
        }

        _seenRaises = raises;
        PauseForWindows();
        if (!ReferenceEquals(_session, _repairSession))
        {
            _repairSession = _session;
            _repairs.Reset();
            _announcements.Reset();
            _worldSounds.Reset();
        }

        // The announcer's: announceRepair's (critical) and the hospital's; the rest are plain sounds.
        var staffedDesk = Simulation!.Entities.Any(e => e is Humanoid { Behaviour: ReceptionistBehaviour { Phase: ReceptionistPhase.Staffing } } receptionist && receptionist.Employer == _session.Player);
        foreach (var sound in _repairs.Update(Simulation!, _session.Player))
        {
            _announcer.Request(sound, AnnouncementPriority.Critical, Simulation!.Hours, staffedDesk);
        }

        foreach (var sound in _announcements.Update(Simulation!, _session.Player))
        {
            _sounds.Play(sound);
        }

        foreach (var (name, priority) in _announcements.TakeAnnouncements())
        {
            _announcer.Request(name, priority, Simulation!.Hours, staffedDesk);
        }

        _worldSounds.Update(Simulation!, Environment.TickCount64, _session.IsPaused, (name, tile) => _sounds.PlayAt(name, ScreenOf(tile), ViewSize), _sounds.Play, name => _announcer.Request(name, AnnouncementPriority.Critical, Simulation!.Hours, staffedDesk));
        _announcer.Update(Environment.TickCount64, Simulation!.Hours, _session.IsPaused, staffedDesk, _config.PlayAnnouncements, name =>
        {
            // Announcer:_play queues the announcement's subtitle.
            var length = _sounds.PlayAnnouncement(name);
            if (_config.EnableAnnouncerSubtitles && length > TimeSpan.Zero && _sounds.LastPlayed is { } played)
            {
                _subtitles.Queue(_texts, played, Environment.TickCount64);
            }

            return length;
        });
        _subtitles.Update(Environment.TickCount64);

        _hellSounds.Update(Simulation!, _session.Speed, Environment.TickCount64, hole => _sounds.PlayAt("LAVA00*.WAV", ScreenOf(hole), ViewSize));
        if (HotkeyInput.Pressed(_hotkeys["ingame_openFirstMessage"], keys, pressed))
        {
            ToggleFax();
        }

        // The watch belongs to a game; behind the main menu there is none to show it for (CorsixTH has no world there).
        var watch = WatchWindow.IsRunning(Simulation!, _session.Player) && _mainMenu is null;
        if (watch && !_watchRan)
        {
            _windows.Open(new WatchWindow());
        }
        else if (!watch && _mainMenu is not null)
        {
            foreach (var open in _windows.Windows.OfType<WatchWindow>().ToList())
            {
                _windows.Close(open);
            }
        }

        _watchRan = watch;

        // The network game's land auction: everyone goes to the bidding screen; when the hammer falls, who got the plot.
        if (Simulation!.Auction is not null && !_windows.Windows.Any(w => w is AuctionWindow))
        {
            _windows.Open(new AuctionWindow());
        }

        if (Simulation.LastAuction is { } sold && !ReferenceEquals(sold, _lastAuctionSeen))
        {
            if (_lastAuctionSeen is not null || _auctionWatched)
            {
                ShowNotice(UiText.Format("openwards.auction.sold", "Plot %1% sold to %2% for $%3%", sold.Parcel, ProgressReportScreen.HospitalName(this, sold.Winner), sold.Bid));
            }

            _lastAuctionSeen = sold;
        }

        _auctionWatched = true;

        // Poaching: the member of staff's answer, and whoever has joined from another hospital.
        if (Simulation.Hospitals[_session.Player].LastPoach is { } answer && !ReferenceEquals(answer, _lastPoachSeen))
        {
            if (_lastPoachSeen is not null || answer.Tick >= Simulation.Tick - 1)
            {
                var name = Simulation.FindEntity(answer.StaffId) is Humanoid asked ? StaffLabels.Name(this, asked) : "?";
                ShowNotice(answer.Listened
                    ? UiText.Format("openwards.poach.listened", "%1% is unhappy and asks their hospital for much more money", name)
                    : UiText.Format("openwards.poach.refused", "%1% is happy where they are", name));
            }

            _lastPoachSeen = answer;
        }

        var elsewhere = Simulation.Entities.OfType<Humanoid>().Where(h => h.Profile is not null && h.Employer >= 0 && h.Employer != _session.Player).Select(h => h.Id).ToHashSet();
        if (_staffElsewhere is not null)
        {
            foreach (var joined in Simulation.Entities.OfType<Humanoid>().Where(h => h.Profile is not null && h.Employer == _session.Player && _staffElsewhere.Contains(h.Id)))
            {
                ShowNotice(UiText.Format("openwards.poach.joined", "%1% has left their hospital and joined yours", StaffLabels.Name(this, joined)));
            }
        }

        _staffElsewhere = elsewhere;

        // The annual report at the year end, the winning letter or losing fax at the level's end.
        var hospital = Simulation!.Hospitals[_session.Player];
        if (_lastYearSeen is { } seen && Simulation.Date.Year > seen)
        {
            _windows.Open(new AnnualReportWindow());
        }

        _lastYearSeen = Simulation.Date.Year;
        if (hospital.Outcome != EndOutcome.None && hospital.Outcome != _lastOutcome)
        {
            if (hospital.Outcome == EndOutcome.Won && FaxScreen.IsAvailable(this))
            {
                // World:winGame: the letter comes on the fax, opened at once (openLastMessage).
                _winLetterPending = true;
                _windows.Open(new FaxWindow());
            }
            else
            {
                _windows.Open(new LevelEndWindow());
                if (hospital.Outcome == EndOutcome.Lost)
                {
                    // World:loseGame: a losing movie first.
                    PlayLoseMovie();

                    // World:loseGame: the information dialog says the level is lost and why.
                    _windows.Open(new InformationWindow(LevelEndWindow.LostLines(_texts, hospital.LostTo)));
                }
            }
        }

        _lastOutcome = hospital.Outcome;
        if (HotkeyInput.Pressed(_hotkeys["ingame_toggleAdvisor"], keys, pressed))
        {
            ToggleAdviser();
        }

        if (HotkeyInput.Pressed(_hotkeys["global_releaseMouse"], keys, pressed))
        {
            _mouseReleased = true;
        }

        if (HotkeyInput.Pressed(_hotkeys["global_resetApp"], keys, pressed))
        {
            // UI:resetApp: the game starts again.
            Restart();
        }

        if (HotkeyInput.Pressed(_hotkeys["global_screenshot"], keys, pressed))
        {
            RequestScreenshot();
        }

        // UIBottomPanel's openSave and restart (CorsixTH's Shift+L load and Shift+Q quit are still this game's repay
        // and guess cure stand-in keys).
        if (HotkeyInput.Pressed(_hotkeys["ingame_saveMenu"], keys, pressed))
        {
            MenuAction("save");
        }

        if (HotkeyInput.Pressed(_hotkeys["ingame_restartLevel"], keys, pressed))
        {
            MenuAction("restart");
        }

        if (HotkeyInput.Pressed(_hotkeys["ingame_toggleSounds"], keys, pressed))
        {
            ToggleSounds();
        }

        if (HotkeyInput.Pressed(_hotkeys["ingame_toggleAnnouncements"], keys, pressed))
        {
            ToggleAnnouncements();
        }

        if (HotkeyInput.Pressed(_hotkeys["ingame_toggleMusic"], keys, pressed))
        {
            ToggleMusic();
        }

        if (HotkeyInput.Pressed(_hotkeys["ingame_jukebox"], keys, pressed) && _music.HasMusic)
        {
            // UIBottomPanel:openJukebox.
            _windows.Toggle(new JukeboxWindow(_music.Playlist.Count));
        }

        (string Action, Func<UiWindow> Window)[] dialogs =
        [
            ("ingame_panel_hireStaff", () => new HireWindow()),
            ("ingame_panel_staffManage", () => new StaffManagementWindow()),
            ("ingame_panel_townMap", () => new TownMapWindow()),
            ("ingame_panel_casebook", () => new CasebookWindow()),
            ("ingame_panel_research", () => new ResearchWindow()),
            ("ingame_panel_status", () => new ProgressReportWindow()),
            ("ingame_panel_charts", () => new GraphWindow()),
            ("ingame_panel_policy", () => new PolicyWindow()),
            ("ingame_panel_machineMenu", () => new MachineMenuWindow()),
            ("ingame_panel_adviserHistory", () => new AdviserHistoryWindow()),
            ("ingame_showCheatWindow", () => new CheatsWindow()),
            ("global_showLuaConsole", () => new DebugConsoleWindow(MenuAction)),
            ("ingame_panel_casebook_alt02", () => new CasebookWindow()),
        ];
        foreach (var (action, window) in dialogs)
        {
            if (HotkeyInput.Pressed(_hotkeys[action], keys, pressed))
            {
                _windows.Toggle(window());
                _sounds.Play("selectx.wav");
            }
        }

        foreach (var (action, statement) in new[] { ("ingame_panel_bankManager", false), ("ingame_panel_bankStats", true) })
        {
            if (HotkeyInput.Pressed(_hotkeys[action], keys, pressed))
            {
                ToggleBank(statement);
                _sounds.Play("selectx.wav");
            }
        }
    }

    /// <summary>
    /// UIBottomPanel:dialogBankCommon: F1 and F2 open the bank manager on its main page or its statement; with it open,
    /// the other key turns to that page and the same key closes it. Without the original's graphics they are two text
    /// windows.
    /// </summary>
    private void ToggleBank(bool statement)
    {
        if (!BankManagerScreen.IsAvailable(this))
        {
            _windows.Toggle(statement ? new BankStatementWindow() : new BankManagerWindow());
        }
        else if (_windows.Windows.OfType<BankManagerWindow>().FirstOrDefault() is { } open && open.IsShowingStatement == statement)
        {
            _windows.Close(open);
        }
        else
        {
            OpenBank(statement);
        }
    }

    /// <summary>The menu's bank manager and statement (dialogBankManager(true), dialogBankStats(true)): opens it on that page.</summary>
    private void OpenBank(bool statement)
    {
        if (!BankManagerScreen.IsAvailable(this))
        {
            _windows.Open(statement ? new BankStatementWindow() : new BankManagerWindow());
        }
        else if (_windows.Windows.OfType<BankManagerWindow>().FirstOrDefault() is { } open)
        {
            open.ShowStatement(statement);
        }
        else
        {
            _windows.Open(new BankManagerWindow(statement));
        }
    }

    /// <summary>
    /// Lets the adviser watch the game and move on through his messages; a new game or a loaded one starts him
    /// afresh.
    /// </summary>
    private void UpdateAdviser()
    {
        if (!ReferenceEquals(_session, _adviserSession))
        {
            _adviserSession = _session;
            _adviser.Clear();
            _adviserFigure.Reset();
            _adviserChecks = new AdviserChecks(_adviser, _adviserRandom);

            // The tutorial belongs to the game it was started with: another game ends it.
            if (_tutorial is not null && !ReferenceEquals(_session, _tutorialSession))
            {
                _tutorial = null;
                Tutorial.Current = null;
            }

            _tutorial?.SayAgain();

            // A game saved during the tutorial comes back without it: its opening watch starts afresh.
            if (_tutorial is null && Simulation is { } loaded && _session!.Player < loaded.Hospitals.Count && loaded.Hospitals[_session.Player].IsOpeningWatchHeld)
            {
                _session.Submit(new HoldOpeningWatchCommand(false));
            }

            _roomsDescribed.Clear();
            if (IsWindowHostReady)
            {
                // Rooms already built in a loaded game have had their description.
                _roomsDescribed.UnionWith(Simulation!.Entities.OfType<Room>().Where(room => room.IsActive).Select(room => room.RoomType));
            }
        }

        if (IsWindowHostReady)
        {
            var now = Environment.TickCount64;
            _adviserChecks.StartedWithTutorial = _tutorialSession is not null && ReferenceEquals(_session, _tutorialSession);
            _adviserChecks.Observe(Simulation!, _session!.Player, _texts, now);
            _adviser.Update(now);
            _adviserFigure.Update(now, _adviser.Current);
            DescribeNewRooms(Simulation!, _session.Player);
        }
    }

    /// <summary>
    /// <c>Room:roomFinished</c>: the first time a kind of room is built, its description from the game's texts.
    /// </summary>
    private void DescribeNewRooms(Simulation simulation, int player)
    {
        foreach (var room in simulation.Entities.OfType<Room>())
        {
            if (room.IsActive && RoomBuilder.IsOwnedBy(simulation.Map, room, player) && _roomsDescribed.Add(room.RoomType)
                && _config.RoomInformationDialogs && _texts?.RoomDescription(room.RoomType) is { Count: > 0 } description)
            {
                _windows.Open(new InformationWindow(description, simulation.Content.FindRoom(room.RoomType) is { } definition ? UiText.Room(definition) : null));
            }
        }
    }

    Adviser IWindowHost.Adviser => _adviser;

    /// <summary>Whether there is a game with a hospital of the local player for windows to show.</summary>
    private bool IsWindowHostReady => _session is { Simulation: { } simulation, Player: var player } && player < simulation.Hospitals.Count;

    Simulation IWindowHost.Simulation => Simulation!;

    int IWindowHost.Player => _session!.Player;

    OriginalTexts? IWindowHost.Texts => _texts;

    void IWindowHost.Submit(Command command)
    {
        if (command is AnswerRaiseCommand answer)
        {
            // The answer only takes effect on the next tick (after the pause the request window made): its icon stays away meanwhile.
            _raisesAnswered.Add(answer.StaffId);
        }

        _session?.Submit(command);
    }

    void IWindowHost.Open(UiWindow window) => _windows.Open(window);

    void IWindowHost.Close(UiWindow window) => _windows.Close(window);

    void IWindowHost.Notice(string text) => ShowNotice(text);

    void IWindowHost.PlaySound(string name) => _sounds.Play(name);

    private bool _vaccinationMode;

    bool IWindowHost.IsVaccinationMode => _vaccinationMode && CoverUpRunning() is not null;

    void IWindowHost.ToggleVaccinationMode() => _vaccinationMode = !_vaccinationMode && CoverUpRunning() is not null;

    /// <summary>The player's epidemic while its cover-up's watch runs, when patients can be picked for vaccination.</summary>
    private Epidemic? CoverUpRunning() =>
        Simulation is { } simulation && _session is not null && _session.Player < simulation.Hospitals.Count
        && simulation.Hospitals[_session.Player].Epidemics.Current is { IsCoverUp: true, IsWatchRunning: true } epidemic ? epidemic : null;

    /// <summary>isValidVaccinationTarget: an infected patient not yet vaccinated or picked, while the cover-up runs.</summary>
    private bool IsVaccinationTarget(Humanoid humanoid) =>
        CoverUpRunning() is not null && humanoid.Behaviour is PatientBehaviour { IsInfected: true, IsVaccinated: false, IsMarkedForVaccination: false };

    private bool _winLetterPending;

    bool IWindowHost.IsWinLetterPending => _winLetterPending && Simulation is { } simulation && _session is not null && simulation.Hospitals[_session.Player].Outcome == EndOutcome.Won;

    void IWindowHost.DeclineWinLetter() => _winLetterPending = false;

    void IWindowHost.PickUpStaff(int staffId)
    {
        _pickedUpStaff = staffId;
        _sounds.Play("pickup.wav");
        ShowNotice(UiText.Get("openwards.status.picked_up", "Picked up: click to put them down, right click lets them go"));
    }

    void IWindowHost.CenterOn(TilePosition tile) => _camera.Center = Camera.TileCenter(tile.X, tile.Y);

    bool IWindowHost.HasNextLevel => _session is LocalSession && _levelIndex + 1 < _levels.Count;

    OriginalGraphics? IWindowHost.Graphics => _graphics;

    string? IWindowHost.LevelTitle => _session is LocalSession local ? LevelTitle(local.Name, _texts) : null;

    string? IWindowHost.NextLevelTitle => _session is LocalSession && _levelIndex + 1 < _levels.Count ? LevelTitle(_levels[_levelIndex + 1].Name, _texts, capitals: false) : null;

    /// <summary>
    /// Map:load's level_name: an original level's town is <c>_S.level_names[level]</c> (S[12][level + 1]) in capitals;
    /// other levels go by their file's name.
    /// </summary>
    internal static string? LevelTitle(string fileName, OriginalTexts? texts, bool capitals = true)
    {
        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"^LEVEL\.L(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, CultureInfo.InvariantCulture, out var level))
        {
            return capitals ? texts?.String(12, level + 1)?.ToUpper(CultureInfo.CurrentCulture) : texts?.String(12, level + 1);
        }

        return Path.GetFileNameWithoutExtension(fileName) is { Length: > 0 } name ? name.ToUpper(CultureInfo.CurrentCulture) : null;
    }

    string IWindowHost.PlayerName => _config.PlayerName is { Length: > 0 } name ? name : "Player";

    string? IWindowHost.HotkeyText(string action) => _hotkeys[action]?.ToString();

    void IWindowHost.StartLevel(bool next)
    {
        if (_session is not LocalSession)
        {
            return;
        }

        _windows.CloseAll();
        var moveOn = next && _levelIndex + 1 < _levels.Count;
        SelectLevel(next ? Math.Min(_levelIndex + 1, _levels.Count - 1) : _levelIndex);
        if (moveOn)
        {
            // UIFax's accept_new_level: the board's movie to the next level, the original's by its number, a custom
            // campaign's from where it ends at the twelfth.
            switch (_campaignMovies)
            {
                case CampaignMovies.Original:
                    PlayAdvanceMovie(_levelIndex + 1);
                    break;
                case CampaignMovies.Custom:
                    PlayAdvanceMovie(Math.Max(1, 12 - _levels.Count + _levelIndex));
                    break;
            }
        }
        else if (next && _campaignMovies != CampaignMovies.None)
        {
            // return_to_main_menu after the campaign's last level: the winning movie.
            PlayMovie(_movieFiles.WinGame, waitForStop: false);
        }
    }

    /// <summary>Whose board movies a run of levels has.</summary>
    private enum CampaignMovies
    {
        None,
        Original,
        Custom,
    }

    /// <summary>The menu's items that are not windows.</summary>
    private readonly OriginalMenuBar _menuBar = new();

    /// <summary>Whether the fax's 24328 has put the cheats menu on the bar (<c>app.cheats_menu_unlocked</c>), until the game is left.</summary>
    private bool _cheatsMenuUnlocked;

    /// <summary>Whether an object is being placed, in a room or the corridor (UIPlaceObjects is open).</summary>
    private bool IsPlacingObject => _buildTool.IsPlacingObject || _furnishTool.IsActive;

    void IWindowHost.UnlockCheatsMenu() => _cheatsMenuUnlocked = true;

    private int MenuBarMeasure(string text) => ((IWindowHost)this).Graphics?.Font("FONT01V", OriginalGraphics.MainPalette)?.Measure(text).Width ?? 0;

    /// <summary>
    /// makeGameMenu's menus: file (load, save, quick save and load, restart, quit), options, charts (the dialogs) and
    /// debug (replays), and once the fax's 24328 has unlocked them the cheats (<c>UIMenuBar:unlockCheats</c>). CorsixTH's
    /// own options and debug items this game has no counterpart for are left out.
    /// </summary>
    private List<MenuBarMenu> MenuBarMenus()
    {
        // The hotkey label CorsixTH puts into an item (hotkey_value_label) is left out.
        static string Item(string name, string fallback) => UiText.Get(name, fallback).Replace("(%1%)", string.Empty, StringComparison.Ordinal).Replace("%1%", string.Empty, StringComparison.Ordinal).Replace("%s", string.Empty, StringComparison.Ordinal).Trim();
        MenuBarItem Open(string name, string fallback, Func<UiWindow> window) => new(Item(name, fallback), () => _windows.Open(window()));
        MenuBarItem Do(string name, string fallback, string action) => new(Item(name, fallback), () => MenuAction(action));

        // The game speed menu's check items (rate): the speed ticked is the one running.
        MenuBarItem Speed(string name, string fallback, GameSpeed speed) => new(Item(name, fallback), () => _session?.SetSpeed(speed), () => _session?.Speed == speed);
        List<MenuBarMenu> menus =
        [
            new(Item("menu.file", "FILE"), [
                Do("menu_file.load", "LOAD", "load"),
                Do("menu_file.save", "SAVE", "save"),
                Do("openwards.menu.quick_save", "Quick save", "quicksave"),
                Do("openwards.menu.quick_load", "Quick load", "quickload"),
                Do("menu_file.restart", "RESTART", "restart"),
                Do("menu_file.quit", "QUIT", "quit")]),
            new(Item("menu.options", "OPTIONS"), [
                Do("openwards.menu.settings", "Settings", "options"),
                Do("openwards.menu.levels", "Levels", "levels"),
                new(Item("menu_options.sound", "SOUND"), ToggleSounds, () => _config.PlaySounds),
                new(Item("menu_options.announcements", "ANNOUNCEMENTS"), ToggleAnnouncements, () => _config.PlayAnnouncements),
                .. (_music.HasMusic
                    ? [new MenuBarItem(Item("menu_options.music", "MUSIC"), ToggleMusic, () => _config.PlayMusic), new MenuBarItem(Item("menu_options.jukebox", "JUKEBOX"), () => _windows.Open(new JukeboxWindow(_music.Playlist.Count)))]
                    : Array.Empty<MenuBarItem>()),
                new(Item("menu_options.adviser_disabled", "ADVISER"), ToggleAdviser, () => !_config.AdviserDisabled),
                new(Item("menu_debug.transparent_walls", "TRANSPARENT WALLS"), () => _wallsTransparentToggled = !_wallsTransparentToggled, () => _wallsTransparentToggled),
                // appendVolume's submenus of the sound, announcements and music volumes, 10 % to 100 %.
                MenuBarItem.Menu(Item("menu_options.sound_vol", "SOUND VOLUME"), Volumes(_config.SoundVolume, v => _config with { SoundVolume = v, PlaySounds = true })),
                MenuBarItem.Menu(Item("menu_options.announcements_vol", "ANNOUNCEMENTS VOLUME"), Volumes(_config.AnnouncementVolume, v => _config with { AnnouncementVolume = v, PlayAnnouncements = true })),
                .. (_music.HasMusic ? [MenuBarItem.Menu(Item("menu_options.music_vol", "MUSIC VOLUME"), Volumes(_config.MusicVolume, v => _config with { MusicVolume = v }))] : Array.Empty<MenuBarItem>()),
                // display_options: lock windows (for this run only), edge scrolling, capture mouse, the 24 hour clock.
                new(Item("menu_options.lock_windows", "LOCK WINDOWS"), () => _windows.LockWindows = !_windows.LockWindows, () => _windows.LockWindows),
                new(Item("menu_options.edge_scrolling", "EDGE SCROLLING"), () => ApplyConfig(_config with { PreventEdgeScrolling = !_config.PreventEdgeScrolling }), () => !_config.PreventEdgeScrolling),
                new(Item("menu_options.capture_mouse", "CAPTURE MOUSE"), () => ApplyConfig(_config with { CaptureMouse = !_config.CaptureMouse }), () => _config.CaptureMouse),
                new(Item("menu_options.twentyfour_hour_clock", "24 HOUR CLOCK"), () => ApplyConfig(_config with { TwentyFourHourClock = !_config.TwentyFourHourClock }), () => _config.TwentyFourHourClock),
                MenuBarItem.Menu(Item("menu_options.wage_increase", "WAGE REQUESTS"), [
                    new(Item("menu_options_wage_increase.grant", "GRANT"), () => _session?.Submit(new SetPolicyCommand(PolicyKind.GrantWageIncrease, 1)), () => OwnPolicies()?.GrantWageIncrease == true),
                    new(Item("menu_options_wage_increase.deny", "DENY"), () => _session?.Submit(new SetPolicyCommand(PolicyKind.GrantWageIncrease, 0)), () => OwnPolicies()?.GrantWageIncrease == false)]),
                MenuBarItem.Menu(Item("menu_options.warmth_colors", "WARMTH COLOURS"), [
                    new(Item("menu_options_warmth_colors.choice_1", "RED"), () => _temperatureTheme = 1, () => _temperatureTheme == 1),
                    new(Item("menu_options_warmth_colors.choice_2", "BLUE GREEN RED"), () => _temperatureTheme = 2, () => _temperatureTheme == 2),
                    new(Item("menu_options_warmth_colors.choice_3", "YELLOW ORANGE RED"), () => _temperatureTheme = 3, () => _temperatureTheme == 3)]),
                MenuBarItem.Menu(Item("menu_options.game_speed", "GAME SPEED"), [
                    Speed("menu_options_game_speed.pause", "PAUSE", GameSpeed.Pause),
                    Speed("menu_options_game_speed.slowest", "SLOWEST", GameSpeed.Slowest),
                    Speed("menu_options_game_speed.slower", "SLOWER", GameSpeed.Slower),
                    Speed("menu_options_game_speed.normal", "NORMAL", GameSpeed.Normal),
                    Speed("menu_options_game_speed.max_speed", "MAX SPEED", GameSpeed.MaxSpeed),
                    Speed("menu_options_game_speed.and_then_some_more", "AND THEN SOME MORE", GameSpeed.AndThenSomeMore),
                    Speed("openwards.menu.insane_speed", "Insane", GameSpeed.Insane)])]),
            new(Item("menu.charts", "CHARTS"), [
                new(Item("menu_charts.bank_manager", "BANK MANAGER"), () => OpenBank(statement: false)),
                new(Item("menu_charts.statement", "STATEMENT"), () => OpenBank(statement: true)),
                Open("menu_charts.staff_listing", "STAFF LISTING", () => new StaffManagementWindow()),
                Open("menu_charts.town_map", "TOWN MAP", () => new TownMapWindow()),
                Open("menu_charts.casebook", "CASEBOOK", () => new CasebookWindow()),
                Open("menu_charts.research", "RESEARCH", () => new ResearchWindow()),
                Open("menu_charts.status", "STATUS", () => new ProgressReportWindow()),
                Open("menu_charts.graphs", "GRAPHS", () => new GraphWindow()),
                Open("menu_charts.policy", "POLICY", () => new PolicyWindow()),
                Open("menu_charts.machine_menu", "MACHINE MENU", () => new MachineMenuWindow())]),
            new(Item("menu.debug", "DEBUG"), [
                // makeDebugMenu: jump to level (local games), limit camera, disable salary raise, the debug patient and
                // rat, the console, and the map overlays.
                .. (_session is LocalSession && _levels.Count > 0
                    ? [MenuBarItem.Menu(Item("menu_debug.jump_to_level", "JUMP TO LEVEL"), [.. _levels.Take(12).Select((_, index) =>
                        new MenuBarItem(string.Create(CultureInfo.InvariantCulture, $"L{index + 1}"), () => SelectLevel(index), () => _levelIndex == index))])]
                    : Array.Empty<MenuBarItem>()),
                new(Item("menu_debug.limit_camera", "LIMIT CAMERA"), () => _limitCamera = !_limitCamera, () => _limitCamera),
                new(Item("menu_debug.disable_salary_raise", "DISABLE SALARY RAISE"), () => _session?.Submit(new CheatCommand(CheatKind.ToggleSalaryRaise)),
                    () => Simulation is { } shown && _session is { } own && own.Player < shown.Hospitals.Count && Cheats.SalaryRaiseDisabled(shown, shown.Hospitals[own.Player])),
                Open("menu_debug.make_debug_patient", "MAKE DEBUG PATIENT", () => new DebugPatientWindow(MakeDebugPatient)),
                Do("menu_debug.make_debug_rat", "MAKE DEBUG RAT", "debugrat"),
                Open("menu_debug.lua_console", "LUA CONSOLE", () => new DebugConsoleWindow(MenuAction)),
                MenuBarItem.Menu(Item("menu_debug.map_overlay", "MAP OVERLAY"), [
                    new(Item("menu_debug_overlay.none", "NONE"), () => _overlay = MapOverlay.None, () => _overlay == MapOverlay.None),
                    new(Item("menu_debug_overlay.flags", "FLAGS"), () => _overlay = MapOverlay.Passable, () => _overlay == MapOverlay.Passable),
                    new(Item("menu_debug_overlay.positions", "POSITIONS"), () => _overlay = MapOverlay.Positions, () => _overlay == MapOverlay.Positions),
                    new(Item("menu_debug_overlay.heat", "HEAT"), () => _overlay = MapOverlay.Temperature, () => _overlay == MapOverlay.Temperature),
                    new(Item("menu_debug_overlay.parcel", "PARCEL"), () => _overlay = MapOverlay.Parcels, () => _overlay == MapOverlay.Parcels)]),
                Do("openwards.menu.save_replay", "Save replay", "savereplay"),
                Do("openwards.menu.watch_replay", "Watch replay", "replays")]),
        ];
        if (_cheatsMenuUnlocked)
        {
            menus.Add(new(Item("menu.cheats_title", "CHEATS"), [Open("menu.cheats", "CHEATS", () => new CheatsWindow())]));
        }

        return menus;
    }

    /// <summary>appendVolume: 10 % to 100 %, the one nearest the volume ticked; picking one sets it and saves it.</summary>
    private List<MenuBarItem> Volumes(decimal current, Func<decimal, GameConfig> set) =>
    [
        .. Enumerable.Range(1, 10).Select(level => new MenuBarItem(
            UiText.Get("menu_options_volume." + (level * 10).ToString(CultureInfo.InvariantCulture), (level * 10).ToString(CultureInfo.InvariantCulture) + "%").Trim(),
            () => ApplyConfig(set(level / 10m)),
            () => Math.Round(current * 10) == level)),
    ];

    /// <summary>The player's hospital's policies, for the wage requests submenu.</summary>
    private Sim.Economy.HospitalPolicies? OwnPolicies() =>
        Simulation is { } simulation && _session is { } session && session.Player < simulation.Hospitals.Count ? simulation.Hospitals[session.Player].Policies : null;

    private void MenuAction(string action)
    {
        switch (action)
        {
            case "save" or "load" when _session is not LocalSession:
                ShowNotice(UiText.Get("openwards.status.only_local_save", "Only local games can be saved and loaded"));
                break;
            case "save" or "load" when _buildTool.IsActive || _furnishTool.IsActive:
                ShowNotice(UiText.Get("openwards.status.finish_building_first", "Finish building before saving or loading"));
                break;
            case "save" or "load":
                _windows.Open(new SaveLoadWindow(_saves, action == "save", SaveGameTo, LoadGame, _install.FindDirectory("SAVE")));
                break;
            case "quicksave" or "quickload":
                QuickSave(load: action == "quickload");
                break;
            case "restart":
                _windows.Open(new ConfirmWindow(Confirmation(7, "Are you sure you want to restart the level?"), () => ((IWindowHost)this).StartLevel(next: false)));
                break;
            case "quit":
                _windows.Open(new ConfirmWindow(Confirmation(1, "Are you sure you want to quit?"), () => ExitRequested = true));
                break;
            case "options":
                _windows.Open(NewOptionsWindow());
                break;
            case "savereplay":
                SaveReplay();
                break;
            case "debugrat":
                MakeDebugRat();
                break;
            case "replays":
                _windows.Open(new ReplayListWindow(Path.Combine(_saves.Directory, Replay.Folder), WatchReplay));
                break;
            case "newgame":
                _windows.Open(new NewGameWindow(AvailableDifficulties(_install), _config.PlayerName, (difficulty, tutorial) => StartCampaign(difficulty, tutorial), SavePlayerName));
                break;
            case "levels" when _session is NetworkSession lobby:
                ToggleLobby(lobby);
                break;
            case "levels" when _levels.Count > 0:
                _levelList = true;
                break;
        }
    }

    /// <summary>Saves the replay of the game so far (local or network) to the saves folder's replays.</summary>
    private void SaveReplay()
    {
        if (_session is not { Recording: { } recording, Simulation: { } simulation } session)
        {
            ShowNotice(UiText.Get("openwards.status.no_replay", "There is no game to record"));
            return;
        }

        var name = string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileNameWithoutExtension(recording.LevelName)} {DateTime.Now:yyyy-MM-dd HH-mm-ss}{Replay.Extension}");
        var path = Path.Combine(_saves.Directory, Replay.Folder, name);
        try
        {
            recording.Finish(simulation);
            recording.Save(path);
            ShowNotice(UiText.Format("openwards.status.replay_saved", "Saved the replay %1%", Path.GetFileNameWithoutExtension(path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNotice(UiText.Format("openwards.status.cannot_save_replay", "Cannot save the replay: %1%", e.Message));
        }
    }

    /// <summary>Plays a replay back in place of the game.</summary>
    private void WatchReplay(string path)
    {
        ReplaySession replay;
        try
        {
            replay = new ReplaySession(Path.GetFileNameWithoutExtension(path), Replay.Load(path), BaseGameContent.Create());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
            ShowNotice(UiText.Format("openwards.status.cannot_watch", "Cannot watch %1%: %2%", Path.GetFileName(path), e.Message));
            return;
        }

        _session?.Dispose();
        replay.SetSpeed(GameSpeed.Normal);
        _session = replay;
        _error = null;
        _demoPending = _demoDeskPending = _demoDiagnosisPending = _demoMachinePending = _demoToiletsPending = false;
        _demoFurniture.Clear();
        PrepareMapIfReady();
        ShowNotice(UiText.Get("openwards.status.watching_replay", "Watching a replay: your commands are not taken; pick a level or load a game to play again"));
    }

    /// <summary>CorsixTH's <c>confirmation</c> texts, S[35][index], with an English stand-in.</summary>
    private string Confirmation(int index, string fallback) => _texts?.String(35, index) ?? fallback;

    /// <summary>Saves the local game to a file, saying so or why not.</summary>
    private bool SaveGameTo(string path)
    {
        if (_session is not LocalSession local)
        {
            return false;
        }

        try
        {
            SaveGameStore.Save(path, local.Name, local.Simulation);
            ShowNotice(UiText.Format("openwards.status.saved", "Saved %1%", Path.GetFileNameWithoutExtension(path)));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNotice(UiText.Format("openwards.status.cannot_save", "Cannot save: %1%", e.Message));
            return false;
        }
    }

    private string? _screenshotRequest;

    /// <inheritdoc/>
    public string? TakeScreenshotRequest()
    {
        var request = _screenshotRequest;
        _screenshotRequest = null;
        return request;
    }

    /// <inheritdoc/>
    public void ScreenshotSaved() => _sounds.Play("SNAPSHOT.WAV");

    /// <summary>
    /// <c>UI:makeScreenshot</c>: the next frame goes to <c>Screenshot_yyyyMMdd-HHmmss.png</c> in the Screenshots folder
    /// beside the settings file; not twice in one second.
    /// </summary>
    private void RequestScreenshot()
    {
        // App:initScreenshotsDir: the screenshots setting, else "Screenshots" beside the config file.
        var folder = _config.Screenshots is { Length: > 0 } configured
            ? configured.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_configPath ?? GameConfig.FileName)) ?? ".", "Screenshots");
        var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"Screenshot_{DateTime.Now:yyyyMMdd-HHmmss}.png"));
        if (File.Exists(path))
        {
            Console.Error.WriteLine("Screenshot failed: File already exists");
            return;
        }

        _screenshotRequest = path;
    }

    private bool _fullscreenToggleRequested;

    /// <summary>The window size the graphics settings changed to, for the game to apply (in points), or null.</summary>
    private Point? _windowSizeRequest;

    /// <summary>The network chat's lines already seen: a new one opens the chat window.</summary>
    private int _chatSeen;

    /// <inheritdoc/>
    public void TextInput(char character)
    {
        if (_windows.Windows.OfType<ITextInputWindow>().FirstOrDefault(window => window.IsCapturing) is { } typing)
        {
            typing.Type(character);
        }
    }

    /// <summary>
    /// The network game's chat: a line said opens its window, and Return on the map (when no build tool or window takes
    /// it) opens it to type a line.
    /// </summary>
    private void UpdateChat(bool returnPressed)
    {
        if (_session is not NetworkSession network)
        {
            return;
        }

        var chat = _windows.Windows.OfType<ChatWindow>().FirstOrDefault();
        var talk = returnPressed && !_buildTool.IsActive && !_furnishTool.IsActive && _editorTool is null && _mainMenu is null;
        if (chat is null && (talk || network.ChatCount > _chatSeen))
        {
            chat = new ChatWindow(() => network.Chat, network.Say);
            _windows.Open(chat);
        }

        if (talk)
        {
            chat!.StartTyping();
        }

        _chatSeen = network.ChatCount;
    }

    /// <inheritdoc/>
    public bool TakeFullscreenToggleRequest()
    {
        var requested = _fullscreenToggleRequested;
        _fullscreenToggleRequested = false;
        return requested;
    }

    /// <inheritdoc/>
    public Point? TakeWindowSizeRequest()
    {
        var requested = _windowSizeRequest;
        _windowSizeRequest = null;
        return requested;
    }

    /// <summary><c>UI:fullscreenHotkey</c>: the window goes fullscreen or back, and the settings keep it (<c>setVideoMode</c>).</summary>
    private void ToggleFullscreen()
    {
        _fullscreenToggleRequested = true;
        _config = _config with { Fullscreen = !_config.Fullscreen };
        SaveConfig();
    }

    /// <summary><c>UI:togglePlaySounds</c>: sound effects on or off, kept in the settings.</summary>
    private void ToggleSounds()
    {
        _config = _config with { PlaySounds = !_config.PlaySounds };
        _sounds.Dispose();
        _sounds = SoundEffects.Load(_install, _config);
        SaveConfig();
    }

    /// <summary><c>UI:togglePlayAnnouncements</c>: the announcer on or off, kept in the settings.</summary>
    private void ToggleAnnouncements()
    {
        _config = _config with { PlayAnnouncements = !_config.PlayAnnouncements };
        SaveConfig();
    }

    /// <summary>
    /// <c>MoviePlayer:playMovie</c>: plays a movie if <c>movies</c> is on and it is there, else goes straight on to
    /// <paramref name="onDone"/>; a movie with sound pauses the music until it ends.
    /// </summary>
    private void PlayMovie(string? path, bool waitForStop, Action? onDone = null, string? sound = null)
    {
        if (!_config.Movies || path is null)
        {
            onDone?.Invoke();
            return;
        }

        void Done()
        {
            // destroyMovie: the music goes on where it was, and the movie's sound stops.
            if (_holdingMusic && _music.IsPaused)
            {
                _music.TogglePause();
            }

            _holdingMusic = false;
            _previousMouse = InputScript.Mouse();
            _previousKeys = InputScript.Keyboard();
            onDone?.Invoke();
        }

        if (!_movie.Play(path, waitForStop, Done, (float)Math.Clamp(_config.SoundVolume * 2, 0m, 1m)))
        {
            return;
        }

        if ((_movie.HasAudio || sound is not null) && _music.Current is not null && !_music.IsPaused)
        {
            _holdingMusic = _music.TogglePause();
        }

        if (sound is not null)
        {
            _sounds.Play(sound);
        }
    }

    /// <summary>
    /// A movie playing takes the input (<c>UI:onMouseDown</c>, the stop and pause keys) and the game waits: a left click,
    /// Esc or Q stops it, P pauses it.
    /// </summary>
    private void UpdateMovie(bool hasFocus)
    {
        _movie.Update();
        var keys = InputScript.Keyboard();
        var mouse = InputScript.Mouse();
        if (hasFocus && _movie.IsPlaying)
        {
            bool Pressed(Keys key) => keys.IsKeyDown(key) && !_previousKeys.IsKeyDown(key);
            if ((mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released)
                || HotkeyInput.Pressed(_hotkeys["global_stop_movie"], keys, Pressed) || HotkeyInput.Pressed(_hotkeys["global_stop_movie_alt"], keys, Pressed))
            {
                _movie.Stop();
            }
            else if (HotkeyInput.Pressed(_hotkeys["global_pause_movie"], keys, Pressed))
            {
                _movie.TogglePause();
            }
        }

        if (_movie.IsPlaying)
        {
            _previousKeys = keys;
            _previousMouse = mouse;
        }
    }

    /// <summary>
    /// <c>App:checkIdle</c>: about thirty seconds on the main menu with nothing touched plays the demo movie
    /// (<c>play_demo</c>).
    /// </summary>
    private void UpdateDemoIdle(GameTime gameTime)
    {
        var keys = InputScript.Keyboard();
        var mouse = InputScript.Mouse();
        var touched = keys.GetPressedKeyCount() > 0 || mouse.X != _previousMouse.X || mouse.Y != _previousMouse.Y
            || mouse.LeftButton == ButtonState.Pressed || mouse.RightButton == ButtonState.Pressed;
        if (_mainMenu is null || _windows.Windows.LastOrDefault() != _mainMenu || touched || !_config.PlayDemo)
        {
            _idleMilliseconds = 0;
            return;
        }

        // 1680 ticks of about 18 ms.
        _idleMilliseconds += gameTime.ElapsedGameTime.TotalMilliseconds;
        if (_idleMilliseconds > 1680 * 18)
        {
            _idleMilliseconds = 0;
            PlayMovie(_movieFiles.Demo, waitForStop: false);
        }
    }

    /// <summary>
    /// <c>MoviePlayer:playAdvanceMovie</c>: the campaign board's movie going on to a level, with the dice's sound
    /// (DICE122M.WAV for the twelfth level, DICEYFIN.WAV for the others); it waits for a click.
    /// </summary>
    private void PlayAdvanceMovie(int level)
    {
        if (_movieFiles.Advance.TryGetValue(level, out var path))
        {
            PlayMovie(path, waitForStop: true, sound: level == 12 ? "DICE122M.WAV" : "DICEYFIN.WAV");
        }
    }

    /// <summary>
    /// <c>MoviePlayer:playLoseMovie</c>: one of the losing movies at random, waiting for a click, with a newspaper headline
    /// on its paper near its end (<c>loseMovieOverlay</c>).
    /// </summary>
    private void PlayLoseMovie()
    {
        if (_movieFiles.Lose.Count == 0)
        {
            return;
        }

        var (number, path) = _movieFiles.Lose.ElementAt(Random.Shared.Next(_movieFiles.Lose.Count));
        PlayMovie(path, waitForStop: true);
        if (_movie.IsPlaying && LoseHeadlines.For(number, _texts) is { Count: > 0 } headlines
            && LoseHeadlines.Render(_device, ((IWindowHost)this).Graphics, headlines[Random.Shared.Next(headlines.Count)]) is { } overlay)
        {
            _movie.SetOverlay(overlay, _movie.Length - LoseHeadlines.Before(number));
        }
    }

    /// <summary><c>disabledtracks.txt</c> beside the settings file: the tracks the jukebox leaves out.</summary>
    private string DisabledTracksPath => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_configPath ?? GameConfig.FileName)) ?? ".", "disabledtracks.txt");

    /// <inheritdoc/>
    BackgroundMusic? IWindowHost.Music => _music;

    /// <inheritdoc/>
    int IWindowHost.TemperatureTheme => _temperatureTheme;

    /// <inheritdoc/>
    void IWindowHost.SetPlayMusic(bool on)
    {
        _music.PlayMusic = on;
        _config = _config with { PlayMusic = on };
        SaveConfig();
    }

    /// <inheritdoc/>
    void IWindowHost.SavePlaylist()
    {
        try
        {
            File.WriteAllLines(DisabledTracksPath, _music.DisabledTracks());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // updatePlaylistPreferences fails silently.
        }
    }

    /// <summary><c>UI:togglePlayMusic</c>: the background music on (a random track) or off, kept in the settings.</summary>
    private void ToggleMusic()
    {
        _config = _config with { PlayMusic = _music.TogglePlayMusic() };
        SaveConfig();
    }

    /// <summary>Opens or closes the network game's level list.</summary>
    private void ToggleLobby(NetworkSession network)
    {
        if (_windows.Windows.OfType<LobbyWindow>().FirstOrDefault() is { } open)
        {
            _windows.Close(open);
            return;
        }

        _windows.Open(new LobbyWindow(() => network.Levels, () => network.Level, () => network.IsHost, network.ChooseLevel));
    }

    /// <summary><c>GameUI:makeDebugRat</c>: a rat out of a hole in view, the view being the map's pixels shown.</summary>
    private void MakeDebugRat()
    {
        var topLeft = _camera.TopLeft(ViewSize);
        var size = _camera.WorldSize(ViewSize);
        _session?.Submit(new MakeDebugRatCommand(topLeft.X, topLeft.Y, topLeft.X + size.X, topLeft.Y + size.Y));
    }

    /// <summary>The new game dialog's name, kept in the settings (<c>UINewGame:saveToConfig</c>).</summary>
    private void SavePlayerName(string name)
    {
        _config = _config with { PlayerName = name };
        SaveConfig();
    }

    /// <summary>Writes the settings to <c>config.yaml</c>, saying so when it cannot.</summary>
    private void SaveConfig()
    {
        if (_configPath is null)
        {
            return;
        }

        try
        {
            _config.Save(_configPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNotice(UiText.Format("openwards.status.cannot_save_settings", "Cannot save the settings: %1%", e.Message));
        }
    }

    /// <summary>
    /// <c>GameUI:toggleAdviser</c> (Shift+A by default): the adviser falls silent or speaks again, and the setting is
    /// saved.
    /// </summary>
    private void ToggleAdviser()
    {
        _config = _config with { AdviserDisabled = !_config.AdviserDisabled };
        _adviser.IsEnabled = !_config.AdviserDisabled;
        if (!_adviser.IsEnabled)
        {
            _adviser.DismissAll();
        }

        if (_configPath is not null)
        {
            try
            {
                _config.Save(_configPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ShowNotice(UiText.Format("openwards.status.cannot_save_settings", "Cannot save the settings: %1%", e.Message));
                return;
            }
        }

        ShowNotice(_adviser.IsEnabled ? UiText.Get("openwards.status.adviser_back", "The adviser is back") : UiText.Get("openwards.status.adviser_quiet", "The adviser is quiet"));
    }

    /// <summary>The options, with the hotkey assignment dialog (<c>UIOptions:buttonHotkey</c>), which goes back to them.</summary>
    private OptionsWindow NewOptionsWindow() =>
        new(_config, ApplyConfig, InstalledLanguageNames(_install), () => _windows.Open(new HotkeyAssignWindow(_hotkeys, SaveHotkeys, () => _windows.Open(NewOptionsWindow()))), FontChoices, PreviewLanguage, OpenSettingsDialog,
            () => _windows.Open(new FolderSettingsWindow(_config, ApplyConfig, () => _windows.Open(NewOptionsWindow()), SaveGameStore.DefaultDirectory(),
                Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_configPath ?? GameConfig.FileName)) ?? ".", "Screenshots"), Restart)));

    /// <summary><c>App:restart</c> after a new game data folder: the game starts again with the same command line.</summary>
    private void Restart()
    {
        if (Environment.ProcessPath is not { } executable)
        {
            return;
        }

        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false };
            foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
            {
                start.ArgumentList.Add(argument);
            }

            System.Diagnostics.Process.Start(start)?.Dispose();
            ExitRequested = true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine("Could not restart: " + e.Message);
        }
    }

    /// <summary>
    /// <c>Audio:init</c>: the playlist, the game's XMI music or with <c>play_own_music</c> the player's own in
    /// <c>audio_music</c> (recorded or MIDI files), the tracks left out read back (<c>readPlaylistPreferences</c>).
    /// </summary>
    private BackgroundMusic CreateMusic(GameConfig config)
    {
        var tracks = config.PlayOwnMusic && config.AudioMusic.Length > 0
            ? ThemeHospitalInstall.OwnMusicTracks(config.AudioMusic, PcmSources.Extensions, BackgroundMusic.InstructionalExtensions)
            : _install.MusicTracks();
        var music = new BackgroundMusic(tracks, NewMidiPlayer(config), path => Rnc.DecompressIfNeeded(File.ReadAllBytes(path)), new Random(), new WaveformPlayer())
        {
            PlayMusic = config.PlayMusic,
        };
        music.SetVolume(config.MusicVolume);
        try
        {
            if (File.Exists(DisabledTracksPath))
            {
                music.ReadDisabledTracks(File.ReadAllLines(DisabledTracksPath));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // readPlaylistPreferences fails silently.
        }

        return music;
    }

    /// <summary>
    /// <c>Audio:initMidiPlayer</c>: the MIDI player on the output the settings name (<c>midi_api</c>, <c>midi_port</c>),
    /// or null when there is none.
    /// </summary>
    /// <remarks>
    /// With the default API the music plays on the software synthesiser when there is a SoundFont
    /// (<c>App:findSoundFont</c>), as CorsixTH's default is FluidSynth's; without one, on Windows, on its MIDI mapper.
    /// </remarks>
    private static MidiPlayer? NewMidiPlayer(GameConfig config)
    {
        IMidiOutput? output = null;
        if (config.MidiApi.Length == 0 && SoundFonts.Find(config.Soundfont, AppContext.BaseDirectory) is { } soundFont)
        {
            output = SoftSynthMidiOutput.Open(soundFont);
        }

        output ??= WinMmMidiOutput.Open(config.MidiPort);
        return output is null ? null : new MidiPlayer(output);
    }

    /// <summary>UIOptions's buttons: a settings dialog, whose Back opens the options again.</summary>
    private void OpenSettingsDialog(SettingsDialogKind kind) =>
        _windows.Open(new SettingsDialog(kind, _config, ApplyConfig, () => _windows.Open(NewOptionsWindow()), FontChoices, _music.HasMusic ? () => _windows.Open(new JukeboxWindow(_music.Playlist.Count)) : null, WinMmMidiOutput.Ports, DisplayModes.Available, ScreenCanvas.SystemDpiScale()));

    /// <summary>
    /// The system's fonts with every letter of the language's translation, for the options window's font choice, empty
    /// until they have been looked for (in the background, once); a language the game's font draws keeps that font
    /// unless one is chosen, the others have one found among the system's.
    /// </summary>
    private LanguageFontChoices? FontChoices(string language)
    {
        var translation = TranslationFiles.Value.Find(language);
        var key = translation?.EnglishName ?? language.Trim();
        if (!_fontChoices.TryGetValue(key, out var choices))
        {
            var fonts = _unicodeFonts;
            var sample = translation is null
                ? language
                : translation.NativeName + string.Concat(TranslationFiles.Value.Load(language, _ => null).Strings.Values);
            choices = Task.Run(() => fonts.Choices(sample));
            _fontChoices[key] = choices;
        }

        var found = choices.IsCompletedSuccessfully ? choices.Result : [];
        return new LanguageFontChoices(translation is { UsesGameFont: false } ? LanguageFontChoices.SystemFont : LanguageFontChoices.GameFont, found);
    }

    /// <summary><c>UIHotkeyAssign:buttonAccept</c>: the keys are used at once and saved to <c>hotkeys.txt</c>.</summary>
    private void SaveHotkeys(Hotkeys hotkeys)
    {
        _hotkeys = hotkeys;
        if (_configPath is null || Path.GetDirectoryName(Path.GetFullPath(_configPath)) is not { } folder)
        {
            return;
        }

        try
        {
            hotkeys.Save(Path.Combine(folder, Hotkeys.FileName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ShowNotice(UiText.Format("openwards.status.cannot_save_hotkeys", "Cannot save the hotkeys: %1%", e.Message));
        }
    }

    /// <summary>
    /// The hotkeys from <c>hotkeys.txt</c> beside the settings file (written with the defaults if missing); the defaults
    /// without a settings file or when the file can't be read.
    /// </summary>
    private static Hotkeys LoadHotkeys(string? configPath)
    {
        if (configPath is null || Path.GetDirectoryName(Path.GetFullPath(configPath)) is not { } folder)
        {
            return new Hotkeys();
        }

        try
        {
            return Hotkeys.Load(Path.Combine(folder, Hotkeys.FileName));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine($"Cannot read {Hotkeys.FileName}: {e.Message}; using the default keys.");
            return new Hotkeys();
        }
    }

    /// <summary>
    /// The new game dialog's Start (<c>UINewGame:startGame</c>): the original campaign at the difficulty, from its first
    /// level; Page Up/Down, F3 and the next level go through the campaign's levels from then on.
    /// </summary>
    private void StartCampaign(Difficulty difficulty, bool tutorial = false)
    {
        if (_session is not null and not LocalSession)
        {
            ShowNotice(UiText.Get("openwards.status.no_new_game_in_network", "A new game can only be started outside a network game"));
            return;
        }

        var levels = OriginalLevels(_install, difficulty);
        if (levels.Count == 0)
        {
            ShowNotice(UiText.Get("openwards.status.no_original_levels", "The original levels are not installed"));
            return;
        }

        _levels = levels;
        _levelList = false;
        _campaignMovies = CampaignMovies.Original;
        SelectLevel(0);

        // UINewGame:startGame: the board's movie to the first level.
        PlayAdvanceMovie(1);
        if (tutorial)
        {
            StartTutorial();
        }
    }

    /// <summary><c>UINewGame:startGame</c> with the tutorial ticked: <c>GameUI:startTutorial</c>.</summary>
    private void StartTutorial()
    {
        _tutorial = new Tutorial(_adviser, () => _texts, FinishTutorial);
        _tutorialSession = _session;
        Tutorial.Current = _tutorial;

        // World:onTick starts no opening watch while the tutorial runs.
        _session?.Submit(new HoldOpeningWatchCommand(true));
        if (TutorialView() is { } view)
        {
            _tutorial.Start(view);
        }
    }

    /// <summary>
    /// The tutorial's last chapter (its begin_callback): the information on what to do next (introduction texts 15 to 17,
    /// and the level's own off the first level).
    /// </summary>
    private void FinishTutorial()
    {
        var paragraphs = new List<string>();
        foreach (var level in new[] { 15, 16, 17 })
        {
            if (_texts?.IntroductionText(level) is { } text)
            {
                paragraphs.AddRange(text.Split("//", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        if (_levelIndex != 0 && _texts?.IntroductionText(1) is { } first)
        {
            paragraphs.AddRange(first.Split("//", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (paragraphs.Count == 0)
        {
            paragraphs.Add(UiText.Get("openwards.status.tutorial_done", "That's the basic mechanics of putting a hospital together. Build diagnosis and treatment rooms, hire staff for them, and keep an eye on your reputation."));
        }

        _windows.Open(new InformationWindow(paragraphs));

        // ...and the opening watch, from the start.
        _session?.Submit(new HoldOpeningWatchCommand(false));
    }

    /// <summary>What the tutorial follows this frame, or null without a game of the local player's.</summary>
    private TutorialView? TutorialView()
    {
        if (_session is not { Simulation: { } simulation } session || session.Player >= simulation.Hospitals.Count)
        {
            return null;
        }

        var player = session.Player;
        var owner = player + 1;
        var map = simulation.Map;
        bool Ours(TilePosition tile) => map.GetParcelOwner(map[tile.X, tile.Y].Parcel) == owner;
        var staff = simulation.Entities.OfType<Humanoid>().Where(h => h.Employer == player && h.Profile is not null && !h.IsFired).ToList();
        var build = _buildTool.TutorialState(simulation, player);
        bool? hiringValid = null;
        if (_hiring is { } hiring && _staffTile is { } hovered)
        {
            var index = simulation.AvailableStaff(hiring.Class).ToList().IndexOf(hiring.Profile);
            hiringValid = index >= 0 && StaffHiring.Check(simulation, hiring.Class, index, new TilePosition(hovered.X, hovered.Y), player) == StaffHireProblem.None;
        }

        return new TutorialView
        {
            CorridorWindowOpen = _windows.Windows.OfType<FurnishCorridorWindow>().Any(),
            ReceptionDeskValid = _furnishTool.Ghost is { ObjectType: ReceptionDesks.ObjectId } ghost ? ghost.Tint == Color.White : _furnishTool.IsActive && _furnishTool.PlacingId == ReceptionDesks.ObjectId ? false : null,
            ReceptionDesks = simulation.Entities.OfType<RoomObject>().Count(o => o.ObjectType == ReceptionDesks.ObjectId && Ours(o.Tile)),
            HireWindowOpen = _windows.Windows.OfType<HireWindow>().Any(),
            HireCategory = _windows.Windows.OfType<HireWindow>().FirstOrDefault()?.Category,
            HiringClass = _hiring?.Class,
            HiringValid = hiringValid == true,
            Receptionists = staff.Count(h => h.Profile!.Class == StaffClass.Receptionist),
            Doctors = staff.Count(h => h.Profile!.Class == StaffClass.Doctor),
            RoomListOpen = _roomList || _windows.Windows.Any(w => w is BuildRoomWindow),
            BuildStage = build.Stage,
            BuildRoomType = build.RoomType,
            Blueprint = build.Blueprint,
            PlacementValid = build.Valid,
            MissingObjects = build.Missing,
            ActiveGpOffices = simulation.Entities.OfType<Room>().Count(r => r is { RoomType: "gp", IsActive: true } && Ours(new TilePosition(r.Area.X, r.Area.Y))),
            InformationWindowOpen = _windows.Windows.OfType<InformationWindow>().Any(),
        };
    }

    /// <summary>
    /// <c>UIMainMenu</c>, when the game starts with nothing to play given: the first level waits paused behind it until
    /// a game is started or loaded.
    /// </summary>
    private void OpenMainMenu()
    {
        _session?.SetSpeed(GameSpeed.Pause);
        _mainMenu = new MainMenuWindow(MainMenuAction, typeof(MapViewScreen).Assembly.GetName().Version?.ToString(3) ?? "?");
        StartUpdateCheck();
        _windows.Open(_mainMenu);

        // CorsixTH opens a tip of the day beside the main menu; starting or loading a game replaces the whole UI, so the
        // tip goes with the menu.
        _tipOfTheDay = new TipOfTheDayWindow(_adviserRandom.Next(TipOfTheDayWindow.Tips.Count));
        _windows.Open(_tipOfTheDay);
    }

    private void CloseMainMenu()
    {
        if (_mainMenu is not null)
        {
            _windows.Close(_mainMenu);
            _mainMenu = null;
        }

        if (_tipOfTheDay is not null)
        {
            _windows.Close(_tipOfTheDay);
            _tipOfTheDay = null;
        }
    }

    /// <summary>The main menu's items (<c>UIMainMenu:button*</c>).</summary>
    /// <summary>
    /// The corridor object of ours a right click on <paramref name="tile"/> picks up: the one on that tile, or else a
    /// side object on the north wall of the tile south of it or the west wall of the tile east of it, whose sprite
    /// stands over this tile (CorsixTH hit-tests the sprites themselves).
    /// </summary>
    private RoomObject? GrabbableObjectAt(Simulation simulation, TilePosition tile)
    {
        RoomObject? At(int x, int y, Direction? facing)
        {
            if (!simulation.Map.Contains(x, y) || simulation.FindEntity(simulation.Map[x, y].ObjectId) is not RoomObject placed
                || (facing is { } side && placed.Orientation != side)
                || !CorridorObjects.CanPickUp(simulation, placed.Id, _session!.Player))
            {
                return null;
            }

            return placed;
        }

        return At(tile.X, tile.Y, null) ?? At(tile.X, tile.Y + 1, Direction.North) ?? At(tile.X + 1, tile.Y, Direction.West);
    }

    /// <summary>App:checkForUpdates, once a launch, in the background; null when not asked for.</summary>
    private Task<UpdateInfo?>? _updateCheck;

    /// <summary>
    /// Starts looking for a newer version on launch (<c>check_for_updates</c>), unless a script plays the game or the
    /// project has no releases yet (<see cref="UpdateCheck.ReleasesPublished"/>); the update window opens once it is found.
    /// </summary>
    private void StartUpdateCheck()
    {
        if (UpdateCheck.ReleasesPublished && _config.CheckForUpdates && InputScript.Current is null && _updateCheck is null)
        {
            _updateCheck = UpdateCheck.FindAsync(UpdateCheck.CurrentVersion());
        }
    }

    private void ShowUpdateFound()
    {
        if (_updateCheck is not { IsCompleted: true } check)
        {
            return;
        }

        _updateCheck = Task.FromResult<UpdateInfo?>(null);
        if (check.IsCompletedSuccessfully && check.Result is { } update && IsWindowHostReady)
        {
            _windows.Open(new UpdateWindow(update, OpenWebPage));
        }
    }

    /// <summary>Opens a web page in the system's browser (<c>UIUpdate:buttonDownload</c>).</summary>
    private static void OpenWebPage(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine("Could not open " + url + ": " + e.Message);
        }
    }

    private void MainMenuAction(string action)
    {
        var saves = _saves.Directory;
        switch (action)
        {
            case "newgame":
                _windows.Open(new NewGameWindow(AvailableDifficulties(_install), _config.PlayerName, (difficulty, tutorial) => StartCampaign(difficulty, tutorial), SavePlayerName));
                break;
            case "campaign":
                _windows.Open(new CustomCampaignWindow(CustomGames.FindCampaigns(GameConfig.UserFolder(_config.Campaigns, saves, "Campaigns")), StartCustomCampaign));
                break;
            case "scenario":
                _windows.Open(new CustomLevelWindow(CustomGames.FindLevels(GameConfig.UserFolder(_config.Levels, saves, "Levels")), _config.FreeBuildMode, StartCustomLevel));
                break;
            case "continue" when _saves.MostRecentSave() is { } latest:
                LoadGame(latest);
                break;
            case "continue":
                _windows.Open(new InformationWindow(["Error while loading game: There are no saved games."]));
                break;
            case "load":
                _windows.Open(new SaveLoadWindow(_saves, false, SaveGameTo, LoadGame, _install.FindDirectory("SAVE")));
                break;
            case "options":
                _windows.Open(NewOptionsWindow());
                break;
            case "mapeditor":
                StartMapEditor(MapEditor.Blank(), null);
                break;
            case "exit":
                _windows.Open(new ConfirmWindow(UiText.Get("openwards.status.confirm_exit", "You are about to quit from OpenWards. Are you sure this is what you want to do?"), () => ExitRequested = true));
                break;
        }
    }

    /// <summary><c>UICustomGame:buttonLoadLevel</c>: the level on its own, with free build if ticked.</summary>
    private void StartCustomLevel(CustomLevelEntry level, bool freeBuild)
    {
        _campaignMovies = CampaignMovies.None;
        _levels = [new LevelSource(Path.GetFileName(level.Path), () => freeBuild
            ? LevelMapLoader.LoadFile(level.Path, _install).WithFreeBuild()
            : LevelMapLoader.LoadFile(level.Path, _install))];
        _levelList = false;
        SelectLevel(0);
    }

    /// <summary><c>UICustomCampaign:startCampaign</c>: the campaign's levels from its first.</summary>
    private void StartCustomCampaign(CustomCampaignEntry campaign)
    {
        IReadOnlyList<string> paths;
        bool movie;
        try
        {
            var loaded = LevelMapLoader.LoadCampaign(campaign.Path);
            paths = loaded.LevelPaths;
            movie = loaded.Campaign.Movie;
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            _windows.Open(new InformationWindow([UiText.Format("openwards.status.cannot_start_campaign", "Cannot start the campaign: %1%", e.Message)]));
            return;
        }

        _levels = [.. paths.Select(path => new LevelSource(Path.GetFileName(path), () => LevelMapLoader.LoadFile(path, _install)))];
        _levelList = false;
        _campaignMovies = movie ? CampaignMovies.Custom : CampaignMovies.None;
        SelectLevel(0);

        // App:loadCampaign: the board's movie from where the campaign ends at the twelfth level.
        if (movie)
        {
            PlayAdvanceMovie(Math.Max(1, 12 - _levels.Count));
        }
    }

    /// <summary>
    /// <c>UI:showMapEditor</c>: the map is shown paused, with nothing on it but its entrance doors, and the editor's
    /// window and cursor take the mouse.
    /// </summary>
    private void StartMapEditor(MapEditor editor, string? path)
    {
        CloseMainMenu();
        _windows.CloseAll();
        foreach (var panel in _windows.Windows.OfType<BottomPanel>().ToList())
        {
            _windows.Close(panel);
        }

        _session?.Dispose();
        var simulation = new Simulation(editor.Map, seed: 1, BaseGameContent.Create(), LevelRules.Free);
        var local = new LocalSession(path is null ? UiText.Get("main_menu.map_edit", "Map Editor") : Path.GetFileName(path), simulation, _saves, autosaveFrequency: 0);
        local.SetSpeed(GameSpeed.Pause);
        _session = local;
        _error = null;
        _editorTool = new MapEditorTool(editor) { Changed = () => { } };
        _editorPath = path;
        PrepareMapIfReady();
        OpenMapEditorWindow();
    }

    private void OpenMapEditorWindow()
    {
        if (_editorTool is { } tool && !_windows.Windows.OfType<MapEditorWindow>().Any())
        {
            _windows.Open(new MapEditorWindow(tool, _blocks, MapEditorAction));
        }
    }

    /// <summary>The editor's mouse on the map; the parcels' overlay shows while parcels are being set.</summary>
    private void UpdateMapEditor(MapEditorTool tool, MouseState mouse)
    {
        var tile = _hoveredTile is { } point ? new TilePosition(point.X, point.Y) : (TilePosition?)null;
        tool.Update(
            tile,
            mouse.LeftButton == ButtonState.Pressed,
            mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released,
            mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed,
            mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released,
            mouse.RightButton == ButtonState.Released && _previousMouse.RightButton == ButtonState.Pressed);
        if (tool.Mode == EditorMode.Parcel)
        {
            _overlay = MapOverlay.Parcels;
        }
        else if (_overlay == MapOverlay.Parcels)
        {
            _overlay = MapOverlay.None;
        }
    }

    /// <summary>The entrance doors the edited map places on <paramref name="tile"/>, closed.</summary>
    private void DrawEditorObjects(SpriteBatch batch, TilePosition tile, int anchorX, int anchorY)
    {
        if (_editorTool is not { } tool)
        {
            return;
        }

        foreach (var placement in tool.Editor.Objects)
        {
            if (placement.Tile != tile || placement.ObjectType is not (MapEditor.EntranceLeftDoor or MapEditor.EntranceRightDoor))
            {
                continue;
            }

            var west = (placement.Direction & 1) != 0;
            var animation = (placement.ObjectType == MapEditor.EntranceRightDoor, west) switch
            {
                (true, false) => 308,
                (true, true) => 312,
                (false, false) => 316,
                _ => 318,
            };
            _animations.DrawFrame(batch, _animations.Animations.FirstFrame(animation), anchorX, anchorY, NoLayers, flipHorizontally: false);
        }
    }

    /// <summary>The editor window's own items: save and load maps in the levels folder, a new blank map, and quitting.</summary>
    private void MapEditorAction(string action)
    {
        if (_editorTool is not { } tool)
        {
            return;
        }

        var folder = GameConfig.UserFolder(_config.Levels, _saves.Directory, "Levels");
        switch (action)
        {
            case "save":
                var path = _editorPath ?? NewMapPath(folder);
                try
                {
                    tool.Editor.Save(path);
                    _editorPath = path;
                    ShowNotice(UiText.Format("openwards.status.map_saved", "Saved the map as %1%", path));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    _windows.Open(new InformationWindow([UiText.Format("openwards.status.cannot_save_map", "Cannot save the map: %1%", e.Message)]));
                }

                break;
            case "load":
                var maps = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.map").Order(StringComparer.OrdinalIgnoreCase).ToList() : [];
                _windows.Open(new MapListWindow(maps, LoadEditorMap));
                break;
            case "restart":
                _windows.Open(new ConfirmWindow(UiText.Get("openwards.status.confirm_new_map", "Start again with a blank map? Changes not saved are lost."), () => StartMapEditor(MapEditor.Blank(), null)));
                break;
            case "quit":
                _windows.Open(new ConfirmWindow(UiText.Get("openwards.status.confirm_leave_editor", "Leave the map editor? Changes not saved are lost."), QuitMapEditor));
                break;
        }
    }

    /// <summary>A file name in the levels folder that no map has yet.</summary>
    private static string NewMapPath(string folder)
    {
        for (var i = 1; ; i++)
        {
            var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"Map {i}.map"));
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }

    private void LoadEditorMap(string path)
    {
        try
        {
            StartMapEditor(MapEditor.Open(MapFile.Parse(File.ReadAllBytes(path))), path);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _windows.Open(new InformationWindow([UiText.Format("openwards.status.cannot_load_map", "Cannot load the map: %1%", e.Message)]));
        }
    }

    /// <summary>Back to the main menu, over the first level as when the game starts.</summary>
    private void QuitMapEditor()
    {
        _editorTool = null;
        _editorPath = null;
        _overlay = MapOverlay.None;
        _windows.CloseAll();
        _windows.Open(new BottomPanel(BottomPanelAction));
        SelectLevel(0);
        OpenMainMenu();
    }

    /// <summary>The options window's Apply: the new settings are saved to <c>config.yaml</c> and used at once.</summary>
    private void ApplyConfig(GameConfig config)
    {
        var midiChanged = config.MidiApi != _config.MidiApi || config.MidiPort != _config.MidiPort || config.Soundfont != _config.Soundfont;
        var playlistChanged = config.PlayOwnMusic != _config.PlayOwnMusic || config.AudioMusic != _config.AudioMusic;
        var savesChanged = !string.Equals(config.Savegames, _config.Savegames, StringComparison.Ordinal);
        if (config.Fullscreen != _config.Fullscreen)
        {
            // The graphics settings' fullscreen switch takes effect at once, as Alt+Enter does.
            _fullscreenToggleRequested = true;
        }

        if (config.Width != _config.Width || config.Height != _config.Height)
        {
            // The window size, too, takes effect at once (CorsixTH's setVideoMode), not after a restart.
            _windowSizeRequest = new Point(config.Width, config.Height);
        }

        _config = config;
        if (savesChanged)
        {
            // App:initSavegameDir: saves go to the new folder from now on.
            _saves = new SaveGameStore(SavesDirectory(config));
        }

        if (playlistChanged)
        {
            // The sound dialog's own music switch: a new playlist, its first track at random if music plays.
            _music.Dispose();
            _music = CreateMusic(config);
            _music.PlayRandom();
        }
        else if (midiChanged)
        {
            _music.SetPlayer(NewMidiPlayer(config), config.MusicVolume);
        }

        _buildTool.AverageContents = config.EnableAverageContents;
        OriginalBottomPanel.MachineMenuButton = config.MachineMenuButton;
        ScreenCanvas.Requested = config.UiScale;
        _adviser.IsEnabled = !config.AdviserDisabled;
        _scrolling = new MapScrolling(config.ScrollSettings());
        _sounds.Dispose();
        _sounds = SoundEffects.Load(_install, config, VoiceOf(config));
        _music.SetVolume(config.MusicVolume);
        if (config.PlayMusic != _music.PlayMusic)
        {
            // UIAudio's music switch: on plays a random track, off stops the music.
            _music.TogglePlayMusic();
        }
        if (!string.Equals(config.UnicodeFont, _unicodeFonts.Configured, StringComparison.Ordinal))
        {
            _text.UseTrueType(null);
            _blackText?.UseTrueType(null);
            _bottomPanel?.UseTrueType(null);
            _graphics.UseTrueType(null);
            _unicodeFonts.Dispose();
            _unicodeFonts = new UnicodeFonts(config.UnicodeFont, AppContext.BaseDirectory);
        }

        _texts = LoadTexts(_install, config);

        UiText.Current = _texts;
        ApplyLanguageFont();
        if (_configPath is not null)
        {
            try
            {
                config.Save(_configPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                ShowNotice(UiText.Format("openwards.status.cannot_save_settings", "Cannot save the settings: %1%", e.Message));
                return;
            }
        }

        ShowNotice(UiText.Get("openwards.status.settings_saved", "Settings saved"));
    }

    /// <summary>
    /// The options window's language and font tried out: the texts and the TrueType font are those of
    /// <paramref name="config"/>'s language at once, without saving; Apply or closing the window settles them.
    /// </summary>
    private void PreviewLanguage(GameConfig config)
    {
        _config = _config with { Language = config.Language, LanguageFonts = config.LanguageFonts };
        _texts = LoadTexts(_install, _config);
        UiText.Current = _texts;
        ApplyLanguageFont();
    }

    /// <summary>Whether the room edit panel's <paramref name="button"/> was pressed; it counts once.</summary>
    private bool TakeButton(BuildButton button)
    {
        if (_buildButton != button)
        {
            return false;
        }

        _buildButton = null;
        return true;
    }

    /// <summary>The bottom panel's buttons that are not windows: the room list and the furnish corridor window.</summary>
    private void BottomPanelAction(string action)
    {
        if (_buildTool.IsActive || _furnishTool.IsActive)
        {
            _buildReminderUntil = Environment.TickCount64 + NoticeMilliseconds;
            return;
        }

        switch (action)
        {
            case "rooms":
                ToggleRoomList();
                break;
            case "corridor":
                _windows.Open(new FurnishCorridorWindow(definition => _furnishTool.Start(definition)) { BuyMany = list => _furnishTool.Start(list) });
                break;
            case "edit":
                // CorsixTH's edit room button: the next click on a room of the hospital edits it.
                _pickingRoomToEdit = true;
                _windows.CloseAll();
                ShowNotice(UiText.Get("openwards.status.edit_room_hint", "Click a room to edit it; right click or Esc cancels"));
                break;
        }
    }

    /// <summary>
    /// Picking the room to edit: a click on an active room of the player's sends it to be edited (<c>Room:tryToEdit</c>)
    /// and opens it for furnishing; a right click gives up.
    /// </summary>
    private void PickRoomToEdit(Simulation simulation, bool clicked, MouseState mouse)
    {
        if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released)
        {
            _pickingRoomToEdit = false;
            return;
        }

        if (!clicked || _hoveredTile is not { } target || !simulation.Map.Contains(target.X, target.Y))
        {
            return;
        }

        var player = _session!.Player;
        if (simulation.FindEntity(simulation.Map[target.X, target.Y].RoomId) is Room { IsCrashed: true } wreck)
        {
            // GameUI:onMouseUp: a wrecked room can only be cleared away, and only with remove_destroyed_rooms, for a fee.
            if (_config.RemoveDestroyedRooms && RemoveDestroyedRoomCommand.RemovalCost(simulation, wreck, player) is { } cost)
            {
                _pickingRoomToEdit = false;
                var question = UiText.Format("confirmation.remove_destroyed_room", "Would you like to remove the room for $%d?", cost);
                _windows.Open(new ConfirmWindow(question, () => _session.Submit(new RemoveDestroyedRoomCommand(wreck.Id))));
            }

            return;
        }

        if (simulation.FindEntity(simulation.Map[target.X, target.Y].RoomId) is Room { IsActive: true } room
            && RoomBuilder.IsOwnedBy(simulation.Map, room, player)
            && simulation.Content.FindRoom(room.RoomType) is { } definition)
        {
            _pickingRoomToEdit = false;
            _session.Submit(new EditRoomCommand(room.Id));
            _buildTool.StartEditing(definition, room.Id, simulation.Tick);
        }
    }

    void IWindowHost.StartHiring(StaffClass staffClass, int index)
    {
        _hiring = (staffClass, Simulation!.AvailableStaff(staffClass)[index]);
        _windows.CloseAll();
        ShowNotice(UiText.Get("openwards.status.place_staff_hint", "Click in the hospital to place the new member of staff; right click or Esc cancels"));
    }

    /// <summary>
    /// <c>UIPlaceStaff</c>: the candidate on the cursor is hired onto the tile clicked if they may stand there; a right
    /// click (or Esc) puts them back on the list. A candidate gone from the list in the meantime is dropped.
    /// </summary>
    /// <returns>Whether a candidate is being placed, so the click was for that.</returns>
    private bool UpdateHiring(Simulation simulation, MouseState mouse)
    {
        if (_hiring is not { } hiring)
        {
            return false;
        }

        var index = simulation.AvailableStaff(hiring.Class).ToList().IndexOf(hiring.Profile);
        var click = StaffPlacing.Click(
            candidateGone: index < 0,
            left: mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released,
            right: mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released,
            paused: _session!.IsPaused);
        if (click == StaffPlacingClick.Cancel)
        {
            _hiring = null;
            return true;
        }

        if (click == StaffPlacingClick.Paused)
        {
            ShowNotice(UiText.Get("openwards.status.paused_place_staff", "The game is paused: unpause it to place the new member of staff"));
            _sounds.Play("wrong2.wav");
            return true;
        }

        if (click == StaffPlacingClick.Place && _staffTile is { } point)
        {
            var tile = new TilePosition(point.X, point.Y);
            var problem = StaffHiring.Check(simulation, hiring.Class, index, tile, _session!.Player);
            if (problem == StaffHireProblem.None)
            {
                AdviseOnPlacement(simulation, hiring.Profile, tile, 0);
                _session.Submit(new HireStaffCommand(hiring.Class, index, tile));
                _sounds.Play("plac_st2.wav"); // UIPlaceStaff:close
                _hiring = null;
            }
            else
            {
                ShowNotice(UiText.Format("openwards.status.cannot_place_staff", "Cannot place them there: %1%", problem));
                _sounds.Play("wrong2.wav");
            }
        }

        return true;
    }

    /// <summary>
    /// <c>UIPlaceStaff</c> for a member of staff picked up (the staff window's Pick up, or M): a left click puts them
    /// down on the tile under the cursor, a right click lets them go where they are.
    /// </summary>
    /// <returns>Whether someone is being carried, so the click was for that.</returns>
    private bool UpdateCarrying(Simulation simulation, MouseState mouse)
    {
        if (_pickedUpStaff is not { } carried)
        {
            return false;
        }

        var click = StaffPlacing.Click(
            candidateGone: simulation.FindEntity(carried) is not Humanoid { IsDespawning: false, IsFired: false },
            left: mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released,
            right: mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released,
            paused: _session!.IsPaused);
        if (click == StaffPlacingClick.Place && _staffTile is null)
        {
            return true;
        }

        PutDownCarried(simulation, carried, _staffTile is { } at ? new TilePosition(at.X, at.Y) : default, click);
        return true;
    }

    /// <summary>
    /// <c>UIPlaceStaff:onMouseUp</c> and <c>close</c> for the member of staff carried: put down on <paramref name="hand"/>
    /// if they may stand there (on their own tile, simply let go), else they stay on the cursor and the adviser says so;
    /// a cancel lets them go where they are. Nothing happens while paused.
    /// </summary>
    private void PutDownCarried(Simulation simulation, int carried, TilePosition hand, StaffPlacingClick click)
    {
        switch (click)
        {
            case StaffPlacingClick.Paused:
                ShowNotice(UiText.Get("openwards.status.paused_put_down_staff", "The game is paused: unpause it to put the member of staff down"));
                _sounds.Play("wrong2.wav");
                return;
            case StaffPlacingClick.Cancel:
                _pickedUpStaff = null;
                _sounds.Play("plac_st2.wav");
                return;
            case StaffPlacingClick.Place:
                break;
            default:
                return;
        }

        if (simulation.FindEntity(carried) is Humanoid held && held.Tile != hand)
        {
            var problem = StaffMoving.Check(simulation, carried, hand, _session!.Player);
            if (problem != StaffHireProblem.None)
            {
                // _A.placement_info.staff_cannot_place: they stay on the cursor.
                ShowNotice(UiText.Format("openwards.status.cannot_put_down_staff", "Cannot put the member of staff down there: %1%", problem));
                _sounds.Play("wrong2.wav");
                return;
            }

            AdviseOnPlacement(simulation, held.Profile, hand, carried);
            _session.Submit(new MoveStaffCommand(carried, hand));
        }

        _pickedUpStaff = null;
        _sounds.Play("plac_st2.wav");
    }

    private void ShowNotice(string text)
    {
        _notice = text;
        _noticeUntil = Environment.TickCount64 + NoticeMilliseconds;
    }

    private string StatusLine()
    {
        if (_editorTool is { } editor)
        {
            var name = _editorPath is null ? UiText.Get("openwards.status.new_map", "new map") : Path.GetFileName(_editorPath);
            var at = editor.Cursor;
            var tile = editor.Editor.Map[at.X, at.Y];
            return string.Create(CultureInfo.InvariantCulture, $"MAP EDITOR   {name}   tile ({at.X}, {at.Y})   floor {tile.Floor}   north {tile.NorthWall}   west {tile.WestWall}   parcel {tile.Parcel}   Esc: editor window");
        }

        if (_session is null)
        {
            return UiText.Get("openwards.status.no_game", "No game.");
        }

        var simulation = Simulation;
        var money = simulation is not null && _session.Player < simulation.Hospitals.Count
            ? UiText.Format(
                "openwards.status.money",
                "   %1%   cured %2%   died %3%   reputation %4%   value %5%",
                MoneyText.Format(simulation.Hospitals[_session.Player].Balance),
                simulation.Hospitals[_session.Player].Cured,
                simulation.Hospitals[_session.Player].Deaths,
                simulation.Hospitals[_session.Player].Reputation,
                MoneyText.Format(simulation.Hospitals[_session.Player].Value))
                + $"{LoanText(simulation.Hospitals[_session.Player])}{VipText(simulation.Hospitals[_session.Player])}{EmergencyText(simulation.Hospitals[_session.Player])}{EpidemicText(simulation, simulation.Hospitals[_session.Player])}{QuestionText(simulation, _session.Player)}{OutcomeText(simulation.Hospitals[_session.Player])}"
            : string.Empty;

        string EmergencyText(Hospital hospital) => hospital.Emergencies.Current switch
        {
            { IsAccepted: false } offer => UiText.Format("openwards.status.status_emergency_offer", "   EMERGENCY: %1% with %2%, %3% % to cure (E accept, Shift+E refuse)", offer.Victims, DiseaseName(offer.DiseaseId), offer.Percentage),
            { } emergency => UiText.Format("openwards.status.status_emergency", "   EMERGENCY: cured %1%, lost %2% of %3%, %4%/13 left", emergency.Cured, emergency.Killed, emergency.Victims, emergency.WatchTimer + 1),
            null => string.Empty,
        };

        string EpidemicText(Simulation simulation, Hospital hospital) => hospital.Epidemics.Current switch
        {
            { FaxExpires: > 0 } fax => UiText.Format("openwards.status.status_epidemic_offer", "   EPIDEMIC of %1%: declare for %2% (Shift+I) or cover up (I)", DiseaseName(fax.DiseaseId), MoneyText.Format(fax.DeclareFine)),
            { IsWatchRunning: true } epidemic => UiText.Format("openwards.status.status_epidemic", "   EPIDEMIC: %1% infected, %2%/13 left (J vaccinate)", Epidemics.CountInfectedPatients(simulation, epidemic), epidemic.WatchTimer + 1),
            { } => UiText.Get("openwards.status.status_inspector_coming", "   EPIDEMIC: the health inspector is coming"),
            null => string.Empty,
        };

        string QuestionText(Simulation simulation, int player)
        {
            if (FirstQuestion(simulation, player) is not { Behaviour: PatientBehaviour patient })
            {
                return string.Empty;
            }

            var count = simulation.Entities.Count(entity => entity is Humanoid { Behaviour: PatientBehaviour { Question: not PatientQuestion.None } } other && other.HospitalPlayer == player);
            return patient.Question == PatientQuestion.NoDiagnosisRoom
                ? UiText.Format("openwards.status.status_asking_diagnosis", "   %1% ASKING: no diagnosis room for %2% (%3% %) (K wait, Shift+K send home, Ctrl+K guess cure)", count, DiseaseName(patient.DiseaseId), patient.DiagnosisProgress / 10)
                : UiText.Format("openwards.status.status_asking_treatment", "   %1% ASKING: no room to treat %2% (K wait, Shift+K send home)", count, DiseaseName(patient.DiseaseId));
        }

        string DiseaseName(string diseaseId) => _texts?.DiseaseName(diseaseId) ?? diseaseId;

        string VipText(Hospital hospital) => hospital.Vips.IsInvitationPending ? UiText.Format("openwards.status.status_vip", "   VIP %1% asks to visit (V invite, Shift+V refuse)", _texts?.VipName(hospital.Vips.InvitedName) ?? hospital.Vips.InvitedName.ToString(CultureInfo.InvariantCulture)) : string.Empty;

        static string LoanText(Hospital hospital) => hospital.Loan > 0 ? UiText.Format("openwards.status.status_loan", "   loan %1%", MoneyText.Format(hospital.Loan)) : string.Empty;

        static string OutcomeText(Hospital hospital) => hospital.Outcome switch
        {
            EndOutcome.Won => UiText.Get("openwards.status.status_level_won", "   LEVEL WON"),
            EndOutcome.Lost => UiText.Format("openwards.status.status_level_lost", "   LEVEL LOST (%1%)", hospital.LostTo?.Criterion),
            _ => string.Empty,
        };

        var levels = _levels.Count > 0 ? UiText.Format("openwards.status.status_level_of", "   (%1% of %2%)", _levelIndex + 1, _levels.Count) : string.Empty;
        var people = Simulation?.Entities.OfType<Humanoid>().Count() ?? 0;
        var levelKeys = _levels.Count > 0 ? UiText.Get("openwards.status.status_levels_key", "   F3: levels") : string.Empty;
        var autosave = _session is LocalSession { AutosaveError: { } failed } ? UiText.Format("openwards.status.status_autosave_failed", "   AUTOSAVE FAILED: %1%", failed) : string.Empty;
        return $"{LevelNames.WardName(_session.Name)}{levels}{money}{autosave}   {_session.Status}   " + UiText.Format(
            "openwards.status.line",
            "%1% people%2%   overlay %3%   zoom x%4%   upscale x%5%%6%   O: overlay   U: upscale   Esc: quit",
            people,
            _wander ? UiText.Get("openwards.status.wandering", " wandering") : string.Empty,
            _overlay,
            _camera.Zoom.ToString("0.00", CultureInfo.InvariantCulture),
            _blocks.Scale,
            levelKeys);
    }

    /// <summary>
    /// The date and the game speed in a box in the top right corner, standing in for the date on the
    /// original's bottom panel until that panel exists.
    /// </summary>
    private void DrawDatePanel(SpriteBatch batch, Viewport viewport)
    {
        if (_session is null || Simulation is not { } simulation)
        {
            return;
        }

        var date = DateText(simulation.Date);
        if (_session.Player < simulation.Hospitals.Count && simulation.Hospitals[_session.Player] is { IsOpen: false, IsOpeningWatchHeld: false } hospital)
        {
            // Stands in for CorsixTH's opening watch; whole days rounded up.
            var days = (hospital.HoursUntilOpen + GameDate.HoursPerDay - 1) / GameDate.HoursPerDay;
            date += UiText.Format("openwards.status.status_opens_in", ", opens in %1% days (Shift+O: now)", days);
        }

        var speed = GameSpeeds.Name(_session.Speed);
        var width = Math.Max(_text.Measure(date).Width, _text.Measure(speed).Width) + 16;
        var box = new Rectangle(viewport.Width - width, 0, width, StatusHeight);
        batch.Draw(_pixel, box, DatePanelColor);
        _text.Draw(batch, date, new Vector2(box.X + 8, 4));
        _text.Draw(batch, speed, new Vector2(box.X + 8, 4 + _text.LineHeight + 2));
    }

    /// <summary>
    /// The list of levels (F3): every level the client can start, the current one marked; the one under the cursor is
    /// highlighted. Campaign levels get their number.
    /// </summary>
    private void DrawLevelList(SpriteBatch batch)
    {
        _levelListRows.Clear();
        if (!_levelList || _levels.Count == 0)
        {
            return;
        }

        var title = UiText.Get("openwards.status.choose_level", "Choose a level (click one; F3 or Esc closes)");
        var labels = _levels.Select(LevelLabel).ToList();
        var lineHeight = _text.LineHeight + 4;
        var width = labels.Append(title).Max(label => _text.Measure(label).Width) + 40;
        var box = new Rectangle(8, StatusHeight + 8, width, ((labels.Count + 1) * lineHeight) + 12);
        batch.Draw(_pixel, box, MoneyPanelColor);
        _text.Draw(batch, title, new Vector2(box.X + 12, box.Y + 6));
        var mouse = InputScript.Mouse();
        for (var i = 0; i < labels.Count; i++)
        {
            var row = new Rectangle(box.X + 4, box.Y + 6 + ((i + 1) * lineHeight) - 2, box.Width - 8, lineHeight);
            _levelListRows.Add((row, i));
            if (row.Contains(mouse.X, mouse.Y))
            {
                batch.Draw(_pixel, row, RoomListHover);
            }

            var current = i == _levelIndex;
            _text.Draw(batch, (current ? "> " : "  ") + labels[i], new Vector2(box.X + 12, row.Y + 2), tint: current ? LevelListCurrent : Color.White);
        }
    }

    /// <summary>What the level list shows for a level: the original's levels as wards with their number, others by file.</summary>
    private static string LevelLabel(LevelSource level) =>
        LevelNames.OriginalLevelNumber(level.Name) is { } number
            ? UiText.Format("openwards.status.ward_level", "Ward No. %1%   (%2%)", number, level.Name)
            : level.Name;

    /// <summary>
    /// Starts building a room: as in the original, only once discovered and with its whole cost in the bank; otherwise
    /// says why not.
    /// </summary>
    private void TryStartRoom(Simulation simulation, RoomDefinition roomType)
    {
        if (_session is null)
        {
            return;
        }

        if (RoomListProblem(simulation, roomType) is { } problem)
        {
            ShowNotice($"{UiText.Room(roomType)}: {problem}");
            _sounds.Play("Wrong2.wav");
            return;
        }

        _buildTool.Start(roomType);
    }

    /// <summary>What the adviser says when a member of staff is put down: no lecture chair, a desk of their own, or the wrong room.</summary>
    private void AdviseOnPlacement(Simulation simulation, StaffProfile? profile, TilePosition tile, int movingId)
    {
        if (profile is null)
        {
            return;
        }

        if (AdviserChecks.LacksLectureChair(simulation, profile, tile, movingId))
        {
            _adviser.Say(AdviserChecks.NotEnoughLectureChairs.Text(_texts), Environment.TickCount64);
        }
        else if (AdviserChecks.StaffPlacement(simulation, profile, tile, movingId, Random.Shared.Next) is var (advice, argument))
        {
            _adviser.Say(argument is null ? advice.Text(_texts) : advice.Text(_texts, argument), Environment.TickCount64);
        }
    }

    /// <summary>Why the player cannot start a room now, or null when they can.</summary>
    private string? RoomListProblem(Simulation simulation, RoomDefinition room)
    {
        var player = _session!.Player;
        if (!simulation.Rules.IsRoomAvailable(room))
        {
            return UiText.Get("openwards.status.not_on_level", "not on this level");
        }

        if (!RoomPurchases.CanBuild(simulation, room, player))
        {
            return UiText.Get("openwards.status.not_researched", "not researched yet");
        }

        return RoomPurchases.CanAfford(simulation, room, player) ? null : UiText.Get("openwards.build_tool.not_enough_money", "not enough money");
    }

    /// <summary>
    /// The build room window (B, the panel's build button): the original's look where its graphics are there, else our
    /// list of every room.
    /// </summary>
    private void ToggleRoomList()
    {
        var window = new BuildRoomWindow(room =>
        {
            if (Simulation is { } simulation)
            {
                TryStartRoom(simulation, room);
            }
        });
        if (window.IsFullscreen(this))
        {
            _windows.Toggle(window);
        }
        else
        {
            _roomList = !_roomList;
        }
    }
    /// <summary>
    /// The list of rooms (B), a stand-in for CorsixTH's build room window (<c>dialogs/build_room.lua</c>): every room type
    /// with what it costs in all, or why it cannot be built; the one under the cursor is highlighted.
    /// </summary>
    private void DrawRoomList(SpriteBatch batch)
    {
        _roomListRows.Clear();
        if (!_roomList || _session is not { Simulation: { } simulation } session || session.Player >= simulation.Hospitals.Count)
        {
            return;
        }

        var hospital = simulation.Hospitals[session.Player];
        var rooms = simulation.Content.Rooms.OrderBy(room => UiText.Room(room), StringComparer.CurrentCultureIgnoreCase).ToList();
        var rows = rooms.Select(room => (Room: room, Cost: MoneyText.Format(RoomPurchases.BuildCost(simulation, hospital, room)), Problem: RoomListProblem(simulation, room))).ToList();
        var title = UiText.Get("openwards.status.build_room_list", "Build a room (click one; B or Esc closes)");
        var lineHeight = _text.LineHeight + 4;
        var nameWidth = rows.Max(row => _text.Measure(UiText.Room(row.Room)).Width);
        var costWidth = rows.Max(row => _text.Measure(row.Cost).Width);
        var problemWidth = rows.Max(row => row.Problem is null ? 0 : _text.Measure(row.Problem).Width);
        var width = Math.Max(_text.Measure(title).Width, nameWidth + costWidth + problemWidth + 48) + 24;
        var box = new Rectangle(8, StatusHeight + 8, width, ((rows.Count + 1) * lineHeight) + 12);
        batch.Draw(_pixel, box, MoneyPanelColor);
        _text.Draw(batch, title, new Vector2(box.X + 12, box.Y + 6));
        var mouse = InputScript.Mouse();
        for (var i = 0; i < rows.Count; i++)
        {
            var (room, cost, problem) = rows[i];
            var row = new Rectangle(box.X + 4, box.Y + 6 + ((i + 1) * lineHeight) - 2, box.Width - 8, lineHeight);
            _roomListRows.Add((row, room));
            if (row.Contains(mouse.X, mouse.Y))
            {
                batch.Draw(_pixel, row, problem is null ? RoomListHover : RoomListHoverBlocked);
            }

            if (room.Id == "gp" && Tutorial.Lit("room:gp"))
            {
                batch.Draw(_pixel, row, RoomListHover);
            }

            var tint = problem is null ? Color.White : RoomListBlocked;
            var y = row.Y + 2;
            _text.Draw(batch, UiText.Room(room), new Vector2(box.X + 12, y), tint: tint);
            var costRight = box.X + 12 + nameWidth + 24 + costWidth;
            _text.Draw(batch, cost, new Vector2(costRight - _text.Measure(cost).Width, y), tint: tint);
            if (problem is not null)
            {
                _text.Draw(batch, problem, new Vector2(costRight + 24, y), tint: tint);
            }
        }
    }

    /// <summary>The date as the original's bottom panel shows it (day and month), with the year.</summary>
    private static string DateText(GameDate date)
    {
        string[] months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
        return UiText.Format("openwards.status.date", "%1% %2% year %3%", date.Day, UiText.Current?.String(6, date.Month) ?? months[date.Month - 1], date.Year);
    }

    /// <summary>The close-up views windows asked for (their view circles), by the entity they follow.</summary>
    private readonly Dictionary<int, RenderTarget2D> _closeUps = [];
    private readonly HashSet<int> _closeUpsWanted = [];

    /// <summary>The size of a close-up view in world pixels (UIStaff's 75 by 75 of the map).</summary>
    public const int CloseUpSize = 75;

    void IWindowHost.DrawAnimation(SpriteBatch batch, int animation, Point at)
    {
        var set = _animations.Animations;
        var first = set.FirstFrame(animation);
        var length = Math.Max(1, set.Length(first));
        _animations.DrawFrame(batch, set.Advance(first, (int)(Environment.TickCount64 * 18 / 1000 % length)), at.X, at.Y, NoAdviserLayers, flipHorizontally: false);
    }

    void IWindowHost.DrawIdle(SpriteBatch batch, Humanoid humanoid, Point at) => _humanoids.DrawIdle(batch, humanoid, at.X, at.Y);

    TilePosition? IWindowHost.TileAtScreen(Point screen)
    {
        var tile = Camera.WorldToTile(_camera.ScreenToWorld(new Point(screen.X, screen.Y - StatusHeight), ViewSize));
        return Map is { } map && map.Contains(tile.X, tile.Y) ? new TilePosition(tile.X, tile.Y) : null;
    }

    Texture2D? IWindowHost.CloseUp(int entityId)
    {
        _closeUpsWanted.Add(entityId);
        return _closeUps.GetValueOrDefault(entityId);
    }

    /// <summary>
    /// UIStaff:draw's <c>map:draw</c> into the view circle: for each entity a window asked about last frame, the map
    /// round it, 75 by 75 world pixels with the entity in the middle and a little low, as the frame shows it.
    /// </summary>
    private void RenderCloseUps(SpriteBatch batch, LevelMap map)
    {
        foreach (var stale in _closeUps.Keys.Where(id => !_closeUpsWanted.Contains(id)).ToList())
        {
            _closeUps[stale].Dispose();
            _closeUps.Remove(stale);
        }

        if (_closeUpsWanted.Count == 0 || Simulation is not { } simulation)
        {
            return;
        }

        var scale = _blocks.Scale;
        var humanoidFrames = _humanoids.FrameDrawn;
        var objectFrames = _objects.FrameDrawn;
        _humanoids.FrameDrawn = null;
        _objects.FrameDrawn = null;
        foreach (var id in _closeUpsWanted)
        {
            if (simulation.FindEntity(id) is not Humanoid humanoid)
            {
                continue;
            }

            var anchor = _humanoids.DrawnAt(id) ?? new Point(32 * (humanoid.Tile.X - humanoid.Tile.Y), 16 * (humanoid.Tile.X + humanoid.Tile.Y));
            var topLeft = new Point(anchor.X - (CloseUpSize / 2), anchor.Y - 61);
            if (!_closeUps.TryGetValue(id, out var target) || target.Width != CloseUpSize * scale)
            {
                target?.Dispose();
                target = new RenderTarget2D(_device, CloseUpSize * scale, CloseUpSize * scale);
                _closeUps[id] = target;
            }

            _device.SetRenderTarget(target);
            _device.Clear(Background);
            var toTarget = Matrix.CreateTranslation(-topLeft.X, -topLeft.Y, 0) * Matrix.CreateScale(scale, scale, 1);
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, transformMatrix: toTarget);
            _renderer.Draw(batch, map, new Rectangle(topLeft, new Point(CloseUpSize, CloseUpSize)), (tile, anchorX, anchorY) => DrawEntities(batch, tile, anchorX, anchorY),
                tile => _entitiesByTile.TryGetValue(tile, out var here) && here.Any(e => e is Humanoid or Rat),
                (tile, anchorX, anchorY) => DrawEarlyEntities(batch, tile, anchorX, anchorY),
                tile => _earlyByTile.TryGetValue(tile, out var early) && early.Count > 0);
            batch.End();
        }

        _humanoids.FrameDrawn = humanoidFrames;
        _objects.FrameDrawn = objectFrames;
        _closeUpsWanted.Clear();
    }
    /// <summary>GameUI: the bottom panel's dynamic info of what is under the cursor, or null.</summary>
    private string? HoverInfo() => HoverPanelInfo()?.ToString() is { Length: > 0 } text ? text : null;

    /// <summary>The dynamic info of what is under the cursor as the original's panel draws it: lines and a bar.</summary>
    private DynamicInfoLines? HoverPanelInfo()
    {
        if (_cursorOverWindow || Simulation is not { } simulation || _hoveredTile is not { } point)
        {
            return null;
        }

        var tile = new TilePosition(point.X, point.Y);
        if (simulation.FindEntity(_hoveredHumanoid) is Humanoid hovered && DynamicInfo.Panel(simulation, hovered, _texts) is { } info)
        {
            return info;
        }

        return MachineUnderCursor(simulation, tile) is { } machine
            ? new DynamicInfoLines(DynamicInfo.OfMachine(simulation, machine, _texts).Split("   ", StringSplitOptions.RemoveEmptyEntries))
            : null;
    }

    /// <summary>
    /// The kinds of the faxes waiting (UIMessage's types), in the order the fax window lists them: the VIP's invitation
    /// is a personality, results are reports, a patient's diagnosis is information.
    /// </summary>
    private List<string> PanelFaxes()
    {
        if (Simulation is not { } simulation || _session is null || _session.Player >= simulation.Hospitals.Count)
        {
            return [];
        }

        return FaxWindow.Pending(simulation, _session.Player, ((IWindowHost)this).IsWinLetterPending).Select(key => key[..key.IndexOf(':', StringComparison.Ordinal)] switch
        {
            "vip" => "personality",
            "vipreport" or "emergencyreport" or "epidemicreport" or "winletter" => "report",
            "diseasereport" => "disease",
            "emergency" => "emergency",
            "epidemic" => "epidemy",
            _ => "information",
        }).Concat(RaiseRequests().Select(_ => "strike")).ToList();
    }

    /// <summary>
    /// The player's staff asking for a raise whose icon stands on the bottom panel (PlayerHospital:makeRaiseRequest's
    /// "strike" message), the longest waiting first: not those whose request window is open or who have been answered.
    /// </summary>
    private List<Humanoid> RaiseRequests()
    {
        if (Simulation is not { } simulation || _session is null || _session.Player >= simulation.Hospitals.Count)
        {
            return [];
        }

        var player = _session.Player;
        var asking = simulation.Entities.OfType<Humanoid>()
            .Where(h => h is { Profile: not null, IsFired: false, RaiseRequest: > 0 } && h.Employer == player)
            .ToList();
        _raisesAnswered.RemoveWhere(id => !asking.Any(h => h.Id == id));
        return asking
            .Where(h => !_raisesAnswered.Contains(h.Id) && !_windows.Windows.Any(w => w is StaffRiseWindow rise && rise.StaffId == h.Id))
            .OrderBy(h => h.QuittingIn)
            .ThenBy(h => h.Id)
            .ToList();
    }

    /// <summary>
    /// World:mustPauseWindowAdd and mustPauseWindowRemoved: while a window that must pause the game is open the game is
    /// paused; when the last one goes the game goes on, unless the player had paused it already.
    /// </summary>
    private void PauseForWindows()
    {
        // A network game's speed is everyone's: a window one player opens does not pause it.
        if (_session is not { CanChangeSpeed: true, IsShared: false } session)
        {
            return;
        }

        var mustPause = _windows.Windows.Any(w => w.MustPause);
        if (mustPause && !session.IsPaused)
        {
            session.SetSpeed(GameSpeed.Pause);
            _pausedForWindow = true;
        }
        else if (!mustPause && _pausedForWindow)
        {
            _pausedForWindow = false;
            if (session.IsPaused)
            {
                session.ResumeSpeed();
            }
        }
    }

    /// <summary>A button of the original bottom panel: the dialog it opens or closes, or the tool it starts.</summary>
    private void BottomPanelButton(string key)
    {
        if (key.StartsWith("faxdismiss:", StringComparison.Ordinal))
        {
            // UIMessage:dismissMessage: a fax with only one choice is taken by a right click on its icon; Wrong2.wav otherwise.
            var index = int.Parse(key["faxdismiss:".Length..], CultureInfo.InvariantCulture);
            var messages = new FaxWindow().Messages(this);
            if (index < messages.Count && messages[index].Choices is [{ Enabled: true } only])
            {
                only.Choose();
            }
            else
            {
                _sounds.Play("wrong2.wav");
            }

            return;
        }

        if (key.StartsWith("fax:", StringComparison.Ordinal))
        {
            // UIMessage:openMessage: a raise request's icon opens its window (UIStaffRise) and goes; the others open the faxes.
            var index = int.Parse(key["fax:".Length..], CultureInfo.InvariantCulture);
            var faxCount = PanelFaxes().Count - RaiseRequests().Count;
            if (index >= faxCount)
            {
                if (RaiseRequests().ElementAtOrDefault(index - faxCount) is { } asking)
                {
                    _windows.Open(new StaffRiseWindow(asking.Id, asking.RaiseRequest));
                }

                return;
            }

            key = "fax";
        }

        if (key == "fax")
        {
            ToggleFax();
            return;
        }

        // research and edit room have no click sound (setSound()).
        if (key is not ("edit" or "research"))
        {
            _sounds.Play(WindowLayer.DefaultButtonSound);
        }

        UiWindow? dialog = key switch
        {
            "hire" => new HireWindow(),
            "staff" => new StaffManagementWindow(),
            "town" => new TownMapWindow(),
            "casebook" => new CasebookWindow(),
            "research" => new ResearchWindow(),
            "status" => new ProgressReportWindow(),
            "charts" => new GraphWindow(),
            "policy" => new PolicyWindow(),
            "fax" => new FaxWindow(),
            "machines" => new MachineMenuWindow(),
            _ => null,
        };
        if (key is "bank" or "bankstats")
        {
            ToggleBank(key == "bankstats");
        }
        else if (dialog is null)
        {
            BottomPanelAction(key);
        }
        else
        {
            _windows.Toggle(dialog);
        }
    }

    /// <summary><c>UIMessage:openMessage</c>: the faxes open with fax_in.wav, or close again.</summary>
    private void ToggleFax()
    {
        if (!_windows.Windows.Any(w => w is FaxWindow))
        {
            _sounds.Play("fax_in.wav");
        }

        _windows.Toggle(new FaxWindow());
    }

    /// <summary>The kinds of dialog the original bottom panel's toggle buttons open, so they show pressed while open.</summary>
    private bool PanelDialogOpen(string key) => key switch
    {
        "bank" => _windows.Windows.Any(w => w is BankManagerWindow or BankStatementWindow),
        "hire" => _windows.Windows.Any(w => w is HireWindow),
        "staff" => _windows.Windows.Any(w => w is StaffManagementWindow),
        "town" => _windows.Windows.Any(w => w is TownMapWindow),
        "casebook" => _windows.Windows.Any(w => w is CasebookWindow),
        "research" => _windows.Windows.Any(w => w is ResearchWindow),
        "status" => _windows.Windows.Any(w => w is ProgressReportWindow),
        "charts" => _windows.Windows.Any(w => w is GraphWindow),
        "policy" => _windows.Windows.Any(w => w is PolicyWindow),
        "fax" => _windows.Windows.Any(w => w is FaxWindow),
        "machines" => _windows.Windows.Any(w => w is MachineMenuWindow),
        "rooms" => _roomList || _buildTool.IsActive || _windows.Windows.Any(w => w is BuildRoomWindow),
        "corridor" => _furnishTool.IsActive || _windows.Windows.Any(w => w is FurnishCorridorWindow),
        "edit" => _pickingRoomToEdit,
        _ => false,
    };

    private void DrawBottomPanel(SpriteBatch batch, Viewport viewport, Point mouse)
    {
        if (_bottomPanel is null || Simulation is not { } simulation || _session is null || _session.Player >= simulation.Hospitals.Count)
        {
            return;
        }

        var hospital = simulation.Hospitals[_session.Player];
        var month = _texts?.String(6, simulation.Date.Month) ?? CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(simulation.Date.Month);
        var state = new BottomPanelState(
            hospital.Balance,
            hospital.Reputation,
            string.Create(CultureInfo.InvariantCulture, $"{simulation.Date.Day} {month}"),
            HoverPanelInfo(),
            _session.IsPaused ? _texts?.String(3, 22) ?? "Pause" : null,
            PanelFaxes(),
            PanelDialogOpen,
            Tutorial.Lit,
            [.. FaxWindow.Pending(simulation, _session.Player, ((IWindowHost)this).IsWinLetterPending), .. RaiseRequests().Select(staff => string.Create(CultureInfo.InvariantCulture, $"strike:{staff.Id}"))]);
        _bottomPanel.Draw(batch, viewport, state, mouse, InputScript.Mouse());
    }

    /// <summary>
    /// Without the status bar, what still has to be read at the top left: an error, the build tool's line, or a notice
    /// such as "Quick saved", each on a dark strip as wide as its text.
    /// </summary>
    private void DrawBriefLine(SpriteBatch batch, bool building)
    {
        var line = _error
            ?? (building ? TileLine() : null)
            ?? (_notice is not null && Environment.TickCount64 < _noticeUntil ? _notice : null);
        if (line is null)
        {
            return;
        }

        var box = new Rectangle(0, 0, _text.Measure(line).Width + 16, _text.LineHeight + 8);
        batch.Draw(_pixel, box, building ? BuildModeColor : Color.Black * 0.75f);
        _text.Draw(batch, line, new Vector2(8, 4));
    }

    private string TileLine()
    {
        if (_editorTool is not null)
        {
            return UiText.Get("openwards.status.editor_keys", "Pan: arrows WASD middle drag   Wheel: zoom   Left drag: paint, clear or paste   Right drag: copy an area");
        }

        if (_buildTool.IsActive)
        {
            var reminder = Environment.TickCount64 < _buildReminderUntil ? UiText.Get("openwards.status.finish_room_first", " (finish or cancel the room to use other keys)") : string.Empty;
            return UiText.Format("openwards.status.build_mode", "BUILD MODE%1%   %2%", reminder, _buildTool.Status(Simulation));
        }

        if (_furnishTool.IsActive)
        {
            return UiText.Format("openwards.status.build_mode_furnish", "BUILD MODE   %1%", _furnishTool.Status(Simulation));
        }

        if (_notice is not null && Environment.TickCount64 < _noticeUntil)
        {
            return _notice;
        }

        if (_bottomPanel is null && HoverInfo() is { } info)
        {
            return info;
        }


        if (_hoveredTile is not { } position || Map is not { } map)
        {
            var pause = _session?.CanChangeSpeed == true ? UiText.Get("openwards.status.speed_keys", "   Space P: pause   1-6: speed   Z: hold to speed up") : string.Empty;
            return UiText.Format("openwards.status.keys", "Pan: arrows WASD right drag   Wheel: zoom   B: build a room   F: buy reception desk   H: spawn   G: spawn 10   Ctrl+1-4: hire   Click: select or walk   R: wander%1%", pause);
        }

        var tile = map[position.X, position.Y];
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Tile {position.X} {position.Y}   floor {tile.Floor}   north {tile.NorthWall}   west {tile.WestWall}   parcel {tile.Parcel}{(tile.RoomId != 0 ? $"   room {tile.RoomId}" : string.Empty)}   {DescribeFlags(tile)}{(_selected is { } id ? $"   selected {id}" : string.Empty)}{StaffText(position)}");
    }

    /// <summary>
    /// The name, profession and description of a member of staff on the tile, standing in for the staff window and
    /// the bottom panel's information until they exist.
    /// </summary>
    private string StaffText(Point position)
    {
        if (_texts is null || Simulation is not { } simulation)
        {
            return string.Empty;
        }

        var staff = simulation.Entities.OfType<Humanoid>().FirstOrDefault(humanoid => humanoid.Profile is not null && humanoid.Tile.X == position.X && humanoid.Tile.Y == position.Y);
        return staff?.Profile is { } profile
            ? $"   {_texts.Staff.FullName(profile)}, {_texts.Staff.Profession(profile.Class)}: {_texts.Staff.Description(profile)}"
            : string.Empty;
    }

    /// <summary>CorsixTH's language files beside the game (Languages/*.yaml), read once.</summary>
    private static readonly Lazy<CorsixLanguages> TranslationFiles = new(() => CorsixLanguages.Load(Path.Combine(AppContext.BaseDirectory, "Languages")));

    /// <summary>The announcer's speech in the settings' language (Languages/Voice/&lt;language&gt;), if it has its own.</summary>
    private static LanguageVoice VoiceOf(GameConfig config) =>
        LanguageVoice.Load(TranslationFiles.Value.VoiceDirectory(Path.Combine(AppContext.BaseDirectory, "Languages"), config.Language));

    /// <summary>
    /// The languages to choose from, as the settings take them: CorsixTH's translations, by English name, that the
    /// game's font can draw or a Unicode font can (<c>Graphics:hasLanguageFont</c>; the Russian original's own font,
    /// "mik", is not read, so Russian takes the Unicode font too), then any installed original language none of them
    /// names.
    /// </summary>
    private List<string> InstalledLanguageNames(ThemeHospitalInstall install)
    {
        var translations = TranslationFiles.Value;
        List<string> names = [.. translations.Languages.Where(CanDraw).Select(language => language.EnglishName)];
        foreach (var original in install.InstalledLanguages().Order().Select(GameConfig.OriginalLanguageName).OfType<string>())
        {
            if (translations.Find(original) is not { } translation || !CanDraw(translation))
            {
                names.Add(original);
            }
        }

        return names;
    }

    /// <summary>Whether the language can be shown: with the game's font, or with a Unicode font that has its name's characters.</summary>
    private bool CanDraw(CorsixLanguage language) => language.UsesGameFont || _unicodeFonts.CanDraw(language.NativeName, _config.LanguageFont(language.Names));

    /// <summary>
    /// Draws text in the settings' language with the game's font, or with the Unicode font found for the language when
    /// its translation asks for one (<c>App:initLanguage</c>, <c>Graphics:onChangeLanguage</c>); a language the game's
    /// font draws can have a TrueType font chosen for it in the options instead. The status bar and the windows below
    /// it follow the font's height.
    /// </summary>
    private void ApplyLanguageFont()
    {
        TrueTypeFace? face = null;
        var minimumSize = 8;
        var translation = TranslationFiles.Value.Find(_config.Language);
        var chosen = _config.LanguageFont(translation?.Names ?? [_config.Language]) is not null;
        if (translation is null || translation.UsesGameFont)
        {
            face = _unicodeFonts.Chosen(_config.LanguageFont(translation?.Names ?? [_config.Language]));
        }
        else
        {
            var sample = translation.NativeName + string.Concat(_texts?.Language?.Strings.Values ?? []);
            face = _unicodeFonts.Find(sample, _config.LanguageFont(translation.Names))?.Face;

            // Ideograms fill the em square and need more pixels than letters to be read; 14 is CorsixTH's smallest.
            minimumSize = translation.NativeName.Any(character => character >= '⺀') ? 14 : 8;
        }

        _text.UseTrueType(face, minimumSize, chosen);
        _blackText?.UseTrueType(face, minimumSize, chosen);
        _bottomPanel?.UseTrueType(face, minimumSize, chosen);
        _graphics.UseTrueType(face, minimumSize, chosen);
        _windows.Origin = new Point(8, StatusHeight + 8);
    }

    /// <summary>
    /// The texts in the settings' language: the original's strings in that language if installed (or in the one its
    /// CorsixTH translation inherits), else in English, else in the first installed language, with the translation laid
    /// over them when the game's font or a Unicode font can draw it.
    /// </summary>
    private OriginalTexts? LoadTexts(ThemeHospitalInstall install, GameConfig config)
    {
        var languages = install.InstalledLanguages();
        if (languages.Count == 0)
        {
            return null;
        }

        var translations = TranslationFiles.Value;
        var translation = translations.Find(config.Language) is { } found && CanDraw(found) ? found : null;
        var wanted = config.OriginalLanguageNumber() ?? (translation is null ? null : translations.OriginalStringsNumber(translation.EnglishName));
        var language = wanted is { } number && languages.Contains(number) ? number
            : languages.Contains(StringTable.English) ? StringTable.English
            : languages[0];
        try
        {
            var strings = install.LoadStrings(language);

            // CorsixTH's own translation over the original's strings: Inherit("original_strings", n) takes LANG-n.DAT
            // where it is installed (the one already loaded is reused).
            var overlay = translation is null
                ? null
                : translations.Load(translation.EnglishName, n => n == language ? strings : languages.Contains(n) ? install.LoadStrings(n) : null);
            return new OriginalTexts(strings, overlay);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static string DescribeFlags(MapTile tile)
    {
        var parts = new List<string>();
        if (tile.Has(TileFlags.Passable))
        {
            parts.Add("passable");
        }

        if (tile.Has(TileFlags.Hospital))
        {
            parts.Add("hospital");
        }

        if (tile.Has(TileFlags.Buildable))
        {
            parts.Add("buildable");
        }

        return string.Join(' ', parts);
    }

    /// <summary>The outline of a tile's floor diamond, two pixels wide across and one down.</summary>
    private static Texture2D CreateCellOutline(GraphicsDevice device)
    {
        const int width = 64;
        const int height = 32;
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var distance = (MathF.Abs(x + 0.5f - 32f) / 32f) + (MathF.Abs(y + 0.5f - 16f) / 16f);
                pixels[(y * width) + x] = distance <= 1f && distance > 1f - (2f / 32f) ? Color.White : Color.Transparent;
            }
        }

        var texture = new Texture2D(device, width, height);
        texture.SetData(pixels);
        return texture;
    }

    private static Texture2D CreateDiamond(GraphicsDevice device)
    {
        const int width = 64;
        const int height = 32;
        var pixels = new Color[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inside = (MathF.Abs(x + 0.5f - 32f) / 32f) + (MathF.Abs(y + 0.5f - 16f) / 16f) <= 1f;
                pixels[(y * width) + x] = inside ? Color.White : Color.Transparent;
            }
        }

        var texture = new Texture2D(device, width, height);
        texture.SetData(pixels);
        return texture;
    }
}

/// <summary>Tile information drawn over the map.</summary>
internal enum MapOverlay
{
    None,

    /// <summary>The debug menu's positions: each tile's coordinates (<c>overlay("positions")</c>).</summary>
    Positions,
    Passable,
    Hospital,
    Parcels,
    Temperature,
}
